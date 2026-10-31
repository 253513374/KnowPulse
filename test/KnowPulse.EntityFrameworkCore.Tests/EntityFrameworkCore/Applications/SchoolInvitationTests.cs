using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationSchoolAppServiceTests
{
    private static CreateSchoolInvitationDto InviteInput(Guid target, SchoolMemberRole role = SchoolMemberRole.Teacher) => new() {
        OperationId = Guid.NewGuid(), TargetUserId = target, Role = role, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
        EffectiveToUtc = DateTime.UtcNow.AddDays(10), ExpiresAtUtc = DateTime.UtcNow.AddDays(2), ConfirmVerifiedAccount = true };
    private static RespondSchoolInvitationDto AcceptInvitation(MySchoolInvitationDto row) => new() { OperationId = Guid.NewGuid(), ConcurrencyStamp = row.ConcurrencyStamp,
        Response = SchoolInvitationResponse.Accept, ConfirmIdentityAndSchool = true };

    [Fact]
    public async Task Invitations_Should_Require_Exact_Account_Confirmation_And_Replay_Without_Duplicating_Or_Restoring_Membership()
    {
        var f = await CreateFixtureAsync(); var target = await CreateUserAsync(); var other = await CreateUserAsync(); var input = InviteInput(target);
        SchoolInvitationDto invitation;
        using (ChangeUser(f.Manager)) {
            invitation = await _service.CreateInvitationAsync(f.School.Id, input); invitation.Id.ShouldBe(input.OperationId);
            (await _service.CreateInvitationAsync(f.School.Id, input)).Id.ShouldBe(invitation.Id);
            (await _service.GetInvitationsAsync(f.School.Id, new())).TotalCount.ShouldBe(1);
            (await Should.ThrowAsync<BusinessException>(() => _service.CreateInvitationAsync(f.School.Id, InviteInput(target)))).Code.ShouldBe(SchoolInvitationErrors.PendingExists);
            input.TargetUserId = other;
            (await Should.ThrowAsync<BusinessException>(() => _service.CreateInvitationAsync(f.School.Id, input))).Code.ShouldBe(SchoolInvitationErrors.OperationConflict);
        }
        using (ChangeUser(other)) { await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetMyInvitationAsync(invitation.Id)); await Should.ThrowAsync<AbpAuthorizationException>(() => _service.RespondToInvitationAsync(invitation.Id, AcceptInvitation(invitation))); }
        using (ChangeUser(f.Teacher)) { await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CreateInvitationAsync(f.School.Id, InviteInput(other))); }
        RespondSchoolInvitationDto response; Guid membership, libraryId; var confirmedAfter = DateTime.UtcNow;
        using (ChangeUser(target)) {
            var library = GetRequiredService<IKnowledgeLibraryAppService>(); libraryId = (await library.GetMyLibraryAsync()).Id;
            (await _service.GetMySchoolsAsync()).Items.ShouldBeEmpty(); var row = (await _service.GetMyInvitationsAsync(new())).Items.Single(); row.CanRespond.ShouldBeTrue(); row.SchoolCode.ShouldBe(f.School.Code);
            response = AcceptInvitation(row); response.ConfirmIdentityAndSchool = false;
            (await Should.ThrowAsync<BusinessException>(() => _service.RespondToInvitationAsync(row.Id, response))).Code.ShouldBe(SchoolInvitationErrors.Invalid);
            response.ConfirmIdentityAndSchool = true; var result = await _service.RespondToInvitationAsync(row.Id, response); membership = result.MembershipId!.Value;
            result.Status.ShouldBe(SchoolInvitationStatus.Accepted); result.ResponseOperationId.ShouldBe(response.OperationId);
            (await _service.RespondToInvitationAsync(row.Id, response)).MembershipId.ShouldBe(membership);
            (await _service.GetMyInvitationsAsync(new())).TotalCount.ShouldBe(0); (await _service.GetMyInvitationsAsync(new() { IncludeInactive = true })).TotalCount.ShouldBe(1);
            (await _service.GetMyWorkspaceAsync()).Workspaces.Single(x => x.SchoolId == f.School.Id && x.Role == SchoolMemberRole.Teacher).Classes.ShouldBeEmpty();
            (await library.GetMyLibraryAsync()).Id.ShouldBe(libraryId);
        }
        await WithUnitOfWorkAsync(async () => {
            var members = await _memberships.GetListAsync(x => x.SchoolId == f.School.Id && x.UserId == target); members.Count.ShouldBe(1);
            members.Single().Id.ShouldBe(membership); members.Single().EffectiveFromUtc.ShouldBeGreaterThanOrEqualTo(confirmedAfter);
            (await _assignments.GetListAsync(x => x.TeacherUserId == target)).ShouldBeEmpty(); (await _enrollments.GetListAsync(x => x.StudentUserId == target)).ShouldBeEmpty();
        });
        using (ChangeUser(f.Manager)) { await _service.EndMembershipAsync(f.School.Id, membership, new() { EffectiveToUtc = DateTime.UtcNow }); }
        using (ChangeUser(target)) {
            (await _service.RespondToInvitationAsync(invitation.Id, response)).MembershipId.ShouldBe(membership);
            (await _service.GetMySchoolsAsync()).Items.ShouldBeEmpty(); (await GetRequiredService<IKnowledgeLibraryAppService>().GetMyLibraryAsync()).Id.ShouldBe(libraryId);
        }
    }

    [Fact]
    public async Task Invitations_Should_Keep_Decline_And_Revocation_Private_And_Reject_Expired_Or_Unqualified_Sources()
    {
        var f = await CreateFixtureAsync(); var target = await CreateUserAsync(); SchoolInvitationDto invitation;
        using (ChangeUser(f.Manager)) { invitation = await _service.CreateInvitationAsync(f.School.Id, InviteInput(target, SchoolMemberRole.Student)); }
        using (ChangeUser(target)) {
            var input = new RespondSchoolInvitationDto { OperationId = Guid.NewGuid(), ConcurrencyStamp = invitation.ConcurrencyStamp, Response = SchoolInvitationResponse.Decline, DeclineReason = SchoolInvitationDeclineReason.IdentityMismatch };
            (await _service.RespondToInvitationAsync(invitation.Id, input)).Status.ShouldBe(SchoolInvitationStatus.Declined);
            (await _service.RespondToInvitationAsync(invitation.Id, input)).ResponseOperationId.ShouldBe(input.OperationId);
            (await _service.GetMySchoolsAsync()).Items.ShouldBeEmpty();
        }
        using (ChangeUser(f.Manager)) {
            invitation = await _service.CreateInvitationAsync(f.School.Id, InviteInput(target));
            var revoke = new RevokeSchoolInvitationDto { OperationId = Guid.NewGuid(), ConcurrencyStamp = invitation.ConcurrencyStamp, Reason = "PRIVATE-SUPPORT-REVOCATION" };
            (await _service.RevokeInvitationAsync(f.School.Id, invitation.Id, revoke)).RevocationOperationId.ShouldBe(revoke.OperationId);
            (await _service.RevokeInvitationAsync(f.School.Id, invitation.Id, revoke)).RevocationOperationId.ShouldBe(revoke.OperationId);
        }
        using (ChangeUser(target)) {
            var row = await _service.GetMyInvitationAsync(invitation.Id); row.Status.ShouldBe(SchoolInvitationStatus.Revoked); row.CanRespond.ShouldBeFalse();
            JsonSerializer.Serialize(row).ShouldNotContain("PRIVATE-SUPPORT"); JsonSerializer.Serialize(row).ShouldNotContain("RevocationReason");
            (await Should.ThrowAsync<BusinessException>(() => _service.RespondToInvitationAsync(row.Id, AcceptInvitation(row)))).Code.ShouldBe(SchoolInvitationErrors.Unavailable);
        }
        var expired = new SchoolInvitation(Guid.NewGuid(), null, f.School.Id, f.Manager, target, SchoolMemberRole.Student,
            DateTime.UtcNow.AddDays(-3), null, DateTime.UtcNow.AddDays(-1), true, new string('a', 64), DateTime.UtcNow.AddDays(-2));
        await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<SchoolInvitation, Guid>>().InsertAsync(expired, autoSave: true));
        using (ChangeUser(target)) {
            var row = await _service.GetMyInvitationAsync(expired.Id); row.Status.ShouldBe(SchoolInvitationStatus.Expired); row.CanRespond.ShouldBeFalse();
            (await _service.GetMyInvitationsAsync(new())).TotalCount.ShouldBe(0);
            (await Should.ThrowAsync<BusinessException>(() => _service.RespondToInvitationAsync(row.Id, AcceptInvitation(row)))).Code.ShouldBe(SchoolInvitationErrors.Unavailable);
        }
        using (ChangeUser(f.Manager)) {
            invitation = await _service.CreateInvitationAsync(f.School.Id, InviteInput(target));
            await _service.AddMembershipAsync(f.School.Id, new() { UserId = f.SecondTeacher, Role = SchoolMemberRole.Manager, EffectiveFromUtc = DateTime.UtcNow.AddMinutes(-1) });
            var manager = (await _service.GetMembershipsAsync(f.School.Id, new() { UserId = f.Manager, Role = SchoolMemberRole.Manager })).Items.Single();
            await _service.EndMembershipAsync(f.School.Id, manager.Id, new() { EffectiveToUtc = DateTime.UtcNow });
        }
        using (ChangeUser(target)) {
            var row = await _service.GetMyInvitationAsync(invitation.Id); row.CanRespond.ShouldBeFalse();
            (await Should.ThrowAsync<BusinessException>(() => _service.RespondToInvitationAsync(row.Id, AcceptInvitation(row)))).Code.ShouldBe(SchoolInvitationErrors.Unavailable);
            (await _service.GetMySchoolsAsync()).Items.ShouldBeEmpty();
        }
    }
}
