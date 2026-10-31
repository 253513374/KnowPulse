using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private async Task<(GradingFixture F, Guid PlanId, EducationTeachingPlanRevisionDto Published, Guid ExposureId)> DeliveredPlanAsync()
    {
        var (f, job) = await PlanSourceAsync(); var plan = Guid.NewGuid(); EducationTeachingPlanRevisionDto published;
        using (User(f.Source.School.Reader)) { await Plans.SaveDraftAsync(plan, await PlanDraftAsync(f, job)); published = await ConfirmAndPublishPlanAsync(plan); }
        using (User(f.Source.Student.UserId)) await Plans.OpenMyAsync(plan, published.Id);
        var exposure = await WithUnitOfWorkAsync(async () => (await Repo<EducationTeachingPlanQuestionExposure>().GetListAsync()).Single());
        return (f, plan, published, exposure.Id);
    }
    private static SaveEducationHintReviewDto PlanHint(EducationTeachingPlanHintContextDto context, EducationLearningExposureUse use = EducationLearningExposureUse.ConfirmedNoHints) =>
        new() { OperationId = Guid.NewGuid(), ExpectedRevisionId = context.Current?.Id, ConcurrencyStamp = context.ConcurrencyStamp, Use = use,
            ConfirmedExactContext = true, Reason = "核对学生当时收到的该版方案与冻结题，不含此题答案或提示" };

    [Fact]
    public async Task Plan_Hint_Review_Should_List_Actual_Context_And_Recover_Original_Operation_Without_Overwriting_A_Later_Decision()
    {
        var (f, plan, published, exposure) = await DeliveredPlanAsync(); var grade = await EvidenceGradeAsync(f);
        using var user = User(f.Source.School.Reader);
        var contexts = await Evidence.GetExposureContextsAsync(grade, new());
        var card = contexts.Items.Single(x => x.Id == exposure); card.Source.ShouldBe(EducationQuestionExposureSource.TeachingPlan);
        card.CanReview.ShouldBeTrue(); card.Use.ShouldBe(EducationLearningExposureUse.Unverified);
        var context = await Plans.GetHintContextAsync(exposure); context.PlanRevisionId.ShouldBe(published.Id);
        context.Content.Title.ShouldBe(published.Content!.Title); JsonSerializer.Serialize(context).ShouldNotContain("PRIVATE-PLAN-NOTES");
        context.Current.ShouldBeNull(); var input = PlanHint(context); input.ConfirmedExactContext = false;
        await Should.ThrowAsync<BusinessException>(() => Plans.SaveHintReviewAsync(exposure, input));
        await WithUnitOfWorkAsync(async () => (await Repo<EducationTeachingPlanHintReview>().GetCountAsync()).ShouldBe(0));
        input.ConfirmedExactContext = true; var first = await Plans.SaveHintReviewAsync(exposure, input);
        (await Plans.SaveHintReviewAsync(exposure, input)).Id.ShouldBe(first.Id);
        (await Plans.GetHintReviewOperationAsync(exposure, first.Id))!.Use.ShouldBe(EducationLearningExposureUse.ConfirmedNoHints);
        var secondInput = PlanHint(await Plans.GetHintContextAsync(exposure), EducationLearningExposureUse.Confirmed);
        secondInput.Reason = "更正：此版方案实际包含这道题的关键提示";
        var second = await Plans.SaveHintReviewAsync(exposure, secondInput);
        (await Plans.SaveHintReviewAsync(exposure, input)).Id.ShouldBe(first.Id);
        (await Plans.GetHintContextAsync(exposure)).Current!.Id.ShouldBe(second.Id);
        input.OperationId = Guid.NewGuid(); await Should.ThrowAsync<AbpDbConcurrencyException>(() => Plans.SaveHintReviewAsync(exposure, input));
        secondInput.Reason = "同一编号不允许另一份请求";
        (await Should.ThrowAsync<BusinessException>(() => Plans.SaveHintReviewAsync(exposure, secondInput))).Code.ShouldBe(EducationTeachingPlanErrors.OperationConflict);
        await WithUnitOfWorkAsync(async () => {
            (await Repo<EducationTeachingPlanHintReviewRevision>().GetCountAsync()).ShouldBe(2);
            (await Repo<EducationTeachingPlanHintReview>().GetAsync(exposure)).CurrentRevisionId.ShouldBe(second.Id);
        });
        var freshCard = (await Evidence.GetExposureContextsAsync(grade, new())).Items.Single(x => x.Id == exposure);
        freshCard.Use.ShouldBe(EducationLearningExposureUse.Confirmed);
        (await Plans.GetAsync(plan)).Current.Id.ShouldBe(published.Id);
    }

    [Fact]
    public async Task Plan_Hint_Review_Should_Keep_The_Exact_Delivered_Text_After_New_Draft_And_Withdrawal()
    {
        var (f, plan, published, exposure) = await DeliveredPlanAsync();
        using var user = User(f.Source.School.Reader);
        var report = await Reports.RequestAsync(ReportInput(f)); await ReportProcessor.ProcessAsync(report.Id);
        var draft = await PlanDraftAsync(f, report); var current = await Plans.GetAsync(plan);
        draft.ExpectedRevisionId = current.Current.Id; draft.ConcurrencyStamp = current.ConcurrencyStamp;
        draft.Content = draft.Content with { Title = "PRIVATE-NEW-PLAN-DRAFT", StudentGoal = "另一份尚未提供的目标" };
        await Plans.SaveDraftAsync(plan, draft);
        await Plans.WithdrawAsync(plan, PlanTransition(await Plans.GetAsync(plan), "撤回后仍须核对原来提供的上下文"));
        var exact = await Plans.GetHintContextAsync(exposure); exact.PlanRevisionId.ShouldBe(published.Id);
        exact.Content.Title.ShouldBe(published.Content!.Title); JsonSerializer.Serialize(exact).ShouldNotContain("PRIVATE-NEW-PLAN-DRAFT");
        var saved = await Plans.SaveHintReviewAsync(exposure, PlanHint(exact));
        (await Plans.GetHintContextAsync(exposure)).Current!.Id.ShouldBe(saved.Id);
        using (User(f.Source.Student.UserId)) (await Plans.GetMyListAsync(new())).Items.Single().Content.ShouldBeNull();
    }

    [Theory]
    [InlineData("student")]
    [InlineData("other-teacher")]
    [InlineData("other-tenant")]
    [InlineData("ended-enrollment")]
    public async Task Plan_Hint_Review_Should_Reauthorize_The_Author_And_Student_Even_For_Existing_Receipts(string denied)
    {
        var (f, _, _, exposure) = await DeliveredPlanAsync(); EducationTeachingPlanHintReviewRevisionDto saved;
        using (User(f.Source.School.Reader)) saved = await Plans.SaveHintReviewAsync(exposure, PlanHint(await Plans.GetHintContextAsync(exposure)));
        if (denied == "ended-enrollment") await WithUnitOfWorkAsync(async () => {
            var rows = await Repo<StudentEnrollment>().GetListAsync(x => x.StudentUserId == f.Source.Student.UserId);
            foreach (var row in rows) { row.EndAt(DateTime.UtcNow.AddSeconds(-1)); await Repo<StudentEnrollment>().UpdateAsync(row, autoSave: true); }
        });
        using var actor = User(denied == "student" ? f.Source.Student.UserId : denied == "other-teacher" ? f.Source.School.Owner : f.Source.School.Reader);
        using var tenant = GetRequiredService<Volo.Abp.MultiTenancy.ICurrentTenant>().Change(denied == "other-tenant" ? Guid.NewGuid() : null);
        await Should.ThrowAsync<AbpAuthorizationException>(() => Plans.GetHintContextAsync(exposure));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Plans.SaveHintReviewAsync(exposure, new() { OperationId = Guid.NewGuid(), Reason = "有效输入仍须重新授权", ConfirmedExactContext = true }));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Plans.GetHintReviewOperationAsync(exposure, saved.Id));
    }

    [Fact]
    public async Task Plan_Hint_Review_Should_Version_Report_Source_Without_Removing_Real_Full_Review_Exposure()
    {
        var (f, _, published, exposure) = await DeliveredPlanAsync(); var grade = await EvidenceGradeAsync(f);
        using (User(f.Source.Student.UserId)) await Grades.GetMyResultAsync(f.RecipientId);
        using var user = User(f.Source.School.Reader);
        await Evidence.SaveEvidenceQualificationAsync(grade, IndependentEvidence());
        var window = GrowthWindow(); Guid contextId; EducationStudentGrowthValue studentBefore;
        using (User(f.Source.Student.UserId)) {
            contextId = (await Growth.GetMyContextsAsync()).Items.Single().Id;
            studentBefore = await Growth.GetMyGrowthAsync(contextId, window);
        }
        var earlier = await Reports.RequestAsync(ReportInput(f)); await ReportProcessor.ProcessAsync(earlier.Id);
        var review = await Plans.SaveHintReviewAsync(exposure, PlanHint(await Plans.GetHintContextAsync(exposure)));
        var next = await Reports.RequestAsync(ReportInput(f)); await ReportProcessor.ProcessAsync(next.Id);
        next.Id.ShouldNotBe(earlier.Id);
        await WithUnitOfWorkAsync(async () => {
            var source = (await Repo<EducationLearningReportJob>().GetAsync(next.Id)).Source();
            var values = source.Students.Single(x => x.Student.StudentUserId == f.Source.Student.UserId).Exposures;
            values.Single(x => x.Id == exposure).Use.ShouldBe(EducationLearningExposureUse.ConfirmedNoHints);
            values.Single(x => x.Id == exposure).HintReviewRevisionId.ShouldBe(review.Id);
            values.Where(x => x.Id != exposure).ShouldNotBeEmpty();
            values.Where(x => x.Id != exposure).All(x => x.Use == EducationLearningExposureUse.Confirmed).ShouldBeTrue();
        });
        var cards = (await Evidence.GetExposureContextsAsync(grade, new())).Items;
        cards.Single(x => x.Id == exposure).Use.ShouldBe(EducationLearningExposureUse.ConfirmedNoHints);
        cards.Where(x => x.Source == EducationQuestionExposureSource.AssessmentReview).All(x => !x.CanReview && x.Use == EducationLearningExposureUse.Confirmed).ShouldBeTrue();
        var firstPage = await Evidence.GetExposureContextsAsync(grade, new() { MaxResultCount = 1 });
        var secondPage = await Evidence.GetExposureContextsAsync(grade, new() { SkipCount = 1, MaxResultCount = 1 });
        firstPage.TotalCount.ShouldBe(2); secondPage.TotalCount.ShouldBe(2);
        firstPage.Items.Single().Id.ShouldNotBe(secondPage.Items.Single().Id);
        (await Plans.GetHintContextAsync(exposure)).PlanRevisionId.ShouldBe(published.Id);
        using (User(f.Source.Student.UserId)) {
            var after = await Growth.GetMyGrowthAsync(contextId, window);
            after.SnapshotId.ShouldBe(studentBefore.SnapshotId);
            after.CalculatedAtUtc.ShouldBe(studentBefore.CalculatedAtUtc);
        }
    }

    [Fact]
    public async Task Plan_Hint_Review_Should_Require_Actual_Body_Delivery_Rather_Than_A_Publication_Id()
    {
        var (f, job) = await PlanSourceAsync(); var plan = Guid.NewGuid();
        using var teacher = User(f.Source.School.Reader);
        await Plans.SaveDraftAsync(plan, await PlanDraftAsync(f, job)); var published = await ConfirmAndPublishPlanAsync(plan);
        foreach (var fake in new[] { plan, published.Id, Guid.NewGuid() }) {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Plans.GetHintContextAsync(fake));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Plans.SaveHintReviewAsync(fake,
                new() { OperationId = Guid.NewGuid(), ConfirmedExactContext = true, Reason = "发布不代表已实际提供正文" }));
        }
        await WithUnitOfWorkAsync(async () => {
            (await Repo<EducationTeachingPlanQuestionExposure>().GetCountAsync()).ShouldBe(0);
            (await Repo<EducationTeachingPlanHintReview>().GetCountAsync()).ShouldBe(0);
        });
    }
}
