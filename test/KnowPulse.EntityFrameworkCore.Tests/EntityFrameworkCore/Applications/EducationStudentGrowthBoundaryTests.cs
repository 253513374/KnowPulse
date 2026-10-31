using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Fact]
    public async Task Student_Growth_Should_Reject_Denied_Functional_Permission_Before_All_Six_Operations()
    {
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(EducationPermissions.AssessmentParticipate).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>()); lazy.LazyGetRequiredService<IUnitOfWorkManager>().Returns(GetRequiredService<IUnitOfWorkManager>());
        var service = new EducationStudentGrowthAppService { LazyServiceProvider = lazy }; var id = Guid.NewGuid();
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetMyContextsAsync());
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetMyGrowthAsync(id, new()));
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetMyPointEvidenceAsync(id, id, id, new()));
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetMyContextsForSchoolAsync(id));
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetMyGrowthForSchoolAsync(id, id, new()));
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetMyPointEvidenceForSchoolAsync(id, id, id, id, new()));
        await checker.Received(6).IsGrantedAsync(EducationPermissions.AssessmentParticipate);
    }

    [Fact]
    public async Task Student_Growth_Should_Retain_Known_Earlier_Hidden_Attempts_Without_Using_Their_Grades_Or_Public_Version_Signals()
    {
        var first = await GradingFixtureAsync(false); var second = await GradingFixtureAsync(false, sourceOverride: first.Source);
        var a = await EvidenceGradeAsync(first); var b = await EvidenceGradeAsync(second);
        using (User(first.Source.School.Reader)) {
            await Evidence.SaveEvidenceQualificationAsync(b, IndependentEvidence());
            await Evidence.SaveOriginRelationAsync(a, new() { OperationId = Guid.NewGuid(), SecondQuestionGradeId = b, IsEnabled = true, Reason = "已知是同题变式" });
        }
        await ExposureReleaseAsync(first, ExamResultVisibility.Hidden); await ExposureReleaseAsync(second, ExamResultVisibility.ScoreOnly);
        var window = GrowthWindow(); Guid context; EducationStudentGrowthValue before;
        using (User(first.Source.Student.UserId)) {
            context = (await Growth.GetMyContextsAsync()).Items.Single().Id; before = await Growth.GetMyGrowthAsync(context, window);
            before.Assessments.Single().TaskId.ShouldBe(second.TaskId);
            before.Points.All(x => x.Ratio == null && x.IndependentQuestionCount == 0 && x.PracticeQuestionCount == 1).ShouldBeTrue();
        }
        using (User(first.Source.School.Reader)) { await ConfirmManualGradeAsync(first.SubmissionId, 1, "不开放的历史分更正"); await Evidence.SaveEvidenceQualificationAsync(a, IndependentEvidence()); }
        using (User(first.Source.Student.UserId)) (await Growth.GetMyGrowthAsync(context, window)).SnapshotId.ShouldBe(before.SnapshotId);
    }

    [Fact]
    public async Task Student_Growth_Should_Honor_Subject_Rule_Changes_And_Keep_Actual_Zero_Different_From_Missing_Evidence()
    {
        var f = await GradingFixtureAsync(false); var school = f.Source.School; var grade = await EvidenceGradeAsync(f);
        using (User(school.Reader)) {
            await Evidence.SaveEvidenceQualificationAsync(grade, IndependentEvidence());
            var current = await Grades.GetSubmissionForGradingAsync(f.SubmissionId); var q = current.Questions.Single();
            var draft = await Grades.SaveGradeDraftAsync(f.SubmissionId, new() { OperationId = Guid.NewGuid(), PaperQuestionId = q.Id,
                ConcurrencyStamp = q.Grade.ConcurrencyStamp, ExpectedGradeRevisionId = q.Grade.CurrentRevisionId,
                PartMarks = q.Grade.Confirmed!.Content.Parts.Select(x => new EducationAssessmentPartMark { PartId = x.PartId, ObtainedScore = 0 }).ToList() });
            var next = await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
            await Grades.ConfirmGradeAsync(q.Grade.Id, new() { OperationId = Guid.NewGuid(), ConcurrencyStamp = next.Questions.Single().Grade.ConcurrencyStamp,
                ExpectedDraftRevisionId = draft.Id, ExpectedGradeSetVersion = next.ConfirmedGradeSetVersion, Reason = "逐空复核后确认零分" });
        }
        await ExposureReleaseAsync(f, ExamResultVisibility.ScoreOnly);
        var window = GrowthWindow(); Guid context; EducationStudentGrowthValue before;
        using (User(f.Source.Student.UserId)) {
            context = (await Growth.GetMyContextsAsync()).Items.Single().Id; before = await Growth.GetMyGrowthAsync(context, window);
            before.Assessments.Single().TotalScore.ShouldBe(0); before.Points.All(x => x.Ratio == 0 && x.State == EducationMasteryState.InsufficientEvidence).ShouldBeTrue();
        }
        var subject = await WithUnitOfWorkAsync(async () => { await Repo<SchoolMembership>().InsertAsync(new(Guid.NewGuid(), null, school.SchoolId, school.Owner, SchoolMemberRole.Manager, DateTime.UtcNow.AddMinutes(-1)), autoSave: true); return (await Repo<TextbookVersion>().GetAsync(school.BookId)).EducationSubjectId; });
        using (User(school.Owner)) await MasteryRules.SaveAsync(school.SchoolId, subject, new() { OperationId = Guid.NewGuid(), IsEnabled = true, WindowDays = 30,
            MinimumIndependentQuestionCount = 1, MinimumAssessmentCount = 1, MaximumLatestEvidenceAgeDays = 30, TargetRatio = .8m,
            Reason = "采用试点学校规则", ConfirmSchoolPolicy = true });
        using (User(f.Source.Student.UserId)) {
            var after = await Growth.GetMyGrowthAsync(context, window); after.SnapshotId.ShouldNotBe(before.SnapshotId); after.RuleMissing.ShouldBeFalse();
            after.Points.All(x => x.Ratio == 0 && x.State == EducationMasteryState.NeedsConsolidation).ShouldBeTrue(); after.Rule!.TargetRatio.ShouldBe(.8m);
            await Should.ThrowAsync<BusinessException>(() => Growth.GetMyPointEvidenceAsync(context, before.SnapshotId, before.Points[0].Id, new()));
        }
    }

    [Fact]
    public async Task Student_Growth_Should_Indicate_Changed_Scoring_Basis_Without_Inflating_Known_Repeat_Evidence()
    {
        var a = await GradingFixtureAsync(false); var b = await GradingFixtureAsync(false, sourceOverride: a.Source);
        var firstGrade = await EvidenceGradeAsync(a); var secondGrade = await EvidenceGradeAsync(b);
        using (User(a.Source.School.Reader)) {
            await Evidence.SaveEvidenceQualificationAsync(firstGrade, IndependentEvidence()); await Evidence.SaveEvidenceQualificationAsync(secondGrade, IndependentEvidence());
            await Evidence.SaveOriginRelationAsync(firstGrade, new() { OperationId = Guid.NewGuid(), SecondQuestionGradeId = secondGrade, IsEnabled = true, Reason = "同题不同冻结定义" });
        }
        await ExposureReleaseAsync(a, ExamResultVisibility.ScoreOnly); await ExposureReleaseAsync(b, ExamResultVisibility.ScoreOnly);
        using (User(a.Source.Student.UserId)) {
            var context = (await Growth.GetMyContextsAsync()).Items.Single().Id; var value = await Growth.GetMyGrowthAsync(context, GrowthWindow());
            value.Assessments.Count.ShouldBe(2); value.Assessments[0].ComparisonLimit.ShouldBe(EducationGrowthComparisonLimit.First);
            value.Assessments[1].ComparisonLimit.ShouldBe(EducationGrowthComparisonLimit.Scoring);
            value.Points.All(x => x.IndependentQuestionCount == 1 && x.PracticeQuestionCount == 1 && x.MaxScore > 0).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Student_Growth_Should_Block_Current_And_Cached_Data_After_Enrollment_Ends()
    {
        var f = await GradingFixtureAsync(false); await ExposureReleaseAsync(f, ExamResultVisibility.FullReview); var window = GrowthWindow(); Guid context; EducationStudentGrowthValue before;
        using (User(f.Source.Student.UserId)) { context = (await Growth.GetMyContextsAsync()).Items.Single().Id; before = await Growth.GetMyGrowthAsync(context, window); }
        await WithUnitOfWorkAsync(async () => { var enrollment = await Repo<StudentEnrollment>().GetAsync(f.Source.Student.EnrollmentId); enrollment.EndAt(DateTime.UtcNow); await Repo<StudentEnrollment>().UpdateManyAsync([enrollment], autoSave: true); });
        using (User(f.Source.Student.UserId)) {
            (await Growth.GetMyContextsAsync()).Items.ShouldBeEmpty(); await Should.ThrowAsync<AbpAuthorizationException>(() => Growth.GetMyGrowthAsync(context, window));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Growth.GetMyPointEvidenceAsync(context, before.SnapshotId, before.Points[0].Id, new()));
        }
    }

    [Fact]
    public async Task Student_Growth_Should_Allow_Factual_Comparison_On_The_Same_Frozen_Paper_While_Repeats_Remain_Practice()
    {
        var first = await GradingFixtureAsync(true); var second = await GradingFixtureAsync(true, sourceOverride: first.Source);
        using (User(first.Source.School.Reader)) {
            await Grades.GetSubmissionForGradingAsync(first.SubmissionId); await ConfirmManualGradeAsync(first.SubmissionId, 0, null);
            await Grades.GetSubmissionForGradingAsync(second.SubmissionId); await ConfirmManualGradeAsync(second.SubmissionId, 0, null);
            await Evidence.SaveEvidenceQualificationAsync(await EvidenceGradeAsync(first), IndependentEvidence());
            await Evidence.SaveEvidenceQualificationAsync(await EvidenceGradeAsync(second), IndependentEvidence());
        }
        await ExposureReleaseAsync(first, ExamResultVisibility.ScoreOnly); await ExposureReleaseAsync(second, ExamResultVisibility.ScoreOnly);
        using (User(first.Source.Student.UserId)) {
            var context = (await Growth.GetMyContextsAsync()).Items.Single().Id; var value = await Growth.GetMyGrowthAsync(context, GrowthWindow());
            value.Assessments.Count.ShouldBe(2); value.Assessments[1].ComparisonLimit.ShouldBe(EducationGrowthComparisonLimit.None);
            value.Points.All(x => x.IndependentQuestionCount == 1 && x.PracticeQuestionCount == 1).ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Student_Growth_Should_Honor_Scheduled_Score_And_Review_Gates(bool futureScore)
    {
        var f = await GradingFixtureAsync(false);
        using (User(f.Source.School.Reader)) {
            await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
            var closure = await Grades.CloseTaskAsync(f.TaskId, CloseInput(await Grades.PreviewFinalizationAsync(f.TaskId)));
            var input = ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), closure.Id, ExamResultVisibility.FullReview);
            input.ScoreOpensAtUtc = futureScore ? DateTime.UtcNow.AddHours(1) : DateTime.UtcNow;
            input.ReviewOpensAtUtc = DateTime.UtcNow.AddHours(2); await Grades.PublishResultsAsync(f.TaskId, input);
        }
        using (User(f.Source.Student.UserId)) {
            var contexts = await Growth.GetMyContextsAsync();
            if (futureScore) contexts.Items.ShouldBeEmpty();
            else {
                var context = contexts.Items.Single().Id; var value = await Growth.GetMyGrowthAsync(context, GrowthWindow());
                value.Assessments.Single().Visibility.ShouldBe(ExamResultVisibility.ScoreOnly);
                var basis = await Growth.GetMyPointEvidenceAsync(context, value.SnapshotId, value.Points[0].Id, new()); basis.Items.ShouldBeEmpty(); basis.HasRestrictedEvidence.ShouldBeTrue();
            }
        }
    }
}
