using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Volo.Abp.Validation;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationLearningWorkloadAppService Workload => GetRequiredService<IEducationLearningWorkloadAppService>();
    [Fact]
    public async Task Learning_Workload_Should_Filter_All_Kinds_And_Withheld_Counts_To_The_Selected_School()
    {
        var f = await LearningWorkloadAsync(); var studentId = f.Source.Source.Student.UserId;
        var otherSchool = await FixtureAsync();
        var otherStudent = await WithUnitOfWorkAsync(async () => {
            var selection = await Repo<TeacherTextbookSelection>().GetAsync(otherSchool.ReaderSelection);
            var assignment = await Repo<TeachingAssignment>().GetAsync(selection.TeachingAssignmentId);
            var term = await Repo<AcademicTerm>().GetAsync(assignment.AcademicTermId);
            var member = new SchoolMembership(Guid.NewGuid(), null, otherSchool.SchoolId, studentId, SchoolMemberRole.Student, term.StartsAtUtc, term.EndsAtUtc);
            var enrollment = new StudentEnrollment(Guid.NewGuid(), null, otherSchool.SchoolId, assignment.SchoolClassId, studentId, term.StartsAtUtc, term.EndsAtUtc);
            await Repo<SchoolMembership>().InsertAsync(member, autoSave: true); await Repo<StudentEnrollment>().InsertAsync(enrollment, autoSave: true);
            return new AssessmentStudent(studentId, member.Id, enrollment.Id);
        });
        var otherStudy = await StudyAsync(false, existingSchool: otherSchool, existingStudent: otherStudent);
        using (User(studentId)) {
            var all = await Workload.GetMyLearningWorkloadAsync(WorkloadWindow()); all.SchoolId.ShouldBeNull(); all.Items.Count.ShouldBe(8);
            var first = await Workload.GetMyLearningWorkloadAsync(WorkloadWindow(), f.Source.Source.School.SchoolId);
            first.SchoolId.ShouldBe(f.Source.Source.School.SchoolId); first.Items.Count.ShouldBe(7); first.Items.Select(x => x.Kind).Distinct().Count().ShouldBe(4);
            first.Items.ShouldNotContain(x => x.Id == StudyRecipient(otherStudy)); first.Days.Sum(x => x.KnownMinutes).ShouldBe(70);
            var second = await Workload.GetMyLearningWorkloadAsync(WorkloadWindow(), otherSchool.SchoolId);
            second.SchoolId.ShouldBe(otherSchool.SchoolId); second.Items.Single().Id.ShouldBe(StudyRecipient(otherStudy)); second.Days.Sum(x => x.KnownMinutes).ShouldBe(20);
            var missing = await Workload.GetMyLearningWorkloadAsync(WorkloadWindow(), Guid.NewGuid());
            missing.Items.ShouldBeEmpty(); missing.Days.ShouldBeEmpty(); missing.UnestimatedPlanCount.ShouldBe(0);
        }
        using (User(f.Source.Source.School.Reader)) {
            var overview = await Grades.GetGradingOverviewAsync(f.Source.TaskId);
            var release = ReleaseInput(overview, overview.ClosureId!.Value, ExamResultVisibility.ScoreOnly); release.Reason = "暂不开放讲评";
            await Grades.PublishResultsAsync(f.Source.TaskId, release);
        }
        using (User(studentId)) {
            (await Workload.GetMyLearningWorkloadAsync(WorkloadWindow(), f.Source.Source.School.SchoolId)).UnestimatedPlanCount.ShouldBe(1);
            (await Workload.GetMyLearningWorkloadAsync(WorkloadWindow(), otherSchool.SchoolId)).UnestimatedPlanCount.ShouldBe(0);
            var later = new GetEducationActivityWorkloadDto { StartsAtUtc = DateTime.UtcNow.Date.AddDays(10), EndsAtUtc = DateTime.UtcNow.Date.AddDays(11) };
            (await Workload.GetMyLearningWorkloadAsync(later, f.Source.Source.School.SchoolId)).UnestimatedPlanCount.ShouldBe(1);
            (await Workload.GetMyLearningWorkloadAsync(later, otherSchool.SchoolId)).UnestimatedPlanCount.ShouldBe(0);
        }
        await WithUnitOfWorkAsync(async () => {
            (await Repo<EducationStudyRecipient>().GetAsync(StudyRecipient(otherStudy))).FirstProvidedAtUtc.ShouldBeNull();
            (await Repo<EducationStudyRecipient>().GetAsync(StudyRecipient(f.Study))).FirstProvidedAtUtc.ShouldBeNull();
            (await Repo<EducationTeachingActivity>().GetListAsync(x => x.PlanId == f.Plan)).All(x => x.FirstProvidedAtUtc == null).ShouldBeTrue();
        });
    }
    [Fact]
    public async Task Learning_Workload_Should_Keep_Overdue_Attempts_Unsealed_And_Cap_Ongoing_Exam_Time()
    {
        var expired = await ExpiredResponseFixtureAsync();
        using (User(expired.Source.Student.UserId)) {
            var result = await Workload.GetMyLearningWorkloadAsync(WorkloadWindow()); var row = result.Items.Single(x => x.Id == expired.RecipientId);
            row.State.ShouldBe(EducationLearningWorkloadState.Overdue); row.EstimatedMinutes.ShouldBe(0);
            await WithUnitOfWorkAsync(async () => (await Repo<EducationAssessmentAttempt>().GetAsync(expired.AttemptId)).SubmissionId.ShouldBeNull());
        }
        var live = await ExpiredResponseFixtureAsync(30000);
        using (User(live.Source.Student.UserId)) {
            var result = await Workload.GetMyLearningWorkloadAsync(WorkloadWindow()); var row = result.Items.Single(x => x.Id == live.RecipientId);
            row.State.ShouldBe(EducationLearningWorkloadState.Ready); row.EstimatedMinutes.ShouldBeInRange(1, 1);
            await WithUnitOfWorkAsync(async () => row.DueAtUtc.ShouldBe((await Repo<EducationAssessmentAttempt>().GetAsync(live.AttemptId)).ExpiresAtUtc));
        }
    }
    [Fact]
    public async Task Learning_Workload_Should_Keep_Started_Stopped_Binding_Once_And_Remove_It_Only_After_Real_Submission()
    {
        var f = await FormalFixtureAsync(); EducationStudentAssessmentAttemptDto attempt;
        using (User(f.Source.Source.Student.UserId)) attempt = await Assessments.StartOrResumeAsync(f.RecipientId, new() { OperationId = Guid.NewGuid() });
        using (User(f.Source.Source.School.Reader)) await Formal.ReviewFormalAssessmentAsync(f.ActivityId, FormalReview((await Formal.GetFormalAssessmentAsync(f.ActivityId))!, false, EducationLearningExposureUse.Unverified));
        using var student = User(f.Source.Source.Student.UserId);
        var result = await Workload.GetMyLearningWorkloadAsync(WorkloadWindow()); result.Items.Single(x => x.Id == f.RecipientId).State.ShouldBe(EducationLearningWorkloadState.Ready);
        result.Items.Count(x => x.Kind == EducationLearningWorkloadKind.PlanActivity).ShouldBe(2);
        await SubmitReviewedAsync(attempt.Id, new() { OperationId = Guid.NewGuid(), PaperSnapshotVersion = attempt.PaperSnapshotVersion, ExpectedResponseSetVersion = attempt.ResponseSetVersion, ConfirmSavedOnly = true });
        (await Workload.GetMyLearningWorkloadAsync(WorkloadWindow())).Items.ShouldNotContain(x => x.Id == f.RecipientId);
    }
    [Fact]
    public async Task Learning_Workload_Should_Exclude_Completed_Study_Keep_Difficulty_And_Count_Only_The_Current_Publication()
    {
        var f = await StudyAsync(false); var started = await StudyStartedAsync(f); var recipient = StudyRecipient(f); Guid completed;
        using (User(f.Student.UserId)) {
            completed = (await Study.SaveMyProgressAsync(recipient, new() { OperationId = Guid.NewGuid(), ExpectedStudentRevisionId = started.Id, Action = EducationTeachingActivityAction.Completed,
                SharedText = "已上交总结", ConfirmSharingWithTeacher = true, ConfirmActivityOnly = true })).Id;
            (await Workload.GetMyLearningWorkloadAsync(WorkloadWindow())).Items.ShouldBeEmpty();
            await Study.SaveMyProgressAsync(recipient, new() { OperationId = Guid.NewGuid(), ExpectedStudentRevisionId = completed, Action = EducationTeachingActivityAction.Difficulty,
                Difficulty = EducationTeachingActivityDifficulty.Unclear, SharedText = "仍需进一步讲解", ConfirmSharingWithTeacher = true });
            var load = await Workload.GetMyLearningWorkloadAsync(WorkloadWindow()); load.Items.Single().State.ShouldBe(EducationLearningWorkloadState.Blocked); load.Days.Sum(x => x.KnownMinutes).ShouldBe(0);
        }
        using (User(f.School.Reader)) {
            var root = await Study.GetAsync(f.TaskId); var draft = JsonSerializer.Deserialize<SaveEducationStudyTaskDto>(JsonSerializer.Serialize(f.Draft))!; draft.OperationId = Guid.NewGuid(); draft.ExpectedRevisionId = root.Current.Id; draft.ConcurrencyStamp = root.ConcurrencyStamp;
            await Study.SaveDraftAsync(f.TaskId, draft); root = await Study.GetAsync(f.TaskId); var preview = await Study.PreviewRecipientsAsync(f.TaskId);
            await Study.PublishAsync(f.TaskId, new() { OperationId = Guid.NewGuid(), ExpectedRevisionId = root.Current.Id, ConcurrencyStamp = root.ConcurrencyStamp, RosterFingerprint = preview.RosterFingerprint,
                PublicReason = "重新布置学习", ConfirmRecipientsMaterialsAndSafeText = true });
        }
        using (User(f.Student.UserId)) {
            var load = await Workload.GetMyLearningWorkloadAsync(WorkloadWindow()); load.Items.Single().Id.ShouldNotBe(recipient); load.Days.Sum(x => x.KnownMinutes).ShouldBe(20);
            (await Study.GetMyHistoryAsync(recipient, (await Study.OpenMyAsync(recipient)).CurrentStudentRevisionId, new())).Items.ShouldContain(x => x.Id == completed);
        }
        using (User(f.School.Reader)) { var root = await Study.GetAsync(f.TaskId); await Study.WithdrawAsync(f.TaskId, new() { OperationId = Guid.NewGuid(), ExpectedRevisionId = root.Current.Id, ConcurrencyStamp = root.ConcurrencyStamp, PublicReason = "停止新执行", ConfirmStudentVisibleReason = true }); }
        using (User(f.Student.UserId)) (await Workload.GetMyLearningWorkloadAsync(WorkloadWindow())).Items.ShouldBeEmpty();
    }
    [Fact]
    public async Task Learning_Workload_Should_Disclose_Incomplete_Estimate_Without_Plan_Body_When_A_Source_Review_Closes()
    {
        var f = await LearningWorkloadAsync();
        using (User(f.Source.Source.School.Reader)) {
            var overview = await Grades.GetGradingOverviewAsync(f.Source.TaskId); var release = ReleaseInput(overview, overview.ClosureId!.Value, ExamResultVisibility.ScoreOnly); release.Reason = "调整讲评范围"; await Grades.PublishResultsAsync(f.Source.TaskId, release);
        }
        using var student = User(f.Source.Source.Student.UserId); var result = await Workload.GetMyLearningWorkloadAsync(WorkloadWindow());
        result.UnestimatedPlanCount.ShouldBe(1); result.Items.ShouldNotContain(x => x.Kind == EducationLearningWorkloadKind.PlanActivity);
        result.Items.Single(x => x.Kind == EducationLearningWorkloadKind.Correction).State.ShouldBe(EducationLearningWorkloadState.Blocked);
        result.Days.Sum(x => x.KnownMinutes).ShouldBe(40); JsonSerializer.Serialize(result).ShouldNotContain("补充诊断");
        var later = await Workload.GetMyLearningWorkloadAsync(new() { StartsAtUtc = DateTime.UtcNow.Date.AddDays(10), EndsAtUtc = DateTime.UtcNow.Date.AddDays(11) });
        later.UnestimatedPlanCount.ShouldBe(result.UnestimatedPlanCount); later.Items.ShouldBeEmpty();
    }
    private sealed record LearningWorkloadFixture(GradingFixture Source, Guid Plan, Guid Correction, Guid Delivery, StudyFixture Study, Guid AssessmentRecipient);
    private async Task<LearningWorkloadFixture> LearningWorkloadAsync()
    {
        var (f, job) = await PlanSourceAsync(); var plan = Guid.NewGuid(); var correction = Guid.NewGuid(); Guid delivery, recipient;
        using (User(f.Source.School.Reader)) {
            await Plans.SaveDraftAsync(plan, await PlanDraftAsync(f, job)); await ConfirmAndPublishPlanAsync(plan);
            await Corrections.SaveCorrectionDraftAsync(correction, new() { OperationId = Guid.NewGuid(), QuestionGradeId = await EvidenceGradeAsync(f), Content = CorrectionInstructions() });
            await PublishCorrectionAsync(correction); delivery = (await Corrections.GetCorrectionProgressAsync(correction, null)).Delivery.Id;
            var draft = AssessmentInput(f.Source.School, f.Source.Revision.ExamPaperId); draft.RecipientScope = EducationAssessmentRecipientScope.SelectedStudents; draft.StudentUserIds = [f.Source.Student.UserId];
            var task = await Assessments.CreateDraftAsync(draft); var published = await Assessments.PublishAsync(task.Id, AssessmentPublish(await Assessments.PreviewRecipientsAsync(task.Id))); recipient = published.Recipients.Single().Id;
        }
        var study = await StudyAsync(false, existingSchool: f.Source.School, existingStudent: f.Source.Student);
        return new(f, plan, correction, delivery, study, recipient);
    }
    [Fact]
    public async Task Learning_Workload_Should_Combine_Four_Kinds_Without_Providing_Content_Starting_Assessments_Or_Changing_Grades()
    {
        var f = await LearningWorkloadAsync(); using var student = User(f.Source.Source.Student.UserId);
        async Task<string> GradeFactsAsync() => await WithUnitOfWorkAsync(async () => JsonSerializer.Serialize((await Repo<EducationAssessmentGradeRevision>().GetListAsync(x => x.SubmissionId == f.Source.SubmissionId))
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.Kind, x.ContentJson, x.ObtainedScore, x.ResultGradeSetVersion })));
        var gradeFacts = await GradeFactsAsync();
        var result = await Workload.GetMyLearningWorkloadAsync(WorkloadWindow());
        result.Items.Count.ShouldBe(7); result.Items.Select(x => x.Kind).Distinct().Count().ShouldBe(4);
        result.Days.Sum(x => x.KnownMinutes).ShouldBe(70); result.Items.Count(x => x.State == EducationLearningWorkloadState.Blocked).ShouldBe(2);
        result.Items.Where(x => x.Kind == EducationLearningWorkloadKind.Assessment).Select(x => x.Id).Order().ShouldBe(new[] { f.AssessmentRecipient, f.Source.Source.Revision.Recipients.Single().Id }.Order());
        result.Items.ShouldNotContain(x => x.SourceId == f.Source.TaskId);
        result.Items.Single(x => x.Kind == EducationLearningWorkloadKind.Correction).Id.ShouldBe(f.Delivery);
        result.Items.Single(x => x.Kind == EducationLearningWorkloadKind.StudyTask).Id.ShouldBe(StudyRecipient(f.Study));
        result.OmittedKinds.ShouldBeEmpty(); result.UnestimatedPlanCount.ShouldBe(0);
        var json = JsonSerializer.Serialize(result); json.ShouldNotContain("PRIVATE"); json.ShouldNotContain("独立任务原始讲义"); json.ShouldNotContain("检查策略");
        await WithUnitOfWorkAsync(async () => {
            (await Repo<EducationStudyRecipient>().GetAsync(StudyRecipient(f.Study))).FirstProvidedAtUtc.ShouldBeNull();
            (await Repo<EducationCorrectionDelivery>().GetAsync(f.Delivery)).FirstProvidedAtUtc.ShouldBeNull();
            (await Repo<EducationTeachingActivity>().GetListAsync(x => x.PlanId == f.Plan)).All(x => x.FirstProvidedAtUtc == null && x.CurrentStudentRevisionId == null).ShouldBeTrue();
            (await Repo<EducationAssessmentRecipient>().GetAsync(f.AssessmentRecipient)).CurrentAttemptId.ShouldBeNull();
            (await Repo<EducationQuestionExposure>().GetCountAsync()).ShouldBe(0);
        });
        (await Workload.GetMyLearningWorkloadAsync(WorkloadWindow())).Days.Sum(x => x.KnownMinutes).ShouldBe(70);
        (await GradeFactsAsync()).ShouldBe(gradeFacts);
    }
    [Fact]
    public async Task Learning_Workload_Should_Remove_Submitted_Corrections_Readd_Exact_NeedsRevision_Then_Remove_Accepted_Copy()
    {
        var f = await CorrectionFixtureAsync();
        using (User(f.Source.Source.Student.UserId)) {
            (await Workload.GetMyLearningWorkloadAsync(WorkloadWindow())).Days.Sum(x => x.KnownMinutes).ShouldBe(25);
            await Corrections.GetMyCorrectionAsync(f.DeliveryId); await Corrections.SubmitMyCorrectionAsync(f.DeliveryId, await CorrectionSubmitInputAsync(f.DeliveryId));
            (await Workload.GetMyLearningWorkloadAsync(WorkloadWindow())).Items.Single().SourceId.ShouldBe(f.Source.Source.Revision.TaskId);
            var metadata = (await Corrections.GetMyCorrectionListAsync(new())).Items.Single(); metadata.WorkState.ShouldBe(EducationCorrectionWorkState.AwaitingReview); metadata.CurrentReview.ShouldBeNull(); metadata.Instructions.ShouldBeNull();
        }
        using (User(f.Source.Source.School.Reader)) await Corrections.PublishCorrectionReviewAsync(f.DeliveryId, CorrectionReviewInput(await Corrections.GetCorrectionProgressAsync(f.TaskId, null)));
        using (User(f.Source.Source.Student.UserId)) {
            (await Workload.GetMyLearningWorkloadAsync(WorkloadWindow())).Days.Sum(x => x.KnownMinutes).ShouldBe(25);
            var metadata = (await Corrections.GetMyCorrectionListAsync(new())).Items.Single(); metadata.WorkState.ShouldBe(EducationCorrectionWorkState.NeedsRevision); metadata.CurrentReview.ShouldBeNull();
        }
        using (User(f.Source.Source.School.Reader)) {
            var command = CorrectionReviewInput(await Corrections.GetCorrectionProgressAsync(f.TaskId, null)); command.Decision = EducationCorrectionReviewDecision.Accepted;
            await Corrections.PublishCorrectionReviewAsync(f.DeliveryId, command);
        }
        using (User(f.Source.Source.Student.UserId)) (await Workload.GetMyLearningWorkloadAsync(WorkloadWindow())).Items.Single().SourceId.ShouldBe(f.Source.Source.Revision.TaskId);
    }
    [Theory]
    [InlineData(ExamResultVisibility.Hidden)]
    [InlineData(ExamResultVisibility.ScoreOnly)]
    public async Task Learning_Workload_Should_Not_Reveal_Correction_Review_State_When_Original_Review_Is_Closed(ExamResultVisibility visibility)
    {
        var f = await CorrectionFixtureAsync();
        using (User(f.Source.Source.Student.UserId)) { await Corrections.GetMyCorrectionAsync(f.DeliveryId); await Corrections.SubmitMyCorrectionAsync(f.DeliveryId, await CorrectionSubmitInputAsync(f.DeliveryId)); }
        using (User(f.Source.Source.School.Reader)) {
            await Corrections.PublishCorrectionReviewAsync(f.DeliveryId, CorrectionReviewInput(await Corrections.GetCorrectionProgressAsync(f.TaskId, null)));
            var overview = await Grades.GetGradingOverviewAsync(f.Source.TaskId); var input = ReleaseInput(overview, overview.ClosureId!.Value, visibility); input.Reason = "关闭讲评"; await Grades.PublishResultsAsync(f.Source.TaskId, input);
        }
        using var student = User(f.Source.Source.Student.UserId);
        var row = (await Corrections.GetMyCorrectionListAsync(new())).Items.Single(); row.WorkState.ShouldBeNull(); row.CurrentReview.ShouldBeNull();
        var result = await Workload.GetMyLearningWorkloadAsync(WorkloadWindow()); result.Items.Single(x => x.Kind == EducationLearningWorkloadKind.Correction).State.ShouldBe(EducationLearningWorkloadState.Blocked); result.Days.Sum(x => x.KnownMinutes).ShouldBe(10);
        result.Items.Single(x => x.Kind == EducationLearningWorkloadKind.Assessment).SourceId.ShouldBe(f.Source.Source.Revision.TaskId);
        JsonSerializer.Serialize(result).ShouldNotContain("请补充验算");
    }
    [Fact]
    public async Task Learning_Workload_Should_Count_Bound_Formal_Assessment_Once_Using_Actual_Deadline_And_Duration()
    {
        var f = await FormalFixtureAsync(open: false); using var student = User(f.Source.Source.Student.UserId);
        var result = await Workload.GetMyLearningWorkloadAsync(WorkloadWindow()); result.Items.Count.ShouldBe(4);
        var exam = result.Items.Single(x => x.Id == f.RecipientId); exam.Kind.ShouldBe(EducationLearningWorkloadKind.Assessment); exam.EstimatedMinutes.ShouldBe(1); exam.DueAtUtc.ShouldBe(f.Task.EndsAtUtc);
        result.Items.Count(x => x.Kind == EducationLearningWorkloadKind.PlanActivity).ShouldBe(2); result.Days.Sum(x => x.KnownMinutes).ShouldBe(26);
        result.Items.Single(x => x.SourceId == f.Source.Source.Revision.TaskId).Kind.ShouldBe(EducationLearningWorkloadKind.Assessment);
        await WithUnitOfWorkAsync(async () => {
            (await Repo<EducationAssessmentRecipient>().GetAsync(f.RecipientId)).CurrentAttemptId.ShouldBeNull();
            (await Repo<EducationTeachingActivity>().GetAsync(f.ActivityId)).FirstProvidedAtUtc.ShouldBeNull();
        });
    }
    [Fact]
    public async Task Learning_Workload_Should_Show_Future_Study_And_Block_Withdrawn_Material_Without_Opening_It()
    {
        var f = await StudyAsync(true, future: true);
        using (User(f.Student.UserId)) {
            var result = await Workload.GetMyLearningWorkloadAsync(WorkloadWindow()); result.Items.Single().State.ShouldBe(EducationLearningWorkloadState.Scheduled); result.Days.Sum(x => x.KnownMinutes).ShouldBe(20);
        }
        using (User(f.School.Owner)) await _sharing.RevokeAsync(f.ShareId!.Value);
        using (User(f.Student.UserId)) {
            var result = await Workload.GetMyLearningWorkloadAsync(WorkloadWindow()); result.Items.Single().State.ShouldBe(EducationLearningWorkloadState.Blocked); result.Days.Sum(x => x.KnownMinutes).ShouldBe(0);
        }
        await WithUnitOfWorkAsync(async () => (await Repo<EducationStudyRecipient>().GetAsync(StudyRecipient(f))).FirstProvidedAtUtc.ShouldBeNull());
    }
    [Fact]
    public async Task Learning_Workload_Should_Respect_Independent_Source_Permissions_And_Not_Require_Plan_Permission_For_Study()
    {
        var f = await LearningWorkloadAsync(); using var student = User(f.Source.Source.Student.UserId);
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(Arg.Any<string>()).Returns(true);
        checker.IsGrantedAsync(EducationPermissions.TeachingPlanView).Returns(false); checker.IsGrantedAsync(EducationPermissions.AssessmentParticipate).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>()); lazy.LazyGetRequiredService<ICurrentTenant>().Returns(GetRequiredService<ICurrentTenant>());
        lazy.LazyGetRequiredService<IUnitOfWorkManager>().Returns(GetRequiredService<IUnitOfWorkManager>()); lazy.LazyGetRequiredService<IAsyncQueryableExecuter>().Returns(GetRequiredService<IAsyncQueryableExecuter>());
        lazy.LazyGetRequiredService<EducationAssessmentAccessGuard>().Returns(GetRequiredService<EducationAssessmentAccessGuard>());
        lazy.LazyGetRequiredService<IRepository<SchoolMembership, Guid>>().Returns(Repo<SchoolMembership>()); lazy.LazyGetRequiredService<IRepository<StudentEnrollment, Guid>>().Returns(Repo<StudentEnrollment>());
        lazy.LazyGetRequiredService<IRepository<SchoolClass, Guid>>().Returns(Repo<SchoolClass>()); lazy.LazyGetRequiredService<IRepository<AcademicTerm, Guid>>().Returns(Repo<AcademicTerm>()); lazy.LazyGetRequiredService<IRepository<School, Guid>>().Returns(Repo<School>());
        lazy.LazyGetRequiredService<IRepository<EducationStudyRecipient, Guid>>().Returns(Repo<EducationStudyRecipient>()); lazy.LazyGetRequiredService<IRepository<EducationStudyTask, Guid>>().Returns(Repo<EducationStudyTask>());
        lazy.LazyGetRequiredService<IRepository<EducationStudyTaskRevision, Guid>>().Returns(Repo<EducationStudyTaskRevision>()); lazy.LazyGetRequiredService<EducationStudyTaskAppService>().Returns(GetRequiredService<EducationStudyTaskAppService>());
        var service = new EducationTeachingPlanAppService { LazyServiceProvider = lazy }; var result = await service.GetMyLearningWorkloadAsync(WorkloadWindow());
        result.Items.Single().Kind.ShouldBe(EducationLearningWorkloadKind.StudyTask); result.Days.Sum(x => x.KnownMinutes).ShouldBe(20);
        result.OmittedKinds.Order().ShouldBe(new[] { EducationLearningWorkloadKind.PlanActivity, EducationLearningWorkloadKind.Correction, EducationLearningWorkloadKind.Assessment }.Order());
        checker.IsGrantedAsync(EducationPermissions.LearningParticipate).Returns(false); await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetMyLearningWorkloadAsync(WorkloadWindow()));
        checker.IsGrantedAsync(EducationPermissions.LearningParticipate).Returns(true, false); await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetMyLearningWorkloadAsync(WorkloadWindow()));
    }
    [Fact]
    public async Task Learning_Workload_Should_Omit_Ended_Student_Relations_And_Reject_Another_Tenant()
    {
        var f = await LearningWorkloadAsync();
        using (User(f.Source.Absent.UserId)) {
            var result = await Workload.GetMyLearningWorkloadAsync(WorkloadWindow()); result.Items.Single().Kind.ShouldBe(EducationLearningWorkloadKind.StudyTask);
            result.Items.ShouldNotContain(x => x.Id == f.Delivery || x.SourceId == f.Plan || x.Id == f.AssessmentRecipient);
        }
        await WithUnitOfWorkAsync(async () => { var row = await Repo<StudentEnrollment>().GetAsync(f.Source.Source.Student.EnrollmentId); row.EndAt(DateTime.UtcNow.AddSeconds(-1)); await Repo<StudentEnrollment>().UpdateAsync(row, autoSave: true); });
        using var student = User(f.Source.Source.Student.UserId); (await Workload.GetMyLearningWorkloadAsync(WorkloadWindow())).Items.ShouldBeEmpty();
        using var tenant = GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()); await Should.ThrowAsync<AbpAuthorizationException>(() => Workload.GetMyLearningWorkloadAsync(WorkloadWindow()));
    }
    [Fact]
    public async Task Learning_Workload_Should_Use_Half_Open_UTC_Window_And_Reject_Invalid_Ranges()
    {
        var f = await StudyAsync(false); using var student = User(f.Student.UserId); var due = f.Publication.Content!.EndsAtUtc;
        (await Workload.GetMyLearningWorkloadAsync(new() { StartsAtUtc = due.AddDays(-1), EndsAtUtc = due })).Items.ShouldBeEmpty();
        (await Workload.GetMyLearningWorkloadAsync(new() { StartsAtUtc = due, EndsAtUtc = due.AddDays(1) })).Items.Count.ShouldBe(1);
        await Should.ThrowAsync<BusinessException>(() => Workload.GetMyLearningWorkloadAsync(new() { StartsAtUtc = DateTime.Now, EndsAtUtc = DateTime.Now.AddDays(1) }));
        await Should.ThrowAsync<BusinessException>(() => Workload.GetMyLearningWorkloadAsync(new() { StartsAtUtc = due, EndsAtUtc = due.AddDays(32) }));
        await Should.ThrowAsync<AbpValidationException>(() => Workload.GetMyLearningWorkloadAsync(null!));
    }
}
