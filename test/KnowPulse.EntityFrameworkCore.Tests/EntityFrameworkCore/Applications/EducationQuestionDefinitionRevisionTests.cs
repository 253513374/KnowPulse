using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Fact]
    public async Task Curriculum_Review_Should_Require_Stable_Operation_And_The_Actual_Pending_Revision_Token()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var service = GetRequiredService<IQuestionAppService>();
        var pending = await DefinitionPendingAsync(f);
        await Should.ThrowAsync<BusinessException>(() => service.ApproveAsync(pending.Id, new ReviewQuestionDto()));
        (await Should.ThrowAsync<BusinessException>(() => service.ApproveAsync(pending.Id,
            new ReviewQuestionDto { OperationId = Guid.NewGuid() }))).Code.ShouldBe("KnowPulse:Education:ReviewConcurrencyRequired");
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => service.ApproveAsync(pending.Id,
            new ReviewQuestionDto { OperationId = Guid.NewGuid(), ConcurrencyStamp = "stale" }));
        using (User(f.Owner))
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.ApproveAsync(pending.Id, DefinitionReview(pending)));
        (await service.GetAsync(pending.Id)).Status.ShouldBe(QuestionStatus.PendingReview);
        (await service.GetDefinitionRevisionsAsync(pending.Id)).Items.ShouldBeEmpty();
        var approved = await service.ApproveAsync(pending.Id, DefinitionReview(pending));
        approved.Status.ShouldBe(QuestionStatus.Approved);
        approved.ApprovedDefinitionRevisionId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Review_Operation_Should_Recover_One_Frozen_Result_And_Reject_Changed_Facts()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var service = GetRequiredService<IQuestionAppService>();
        var pending = await DefinitionPendingAsync(f);
        var review = DefinitionReview(pending);
        var approved = await service.ApproveAsync(pending.Id, review);
        var repeated = await service.ApproveAsync(pending.Id, review);
        repeated.ConcurrencyStamp.ShouldBe(approved.ConcurrencyStamp);
        repeated.ApprovedDefinitionRevisionId.ShouldBe(approved.ApprovedDefinitionRevisionId);
        repeated.ReviewOperation!.Id.ShouldBe(review.OperationId!.Value);
        repeated.ReviewOperation.DefinitionRevisionId.ShouldBe(approved.ApprovedDefinitionRevisionId);
        repeated.ReviewOperation.ReviewTime.ShouldBe(approved.ReviewOperation!.ReviewTime);
        var receipt = await service.GetReviewOperationAsync(pending.Id, review.OperationId.Value);
        receipt!.FromStatus.ShouldBe(QuestionStatus.PendingReview);
        receipt.ToStatus.ShouldBe(QuestionStatus.Approved);
        receipt.RequestHash.ShouldBe(approved.ReviewOperation.RequestHash);
        receipt.ExpectedConcurrencyStamp.ShouldBe(pending.ConcurrencyStamp);
        (await service.GetReviewOperationAsync(pending.Id, Guid.NewGuid())).ShouldBeNull();
        (await service.GetDefinitionRevisionsAsync(pending.Id)).Items.Count.ShouldBe(1);
        (await WithUnitOfWorkAsync(() => Repo<QuestionReview>().CountAsync(x => x.Id == review.OperationId.Value))).ShouldBe(1);
        var conflict = new ReviewQuestionDto
        { OperationId = review.OperationId, ConcurrencyStamp = review.ConcurrencyStamp, Comment = "与原审核不同的理由" };
        (await Should.ThrowAsync<BusinessException>(() => service.ApproveAsync(pending.Id, conflict)))
            .Code.ShouldBe("KnowPulse:Education:ReviewOperationConflict");
        conflict.Comment = review.Comment;
        conflict.ConcurrencyStamp = repeated.ConcurrencyStamp;
        await Should.ThrowAsync<BusinessException>(() => service.ApproveAsync(pending.Id, conflict));
        await Should.ThrowAsync<BusinessException>(() => service.RejectAsync(pending.Id,
            new ReviewQuestionDto { OperationId = review.OperationId, ConcurrencyStamp = review.ConcurrencyStamp, Comment = review.Comment }));
        var other = await DefinitionPendingAsync(f);
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.ApproveAsync(other.Id, review));
        using (User(f.Owner))
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetReviewOperationAsync(pending.Id, review.OperationId.Value));
    }

    [Fact]
    public async Task Old_Approval_Retry_Should_Not_Approve_A_Later_Draft_And_New_Approval_Should_Append_A_Revision()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var service = GetRequiredService<IQuestionAppService>();
        var pending = await DefinitionPendingAsync(f);
        var firstReview = DefinitionReview(pending);
        var approved = await service.ApproveAsync(pending.Id, firstReview);
        var first = await service.GetDefinitionRevisionAsync(approved.Id, approved.ApprovedDefinitionRevisionId!.Value);
        var draft = Draft(approved);
        draft.Stem = "教师修订后的新题干";
        draft.ScoringDefinition = new QuestionScoringDefinition { ReferenceAnswer = "新参考答案" };
        var edited = await GetRequiredService<IQuestionWorkflowAppService>().SaveDraftAsync(approved.Id, draft);
        edited.CurriculumConfirmed.ShouldBeFalse();
        edited = await service.ConfirmCurriculumAsync(edited.Id, Confirm(f, edited));
        edited = await service.SubmitReviewAsync(edited.Id);
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => service.ApproveAsync(edited.Id,
            new ReviewQuestionDto { OperationId = Guid.NewGuid(), ConcurrencyStamp = firstReview.ConcurrencyStamp, Comment = firstReview.Comment }));
        var recovered = await service.ApproveAsync(edited.Id, firstReview);
        recovered.Status.ShouldBe(QuestionStatus.PendingReview);
        recovered.Stem.ShouldBe(draft.Stem);
        recovered.ReviewOperation!.DefinitionRevisionId.ShouldBe(first.Id);
        (await service.GetDefinitionRevisionsAsync(edited.Id)).Items.Count.ShouldBe(1);
        var second = await service.ApproveAsync(edited.Id, DefinitionReview(recovered));
        second.ApprovedDefinitionRevisionId.ShouldNotBe(first.Id);
        var revisions = (await service.GetDefinitionRevisionsAsync(edited.Id)).Items;
        revisions.Select(x => x.RevisionNumber).ShouldBe(new[] { 2, 1 });
        revisions[0].Definition.Stem.ShouldBe(draft.Stem);
        revisions[0].Definition.ScoringDefinition!.ReferenceAnswer.ShouldBe("新参考答案");
        revisions[1].Definition.Stem.ShouldBe(first.Definition.Stem);
        revisions[1].Definition.ScoringDefinition!.ReferenceAnswer.ShouldBe(first.Definition.ScoringDefinition!.ReferenceAnswer);
        revisions[1].DefinitionHash.ShouldBe(first.DefinitionHash);
    }

    [Theory]
    [InlineData(QuestionReviewDecision.ReturnForRevision)]
    [InlineData(QuestionReviewDecision.Reject)]
    public async Task Return_And_Reject_Receipts_Should_Remain_Frozen_After_Resubmission(QuestionReviewDecision decision)
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var service = GetRequiredService<IQuestionAppService>();
        var workflow = GetRequiredService<IQuestionWorkflowAppService>();
        var pending = await DefinitionPendingAsync(f);
        var operation = new DecideQuestionReviewDto
        { OperationId = Guid.NewGuid(), ConcurrencyStamp = pending.ConcurrencyStamp, Decision = decision, Comment = "请补充审核依据" };
        var decided = await workflow.DecideReviewAsync(pending.Id, operation);
        var expectedStatus = decision == QuestionReviewDecision.Reject ? QuestionStatus.Rejected : QuestionStatus.ReturnedForRevision;
        decided.Status.ShouldBe(expectedStatus);
        decided.ApprovedDefinitionRevisionId.ShouldBeNull();
        var edited = await workflow.SaveDraftAsync(decided.Id, Draft(decided));
        edited = await service.SubmitReviewAsync(edited.Id);
        var repeated = await workflow.DecideReviewAsync(edited.Id, operation);
        repeated.Status.ShouldBe(QuestionStatus.PendingReview);
        repeated.ReviewOperation!.ToStatus.ShouldBe(expectedStatus);
        repeated.ReviewOperation.DefinitionRevisionId.ShouldBeNull();
        repeated.ReviewOperation.ReviewTime.ShouldBe(decided.ReviewOperation!.ReviewTime);
        (await service.GetReviewOperationAsync(edited.Id, operation.OperationId!.Value))!.ToStatus.ShouldBe(expectedStatus);
        (await service.GetDefinitionRevisionsAsync(edited.Id)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Approved_Definition_Should_Freeze_Original_Curriculum_And_Shared_Source_And_Remain_Owner_History()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Owner, "学校确认资料标题");
        Guid binding;
        Guid share;
        using (User(f.Owner))
        {
            binding = await BindAsync(source, f.MappingA);
            share = (await _sharing.ShareVersionAsync(Share(f, source))).Id;
        }
        using var reader = User(f.Reader);
        var service = GetRequiredService<IQuestionAppService>();
        var pending = await DefinitionPendingAsync(f, input => input.Sources = [new()
        {
            KnowledgeItemId = source.KnowledgeItemId, KnowledgeVersionId = source.KnowledgeVersionId, ChunkId = source.Id,
            SchoolKnowledgeShareId = share, CurriculumBindingIds = [binding], TextbookKnowledgePointMappingIds = [f.MappingA],
            SourceExcerpt = "明确的引用摘要", SourceHash = source.ContentHash
        }]);
        var review = DefinitionReview(pending);
        var approved = await service.ApproveAsync(pending.Id, review);
        var revision = await service.GetDefinitionRevisionAsync(approved.Id, approved.ApprovedDefinitionRevisionId!.Value);
        revision.Definition.Curriculum!.Mappings.Select(x => x.MappingId).OrderBy(x => x)
            .ShouldBe(new[] { f.MappingA, f.MappingB }.OrderBy(x => x));
        revision.Definition.CurriculumPointWeights.Sum(x => x.Score).ShouldBe(10);
        revision.Definition.CurriculumConfirmed.ShouldBeTrue();
        revision.Definition.CurriculumConfirmedByUserId.ShouldBe(f.Reader);
        revision.Definition.Options.Single(x => x.OptionKey == "A").Content.ShouldBe("甲");
        revision.Definition.Answers.Single().Value.ShouldBe("A");
        revision.Definition.Analysis.ShouldBe("原解析");
        revision.Definition.ScoringDefinition!.ReferenceAnswer.ShouldBe("甲为正确答案，依据已确认教材范围。");
        revision.Definition.Sources.Single().SchoolKnowledgeShareId.ShouldBe(share);
        revision.Definition.Sources.Single().SourceExcerpt.ShouldBe("明确的引用摘要");
        revision.Definition.CurriculumSources.Single().CurriculumBindingIds.ShouldBe(new[] { binding });
        var stored = await WithUnitOfWorkAsync(() => Repo<QuestionDefinitionRevision>().GetAsync(revision.Id));
        stored.DefinitionJson.ShouldNotContain("approvedDefinitionRevisionId");
        stored.DefinitionJson.ShouldNotContain("\"reviews\"");
        stored.DefinitionJson.ShouldNotContain("\"status\"");
        var saved = await GetRequiredService<IExamAppService>().SavePaperAsync(Paper(f, approved.Id));
        using (User(f.Owner))
        {
            await EndBindingAsync(binding);
            await _sharing.RevokeAsync(share);
        }
        await Should.ThrowAsync<AbpAuthorizationException>(() => GetRequiredService<IExamAppService>().SavePaperAsync(Paper(f, approved.Id)));
        // A deleted current aggregate must not delete or reauthorize its frozen definition history.
        await WithUnitOfWorkAsync(() => Repo<Question>().DeleteAsync(approved.Id, autoSave: true));
        var history = await service.GetDefinitionRevisionAsync(approved.Id, revision.Id);
        history.DefinitionHash.ShouldBe(revision.DefinitionHash);
        (await service.GetDefinitionRevisionsAsync(approved.Id)).Items.Single().Id.ShouldBe(revision.Id);
        (await service.GetReviewOperationAsync(approved.Id, review.OperationId!.Value))!.DefinitionRevisionId.ShouldBe(revision.Id);
        (await GetRequiredService<IExamAppService>().GetSavedPaperAsync(saved.Id)).Questions.Single()
            .QuestionDefinitionRevisionId.ShouldBe(revision.Id);
        using (User(f.Owner))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetDefinitionRevisionAsync(approved.Id, revision.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetDefinitionRevisionsAsync(approved.Id));
        }
        using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetDefinitionRevisionAsync(approved.Id, revision.Id));
        (await WithUnitOfWorkAsync(() => Repo<QuestionDefinitionRevision>().GetAsync(revision.Id))).DefinitionJson.ShouldBe(stored.DefinitionJson);
    }

    [Fact]
    public async Task Subjective_Definition_And_Paper_Should_Freeze_Actual_Scoring_Points_And_Reference_Answer()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var service = GetRequiredService<IQuestionAppService>();
        var pending = await DefinitionPendingAsync(f, input =>
        {
            input.Type = QuestionType.Subjective;
            input.Stem = "请解释第一章与第二章的关系。";
            input.Options = [];
            input.Answers = [];
            input.ScoringDefinition = new QuestionScoringDefinition
            {
                ReferenceAnswer = "第一章建立基础，第二章给出应用。",
                ScoringPoints =
                [
                    new() { Id = Guid.NewGuid(), DisplayOrder = 1, Description = "正确说明基础", Score = 6, TextbookKnowledgePointMappingId = f.MappingA },
                    new() { Id = Guid.NewGuid(), DisplayOrder = 2, Description = "正确说明应用", Score = 4, TextbookKnowledgePointMappingId = f.MappingB }
                ]
            };
        });
        var approved = await service.ApproveAsync(pending.Id, DefinitionReview(pending));
        var revision = await service.GetDefinitionRevisionAsync(approved.Id, approved.ApprovedDefinitionRevisionId!.Value);
        var saved = await GetRequiredService<IExamAppService>().SavePaperAsync(Paper(f, approved.Id));
        var frozen = saved.Questions.Single();
        frozen.QuestionDefinitionRevisionId.ShouldBe(revision.Id);
        frozen.ScoringDefinition!.ToCanonicalJson().ShouldBe(revision.Definition.ScoringDefinition!.ToCanonicalJson());
        frozen.ScoringDefinition.ScoringPoints.Sum(x => x.Score).ShouldBe(frozen.Score);
        var fresh = await service.GetAsync(approved.Id);
        var draft = Draft(fresh);
        draft.ScoringDefinition = QuestionScoringDefinition.Parse(fresh.ScoringDefinition!.ToCanonicalJson());
        draft.ScoringDefinition.ReferenceAnswer = "教师改写参考答案。";
        var edited = await GetRequiredService<IQuestionWorkflowAppService>().SaveDraftAsync(approved.Id, draft);
        edited.Status.ShouldBe(QuestionStatus.Draft);
        edited.CurriculumConfirmed.ShouldBeFalse();
        await Should.ThrowAsync<BusinessException>(() => GetRequiredService<IExamAppService>().SavePaperAsync(Paper(f, approved.Id)));
        var history = await GetRequiredService<IExamAppService>().GetSavedPaperAsync(saved.Id);
        history.Questions.Single().ScoringDefinition!.ReferenceAnswer.ShouldBe("第一章建立基础，第二章给出应用。");
        (await service.GetDefinitionRevisionAsync(approved.Id, revision.Id)).DefinitionHash.ShouldBe(revision.DefinitionHash);
    }

    [Fact]
    public async Task Approved_Definition_Should_Detect_Changed_Child_Answer_Even_When_Current_Status_Is_Still_Approved()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var service = GetRequiredService<IQuestionAppService>();
        var pending = await DefinitionPendingAsync(f);
        var approved = await service.ApproveAsync(pending.Id, DefinitionReview(pending));
        var revision = await service.GetDefinitionRevisionAsync(approved.Id, approved.ApprovedDefinitionRevisionId!.Value);
        await WithUnitOfWorkAsync(async () =>
        {
            var answer = await Repo<QuestionAnswer>().GetAsync(approved.Answers.Single().Id);
            await Repo<QuestionAnswer>().DeleteAsync(answer, autoSave: true);
            await Repo<QuestionAnswer>().InsertAsync(new QuestionAnswer(Guid.NewGuid(), null, approved.Id, "B", 1), autoSave: true);
        });
        (await service.GetAsync(approved.Id)).Status.ShouldBe(QuestionStatus.Approved);
        (await Should.ThrowAsync<BusinessException>(() => GetRequiredService<IExamAppService>().SavePaperAsync(Paper(f, approved.Id))))
            .Code.ShouldBe("KnowPulse:Education:FormalDefinitionRequired");
        (await service.GetDefinitionRevisionAsync(approved.Id, revision.Id)).Definition.Answers.Single().Value.ShouldBe("A");
    }

    [Fact]
    public async Task Equivalent_Manual_Source_Reordering_Should_Not_Change_The_Approved_Definition_Hash()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var service = GetRequiredService<IQuestionAppService>();
        var pending = await DefinitionPendingAsync(f, input => input.Sources =
        [
            new() { SourceTitle = "乙来源", SourceExcerpt = "摘要乙", ManualSourceDescription = "教师人工说明", SourceHash = "same" },
            new() { SourceTitle = "甲来源", SourceExcerpt = "摘要甲", ManualSourceDescription = "教师人工说明", SourceHash = "same" }
        ]);
        var approved = await service.ApproveAsync(pending.Id, DefinitionReview(pending));
        var revision = await service.GetDefinitionRevisionAsync(approved.Id, approved.ApprovedDefinitionRevisionId!.Value);
        revision.Definition.Sources.Select(x => x.SourceTitle).ShouldBe(new[] { "乙来源", "甲来源" }.OrderBy(x => x, StringComparer.Ordinal));
        await WithUnitOfWorkAsync(async () =>
        {
            foreach (var source in (await Repo<QuestionSource>().GetListAsync(x => x.QuestionId == approved.Id)))
                await Repo<QuestionSource>().DeleteAsync(source, autoSave: true);
            foreach (var source in revision.Definition.Sources.AsEnumerable().Reverse())
                await Repo<QuestionSource>().InsertAsync(new QuestionSource(Guid.NewGuid(), null, approved.Id, null, null, null,
                    source.SourceTitle, source.SourceExcerpt, source.SourceHash, source.ManualSourceDescription), autoSave: true);
        });
        (await GetRequiredService<IExamAppService>().SavePaperAsync(Paper(f, approved.Id))).Questions.Single()
            .QuestionDefinitionRevisionId.ShouldBe(revision.Id);
        (await service.GetDefinitionRevisionAsync(approved.Id, revision.Id)).DefinitionHash.ShouldBe(revision.DefinitionHash);
    }

    [Fact]
    public async Task Legacy_Unstructured_Review_Should_Keep_Compatibility_While_Explicit_Rules_Require_Curriculum()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var service = GetRequiredService<IQuestionAppService>();
        var input = Manual(f);
        input.CurriculumScope = null;
        var legacy = await service.CreateAsync(input);
        legacy = await service.SubmitReviewAsync(legacy.Id);
        legacy = await service.ApproveAsync(legacy.Id, new ReviewQuestionDto());
        legacy.Status.ShouldBe(QuestionStatus.Approved);
        legacy.ApprovedDefinitionRevisionId.ShouldBeNull();
        (await WithUnitOfWorkAsync(() => Repo<Question>().GetAsync(legacy.Id))).PersonalOwnerUserId.ShouldBeNull();
        var legacyPaper = Paper(f, legacy.Id);
        legacyPaper.CurriculumScope = null;
        (await GetRequiredService<IExamAppService>().PreviewPaperAsync(legacyPaper)).Questions.Single().Stem.ShouldBe(legacy.Stem);
        var adoptedLegacyRevision = await service.CreateAsync(input);
        adoptedLegacyRevision = await service.SubmitReviewAsync(adoptedLegacyRevision.Id);
        adoptedLegacyRevision = await service.ApproveAsync(adoptedLegacyRevision.Id, DefinitionReview(adoptedLegacyRevision));
        var oldRevisionId = adoptedLegacyRevision.ApprovedDefinitionRevisionId!.Value;
        var draft = Draft(adoptedLegacyRevision);
        draft.Stem = "旧企业客户端修改后的题干";
        adoptedLegacyRevision = await GetRequiredService<IQuestionWorkflowAppService>().SaveDraftAsync(adoptedLegacyRevision.Id, draft);
        adoptedLegacyRevision = await service.SubmitReviewAsync(adoptedLegacyRevision.Id);
        adoptedLegacyRevision = await service.ApproveAsync(adoptedLegacyRevision.Id, new ReviewQuestionDto());
        adoptedLegacyRevision.ApprovedDefinitionRevisionId.ShouldNotBe(oldRevisionId);
        (await service.GetDefinitionRevisionsAsync(adoptedLegacyRevision.Id)).Items.Count.ShouldBe(2);
        legacyPaper = Paper(f, adoptedLegacyRevision.Id);
        legacyPaper.CurriculumScope = null;
        (await GetRequiredService<IExamAppService>().PreviewPaperAsync(legacyPaper)).Questions.Single()
            .QuestionDefinitionRevisionId.ShouldBe(adoptedLegacyRevision.ApprovedDefinitionRevisionId);
        input.ScoringDefinition = new QuestionScoringDefinition { ReferenceAnswer = "仅新课程可发布显式评分定义" };
        var structured = await service.CreateAsync(input);
        structured = await service.SubmitReviewAsync(structured.Id);
        (await Should.ThrowAsync<BusinessException>(() => service.ApproveAsync(structured.Id, DefinitionReview(structured))))
            .Code.ShouldBe("KnowPulse:Education:CurriculumConfirmationRequired");
        (await service.GetDefinitionRevisionsAsync(structured.Id)).Items.ShouldBeEmpty();
    }

    private async Task<QuestionDto> DefinitionPendingAsync(Fixture f, Action<CreateQuestionDto>? configure = null)
    {
        var input = Manual(f);
        input.ScoringDefinition = new QuestionScoringDefinition { ReferenceAnswer = "甲为正确答案，依据已确认教材范围。" };
        configure?.Invoke(input);
        var service = GetRequiredService<IQuestionAppService>();
        var question = await service.CreateAsync(input);
        question = await service.ConfirmCurriculumAsync(question.Id, Confirm(f, question));
        return await service.SubmitReviewAsync(question.Id);
    }

    private static ReviewQuestionDto DefinitionReview(QuestionDto question) => new()
    { OperationId = Guid.NewGuid(), ConcurrencyStamp = question.ConcurrencyStamp, Comment = "教师确认正式定义" };
}
