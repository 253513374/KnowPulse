using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class KnowledgeSchoolSharingAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IKnowledgeSchoolSharingAppService _service;
    private readonly IKnowledgeItemAppService _itemService;
    private readonly IRepository<SchoolKnowledgeShare, Guid> _shares;
    private readonly IRepository<KnowledgeItem, Guid> _items;
    private readonly IRepository<KnowledgeVersion, Guid> _versions;
    private readonly IRepository<SchoolMembership, Guid> _memberships;
    private readonly IRepository<KnowledgeLibrary, Guid> _libraries;

    public KnowledgeSchoolSharingAppServiceTests()
    {
        _service = GetRequiredService<IKnowledgeSchoolSharingAppService>();
        _itemService = GetRequiredService<IKnowledgeItemAppService>();
        _shares = GetRequiredService<IRepository<SchoolKnowledgeShare, Guid>>();
        _items = GetRequiredService<IRepository<KnowledgeItem, Guid>>();
        _versions = GetRequiredService<IRepository<KnowledgeVersion, Guid>>();
        _memberships = GetRequiredService<IRepository<SchoolMembership, Guid>>();
        _libraries = GetRequiredService<IRepository<KnowledgeLibrary, Guid>>();
    }

    [Fact]
    public async Task Sharing_V1_Should_Not_Expose_Private_V2_Or_Later_Item_Metadata()
    {
        var fixture = await CreateFixtureAsync();
        SchoolKnowledgeShareDto share;
        KnowledgeVersionDto privateVersion;
        using (ChangeUser(fixture.Owner))
        {
            share = await _service.ShareVersionAsync(Input(fixture));
            await _itemService.UpdateAsync(fixture.Item.Id, new UpdateKnowledgeItemDto
            {
                Title = "PRIVATE-V2-TITLE", Summary = "PRIVATE-V2-SUMMARY"
            });
            privateVersion = await _itemService.CreateVersionAsync(fixture.Item.Id, new CreateKnowledgeVersionDto
            {
                Title = "PRIVATE-V2-TITLE", ContentText = "PRIVATE-V2-CONTENT"
            });
            await _itemService.PublishVersionAsync(fixture.Item.Id, privateVersion.Id);
        }
        using (ChangeUser(fixture.Reader))
        {
            var page = await _service.GetSchoolSharesAsync(fixture.School.Id, new GetSchoolKnowledgeShareListInput
            {
                Keyword = "共享", MaxResultCount = 1
            });
            page.TotalCount.ShouldBe(1);
            page.Items.Single().Title.ShouldBe("共享V1标题");
            page.Items.Single().Summary.ShouldBe("共享V1摘要");
            var detail = await _service.GetDetailAsync(share.Id);
            detail.Share.KnowledgeVersionId.ShouldBe(fixture.Version.Id);
            detail.VersionNumber.ShouldBe(1);
            detail.ContentText.ShouldBe("共享V1正文");
            detail.IndexStatus.ShouldBe(KnowledgeIndexJobStatus.Pending);
            (await _service.GetSchoolSharesAsync(fixture.School.Id,
                new GetSchoolKnowledgeShareListInput { Keyword = "PRIVATE-V2" })).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemService.GetAsync(fixture.Item.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemService.GetVersionsAsync(fixture.Item.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemService.DownloadAttachmentAsync(
                fixture.Item.Id, privateVersion.Id, fixture.Attachment.Id));
        }
    }

    [Fact]
    public async Task Default_Uses_Should_Allow_Reading_But_Require_Explicit_Download()
    {
        var fixture = await CreateFixtureAsync();
        SchoolKnowledgeShareDto share;
        using (ChangeUser(fixture.Owner)) share = await _service.ShareVersionAsync(Input(fixture));
        share.Uses.ShouldBe(SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve);
        using (ChangeUser(fixture.Reader))
        {
            (await _service.GetDetailAsync(share.Id)).Attachments.Single().Id.ShouldBe(fixture.Attachment.Id);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.DownloadAttachmentAsync(share.Id, fixture.Attachment.Id));
        }
    }

    [Fact]
    public async Task Explicit_Download_Should_Use_Exact_Attachment_And_Revoke_Should_Stop_New_Use()
    {
        var fixture = await CreateFixtureAsync();
        SchoolKnowledgeShareDto share;
        using (ChangeUser(fixture.Owner))
            share = await _service.ShareVersionAsync(Input(fixture, SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Download));
        using (ChangeUser(fixture.Reader))
        {
            using var file = await _service.DownloadAttachmentAsync(share.Id, fixture.Attachment.Id);
            using var memory = new MemoryStream();
            await file.GetStream().CopyToAsync(memory);
            memory.ToArray().ShouldBe(new byte[] { 1, 2, 3 });
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.DownloadAttachmentAsync(share.Id, fixture.OtherAttachment.Id));
        }
        using (ChangeUser(fixture.Owner)) await _service.RevokeAsync(share.Id);
        using (ChangeUser(fixture.Reader))
        {
            (await _service.GetSchoolSharesAsync(fixture.School.Id, new GetSchoolKnowledgeShareListInput())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetAsync(share.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetDetailAsync(share.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.DownloadAttachmentAsync(share.Id, fixture.Attachment.Id));
        }
        using (ChangeUser(fixture.Owner))
        {
            (await _service.GetAsync(share.Id)).IsRevoked.ShouldBeTrue();
            (await _itemService.GetAsync(fixture.Item.Id)).Id.ShouldBe(fixture.Item.Id);
            (await _service.GetMySharesAsync(new GetSchoolKnowledgeShareListInput())).TotalCount.ShouldBe(1);
        }
    }

    [Theory]
    [InlineData(SchoolMemberRole.Student, 0)]
    [InlineData(SchoolMemberRole.Manager, 0)]
    [InlineData(SchoolMemberRole.Teacher, 1)]
    [InlineData(SchoolMemberRole.Teacher, -1)]
    public async Task Roles_And_Noncurrent_Teacher_Memberships_Should_Not_Grant_Shared_Access(SchoolMemberRole role, int period)
    {
        var fixture = await CreateFixtureAsync();
        SchoolKnowledgeShareDto share;
        using (ChangeUser(fixture.Owner)) share = await _service.ShareVersionAsync(Input(fixture));
        await WithUnitOfWorkAsync(async () =>
        {
            var membership = await _memberships.GetAsync(fixture.ReaderMembershipId);
            membership.EndAt(DateTime.UtcNow.AddSeconds(-1));
            await _memberships.UpdateAsync(membership, autoSave: true);
            var start = DateTime.UtcNow.AddDays(period > 0 ? 1 : -2);
            var end = period < 0 ? DateTime.UtcNow.AddDays(-1) : (DateTime?)null;
            await _memberships.InsertAsync(new SchoolMembership(Guid.NewGuid(), null, fixture.School.Id,
                fixture.Reader, role, start, end), autoSave: true);
        });
        using var user = ChangeUser(fixture.Reader);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetSchoolSharesAsync(fixture.School.Id, new GetSchoolKnowledgeShareListInput()));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetDetailAsync(share.Id));
    }

    [Fact]
    public async Task Author_Leaving_Should_Stop_New_Use_And_Still_Allow_Owner_To_Revoke()
    {
        var fixture = await CreateFixtureAsync();
        var request = Input(fixture);
        SchoolKnowledgeShareDto share;
        using (ChangeUser(fixture.Owner)) share = await _service.ShareVersionAsync(request);
        await WithUnitOfWorkAsync(async () =>
        {
            var membership = await _memberships.GetAsync(fixture.OwnerMembershipId);
            membership.EndAt(DateTime.UtcNow.AddSeconds(-1));
            await _memberships.UpdateAsync(membership, autoSave: true);
        });
        using (ChangeUser(fixture.Reader))
        {
            (await _service.GetSchoolSharesAsync(fixture.School.Id, new GetSchoolKnowledgeShareListInput())).Items.ShouldBeEmpty();
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetDetailAsync(share.Id));
        }
        using (ChangeUser(fixture.Owner))
        {
            (await _service.GetMySharesAsync(new GetSchoolKnowledgeShareListInput())).TotalCount.ShouldBe(1);
            (await _service.RevokeAsync(share.Id)).IsRevoked.ShouldBeTrue();
            (await _service.ShareVersionAsync(request)).IsRevoked.ShouldBeTrue();
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.ShareVersionAsync(Input(fixture)));
        }
    }

    [Fact]
    public async Task Publication_Should_Be_Idempotent_Without_Reactivating_Revoked_Operation()
    {
        var fixture = await CreateFixtureAsync();
        using var user = ChangeUser(fixture.Owner);
        var request = Input(fixture);
        var first = await _service.ShareVersionAsync(request);
        (await _service.ShareVersionAsync(request)).Id.ShouldBe(first.Id);
        (await _service.ShareVersionAsync(Input(fixture))).Id.ShouldBe(first.Id);
        var changed = Input(fixture, SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Download);
        (await Should.ThrowAsync<BusinessException>(() => _service.ShareVersionAsync(changed)))
            .Code.ShouldBe(SchoolKnowledgeSharingErrorCodes.ConflictingShare);
        await _service.RevokeAsync(first.Id);
        (await _service.ShareVersionAsync(request)).IsRevoked.ShouldBeTrue();
        var second = await _service.ShareVersionAsync(changed);
        second.Id.ShouldNotBe(first.Id);
        second.IsRevoked.ShouldBeFalse();
        (await _service.GetMySharesAsync(new GetSchoolKnowledgeShareListInput())).TotalCount.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sharing_A_Selected_Version_Should_Not_Copy_Private_Draft_Or_Revision_Summary(bool publishRevision)
    {
        var fixture = await CreateFixtureAsync();
        using var user = ChangeUser(fixture.Owner);
        await _itemService.UpdateAsync(fixture.Item.Id, new UpdateKnowledgeItemDto { Title = "PRIVATE-TITLE", Summary = "PRIVATE-SUMMARY" });
        var second = await _itemService.CreateVersionAsync(fixture.Item.Id, new CreateKnowledgeVersionDto { Title = "PRIVATE-TITLE", ContentText = "PRIVATE-CONTENT" });
        if (publishRevision) await _itemService.PublishVersionAsync(fixture.Item.Id, second.Id);
        var request = Input(fixture);
        request.SharedSummary = null;
        var share = await _service.ShareVersionAsync(request);
        share.Title.ShouldBe("共享V1标题");
        share.Summary.ShouldBeNull();
    }

    [Fact]
    public async Task A_Teacher_And_Admin_Cannot_Share_Or_Revoke_Another_Authors_Private_Item()
    {
        var fixture = await CreateFixtureAsync();
        SchoolKnowledgeShareDto share;
        using (ChangeUser(fixture.Owner)) share = await _service.ShareVersionAsync(Input(fixture));
        using var user = ChangeUser(fixture.Reader);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.ShareVersionAsync(Input(fixture)));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.RevokeAsync(share.Id));
        (await _service.GetMySharesAsync(new GetSchoolKnowledgeShareListInput())).Items.ShouldBeEmpty();
        (await WithUnitOfWorkAsync(() => _shares.GetAsync(share.Id))).IsRevoked.ShouldBeFalse();
    }

    [Fact]
    public async Task Drafts_Unsupported_Uses_And_Foreign_Tenants_Should_Not_Create_Shares()
    {
        var fixture = await CreateFixtureAsync();
        using var user = ChangeUser(fixture.Owner);
        var draft = await _itemService.CreateVersionAsync(fixture.Item.Id, new CreateKnowledgeVersionDto { Title = "草稿" });
        var request = Input(fixture);
        request.KnowledgeVersionId = draft.Id;
        (await Should.ThrowAsync<BusinessException>(() => _service.ShareVersionAsync(request)))
            .Code.ShouldBe(SchoolKnowledgeSharingErrorCodes.PreparedVersionRequired);
        (await Should.ThrowAsync<BusinessException>(() => _service.ShareVersionAsync(Input(fixture, SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Export))))
            .Code.ShouldBe(SchoolKnowledgeSharingErrorCodes.InvalidUses);
        using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.ShareVersionAsync(Input(fixture)));
        (await WithUnitOfWorkAsync(() => _shares.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Available_Lists_Should_Filter_Invalid_Libraries_Before_Count_And_Paging()
    {
        var first = await CreateFixtureAsync();
        var second = await CreateFixtureAsync(first.School, first.Reader);
        using (ChangeUser(first.Owner)) await _service.ShareVersionAsync(Input(first));
        using (ChangeUser(second.Owner)) await _service.ShareVersionAsync(Input(second));
        await WithUnitOfWorkAsync(() => _libraries.DeleteAsync(first.Owner, autoSave: true));
        using var user = ChangeUser(first.Reader);
        var result = await _service.GetSchoolSharesAsync(first.School.Id, new GetSchoolKnowledgeShareListInput { MaxResultCount = 1 });
        result.TotalCount.ShouldBe(1);
        result.Items.Single().OwnerUserId.ShouldBe(second.Owner);
    }

    [Fact]
    public async Task Database_Should_Enforce_One_Active_Share_Per_Version_And_School()
    {
        var fixture = await CreateFixtureAsync();
        using var user = ChangeUser(fixture.Owner);
        await _service.ShareVersionAsync(Input(fixture));
        await Should.ThrowAsync<DbUpdateException>(() => WithUnitOfWorkAsync(() => _shares.InsertAsync(new SchoolKnowledgeShare(
            Guid.NewGuid(), null, fixture.Owner, fixture.Owner, fixture.Item.Id, fixture.Version.Id,
            fixture.School.Id, "重复", null, SchoolKnowledgeShareUses.Read, DateTime.UtcNow), autoSave: true)));
        (await WithUnitOfWorkAsync(() => _shares.CountAsync(x => !x.IsRevoked))).ShouldBe(1);
    }

    [Theory]
    [InlineData(KnowledgeIndexJobStatus.Pending, false, false)]
    [InlineData(KnowledgeIndexJobStatus.Processing, false, false)]
    [InlineData(KnowledgeIndexJobStatus.Indexed, true, false)]
    [InlineData(KnowledgeIndexJobStatus.Indexed, false, true)]
    [InlineData(KnowledgeIndexJobStatus.Failed, false, true)]
    [InlineData(KnowledgeIndexJobStatus.Outdated, false, true)]
    [InlineData(KnowledgeIndexJobStatus.Deleted, false, true)]
    [InlineData(null, false, true)]
    public async Task Retrieval_Share_Should_Queue_Only_When_No_Usable_Or_In_Progress_Index_Exists(
        KnowledgeIndexJobStatus? status, bool activeChunk, bool expectQueued)
    {
        var fixture = await CreateFixtureAsync();
        var jobs = GetRequiredService<IRepository<KnowledgeIndexJob, Guid>>();
        await WithUnitOfWorkAsync(async () =>
        {
            var job = await jobs.GetAsync(x => x.KnowledgeItemId == fixture.Item.Id && x.KnowledgeVersionId == fixture.Version.Id);
            if (!status.HasValue)
            {
                await jobs.DeleteAsync(job, autoSave: true);
                return;
            }
            // 数据库决策夹具；完整索引链路由独立 pipeline 集成测试验证，不能用此状态构造证明管道完成。
            switch (status)
            {
                case KnowledgeIndexJobStatus.Processing: job.Start(DateTime.UtcNow); break;
                case KnowledgeIndexJobStatus.Indexed: job.MarkIndexed(DateTime.UtcNow); break;
                case KnowledgeIndexJobStatus.Failed: job.MarkFailed("reader", "Fixture failure.", DateTime.UtcNow); break;
                case KnowledgeIndexJobStatus.Outdated: job.MarkOutdated(); break;
                case KnowledgeIndexJobStatus.Deleted: job.MarkDeleted(DateTime.UtcNow); break;
            }
            await jobs.UpdateAsync(job, autoSave: true);
            if (activeChunk)
                await GetRequiredService<IRepository<KnowledgeChunk, Guid>>().InsertAsync(new KnowledgeChunk(
                    Guid.NewGuid(), null, fixture.Item.Id, fixture.Version.Id, job.Id, 0, "索引决策夹具", "fixture-hash",
                    fixture.Version.Title), autoSave: true);
        });
        using var user = ChangeUser(fixture.Owner);
        var request = Input(fixture);
        await _service.ShareVersionAsync(request);
        await _service.ShareVersionAsync(request);
        await _service.ShareVersionAsync(Input(fixture));
        var rows = await WithUnitOfWorkAsync(() => jobs.GetListAsync(x => x.KnowledgeVersionId == fixture.Version.Id));
        rows.Count.ShouldBe(status.HasValue && expectQueued ? 2 : 1);
        if (expectQueued)
        {
            var queued = rows.Single(x => x.Status == KnowledgeIndexJobStatus.Pending);
            queued.TriggerType.ShouldBe(KnowledgeIndexJobTriggerType.ManualRebuild);
            queued.RequestedByUserId.ShouldBe(fixture.Owner);
            queued.KnowledgeItemId.ShouldBe(fixture.Item.Id);
            (await _service.GetDetailAsync(request.IdempotencyKey)).IndexStatus.ShouldBe(KnowledgeIndexJobStatus.Pending);
        }
    }

    [Fact]
    public async Task Read_Only_Share_Should_Not_Queue_A_Retrieval_Index()
    {
        var fixture = await CreateFixtureAsync();
        var jobs = GetRequiredService<IRepository<KnowledgeIndexJob, Guid>>();
        await WithUnitOfWorkAsync(async () =>
        {
            var job = await jobs.GetAsync(x => x.KnowledgeItemId == fixture.Item.Id);
            job.MarkOutdated();
            await jobs.UpdateAsync(job, autoSave: true);
        });
        using var user = ChangeUser(fixture.Owner);
        await _service.ShareVersionAsync(Input(fixture, SchoolKnowledgeShareUses.Read));
        (await WithUnitOfWorkAsync(() => jobs.CountAsync(x => x.KnowledgeItemId == fixture.Item.Id))).ShouldBe(1);
    }

    private async Task<Fixture> CreateFixtureAsync(School? existingSchool = null, Guid? existingReader = null)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var users = GetRequiredService<IRepository<IdentityUser, Guid>>();
            var owner = Guid.NewGuid();
            var reader = existingReader ?? Guid.NewGuid();
            await users.InsertAsync(new IdentityUser(owner, $"share-{owner:N}", $"{owner:N}@example.invalid"), autoSave: true);
            if (!existingReader.HasValue)
                await users.InsertAsync(new IdentityUser(reader, $"share-{reader:N}", $"{reader:N}@example.invalid"), autoSave: true);
            var school = existingSchool ?? new School(Guid.NewGuid(), null, $"S{Guid.NewGuid():N}", "共享测试初中");
            if (existingSchool == null)
                await GetRequiredService<IRepository<School, Guid>>().InsertAsync(school, autoSave: true);
            var now = DateTime.UtcNow.AddDays(-1);
            var ownerMembership = new SchoolMembership(Guid.NewGuid(), null, school.Id, owner, SchoolMemberRole.Teacher, now);
            var readerMembership = new SchoolMembership(Guid.NewGuid(), null, school.Id, reader, SchoolMemberRole.Teacher, now);
            await _memberships.InsertAsync(ownerMembership, autoSave: true);
            if (!existingReader.HasValue) await _memberships.InsertAsync(readerMembership, autoSave: true);
            await _libraries.InsertAsync(new KnowledgeLibrary(owner, null), autoSave: true);
            var item = new KnowledgeItem(Guid.NewGuid(), null, "共享V1标题", summary: "共享V1摘要", libraryId: owner, ownerUserId: owner);
            await _items.InsertAsync(item, autoSave: true);
            var version = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1, "共享V1标题", "共享V1正文");
            version.Publish(now);
            await _versions.InsertAsync(version, autoSave: true);
            item.SetCurrentPublishedVersion(version.Id);
            await _items.UpdateAsync(item, autoSave: true);
            var otherVersion = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 2, "私有草稿", "私人内容");
            await _versions.InsertAsync(otherVersion, autoSave: true);
            var attachment = new KnowledgeAttachment(Guid.NewGuid(), null, version.Id, "v1.txt", $"shares/{Guid.NewGuid():N}", "text/plain", 3);
            var otherAttachment = new KnowledgeAttachment(Guid.NewGuid(), null, otherVersion.Id, "private.txt", $"shares/{Guid.NewGuid():N}", "text/plain", 3);
            var attachments = GetRequiredService<IRepository<KnowledgeAttachment, Guid>>();
            await attachments.InsertAsync(attachment, autoSave: true);
            await attachments.InsertAsync(otherAttachment, autoSave: true);
            await GetRequiredService<IBlobContainer<KnowledgeAttachmentBlobContainer>>().SaveAsync(attachment.BlobName, new byte[] { 1, 2, 3 });
            var job = new KnowledgeIndexJob(Guid.NewGuid(), null, item.Id, version.Id, KnowledgeIndexJobTriggerType.Publish);
            await GetRequiredService<IRepository<KnowledgeIndexJob, Guid>>().InsertAsync(job, autoSave: true);
            return new Fixture(owner, reader, school, item, version, attachment, otherAttachment, ownerMembership.Id, readerMembership.Id);
        });
    }

    private IDisposable ChangeUser(Guid userId) => GetRequiredService<ICurrentPrincipalAccessor>().Change(
        new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, userId.ToString()), new Claim(AbpClaimTypes.UserName, $"share-{userId:N}"),
            new Claim(AbpClaimTypes.Role, "admin")
        }, "Test")));

    private static CreateSchoolKnowledgeShareDto Input(Fixture fixture, SchoolKnowledgeShareUses uses =
        SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve) => new()
    {
        IdempotencyKey = Guid.NewGuid(), KnowledgeItemId = fixture.Item.Id, KnowledgeVersionId = fixture.Version.Id,
        SchoolId = fixture.School.Id, Uses = uses, SharedSummary = "共享V1摘要"
    };

    private sealed record Fixture(Guid Owner, Guid Reader, School School, KnowledgeItem Item, KnowledgeVersion Version,
        KnowledgeAttachment Attachment, KnowledgeAttachment OtherAttachment, Guid OwnerMembershipId, Guid ReaderMembershipId);
}
