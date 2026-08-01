using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class Mvp3KnowledgeFeedbackAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IKnowledgeFeedbackAppService _feedbackAppService;
    private readonly IRepository<KnowledgeItem, Guid> _knowledgeItemRepository;
    private readonly IRepository<KnowledgeVersion, Guid> _knowledgeVersionRepository;
    private readonly IRepository<KnowledgeIndexJob, Guid> _knowledgeIndexJobRepository;

    public Mvp3KnowledgeFeedbackAppServiceTests()
    {
        _feedbackAppService = GetRequiredService<IKnowledgeFeedbackAppService>();
        _knowledgeItemRepository = GetRequiredService<IRepository<KnowledgeItem, Guid>>();
        _knowledgeVersionRepository = GetRequiredService<IRepository<KnowledgeVersion, Guid>>();
        _knowledgeIndexJobRepository = GetRequiredService<IRepository<KnowledgeIndexJob, Guid>>();
    }

    [Fact]
    public async Task Should_Submit_Feedback_Without_Modifying_Published_Version()
    {
        var prefix = UniquePrefix();
        var knowledge = await CreatePublishedKnowledgeAsync($"{prefix} policy", "original published content");

        var submitted = await _feedbackAppService.SubmitAsync(new SubmitKnowledgeFeedbackDto
        {
            KnowledgeItemId = knowledge.Item.Id,
            KnowledgeVersionId = knowledge.PublishedVersion.Id,
            Type = KnowledgeFeedbackType.Error,
            Title = "标题错误",
            Description = "发布版本中的制度名称写错了"
        });

        submitted.Status.ShouldBe(KnowledgeFeedbackStatus.Submitted);
        submitted.Type.ShouldBe(KnowledgeFeedbackType.Error);
        submitted.KnowledgeTitle.ShouldBe(knowledge.Item.Title);
        submitted.KnowledgeVersionId.ShouldBe(knowledge.PublishedVersion.Id);

        var publishedVersion = await _knowledgeVersionRepository.GetAsync(knowledge.PublishedVersion.Id);
        publishedVersion.Status.ShouldBe(KnowledgeVersionStatus.Published);
        publishedVersion.IsCurrentPublished.ShouldBeTrue();
        publishedVersion.ContentText.ShouldBe("original published content");

        var list = await _feedbackAppService.GetListAsync(new GetKnowledgeFeedbackListInput
        {
            Keyword = "标题错误",
            MaxResultCount = 10
        });
        list.Items.Single().Id.ShouldBe(submitted.Id);
    }

    [Fact]
    public async Task Should_Process_Create_Revision_And_Complete_After_Publish_And_Index()
    {
        var prefix = UniquePrefix();
        var knowledge = await CreatePublishedKnowledgeAsync($"{prefix} handbook", "v1 content");
        var feedback = await _feedbackAppService.SubmitAsync(new SubmitKnowledgeFeedbackDto
        {
            KnowledgeItemId = knowledge.Item.Id,
            KnowledgeVersionId = knowledge.PublishedVersion.Id,
            Type = KnowledgeFeedbackType.Outdated,
            Title = "流程过期",
            Description = "流程需要更新"
        });

        var processing = await _feedbackAppService.ProcessAsync(feedback.Id, new ProcessKnowledgeFeedbackDto
        {
            Resolution = "确认需要修订"
        });
        processing.Status.ShouldBe(KnowledgeFeedbackStatus.Processing);
        processing.Resolution.ShouldBe("确认需要修订");

        var linked = await _feedbackAppService.CreateRevisionAsync(feedback.Id);
        linked.Status.ShouldBe(KnowledgeFeedbackStatus.Processing);
        linked.LinkedKnowledgeVersionId.ShouldNotBeNull();
        linked.LinkedKnowledgeVersionNumber.ShouldBe(2);
        linked.LinkedKnowledgeVersionStatus.ShouldBe(KnowledgeVersionStatus.Draft);

        var draft = await _knowledgeVersionRepository.GetAsync(linked.LinkedKnowledgeVersionId!.Value);
        draft.Title.ShouldBe(knowledge.PublishedVersion.Title);
        draft.ContentText.ShouldBe("v1 content");

        var notPublished = await Should.ThrowAsync<BusinessException>(() =>
            _feedbackAppService.ResolveAsync(feedback.Id, new ResolveKnowledgeFeedbackDto
            {
                Resolution = "尝试过早完成"
            }));
        notPublished.Code.ShouldBe(KnowPulseDomainErrorCodes.KnowledgeFeedbackRevisionNotPublished);

        await PublishRevisionAsync(knowledge, draft);

        var notIndexed = await Should.ThrowAsync<BusinessException>(() =>
            _feedbackAppService.ResolveAsync(feedback.Id, new ResolveKnowledgeFeedbackDto
            {
                Resolution = "发布后但索引尚未完成"
            }));
        notIndexed.Code.ShouldBe(KnowPulseDomainErrorCodes.KnowledgeFeedbackRevisionNotIndexed);

        await MarkRevisionIndexedAsync(knowledge.Item.Id, draft.Id);

        var resolved = await _feedbackAppService.ResolveAsync(feedback.Id, new ResolveKnowledgeFeedbackDto
        {
            Resolution = "修订已发布并完成检索验证"
        });
        resolved.Status.ShouldBe(KnowledgeFeedbackStatus.Resolved);
        resolved.LinkedKnowledgeVersionTitle.ShouldBe(draft.Title);
        resolved.LinkedKnowledgeVersionIndexStatus.ShouldBe(KnowledgeIndexJobStatus.Indexed);
        resolved.Resolution.ShouldBe("修订已发布并完成检索验证");

        var publishedVersion = await _knowledgeVersionRepository.GetAsync(knowledge.PublishedVersion.Id);
        publishedVersion.Status.ShouldBe(KnowledgeVersionStatus.Published);
        publishedVersion.ContentText.ShouldBe("v1 content");
    }

    [Fact]
    public async Task Should_Require_Taking_Ownership_Before_Creating_Revision()
    {
        var prefix = UniquePrefix();
        var knowledge = await CreatePublishedKnowledgeAsync($"{prefix} ownership", "source content");
        var feedback = await _feedbackAppService.SubmitAsync(new SubmitKnowledgeFeedbackDto
        {
            KnowledgeItemId = knowledge.Item.Id,
            KnowledgeVersionId = knowledge.PublishedVersion.Id,
            Type = KnowledgeFeedbackType.Error,
            Title = "需要修订",
            Description = "先接手再开始修改"
        });

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _feedbackAppService.CreateRevisionAsync(feedback.Id));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.KnowledgeFeedbackRevisionRequiresProcessing);
    }

    [Fact]
    public async Task Should_Reject_Linked_Version_From_Other_Knowledge_Item()
    {
        var prefix = UniquePrefix();
        var source = await CreatePublishedKnowledgeAsync($"{prefix} source", "source content");
        var other = await CreatePublishedKnowledgeAsync($"{prefix} other", "other content");
        var feedback = await _feedbackAppService.SubmitAsync(new SubmitKnowledgeFeedbackDto
        {
            KnowledgeItemId = source.Item.Id,
            KnowledgeVersionId = source.PublishedVersion.Id,
            Type = KnowledgeFeedbackType.Missing,
            Title = "缺少附件",
            Description = "需要补充附件说明"
        });

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _feedbackAppService.LinkVersionAsync(feedback.Id, new LinkKnowledgeFeedbackVersionDto
            {
                LinkedKnowledgeVersionId = other.PublishedVersion.Id
            }));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.KnowledgeFeedbackVersionItemMismatch);
    }

    private async Task<PublishedKnowledge> CreatePublishedKnowledgeAsync(string title, string content)
    {
        var item = await _knowledgeItemRepository.InsertAsync(
            new KnowledgeItem(Guid.NewGuid(), null, title, summary: $"{title} summary"),
            autoSave: true);

        var version = new KnowledgeVersion(
            Guid.NewGuid(),
            null,
            item.Id,
            1,
            $"{title} v1",
            content);
        version.Publish(DateTime.UtcNow);
        version = await _knowledgeVersionRepository.InsertAsync(version, autoSave: true);

        item.SetCurrentPublishedVersion(version.Id);
        item = await _knowledgeItemRepository.UpdateAsync(item, autoSave: true);

        return new PublishedKnowledge(item, version);
    }

    private async Task PublishRevisionAsync(PublishedKnowledge knowledge, KnowledgeVersion revision)
    {
        var original = await _knowledgeVersionRepository.GetAsync(knowledge.PublishedVersion.Id);
        original.ClearCurrentPublished();
        await _knowledgeVersionRepository.UpdateAsync(original, autoSave: true);

        revision.Publish(DateTime.UtcNow);
        await _knowledgeVersionRepository.UpdateAsync(revision, autoSave: true);

        var item = await _knowledgeItemRepository.GetAsync(knowledge.Item.Id);
        item.SetCurrentPublishedVersion(revision.Id);
        await _knowledgeItemRepository.UpdateAsync(item, autoSave: true);

    }

    private async Task MarkRevisionIndexedAsync(Guid itemId, Guid revisionId)
    {
        var job = new KnowledgeIndexJob(
            Guid.NewGuid(),
            null,
            itemId,
            revisionId,
            KnowledgeIndexJobTriggerType.Publish);
        job.Start(DateTime.UtcNow);
        job.MarkIndexed(DateTime.UtcNow);
        await _knowledgeIndexJobRepository.InsertAsync(job, autoSave: true);
    }

    private static string UniquePrefix()
    {
        return $"feedback-{Guid.NewGuid():N}";
    }

    private sealed record PublishedKnowledge(KnowledgeItem Item, KnowledgeVersion PublishedVersion);
}
