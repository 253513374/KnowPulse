using System;
using System.Threading.Tasks;
using KnowPulse.Education;
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
    [Fact]
    public async Task Native_Observations_Should_Validate_School_And_Exact_Target_Before_Returning_Bodies_Or_Receipts()
    {
        var f = await GradingFixtureAsync(false); using var actor = User(f.Source.School.Reader);
        var target = FeedbackInput(f); var school = f.Source.Revision.SchoolId; var operation = Guid.NewGuid();
        var input = new CreateEducationTeacherObservationDto { OperationId = operation, TeachingAssignmentId = target.TeachingAssignmentId,
            StudentEnrollmentId = target.StudentEnrollmentId, SchoolMembershipId = target.SchoolMembershipId, Text = "私有观察，保留原文\n 第二行 ", ObservedAtUtc = DateTime.UtcNow };
        (await Feedback.GetObservationOperationAsync(school, operation, target)).ShouldBeNull();
        await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.CreateObservationForSchoolAsync(Guid.NewGuid(), input));
        (await Feedback.GetObservationOperationAsync(school, operation, target)).ShouldBeNull();
        var row = await Feedback.CreateObservationForSchoolAsync(school, input);
        row.SchoolId.ShouldBe(school); row.StudentEnrollmentId.ShouldBe(target.StudentEnrollmentId); row.Text.ShouldBe(input.Text);
        (await Feedback.CreateObservationAsync(input)).Id.ShouldBe(row.Id);
        (await Feedback.GetObservationsForSchoolAsync(school, target, new())).TotalCount.ShouldBe(1);
        (await Feedback.GetObservationOperationAsync(school, operation, target))!.Text.ShouldBe(input.Text);
        await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.GetObservationsForSchoolAsync(Guid.NewGuid(), target, new()));
        var other = FeedbackInput(f); other.StudentEnrollmentId = f.Absent.EnrollmentId; other.SchoolMembershipId = f.Absent.MembershipId;
        await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.GetObservationOperationAsync(school, operation, other));
        input.Text = "同号不同观察";
        (await Should.ThrowAsync<BusinessException>(() => Feedback.CreateObservationForSchoolAsync(school, input))).Code.ShouldBe(EducationFeedbackErrorCodes.OperationConflict);
        (await Feedback.GetMyListAsync(new())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Personal_Phrases_Should_Keep_Exact_Receipts_Reject_Stale_Edits_And_Archive_Without_Changing_Copied_Feedback()
    {
        var f = await GradingFixtureAsync(false); using var actor = User(f.Source.School.Reader);
        var scope = new EducationFeedbackPhraseScopeDto { SchoolId = f.Source.Revision.SchoolId, TeachingAssignmentId = FeedbackInput(f).TeachingAssignmentId };
        var root = Guid.NewGuid(); var input = new SaveEducationFeedbackPhraseDto { SchoolId = scope.SchoolId, TeachingAssignmentId = scope.TeachingAssignmentId,
            OperationId = Guid.NewGuid(), Text = " 请补充推理步骤\n再核对单位。 " };
        (await Feedback.GetPhraseAsync(root, scope)).ShouldBeNull();
        (await Feedback.GetPhraseOperationAsync(root, input.OperationId, scope)).ShouldBeNull();
        var first = await Feedback.SavePhraseAsync(root, input);
        first.Text.ShouldBe(input.Text); first.RevisionNumber.ShouldBe(1);
        (await Feedback.SavePhraseAsync(root, input)).Id.ShouldBe(first.Id);
        var copied = Guid.NewGuid(); await Feedback.SaveDraftAsync(copied, FeedbackInput(f, first.Text));
        var original = (await Feedback.GetPhraseAsync(root, scope))!;
        SaveEducationFeedbackPhraseDto Change(string text, bool archived = false) => new() { SchoolId = scope.SchoolId, TeachingAssignmentId = scope.TeachingAssignmentId,
            OperationId = Guid.NewGuid(), Text = text, IsArchived = archived, ConcurrencyStamp = original.ConcurrencyStamp, ExpectedRevisionId = original.CurrentRevision.Id };
        var next = Change("PRIVATE-PHRASE-NEW"); var second = await Feedback.SavePhraseAsync(root, next);
        second.RevisionNumber.ShouldBe(2);
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Feedback.SavePhraseAsync(root, Change("旧页面不可覆盖")));
        (await Feedback.GetPhraseOperationAsync(root, first.Id, scope))!.Text.ShouldBe(first.Text);
        (await Feedback.SavePhraseAsync(root, input)).Id.ShouldBe(first.Id);
        original = (await Feedback.GetPhraseAsync(root, scope))!;
        await Feedback.SavePhraseAsync(root, Change(original.CurrentRevision.Text, true));
        (await Feedback.GetPhrasesAsync(scope, new())).TotalCount.ShouldBe(0);
        (await Feedback.GetPhrasesAsync(scope, new(), true)).TotalCount.ShouldBe(1);
        (await Feedback.GetAsync(copied)).Draft!.Content!.Text.ShouldBe(first.Text);
        original = (await Feedback.GetPhraseAsync(root, scope))!;
        var restored = await Feedback.SavePhraseAsync(root, Change("恢复后本人可选用")); restored.RevisionNumber.ShouldBe(4);
        (await Feedback.GetPhrasesAsync(scope, new())).TotalCount.ShouldBe(1);
        next.Text = "同操作不同正文";
        (await Should.ThrowAsync<BusinessException>(() => Feedback.SavePhraseAsync(root, next))).Code.ShouldBe(EducationFeedbackErrorCodes.OperationConflict);
    }

    [Fact]
    public async Task Personal_Phrases_Should_Require_Current_Teaching_Ownership_Tenant_And_Functional_Permission()
    {
        var f = await GradingFixtureAsync(false); var root = Guid.NewGuid();
        var scope = new EducationFeedbackPhraseScopeDto { SchoolId = f.Source.Revision.SchoolId, TeachingAssignmentId = FeedbackInput(f).TeachingAssignmentId };
        var input = new SaveEducationFeedbackPhraseDto { SchoolId = scope.SchoolId, TeachingAssignmentId = scope.TeachingAssignmentId, OperationId = Guid.NewGuid(), Text = "仅作者本人常用语" };
        using (User(f.Source.School.Reader)) await Feedback.SavePhraseAsync(root, input);
        using (User(f.Source.Student.UserId)) await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.GetPhraseAsync(root, scope));
        using (User(f.Source.School.Owner)) await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.GetPhraseOperationAsync(root, input.OperationId, scope));
        var peerTeaching = await WithUnitOfWorkAsync(async () =>
        {
            var teaching = await Repo<TeachingAssignment>().GetAsync(scope.TeachingAssignmentId);
            var members = await Repo<SchoolMembership>().GetListAsync(x => x.SchoolId == teaching.SchoolId && x.UserId == f.Source.School.Owner && x.Role == SchoolMemberRole.Teacher);
            if (members.Count == 0) await Repo<SchoolMembership>().InsertAsync(new SchoolMembership(Guid.NewGuid(), null, teaching.SchoolId, f.Source.School.Owner, SchoolMemberRole.Teacher, DateTime.UtcNow.AddDays(-1)), autoSave: true);
            var peer = new TeachingAssignment(Guid.NewGuid(), null, teaching.SchoolId, teaching.AcademicTermId, teaching.SchoolClassId, teaching.EducationSubjectId, f.Source.School.Owner, DateTime.UtcNow.AddHours(-1));
            await Repo<TeachingAssignment>().InsertAsync(peer, autoSave: true); return peer.Id;
        });
        using (User(f.Source.School.Owner))
        {
            var peerScope = new EducationFeedbackPhraseScopeDto { SchoolId = scope.SchoolId, TeachingAssignmentId = peerTeaching };
            (await Feedback.GetPhrasesAsync(peerScope, new())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.GetPhraseAsync(root, peerScope));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.GetPhraseOperationAsync(root, input.OperationId, peerScope));
        }
        using (User(f.Source.School.Reader)) using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
            await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.GetPhraseAsync(root, scope));
        using (User(f.Source.School.Reader))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.GetPhrasesAsync(new() { SchoolId = Guid.NewGuid(), TeachingAssignmentId = scope.TeachingAssignmentId }, new()));
            await WithUnitOfWorkAsync(async () => { var teaching = await Repo<TeachingAssignment>().GetAsync(scope.TeachingAssignmentId); teaching.EndAt(DateTime.UtcNow); await Repo<TeachingAssignment>().UpdateManyAsync([teaching], autoSave: true); });
            await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.GetPhraseAsync(root, scope));
        }
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(Arg.Any<string>()).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>());
        lazy.LazyGetRequiredService<IUnitOfWorkManager>().Returns(GetRequiredService<IUnitOfWorkManager>());
        var denied = new EducationFeedbackAppService { LazyServiceProvider = lazy };
        Func<Task>[] calls = [() => denied.GetPhrasesAsync(scope, new()), () => denied.GetPhraseAsync(root, scope),
            () => denied.GetPhraseOperationAsync(root, input.OperationId, scope), () => denied.SavePhraseAsync(root, input),
            () => denied.CreateObservationForSchoolAsync(scope.SchoolId, new()), () => denied.GetObservationsForSchoolAsync(scope.SchoolId, new(), new()),
            () => denied.GetObservationOperationAsync(scope.SchoolId, input.OperationId, new())];
        foreach (var call in calls) await Should.ThrowAsync<AbpAuthorizationException>(call);
        await checker.Received(7).IsGrantedAsync(EducationPermissions.FeedbackManage);
    }
}
