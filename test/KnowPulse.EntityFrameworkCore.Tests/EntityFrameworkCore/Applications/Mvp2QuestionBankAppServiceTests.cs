using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.QuestionBank;
using Shouldly;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class Mvp2QuestionBankAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IQuestionAppService _questionAppService;
    private readonly IQuestionWorkflowAppService _questionWorkflowAppService;
    private readonly IRepository<QuestionReview, Guid> _reviewRepository;

    public Mvp2QuestionBankAppServiceTests()
    {
        _questionAppService = GetRequiredService<IQuestionAppService>();
        _questionWorkflowAppService = GetRequiredService<IQuestionWorkflowAppService>();
        _reviewRepository = GetRequiredService<IRepository<QuestionReview, Guid>>();
    }

    [Fact]
    public void Question_App_Service_Should_Remain_Resolvable_As_HttpApi_Controller()
    {
        GetRequiredService<QuestionAppService>().ShouldNotBeNull();
    }

    [Fact]
    public async Task Manual_Draft_Creation_Should_Own_Source_On_Server()
    {
        var input = CreateSingleChoiceInput("Server owned manual source", QuestionCreationSource.AiGenerated);

        var question = await _questionWorkflowAppService.CreateManualDraftAsync(input);

        question.Status.ShouldBe(QuestionStatus.Draft);
        question.CreationSource.ShouldBe(QuestionCreationSource.Manual);

        var saved = await _questionWorkflowAppService.SaveDraftAsync(
            question.Id,
            CreateSingleChoiceInput("Server owned manual source updated", QuestionCreationSource.AiGenerated));

        saved.CreationSource.ShouldBe(QuestionCreationSource.Manual);
    }

    [Fact]
    public async Task Returning_Question_Should_Require_Review_Reason()
    {
        var question = await _questionWorkflowAppService.CreateManualDraftAsync(CreateSingleChoiceInput("Return reason required"));
        await _questionAppService.SubmitReviewAsync(question.Id);

        var exception = await Should.ThrowAsync<Volo.Abp.BusinessException>(() =>
            _questionWorkflowAppService.DecideReviewAsync(question.Id, new DecideQuestionReviewDto
            {
                Decision = QuestionReviewDecision.ReturnForRevision,
                Comment = " "
            }));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.QuestionReviewCommentRequired);
    }

    [Fact]
    public async Task Rejecting_Question_Should_Require_Review_Reason()
    {
        var question = await _questionWorkflowAppService.CreateManualDraftAsync(CreateSingleChoiceInput("Reject reason required"));
        await _questionAppService.SubmitReviewAsync(question.Id);

        var exception = await Should.ThrowAsync<Volo.Abp.BusinessException>(() =>
            _questionWorkflowAppService.DecideReviewAsync(question.Id, new DecideQuestionReviewDto
            {
                Decision = QuestionReviewDecision.Reject
            }));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.QuestionReviewCommentRequired);
    }

    [Fact]
    public async Task Worklists_Should_Map_User_Tasks_To_Server_Side_Statuses()
    {
        var draft = await _questionWorkflowAppService.CreateManualDraftAsync(CreateSingleChoiceInput("Worklist draft"));
        var pending = await _questionWorkflowAppService.CreateManualDraftAsync(CreateSingleChoiceInput("Worklist pending"));
        await _questionWorkflowAppService.SubmitAsync(pending.Id);
        var formal = await _questionWorkflowAppService.CreateManualDraftAsync(CreateSingleChoiceInput("Worklist formal"));
        await _questionWorkflowAppService.SubmitAsync(formal.Id);
        await _questionWorkflowAppService.DecideReviewAsync(formal.Id, new DecideQuestionReviewDto
        {
            Decision = QuestionReviewDecision.Approve,
            Comment = "Ready for formal library"
        });

        var drafts = await _questionAppService.GetListAsync(new GetQuestionListInput
        {
            Worklist = QuestionWorklistKind.Drafts,
            MaxResultCount = 100
        });
        var reviews = await _questionAppService.GetListAsync(new GetQuestionListInput
        {
            Worklist = QuestionWorklistKind.ReviewQueue,
            MaxResultCount = 100
        });
        var formalQuestions = await _questionAppService.GetListAsync(new GetQuestionListInput
        {
            Worklist = QuestionWorklistKind.Formal,
            MaxResultCount = 100
        });

        drafts.Items.Select(x => x.Id).ShouldContain(draft.Id);
        drafts.Items.Select(x => x.Id).ShouldNotContain(pending.Id);
        reviews.Items.Select(x => x.Id).ShouldContain(pending.Id);
        reviews.Items.Select(x => x.Id).ShouldNotContain(formal.Id);
        formalQuestions.Items.Select(x => x.Id).ShouldContain(formal.Id);
        formalQuestions.Items.Select(x => x.Id).ShouldNotContain(draft.Id);
    }

    [Fact]
    public async Task Should_Create_Update_Filter_Disable_And_Archive_Question()
    {
        var question = await _questionAppService.CreateAsync(CreateSingleChoiceInput("MVP2 filtering"));
        question.Status.ShouldBe(QuestionStatus.Draft);
        question.CreationSource.ShouldBe(QuestionCreationSource.Manual);
        question.Options.Count.ShouldBe(2);
        question.Answers.Single().Value.ShouldBe("A");

        var updated = await _questionAppService.UpdateAsync(question.Id, new UpdateQuestionDto
        {
            Type = QuestionType.SingleChoice,
            Difficulty = QuestionDifficulty.Hard,
            CreationSource = QuestionCreationSource.Manual,
            Title = "MVP2 updated filtering",
            Stem = "Updated stem",
            SecurityLevel = 2,
            Tags = "policy, updated",
            Analysis = "Updated analysis",
            Options =
            {
                new CreateUpdateQuestionOptionDto { OptionKey = "A", Content = "Updated A", DisplayOrder = 1 },
                new CreateUpdateQuestionOptionDto { OptionKey = "B", Content = "Updated B", DisplayOrder = 2 }
            },
            Answers =
            {
                new CreateUpdateQuestionAnswerDto { Value = "B", DisplayOrder = 1 }
            },
            KnowledgePointNames = { "制度基础", "审核流" },
            Sources =
            {
                new CreateUpdateQuestionSourceDto { ManualSourceDescription = "Manual source" }
            }
        });

        updated.Difficulty.ShouldBe(QuestionDifficulty.Hard);
        updated.Answers.Single().Value.ShouldBe("B");
        updated.KnowledgePoints.Select(x => x.Name).ShouldContain("审核流");

        var sources = await _questionAppService.GetSourcesAsync(question.Id);
        sources.Items.Single().ManualSourceDescription.ShouldBe("Manual source");

        var filtered = await _questionAppService.GetListAsync(new GetQuestionListInput
        {
            Keyword = "updated",
            Difficulty = QuestionDifficulty.Hard,
            KnowledgePoint = "审核",
            MaxResultCount = 20
        });
        filtered.Items.Select(x => x.Id).ShouldContain(question.Id);

        var disabled = await _questionAppService.DisableAsync(question.Id);
        disabled.Status.ShouldBe(QuestionStatus.Disabled);

        var archived = await _questionAppService.ArchiveAsync(question.Id);
        archived.Status.ShouldBe(QuestionStatus.Archived);
    }

    [Fact]
    public async Task Review_Flow_Should_Require_Submission_And_Record_Audit_Trail()
    {
        var aiQuestion = await _questionAppService.CreateAsync(CreateSingleChoiceInput("AI candidate", QuestionCreationSource.AiGenerated));
        aiQuestion.Status.ShouldBe(QuestionStatus.Draft);

        await Should.ThrowAsync<Volo.Abp.BusinessException>(
            () => _questionAppService.ApproveAsync(aiQuestion.Id, new ReviewQuestionDto { Comment = "Should fail" }));

        var submitted = await _questionAppService.SubmitReviewAsync(aiQuestion.Id);
        submitted.Status.ShouldBe(QuestionStatus.PendingReview);

        var approved = await _questionAppService.ApproveAsync(aiQuestion.Id, new ReviewQuestionDto
        {
            Comment = "Approved after human review"
        });
        approved.Status.ShouldBe(QuestionStatus.Approved);
        approved.IsUsableForExam.ShouldBeTrue();
        approved.Reviews.Count.ShouldBeGreaterThanOrEqualTo(2);
        approved.Reviews.Select(x => x.Action).ShouldContain(QuestionReviewAction.Submitted);
        approved.Reviews.Select(x => x.Action).ShouldContain(QuestionReviewAction.Approved);

        var rejectedQuestion = await _questionAppService.CreateAsync(CreateSingleChoiceInput("Rejected AI candidate", QuestionCreationSource.AiGenerated));
        await _questionAppService.SubmitReviewAsync(rejectedQuestion.Id);
        var rejected = await _questionAppService.RejectAsync(rejectedQuestion.Id, new ReviewQuestionDto { Comment = "Rejected" });
        rejected.Status.ShouldBe(QuestionStatus.Rejected);
        rejected.IsUsableForExam.ShouldBeFalse();
        rejected.Reviews.Select(x => x.Action).ShouldContain(QuestionReviewAction.Rejected);

        var formalQuestions = await _questionAppService.GetFormalListAsync(new GetQuestionListInput
        {
            Status = QuestionStatus.Rejected,
            OnlyUsableForExam = true,
            MaxResultCount = 20
        });
        formalQuestions.Items.Select(x => x.Id).ShouldContain(aiQuestion.Id);
        formalQuestions.Items.Select(x => x.Id).ShouldNotContain(rejectedQuestion.Id);

        var reviews = await _questionAppService.GetReviewsAsync(aiQuestion.Id);
        reviews.Items.Select(x => x.Action).ShouldContain(QuestionReviewAction.Submitted);
        reviews.Items.Select(x => x.Action).ShouldContain(QuestionReviewAction.Approved);

        await _questionAppService.DisableAsync(aiQuestion.Id);
        formalQuestions = await _questionAppService.GetFormalListAsync(new GetQuestionListInput
        {
            OnlyUsableForExam = true,
            MaxResultCount = 20
        });
        formalQuestions.Items.Select(x => x.Id).ShouldNotContain(aiQuestion.Id);

        var reviewCount = await WithUnitOfWorkAsync(async () =>
        {
            var reviewQueryable = await _reviewRepository.GetQueryableAsync();
            return reviewQueryable.Count(x => x.QuestionId == aiQuestion.Id);
        });
        reviewCount.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Returned_Question_Should_Be_Editable_And_Submittable_Again()
    {
        var question = await _questionAppService.CreateAsync(CreateSingleChoiceInput("Return candidate"));
        await _questionAppService.SubmitReviewAsync(question.Id);

        var returned = await _questionAppService.ReturnForRevisionAsync(question.Id, new ReviewQuestionDto
        {
            Comment = "Need more precise wording"
        });
        returned.Status.ShouldBe(QuestionStatus.ReturnedForRevision);

        var edited = await _questionAppService.UpdateAsync(question.Id, CreateSingleChoiceInput("Return candidate updated"));
        edited.Status.ShouldBe(QuestionStatus.Draft);

        var submittedAgain = await _questionAppService.SubmitReviewAsync(question.Id);
        submittedAgain.Status.ShouldBe(QuestionStatus.PendingReview);
    }

    private static UpdateQuestionDto CreateSingleChoiceInput(
        string title,
        QuestionCreationSource source = QuestionCreationSource.Manual)
    {
        return new UpdateQuestionDto
        {
            Type = QuestionType.SingleChoice,
            Difficulty = QuestionDifficulty.Normal,
            CreationSource = source,
            Title = title,
            Stem = $"{title} stem",
            SecurityLevel = 1,
            Tags = "mvp2, question-bank",
            Analysis = "Because A is correct.",
            Options =
            {
                new CreateUpdateQuestionOptionDto { OptionKey = "A", Content = "Correct option", DisplayOrder = 1 },
                new CreateUpdateQuestionOptionDto { OptionKey = "B", Content = "Wrong option", DisplayOrder = 2 }
            },
            Answers =
            {
                new CreateUpdateQuestionAnswerDto { Value = "A", DisplayOrder = 1 }
            },
            KnowledgePointNames = { "制度基础" },
            Sources =
            {
                new CreateUpdateQuestionSourceDto { ManualSourceDescription = "Manual source" }
            }
        };
    }
}
