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
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationTeachingPlanActivityAppService Activities => GetRequiredService<IEducationTeachingPlanActivityAppService>();
    private static SaveEducationMyActivityDto ActivityInput(EducationStudentActivityDto row, EducationTeachingActivityAction action, string? text = null) => new() {
        OperationId = Guid.NewGuid(), ExpectedStudentRevisionId = row.CurrentStudentRevisionId, Action = action, SharedText = text,
        ConfirmSharingWithTeacher = text != null, ConfirmActivityOnly = true,
        Difficulty = action == EducationTeachingActivityAction.Difficulty ? EducationTeachingActivityDifficulty.Unclear : null };
    private static SaveEducationActivityFollowUpDto FollowInput(EducationTeacherActivityDto row) => new() { OperationId = Guid.NewGuid(),
        ExpectedRevisionId = row.CurrentRevisionId, ConcurrencyStamp = row.ConcurrencyStamp, ExpectedStudentRevisionId = row.CurrentStudentRevisionId!.Value,
        InternalTeacherNote = "PRIVATE-HANDLING 已约面谈，不向学生发送说明正文", ConfirmHandlingReceipt = true };
    private static GetEducationActivityWorkloadDto WorkloadWindow() => new() { StartsAtUtc = DateTime.UtcNow.Date.AddDays(-1), EndsAtUtc = DateTime.UtcNow.Date.AddDays(4) };

    [Fact]
    public async Task Native_Plan_Should_Filter_School_And_Reject_A_Wrong_School_Before_Providing_Activities()
    {
        var (f, job) = await PlanSourceAsync(); var plan = Guid.NewGuid(); EducationTeachingPlanRevisionDto published;
        using (User(f.Source.School.Reader)) { await Plans.SaveDraftAsync(plan, await PlanDraftAsync(f, job)); published = await ConfirmAndPublishPlanAsync(plan); }
        using var student = User(f.Source.Student.UserId);
        var list = await Plans.GetMyListAsync(new(), f.Source.School.SchoolId);
        list.TotalCount.ShouldBe(1); list.Items.Single().SchoolId.ShouldBe(f.Source.School.SchoolId); list.Items.Single().Content.ShouldBeNull();
        (await Plans.GetMyListAsync(new(), Guid.NewGuid())).TotalCount.ShouldBe(0);
        await Should.ThrowAsync<AbpAuthorizationException>(() => Activities.GetMyActivitiesForSchoolAsync(plan, published.Id, Guid.NewGuid()));
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Activities.GetMyActivitiesForSchoolAsync(plan, Guid.NewGuid(), f.Source.School.SchoolId));
        await WithUnitOfWorkAsync(async () => {
            (await Repo<EducationTeachingActivity>().GetListAsync(x => x.PlanId == plan)).All(x => x.FirstProvidedAtUtc == null).ShouldBeTrue();
            (await Repo<EducationTeachingPlanQuestionExposure>().GetCountAsync()).ShouldBe(0);
            (await Repo<EducationTeachingActivityRevision>().GetCountAsync()).ShouldBe(0);
        });
        var view = await Activities.GetMyActivitiesForSchoolAsync(plan, published.Id, f.Source.School.SchoolId);
        view.Plan.SchoolId.ShouldBe(f.Source.School.SchoolId); view.Plan.Content.ShouldNotBeNull();
        view.Activities.Count.ShouldBe(3); view.Activities.All(x => x.FirstProvidedAtUtc != null && x.MaterialsAvailable).ShouldBeTrue();
        (await Activities.GetMyActivitiesAsync(plan, published.Id)).Plan.VersionId.ShouldBe(published.Id);
    }

    [Fact]
    public async Task Activity_Publication_Should_Assign_Steps_And_Record_Actual_Progress_Without_Changing_Growth()
    {
        var (f, job) = await PlanSourceAsync(); var plan = Guid.NewGuid(); EducationTeachingPlanRevisionDto published;
        using (User(f.Source.School.Reader)) {
            await Plans.SaveDraftAsync(plan, await PlanDraftAsync(f, job)); published = await ConfirmAndPublishPlanAsync(plan);
            var teacher = await Activities.GetActivityProgressAsync(plan); teacher.Activities.Count.ShouldBe(3);
            teacher.Activities.All(x => x.State == EducationTeachingActivityState.NotStarted && x.FirstProvidedAtUtc == null).ShouldBeTrue();
        }
        using var student = User(f.Source.Student.UserId);
        var beforeLoad = await Activities.GetMyActivityWorkloadAsync(WorkloadWindow()); beforeLoad.Days.Sum(x => x.EstimatedMinutes).ShouldBe(40);
        await WithUnitOfWorkAsync(async () => {
            (await Repo<EducationTeachingPlanQuestionExposure>().GetCountAsync()).ShouldBe(0);
            (await Repo<EducationTeachingActivity>().GetListAsync()).All(x => x.FirstProvidedAtUtc == null).ShouldBeTrue();
        });
        var view = await Activities.GetMyActivitiesAsync(plan, published.Id); view.Activities.All(x => x.FirstProvidedAtUtc.HasValue).ShouldBeTrue();
        var row = view.Activities.Single(x => x.StepOrder == 2); var window = GrowthWindow();
        var context = (await Growth.GetMyContextsAsync()).Items.Single().Id; var growth = await Growth.GetMyGrowthAsync(context, window);
        var start = ActivityInput(row, EducationTeachingActivityAction.Started); var first = await Activities.SaveMyActivityAsync(row.Id, start);
        (await Activities.SaveMyActivityAsync(row.Id, start)).Id.ShouldBe(first.Id);
        view = await Activities.GetMyActivitiesAsync(plan, published.Id); row = view.Activities.Single(x => x.Id == row.Id);
        var done = ActivityInput(row, EducationTeachingActivityAction.Completed, "已逐步复盘例题"); done.ReportedMinutes = 7;
        var completed = await Activities.SaveMyActivityAsync(row.Id, done);
        (await Activities.SaveMyActivityAsync(row.Id, start)).Id.ShouldBe(first.Id);
        view = await Activities.GetMyActivitiesAsync(plan, published.Id); row = view.Activities.Single(x => x.Id == row.Id);
        row.State.ShouldBe(EducationTeachingActivityState.ReportedComplete); row.CurrentStudentRevisionId.ShouldBe(completed.Id);
        (await Activities.GetMyActivityWorkloadAsync(WorkloadWindow())).Days.Sum(x => x.EstimatedMinutes).ShouldBe(25);
        (await Growth.GetMyGrowthAsync(context, window)).SnapshotId.ShouldBe(growth.SnapshotId);
        (await Grades.GetMyResultAsync(f.RecipientId)).TotalScore.ShouldBe(10);
        (await Activities.GetMyActivityHistoryAsync(row.Id, completed.Id, new())).TotalCount.ShouldBe(2);
        done.OperationId = Guid.NewGuid(); await Should.ThrowAsync<AbpDbConcurrencyException>(() => Activities.SaveMyActivityAsync(row.Id, done));
    }

    [Fact]
    public async Task Activity_Difficulty_And_Private_Handling_Should_Use_Separate_Public_Student_Versions()
    {
        var (f, plan, published, _) = await DeliveredPlanAsync(); EducationStudentActivityDto row; EducationStudentActivityEventDto difficulty;
        using (User(f.Source.Student.UserId)) {
            row = (await Activities.GetMyActivitiesAsync(plan, published.Id)).Activities.Single(x => x.StepOrder == 2);
            difficulty = await Activities.SaveMyActivityAsync(row.Id, ActivityInput(row, EducationTeachingActivityAction.Difficulty, "看不懂这一步"));
        }
        SaveEducationActivityFollowUpDto handling; EducationTeacherActivityEventDto receipt;
        using (User(f.Source.School.Reader)) {
            var teacher = (await Activities.GetActivityProgressAsync(plan)).Activities.Single(x => x.Id == row.Id);
            handling = FollowInput(teacher); receipt = await Activities.SaveActivityFollowUpAsync(row.Id, handling);
            (await Activities.SaveActivityFollowUpAsync(row.Id, handling)).Id.ShouldBe(receipt.Id);
        }
        using (User(f.Source.Student.UserId)) {
            var view = await Activities.GetMyActivitiesAsync(plan, published.Id); var current = view.Activities.Single(x => x.Id == row.Id);
            current.GetType().ShouldBe(typeof(EducationStudentActivityDto)); current.CurrentStudentRevisionId.ShouldBe(difficulty.Id);
            current.StudentRevisionNumber.ShouldBe(1); current.State.ShouldBe(EducationTeachingActivityState.Blocked); current.TeacherHandledAtUtc.ShouldNotBeNull();
            var json = JsonSerializer.Serialize(view); json.ShouldNotContain("PRIVATE-HANDLING"); json.ShouldNotContain(receipt.Id.ToString());
            var history = await Activities.GetMyActivityHistoryAsync(row.Id, difficulty.Id, new()); history.TotalCount.ShouldBe(1);
            JsonSerializer.Serialize(history).ShouldNotContain("PRIVATE-HANDLING");
            await Should.ThrowAsync<AbpAuthorizationException>(() => Activities.GetMyActivityOperationAsync(plan, receipt.Id));
            var started = await Activities.SaveMyActivityAsync(row.Id, ActivityInput(current, EducationTeachingActivityAction.Started));
            current = (await Activities.GetMyActivitiesAsync(plan, published.Id)).Activities.Single(x => x.Id == row.Id);
            await Activities.SaveMyActivityAsync(row.Id, ActivityInput(current, EducationTeachingActivityAction.Difficulty, "仍需要协助"));
            current = (await Activities.GetMyActivitiesAsync(plan, published.Id)).Activities.Single(x => x.Id == row.Id);
            current.StudentRevisionNumber.ShouldBe(3); current.TeacherHandledAtUtc.ShouldBeNull();
            await Should.ThrowAsync<AbpDbConcurrencyException>(() => Activities.GetMyActivityHistoryAsync(row.Id, started.Id, new()));
        }
        using (User(f.Source.School.Reader)) {
            (await Activities.SaveActivityFollowUpAsync(row.Id, handling)).Id.ShouldBe(receipt.Id);
            handling.OperationId = Guid.NewGuid(); await Should.ThrowAsync<AbpDbConcurrencyException>(() => Activities.SaveActivityFollowUpAsync(row.Id, handling));
            (await Activities.GetActivityHistoryAsync(row.Id, (await Activities.GetActivityProgressAsync(plan)).Activities.Single(x => x.Id == row.Id).CurrentRevisionId, new())).TotalCount.ShouldBe(4);
        }
        using (User(f.Source.School.Owner)) {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Activities.GetActivityProgressAsync(plan));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Activities.GetActivityHistoryAsync(row.Id, receipt.Id, new()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Activities.GetActivityFollowUpOperationAsync(plan, receipt.Id));
        }
    }

    [Fact]
    public async Task Activity_Should_Withhold_Plan_Body_But_Accept_A_Known_Student_Difficulty_When_Source_Changes()
    {
        var (f, plan, published, _) = await DeliveredPlanAsync(); EducationStudentActivityDto row;
        using (User(f.Source.Student.UserId)) row = (await Activities.GetMyActivitiesAsync(plan, published.Id)).Activities.Single(x => x.StepOrder == 2);
        using (User(f.Source.School.Reader)) await ConfirmManualGradeAsync(f.SubmissionId, 1, "改变本人证据");
        using (User(f.Source.Student.UserId)) {
            var view = await Activities.GetMyActivitiesAsync(plan, published.Id); view.Plan.Content.ShouldBeNull();
            view.Plan.Availability.ShouldBe(EducationTeachingPlanAvailability.SourceChanged); view.Activities.Count.ShouldBe(3);
            await Should.ThrowAsync<BusinessException>(() => Activities.SaveMyActivityAsync(row.Id, ActivityInput(row, EducationTeachingActivityAction.Started)));
            var report = await Activities.SaveMyActivityAsync(row.Id, ActivityInput(row, EducationTeachingActivityAction.Difficulty, "方案目前无法打开"));
            (await Activities.GetMyActivityOperationAsync(plan, report.Id))!.SharedText.ShouldBe("方案目前无法打开");
            var load = await Activities.GetMyActivityWorkloadAsync(WorkloadWindow()); load.Days.ShouldBeEmpty(); load.UncountedPlanCount.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Activity_New_Publications_Should_Retain_Old_Facts_And_Stop_New_Writes_To_Superseded_Or_Withdrawn_Steps()
    {
        var (f, plan, published, _) = await DeliveredPlanAsync(); EducationStudentActivityDto row; SaveEducationMyActivityDto original; EducationStudentActivityEventDto saved;
        using (User(f.Source.Student.UserId)) {
            row = (await Activities.GetMyActivitiesAsync(plan, published.Id)).Activities.Single(x => x.StepOrder == 2);
            original = ActivityInput(row, EducationTeachingActivityAction.Difficulty, "请协助");
            saved = await Activities.SaveMyActivityAsync(row.Id, original);
        }
        EducationTeachingPlanRevisionDto replacement;
        using (User(f.Source.School.Reader)) {
            var report = await Reports.RequestAsync(ReportInput(f)); await ReportProcessor.ProcessAsync(report.Id);
            var input = await PlanDraftAsync(f, report); var current = await Plans.GetAsync(plan);
            input.ExpectedRevisionId = current.Current.Id; input.ConcurrencyStamp = current.ConcurrencyStamp;
            await Plans.SaveDraftAsync(plan, input); replacement = await ConfirmAndPublishPlanAsync(plan, "调整活动安排");
            var past = await Activities.GetActivityProgressAsync(plan, published.Id);
            past.IsCurrentPublication.ShouldBeFalse(); past.Activities.Single(x => x.Id == row.Id).State.ShouldBe(EducationTeachingActivityState.Blocked);
            await Should.ThrowAsync<BusinessException>(() => Activities.SaveActivityFollowUpAsync(row.Id, FollowInput(past.Activities.Single(x => x.Id == row.Id))));
        }
        using (User(f.Source.Student.UserId)) {
            (await Activities.SaveMyActivityAsync(row.Id, original)).Id.ShouldBe(saved.Id);
            await Should.ThrowAsync<BusinessException>(() => Activities.SaveMyActivityAsync(row.Id, ActivityInput(row, EducationTeachingActivityAction.Started)));
            var current = await Activities.GetMyActivitiesAsync(plan, replacement.Id); current.Activities.All(x => x.State == EducationTeachingActivityState.NotStarted).ShouldBeTrue();
        }
        using (User(f.Source.School.Reader)) await Plans.WithdrawAsync(plan, PlanTransition(await Plans.GetAsync(plan), "暂停后续执行"));
        using (User(f.Source.Student.UserId)) {
            var ownHistory = await Activities.GetMyActivityHistoryAsync(row.Id, saved.Id, new()); ownHistory.TotalCount.ShouldBe(1);
            (await Activities.GetMyActivityOperationAsync(plan, saved.Id))!.Id.ShouldBe(saved.Id);
            var card = (await Plans.GetMyListAsync(new())).Items.Single();
            var stopped = await Activities.GetMyActivitiesAsync(plan, card.VersionId); stopped.Plan.Content.ShouldBeNull(); stopped.Activities.Count.ShouldBe(3);
            await Should.ThrowAsync<BusinessException>(() => Activities.SaveMyActivityAsync(stopped.Activities.First().Id,
                ActivityInput(stopped.Activities.First(), EducationTeachingActivityAction.Difficulty, "撤回后不能新增反馈")));
            (await Activities.GetMyActivityWorkloadAsync(WorkloadWindow())).Days.ShouldBeEmpty();
        }
        await WithUnitOfWorkAsync(async () => (await Repo<EducationTeachingActivity>().GetCountAsync()).ShouldBe(6));
    }

    [Fact]
    public async Task Activity_Should_Not_Allow_Undelivered_Progress_Or_Self_Report_A_Formal_Assessment_As_Complete()
    {
        var (f, job) = await PlanSourceAsync(ExamResultVisibility.ScoreOnly); var plan = Guid.NewGuid(); EducationTeachingPlanRevisionDto published; Guid activity;
        using (User(f.Source.School.Reader)) { await Plans.SaveDraftAsync(plan, await PlanDraftAsync(f, job)); published = await ConfirmAndPublishPlanAsync(plan); activity = (await Activities.GetActivityProgressAsync(plan)).Activities.First().Id; }
        using (User(f.Source.Student.UserId)) {
            var waiting = await Activities.GetMyActivitiesAsync(plan, published.Id); waiting.Activities.ShouldBeEmpty();
            await Should.ThrowAsync<BusinessException>(() => Activities.SaveMyActivityAsync(activity, new() { OperationId = Guid.NewGuid(), Action = EducationTeachingActivityAction.Difficulty,
                Difficulty = EducationTeachingActivityDifficulty.MaterialUnavailable, SharedText = "未提供活动不能伪造反馈", ConfirmSharingWithTeacher = true }));
        }
        using (User(f.Source.School.Reader)) { var overview = await Grades.GetGradingOverviewAsync(f.TaskId); var input = ReleaseInput(overview, overview.ClosureId!.Value, ExamResultVisibility.FullReview); input.Reason = "开放活动"; await Grades.PublishResultsAsync(f.TaskId, input); }
        using (User(f.Source.Student.UserId)) {
            var view = await Activities.GetMyActivitiesAsync(plan, published.Id);
            foreach (var row in view.Activities.Where(x => x.StepOrder != 2)) {
                var started = await Activities.SaveMyActivityAsync(row.Id, ActivityInput(row, EducationTeachingActivityAction.Started));
                row.CurrentStudentRevisionId = started.Id;
                await Should.ThrowAsync<BusinessException>(() => Activities.SaveMyActivityAsync(row.Id, ActivityInput(row, EducationTeachingActivityAction.Completed)));
            }
            var worked = view.Activities.Single(x => x.StepOrder == 2); var noConsent = ActivityInput(worked, EducationTeachingActivityAction.Difficulty, "此内容不得自动上交"); noConsent.ConfirmSharingWithTeacher = false;
            await Should.ThrowAsync<BusinessException>(() => Activities.SaveMyActivityAsync(worked.Id, noConsent));
        }
    }

    [Theory]
    [InlineData("other-student")]
    [InlineData("teacher")]
    [InlineData("tenant")]
    [InlineData("ended-enrollment")]
    public async Task Activity_Should_Reject_Authority_Changes_For_Progress_History_And_Operation_Recovery(string denied)
    {
        var (f, plan, published, _) = await DeliveredPlanAsync(); EducationStudentActivityDto row; EducationStudentActivityEventDto saved;
        using (User(f.Source.Student.UserId)) { row = (await Activities.GetMyActivitiesAsync(plan, published.Id)).Activities.First(); saved = await Activities.SaveMyActivityAsync(row.Id, ActivityInput(row, EducationTeachingActivityAction.Started)); }
        if (denied == "ended-enrollment") await WithUnitOfWorkAsync(async () => { foreach (var e in await Repo<StudentEnrollment>().GetListAsync(x => x.StudentUserId == f.Source.Student.UserId)) { e.EndAt(DateTime.UtcNow.AddSeconds(-1)); await Repo<StudentEnrollment>().UpdateAsync(e, autoSave: true); } });
        var actorId = denied == "other-student" ? (await AssessmentStudentAsync(f.Source.School)).UserId :
            denied == "teacher" ? f.Source.School.Reader : f.Source.Student.UserId;
        using var actor = User(actorId);
        using var tenant = GetRequiredService<Volo.Abp.MultiTenancy.ICurrentTenant>().Change(denied == "tenant" ? Guid.NewGuid() : null);
        await Should.ThrowAsync<AbpAuthorizationException>(() => Activities.GetMyActivitiesAsync(plan, published.Id));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Activities.GetMyActivityHistoryAsync(row.Id, saved.Id, new()));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Activities.GetMyActivityOperationAsync(plan, saved.Id));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Activities.SaveMyActivityAsync(row.Id, ActivityInput(row, EducationTeachingActivityAction.Difficulty, "有效内容仍须授权")));
    }

    [Fact]
    public async Task Activity_Write_Should_Explicitly_Check_The_Separate_Participation_Permission()
    {
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(Arg.Any<string>()).Returns(true);
        checker.IsGrantedAsync(EducationPermissions.LearningParticipate).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        var service = new EducationTeachingPlanAppService { LazyServiceProvider = lazy };
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.SaveMyActivityAsync(Guid.NewGuid(), new()));
    }

    [Fact]
    public async Task Activity_Workload_Should_Combine_Real_Mathematics_And_Physics_Plans_For_The_Same_Student()
    {
        var (f, _, _, _) = await DeliveredPlanAsync();
        var physics = await WithUnitOfWorkAsync(async () => {
            var selection = await Repo<TeacherTextbookSelection>().GetAsync(f.Source.School.ReaderSelection);
            var math = await Repo<TeachingAssignment>().GetAsync(selection.TeachingAssignmentId); var now = DateTime.UtcNow;
            var subject = new EducationSubject(Guid.NewGuid(), null, "PHY-" + Guid.NewGuid().ToString("N"), "物理");
            await Repo<EducationSubject>().InsertAsync(subject, autoSave: true);
            var assignment = new TeachingAssignment(Guid.NewGuid(), null, math.SchoolId, math.AcademicTermId, math.SchoolClassId, subject.Id,
                f.Source.School.Reader, math.EffectiveFromUtc, math.EffectiveToUtc);
            await Repo<TeachingAssignment>().InsertAsync(assignment, autoSave: true);
            var book = new TextbookVersion(Guid.NewGuid(), null, math.SchoolId, subject.Id, "PHY", 1, 7, "物理教材", "测试出版社", "测试版", "上册");
            await Repo<TextbookVersion>().InsertAsync(book, autoSave: true);
            var chapter = new TextbookChapter(Guid.NewGuid(), null, book.Id, null, "PHY1", "运动", 0);
            var point = new CourseKnowledgePoint(Guid.NewGuid(), null, math.SchoolId, subject.Id, "PHY-KP");
            var revision = new CourseKnowledgePointRevision(Guid.NewGuid(), null, point.Id, point.AdvanceRevision(), "运动描述", null);
            await Repo<TextbookChapter>().InsertAsync(chapter, autoSave: true); await Repo<CourseKnowledgePoint>().InsertAsync(point, autoSave: true);
            await Repo<CourseKnowledgePointRevision>().InsertAsync(revision, autoSave: true);
            var mapping = new TextbookKnowledgePointMapping(Guid.NewGuid(), null, book.Id, chapter.Id, point.Id, revision.Id, f.Source.School.Reader, now);
            await Repo<TextbookKnowledgePointMapping>().InsertAsync(mapping, autoSave: true); book.Publish(now); await Repo<TextbookVersion>().UpdateManyAsync([book], autoSave: true);
            var physicsSelection = new TeacherTextbookSelection(Guid.NewGuid(), null, math.SchoolId, f.Source.School.Reader, assignment.Id, book.Id, now);
            await Repo<TeacherTextbookSelection>().InsertAsync(physicsSelection, autoSave: true);
            return (Selection: physicsSelection.Id, Mapping: mapping.Id, Point: revision.Id);
        });
        using (User(f.Source.School.Reader)) {
            var report = await Reports.RequestAsync(new() { OperationId = Guid.NewGuid(), WindowStartsAtUtc = DateTime.UtcNow.AddDays(-1), WindowEndsAtUtc = DateTime.UtcNow,
                Scope = new() { TeacherTextbookSelectionId = physics.Selection, TextbookKnowledgePointMappingIds = [physics.Mapping] } });
            await ReportProcessor.ProcessAsync(report.Id); var draft = await PlanDraftAsync(f, report);
            draft.Content = draft.Content with { Title = "物理学习安排", TargetPointRevisionIds = [physics.Point] };
            var id = Guid.NewGuid(); await Plans.SaveDraftAsync(id, draft); await ConfirmAndPublishPlanAsync(id);
        }
        using (User(f.Source.Student.UserId)) {
            var workload = await Activities.GetMyActivityWorkloadAsync(WorkloadWindow());
            workload.Days.Where(x => x.SubjectName == "数学").Sum(x => x.EstimatedMinutes).ShouldBe(40);
            workload.Days.Where(x => x.SubjectName == "物理").Sum(x => x.EstimatedMinutes).ShouldBe(40);
            workload.Days.Sum(x => x.ActivityCount).ShouldBe(6); workload.UncountedPlanCount.ShouldBe(0);
        }
    }
}
