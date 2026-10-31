using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationAssessmentAppService
{
    private async Task<(EducationAssessmentRecipient Recipient, EducationAssessmentTask Task, EducationAssessmentTaskRevision Revision)> ReminderRecipientAsync(Guid recipientId, Guid schoolId, bool write = false)
    {
        EducationRules.RequireId(recipientId); EducationRules.RequireId(schoolId);
        await TeacherAsync();
        var recipient = await ReferenceAsync<EducationAssessmentRecipient>(recipientId) ?? throw new AbpAuthorizationException();
        if (recipient.SchoolId != schoolId || recipient.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        var (task, revision) = await GradingTaskAsync(recipient.TaskId, write);
        if (recipient.TaskRevisionId != revision.Id || revision.SchoolId != schoolId) throw new AbpAuthorizationException();
        if (write) {
            using var tracking = Repo<IdentityUser>().EnableTracking();
            var student = await Repo<IdentityUser>().FindAsync(recipient.StudentUserId) ?? throw new AbpAuthorizationException();
            if (!student.IsActive || student.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
            await Repo<IdentityUser>().UpdateManyAsync([student], autoSave: true);
            recipient = await ReferenceAsync<EducationAssessmentRecipient>(recipientId) ?? throw new AbpAuthorizationException();
        }
        await Access.ValidateRecordedStudentAsync(revision, recipient);
        return (recipient, task, revision);
    }

    private async Task<EducationAssessmentReminderPreviewDto> ReminderPreviewAsync(EducationAssessmentRecipient recipient, EducationAssessmentTask task, EducationAssessmentTaskRevision revision)
    {
        var attempt = recipient.CurrentAttemptId is Guid id ? await ReferenceAsync<EducationAssessmentAttempt>(id) : null;
        if (recipient.CurrentAttemptId.HasValue && (attempt == null || attempt.RecipientId != recipient.Id || attempt.TaskRevisionId != revision.Id || attempt.StudentUserId != recipient.StudentUserId)) throw Invalid();
        var last = await AsyncExecuter.FirstOrDefaultAsync((await Repo<EducationAssessmentReminder>().GetQueryableAsync())
            .Where(x => x.TenantId == CurrentTenant.Id && x.TaskId == task.Id && x.StudentUserId == recipient.StudentUserId && x.Kind == EducationAssessmentReminderKind.Manual).OrderByDescending(x => x.RecordedAtUtc).ThenBy(x => x.Id));
        var next = last?.RecordedAtUtc.AddMinutes(EducationAssessmentReminder.MinimumIntervalMinutes);
        var enabled = await LazyServiceProvider.LazyGetRequiredService<EducationTeachingFormalAssessmentGuard>().CanRemindAsync(recipient.Id);
        var now = DateTime.UtcNow;
        var reason = attempt?.SubmissionId != null ? "Submitted" : task.ClosureId.HasValue ? "Closed" : now >= revision.EndsAtUtc || attempt != null && now >= attempt.ExpiresAtUtc ? "Expired" :
            !enabled ? "PlanStopped" : next.HasValue && now < next.Value ? "CoolingDown" : string.Empty;
        return new() { Id = recipient.Id, SchoolId = recipient.SchoolId, TaskId = task.Id, PublicationId = revision.Id, StudentName = recipient.StudentName,
            TaskTitle = revision.Title, EndsAtUtc = attempt?.ExpiresAtUtc ?? revision.EndsAtUtc, ServerNowUtc = now, NextAllowedAtUtc = next, HasStarted = attempt != null,
            CanSend = reason.Length == 0, BlockReason = reason,
            PreviewFingerprint = Hash(new { recipient.Id, revision.TaskId, Publication = revision.Id, recipient.CurrentAttemptId, attempt?.SubmissionId, attempt?.ExpiresAtUtc, task.ClosureId, Last = last?.Id, enabled }) };
    }

    [Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationAssessmentReminderPreviewDto>> GetReminderTargetsAsync(GetEducationReminderTargetsInput input)
    {
        await TeacherAsync(); EducationRules.RequireId(input.SchoolId); var take = Page(input);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var (task, revision) = await GradingTaskAsync(input.TaskId);
        if (revision.SchoolId != input.SchoolId) throw new AbpAuthorizationException();
        var recipients = await Repo<EducationAssessmentRecipient>().GetQueryableAsync(); var members = await Repo<SchoolMembership>().GetQueryableAsync();
        var enrollments = await Repo<StudentEnrollment>().GetQueryableAsync(); var students = await Repo<IdentityUser>().GetQueryableAsync(); var now = DateTime.UtcNow;
        var query = from r in recipients join m in members on r.SchoolMembershipId equals m.Id join e in enrollments on r.StudentEnrollmentId equals e.Id join s in students on r.StudentUserId equals s.Id
            where r.TaskId == task.Id && r.TaskRevisionId == revision.Id && r.TenantId == CurrentTenant.Id && s.IsActive && m.Role == SchoolMemberRole.Student &&
                m.EffectiveFromUtc <= now && (!m.EffectiveToUtc.HasValue || now < m.EffectiveToUtc) && e.EffectiveFromUtc <= now && (!e.EffectiveToUtc.HasValue || now < e.EffectiveToUtc)
            select r;
        var count = await AsyncExecuter.LongCountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.StudentName).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take));
        var items = new List<EducationAssessmentReminderPreviewDto>();
        foreach (var row in rows) { await Access.ValidateRecordedStudentAsync(revision, row); items.Add(await ReminderPreviewAsync(row, task, revision)); }
        await uow.CompleteAsync(); return new(count, items);
    }

    [Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentReminderPreviewDto> GetReminderPreviewAsync(Guid recipientId, Guid schoolId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (r, task, revision) = await ReminderRecipientAsync(recipientId, schoolId);
        var result = await ReminderPreviewAsync(r, task, revision); await uow.CompleteAsync(); return result;
    }

    private static EducationAssessmentReminderDto ReminderDto(EducationAssessmentReminder row, bool canOpen = true) => new() { Id = row.Id, SchoolId = row.SchoolId,
        TaskId = row.TaskId, PublicationId = row.TaskRevisionId, RecipientId = row.RecipientId, Kind = row.Kind, ResultReleaseId = row.ResultReleaseId,
        CanOpen = canOpen, RecordedAtUtc = row.RecordedAtUtc, AvailableAtUtc = row.AvailableAtUtc, ReadAtUtc = row.ReadAtUtc };

    private async Task<EducationAssessmentReminderDto> InboxReminderDtoAsync(EducationAssessmentReminder row)
    {
        var canOpen = true;
        if (row.Kind == EducationAssessmentReminderKind.ResultReleased) {
            var task = await ReferenceAsync<EducationAssessmentTask>(row.TaskId) ?? throw new AbpAuthorizationException();
            var release = row.ResultReleaseId is Guid id ? await ReferenceAsync<EducationAssessmentResultRelease>(id) : null;
            canOpen = release != null && task.CurrentResultReleaseId == release.Id && release.VisibilityAt(DateTime.UtcNow) != ExamResultVisibility.Hidden;
        }
        return ReminderDto(row, canOpen);
    }

    private async Task EnsureReminderAvailableAsync(EducationAssessmentReminder row)
    {
        if (DateTime.UtcNow < row.AvailableAtUtc || row.Kind == EducationAssessmentReminderKind.ResultReleased &&
            await AsyncExecuter.AnyAsync((await Repo<EducationAssessmentResultRelease>().GetQueryableAsync()).Where(next => next.PreviousReleaseId == row.ResultReleaseId && next.ReleasedAtUtc <= row.AvailableAtUtc)))
            throw new BusinessException(EducationAssessmentErrorCodes.ReminderUnavailable);
    }

    private async Task<EducationAssessmentReminder?> ReminderReceiptAsync(Guid recipientId, Guid schoolId, Guid operationId, string? fingerprint = null)
    {
        EducationRules.RequireId(operationId);
        var row = await ReferenceAsync<EducationAssessmentReminder>(operationId); if (row == null) return null;
        if (row.Kind != EducationAssessmentReminderKind.Manual || row.RecipientId != recipientId || row.SchoolId != schoolId || row.TenantId != CurrentTenant.Id || row.TeacherUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        if (fingerprint != null && row.RequestFingerprint != fingerprint) throw Conflict(); return row;
    }

    [Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentReminderDto> SendReminderAsync(Guid recipientId, Guid schoolId, SendEducationAssessmentReminderDto input)
    {
        EducationRules.RequireId(input.OperationId);
        if (!input.ConfirmRecipient || input.PreviewFingerprint == null || input.PreviewFingerprint.Length != 64 || input.PreviewFingerprint.Any(x => !Uri.IsHexDigit(x))) throw Invalid();
        var fingerprint = Hash(new { Command = "assessment-reminder", recipientId, schoolId, input });
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var (recipient, task, revision) = await ReminderRecipientAsync(recipientId, schoolId, write: true);
        var prior = await ReminderReceiptAsync(recipientId, schoolId, input.OperationId, fingerprint);
        if (prior != null) { await uow.CompleteAsync(); return ReminderDto(prior); }
        var preview = await ReminderPreviewAsync(recipient, task, revision);
        if (input.PreviewFingerprint != preview.PreviewFingerprint) throw new AbpDbConcurrencyException();
        if (!preview.CanSend || DateTime.UtcNow >= preview.EndsAtUtc) throw new BusinessException(EducationAssessmentErrorCodes.ReminderUnavailable);
        var row = new EducationAssessmentReminder(input.OperationId, recipient, revision, fingerprint, DateTime.UtcNow);
        await Repo<EducationAssessmentReminder>().InsertAsync(row, autoSave: true); await uow.CompleteAsync(); return ReminderDto(row);
    }

    [Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentReminderDto?> GetReminderOperationAsync(Guid recipientId, Guid schoolId, Guid operationId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); await ReminderRecipientAsync(recipientId, schoolId);
        var row = await ReminderReceiptAsync(recipientId, schoolId, operationId); await uow.CompleteAsync(); return row == null ? null : ReminderDto(row);
    }

    [Authorize(EducationPermissions.AssessmentParticipate), UnitOfWork(IsDisabled = true)]
    public virtual Task<PagedResultDto<EducationAssessmentReminderDto>> GetMyRemindersAsync(GetEducationMyRemindersInput input) => MyReminderListCoreAsync(input, includeAutomatic: false);

    [Authorize(EducationPermissions.AssessmentParticipate), UnitOfWork(IsDisabled = true)]
    public virtual Task<PagedResultDto<EducationAssessmentReminderDto>> GetMyNoticesAsync(GetEducationMyRemindersInput input) => MyReminderListCoreAsync(input, includeAutomatic: true);

    private async Task<PagedResultDto<EducationAssessmentReminderDto>> MyReminderListCoreAsync(GetEducationMyRemindersInput input, bool includeAutomatic)
    {
        await StudentAsync(); EducationRules.RequireId(input.SchoolId); var take = Page(input);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: false); var now = DateTime.UtcNow;
        var notices = await Repo<EducationAssessmentReminder>().GetQueryableAsync(); var recipients = await Repo<EducationAssessmentRecipient>().GetQueryableAsync();
        var versions = await Repo<EducationAssessmentTaskRevision>().GetQueryableAsync(); var tasks = await Repo<EducationAssessmentTask>().GetQueryableAsync();
        var releases = await Repo<EducationAssessmentResultRelease>().GetQueryableAsync();
        var members = await Repo<SchoolMembership>().GetQueryableAsync(); var enrollments = await Repo<StudentEnrollment>().GetQueryableAsync(); var terms = await Repo<AcademicTerm>().GetQueryableAsync();
        var schools = await Repo<School>().GetQueryableAsync(); var classes = await Repo<SchoolClass>().GetQueryableAsync();
        var query = from n in notices join r in recipients on n.RecipientId equals r.Id join v in versions on r.TaskRevisionId equals v.Id
            join t in tasks on r.TaskId equals t.Id join m in members on r.SchoolMembershipId equals m.Id join e in enrollments on r.StudentEnrollmentId equals e.Id
            join term in terms on v.AcademicTermId equals term.Id join school in schools on r.SchoolId equals school.Id join schoolClass in classes on r.SchoolClassId equals schoolClass.Id
            where n.TenantId == CurrentTenant.Id && n.StudentUserId == CurrentUser.Id && n.SchoolId == input.SchoolId && n.AvailableAtUtc <= now && t.PublishedRevisionId == v.Id &&
                (n.Kind != EducationAssessmentReminderKind.ResultReleased || !releases.Any(next => next.PreviousReleaseId == n.ResultReleaseId && next.ReleasedAtUtc <= n.AvailableAtUtc)) &&
                m.Role == SchoolMemberRole.Student && m.EffectiveFromUtc <= now && (!m.EffectiveToUtc.HasValue || now < m.EffectiveToUtc) &&
                e.EffectiveFromUtc <= now && (!e.EffectiveToUtc.HasValue || now < e.EffectiveToUtc) && term.StartsAtUtc <= now && now < term.EndsAtUtc
            select n;
        if (!includeAutomatic) query = query.Where(x => x.Kind == EducationAssessmentReminderKind.Manual);
        var count = await AsyncExecuter.LongCountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.AvailableAtUtc).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take));
        var items = new List<EducationAssessmentReminderDto>();
        foreach (var row in rows) { var recipient = await StudentRecipientAsync(row.RecipientId); await StudentRevisionAsync(recipient); items.Add(await InboxReminderDtoAsync(row)); }
        await uow.CompleteAsync(); return new(count, items);
    }

    [Authorize(EducationPermissions.AssessmentParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentReminderDto> ReadMyReminderAsync(Guid id, Guid schoolId)
    {
        await StudentAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var row = await ReferenceAsync<EducationAssessmentReminder>(id) ?? throw new AbpAuthorizationException();
        if (row.StudentUserId != CurrentUser.Id || row.TenantId != CurrentTenant.Id || row.SchoolId != schoolId) throw new AbpAuthorizationException();
        await EnsureReminderAvailableAsync(row);
        var recipient = await StudentRecipientAsync(row.RecipientId); await StudentRevisionAsync(recipient, forUpdate: true);
        using var tracking = Repo<EducationAssessmentReminder>().EnableTracking(); row = await Repo<EducationAssessmentReminder>().GetAsync(id);
        if (!row.ReadAtUtc.HasValue) { row.MarkRead(DateTime.UtcNow); await Repo<EducationAssessmentReminder>().UpdateManyAsync([row], autoSave: true); }
        var result = await InboxReminderDtoAsync(row); await uow.CompleteAsync(); return result;
    }
}
