using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

[Authorize]
[ExposeServices(typeof(IEducationStudyTaskAppService), typeof(IEducationStudyHintReviewAppService), IncludeSelf = true)]
public partial class EducationStudyTaskAppService : KnowPulseAppService, IEducationStudyTaskAppService, IEducationStudyHintReviewAppService
{
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private async Task<T?> RefAsync<T>(Guid id) where T : class, IEntity<Guid> { using var tracking = Repo<T>().DisableTracking(); return await Repo<T>().FindAsync(id); }
    private async Task<List<T>> RowsAsync<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity<Guid> { using var tracking = Repo<T>().DisableTracking(); return await Repo<T>().GetListAsync(predicate); }
    private IUnitOfWorkManager Uow => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();
    private EducationAssessmentAccessGuard Access => LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAccessGuard>();
    private EducationCurriculumAccessGuard Curriculum => LazyServiceProvider.LazyGetRequiredService<EducationCurriculumAccessGuard>();
    private EducationMaterialDelivery MaterialDelivery => LazyServiceProvider.LazyGetRequiredService<EducationMaterialDelivery>();
    private static EducationMaterialAuthority Authority(EducationStudyTask task) => new(task.TenantId, task.OwnerUserId, task.SchoolId);
    private static BusinessException Invalid() => new(EducationStudyTaskErrors.Invalid);
    private static BusinessException Changed() => new(EducationStudyTaskErrors.Changed);
    private static string Hash(object value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static int Page(PagedResultRequestDto input) { if (input.SkipCount < 0 || input.MaxResultCount is < 1 or > 100) throw Invalid(); return input.MaxResultCount; }
    private async Task TeacherAsync() { await CheckMvpPermissionAsync(EducationPermissions.LearningManage); await Access.CurrentAccountAsync(); }
    private async Task StudentAsync() { await CheckMvpPermissionAsync(EducationPermissions.LearningParticipate); await Access.CurrentAccountAsync(); }
    private async Task<EducationStudyTask> OwnAsync(Guid id, bool write = true)
    {
        await TeacherAsync(); var task = await RefAsync<EducationStudyTask>(id) ?? throw new AbpAuthorizationException();
        if (task.TenantId != CurrentTenant.Id || task.OwnerUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        await Access.EnsureTeacherRelationAsync(task.Authority(), task.AcademicTermId, task.SchoolClassId, write);
        return await RefAsync<EducationStudyTask>(id) ?? throw new AbpAuthorizationException();
    }
    private async Task<EducationStudyTaskRevision> CurrentAsync(EducationStudyTask task)
    {
        var row = await RefAsync<EducationStudyTaskRevision>(task.CurrentRevisionId ?? throw Invalid()) ?? throw Invalid();
        if (row.TaskId != task.Id || row.OwnerUserId != task.OwnerUserId || row.TenantId != task.TenantId || row.RevisionNumber != task.RevisionNumber) throw Invalid(); return row;
    }
    private static EducationStudyRevisionDto Revision(EducationStudyTaskRevision row) => new() { Id = row.Id, TaskId = row.TaskId, RevisionNumber = row.RevisionNumber, Kind = row.Kind,
        Scope = row.Scope(), Content = row.Content(), PublicReason = row.PublicReason, RecordedAtUtc = row.RecordedAtUtc, RequestFingerprint = row.RequestFingerprint };
    private static void Stamp(EducationStudyTask task, Guid? expected, string? stamp) { if (task.CurrentRevisionId != expected || task.ConcurrencyStamp != stamp) throw new AbpDbConcurrencyException(); }
    private async Task<EducationStudyTaskRevision?> ReceiptAsync(Guid taskId, Guid operationId, string? fingerprint = null)
    {
        await TeacherAsync(); if (await RefAsync<EducationStudyTask>(taskId) == null) return null;
        await OwnAsync(taskId); var row = await RefAsync<EducationStudyTaskRevision>(operationId); if (row == null) return null;
        if (row.TaskId != taskId || row.TenantId != CurrentTenant.Id || row.OwnerUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        if (fingerprint != null && row.RequestFingerprint != fingerprint) throw new BusinessException(EducationStudyTaskErrors.OperationConflict); return row;
    }
    private async Task AppendAsync(EducationStudyTask task, EducationStudyTaskRevision revision)
    { task.Advance(revision); await Repo<EducationStudyTaskRevision>().InsertAsync(revision, autoSave: true); await Repo<EducationStudyTask>().UpdateManyAsync([task], autoSave: true); }
    private async Task ValidateContentAsync(EducationStudyTask task, EducationStudyTaskContent content, EducationCurriculumSnapshot scope, bool published)
    {
        content.Validate(published); await Curriculum.ValidateAsync(scope);
        var teaching = await RefAsync<TeachingAssignment>(task.TeachingAssignmentId) ?? throw Invalid(); var term = await RefAsync<AcademicTerm>(task.AcademicTermId) ?? throw Invalid();
        if (content.StartsAtUtc < term.StartsAtUtc || content.StartsAtUtc < teaching.EffectiveFromUtc || content.EndsAtUtc > term.EndsAtUtc ||
            teaching.EffectiveToUtc is DateTime end && content.EndsAtUtc > end || published && content.EndsAtUtc <= DateTime.UtcNow) throw Invalid();
        if (content.Materials.Count > 0) await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        foreach (var material in content.Materials) if (!await MaterialDelivery.AvailableAsync(Authority(task), material)) throw new BusinessException(EducationTeachingPlanErrors.MaterialUnavailable);
    }
    [Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudyRevisionDto> SaveDraftAsync(Guid taskId, SaveEducationStudyTaskDto input)
    {
        await TeacherAsync(); EducationRules.RequireId(taskId); EducationRules.RequireId(input.OperationId); var fingerprint = EducationStudyTaskFingerprint.Draft(taskId, input);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var prior = await RefAsync<EducationStudyTaskRevision>(input.OperationId);
        if (prior != null) { var receipt = await ReceiptAsync(taskId, input.OperationId, fingerprint) ?? throw Invalid(); await uow.CompleteAsync(); return Revision(receipt); }
        var scope = await Curriculum.ResolveAsync(input.Scope); var teaching = await Access.EnsureTeachingAssignmentAsync(scope.TeachingAssignmentId, forUpdate: true);
        using var tracking = Repo<EducationStudyTask>().EnableTracking(); var task = await Repo<EducationStudyTask>().FindAsync(taskId);
        if (task == null) { if (input.ExpectedRevisionId != null || input.ConcurrencyStamp != null) throw new AbpDbConcurrencyException(); task = new(taskId, scope, teaching); }
        else { await OwnAsync(taskId); Stamp(task, input.ExpectedRevisionId, input.ConcurrencyStamp); }
        await ValidateContentAsync(task, input.Content, scope, false);
        var teacher = await RefAsync<IdentityUser>(task.OwnerUserId) ?? throw Invalid(); var schoolClass = await RefAsync<SchoolClass>(task.SchoolClassId) ?? throw Invalid();
        var revision = new EducationStudyTaskRevision(input.OperationId, task, EducationStudyTaskRevisionKind.Draft, scope, input.Content,
            string.Join(" ", new[] { teacher.Name, teacher.Surname }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } name ? name : teacher.UserName,
            schoolClass.Name, null, null, null, fingerprint, DateTime.UtcNow);
        if (task.RevisionNumber == 0) await Repo<EducationStudyTask>().InsertAsync(task, autoSave: true);
        await AppendAsync(task, revision); await uow.CompleteAsync(); return Revision(revision);
    }
    [Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudyTaskDto> GetAsync(Guid id)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var task = await OwnAsync(id);
        var result = new EducationStudyTaskDto { Id = task.Id, ConcurrencyStamp = task.ConcurrencyStamp, PublishedRevisionId = task.PublishedRevisionId,
            LastPublishedRevisionId = task.LastPublishedRevisionId, Current = Revision(await CurrentAsync(task)) }; await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.LearningManage)]
    public virtual async Task<PagedResultDto<EducationStudyTaskListDto>> GetListAsync(PagedResultRequestDto input)
    {
        await TeacherAsync(); var take = Page(input); var query = (await Repo<EducationStudyTask>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.OwnerUserId == CurrentUser.Id);
        var roots = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.CreationTime).ThenBy(x => x.Id).Take(1001));
        if (roots.Count > 1000) throw new BusinessException(EducationLearningReportErrors.Capacity); var rows = new List<EducationStudyTaskListDto>();
        foreach (var candidate in roots) {
            EducationStudyTask root;
            try { root = await OwnAsync(candidate.Id, write: false); } catch (AbpAuthorizationException) { continue; }
            var revision = await CurrentAsync(root); rows.Add(new() { Id = root.Id, Title = revision.Title, ClassName = revision.ClassName, SubjectName = revision.Scope().SubjectName,
                EndsAtUtc = revision.EndsAtUtc, PublishedRevisionId = root.PublishedRevisionId });
        }
        return new(rows.Count, rows.Skip(input.SkipCount).Take(take).ToList());
    }
    [Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudyRevisionDto?> GetOperationAsync(Guid taskId, Guid operationId)
    { using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var row = await ReceiptAsync(taskId, operationId); await uow.CompleteAsync(); return row == null ? null : Revision(row); }
    private async Task<List<EducationClassStudent>> RosterAsync(EducationStudyTask task, EducationStudyTaskRevision draft)
    {
        if (draft.Kind != EducationStudyTaskRevisionKind.Draft) throw Changed(); var content = draft.Content()!; await Curriculum.ValidateAsync(draft.Scope());
        var teaching = await RefAsync<TeachingAssignment>(task.TeachingAssignmentId) ?? throw Invalid();
        var rows = await LazyServiceProvider.LazyGetRequiredService<EducationClassRoster>().GetAsync(draft.Scope(), teaching,
            content.RecipientScope == EducationAssessmentRecipientScope.FullClass ? null : content.StudentUserIds);
        if (rows.Count == 0 || content.RecipientScope == EducationAssessmentRecipientScope.SelectedStudents && rows.Count != content.StudentUserIds.Count ||
            rows.Any(x => x.MembershipFrom > content.StartsAtUtc || x.EnrollmentFrom > content.StartsAtUtc || x.MembershipTo is DateTime m && content.EndsAtUtc > m || x.EnrollmentTo is DateTime e && content.EndsAtUtc > e)) throw Invalid(); return rows;
    }
    private static string RosterFingerprint(EducationStudyTaskRevision draft, List<EducationClassStudent> rows) => Hash(new { draft.Id, Students = rows.OrderBy(x => x.UserId) });
    [Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudyRosterDto> PreviewRecipientsAsync(Guid taskId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var task = await OwnAsync(taskId); var draft = await CurrentAsync(task); var rows = await RosterAsync(task, draft);
        var result = new EducationStudyRosterDto { DraftRevisionId = draft.Id, ConcurrencyStamp = task.ConcurrencyStamp, RosterFingerprint = RosterFingerprint(draft, rows),
            Students = rows.Select(x => new EducationAssessmentRosterStudentDto { StudentUserId = x.UserId, StudentName = x.Name, AccountName = x.AccountName }).ToList() };
        await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudyRevisionDto> PublishAsync(Guid taskId, PublishEducationStudyTaskDto input)
    {
        await TeacherAsync(); EducationRules.RequireId(input.OperationId); var fingerprint = EducationStudyTaskFingerprint.Publish(taskId, input);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var prior = await ReceiptAsync(taskId, input.OperationId, fingerprint);
        if (prior != null) { await uow.CompleteAsync(); return Revision(prior); }
        await OwnAsync(taskId); using var tracking = Repo<EducationStudyTask>().EnableTracking(); var task = await Repo<EducationStudyTask>().GetAsync(taskId);
        Stamp(task, input.ExpectedRevisionId, input.ConcurrencyStamp); var source = await CurrentAsync(task);
        if (!input.ConfirmRecipientsMaterialsAndSafeText) throw Invalid(); var rows = await RosterAsync(task, source);
        if (RosterFingerprint(source, rows) != input.RosterFingerprint) throw new BusinessException(EducationStudyTaskErrors.RosterChanged);
        using var accountsTracking = Repo<IdentityUser>().EnableTracking(); var ids = rows.Select(x => x.UserId).ToList(); var accounts = await Repo<IdentityUser>().GetListAsync(x => ids.Contains(x.Id) && x.TenantId == CurrentTenant.Id);
        if (accounts.Count != rows.Count || accounts.Any(x => !x.IsActive)) throw new BusinessException(EducationStudyTaskErrors.RosterChanged);
        await ValidateContentAsync(task, source.Content()!, source.Scope(), true);
        await Repo<IdentityUser>().UpdateManyAsync(accounts.OrderBy(x => x.Id).ToList(), autoSave: true);
        rows = await RosterAsync(task, source); if (RosterFingerprint(source, rows) != input.RosterFingerprint) throw new BusinessException(EducationStudyTaskErrors.RosterChanged);
        var revision = new EducationStudyTaskRevision(input.OperationId, task, EducationStudyTaskRevisionKind.Published, source.Scope(), source.Content(), source.TeacherName,
            source.ClassName, input.PublicReason, source, input.RosterFingerprint, fingerprint, DateTime.UtcNow);
        task.Advance(revision);
        await Repo<EducationStudyTaskRevision>().InsertAsync(revision, autoSave: true);
        await Repo<EducationStudyRecipient>().InsertManyAsync(rows.Select(x => new EducationStudyRecipient(revision, x.UserId, x.MembershipId, x.EnrollmentId, x.Name)).ToList(), autoSave: true);
        await Repo<EducationStudyTask>().UpdateManyAsync([task], autoSave: true); await uow.CompleteAsync(); return Revision(revision);
    }
    [Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudyRevisionDto> WithdrawAsync(Guid taskId, WithdrawEducationStudyTaskDto input)
    {
        await TeacherAsync(); EducationRules.RequireId(input.OperationId); var fingerprint = EducationStudyTaskFingerprint.Withdraw(taskId, input);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var prior = await ReceiptAsync(taskId, input.OperationId, fingerprint);
        if (prior != null) { await uow.CompleteAsync(); return Revision(prior); }
        await OwnAsync(taskId); using var tracking = Repo<EducationStudyTask>().EnableTracking(); var task = await Repo<EducationStudyTask>().GetAsync(taskId);
        Stamp(task, input.ExpectedRevisionId, input.ConcurrencyStamp); if (!input.ConfirmStudentVisibleReason) throw Invalid(); var source = await CurrentAsync(task);
        var revision = new EducationStudyTaskRevision(input.OperationId, task, EducationStudyTaskRevisionKind.Withdrawn, source.Scope(), null, source.TeacherName,
            source.ClassName, input.PublicReason, source, null, fingerprint, DateTime.UtcNow); await AppendAsync(task, revision); await uow.CompleteAsync(); return Revision(revision);
    }
}
