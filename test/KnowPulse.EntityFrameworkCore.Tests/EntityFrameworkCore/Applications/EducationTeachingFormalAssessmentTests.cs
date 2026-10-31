using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationTeachingFormalAssessmentAppService Formal => GetRequiredService<IEducationTeachingFormalAssessmentAppService>();
    private sealed record FormalFixture(GradingFixture Source, Guid PlanId, Guid PublicationId, Guid ActivityId,
        EducationAssessmentTaskRevisionDto Task, Guid RecipientId);
    private async Task<FormalFixture> FormalFixtureAsync(int endsInSeconds = 600, bool bind = true, bool open = true)
    {
        var (f, report) = await PlanSourceAsync(); var planId = Guid.NewGuid(); Guid activityId, publicationId;
        using (User(f.Source.School.Reader)) {
            var draft = await PlanDraftAsync(f, report); draft.Content = draft.Content with { StartsAtUtc = DateTime.UtcNow.AddMinutes(-2) };
            await Plans.SaveDraftAsync(planId, draft); publicationId = (await ConfirmAndPublishPlanAsync(planId)).Id;
            activityId = (await Activities.GetActivityProgressAsync(planId)).Activities.Single(x => x.StepOrder == 3).Id;
        }
        if (open) using (User(f.Source.Student.UserId)) await Plans.OpenMyAsync(planId, publicationId);
        EducationAssessmentTaskRevisionDto published;
        using (User(f.Source.School.Reader)) {
            var school = f.Source.School; var scope = new EducationCurriculumScopeDto { TeacherTextbookSelectionId = school.ReaderSelection, TextbookKnowledgePointMappingIds = [school.MappingA] };
            var input = Manual(school); input.CurriculumScope = scope; input.Stem = "新题独立复测 " + Guid.NewGuid();
            var questions = GetRequiredService<IQuestionAppService>(); var question = await questions.CreateAsync(input);
            var confirm = Confirm(school, question); confirm.Scope = scope;
            confirm.PointWeights = [new() { TextbookKnowledgePointMappingId = school.MappingA, Weight = 1, Score = 10 }];
            question = await questions.ConfirmCurriculumAsync(question.Id, confirm); question = await questions.SubmitReviewAsync(question.Id);
            question = await questions.ApproveAsync(question.Id, Review(question));
            var paperInput = Paper(school, question.Id); paperInput.CurriculumScope = scope;
            var paper = await GetRequiredService<IExamAppService>().SavePaperAsync(paperInput);
            var taskInput = AssessmentInput(school, paper.Id); taskInput.Scope = scope; taskInput.MaxAttempts = 1;
            taskInput.DurationMinutes = 1; taskInput.EndsAtUtc = DateTime.UtcNow.AddSeconds(endsInSeconds);
            taskInput.RecipientScope = EducationAssessmentRecipientScope.SelectedStudents; taskInput.StudentUserIds = [f.Source.Student.UserId];
            var task = await Assessments.CreateDraftAsync(taskInput);
            published = await Assessments.PublishAsync(task.Id, AssessmentPublish(await Assessments.PreviewRecipientsAsync(task.Id)));
            if (bind) await Formal.BindFormalAssessmentAsync(activityId, FormalBind(await Formal.PreviewFormalAssessmentAsync(activityId, task.Id)));
        }
        return new(f, planId, publicationId, activityId, published, published.Recipients.Single().Id);
    }
    private static BindEducationFormalAssessmentDto FormalBind(EducationFormalAssessmentPreviewDto preview) => new() {
        OperationId = Guid.NewGuid(), TaskId = preview.TaskId, ExpectedTaskRevisionId = preview.TaskRevisionId,
        ExpectedPaperSnapshotVersion = preview.PaperSnapshotVersion, ExpectedActivityRevisionId = preview.ExpectedActivityRevisionId,
        ActivityConcurrencyStamp = preview.ActivityConcurrencyStamp, PreviewFingerprint = preview.PreviewFingerprint,
        ConfirmOriginalRelationships = true, ConfirmNewAssessmentRequirements = true, ConfirmExactPlanContainsNoHints = true };
    private static ReviewEducationFormalAssessmentDto FormalReview(EducationFormalAssessmentDetailsDto details, bool enabled, EducationLearningExposureUse use) => new() {
        OperationId = Guid.NewGuid(), ExpectedReviewId = details.CurrentReview?.Id, ConcurrencyStamp = details.ConcurrencyStamp,
        IsEnabled = enabled, PlanContextUse = use, ConfirmExactContext = true, Reason = "重新核对该次提供的方案与冻结试题" };

    [Fact]
    public async Task Formal_Activity_Should_Use_Actual_Submission_Then_Existing_Grading_And_Unified_Result_Release()
    {
        var f = await FormalFixtureAsync(15); EducationAssessmentSubmissionDto submission;
        using (User(f.Source.Source.Student.UserId)) {
            var tasks = await Assessments.GetMyStudentTaskAsync(f.RecipientId); tasks.CanStart.ShouldBeTrue();
            var attempt = await Assessments.StartOrResumeAsync(f.RecipientId, new() { OperationId = Guid.NewGuid() });
            var question = attempt.Paper.Questions.Single();
            var saved = await Assessments.SaveResponseAsync(attempt.Id, new() { OperationId = Guid.NewGuid(), PaperQuestionId = question.Id,
                PaperSnapshotVersion = attempt.PaperSnapshotVersion, ExpectedResponseSetVersion = attempt.ResponseSetVersion,
                Response = new() { Type = QuestionType.SingleChoice, SelectedOptionKeys = ["A"] } });
            submission = await SubmitReviewedAsync(attempt.Id, new() { OperationId = Guid.NewGuid(), PaperSnapshotVersion = attempt.PaperSnapshotVersion,
                ExpectedResponseSetVersion = saved.ResultResponseSetVersion, ConfirmSavedOnly = true });
            var progress = (await Activities.GetMyActivitiesAsync(f.PlanId, f.PublicationId)).Activities.Single(x => x.Id == f.ActivityId);
            progress.State.ShouldBe(EducationTeachingActivityState.AssessmentSealed); progress.StudentRevisionNumber.ShouldBe(0);
            progress.FormalAssessment!.SealedAtUtc.ShouldBe(submission.SealedAtUtc); progress.FormalAssessment.CanOpen.ShouldBeTrue();
            (await Grades.GetMyResultAsync(f.RecipientId)).CurrentResultVisibility.ShouldBe(ExamResultVisibility.Hidden);
        }
        var wait = f.Task.EndsAtUtc - DateTime.UtcNow; if (wait > TimeSpan.Zero) await Task.Delay(wait + TimeSpan.FromMilliseconds(100));
        using (User(f.Source.Source.School.Reader)) {
            await GetRequiredService<EducationAssessmentGradingProcessor>().InitializeAsync(submission.Id);
            var gradeId = (await Grades.GetSubmissionForGradingAsync(submission.Id)).Questions.Single().Grade.Id;
            var contexts = await Evidence.GetExposureContextsAsync(gradeId, new());
            var context = contexts.Items.Single(x => x.Source == EducationQuestionExposureSource.FormalAssessmentPlan);
            context.ReviewActivityId.ShouldBe(f.ActivityId); context.CanReview.ShouldBeTrue(); context.Use.ShouldBe(EducationLearningExposureUse.ConfirmedNoHints);
            JsonSerializer.Serialize((await Formal.GetFormalAssessmentAsync(f.ActivityId))!.Preview).ShouldNotContain("PRIVATE-PLAN-NOTES");
            var closure = await Grades.CloseTaskAsync(f.Task.TaskId, CloseInput(await Grades.PreviewFinalizationAsync(f.Task.TaskId)));
            var release = ReleaseInput(await Grades.GetGradingOverviewAsync(f.Task.TaskId), closure.Id, ExamResultVisibility.ScoreOnly);
            release.ScoreOpensAtUtc = DateTime.UtcNow; release.ReviewOpensAtUtc = null;
            await Grades.PublishResultsAsync(f.Task.TaskId, release);
        }
        using (User(f.Source.Source.Student.UserId)) {
            var result = await Grades.GetMyResultAsync(f.RecipientId); result.CurrentResultVisibility.ShouldBe(ExamResultVisibility.ScoreOnly);
            result.TotalScore.ShouldBe(10); result.Questions.ShouldBeEmpty();
        }
        using (User(f.Source.Source.School.Reader)) {
            var overview = await Grades.GetGradingOverviewAsync(f.Task.TaskId);
            var review = ReleaseInput(overview, overview.ClosureId!.Value, ExamResultVisibility.FullReview);
            review.ScoreOpensAtUtc = review.ReviewOpensAtUtc = DateTime.UtcNow; review.Reason = "确认对该次正式结果开放逐题讲评";
            await Grades.PublishResultsAsync(f.Task.TaskId, review);
        }
        using (User(f.Source.Source.Student.UserId)) (await Grades.GetMyResultAsync(f.RecipientId)).Questions.Single().CorrectAnswers.ShouldContain("A");
        using (User(f.Source.Source.School.Reader)) {
            var gradeId = (await Grades.GetSubmissionForGradingAsync(submission.Id)).Questions.Single().Grade.Id;
            var contexts = await Evidence.GetExposureContextsAsync(gradeId, new());
            contexts.Items.Single(x => x.Source == EducationQuestionExposureSource.AssessmentReview).Use.ShouldBe(EducationLearningExposureUse.Confirmed);
            contexts.Items.Single(x => x.Source == EducationQuestionExposureSource.FormalAssessmentPlan).Use.ShouldBe(EducationLearningExposureUse.ConfirmedNoHints);
        }
    }

    [Fact]
    public async Task Formal_Binding_And_Review_Should_Recover_Original_Operations_And_Reject_Stale_Or_Changed_Commands()
    {
        var f = await FormalFixtureAsync(bind: false);
        using (User(f.Source.Source.School.Reader)) {
            var preview = await Formal.PreviewFormalAssessmentAsync(f.ActivityId, f.Task.TaskId); var input = FormalBind(preview);
            var bound = await Formal.BindFormalAssessmentAsync(f.ActivityId, input);
            (await Formal.BindFormalAssessmentAsync(f.ActivityId, input)).Id.ShouldBe(bound.Id);
            (await Formal.GetFormalBindingOperationAsync(f.ActivityId, input.OperationId))!.Id.ShouldBe(bound.Id);
            input.ConfirmExactPlanContainsNoHints = false;
            (await Should.ThrowAsync<BusinessException>(() => Formal.BindFormalAssessmentAsync(f.ActivityId, input))).Code.ShouldBe(EducationTeachingPlanErrors.OperationConflict);
            var details = (await Formal.GetFormalAssessmentAsync(f.ActivityId))!;
            var stop = FormalReview(details, false, EducationLearningExposureUse.Confirmed);
            var review = await Formal.ReviewFormalAssessmentAsync(f.ActivityId, stop);
            (await Formal.ReviewFormalAssessmentAsync(f.ActivityId, stop)).Id.ShouldBe(review.Id);
            (await Formal.GetFormalReviewOperationAsync(f.ActivityId, stop.OperationId))!.Id.ShouldBe(review.Id);
            var stale = FormalReview(details, true, EducationLearningExposureUse.ConfirmedNoHints);
            await Should.ThrowAsync<AbpDbConcurrencyException>(() => Formal.ReviewFormalAssessmentAsync(f.ActivityId, stale));
        }
        using (User(f.Source.Source.Student.UserId)) {
            (await Assessments.GetMyStudentTaskAsync(f.RecipientId)).CanStart.ShouldBeFalse();
            (await Should.ThrowAsync<BusinessException>(() => Assessments.StartOrResumeAsync(f.RecipientId, new() { OperationId = Guid.NewGuid() }))).Code.ShouldBe(EducationTeachingPlanErrors.AssessmentStopped);
            var row = (await Activities.GetMyActivitiesAsync(f.PlanId, f.PublicationId)).Activities.Single(x => x.Id == f.ActivityId);
            row.FormalAssessment!.IsStopped.ShouldBeTrue(); JsonSerializer.Serialize(row).ShouldNotContain("重新核对");
        }
        using (User(f.Source.Source.School.Reader)) {
            var details = (await Formal.GetFormalAssessmentAsync(f.ActivityId))!;
            var bad = FormalReview(details, true, EducationLearningExposureUse.Unverified);
            await Should.ThrowAsync<BusinessException>(() => Formal.ReviewFormalAssessmentAsync(f.ActivityId, bad));
            await Formal.ReviewFormalAssessmentAsync(f.ActivityId, FormalReview(details, true, EducationLearningExposureUse.ConfirmedNoHints));
        }
        using (User(f.Source.Source.Student.UserId)) (await Assessments.GetMyStudentTaskAsync(f.RecipientId)).CanStart.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Formal_Withdrawal_Should_Stop_New_Starts_But_Keep_Already_Started_Attempt_And_Submission(bool started)
    {
        var f = await FormalFixtureAsync(); Guid? attemptId = null;
        if (started) using (User(f.Source.Source.Student.UserId)) attemptId = (await Assessments.StartOrResumeAsync(f.RecipientId, new() { OperationId = Guid.NewGuid() })).Id;
        using (User(f.Source.Source.School.Reader)) await Plans.WithdrawAsync(f.PlanId, PlanTransition(await Plans.GetAsync(f.PlanId), "停止该方案的新执行"));
        using (User(f.Source.Source.Student.UserId)) {
            if (!started) { (await Assessments.GetMyStudentTaskAsync(f.RecipientId)).CanStart.ShouldBeFalse(); await Should.ThrowAsync<BusinessException>(() => Assessments.StartOrResumeAsync(f.RecipientId, new() { OperationId = Guid.NewGuid() })); }
            else {
                var resumed = await Assessments.StartOrResumeAsync(f.RecipientId, new() { OperationId = Guid.NewGuid() }); resumed.Id.ShouldBe(attemptId!.Value);
                var sealedAnswer = await SubmitReviewedAsync(resumed.Id, new() { OperationId = Guid.NewGuid(), PaperSnapshotVersion = resumed.PaperSnapshotVersion, ExpectedResponseSetVersion = resumed.ResponseSetVersion, ConfirmSavedOnly = true });
                (await Assessments.GetSubmissionAsync(resumed.Id))!.Id.ShouldBe(sealedAnswer.Id);
            }
        }
    }

    [Fact]
    public async Task Formal_Binding_Should_Reject_Previously_Started_Recipient_And_Require_All_Explicit_Confirmations()
    {
        var f = await FormalFixtureAsync(bind: false);
        using (User(f.Source.Source.School.Reader)) {
            var input = FormalBind(await Formal.PreviewFormalAssessmentAsync(f.ActivityId, f.Task.TaskId)); input.ConfirmNewAssessmentRequirements = false;
            await Should.ThrowAsync<BusinessException>(() => Formal.BindFormalAssessmentAsync(f.ActivityId, input));
            await Should.ThrowAsync<BusinessException>(() => Formal.PreviewFormalAssessmentAsync(f.ActivityId, f.Source.TaskId));
        }
        using (User(f.Source.Source.Student.UserId)) await Assessments.StartOrResumeAsync(f.RecipientId, new() { OperationId = Guid.NewGuid() });
        using (User(f.Source.Source.School.Reader)) await Should.ThrowAsync<BusinessException>(() => Formal.PreviewFormalAssessmentAsync(f.ActivityId, f.Task.TaskId));
    }

    [Fact]
    public async Task Formal_Teacher_Details_Should_Reject_Student_And_Other_Teacher()
    {
        var f = await FormalFixtureAsync();
        using (User(f.Source.Source.Student.UserId)) await Should.ThrowAsync<AbpAuthorizationException>(() => Formal.GetFormalAssessmentAsync(f.ActivityId));
        using (User(f.Source.Source.School.Owner)) await Should.ThrowAsync<AbpAuthorizationException>(() => Formal.GetFormalAssessmentAsync(f.ActivityId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Formal_Plan_Context_Should_Require_Actual_Provision_And_Preserve_The_Exact_First_Provision_Time(bool providedBeforeBinding)
    {
        var f = await FormalFixtureAsync(open: providedBeforeBinding);
        var reader = GetRequiredService<EducationLearningReportSourceReader>();
        var binding = await WithUnitOfWorkAsync(async () => (await Repo<EducationTeachingFormalAssessment>().GetListAsync()).Single());
        var original = await WithUnitOfWorkAsync(async () => (await Repo<EducationTeachingFormalQuestion>().GetListAsync()).Single().OriginalQuestionId);
        using (User(f.Source.Source.School.Reader)) {
            await Formal.GetFormalAssessmentAsync(f.ActivityId); await Activities.GetActivityProgressAsync(f.PlanId);
            var first = await WithUnitOfWorkAsync(async () => (await Repo<EducationTeachingActivity>().GetAsync(f.ActivityId)).FirstProvidedAtUtc);
            first.HasValue.ShouldBe(providedBeforeBinding);
            if (first.HasValue) (first.Value <= binding.RecordedAtUtc).ShouldBeTrue();
            var initial = await WithUnitOfWorkAsync(() => reader.CheckFormalNoveltyAsync(f.Task.Curriculum, f.Source.Source.Student.UserId, [original], [], binding.Id));
            var details = (await Formal.GetFormalAssessmentAsync(f.ActivityId))!;
            await Formal.ReviewFormalAssessmentAsync(f.ActivityId, FormalReview(details, false, EducationLearningExposureUse.Unverified));
            if (providedBeforeBinding) await Should.ThrowAsync<BusinessException>(() => WithUnitOfWorkAsync(() => reader.CheckFormalNoveltyAsync(f.Task.Curriculum, f.Source.Source.Student.UserId, [original], [], binding.Id)));
            else (await WithUnitOfWorkAsync(() => reader.CheckFormalNoveltyAsync(f.Task.Curriculum, f.Source.Source.Student.UserId, [original], [], binding.Id))).ShouldBe(initial);
        }
        if (!providedBeforeBinding) using (User(f.Source.Source.Student.UserId)) await Plans.OpenMyAsync(f.PlanId, f.PublicationId);
        using (User(f.Source.Source.School.Reader)) {
            await Should.ThrowAsync<BusinessException>(() => WithUnitOfWorkAsync(() => reader.CheckFormalNoveltyAsync(f.Task.Curriculum, f.Source.Source.Student.UserId, [original], [], binding.Id)));
            await Formal.ReviewFormalAssessmentAsync(f.ActivityId, FormalReview((await Formal.GetFormalAssessmentAsync(f.ActivityId))!, true, EducationLearningExposureUse.ConfirmedNoHints));
            (await WithUnitOfWorkAsync(() => reader.CheckFormalNoveltyAsync(f.Task.Curriculum, f.Source.Source.Student.UserId, [original], [], binding.Id))).ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task Formal_New_Start_Should_Recheck_All_Earlier_Ungraded_Attempts_After_Binding()
    {
        var f = await FormalFixtureAsync(); Guid otherRecipient;
        using (User(f.Source.Source.School.Reader)) {
            var input = AssessmentInput(f.Source.Source.School, f.Task.ExamPaperId);
            input.Scope = new() { TeacherTextbookSelectionId = f.Task.Curriculum.TeacherTextbookSelectionId,
                TextbookKnowledgePointMappingIds = f.Task.Curriculum.Mappings.Select(x => x.MappingId).ToList() };
            input.RecipientScope = EducationAssessmentRecipientScope.SelectedStudents; input.StudentUserIds = [f.Source.Source.Student.UserId];
            var other = await Assessments.CreateDraftAsync(input);
            otherRecipient = (await Assessments.PublishAsync(other.Id, AssessmentPublish(await Assessments.PreviewRecipientsAsync(other.Id)))).Recipients.Single().Id;
            var diagnostic = (await Activities.GetActivityProgressAsync(f.PlanId)).Activities.Single(x => x.StepOrder == 1);
            (await Should.ThrowAsync<BusinessException>(() => Formal.PreviewFormalAssessmentAsync(diagnostic.Id, f.Task.TaskId))).Code.ShouldBe(EducationTeachingPlanErrors.AssessmentNotNew);
        }
        using (User(f.Source.Source.Student.UserId)) {
            (await Assessments.GetMyStudentTaskAsync(f.RecipientId)).CanStart.ShouldBeTrue();
            await Assessments.StartOrResumeAsync(otherRecipient, new() { OperationId = Guid.NewGuid() });
            (await Assessments.GetMyStudentTaskAsync(f.RecipientId)).CanStart.ShouldBeFalse();
            (await Should.ThrowAsync<BusinessException>(() => Assessments.StartOrResumeAsync(f.RecipientId, new() { OperationId = Guid.NewGuid() }))).Code.ShouldBe(EducationTeachingPlanErrors.AssessmentStopped);
        }
    }

    [Theory]
    [InlineData(EducationPermissions.TeachingPlanManage)]
    [InlineData(EducationPermissions.AnalysisManage)]
    [InlineData(EducationPermissions.AssessmentManage)]
    public async Task Formal_Service_Should_Explicitly_Check_Each_Function_Permission(string permission)
    {
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(Arg.Any<string>()).Returns(true); checker.IsGrantedAsync(permission).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        var service = new EducationTeachingPlanAppService { LazyServiceProvider = lazy };
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetFormalAssessmentAsync(Guid.NewGuid()));
    }
}
