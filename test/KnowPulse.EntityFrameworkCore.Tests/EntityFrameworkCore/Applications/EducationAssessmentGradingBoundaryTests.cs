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
using Volo.Abp.Identity;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Fact]
    public async Task Grading_Review_Should_Reject_A_Superseded_Release_But_Recover_An_Already_Committed_Request()
    {
        var f = await GradingFixtureAsync(subjective: false);
        EducationAssessmentResultReleaseDto release;
        using (User(f.Source.School.Reader))
        {
            await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
            var closure = await Grades.CloseTaskAsync(f.TaskId, CloseInput(await Grades.PreviewFinalizationAsync(f.TaskId)));
            release = await Grades.PublishResultsAsync(f.TaskId, ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), closure.Id, ExamResultVisibility.FullReview));
        }
        var committed = new RequestEducationAssessmentScoreReviewDto { OperationId = Guid.NewGuid(), ResultReleaseId = release.Id, Reason = "请复核原成绩" };
        using (User(f.Source.Student.UserId)) await Grades.RequestScoreReviewAsync(f.RecipientId, committed);
        using (User(f.Source.School.Reader))
        {
            var hide = ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), release.ClosureId, ExamResultVisibility.Hidden);
            hide.Reason = "重新核对后再开放";
            await Grades.PublishResultsAsync(f.TaskId, hide);
        }
        using (User(f.Source.Student.UserId))
        {
            (await Grades.GetMyResultAsync(f.RecipientId)).CurrentResultVisibility.ShouldBe(ExamResultVisibility.Hidden);
            (await Grades.RequestScoreReviewAsync(f.RecipientId, committed)).Id.ShouldBe(committed.OperationId);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Grades.RequestScoreReviewAsync(f.RecipientId,
                new() { OperationId = Guid.NewGuid(), ResultReleaseId = release.Id, Reason = "旧页面发起的新申请" }));
        }
        await WithUnitOfWorkAsync(async () =>
            (await Repo<EducationAssessmentScoreReviewRequest>().GetListAsync(x => x.TaskId == f.TaskId)).Count.ShouldBe(1));
    }

    [Fact]
    public async Task Grading_Draft_And_Confirmation_Replays_Should_Return_Their_Original_Revisions_Without_Overwriting_A_Newer_Draft()
    {
        var f = await GradingFixtureAsync(subjective: true);
        using var teacher = User(f.Source.School.Reader);
        var sheet = await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
        var question = sheet.Questions.Single();
        var input = new SaveEducationAssessmentGradeDraftDto { OperationId = Guid.NewGuid(), PaperQuestionId = question.Id,
            ConcurrencyStamp = question.Grade.ConcurrencyStamp, ExpectedGradeRevisionId = question.Grade.CurrentRevisionId,
            PartMarks = question.Grade.Draft!.Content.Parts.Select(x => new EducationAssessmentPartMark { PartId = x.PartId, ObtainedScore = x.MaxScore }).ToList(),
            Feedback = "首个已保存草稿" };
        var draft = await Grades.SaveGradeDraftAsync(f.SubmissionId, input);
        sheet = await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
        var confirm = new ConfirmEducationAssessmentGradeDto { OperationId = Guid.NewGuid(), ConcurrencyStamp = sheet.Questions.Single().Grade.ConcurrencyStamp,
            ExpectedDraftRevisionId = draft.Id, ExpectedGradeSetVersion = sheet.ConfirmedGradeSetVersion };
        var confirmed = await Grades.ConfirmGradeAsync(question.Grade.Id, confirm);
        sheet = await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
        var newer = await Grades.SaveGradeDraftAsync(f.SubmissionId, new() { OperationId = Guid.NewGuid(), PaperQuestionId = question.Id,
            ConcurrencyStamp = sheet.Questions.Single().Grade.ConcurrencyStamp, ExpectedGradeRevisionId = sheet.Questions.Single().Grade.CurrentRevisionId,
            PartMarks = input.PartMarks, Feedback = "新的待确认评语" });

        (await Grades.SaveGradeDraftAsync(f.SubmissionId, input)).Id.ShouldBe(draft.Id);
        (await Grades.ConfirmGradeAsync(question.Grade.Id, confirm)).Id.ShouldBe(confirmed.Id);
        (await Grades.GetGradeOperationAsync(f.SubmissionId, draft.Id))!.Feedback.ShouldBe(input.Feedback);
        var current = (await Grades.GetSubmissionForGradingAsync(f.SubmissionId)).Questions.Single().Grade;
        current.Draft!.Id.ShouldBe(newer.Id); current.Confirmed!.Id.ShouldBe(confirmed.Id);
        input.Feedback = "同操作号不能更换命令";
        (await Should.ThrowAsync<BusinessException>(() => Grades.SaveGradeDraftAsync(f.SubmissionId, input))).Code.ShouldBe(EducationAssessmentErrorCodes.ConflictingOperation);
        input.OperationId = Guid.NewGuid();
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Grades.SaveGradeDraftAsync(f.SubmissionId, input));
        confirm.OperationId = Guid.NewGuid();
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Grades.ConfirmGradeAsync(question.Grade.Id, confirm));
        (await Grades.GetSubmissionForGradingAsync(f.SubmissionId)).Questions.Single().Grade.CurrentRevisionId.ShouldBe(newer.Id);
    }

    [Fact]
    public async Task Grading_Release_Should_Reject_A_Changed_Confirmed_Grade_Set_And_Keep_The_Current_Official_Release()
    {
        var f = await GradingFixtureAsync(subjective: true);
        using var teacher = User(f.Source.School.Reader);
        await ConfirmManualGradeAsync(f.SubmissionId, 0, null);
        var closure = await Grades.CloseTaskAsync(f.TaskId, CloseInput(await Grades.PreviewFinalizationAsync(f.TaskId)));
        var stale = ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), closure.Id, ExamResultVisibility.FullReview);
        await ConfirmManualGradeAsync(f.SubmissionId, 1, "核对原始作答后更正");
        // Supply a fresh row stamp to isolate the confirmed-set guard from the row CAS guard.
        stale.ConcurrencyStamp = (await Grades.GetGradingOverviewAsync(f.TaskId)).ConcurrencyStamp;
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Grades.PublishResultsAsync(f.TaskId, stale));
        using (User(f.Source.Student.UserId)) (await Grades.GetMyResultAsync(f.RecipientId)).TotalScore.ShouldBeNull();
        var input = ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), closure.Id, ExamResultVisibility.FullReview);
        var release = await Grades.PublishResultsAsync(f.TaskId, input);
        await ConfirmManualGradeAsync(f.SubmissionId, 0, "复核后恢复评分");
        var correctedId = (await Grades.GetGradingOverviewAsync(f.TaskId)).CurrentResultReleaseId;
        correctedId.ShouldNotBe(release.Id);
        (await Grades.PublishResultsAsync(f.TaskId, input)).Id.ShouldBe(release.Id);
        (await Grades.GetGradingOverviewAsync(f.TaskId)).CurrentResultReleaseId.ShouldBe(correctedId);
        input.OperationId = Guid.NewGuid(); input.Reason = "旧预览不能覆盖新正式分";
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Grades.PublishResultsAsync(f.TaskId, input));
        using (User(f.Source.Student.UserId)) (await Grades.GetMyResultAsync(f.RecipientId)).TotalScore.ShouldBe(10);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Grading_Endpoints_Should_Reject_Explicitly_Denied_Functional_Permissions(bool teacherMode)
    {
        var permission = teacherMode ? EducationPermissions.AssessmentManage : EducationPermissions.AssessmentParticipate;
        var checker = Substitute.For<IPermissionChecker>();
        checker.IsGrantedAsync(permission).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>();
        lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>());
        lazy.LazyGetRequiredService<IUnitOfWorkManager>().Returns(GetRequiredService<IUnitOfWorkManager>());
        var denied = new EducationAssessmentAppService { LazyServiceProvider = lazy };
        var id = Guid.NewGuid();
        Func<Task>[] calls = teacherMode ?
        [
            () => denied.GetGradingOverviewAsync(id), () => denied.GetSubmissionForGradingAsync(id),
            () => denied.DownloadSubmissionImageAsync(id, id), () => denied.SaveGradeDraftAsync(id, new()),
            () => denied.ConfirmGradeAsync(id, new()), () => denied.GetGradeOperationAsync(id, id),
            () => denied.PreviewFinalizationAsync(id), () => denied.CloseTaskAsync(id, new()),
            () => denied.GetClosureOperationAsync(id, id), () => denied.PublishResultsAsync(id, new()),
            () => denied.GetReleaseOperationAsync(id, id), () => denied.GetScoreReviewsAsync(id, new()),
            () => denied.ResolveScoreReviewAsync(id, new()), () => denied.GetScoreReviewResolutionAsync(id, id)
        ] : [() => denied.GetMyResultAsync(id), () => denied.RequestScoreReviewAsync(id, new()), () => denied.GetMyScoreReviewAsync(id)];
        foreach (var call in calls) await Should.ThrowAsync<AbpAuthorizationException>(call);
        await checker.Received(calls.Length).IsGrantedAsync(permission);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Grading_Operation_Recovery_Should_Recheck_The_Current_Account_And_Teaching_Relation(bool disableAccount)
    {
        var f = await GradingFixtureAsync(subjective: false);
        using var teacher = User(f.Source.School.Reader);
        var sheet = await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
        var revisionId = sheet.Questions.Single().Grade.Confirmed!.Id;
        await WithUnitOfWorkAsync(async () =>
        {
            if (disableAccount)
            {
                var account = await Repo<IdentityUser>().GetAsync(f.Source.School.Reader);
                account.SetIsActive(false); await Repo<IdentityUser>().UpdateManyAsync([account], autoSave: true);
            }
            else
            {
                var assignment = await Repo<TeachingAssignment>().GetAsync(f.Source.Revision.TeachingAssignmentId);
                assignment.EndAt(DateTime.UtcNow); await Repo<TeachingAssignment>().UpdateManyAsync([assignment], autoSave: true);
            }
        });
        await Should.ThrowAsync<AbpAuthorizationException>(() => Grades.GetGradeOperationAsync(f.SubmissionId, revisionId));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Grades.GetGradingOverviewAsync(f.TaskId));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Grades.GetSubmissionForGradingAsync(f.SubmissionId));
        await WithUnitOfWorkAsync(async () =>
            (await Repo<EducationAssessmentGradeRevision>().GetListAsync(x => x.SubmissionId == f.SubmissionId)).Count.ShouldBe(1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Grading_Full_Review_Should_Redact_Private_Mappings_Without_Changing_The_Teacher_Frozen_Rubric(bool subjective)
    {
        var f = await GradingFixtureAsync(subjective);
        using (User(f.Source.School.Reader))
        {
            if (subjective) await ConfirmManualGradeAsync(f.SubmissionId, 1, null);
            else await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
            var closure = await Grades.CloseTaskAsync(f.TaskId, CloseInput(await Grades.PreviewFinalizationAsync(f.TaskId)));
            await Grades.PublishResultsAsync(f.TaskId, ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), closure.Id, ExamResultVisibility.FullReview));
        }
        using (User(f.Source.Student.UserId))
        {
            var result = await Grades.GetMyResultAsync(f.RecipientId);
            var rubric = result.Questions.Single().ScoringDefinition!;
            rubric.Blanks.All(x => x.TextbookKnowledgePointMappingId == null).ShouldBeTrue();
            rubric.ScoringPoints.All(x => x.TextbookKnowledgePointMappingId == null).ShouldBeTrue();
            JsonSerializer.Serialize(result).ShouldNotContain(f.Source.School.MappingA.ToString());
            JsonSerializer.Serialize(result).ShouldNotContain(f.Source.School.MappingB.ToString());
            result.TotalScore.ShouldBe(subjective ? 8 : 10);
        }
        using (User(f.Source.School.Reader))
        {
            var question = (await Grades.GetSubmissionForGradingAsync(f.SubmissionId)).Questions.Single();
            var mappings = question.ScoringDefinition!.Blanks.Select(x => x.TextbookKnowledgePointMappingId)
                .Concat(question.ScoringDefinition.ScoringPoints.Select(x => x.TextbookKnowledgePointMappingId));
            mappings.Order().ShouldBe(new Guid?[] { f.Source.School.MappingA, f.Source.School.MappingB }.Order());
            question.Grade.Confirmed!.Content.MappingEvidence.Sum(x => x.ObtainedScore).ShouldBe(subjective ? 8 : 10);
        }
    }
}
