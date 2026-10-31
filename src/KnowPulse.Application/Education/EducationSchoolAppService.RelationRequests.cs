using System;
using System.Collections.Generic;
using System.Linq;
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
    private IRepository<TeachingRelationRequest, Guid> RelationRequests => LazyServiceProvider.LazyGetRequiredService<IRepository<TeachingRelationRequest, Guid>>();
    private async Task<SchoolMembership> TeacherMemberAsync(Guid schoolId, Guid? expectedMember = null)
    {
        var actor = await GetAuthenticatedUserIdAsync(); var now = DateTime.UtcNow; var school = await _schools.FindAsync(schoolId);
        if (school == null || school.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        var query = (await _memberships.GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.SchoolId == schoolId && x.UserId == actor && x.Role == SchoolMemberRole.Teacher &&
            x.EffectiveFromUtc <= now && (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc));
        if (expectedMember.HasValue) query = query.Where(x => x.Id == expectedMember);
        return await AsyncExecuter.FirstOrDefaultAsync(query.OrderBy(x => x.Id)) ?? throw new AbpAuthorizationException();
    }
    private async Task<TeachingRelationRequest> RelationRequestAsync(Guid id, Guid? schoolId = null, bool mine = false)
    {
        EducationRules.RequireId(id); using var tracking = RelationRequests.DisableTracking(); var row = await RelationRequests.FindAsync(id);
        if (row == null || row.TenantId != CurrentTenant.Id || schoolId.HasValue && row.SchoolId != schoolId || mine && row.TeacherUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        if (mine) await TeacherMemberAsync(row.SchoolId, row.SchoolMembershipId); return row;
    }
    private async Task<MyTeachingRelationDto> RelationDtoAsync(TeachingAssignment row) => new() { Id = row.Id, EffectiveFromUtc = row.EffectiveFromUtc, EffectiveToUtc = row.EffectiveToUtc,
        AcademicTermName = (await _terms.GetAsync(row.AcademicTermId)).Name, ClassName = (await _classes.GetAsync(row.SchoolClassId)).Name, SubjectName = (await _subjects.GetAsync(row.EducationSubjectId)).Name };
    private async Task<TeachingRelationRequestDto> RelationRequestDtoAsync(TeachingRelationRequest row)
    {
        var school = await _schools.GetAsync(row.SchoolId); var user = await _users.FindAsync(row.TeacherUserId);
        using var tracking = _assignments.DisableTracking(); var original = row.OriginalAssignmentId is Guid id ? await _assignments.FindAsync(id) : null;
        var result = row.ResultingAssignmentId is Guid resultId ? await _assignments.FindAsync(resultId) : null;
        return new() { Id = row.Id, SchoolId = school.Id, SchoolName = school.Name, SchoolCode = school.Code, TeacherUserId = row.TeacherUserId, TeacherDisplayName = user == null ? "" : GetAccountDisplayName(user),
            OriginalAssignmentId = row.OriginalAssignmentId, Original = original == null ? null : await RelationDtoAsync(original), OriginalEndsAtRequestUtc = row.OriginalEndsAtUtc,
            OriginalAssignmentStamp = original?.ConcurrencyStamp, OriginalChanged = row.OriginalAssignmentId.HasValue && (original == null || original.ConcurrencyStamp != row.OriginalAssignmentStamp),
            Text = row.RequestText, RecordedAtUtc = row.RecordedAtUtc, Status = row.Status, Resolution = row.Resolution, ResolutionText = row.ResolutionText,
            ResultingAssignmentId = row.ResultingAssignmentId, Resulting = result == null ? null : await RelationDtoAsync(result), EndedAtUtc = row.EndedAtUtc,
            CompletedByUserId = row.CompletedByUserId, CompletionOperationId = row.CompletionOperationId, CompletedAtUtc = row.CompletedAtUtc,
            SchoolConfigurationRevision = school.ConfigurationRevision, CanWithdraw = row.TeacherUserId == CurrentUser.Id && row.Status == TeachingRelationRequestStatus.Pending, ConcurrencyStamp = row.ConcurrencyStamp };
    }
    [Authorize, UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<MyTeachingRelationDto>> GetMyTeachingRelationsAsync(Guid schoolId, PagedResultRequestDto input)
    {
        using var uow = SchoolUow.Begin(requiresNew: true, isTransactional: false); var member = await TeacherMemberAsync(schoolId); var take = SchoolPage(input);
        using var tracking = _assignments.DisableTracking(); var query = (await _assignments.GetQueryableAsync()).Where(x => x.SchoolId == schoolId && x.TeacherUserId == member.UserId && x.TenantId == CurrentTenant.Id);
        var total = await AsyncExecuter.LongCountAsync(query); var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.EffectiveFromUtc).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take));
        var items = new List<MyTeachingRelationDto>(); foreach (var row in rows) items.Add(await RelationDtoAsync(row)); await uow.CompleteAsync(); return new(total, items);
    }
    [Authorize, UnitOfWork(IsDisabled = true)]
    public virtual Task<PagedResultDto<TeachingRelationRequestDto>> GetMyRelationRequestsAsync(Guid schoolId, PagedResultRequestDto input) => RelationRequestListAsync(schoolId, input, true);
    [Authorize(EducationPermissions.SchoolManage), UnitOfWork(IsDisabled = true)]
    public virtual Task<PagedResultDto<TeachingRelationRequestDto>> GetRelationRequestsAsync(Guid schoolId, PagedResultRequestDto input) => RelationRequestListAsync(schoolId, input, false);
    private async Task<PagedResultDto<TeachingRelationRequestDto>> RelationRequestListAsync(Guid schoolId, PagedResultRequestDto input, bool mine)
    {
        var actor = await GetAuthenticatedUserIdAsync(); using var uow = SchoolUow.Begin(requiresNew: true, isTransactional: false); var take = SchoolPage(input);
        if (!mine) await GetManagedSchoolAsync(schoolId);
        using var tracking = RelationRequests.DisableTracking(); var query = (await RelationRequests.GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.SchoolId == schoolId);
        if (mine) {
            var now = DateTime.UtcNow; var members = await _memberships.GetQueryableAsync(); var schools = await _schools.GetQueryableAsync();
            query = query.Where(x => x.TeacherUserId == actor && schools.Any(s => s.Id == x.SchoolId && s.TenantId == x.TenantId) && members.Any(m => m.Id == x.SchoolMembershipId && m.SchoolId == x.SchoolId &&
                m.UserId == actor && m.TenantId == x.TenantId && m.Role == SchoolMemberRole.Teacher && m.EffectiveFromUtc <= now && (!m.EffectiveToUtc.HasValue || now < m.EffectiveToUtc)));
        }
        var total = await AsyncExecuter.LongCountAsync(query); var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.RecordedAtUtc).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take));
        var items = new List<TeachingRelationRequestDto>(); foreach (var row in rows) { if (mine) await TeacherMemberAsync(schoolId, row.SchoolMembershipId); items.Add(await RelationRequestDtoAsync(row)); }
        await uow.CompleteAsync(); return new(total, items);
    }
    [Authorize, UnitOfWork(IsDisabled = true)]
    public virtual async Task<TeachingRelationRequestDto> GetMyRelationRequestAsync(Guid requestId)
    {
        await GetAuthenticatedUserIdAsync(); using var uow = SchoolUow.Begin(requiresNew: true, isTransactional: false); var result = await RelationRequestDtoAsync(await RelationRequestAsync(requestId, mine: true)); await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.SchoolManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<TeachingRelationRequestDto> GetRelationRequestAsync(Guid schoolId, Guid requestId)
    {
        using var uow = SchoolUow.Begin(requiresNew: true, isTransactional: false); await GetManagedSchoolAsync(schoolId);
        var result = await RelationRequestDtoAsync(await RelationRequestAsync(requestId, schoolId)); await uow.CompleteAsync(); return result;
    }
    [Authorize, UnitOfWork(IsDisabled = true)]
    public virtual async Task<TeachingRelationRequestDto> SubmitRelationRequestAsync(Guid schoolId, SubmitTeachingRelationRequestDto input)
    {
        EducationRules.RequireId(input.OperationId); var actor = await GetAuthenticatedUserIdAsync();
        var fp = SchoolCommandFingerprint(new { Command = "relation-request", Tenant = CurrentTenant.Id, schoolId, actor, input });
        using var uow = SchoolUow.Begin(requiresNew: true, isTransactional: true); await SchoolAuthorityBarrierAsync(schoolId, false, actor); var member = await TeacherMemberAsync(schoolId);
        var prior = await RelationRequests.FindAsync(input.OperationId);
        if (prior != null) { if (prior.TeacherUserId != actor || prior.TenantId != CurrentTenant.Id || prior.SchoolId != schoolId || prior.RequestFingerprint != fp) throw new BusinessException(TeachingRelationRequestErrors.Conflict);
            await TeacherMemberAsync(schoolId, prior.SchoolMembershipId); var receipt = await RelationRequestDtoAsync(prior); await uow.CompleteAsync(); return receipt; }
        using var tracking = _assignments.DisableTracking(); var original = input.OriginalAssignmentId is Guid id ? await _assignments.FindAsync(EducationRules.RequireId(id)) ?? throw new AbpAuthorizationException() : null;
        if (original != null && (original.SchoolId != schoolId || original.TeacherUserId != actor || original.TenantId != CurrentTenant.Id)) throw new AbpAuthorizationException();
        var row = new TeachingRelationRequest(input.OperationId, member, original, input.Text, input.ConfirmSharingWithSchool, fp, DateTime.UtcNow);
        await RelationRequests.InsertAsync(row, autoSave: true); var result = await RelationRequestDtoAsync(row); await uow.CompleteAsync(); return result;
    }
    private async Task<bool> RelationCompletionAsync(TeachingRelationRequest row, Guid operation, string fingerprint)
    {
        EducationRules.RequireId(operation);
        if (row.CompletionOperationId == operation && row.CompletionFingerprint != fingerprint || await RelationRequests.AnyAsync(x => x.CompletionOperationId == operation && x.Id != row.Id)) throw new BusinessException(TeachingRelationRequestErrors.Conflict);
        return row.CompletionOperationId == operation;
    }
    [Authorize, UnitOfWork(IsDisabled = true)]
    public virtual async Task<TeachingRelationRequestDto> WithdrawMyRelationRequestAsync(Guid requestId, WithdrawTeachingRelationRequestDto input)
    {
        var actor = await GetAuthenticatedUserIdAsync(); var fp = SchoolCommandFingerprint(new { Command = "relation-request-withdraw", Tenant = CurrentTenant.Id, requestId, actor, input });
        using var uow = SchoolUow.Begin(requiresNew: true, isTransactional: true); var original = await RelationRequestAsync(requestId, mine: true);
        if (await RelationCompletionAsync(original, input.OperationId, fp)) { var receipt = await RelationRequestDtoAsync(original); await uow.CompleteAsync(); return receipt; }
        await SchoolAuthorityBarrierAsync(original.SchoolId, false, actor); await TeacherMemberAsync(original.SchoolId, original.SchoolMembershipId);
        using var tracking = RelationRequests.EnableTracking(); var row = await RelationRequests.GetAsync(requestId); if (row.ConcurrencyStamp != input.ConcurrencyStamp) throw new AbpDbConcurrencyException();
        row.Withdraw(actor, input.OperationId, fp, DateTime.UtcNow); await RelationRequests.UpdateManyAsync([row], autoSave: true); var result = await RelationRequestDtoAsync(row); await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.SchoolManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<TeachingRelationRequestDto> ResolveRelationRequestAsync(Guid schoolId, Guid requestId, ResolveTeachingRelationRequestDto input)
    {
        var actor = await GetAuthenticatedUserIdAsync(); var fp = SchoolCommandFingerprint(new { Command = "relation-request-resolve", Tenant = CurrentTenant.Id, schoolId, requestId, actor, input });
        using var uow = SchoolUow.Begin(requiresNew: true, isTransactional: true); await GetManagedSchoolAsync(schoolId); var original = await RelationRequestAsync(requestId, schoolId);
        if (await RelationCompletionAsync(original, input.OperationId, fp)) { var receipt = await RelationRequestDtoAsync(original); await uow.CompleteAsync(); return receipt; }
        var adding = input.Resolution is TeachingRelationResolution.Add or TeachingRelationResolution.Replace; var ending = input.Resolution is TeachingRelationResolution.End or TeachingRelationResolution.Replace;
        var school = await SchoolAuthorityBarrierAsync(schoolId, true, adding ? [actor, original.TeacherUserId] : [actor]);
        using var tracking = RelationRequests.EnableTracking(); var row = await RelationRequests.GetAsync(requestId);
        if (row.ConcurrencyStamp != input.ConcurrencyStamp || school.ConfigurationRevision != input.SchoolConfigurationRevision) throw new AbpDbConcurrencyException();
        if (!input.ConfirmReviewedAccountAndChange || !Enum.IsDefined(input.Resolution) || row.Status != TeachingRelationRequestStatus.Pending ||
            ending && row.OriginalAssignmentId == null || input.Resolution == TeachingRelationResolution.Add && row.OriginalAssignmentId.HasValue ||
            adding && (input.AcademicTermId == null || input.SchoolClassId == null || input.EducationSubjectId == null) ||
            !adding && (input.AcademicTermId.HasValue || input.SchoolClassId.HasValue || input.EducationSubjectId.HasValue || input.EffectiveToUtc.HasValue) ||
            (adding || ending) != input.EffectiveFromUtc.HasValue) throw new BusinessException(TeachingRelationRequestErrors.Invalid);
        Check.NotNullOrWhiteSpace(input.Text, nameof(input.Text), 2000); var now = DateTime.UtcNow;
        var from = input.EffectiveFromUtc is DateTime at ? EducationRules.RequireUtc(at) > now ? at : now : (DateTime?)null;
        TeachingAssignment? old = null;
        if (ending) {
            using var assignmentTracking = _assignments.DisableTracking(); old = await _assignments.GetAsync(row.OriginalAssignmentId!.Value);
            if (old.ConcurrencyStamp != row.OriginalAssignmentStamp || old.ConcurrencyStamp != input.OriginalAssignmentStamp) throw new BusinessException(TeachingRelationRequestErrors.Changed);
            EducationRules.EndPeriod(old.EffectiveFromUtc, old.EffectiveToUtc, from!.Value);
        }
        TeachingAssignment? result = null; var existed = false;
        if (adding) {
            var member = await _memberships.FindAsync(row.SchoolMembershipId); if (member == null || member.TenantId != row.TenantId || member.SchoolId != row.SchoolId || member.UserId != row.TeacherUserId || member.Role != SchoolMemberRole.Teacher || !member.IsEffectiveAt(now)) throw new BusinessException(TeachingRelationRequestErrors.Changed);
            (result, existed) = await PrepareTeachingAssignmentAsync(school, new() { TeacherUserId = row.TeacherUserId, AcademicTermId = input.AcademicTermId!.Value,
                SchoolClassId = input.SchoolClassId!.Value, EducationSubjectId = input.EducationSubjectId!.Value, EffectiveFromUtc = from!.Value, EffectiveToUtc = input.EffectiveToUtc }, ending ? old!.Id : null);
        }
        if (ending || adding && !existed) await AdvanceSchoolAsync(school);
        if (ending) { old!.EndAt(from!.Value); await _assignments.UpdateManyAsync([old], autoSave: true); }
        if (adding && !existed) await _assignments.InsertAsync(result!, autoSave: true);
        row.Process(actor, input.OperationId, fp, input.Resolution, input.Text, result, ending ? from : null, input.ConfirmReviewedAccountAndChange, now);
        await RelationRequests.UpdateManyAsync([row], autoSave: true); var dto = await RelationRequestDtoAsync(row); await uow.CompleteAsync(); return dto;
    }
}
