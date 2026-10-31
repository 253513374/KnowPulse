using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationAssessmentGradingAppService Grades => GetRequiredService<IEducationAssessmentGradingAppService>();

    [Theory]
    [InlineData(ExamResultVisibility.Hidden)]
    [InlineData(ExamResultVisibility.ScoreOnly)]
    [InlineData(ExamResultVisibility.FullReview)]
    public async Task Assessment_Grading_Should_Persist_All_Correct_Evidence_And_Enforce_Released_Student_Visibility(ExamResultVisibility visibility)
    {
        var f = await GradingFixtureAsync(subjective: false);
        await GetRequiredService<EducationAssessmentGradingProcessor>().InitializeAsync(f.SubmissionId);
        await GetRequiredService<EducationAssessmentGradingProcessor>().InitializeAsync(f.SubmissionId);
        EducationAssessmentClosureDto closure;
        EducationAssessmentResultReleaseDto release;
        using (User(f.Source.School.Reader))
        {
            var overview = await Grades.GetGradingOverviewAsync(f.TaskId);
            overview.GradingCompleteCount.ShouldBe(1); overview.Submissions.Single().TotalScore.ShouldBe(10);
            var submission = await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
            var confirmed = submission.Questions.Single().Grade.Confirmed!;
            confirmed.Content.MappingEvidence.Count.ShouldBe(2);
            confirmed.Content.MappingEvidence.Sum(x => x.ObtainedScore).ShouldBe(10);
            var preview = await Grades.PreviewFinalizationAsync(f.TaskId);
            closure = await Grades.CloseTaskAsync(f.TaskId, CloseInput(preview));
            var next = await Grades.GetGradingOverviewAsync(f.TaskId);
            var publish = ReleaseInput(next, closure.Id, visibility);
            release = await Grades.PublishResultsAsync(f.TaskId, publish);
            (await Grades.PublishResultsAsync(f.TaskId, publish)).Id.ShouldBe(release.Id);
            release.Items.Count.ShouldBe(1);
        }
        using (User(f.Source.Student.UserId))
        {
            var result = await Grades.GetMyResultAsync(f.RecipientId);
            result.CurrentResultVisibility.ShouldBe(visibility);
            result.TotalScore.ShouldBe(visibility == ExamResultVisibility.Hidden ? null : 10m);
            if (visibility == ExamResultVisibility.FullReview)
            {
                result.Questions.Count.ShouldBe(1);
                result.Questions.Single().ScoringDefinition!.Blanks.Count.ShouldBe(2);
                result.Questions.Single().ScoringDefinition!.Blanks.All(x => !x.TextbookKnowledgePointMappingId.HasValue).ShouldBeTrue();
            }
            else result.Questions.ShouldBeEmpty();
            JsonSerializer.Serialize(result).ShouldNotContain("CurriculumSource");
        }
        using (User(f.Absent.UserId))
        {
            var result = await Grades.GetMyResultAsync(f.AbsentRecipientId);
            result.Outcome.ShouldBe(EducationAssessmentRecipientOutcome.Absent);
            result.TotalScore.ShouldBeNull(); result.Questions.ShouldBeEmpty();
            await Should.ThrowAsync<AbpAuthorizationException>(() => Grades.GetMyResultAsync(f.RecipientId));
        }
        await WithUnitOfWorkAsync(async () =>
        {
            (await Repo<EducationAssessmentQuestionGrade>().GetListAsync(x => x.SubmissionId == f.SubmissionId)).Count.ShouldBe(1);
            (await Repo<EducationAssessmentGradeRevision>().GetListAsync(x => x.SubmissionId == f.SubmissionId)).Count.ShouldBe(1);
        });
    }

    [Fact]
    public async Task Assessment_Grading_Should_Keep_Manual_Pending_Block_Incomplete_Release_And_Preserve_Official_Score_During_Correction()
    {
        var f = await GradingFixtureAsync(subjective: true);
        using var teacher = User(f.Source.School.Reader);
        var initial = await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
        initial.Questions.Single().Grade.Confirmed.ShouldBeNull();
        initial.Questions.Single().Grade.Draft!.ObtainedScore.ShouldBeNull();
        var closure = await Grades.CloseTaskAsync(f.TaskId, CloseInput(await Grades.PreviewFinalizationAsync(f.TaskId)));
        var notReady = ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), closure.Id, ExamResultVisibility.FullReview);
        (await Should.ThrowAsync<BusinessException>(() => Grades.PublishResultsAsync(f.TaskId, notReady))).Code.ShouldBe(EducationAssessmentErrorCodes.GradingIncomplete);
        await ConfirmManualGradeAsync(f.SubmissionId, 1, null);
        var published = await Grades.PublishResultsAsync(f.TaskId, ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), closure.Id, ExamResultVisibility.FullReview));
        published.Items.Single().TotalScore.ShouldBe(8);
        var current = await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
        var question = current.Questions.Single();
        var draftInput = new SaveEducationAssessmentGradeDraftDto { OperationId = Guid.NewGuid(), PaperQuestionId = question.Id,
            ConcurrencyStamp = question.Grade.ConcurrencyStamp, ExpectedGradeRevisionId = question.Grade.CurrentRevisionId,
            PartMarks = question.Grade.Confirmed!.Content.Parts.Select(x => new EducationAssessmentPartMark { PartId = x.PartId, ObtainedScore = x.MaxScore }).ToList(), Feedback = "更正反馈" };
        var draft = await Grades.SaveGradeDraftAsync(f.SubmissionId, draftInput);
        (await Grades.SaveGradeDraftAsync(f.SubmissionId, draftInput)).Id.ShouldBe(draft.Id);
        using (User(f.Source.Student.UserId)) (await Grades.GetMyResultAsync(f.RecipientId)).TotalScore.ShouldBe(8);
        var refreshed = await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
        var confirm = new ConfirmEducationAssessmentGradeDto { OperationId = Guid.NewGuid(), ConcurrencyStamp = refreshed.Questions.Single().Grade.ConcurrencyStamp,
            ExpectedDraftRevisionId = draft.Id, ExpectedGradeSetVersion = refreshed.ConfirmedGradeSetVersion, Reason = "复核后补回评分点" };
        var corrected = await Grades.ConfirmGradeAsync(question.Grade.Id, confirm);
        (await Grades.ConfirmGradeAsync(question.Grade.Id, confirm)).Id.ShouldBe(corrected.Id);
        using (User(f.Source.Student.UserId)) (await Grades.GetMyResultAsync(f.RecipientId)).TotalScore.ShouldBe(10);
        var old = await Grades.GetReleaseOperationAsync(f.TaskId, published.Id);
        old!.Items.Single().TotalScore.ShouldBe(8);
        (await Grades.GetGradeOperationAsync(f.SubmissionId, draft.Id))!.Kind.ShouldBe(EducationAssessmentGradeRevisionKind.Draft);
    }

    [Fact]
    public async Task Assessment_Grading_Should_Not_Release_When_Another_Student_Can_Still_Answer()
    {
        var f = await GradingFixtureAsync(subjective: false, ended: false, secondStarted: true);
        using var teacher = User(f.Source.School.Reader);
        await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
        await WithUnitOfWorkAsync(async () =>
        {
            var attempts = await Repo<EducationAssessmentAttempt>().GetListAsync(x => x.TaskId == f.TaskId);
            attempts.Count.ShouldBe(2);
            attempts.Count(x => !x.SubmissionId.HasValue).ShouldBe(1);
        });
        (await Grades.GetGradingOverviewAsync(f.TaskId)).InProgressCount.ShouldBe(1);
        var preview = await Grades.PreviewFinalizationAsync(f.TaskId);
        preview.InProgressCount.ShouldBe(1); preview.GradingCompleteCount.ShouldBe(1); preview.CanClose.ShouldBeFalse(); preview.CanPublish.ShouldBeFalse();
        await Should.ThrowAsync<BusinessException>(() => Grades.CloseTaskAsync(f.TaskId, CloseInput(preview)));
        using (User(f.Source.Student.UserId))
        {
            var result = await Grades.GetMyResultAsync(f.RecipientId);
            result.TotalScore.ShouldBeNull(); result.Questions.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Assessment_Grading_Should_Seal_All_Ongoing_Attempts_And_Release_A_Real_Zero_Without_Marking_It_Absent()
    {
        var f = await GradingFixtureAsync(subjective: false, secondStarted: true);
        using (User(f.Source.School.Reader))
        {
            await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
            var input = CloseInput(await Grades.PreviewFinalizationAsync(f.TaskId));
            var closure = await Grades.CloseTaskAsync(f.TaskId, input);
            (await Grades.CloseTaskAsync(f.TaskId, input)).Id.ShouldBe(closure.Id);
            closure.ValidSubmissionCount.ShouldBe(2);
            closure.Manifest.Entries.All(x => x.Outcome == EducationAssessmentRecipientOutcome.Submitted).ShouldBeTrue();
            var second = closure.Manifest.Entries.Single(x => x.StudentUserId == f.Absent.UserId);
            var preview = await Grades.PreviewFinalizationAsync(f.TaskId);
            preview.InProgressCount.ShouldBe(0); preview.PendingQuestionCount.ShouldBe(1); preview.CanPublish.ShouldBeFalse();
            var ungraded = ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), closure.Id, ExamResultVisibility.FullReview);
            (await Should.ThrowAsync<BusinessException>(() => Grades.PublishResultsAsync(f.TaskId, ungraded))).Code.ShouldBe(EducationAssessmentErrorCodes.GradingIncomplete);
            var sheet = await Grades.GetSubmissionForGradingAsync(second.SubmissionId!.Value);
            sheet.Submission.Cause.ShouldBe(EducationAssessmentSubmissionCause.Deadline);
            sheet.Questions.Single().Response.ShouldBeNull();
            sheet.Questions.Single().Grade.Confirmed!.ObtainedScore.ShouldBe(0);
            var release = await Grades.PublishResultsAsync(f.TaskId, ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), closure.Id, ExamResultVisibility.FullReview));
            release.Items.Count.ShouldBe(2);
        }
        using (User(f.Absent.UserId))
        {
            var result = await Grades.GetMyResultAsync(f.AbsentRecipientId);
            result.Outcome.ShouldBe(EducationAssessmentRecipientOutcome.Submitted);
            result.TotalScore.ShouldBe(0); result.MaxScore.ShouldBe(10);
            result.Questions.Single().Response.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Assessment_Grading_Should_Keep_Scheduled_Scores_And_Later_Review_Hidden_Until_Their_Own_Opening()
    {
        var f = await GradingFixtureAsync(subjective: false);
        EducationAssessmentResultReleaseDto scheduled, opened;
        using (User(f.Source.School.Reader))
        {
            await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
            var closure = await Grades.CloseTaskAsync(f.TaskId, CloseInput(await Grades.PreviewFinalizationAsync(f.TaskId)));
            var input = ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), closure.Id, ExamResultVisibility.FullReview);
            input.ScoreOpensAtUtc = DateTime.UtcNow.AddHours(1); input.ReviewOpensAtUtc = DateTime.UtcNow.AddHours(2);
            scheduled = await Grades.PublishResultsAsync(f.TaskId, input);
            (await Grades.PublishResultsAsync(f.TaskId, input)).Id.ShouldBe(scheduled.Id);
        }
        var pendingNoticeId = await WithUnitOfWorkAsync(async () => (await Repo<EducationAssessmentReminder>().GetListAsync(x => x.ResultReleaseId == scheduled.Id && x.StudentUserId == f.Source.Student.UserId)).Single().Id);
        using (User(f.Source.Student.UserId))
        {
            (await Reminders.GetMyNoticesAsync(new() { SchoolId = f.Source.School.SchoolId, MaxResultCount = 100 })).Items.ShouldNotContain(x => x.TaskId == f.TaskId);
            await Should.ThrowAsync<BusinessException>(() => Reminders.ReadMyReminderAsync(pendingNoticeId, f.Source.School.SchoolId));
            var hidden = await Grades.GetMyResultAsync(f.RecipientId);
            hidden.CurrentResultVisibility.ShouldBe(ExamResultVisibility.Hidden);
            hidden.TotalScore.ShouldBeNull(); hidden.ResultReleaseId.ShouldBeNull(); hidden.Questions.ShouldBeEmpty();
        }
        await WithUnitOfWorkAsync(async () =>
        {
            var release = await Repo<EducationAssessmentResultRelease>().GetAsync(scheduled.Id);
            release.VisibilityAt(scheduled.ScoreOpensAtUtc!.Value.AddTicks(-1)).ShouldBe(ExamResultVisibility.Hidden);
            release.VisibilityAt(scheduled.ScoreOpensAtUtc.Value).ShouldBe(ExamResultVisibility.ScoreOnly);
            release.VisibilityAt(scheduled.ReviewOpensAtUtc!.Value.AddTicks(-1)).ShouldBe(ExamResultVisibility.ScoreOnly);
            release.VisibilityAt(scheduled.ReviewOpensAtUtc.Value).ShouldBe(ExamResultVisibility.FullReview);
        });
        using (User(f.Source.School.Reader))
        {
            var input = ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), scheduled.ClosureId, ExamResultVisibility.FullReview);
            input.ReviewOpensAtUtc = scheduled.ReviewOpensAtUtc; input.Reason = "提前开放总分，讲评时间不变";
            opened = await Grades.PublishResultsAsync(f.TaskId, input);
            (await Grades.GetReleaseOperationAsync(f.TaskId, scheduled.Id))!.ScoreOpensAtUtc.ShouldBe(scheduled.ScoreOpensAtUtc);
        }
        EducationAssessmentReminderDto openedNotice;
        using (User(f.Source.Student.UserId))
        {
            var scoresOnly = await Grades.GetMyResultAsync(f.RecipientId);
            scoresOnly.CurrentResultVisibility.ShouldBe(ExamResultVisibility.ScoreOnly);
            scoresOnly.TotalScore.ShouldBe(10); scoresOnly.Questions.ShouldBeEmpty();
            openedNotice = (await Reminders.GetMyNoticesAsync(new() { SchoolId = f.Source.School.SchoolId, MaxResultCount = 100 })).Items.Single(x => x.TaskId == f.TaskId);
            openedNotice.Kind.ShouldBe(EducationAssessmentReminderKind.ResultReleased); openedNotice.ResultReleaseId.ShouldBe(opened.Id); openedNotice.CanOpen.ShouldBeTrue(); openedNotice.ReadAtUtc.ShouldBeNull();
            (await Reminders.ReadMyReminderAsync(openedNotice.Id, f.Source.School.SchoolId)).ReadAtUtc.ShouldNotBeNull();
        }
        using (User(f.Absent.UserId)) { (await Reminders.GetMyNoticesAsync(new() { SchoolId = f.Source.School.SchoolId, MaxResultCount = 100 })).Items.Single(x => x.TaskId == f.TaskId).ResultReleaseId.ShouldBe(opened.Id); }
        await WithUnitOfWorkAsync(async () => {
            var pending = await Repo<EducationAssessmentReminder>().GetAsync(pendingNoticeId);
            // Advance the test notice to its due point after replacement without rewriting either result release.
            typeof(EducationAssessmentReminder).GetProperty(nameof(EducationAssessmentReminder.AvailableAtUtc))!.SetValue(pending, opened.ReleasedAtUtc);
            await Repo<EducationAssessmentReminder>().UpdateAsync(pending, autoSave: true);
        });
        using (User(f.Source.Student.UserId)) {
            (await Reminders.GetMyNoticesAsync(new() { SchoolId = f.Source.School.SchoolId, MaxResultCount = 100 })).Items.ShouldNotContain(x => x.Id == pendingNoticeId);
            await Should.ThrowAsync<BusinessException>(() => Reminders.ReadMyReminderAsync(pendingNoticeId, f.Source.School.SchoolId));
        }
        using (User(f.Source.School.Reader)) {
            var input = ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), scheduled.ClosureId, ExamResultVisibility.Hidden); input.Reason = "重新隐藏当前结果";
            await Grades.PublishResultsAsync(f.TaskId, input);
        }
        using (User(f.Source.Student.UserId)) {
            var old = (await Reminders.GetMyNoticesAsync(new() { SchoolId = f.Source.School.SchoolId, MaxResultCount = 100 })).Items.Single(x => x.TaskId == f.TaskId);
            old.Id.ShouldBe(openedNotice.Id); old.CanOpen.ShouldBeFalse(); old.ReadAtUtc.ShouldNotBeNull();
            (await Reminders.ReadMyReminderAsync(old.Id, f.Source.School.SchoolId)).ReadAtUtc.ShouldBe(old.ReadAtUtc);
            await Should.ThrowAsync<BusinessException>(() => Grades.GetMyResultForSchoolAsync(f.RecipientId, f.Source.School.SchoolId, old.PublicationId, opened.Id));
        }
    }

    [Fact]
    public async Task Assessment_Grading_Should_Deny_Other_Teachers_And_Students_Even_With_Functional_Permission()
    {
        var f = await GradingFixtureAsync(subjective: true);
        using (User(f.Source.School.Owner))
            await Should.ThrowAsync<AbpAuthorizationException>(() => Grades.GetSubmissionForGradingAsync(f.SubmissionId));
        using (User(f.Source.Student.UserId))
            await Should.ThrowAsync<AbpAuthorizationException>(() => Grades.GetGradingOverviewAsync(f.TaskId));
    }

    private async Task ConfirmManualGradeAsync(Guid submissionId, decimal deductionPerPart, string? reason)
    {
        var current = await Grades.GetSubmissionForGradingAsync(submissionId);
        var q = current.Questions.Single(); var content = q.Grade.Draft?.Content ?? q.Grade.Confirmed!.Content;
        var draft = await Grades.SaveGradeDraftAsync(submissionId, new() { OperationId = Guid.NewGuid(), PaperQuestionId = q.Id,
            ConcurrencyStamp = q.Grade.ConcurrencyStamp, ExpectedGradeRevisionId = q.Grade.CurrentRevisionId,
            PartMarks = content.Parts.Select(x => new EducationAssessmentPartMark { PartId = x.PartId, ObtainedScore = x.MaxScore - deductionPerPart }).ToList() });
        var next = await Grades.GetSubmissionForGradingAsync(submissionId);
        await Grades.ConfirmGradeAsync(q.Grade.Id, new() { OperationId = Guid.NewGuid(), ConcurrencyStamp = next.Questions.Single().Grade.ConcurrencyStamp,
            ExpectedDraftRevisionId = draft.Id, ExpectedGradeSetVersion = next.ConfirmedGradeSetVersion, Reason = reason });
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("negative")]
    [InlineData("over-maximum")]
    [InlineData("precision")]
    public async Task Assessment_Grading_Should_Reject_Invalid_Point_Identities_And_Marks(string change)
    {
        var f = await GradingFixtureAsync(subjective: true);
        using var teacher = User(f.Source.School.Reader);
        var sheet = await Grades.GetSubmissionForGradingAsync(f.SubmissionId); var q = sheet.Questions.Single();
        var marks = q.Grade.Draft!.Content.Parts.Select(x => new EducationAssessmentPartMark { PartId = x.PartId, ObtainedScore = x.MaxScore }).ToList();
        if (change == "unknown") marks[0].PartId = Guid.NewGuid();
        if (change == "duplicate") marks[1].PartId = marks[0].PartId;
        if (change == "negative") marks[0].ObtainedScore = -0.01m;
        if (change == "over-maximum") marks[0].ObtainedScore += 1;
        if (change == "precision") marks[0].ObtainedScore = 0.001m;
        await Should.ThrowAsync<BusinessException>(() => Grades.SaveGradeDraftAsync(f.SubmissionId, new()
        { OperationId = Guid.NewGuid(), PaperQuestionId = q.Id, ConcurrencyStamp = q.Grade.ConcurrencyStamp, ExpectedGradeRevisionId = q.Grade.CurrentRevisionId, PartMarks = marks }));
        (await Grades.GetSubmissionForGradingAsync(f.SubmissionId)).Questions.Single().Grade.Confirmed.ShouldBeNull();
    }

    [Fact]
    public async Task Assessment_Grading_Should_Keep_Original_Results_While_A_Student_Score_Review_Is_Open()
    {
        var f = await GradingFixtureAsync(subjective: true);
        EducationAssessmentResultReleaseDto release;
        using (User(f.Source.School.Reader))
        {
            await ConfirmManualGradeAsync(f.SubmissionId, 0, null);
            var closure = await Grades.CloseTaskAsync(f.TaskId, CloseInput(await Grades.PreviewFinalizationAsync(f.TaskId)));
            release = await Grades.PublishResultsAsync(f.TaskId, ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), closure.Id, ExamResultVisibility.ScoreOnly));
        }
        EducationAssessmentScoreReviewRequestDto review;
        using (User(f.Source.Student.UserId))
        {
            var input = new RequestEducationAssessmentScoreReviewDto { OperationId = Guid.NewGuid(), ResultReleaseId = release.Id, Reason = "请复核评分点" };
            review = await Grades.RequestScoreReviewAsync(f.RecipientId, input);
            (await Grades.RequestScoreReviewAsync(f.RecipientId, input)).Id.ShouldBe(review.Id);
            (await Grades.GetMyResultAsync(f.RecipientId)).TotalScore.ShouldBe(10);
            (await Grades.GetMyScoreReviewAsync(review.Id)).Decision.ShouldBeNull();
        }
        using (User(f.Source.School.Reader))
        {
            var input = new ResolveEducationAssessmentScoreReviewDto { OperationId = Guid.NewGuid(), ConcurrencyStamp = review.ConcurrencyStamp,
                Decision = EducationAssessmentScoreReviewDecision.ConfirmOriginal, Reason = "尚未讲评的答案反馈-SENTINEL" };
            await Grades.ResolveScoreReviewAsync(review.Id, input);
            (await Grades.ResolveScoreReviewAsync(review.Id, input)).ResolutionOperationId.ShouldBe(input.OperationId);
        }
        using (User(f.Source.Student.UserId))
        {
            var resolved = await Grades.GetMyScoreReviewAsync(review.Id);
            resolved.Decision.ShouldBe(EducationAssessmentScoreReviewDecision.ConfirmOriginal);
            resolved.ResolutionReason.ShouldBeNull();
            (await Grades.GetMyResultAsync(f.RecipientId)).TotalScore.ShouldBe(10);
        }
    }
    private static CloseEducationAssessmentTaskDto CloseInput(EducationAssessmentFinalizationPreviewDto p) => new()
    { OperationId = Guid.NewGuid(), ConcurrencyStamp = p.ConcurrencyStamp, ExpectedPublishedRevisionId = p.PublishedRevisionId, FinalizationFingerprint = p.FinalizationFingerprint };
    private static PublishEducationAssessmentResultsDto ReleaseInput(EducationAssessmentGradingOverviewDto p, Guid closure, ExamResultVisibility visibility) => new()
    { OperationId = Guid.NewGuid(), ConcurrencyStamp = p.ConcurrencyStamp, ExpectedClosureId = closure, ExpectedGradeSetVersion = p.ConfirmedGradeSetVersion,
      ConfiguredVisibility = visibility, ConfirmVisibilityChange = true, ScoreOpensAtUtc = DateTime.UtcNow.AddSeconds(-5), ReviewOpensAtUtc = DateTime.UtcNow.AddSeconds(-5) };

    private async Task<GradingFixture> GradingFixtureAsync(bool subjective, bool ended = true, bool secondStarted = false, AssessmentResponseFixture? sourceOverride = null, bool allowCollection = false)
    {
        var source = sourceOverride == null ? await ResponseFixtureAsync(subjective) : sourceOverride with
        { Revision = JsonSerializer.Deserialize<EducationAssessmentTaskRevisionDto>(JsonSerializer.Serialize(sourceOverride.Revision))! };
        if (!subjective)
        {
            using var teacher = User(source.School.Reader);
            var value = Manual(source.School); value.Type = QuestionType.FillBlank; value.Stem = "甲【1】，乙【2】"; value.Options = []; value.Answers = [];
            value.ScoringDefinition = new() { Blanks = [new() { Id = Guid.NewGuid(), DisplayOrder = 1, StemMarker = "【1】", Score = 6,
                TextRule = QuestionBlankTextRule.Exact, AllowedAnswers = ["正确甲"], TextbookKnowledgePointMappingId = source.School.MappingA },
                new() { Id = Guid.NewGuid(), DisplayOrder = 2, StemMarker = "【2】", Score = 4, TextRule = QuestionBlankTextRule.Exact,
                AllowedAnswers = ["正确乙"], TextbookKnowledgePointMappingId = source.School.MappingB }] };
            var questions = GetRequiredService<IQuestionAppService>();
            var question = await questions.CreateAsync(value);
            question = await questions.ConfirmCurriculumAsync(question.Id, Confirm(source.School, question));
            question = await questions.SubmitReviewAsync(question.Id); question = await questions.ApproveAsync(question.Id, Review(question));
            var paper = await GetRequiredService<IExamAppService>().SavePaperAsync(Paper(source.School, question.Id));
            source.Revision.ExamPaperId = paper.Id;
            source = source with { QuestionId = paper.Questions.Single().Id, PaperSnapshotVersion = paper.SnapshotVersion };
        }
        var absent = await AssessmentStudentAsync(source.School);
        return await WithUnitOfWorkAsync(async () =>
        {
            var now = DateTime.UtcNow; var time = now.AddMinutes(-40); var original = source.Revision;
            var task = new EducationAssessmentTask(Guid.NewGuid(), null, source.School.Reader, new string('a', 64), time);
            await Repo<EducationAssessmentTask>().InsertAsync(task, autoSave: true);
            EducationAssessmentTaskRevision Rev(int n, Guid? previous, bool published) => new(Guid.NewGuid(), null, task.OwnerUserId, task.Id, n, previous,
                "待批改与发布验收", original.Instructions, original.SchoolId, original.AcademicTermId, original.SchoolClassId, original.SchoolClassName,
                original.TeachingAssignmentId, original.ExamPaperId, JsonSerializer.Serialize(original.Curriculum), EducationAssessmentRecipientScope.FullClass,
                [], now.AddHours(-1), ended ? now.AddMinutes(-1) : now.AddHours(1), 60, 1, ExamResultVisibility.Hidden, allowCollection,
                new string('b', 64), time.AddMinutes(n), published ? time.AddMinutes(n) : null, published ? new string('c', 64) : null);
            var draft = Rev(1, null, false); await Repo<EducationAssessmentTaskRevision>().InsertAsync(draft, autoSave: true); task.AdvanceDraft(draft);
            await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true);
            var publication = Rev(2, draft.Id, true); await Repo<EducationAssessmentTaskRevision>().InsertAsync(publication, autoSave: true);
            var recipient = new EducationAssessmentRecipient(Guid.NewGuid(), publication, source.Student.UserId, source.Student.MembershipId, source.Student.EnrollmentId, "已提交学生", "submitted");
            var other = new EducationAssessmentRecipient(Guid.NewGuid(), publication, absent.UserId, absent.MembershipId, absent.EnrollmentId, "另一位学生", "other");
            await Repo<EducationAssessmentRecipient>().InsertManyAsync([recipient, other], autoSave: true); task.Publish(publication);
            await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true);
            var attempt = new EducationAssessmentAttempt(Guid.NewGuid(), publication, recipient, now.AddMinutes(-30));
            await Repo<EducationAssessmentAttempt>().InsertAsync(attempt, autoSave: true); recipient.RecordStartedAttempt(attempt);
            await Repo<EducationAssessmentRecipient>().UpdateManyAsync([recipient], autoSave: true);
            var question = await Repo<ExamPaperQuestion>().GetAsync(source.QuestionId);
            var definition = QuestionScoringDefinition.Parse(question.ScoringDefinitionJson!);
            var response = new EducationAssessmentResponseValue { Type = question.Type, Text = subjective ? "保留原始推理" : null,
                Blanks = subjective ? [] : definition.Blanks.Select(x => new EducationAssessmentBlankResponse { BlankId = x.Id, Text = x.AllowedAnswers.First() }).ToList() };
            var revision = new EducationAssessmentResponseRevision(Guid.NewGuid(), attempt, question.Id, source.PaperSnapshotVersion, 0, response, new string('d', 64), now.AddMinutes(-20));
            await Repo<EducationAssessmentResponseRevision>().InsertAsync(revision, autoSave: true);
            var pointer = new EducationAssessmentCurrentResponse(Guid.NewGuid(), revision); await Repo<EducationAssessmentCurrentResponse>().InsertAsync(pointer, autoSave: true);
            attempt.RecordResponse(revision, now.AddMinutes(-20)); await Repo<EducationAssessmentAttempt>().UpdateManyAsync([attempt], autoSave: true);
            var paperQuestions = await Repo<ExamPaperQuestion>().GetListAsync(x => x.PaperId == original.ExamPaperId);
            // Multi-question fixtures explicitly seal the saved portion and retain unanswered manifest entries.
            var submission = new EducationAssessmentSubmission(Guid.NewGuid(), attempt, source.PaperSnapshotVersion, EducationAssessmentSubmissionCause.Manual,
                new string('e', 64), paperQuestions.Count > 1, paperQuestions.Select(x => x.Id).ToList(), [pointer], [revision], now.AddMinutes(-10));
            await Repo<EducationAssessmentSubmission>().InsertAsync(submission, autoSave: true); attempt.Seal(submission);
            await Repo<EducationAssessmentAttempt>().UpdateManyAsync([attempt], autoSave: true);
            if (secondStarted)
            {
                var ongoing = new EducationAssessmentAttempt(Guid.NewGuid(), publication, other, now.AddMinutes(-5));
                await Repo<EducationAssessmentAttempt>().InsertAsync(ongoing, autoSave: true); other.RecordStartedAttempt(ongoing);
                await Repo<EducationAssessmentRecipient>().UpdateManyAsync([other], autoSave: true);
            }
            return new GradingFixture(source, task.Id, submission.Id, recipient.Id, absent, other.Id);
        });
    }
    private sealed record GradingFixture(AssessmentResponseFixture Source, Guid TaskId, Guid SubmissionId, Guid RecipientId,
        AssessmentStudent Absent, Guid AbsentRecipientId);
}
