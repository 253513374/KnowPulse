using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.Organization;
using Shouldly;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class Mvp0AppServiceIntegrationTests : KnowPulseEntityFrameworkCoreTestBase
{
    private static readonly Guid CurrentUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private readonly IDepartmentAppService _departmentAppService;
    private readonly IPositionAppService _positionAppService;
    private readonly IJobLevelAppService _jobLevelAppService;
    private readonly IKnowledgeCategoryAppService _categoryAppService;
    private readonly IKnowledgeTagAppService _tagAppService;
    private readonly IKnowledgeItemAppService _itemAppService;
    private readonly IRepository<UserOrganizationAssignment, Guid> _assignmentRepository;
    private readonly IIdentityRoleRepository _roleRepository;

    public Mvp0AppServiceIntegrationTests()
    {
        _departmentAppService = GetRequiredService<IDepartmentAppService>();
        _positionAppService = GetRequiredService<IPositionAppService>();
        _jobLevelAppService = GetRequiredService<IJobLevelAppService>();
        _categoryAppService = GetRequiredService<IKnowledgeCategoryAppService>();
        _tagAppService = GetRequiredService<IKnowledgeTagAppService>();
        _itemAppService = GetRequiredService<IKnowledgeItemAppService>();
        _assignmentRepository = GetRequiredService<IRepository<UserOrganizationAssignment, Guid>>();
        _roleRepository = GetRequiredService<IIdentityRoleRepository>();
    }

    [Fact]
    public async Task Organization_AppServices_Should_Create_And_List_Mvp0_Data()
    {
        var suffix = NewSuffix();

        var department = await _departmentAppService.CreateAsync(new CreateUpdateDepartmentDto
        {
            Code = $"D-{suffix}",
            Name = $"Department {suffix}",
            SecurityLevel = 2
        });

        var position = await _positionAppService.CreateAsync(new CreateUpdatePositionDto
        {
            DepartmentId = department.Id,
            Code = $"P-{suffix}",
            Name = $"Position {suffix}",
            SecurityLevel = 2
        });

        var jobLevel = await _jobLevelAppService.CreateAsync(new CreateUpdateJobLevelDto
        {
            Code = $"L-{suffix}",
            Name = $"Level {suffix}",
            SortOrder = 1,
            SecurityLevel = 2
        });

        var departments = await _departmentAppService.GetListAsync(new PagedAndSortedResultRequestDto
        {
            MaxResultCount = 100
        });
        var positions = await _positionAppService.GetListAsync(new PagedAndSortedResultRequestDto
        {
            MaxResultCount = 100
        });
        var jobLevels = await _jobLevelAppService.GetListAsync(new PagedAndSortedResultRequestDto
        {
            MaxResultCount = 100
        });

        departments.Items.ShouldContain(x => x.Id == department.Id);
        positions.Items.ShouldContain(x => x.Id == position.Id && x.DepartmentId == department.Id);
        jobLevels.Items.ShouldContain(x => x.Id == jobLevel.Id);
    }

    [Fact]
    public async Task Knowledge_AppServices_Should_Manage_Versions_And_Permission_Scopes()
    {
        var suffix = NewSuffix();
        var allowedDepartment = await _departmentAppService.CreateAsync(new CreateUpdateDepartmentDto
        {
            Code = $"KD-{suffix}",
            Name = $"Knowledge Department {suffix}",
            SecurityLevel = 5
        });
        var blockedDepartment = await _departmentAppService.CreateAsync(new CreateUpdateDepartmentDto
        {
            Code = $"BD-{suffix}",
            Name = $"Blocked Department {suffix}",
            SecurityLevel = 5
        });

        await WithUnitOfWorkAsync(async () =>
        {
            await _assignmentRepository.InsertAsync(
                new UserOrganizationAssignment(
                    Guid.NewGuid(),
                    null,
                    CurrentUserId,
                    allowedDepartment.Id,
                    securityLevel: 5,
                    isPrimary: true),
                autoSave: true);
        });

        var category = await _categoryAppService.CreateAsync(new CreateUpdateKnowledgeCategoryDto
        {
            Code = $"KC-{suffix}",
            Name = $"Category {suffix}"
        });
        var tag = await _tagAppService.CreateAsync(new CreateUpdateKnowledgeTagDto
        {
            Name = $"Tag {suffix}"
        });

        var item = await CreateLegacyKnowledgeAsync(new CreateKnowledgeItemDto
        {
            CategoryId = category.Id,
            Title = $"Knowledge {suffix}",
            Summary = "MVP-0 integration test item"
        });
        var visibleVersion = await _itemAppService.CreateVersionAsync(item.Id, new CreateKnowledgeVersionDto
        {
            Title = $"Version {suffix}",
            ContentText = "Version content"
        });

        var published = await _itemAppService.PublishVersionAsync(item.Id, visibleVersion.Id);
        var refreshedItem = await _itemAppService.GetAsync(item.Id);

        published.Status.ShouldBe(KnowledgeVersionStatus.Published);
        published.IsCurrentPublished.ShouldBeTrue();
        refreshedItem.CurrentPublishedVersionId.ShouldBe(visibleVersion.Id);

        var scope = await _itemAppService.CreatePermissionScopeAsync(item.Id, new CreateKnowledgePermissionScopeDto
        {
            ScopeType = KnowledgePermissionScopeType.Department,
            TargetId = allowedDepartment.Id,
            MinimumSecurityLevel = 3
        });

        var visibleList = await _itemAppService.GetListAsync(new PagedAndSortedResultRequestDto
        {
            MaxResultCount = 100
        });
        visibleList.Items.ShouldContain(x => x.Id == item.Id);

        var hiddenItem = await CreateLegacyKnowledgeAsync(new CreateKnowledgeItemDto
        {
            CategoryId = category.Id,
            Title = $"Hidden Knowledge {suffix}",
            Summary = "Should not be visible to current user"
        });
        var hiddenVersion = await _itemAppService.CreateVersionAsync(hiddenItem.Id, new CreateKnowledgeVersionDto
        {
            Title = $"Hidden Version {suffix}",
            ContentText = "Hidden content"
        });
        var hiddenBytes = Encoding.UTF8.GetBytes("hidden attachment content");
        var hiddenAttachment = await _itemAppService.UploadAttachmentAsync(
            hiddenItem.Id,
            hiddenVersion.Id,
            new RemoteStreamContent(new MemoryStream(hiddenBytes), "hidden.txt", "text/plain", hiddenBytes.Length));

        await _itemAppService.CreatePermissionScopeAsync(hiddenItem.Id, new CreateKnowledgePermissionScopeDto
        {
            ScopeType = KnowledgePermissionScopeType.Department,
            TargetId = blockedDepartment.Id
        });

        var filteredList = await _itemAppService.GetListAsync(new PagedAndSortedResultRequestDto
        {
            MaxResultCount = 100
        });

        filteredList.Items.ShouldContain(x => x.Id == item.Id);
        filteredList.Items.ShouldNotContain(x => x.Id == hiddenItem.Id);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.GetAsync(hiddenItem.Id));
        await Should.ThrowAsync<AbpAuthorizationException>(
            () => _itemAppService.DownloadAttachmentAsync(hiddenItem.Id, hiddenVersion.Id, hiddenAttachment.Id));

        var scopes = await _itemAppService.GetPermissionScopesAsync(item.Id);
        scopes.Items.ShouldContain(x => x.Id == scope.Id);

        var bytes = Encoding.UTF8.GetBytes("MVP-0 attachment content");
        var attachment = await _itemAppService.UploadAttachmentAsync(
            item.Id,
            visibleVersion.Id,
            new RemoteStreamContent(new MemoryStream(bytes), "mvp0.txt", "text/plain", bytes.Length));

        var attachments = await _itemAppService.GetAttachmentsAsync(item.Id, visibleVersion.Id);
        attachments.Items.ShouldContain(x => x.Id == attachment.Id && x.FileName == "mvp0.txt");

        var downloaded = await _itemAppService.DownloadAttachmentAsync(item.Id, visibleVersion.Id, attachment.Id);
        using (var reader = new StreamReader(downloaded.GetStream(), Encoding.UTF8))
        {
            var text = await reader.ReadToEndAsync();
            text.ShouldBe("MVP-0 attachment content");
        }

        await _itemAppService.DeleteAttachmentAsync(item.Id, visibleVersion.Id, attachment.Id);
        attachments = await _itemAppService.GetAttachmentsAsync(item.Id, visibleVersion.Id);
        attachments.Items.ShouldNotContain(x => x.Id == attachment.Id);

        var withdrawn = await _itemAppService.WithdrawVersionAsync(item.Id, visibleVersion.Id);
        withdrawn.Status.ShouldBe(KnowledgeVersionStatus.Withdrawn);
        withdrawn.IsCurrentPublished.ShouldBeFalse();

        var archived = await _itemAppService.ArchiveVersionAsync(item.Id, visibleVersion.Id);
        archived.Status.ShouldBe(KnowledgeVersionStatus.Archived);

        var tags = await _tagAppService.GetListAsync(new PagedAndSortedResultRequestDto
        {
            MaxResultCount = 100
        });
        tags.Items.ShouldContain(x => x.Id == tag.Id);
    }

    [Fact]
    public async Task Knowledge_AppServices_Should_Apply_Default_Position_Role_User_And_Security_Scopes()
    {
        var suffix = NewSuffix();
        var department = await _departmentAppService.CreateAsync(new CreateUpdateDepartmentDto
        {
            Code = $"SD-{suffix}",
            Name = $"Scoped Department {suffix}",
            SecurityLevel = 3
        });
        var position = await _positionAppService.CreateAsync(new CreateUpdatePositionDto
        {
            DepartmentId = department.Id,
            Code = $"SP-{suffix}",
            Name = $"Scoped Position {suffix}",
            SecurityLevel = 3
        });

        await WithUnitOfWorkAsync(async () =>
        {
            await _assignmentRepository.InsertAsync(
                new UserOrganizationAssignment(
                    Guid.NewGuid(),
                    null,
                    CurrentUserId,
                    department.Id,
                    position.Id,
                    securityLevel: 3,
                    isPrimary: true),
                autoSave: true);
        });

        var category = await _categoryAppService.CreateAsync(new CreateUpdateKnowledgeCategoryDto
        {
            Code = $"SC-{suffix}",
            Name = $"Scoped Category {suffix}"
        });
        var adminRole = (await _roleRepository.GetListAsync(new[] { "admin" })).Single(x => x.Name == "admin");

        var defaultVisibleItem = await CreateItemAsync(category.Id, $"Default Visible {suffix}");
        var positionVisibleItem = await CreateItemAsync(category.Id, $"Position Visible {suffix}");
        await _itemAppService.CreatePermissionScopeAsync(positionVisibleItem.Id, new CreateKnowledgePermissionScopeDto
        {
            ScopeType = KnowledgePermissionScopeType.Position,
            TargetId = position.Id,
            MinimumSecurityLevel = 2
        });

        var roleVisibleItem = await CreateItemAsync(category.Id, $"Role Visible {suffix}");
        await _itemAppService.CreatePermissionScopeAsync(roleVisibleItem.Id, new CreateKnowledgePermissionScopeDto
        {
            ScopeType = KnowledgePermissionScopeType.Role,
            TargetId = adminRole.Id
        });

        var userVisibleItem = await CreateItemAsync(category.Id, $"User Visible {suffix}");
        await _itemAppService.CreatePermissionScopeAsync(userVisibleItem.Id, new CreateKnowledgePermissionScopeDto
        {
            ScopeType = KnowledgePermissionScopeType.User,
            TargetId = CurrentUserId
        });

        var wrongUserHiddenItem = await CreateItemAsync(category.Id, $"Wrong User Hidden {suffix}");
        await _itemAppService.CreatePermissionScopeAsync(wrongUserHiddenItem.Id, new CreateKnowledgePermissionScopeDto
        {
            ScopeType = KnowledgePermissionScopeType.User,
            TargetId = Guid.NewGuid()
        });

        var highSecurityHiddenItem = await CreateItemAsync(category.Id, $"High Security Hidden {suffix}");
        await _itemAppService.CreatePermissionScopeAsync(highSecurityHiddenItem.Id, new CreateKnowledgePermissionScopeDto
        {
            ScopeType = KnowledgePermissionScopeType.All,
            MinimumSecurityLevel = 999
        });

        var visibleList = await _itemAppService.GetListAsync(new PagedAndSortedResultRequestDto
        {
            MaxResultCount = 100
        });

        visibleList.Items.ShouldContain(x => x.Id == defaultVisibleItem.Id);
        visibleList.Items.ShouldContain(x => x.Id == positionVisibleItem.Id);
        visibleList.Items.ShouldContain(x => x.Id == roleVisibleItem.Id);
        visibleList.Items.ShouldContain(x => x.Id == userVisibleItem.Id);
        visibleList.Items.ShouldNotContain(x => x.Id == wrongUserHiddenItem.Id);
        visibleList.Items.ShouldNotContain(x => x.Id == highSecurityHiddenItem.Id);

        await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.GetAsync(wrongUserHiddenItem.Id));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.GetAsync(highSecurityHiddenItem.Id));
    }

    private Task<KnowledgeItemDto> CreateItemAsync(Guid categoryId, string title)
    {
        return CreateLegacyKnowledgeAsync(new CreateKnowledgeItemDto
        {
            CategoryId = categoryId,
            Title = title,
            Summary = "MVP-0 scoped integration test item"
        });
    }

    private static string NewSuffix()
    {
        return Guid.NewGuid().ToString("N")[..8];
    }
}
