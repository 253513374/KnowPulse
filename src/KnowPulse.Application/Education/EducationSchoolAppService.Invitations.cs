using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationSchoolAppService
{
    private IRepository<SchoolInvitation, Guid> Invitations => LazyServiceProvider.LazyGetRequiredService<IRepository<SchoolInvitation, Guid>>();
    private IUnitOfWorkManager SchoolUow => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();
    private static string SchoolCommandFingerprint(object input) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));
    private static int SchoolPage(PagedResultRequestDto input)
    {
        if (input.SkipCount < 0 || input.MaxResultCount is < 1 or > 100) throw new BusinessException(SchoolInvitationErrors.Invalid);
        return input.MaxResultCount;
    }
    private async Task<SchoolInvitation> FindInvitationAsync(Guid id, Guid? schoolId = null, bool mine = false)
    {
        EducationRules.RequireId(id); using var tracking = Invitations.DisableTracking(); var invite = await Invitations.FindAsync(id);
        if (invite == null || invite.TenantId != CurrentTenant.Id || schoolId.HasValue && invite.SchoolId != schoolId || mine && invite.TargetUserId != CurrentUser.Id)
            throw new AbpAuthorizationException();
        return invite;
    }
    private async Task<bool> InvitationIssuerEffectiveAsync(SchoolInvitation invite, DateTime now)
    {
        var users = await _users.GetQueryableAsync(); var members = await _memberships.GetQueryableAsync();
        return await AsyncExecuter.AnyAsync(members.Where(x => x.SchoolId == invite.SchoolId && x.TenantId == invite.TenantId && x.UserId == invite.InvitedByUserId &&
            x.Role == SchoolMemberRole.Manager && x.EffectiveFromUtc <= now && (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc) &&
            users.Any(u => u.Id == x.UserId && u.TenantId == invite.TenantId && u.IsActive)));
    }
    private async Task<School> SchoolAuthorityBarrierAsync(Guid schoolId, bool manager, params Guid[] accounts)
    {
        await GetAuthenticatedUserIdAsync();
        if (manager) await GetManagedSchoolAsync(schoolId);
        using var schools = _schools.EnableTracking(); using var users = _users.EnableTracking();
        var school = await _schools.FindAsync(schoolId);
        if (school == null || school.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        await _schools.UpdateManyAsync([school], autoSave: true);
        foreach (var id in accounts.Distinct().Order()) {
            var account = await _users.FindAsync(id);
            if (account == null || account.TenantId != CurrentTenant.Id || !account.IsActive) throw new AbpAuthorizationException();
            await _users.UpdateManyAsync([account], autoSave: true);
        }
        await GetAuthenticatedUserIdAsync();
        if (manager) await GetManagedSchoolAsync(schoolId);
        return school;
    }
    private static void FillInvitation(MySchoolInvitationDto dto, SchoolInvitation row, School school, DateTime now, bool canRespond)
    {
        dto.Id = row.Id; dto.SchoolId = row.SchoolId; dto.SchoolName = school.Name; dto.SchoolCode = school.Code; dto.TargetUserId = row.TargetUserId; dto.Role = row.Role;
        dto.EffectiveFromUtc = row.EffectiveFromUtc; dto.EffectiveToUtc = row.EffectiveToUtc; dto.ExpiresAtUtc = row.ExpiresAtUtc; dto.RecordedAtUtc = row.RecordedAtUtc;
        dto.Status = row.StatusAt(now); dto.CompletedAtUtc = row.CompletedAtUtc; dto.MembershipId = row.MembershipId; dto.DeclineReason = row.DeclineReason;
        dto.ResponseOperationId = row.Status is SchoolInvitationStatus.Accepted or SchoolInvitationStatus.Declined ? row.CompletionOperationId : null;
        dto.CanRespond = canRespond && dto.Status == SchoolInvitationStatus.Pending; dto.ConcurrencyStamp = row.ConcurrencyStamp;
    }
    private async Task<MySchoolInvitationDto> MyInvitationDtoAsync(SchoolInvitation row)
    {
        var school = await _schools.FindAsync(row.SchoolId); if (school == null || school.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        var now = DateTime.UtcNow; var dto = new MySchoolInvitationDto();
        FillInvitation(dto, row, school, now, row.TargetUserId == CurrentUser.Id && await InvitationIssuerEffectiveAsync(row, now)); return dto;
    }
    private async Task<SchoolInvitationDto> ManagedInvitationDtoAsync(SchoolInvitation row, School school)
    {
        var account = await _users.FindAsync(row.TargetUserId); var now = DateTime.UtcNow;
        var dto = new SchoolInvitationDto { InvitedByUserId = row.InvitedByUserId, TargetDisplayName = account == null ? "" : GetAccountDisplayName(account),
            RevokedByUserId = row.Status == SchoolInvitationStatus.Revoked ? row.CompletedByUserId : null, RevocationReason = row.RevocationReason,
            RevocationOperationId = row.Status == SchoolInvitationStatus.Revoked ? row.CompletionOperationId : null,
            CanRevoke = row.StatusAt(now) == SchoolInvitationStatus.Pending };
        FillInvitation(dto, row, school, now, false); return dto;
    }

    [Authorize, UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<MySchoolInvitationDto>> GetMyInvitationsAsync(GetMySchoolInvitationsInput input)
    {
        var user = await GetAuthenticatedUserIdAsync(); var take = SchoolPage(input); var now = DateTime.UtcNow;
        using var uow = SchoolUow.Begin(requiresNew: true, isTransactional: false); using var tracking = Invitations.DisableTracking();
        var schools = await _schools.GetQueryableAsync(); var query = (await Invitations.GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.TargetUserId == user && schools.Any(s => s.Id == x.SchoolId && s.TenantId == x.TenantId));
        if (!input.IncludeInactive) query = query.Where(x => x.Status == SchoolInvitationStatus.Pending && x.ExpiresAtUtc > now && (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc));
        var total = await AsyncExecuter.LongCountAsync(query); var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.RecordedAtUtc).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take));
        var items = new List<MySchoolInvitationDto>(); foreach (var row in rows) items.Add(await MyInvitationDtoAsync(row));
        await uow.CompleteAsync(); return new(total, items);
    }
    [Authorize, UnitOfWork(IsDisabled = true)]
    public virtual async Task<MySchoolInvitationDto> GetMyInvitationAsync(Guid invitationId)
    {
        await GetAuthenticatedUserIdAsync(); using var uow = SchoolUow.Begin(requiresNew: true, isTransactional: false);
        var dto = await MyInvitationDtoAsync(await FindInvitationAsync(invitationId, mine: true)); await uow.CompleteAsync(); return dto;
    }
    [Authorize(EducationPermissions.SchoolManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<SchoolInvitationDto>> GetInvitationsAsync(Guid schoolId, PagedResultRequestDto input)
    {
        using var uow = SchoolUow.Begin(requiresNew: true, isTransactional: false); var school = await GetManagedSchoolAsync(schoolId); var take = SchoolPage(input);
        using var tracking = Invitations.DisableTracking(); var query = (await Invitations.GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.SchoolId == schoolId);
        var total = await AsyncExecuter.LongCountAsync(query); var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.RecordedAtUtc).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take));
        var items = new List<SchoolInvitationDto>(); foreach (var row in rows) items.Add(await ManagedInvitationDtoAsync(row, school));
        await uow.CompleteAsync(); return new(total, items);
    }
    [Authorize(EducationPermissions.SchoolManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<SchoolInvitationDto> GetInvitationAsync(Guid schoolId, Guid invitationId)
    {
        using var uow = SchoolUow.Begin(requiresNew: true, isTransactional: false); var school = await GetManagedSchoolAsync(schoolId);
        var dto = await ManagedInvitationDtoAsync(await FindInvitationAsync(invitationId, schoolId), school); await uow.CompleteAsync(); return dto;
    }
    [Authorize(EducationPermissions.SchoolManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<SchoolInvitationDto> CreateInvitationAsync(Guid schoolId, CreateSchoolInvitationDto input)
    {
        EducationRules.RequireId(input.OperationId); var actor = await GetAuthenticatedUserIdAsync();
        var fp = SchoolCommandFingerprint(new { Command = "school-invite", Tenant = CurrentTenant.Id, schoolId, actor, input });
        using var uow = SchoolUow.Begin(requiresNew: true, isTransactional: true);
        var school = await SchoolAuthorityBarrierAsync(schoolId, true, actor, input.TargetUserId);
        var prior = await Invitations.FindAsync(input.OperationId);
        if (prior != null) {
            if (prior.SchoolId != schoolId || prior.InvitedByUserId != actor || prior.TenantId != CurrentTenant.Id || prior.RequestFingerprint != fp) throw new BusinessException(SchoolInvitationErrors.OperationConflict);
            var receipt = await ManagedInvitationDtoAsync(prior, school); await uow.CompleteAsync(); return receipt;
        }
        var now = DateTime.UtcNow; var row = new SchoolInvitation(input.OperationId, CurrentTenant.Id, schoolId, actor, input.TargetUserId, input.Role,
            input.EffectiveFromUtc, input.EffectiveToUtc, input.ExpiresAtUtc, input.ConfirmVerifiedAccount, fp, now);
        if (await Invitations.AnyAsync(x => x.SchoolId == schoolId && x.TenantId == CurrentTenant.Id && x.TargetUserId == input.TargetUserId && x.Role == input.Role &&
            x.Status == SchoolInvitationStatus.Pending && x.ExpiresAtUtc > now && (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc))) throw new BusinessException(SchoolInvitationErrors.PendingExists);
        await Invitations.InsertAsync(row, autoSave: true); var result = await ManagedInvitationDtoAsync(row, school); await uow.CompleteAsync(); return result;
    }
    private async Task CheckInvitationCompletionAsync(SchoolInvitation row, Guid operation, string fingerprint)
    {
        EducationRules.RequireId(operation);
        if (row.CompletionOperationId == operation && row.CompletionFingerprint != fingerprint ||
            await Invitations.AnyAsync(x => x.CompletionOperationId == operation && x.Id != row.Id)) throw new BusinessException(SchoolInvitationErrors.OperationConflict);
    }
    [Authorize, UnitOfWork(IsDisabled = true)]
    public virtual async Task<MySchoolInvitationDto> RespondToInvitationAsync(Guid invitationId, RespondSchoolInvitationDto input)
    {
        var actor = await GetAuthenticatedUserIdAsync();
        if (!Enum.IsDefined(input.Response) || input.Response == SchoolInvitationResponse.Accept && input.DeclineReason.HasValue ||
            input.Response == SchoolInvitationResponse.Decline && (input.DeclineReason == null || input.ConfirmIdentityAndSchool)) throw new BusinessException(SchoolInvitationErrors.Invalid);
        var fp = SchoolCommandFingerprint(new { Command = "school-invite-response", Tenant = CurrentTenant.Id, invitationId, actor, input });
        using var uow = SchoolUow.Begin(requiresNew: true, isTransactional: true);
        var original = await FindInvitationAsync(invitationId, mine: true); await CheckInvitationCompletionAsync(original, input.OperationId, fp);
        if (original.CompletionOperationId == input.OperationId) { var prior = await MyInvitationDtoAsync(original); await uow.CompleteAsync(); return prior; }
        var school = await SchoolAuthorityBarrierAsync(original.SchoolId, false, actor, original.InvitedByUserId);
        using var tracking = Invitations.EnableTracking(); var row = await Invitations.GetAsync(invitationId); var now = DateTime.UtcNow;
        if (row.ConcurrencyStamp != input.ConcurrencyStamp) throw new AbpDbConcurrencyException();
        if (!await InvitationIssuerEffectiveAsync(row, now) || row.StatusAt(now) != SchoolInvitationStatus.Pending) throw new BusinessException(SchoolInvitationErrors.Unavailable);
        if (input.Response == SchoolInvitationResponse.Accept) {
            if (!input.ConfirmIdentityAndSchool) throw new BusinessException(SchoolInvitationErrors.Invalid);
            var from = row.EffectiveFromUtc > now ? row.EffectiveFromUtc : now;
            var members = await _memberships.GetQueryableAsync();
            var existing = await AsyncExecuter.FirstOrDefaultAsync(members.Where(x => x.TenantId == row.TenantId && x.SchoolId == row.SchoolId && x.UserId == row.TargetUserId && x.Role == row.Role &&
                x.EffectiveFromUtc <= from && (!x.EffectiveToUtc.HasValue || row.EffectiveToUtc.HasValue && row.EffectiveToUtc <= x.EffectiveToUtc)));
            var membership = existing;
            if (membership == null) {
                var created = await AddMembershipAsync(school, new() { UserId = row.TargetUserId, Role = row.Role, EffectiveFromUtc = from, EffectiveToUtc = row.EffectiveToUtc });
                membership = await _memberships.GetAsync(created.Id);
            }
            row.Accept(actor, input.OperationId, fp, membership, input.ConfirmIdentityAndSchool, now);
        } else row.Decline(actor, input.OperationId, fp, input.DeclineReason!.Value, now);
        await Invitations.UpdateManyAsync([row], autoSave: true); var result = await MyInvitationDtoAsync(row); await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.SchoolManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<SchoolInvitationDto> RevokeInvitationAsync(Guid schoolId, Guid invitationId, RevokeSchoolInvitationDto input)
    {
        var actor = await GetAuthenticatedUserIdAsync(); var fp = SchoolCommandFingerprint(new { Command = "school-invite-revoke", Tenant = CurrentTenant.Id, schoolId, invitationId, actor, input });
        using var uow = SchoolUow.Begin(requiresNew: true, isTransactional: true); var school = await SchoolAuthorityBarrierAsync(schoolId, true, actor);
        var original = await FindInvitationAsync(invitationId, schoolId); await CheckInvitationCompletionAsync(original, input.OperationId, fp);
        if (original.CompletionOperationId == input.OperationId) { var prior = await ManagedInvitationDtoAsync(original, school); await uow.CompleteAsync(); return prior; }
        using var tracking = Invitations.EnableTracking(); var row = await Invitations.GetAsync(invitationId);
        if (row.ConcurrencyStamp != input.ConcurrencyStamp) throw new AbpDbConcurrencyException();
        row.Revoke(actor, input.OperationId, fp, input.Reason, DateTime.UtcNow); await Invitations.UpdateManyAsync([row], autoSave: true);
        var result = await ManagedInvitationDtoAsync(row, school); await uow.CompleteAsync(); return result;
    }
}
