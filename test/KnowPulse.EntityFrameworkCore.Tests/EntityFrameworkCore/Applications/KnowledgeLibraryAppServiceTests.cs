using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.Permissions;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class KnowledgeLibraryAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IKnowledgeLibraryAppService _service;
    private readonly IRepository<KnowledgeLibrary, Guid> _libraries;
    private readonly IRepository<KnowledgeItem, Guid> _items;
    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public KnowledgeLibraryAppServiceTests()
    {
        _service = GetRequiredService<IKnowledgeLibraryAppService>();
        _libraries = GetRequiredService<IRepository<KnowledgeLibrary, Guid>>();
        _items = GetRequiredService<IRepository<KnowledgeItem, Guid>>();
        _principalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
    }

    [Fact]
    public async Task Repeated_Initialization_And_Recovery_Should_Keep_One_Library()
    {
        var ownerId = Guid.NewGuid();
        using var user = ChangeUser(ownerId);
        var first = await _service.GetMyLibraryAsync();
        var second = await _service.GetMyLibraryAsync();

        first.Id.ShouldBe(ownerId);
        first.OwnerUserId.ShouldBe(ownerId);
        second.Id.ShouldBe(first.Id);

        await WithUnitOfWorkAsync(() => _libraries.DeleteAsync(ownerId, autoSave: true));
        var restored = await _service.GetMyLibraryAsync();
        restored.Id.ShouldBe(first.Id);
        (await WithUnitOfWorkAsync(() => _libraries.GetAsync(ownerId))).IsDeleted.ShouldBeFalse();
        (await WithUnitOfWorkAsync(() => _libraries.CountAsync(x => x.OwnerUserId == ownerId))).ShouldBe(1);
    }

    [Fact]
    public async Task My_Items_Should_Search_And_Page_Only_The_Authenticated_Owners_Library()
    {
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        using (ChangeUser(firstUserId))
        {
            var first = await _service.CreateItemAsync(new CreateKnowledgeItemDto { Title = "数学教案 一" });
            first.LibraryId.ShouldBe(firstUserId);
            first.OwnerUserId.ShouldBe(firstUserId);
            await _service.CreateItemAsync(new CreateKnowledgeItemDto { Title = "数学教案 二", LibraryId = firstUserId });
            await _service.CreateItemAsync(new CreateKnowledgeItemDto { Title = "其他资料", Summary = "英语反思" });
        }

        using (ChangeUser(secondUserId))
        {
            await _service.CreateItemAsync(new CreateKnowledgeItemDto { Title = "数学教案 他人私有" });
            var result = await _service.GetMyItemsAsync(new GetPersonalKnowledgeItemListInput { Keyword = "数学" });
            result.TotalCount.ShouldBe(1);
            result.Items.Single().OwnerUserId.ShouldBe(secondUserId);
        }

        using (ChangeUser(firstUserId))
        {
            var page = await _service.GetMyItemsAsync(new GetPersonalKnowledgeItemListInput
            {
                Keyword = "数学",
                SkipCount = 1,
                MaxResultCount = 1
            });
            page.TotalCount.ShouldBe(2);
            page.Items.Count.ShouldBe(1);
            page.Items.Single().OwnerUserId.ShouldBe(firstUserId);

            var summarySearch = await _service.GetMyItemsAsync(new GetPersonalKnowledgeItemListInput { Keyword = "英语" });
            summarySearch.Items.Single().Title.ShouldBe("其他资料");
        }
    }

    [Fact]
    public async Task Personal_Create_Should_Reject_Another_Library_Without_Creating_An_Item()
    {
        var ownerId = Guid.NewGuid();
        using var user = ChangeUser(ownerId);

        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CreateItemAsync(new CreateKnowledgeItemDto
        {
            Title = "Must not be created",
            LibraryId = Guid.NewGuid()
        }));

        (await _service.GetMyItemsAsync(new GetPersonalKnowledgeItemListInput())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Existing_Default_Library_Should_Not_Be_Reused_Or_Recreated_In_Another_Tenant()
    {
        var ownerId = Guid.NewGuid();
        using var user = ChangeUser(ownerId);
        await _service.GetMyLibraryAsync();

        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetMyLibraryAsync());
        }

        (await WithUnitOfWorkAsync(() => _libraries.CountAsync(x => x.OwnerUserId == ownerId))).ShouldBe(1);
    }

    [Fact]
    public async Task Tenant_Library_And_Items_Should_Use_Current_Tenant_And_Account()
    {
        var ownerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        using var user = ChangeUser(ownerId);
        using var tenant = _currentTenant.Change(tenantId);

        var library = await _service.GetMyLibraryAsync();
        var item = await _service.CreateItemAsync(new CreateKnowledgeItemDto { Title = "Tenant personal note" });

        (await WithUnitOfWorkAsync(() => _libraries.GetAsync(library.Id))).TenantId.ShouldBe(tenantId);
        var storedItem = await WithUnitOfWorkAsync(() => _items.GetAsync(item.Id));
        storedItem.TenantId.ShouldBe(tenantId);
        storedItem.OwnerUserId.ShouldBe(ownerId);
    }

    [Fact]
    public async Task Insert_Conflict_Should_Read_The_Winner_In_A_New_Unit_Of_Work()
    {
        var ownerId = Guid.NewGuid();
        using var user = ChangeUser(ownerId);
        var winner = await _service.GetMyLibraryAsync();
        var reads = 0;
        var readUnitOfWorks = new List<Guid>();
        var competingRepository = Substitute.For<IRepository<KnowledgeLibrary, Guid>>();
        competingRepository.FindAsync(ownerId, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                readUnitOfWorks.Add(_unitOfWorkManager.Current!.Id);
                if (++reads == 1)
                {
                    // 模拟另一请求在首次查询之后提交；接下来的真实 SQLite 插入会触发唯一键冲突。
                    return null;
                }

                return await _libraries.FindAsync(ownerId);
            });
        competingRepository.InsertAsync(Arg.Any<KnowledgeLibrary>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => _libraries.InsertAsync(call.Arg<KnowledgeLibrary>(), autoSave: true));
        var competingService = CreateService(competingRepository);

        var recovered = await competingService.GetMyLibraryAsync();

        recovered.Id.ShouldBe(winner.Id);
        reads.ShouldBe(2);
        readUnitOfWorks[0].ShouldNotBe(readUnitOfWorks[1]);
        (await WithUnitOfWorkAsync(() => _libraries.CountAsync(x => x.OwnerUserId == ownerId))).ShouldBe(1);
    }

    [Fact]
    public async Task Initialization_Should_Not_Hide_A_Failure_Without_An_Existing_Library()
    {
        var ownerId = Guid.NewGuid();
        using var user = ChangeUser(ownerId);
        var repository = Substitute.For<IRepository<KnowledgeLibrary, Guid>>();
        repository.FindAsync(ownerId, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<KnowledgeLibrary?>(null));
        var failure = new InvalidOperationException("Storage unavailable");
        repository.InsertAsync(Arg.Any<KnowledgeLibrary>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<KnowledgeLibrary>(failure));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => CreateService(repository).GetMyLibraryAsync());
        thrown.ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task Personal_Endpoints_Should_Reject_Anonymous_Users()
    {
        using var anonymous = _principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity()));

        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetMyLibraryAsync());
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetMyItemsAsync(new GetPersonalKnowledgeItemListInput()));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CreateItemAsync(new CreateKnowledgeItemDto { Title = "Denied" }));
    }

    [Fact]
    public async Task Personal_Endpoints_Should_Explicitly_Check_The_Personal_Permission()
    {
        var ownerId = Guid.NewGuid();
        using var user = ChangeUser(ownerId);
        var permissionChecker = Substitute.For<IPermissionChecker>();
        permissionChecker.IsGrantedAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary).Returns(false);
        var lazyProvider = Substitute.For<IAbpLazyServiceProvider>();
        lazyProvider.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>());
        lazyProvider.LazyGetRequiredService<IPermissionChecker>().Returns(permissionChecker);
        var service = CreateService(_libraries);
        service.LazyServiceProvider = lazyProvider;

        await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetMyLibraryAsync());
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetMyItemsAsync(new GetPersonalKnowledgeItemListInput()));
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.CreateItemAsync(new CreateKnowledgeItemDto { Title = "Denied" }));
        (await WithUnitOfWorkAsync(() => _libraries.CountAsync(x => x.OwnerUserId == ownerId))).ShouldBe(0);
    }

    private KnowledgeLibraryAppService CreateService(IRepository<KnowledgeLibrary, Guid> repository)
    {
        return new KnowledgeLibraryAppService(
            repository,
            _items,
            GetRequiredService<IRepository<KnowledgeCategory, Guid>>(),
            _unitOfWorkManager,
            GetRequiredService<IDataFilter>())
        {
            LazyServiceProvider = GetRequiredService<IAbpLazyServiceProvider>()
        };
    }

    private IDisposable ChangeUser(Guid userId)
    {
        return _principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
            new Claim(AbpClaimTypes.UserName, $"personal-{userId:N}")
        }, "Test")));
    }
}
