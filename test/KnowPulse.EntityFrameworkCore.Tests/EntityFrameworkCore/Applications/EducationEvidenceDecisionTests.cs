using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Education;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Identity;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationAssessmentEvidenceAppService Evidence => GetRequiredService<IEducationAssessmentEvidenceAppService>();
    private async Task<Guid> EvidenceGradeAsync(GradingFixture f)
    { using var teacher = User(f.Source.School.Reader); return (await Grades.GetSubmissionForGradingAsync(f.SubmissionId)).Questions.Single().Grade.Id; }
    private static SaveEducationEvidenceQualificationDto IndependentEvidence() => new()
    { OperationId = Guid.NewGuid(), Use = EducationLearningEvidenceUse.Independent, NoHintsOrAnswersConfirmed = true,
      NewRequirementsConfirmed = true, OriginalRelationshipsChecked = true, Reason = "已核对原始答卷、前次接触和考察要求" };

    [Fact]
    public async Task Evidence_Qualification_Should_Default_To_Unverified_Keep_Versions_And_Never_Change_A_Grade()
    {
        var f = await GradingFixtureAsync(false); var gradeId = await EvidenceGradeAsync(f);
        using var teacher = User(f.Source.School.Reader);
        (await Evidence.GetEvidenceAsync(gradeId)).Current.ShouldBeNull();
        await WithUnitOfWorkAsync(async () => (await Repo<EducationEvidenceQualification>().GetCountAsync()).ShouldBe(0));
        var input = IndependentEvidence(); var first = await Evidence.SaveEvidenceQualificationAsync(gradeId, input);
        (await Evidence.SaveEvidenceQualificationAsync(gradeId, input)).Id.ShouldBe(first.Id);
        var current = await Evidence.GetEvidenceAsync(gradeId);
        var correction = new SaveEducationEvidenceQualificationDto { OperationId = Guid.NewGuid(), ExpectedRevisionId = first.Id, ConcurrencyStamp = current.ConcurrencyStamp,
            Use = EducationLearningEvidenceUse.Practice, Reason = "发现此前已做过同题，保留为练习" };
        var second = await Evidence.SaveEvidenceQualificationAsync(gradeId, correction);
        (await Evidence.SaveEvidenceQualificationAsync(gradeId, input)).Id.ShouldBe(first.Id);
        (await Evidence.GetEvidenceAsync(gradeId)).Current!.Id.ShouldBe(second.Id);
        (await Evidence.GetQualificationOperationAsync(gradeId, first.Id))!.Use.ShouldBe(EducationLearningEvidenceUse.Independent);
        input.Reason = "同一操作更改内容";
        (await Should.ThrowAsync<BusinessException>(() => Evidence.SaveEvidenceQualificationAsync(gradeId, input))).Code.ShouldBe(EducationAssessmentErrorCodes.ConflictingOperation);
        correction.OperationId = Guid.NewGuid();
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Evidence.SaveEvidenceQualificationAsync(gradeId, correction));
        await GetRequiredService<EducationAssessmentGradingProcessor>().InitializeAsync(f.SubmissionId);
        (await Grades.GetSubmissionForGradingAsync(f.SubmissionId)).Questions.Single().Grade.Confirmed!.ObtainedScore.ShouldBe(10);
        await WithUnitOfWorkAsync(async () =>
        {
            (await Repo<EducationEvidenceQualificationRevision>().GetListAsync(x => x.QuestionGradeId == gradeId)).Count.ShouldBe(2);
            var original = await Repo<EducationEvidenceQualificationRevision>().GetAsync(first.Id);
            var row = new EducationLearningQuestionEvidence { QuestionGradeId = gradeId, StudentUserId = f.Source.Student.UserId, QuestionDefinitionRevisionId = original.QuestionDefinitionRevisionId };
            original.ApplyTo(row).QualificationRevisionId.ShouldBe(first.Id);
            await Should.ThrowAsync<BusinessException>(() => Task.FromResult(original.ApplyTo(row with { StudentUserId = f.Absent.UserId })));
        });
    }

    [Theory]
    [InlineData("hints")]
    [InlineData("requirements")]
    [InlineData("origins")]
    [InlineData("reason")]
    [InlineData("enum")]
    public async Task Evidence_Independent_Qualification_Should_Require_Every_Explicit_Check_And_Reason(string missing)
    {
        var f = await GradingFixtureAsync(false); var gradeId = await EvidenceGradeAsync(f); using var teacher = User(f.Source.School.Reader);
        var input = IndependentEvidence();
        if (missing == "hints") input.NoHintsOrAnswersConfirmed = false;
        if (missing == "requirements") input.NewRequirementsConfirmed = false;
        if (missing == "origins") input.OriginalRelationshipsChecked = false;
        if (missing == "reason") input.Reason = " ";
        if (missing == "enum") input.Use = (EducationLearningEvidenceUse)999;
        var error = await Should.ThrowAsync<Exception>(() => Evidence.SaveEvidenceQualificationAsync(gradeId, input));
        (error is BusinessException or Volo.Abp.Validation.AbpValidationException or ArgumentException).ShouldBeTrue();
        (await Evidence.GetEvidenceAsync(gradeId)).Current.ShouldBeNull();
        await WithUnitOfWorkAsync(async () => (await Repo<EducationEvidenceQualification>().GetCountAsync()).ShouldBe(0));
    }

    [Theory]
    [InlineData(EducationLearningEvidenceUse.Unverified)]
    [InlineData(EducationLearningEvidenceUse.Practice)]
    [InlineData(EducationLearningEvidenceUse.Invalid)]
    public async Task Evidence_Qualification_Should_Keep_Non_Independent_Decisions_Without_Fabricating_Independent_Checks(EducationLearningEvidenceUse use)
    {
        var f = await GradingFixtureAsync(false); var gradeId = await EvidenceGradeAsync(f); using var teacher = User(f.Source.School.Reader);
        var saved = await Evidence.SaveEvidenceQualificationAsync(gradeId, new() { OperationId = Guid.NewGuid(), Use = use, Reason = "按实际核验保留分类" });
        saved.Use.ShouldBe(use); saved.NoHintsOrAnswersConfirmed.ShouldBeFalse(); saved.NewRequirementsConfirmed.ShouldBeFalse(); saved.OriginalRelationshipsChecked.ShouldBeFalse();
        (await Grades.GetSubmissionForGradingAsync(f.SubmissionId)).Questions.Single().Grade.Confirmed!.ObtainedScore.ShouldBe(10);
    }

    [Fact]
    public async Task Evidence_Original_Relation_Should_Be_Canonical_Recoverable_And_Reversible_Without_Deleting_History()
    {
        var first = await GradingFixtureAsync(false); var second = await GradingFixtureAsync(false, sourceOverride: first.Source);
        var a = await EvidenceGradeAsync(first); var b = await EvidenceGradeAsync(second); using var teacher = User(first.Source.School.Reader);
        var options = await Evidence.GetOriginCandidatesAsync(a, new() { MaxResultCount = 20 });
        options.Items.Select(x => x.QuestionGradeId).ShouldContain(b);
        var before = await Evidence.GetOriginRelationAsync(a, b);
        (await Evidence.GetOriginRelationAsync(b, a)).RelationId.ShouldBe(before.RelationId);
        var input = new SaveEducationOriginRelationDto { OperationId = Guid.NewGuid(), SecondQuestionGradeId = b, IsEnabled = true, Reason = "核对两份冻结题，属于同一原题的变式" };
        var enabled = await Evidence.SaveOriginRelationAsync(a, input);
        var current = await Evidence.GetOriginRelationAsync(b, a);
        var disabled = await Evidence.SaveOriginRelationAsync(b, new() { OperationId = Guid.NewGuid(), SecondQuestionGradeId = a, ExpectedRevisionId = enabled.Id,
            ConcurrencyStamp = current.ConcurrencyStamp, IsEnabled = false, Reason = "进一步核对后纠正错误关联" });
        (await Evidence.SaveOriginRelationAsync(a, input)).Id.ShouldBe(enabled.Id);
        (await Evidence.GetOriginOperationAsync(b, a, enabled.Id))!.IsEnabled.ShouldBeTrue();
        (await Evidence.GetOriginRelationAsync(a, b)).Current!.Id.ShouldBe(disabled.Id);
        await WithUnitOfWorkAsync(async () =>
        {
            (await Repo<EducationQuestionOriginRelation>().GetCountAsync()).ShouldBe(1);
            (await Repo<EducationQuestionOriginRelationRevision>().GetAsync(enabled.Id)).ToLearningRelation().ShouldNotBeNull();
            (await Repo<EducationQuestionOriginRelationRevision>().GetAsync(disabled.Id)).ToLearningRelation().ShouldBeNull();
        });
        await Should.ThrowAsync<BusinessException>(() => Evidence.GetOriginRelationAsync(a, a));
    }

    [Fact]
    public async Task Evidence_Origin_Receipt_Should_Not_Bypass_An_Unavailable_Recorded_Source_Through_Other_Eligible_Grades()
    {
        var first = await GradingFixtureAsync(false, secondStarted: true);
        var second = await GradingFixtureAsync(false, secondStarted: true, sourceOverride: first.Source);
        var a = await EvidenceGradeAsync(first); var b = await EvidenceGradeAsync(second); using var teacher = User(first.Source.School.Reader);
        async Task<Guid> OtherGradeAsync(GradingFixture fixture)
        {
            var closure = await Grades.CloseTaskAsync(fixture.TaskId, CloseInput(await Grades.PreviewFinalizationAsync(fixture.TaskId)));
            var submission = closure.Manifest.Entries.Single(x => x.StudentUserId == fixture.Absent.UserId).SubmissionId!.Value;
            return (await Grades.GetSubmissionForGradingAsync(submission)).Questions.Single().Grade.Id;
        }
        var otherA = await OtherGradeAsync(first); var otherB = await OtherGradeAsync(second);
        var saved = await Evidence.SaveOriginRelationAsync(a, new() { OperationId = Guid.NewGuid(), SecondQuestionGradeId = b, IsEnabled = true, Reason = "只允许在有权核对原始来源时读取这一依据" });
        (await Evidence.GetOriginRelationAsync(otherA, otherB)).Current!.Id.ShouldBe(saved.Id);
        // The alternative grades still exist, but cannot grant access to a recorded source removed from current queries.
        await WithUnitOfWorkAsync(() => Repo<EducationAssessmentQuestionGrade>().DeleteAsync(a, autoSave: true));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Evidence.GetOriginRelationAsync(otherA, otherB));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Evidence.GetOriginOperationAsync(otherA, otherB, saved.Id));
    }

    [Fact]
    public async Task Evidence_Hint_Review_Should_Bind_The_Delivered_Feedback_Version_And_Keep_Full_Review_Exposure_Confirmed()
    {
        var f = await GradingFixtureAsync(false); await ExposureReleaseAsync(f, KnowPulse.Exam.ExamResultVisibility.FullReview);
        var gradeId = await EvidenceGradeAsync(f); var feedbackId = Guid.NewGuid(); Guid published;
        using (User(f.Source.School.Reader))
        {
            var input = FeedbackInput(f, "已提供的旧版上下文-SENTINEL"); input.Content.AssessmentTaskIds = [f.TaskId];
            await Feedback.SaveDraftAsync(feedbackId, input); published = (await PublishFeedbackAsync(feedbackId)).Id;
        }
        using (User(f.Source.Student.UserId)) await Feedback.OpenMyAsync(feedbackId, published);
        var exposure = await WithUnitOfWorkAsync(async () => (await Repo<EducationQuestionExposure>().GetListAsync()).Single());
        using (User(f.Source.School.Reader))
        {
            var context = await Evidence.GetHintContextAsync(gradeId, exposure.Id); context.Text.ShouldBe("已提供的旧版上下文-SENTINEL");
            var change = FeedbackInput(f, "后来更正的上下文-SENTINEL"); change.Content.AssessmentTaskIds = [f.TaskId];
            var current = await Feedback.GetAsync(feedbackId); change.ConcurrencyStamp = current.ConcurrencyStamp; change.ExpectedRevisionId = current.CurrentRevision.Id;
            await Feedback.SaveDraftAsync(feedbackId, change); await PublishFeedbackAsync(feedbackId, "更正文字");
            (await Evidence.GetHintContextAsync(gradeId, exposure.Id)).FeedbackRevisionId.ShouldBe(published);
            (await Evidence.GetHintContextAsync(gradeId, exposure.Id)).Text.ShouldBe("已提供的旧版上下文-SENTINEL");
            var input = new SaveEducationHintReviewDto { OperationId = Guid.NewGuid(), Use = EducationLearningExposureUse.ConfirmedNoHints, ConfirmedExactContext = true, Reason = "逐题核对当时提供的这一版评语，仅含一般学习建议" };
            input.ConfirmedExactContext = false;
            await Should.ThrowAsync<BusinessException>(() => Evidence.SaveHintReviewAsync(gradeId, exposure.Id, input));
            input.ConfirmedExactContext = true;
            var reviewed = await Evidence.SaveHintReviewAsync(gradeId, exposure.Id, input);
            (await Evidence.SaveHintReviewAsync(gradeId, exposure.Id, input)).Id.ShouldBe(reviewed.Id);
            var pending = await Evidence.GetHintContextAsync(gradeId, exposure.Id);
            await Evidence.SaveHintReviewAsync(gradeId, exposure.Id, new() { OperationId = Guid.NewGuid(), ExpectedRevisionId = reviewed.Id, ConcurrencyStamp = pending.ConcurrencyStamp,
                Use = EducationLearningExposureUse.Unverified, ConfirmedExactContext = true, Reason = "进一步核验前恢复未知" });
            await WithUnitOfWorkAsync(async () => exposure.ToLearningEvidence(await Repo<EducationExposureHintReviewRevision>().GetAsync(reviewed.Id)).HintReviewRevisionId.ShouldBe(reviewed.Id));
        }
        using (User(f.Source.Student.UserId)) await Grades.GetMyResultAsync(f.RecipientId);
        var actual = await WithUnitOfWorkAsync(async () => (await Repo<EducationQuestionExposure>().GetListAsync(x => x.Source == EducationQuestionExposureSource.AssessmentReview)).Single());
        using (User(f.Source.School.Reader))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Evidence.SaveHintReviewAsync(gradeId, actual.Id,
                new() { OperationId = Guid.NewGuid(), Use = EducationLearningExposureUse.ConfirmedNoHints, ConfirmedExactContext = true, Reason = "不能撤销实际讲评" }));
            actual.ToLearningEvidence().Use.ShouldBe(EducationLearningExposureUse.Confirmed);
            var list = await Evidence.GetExposureContextsAsync(gradeId, new()); list.TotalCount.ShouldBe(2);
            list.Items.Single(x => x.Id == actual.Id).CanReview.ShouldBeFalse();
            list.Items.Single(x => x.Id == exposure.Id).Use.ShouldBe(EducationLearningExposureUse.Unverified);
            await WithUnitOfWorkAsync(async () => { var account = await Repo<IdentityUser>().GetAsync(f.Source.Student.UserId); account.SetIsActive(false); await Repo<IdentityUser>().UpdateManyAsync([account], autoSave: true); });
            await Should.ThrowAsync<AbpAuthorizationException>(() => Evidence.GetHintContextAsync(gradeId, exposure.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Evidence.SaveHintReviewAsync(gradeId, exposure.Id,
                new() { OperationId = Guid.NewGuid(), Use = EducationLearningExposureUse.ConfirmedNoHints, ConfirmedExactContext = true, Reason = "学生停用后不能新增上下文判断" }));
            await WithUnitOfWorkAsync(async () => (await Repo<EducationExposureHintReviewRevision>().GetCountAsync()).ShouldBe(2));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Evidence_Endpoints_Should_Deny_Other_Actors_And_Recheck_A_Deactivated_Owner(bool otherTeacher)
    {
        var f = await GradingFixtureAsync(false); var gradeId = await EvidenceGradeAsync(f); Guid operation;
        using (User(f.Source.School.Reader)) operation = (await Evidence.SaveEvidenceQualificationAsync(gradeId, IndependentEvidence())).Id;
        using (User(otherTeacher ? f.Source.School.Owner : f.Source.Student.UserId))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Evidence.GetEvidenceAsync(gradeId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Evidence.GetQualificationOperationAsync(gradeId, operation));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Evidence.SaveEvidenceQualificationAsync(gradeId, IndependentEvidence()));
        }
        await WithUnitOfWorkAsync(async () => { var account = await Repo<IdentityUser>().GetAsync(f.Source.School.Reader); account.SetIsActive(false); await Repo<IdentityUser>().UpdateManyAsync([account], autoSave: true); });
        using (User(f.Source.School.Reader)) await Should.ThrowAsync<AbpAuthorizationException>(() => Evidence.GetQualificationOperationAsync(gradeId, operation));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Evidence_Endpoints_Should_Enforce_Functional_Permissions_Before_Returning_Evidence_Or_Feedback(bool denyAssessment)
    {
        var f = await GradingFixtureAsync(false); var gradeId = await EvidenceGradeAsync(f); using var teacher = User(f.Source.School.Reader);
        var checker = Substitute.For<IPermissionChecker>();
        checker.IsGrantedAsync(EducationPermissions.AssessmentManage).Returns(!denyAssessment);
        checker.IsGrantedAsync(EducationPermissions.FeedbackManage).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>();
        lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>());
        lazy.LazyGetRequiredService<EducationAssessmentAccessGuard>().Returns(GetRequiredService<EducationAssessmentAccessGuard>());
        var denied = new EducationAssessmentAppService { LazyServiceProvider = lazy };
        var id = Guid.NewGuid();
        Func<Task>[] calls = denyAssessment ?
        [ () => denied.GetEvidenceAsync(gradeId), () => denied.SaveEvidenceQualificationAsync(gradeId, IndependentEvidence()),
          () => denied.GetQualificationOperationAsync(gradeId, id), () => denied.GetOriginCandidatesAsync(gradeId, new()),
          () => denied.GetOriginRelationAsync(gradeId, id), () => denied.SaveOriginRelationAsync(gradeId, new()),
          () => denied.GetOriginOperationAsync(gradeId, id, id), () => denied.GetExposureContextsAsync(gradeId, new()),
          () => denied.GetHintContextAsync(gradeId, id), () => denied.SaveHintReviewAsync(gradeId, id, new()), () => denied.GetHintReviewOperationAsync(gradeId, id, id) ] :
        [ () => denied.GetHintContextAsync(gradeId, id), () => denied.SaveHintReviewAsync(gradeId, id, new()), () => denied.GetHintReviewOperationAsync(gradeId, id, id) ];
        await WithUnitOfWorkAsync(async () => { foreach (var call in calls) await Should.ThrowAsync<AbpAuthorizationException>(call); });
        await checker.Received(calls.Length).IsGrantedAsync(denyAssessment ? EducationPermissions.AssessmentManage : EducationPermissions.FeedbackManage);
    }
}
