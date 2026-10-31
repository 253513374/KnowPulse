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
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationQuestionCollectionAppService Collections => GetRequiredService<IEducationQuestionCollectionAppService>();
    private static SetEducationQuestionCollectionDto CollectInput(GradingFixture f, Guid release, Guid? previous = null, bool collect = true) => new()
    { SchoolId = f.Source.Revision.SchoolId, OperationId = Guid.NewGuid(), PaperQuestionId = f.Source.QuestionId, ExpectedCurrentOperationId = previous, ExpectedResultReleaseId = collect ? release : null, IsCollected = collect };

    [Theory]
    [InlineData(ExamResultVisibility.Hidden, true)]
    [InlineData(ExamResultVisibility.ScoreOnly, true)]
    [InlineData(ExamResultVisibility.FullReview, false)]
    [InlineData(ExamResultVisibility.FullReview, true)]
    public async Task Question_Collection_Should_Require_Current_Full_Review_And_Separate_Authorization_Without_Delivering_Source_Bodies(ExamResultVisibility visibility, bool allow)
    {
        var f = await GradingFixtureAsync(false, allowCollection: allow); var release = await ExposureReleaseAsync(f, visibility);
        using var actor = NotebookActor(f.Source.Student.UserId); var input = CollectInput(f, release.Id);
        var state = await Collections.GetMyCollectionAsync(input.SchoolId, f.RecipientId, input.PaperQuestionId);
        state.Current.ShouldBeNull(); state.CanCollect.ShouldBe(allow && visibility == ExamResultVisibility.FullReview);
        if (!state.CanCollect) (await Should.ThrowAsync<BusinessException>(() => Collections.SetMyCollectionAsync(f.RecipientId, input))).Code.ShouldBe(EducationQuestionCollectionErrors.SourceUnavailable);
        else {
            (await Should.ThrowAsync<BusinessException>(() => Collections.SetMyCollectionAsync(f.RecipientId, CollectInput(f, release.Id, null, false)))).Code.ShouldBe(EducationQuestionCollectionErrors.Invalid);
            var saved = await Collections.SetMyCollectionAsync(f.RecipientId, input); saved.Id.ShouldBe(input.OperationId); saved.IsCollected.ShouldBeTrue();
            (await Should.ThrowAsync<BusinessException>(() => Collections.SetMyCollectionAsync(f.RecipientId, CollectInput(f, release.Id, saved.Id)))).Code.ShouldBe(EducationQuestionCollectionErrors.Invalid);
            (await Collections.GetMyCollectionsAsync(new() { SchoolId = input.SchoolId })).TotalCount.ShouldBe(1);
        }
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(0);
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionPractice>().GetCountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Collection_Receipts_Should_Not_Roll_Back_Later_Toggles_And_Cancellation_Should_Remain_Possible_When_Review_Closes()
    {
        var f = await GradingFixtureAsync(false, allowCollection: true); var release = await ExposureReleaseAsync(f, ExamResultVisibility.FullReview);
        var add = CollectInput(f, release.Id); EducationQuestionCollectionDto first, second, third;
        using (NotebookActor(f.Source.Student.UserId)) {
            first = await Collections.SetMyCollectionAsync(f.RecipientId, add);
            second = await Collections.SetMyCollectionAsync(f.RecipientId, CollectInput(f, release.Id, first.Id, false));
            (await Collections.SetMyCollectionAsync(f.RecipientId, add)).Id.ShouldBe(first.Id);
            (await Collections.GetMyCollectionAsync(add.SchoolId, f.RecipientId, add.PaperQuestionId)).Current!.Id.ShouldBe(second.Id);
            (await Collections.GetMyCollectionsAsync(new() { SchoolId = add.SchoolId })).TotalCount.ShouldBe(0);
            third = await Collections.SetMyCollectionAsync(f.RecipientId, CollectInput(f, release.Id, second.Id));
            await Should.ThrowAsync<AbpDbConcurrencyException>(() => Collections.SetMyCollectionAsync(f.RecipientId, CollectInput(f, release.Id, first.Id, false)));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Grades.GetMyResultForSchoolAsync(f.RecipientId, Guid.NewGuid()));
            (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(0);
            (await Grades.GetMyResultForSchoolAsync(f.RecipientId, add.SchoolId)).AllowQuestionCollection.ShouldBeTrue();
        }
        using (User(f.Source.School.Reader)) {
            var overview = await Grades.GetGradingOverviewAsync(f.TaskId);
            var change = ReleaseInput(overview, overview.ClosureId!.Value, ExamResultVisibility.ScoreOnly); change.Reason = "确认关闭讲评，核对收藏仅保留引用";
            await Grades.PublishResultsAsync(f.TaskId, change);
        }
        using (NotebookActor(f.Source.Student.UserId)) {
            var page = await Collections.GetMyCollectionsAsync(new() { SchoolId = add.SchoolId }); page.TotalCount.ShouldBe(1); page.Items.Single().SourceAvailable.ShouldBeFalse();
            var state = await Collections.GetMyCollectionAsync(add.SchoolId, f.RecipientId, add.PaperQuestionId); state.CanCollect.ShouldBeFalse(); state.ResultReleaseId.ShouldBeNull();
            var removed = await Collections.SetMyCollectionAsync(f.RecipientId, CollectInput(f, release.Id, third.Id, false)); removed.RevisionNumber.ShouldBe(4);
            (await Collections.GetMyCollectionOperationAsync(add.SchoolId, f.RecipientId, first.Id))!.IsCollected.ShouldBeTrue();
            (await Collections.GetMyCollectionsAsync(new() { SchoolId = add.SchoolId })).TotalCount.ShouldBe(0);
            (await Grades.GetMyResultForSchoolAsync(f.RecipientId, add.SchoolId)).AllowQuestionCollection.ShouldBeFalse();
        }
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionCollection>().GetCountAsync())).ShouldBe(4);
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(1);
        (await WithUnitOfWorkAsync(() => Repo<EducationAssessmentSubmission>().GetCountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Collection_Should_Deny_Wrong_School_Peer_Teacher_Tenant_And_Explicit_Functional_Permission_Refusal()
    {
        var f = await GradingFixtureAsync(false, allowCollection: true); var release = await ExposureReleaseAsync(f, ExamResultVisibility.FullReview); var input = CollectInput(f, release.Id);
        using (NotebookActor(f.Source.Student.UserId)) {
            await Collections.SetMyCollectionAsync(f.RecipientId, input);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Collections.GetMyCollectionAsync(Guid.NewGuid(), f.RecipientId, input.PaperQuestionId));
            var changed = CollectInput(f, release.Id); changed.OperationId = input.OperationId; changed.IsCollected = false;
            (await Should.ThrowAsync<BusinessException>(() => Collections.SetMyCollectionAsync(f.RecipientId, changed))).Code.ShouldBe(EducationQuestionCollectionErrors.Conflict);
        }
        foreach (var account in new[] { f.Absent.UserId, f.Source.School.Reader }) using (NotebookActor(account))
            await Should.ThrowAsync<AbpAuthorizationException>(() => Collections.GetMyCollectionOperationAsync(input.SchoolId, f.RecipientId, input.OperationId));
        using (NotebookActor(f.Source.Student.UserId)) using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
            await Should.ThrowAsync<AbpAuthorizationException>(() => Collections.GetMyCollectionAsync(input.SchoolId, f.RecipientId, input.PaperQuestionId));
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(Arg.Any<string>()).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>()); lazy.LazyGetRequiredService<IUnitOfWorkManager>().Returns(GetRequiredService<IUnitOfWorkManager>());
        var denied = new EducationAssessmentAppService { LazyServiceProvider = lazy };
        Func<Task>[] calls = [() => denied.SetMyCollectionAsync(f.RecipientId, input), () => denied.GetMyCollectionAsync(input.SchoolId, f.RecipientId, input.PaperQuestionId),
            () => denied.GetMyCollectionOperationAsync(input.SchoolId, f.RecipientId, input.OperationId), () => denied.GetMyCollectionsAsync(new() { SchoolId = input.SchoolId })];
        foreach (var call in calls) await Should.ThrowAsync<AbpAuthorizationException>(call);
        await checker.Received(4).IsGrantedAsync(EducationPermissions.AssessmentParticipate);
    }
}
