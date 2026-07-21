using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class KnowledgeManagementAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IKnowledgeItemAppService _itemAppService;
    private readonly IKnowledgeIndexJobAppService _indexJobAppService;
    private readonly IKnowledgeManagementAppService _managementAppService;

    public KnowledgeManagementAppServiceTests()
    {
        _itemAppService = GetRequiredService<IKnowledgeItemAppService>();
        _indexJobAppService = GetRequiredService<IKnowledgeIndexJobAppService>();
        _managementAppService = GetRequiredService<IKnowledgeManagementAppService>();
    }

    [Fact]
    public async Task Should_Aggregate_Draft_Publishing_Ready_And_Failed_Statuses()
    {
        var prefix = $"management-{Guid.NewGuid():N}";
        var ready = await CreateVersionAsync($"{prefix}-ready");
        var publishing = await CreateVersionAsync($"{prefix}-publishing");
        var failed = await CreateVersionAsync($"{prefix}-failed");
        var draft = await CreateVersionAsync($"{prefix}-draft");

        var readyVersion = await _itemAppService.PublishVersionAsync(ready.Item.Id, ready.Version.Id);
        var readyJob = await GetLatestJobAsync(readyVersion.Id);
        await _indexJobAppService.StartAsync(readyJob.Id);
        await _indexJobAppService.MarkIndexedAsync(readyJob.Id);

        var publishingVersion = await _itemAppService.PublishVersionAsync(publishing.Item.Id, publishing.Version.Id);
        var publishingJob = await GetLatestJobAsync(publishingVersion.Id);
        await _indexJobAppService.StartAsync(publishingJob.Id);

        var failedVersion = await _itemAppService.PublishVersionAsync(failed.Item.Id, failed.Version.Id);
        var failedJob = await GetLatestJobAsync(failedVersion.Id);
        await _indexJobAppService.StartAsync(failedJob.Id);
        await _indexJobAppService.MarkFailedAsync(failedJob.Id, new MarkKnowledgeIndexJobFailedDto
        {
            FailureStage = "reader",
            FailureReason = "No readable knowledge content was found."
        });

        var result = await _managementAppService.GetListAsync(new GetKnowledgeManagementListInput
        {
            Keyword = prefix,
            MaxResultCount = 20
        });

        result.TotalCount.ShouldBe(4);
        result.Overview.TotalCount.ShouldBe(4);
        result.Overview.ReadyCount.ShouldBe(1);
        result.Overview.NeedsAttentionCount.ShouldBe(1);
        result.Overview.DraftCount.ShouldBe(1);
        result.Overview.PublishingCount.ShouldBe(1);
        result.Items.Single(x => x.Id == ready.Item.Id).Status.ShouldBe(KnowledgeManagementStatus.Ready);
        result.Items.Single(x => x.Id == publishing.Item.Id).Status.ShouldBe(KnowledgeManagementStatus.Publishing);
        result.Items.Single(x => x.Id == failed.Item.Id).Status.ShouldBe(KnowledgeManagementStatus.NeedsAttention);
        result.Items.Single(x => x.Id == draft.Item.Id).Status.ShouldBe(KnowledgeManagementStatus.Draft);

        var detail = await _managementAppService.GetAsync(failed.Item.Id);
        detail.LatestIndexFailureStage.ShouldBe("reader");
        detail.IndexJobs.Count.ShouldBe(1);
        detail.Versions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Should_Keep_Knowledge_Access_Filter_On_List_And_Detail()
    {
        var prefix = $"management-access-{Guid.NewGuid():N}";
        var visible = await CreateVersionAsync($"{prefix}-visible");
        var hidden = await CreateVersionAsync($"{prefix}-hidden");

        await _itemAppService.CreatePermissionScopeAsync(hidden.Item.Id, new CreateKnowledgePermissionScopeDto
        {
            ScopeType = KnowledgePermissionScopeType.User,
            TargetId = Guid.NewGuid()
        });

        var result = await _managementAppService.GetListAsync(new GetKnowledgeManagementListInput
        {
            Keyword = prefix,
            MaxResultCount = 20
        });

        result.Items.Select(x => x.Id).ShouldContain(visible.Item.Id);
        result.Items.Select(x => x.Id).ShouldNotContain(hidden.Item.Id);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _managementAppService.GetAsync(hidden.Item.Id));
    }

    [Fact]
    public async Task Should_Atomically_Replace_Access_Policy_Publish_And_Create_Index_Job()
    {
        var knowledge = await CreateVersionAsync($"atomic-publish-{Guid.NewGuid():N}");
        var firstPublish = await _itemAppService.PublishVersionAsync(knowledge.Item.Id, knowledge.Version.Id);
        var firstJob = await GetLatestJobAsync(firstPublish.Id);
        await _indexJobAppService.StartAsync(firstJob.Id);
        await _indexJobAppService.MarkIndexedAsync(firstJob.Id);

        var nextVersion = await _itemAppService.CreateVersionAsync(knowledge.Item.Id, new CreateKnowledgeVersionDto
        {
            Title = "atomic v2",
            ContentText = "replacement content"
        });
        var result = await _itemAppService.PublishVersionWithAccessPolicyAsync(
            knowledge.Item.Id,
            new PublishKnowledgeVersionInput
            {
                KnowledgeVersionId = nextVersion.Id,
                PermissionScopes =
                [
                    new CreateKnowledgePermissionScopeDto
                    {
                        ScopeType = KnowledgePermissionScopeType.All,
                        MinimumSecurityLevel = 0
                    }
                ]
            });

        result.Version.Id.ShouldBe(nextVersion.Id);
        result.Version.IsCurrentPublished.ShouldBeTrue();
        result.PermissionScopes.Count.ShouldBe(1);
        result.PermissionScopes[0].ScopeType.ShouldBe(KnowledgePermissionScopeType.All);
        result.IndexJob.Status.ShouldBe(KnowledgeIndexJobStatus.Pending);
        result.IndexJob.TriggerType.ShouldBe(KnowledgeIndexJobTriggerType.Publish);

        (await _indexJobAppService.GetAsync(firstJob.Id)).Status.ShouldBe(KnowledgeIndexJobStatus.Outdated);
        var item = await _itemAppService.GetAsync(knowledge.Item.Id);
        item.CurrentPublishedVersionId.ShouldBe(nextVersion.Id);
        var scopes = await _itemAppService.GetPermissionScopesAsync(knowledge.Item.Id);
        scopes.Items.Count.ShouldBe(1);

        var detail = await _managementAppService.GetAsync(knowledge.Item.Id);
        detail.HasExplicitAccessPolicy.ShouldBeTrue();
        detail.Status.ShouldBe(KnowledgeManagementStatus.Publishing);
        detail.LatestIndexJobId.ShouldBe(result.IndexJob.Id);
    }

    [Fact]
    public async Task Should_Roll_Back_Access_Policy_When_Publish_Fails()
    {
        var knowledge = await CreateVersionAsync($"atomic-rollback-{Guid.NewGuid():N}");
        await _itemAppService.CreatePermissionScopeAsync(knowledge.Item.Id, new CreateKnowledgePermissionScopeDto
        {
            ScopeType = KnowledgePermissionScopeType.All
        });
        await _itemAppService.ArchiveVersionAsync(knowledge.Item.Id, knowledge.Version.Id);

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _itemAppService.PublishVersionWithAccessPolicyAsync(
                knowledge.Item.Id,
                new PublishKnowledgeVersionInput
                {
                    KnowledgeVersionId = knowledge.Version.Id,
                    PermissionScopes =
                    [
                        new CreateKnowledgePermissionScopeDto
                        {
                            ScopeType = KnowledgePermissionScopeType.Department,
                            TargetId = Guid.NewGuid()
                        }
                    ]
                }));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.KnowledgeVersionAlreadyArchived);
        var scopes = await _itemAppService.GetPermissionScopesAsync(knowledge.Item.Id);
        scopes.Items.Count.ShouldBe(1);
        scopes.Items[0].ScopeType.ShouldBe(KnowledgePermissionScopeType.All);
        var versions = await _itemAppService.GetVersionsAsync(knowledge.Item.Id);
        versions.Items.Single().Status.ShouldBe(KnowledgeVersionStatus.Archived);
        (await _indexJobAppService.GetListAsync(new GetKnowledgeIndexJobListInput
        {
            KnowledgeVersionId = knowledge.Version.Id,
            MaxResultCount = 20
        })).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Should_Keep_Published_Access_Policy_Explicit_When_Editing_Scopes()
    {
        var knowledge = await CreateVersionAsync($"explicit-policy-{Guid.NewGuid():N}");
        await _itemAppService.PublishVersionAsync(knowledge.Item.Id, knowledge.Version.Id);

        var departmentId = Guid.NewGuid();
        await _itemAppService.CreatePermissionScopeAsync(knowledge.Item.Id, new CreateKnowledgePermissionScopeDto
        {
            ScopeType = KnowledgePermissionScopeType.Department,
            TargetId = departmentId
        });

        var scopes = await _itemAppService.GetPermissionScopesAsync(knowledge.Item.Id);
        scopes.Items.Count.ShouldBe(1);
        scopes.Items[0].ScopeType.ShouldBe(KnowledgePermissionScopeType.Department);
        scopes.Items[0].TargetId.ShouldBe(departmentId);

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _itemAppService.DeletePermissionScopeAsync(knowledge.Item.Id, scopes.Items[0].Id));
        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.KnowledgeAccessPolicyRequired);
        (await _itemAppService.GetPermissionScopesAsync(knowledge.Item.Id)).Items.Count.ShouldBe(1);
    }

    private async Task<(KnowledgeItemDto Item, KnowledgeVersionDto Version)> CreateVersionAsync(string title)
    {
        var item = await _itemAppService.CreateAsync(new CreateKnowledgeItemDto
        {
            Title = title,
            Summary = $"{title} summary"
        });
        var version = await _itemAppService.CreateVersionAsync(item.Id, new CreateKnowledgeVersionDto
        {
            Title = $"{title} v1",
            ContentText = $"{title} content"
        });
        return (item, version);
    }

    private async Task<KnowledgeIndexJobDto> GetLatestJobAsync(Guid versionId)
    {
        var result = await _indexJobAppService.GetListAsync(new GetKnowledgeIndexJobListInput
        {
            KnowledgeVersionId = versionId,
            MaxResultCount = 20
        });
        return result.Items.OrderByDescending(x => x.CreationTime).First();
    }
}
