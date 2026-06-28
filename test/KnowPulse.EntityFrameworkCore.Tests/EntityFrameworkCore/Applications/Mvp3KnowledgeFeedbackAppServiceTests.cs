using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
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

    public Mvp3KnowledgeFeedbackAppServiceTests()
    {
        _feedbackAppService = GetRequiredService<IKnowledgeFeedbackAppService>();
        _knowledgeItemRepository = GetRequiredService<IRepository<KnowledgeItem, Guid>>();
        _knowledgeVersionRepository = GetRequiredService<IRepository<KnowledgeVersion, Guid>>();
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
    public async Task Should_Process_Link_Resolve_And_Close_Feedback()
    {
        var prefix = UniquePrefix();
        var knowledge = await CreatePublishedKnowledgeAsync($"{prefix} handbook", "v1 content");
        var draft = await CreateDraftVersionAsync(knowledge.Item.Id, $"{prefix} handbook v2", "fixed draft content", versionNumber: 2);
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

        var linked = await _feedbackAppService.LinkVersionAsync(feedback.Id, new LinkKnowledgeFeedbackVersionDto
        {
            LinkedKnowledgeVersionId = draft.Id,
            Resolution = "已创建修订草稿"
        });
        linked.Status.ShouldBe(KnowledgeFeedbackStatus.Processing);
        linked.LinkedKnowledgeVersionId.ShouldBe(draft.Id);

        var resolved = await _feedbackAppService.ResolveAsync(feedback.Id, new ResolveKnowledgeFeedbackDto
        {
            LinkedKnowledgeVersionId = draft.Id,
            Resolution = "已在草稿版本修正"
        });
        resolved.Status.ShouldBe(KnowledgeFeedbackStatus.Resolved);
        resolved.LinkedKnowledgeVersionTitle.ShouldBe(draft.Title);
        resolved.Resolution.ShouldBe("已在草稿版本修正");

        var closed = await _feedbackAppService.CloseAsync(feedback.Id, new CloseKnowledgeFeedbackDto
        {
            CloseReason = "修订已验收"
        });
        closed.Status.ShouldBe(KnowledgeFeedbackStatus.Closed);
        closed.CloseReason.ShouldBe("修订已验收");

        var publishedVersion = await _knowledgeVersionRepository.GetAsync(knowledge.PublishedVersion.Id);
        publishedVersion.Status.ShouldBe(KnowledgeVersionStatus.Published);
        publishedVersion.ContentText.ShouldBe("v1 content");
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

    private async Task<KnowledgeVersion> CreateDraftVersionAsync(Guid itemId, string title, string content, int versionNumber)
    {
        return await _knowledgeVersionRepository.InsertAsync(
            new KnowledgeVersion(
                Guid.NewGuid(),
                null,
                itemId,
                versionNumber,
                title,
                content),
            autoSave: true);
    }

    private static string UniquePrefix()
    {
        return $"feedback-{Guid.NewGuid():N}";
    }

    private sealed record PublishedKnowledge(KnowledgeItem Item, KnowledgeVersion PublishedVersion);
}
