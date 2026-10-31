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
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationStudyTaskAppService
{
    private async Task<(EducationStudyRecipient Recipient, EducationStudyTask Task, EducationStudyTaskRevision Publication)> RecipientAsync(Guid id, bool student, bool acquireBarrier = true)
    {
        if (student) await StudentAsync(); else await TeacherAsync();
        var row = await RefAsync<EducationStudyRecipient>(id) ?? throw new AbpAuthorizationException();
        if (row.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        EducationStudyTask task;
        if (student) {
            await StudentAsync();
            await Access.EnsureStudentAsync(row.SchoolId, row.AcademicTermId, row.SchoolClassId, row.SchoolMembershipId, row.StudentEnrollmentId, row.StudentUserId, forUpdate: acquireBarrier);
            task = await RefAsync<EducationStudyTask>(row.TaskId) ?? throw new AbpAuthorizationException();
        } else {
            task = await OwnAsync(row.TaskId);
            using var tracking = Repo<IdentityUser>().EnableTracking(); var account = await Repo<IdentityUser>().FindAsync(row.StudentUserId);
            await CurrentRecipientRelationAsync(row);
            if (account == null) throw new AbpAuthorizationException();
            await Repo<IdentityUser>().UpdateManyAsync([account], autoSave: true); await CurrentRecipientRelationAsync(row);
        }
        row = await RefAsync<EducationStudyRecipient>(id) ?? throw Invalid();
        var publication = await RefAsync<EducationStudyTaskRevision>(row.TaskRevisionId) ?? throw Invalid();
        if (task.TenantId != row.TenantId || task.OwnerUserId != row.OwnerUserId || task.SchoolId != row.SchoolId || task.AcademicTermId != row.AcademicTermId ||
            task.SchoolClassId != row.SchoolClassId || publication.TaskId != task.Id || publication.TenantId != row.TenantId || publication.OwnerUserId != task.OwnerUserId ||
            publication.Kind != EducationStudyTaskRevisionKind.Published) throw Invalid();
        return (row, task, publication);
    }
    private async Task CurrentRecipientRelationAsync(EducationStudyRecipient row)
    {
        var member = await RefAsync<SchoolMembership>(row.SchoolMembershipId); var enrollment = await RefAsync<StudentEnrollment>(row.StudentEnrollmentId);
        var account = await RefAsync<IdentityUser>(row.StudentUserId); var now = DateTime.UtcNow;
        if (member == null || enrollment == null || account == null || member.TenantId != row.TenantId || enrollment.TenantId != row.TenantId || account.TenantId != row.TenantId || !account.IsActive ||
            member.SchoolId != row.SchoolId || member.UserId != row.StudentUserId || member.Role != SchoolMemberRole.Student || !member.IsEffectiveAt(now) ||
            enrollment.SchoolId != row.SchoolId || enrollment.SchoolClassId != row.SchoolClassId || enrollment.StudentUserId != row.StudentUserId || !enrollment.IsEffectiveAt(now)) throw new AbpAuthorizationException();
    }
    private async Task<EducationStudyEvent?> StudentHeadAsync(EducationStudyRecipient row)
    {
        if (row.CurrentStudentRevisionId is not Guid id) return null;
        var value = await RefAsync<EducationStudyEvent>(id) ?? throw Invalid();
        if (value.RecipientId != row.Id || value.TenantId != row.TenantId || value.StudentUserId != row.StudentUserId || value.OwnerUserId != row.OwnerUserId ||
            value.Action == EducationTeachingActivityAction.TeacherFollowUp || value.StudentRevisionNumber != row.StudentRevisionNumber) throw Invalid(); return value;
    }
    private static EducationStudentStudyEventDto StudentEvent(EducationStudyEvent row)
    {
        if (row.Action == EducationTeachingActivityAction.TeacherFollowUp || row.StudentRevisionNumber == null) throw new AbpAuthorizationException();
        return new() { Id = row.Id, RecipientId = row.RecipientId, StudentRevisionNumber = row.StudentRevisionNumber.Value, Action = row.Action, Difficulty = row.Difficulty,
            SharedText = row.SharedText, ReportedMinutes = row.ReportedMinutes, RecordedAtUtc = row.RecordedAtUtc, RequestFingerprint = row.RequestFingerprint };
    }
    private static EducationTeacherStudyEventDto TeacherEvent(EducationStudyEvent row)
    {
        if (row.Action != EducationTeachingActivityAction.TeacherFollowUp) throw new AbpAuthorizationException();
        return new() { Id = row.Id, RecipientId = row.RecipientId, ReplyToStudentRevisionId = row.ReplyToStudentRevisionId, InternalTeacherNote = row.InternalTeacherNote!,
            RecordedAtUtc = row.RecordedAtUtc, RequestFingerprint = row.RequestFingerprint };
    }
    private async Task<bool> MaterialsAvailableAsync(EducationStudyTask task, EducationStudyTaskRevision publication)
    { foreach (var material in publication.Content()!.Materials) if (!await MaterialDelivery.AvailableAsync(Authority(task), material)) return false; return true; }
    private async Task<EducationStudyTaskAvailability> AvailabilityAsync(EducationStudyTask task, EducationStudyTaskRevision publication)
    {
        if (task.PublishedRevisionId == null) return EducationStudyTaskAvailability.Withdrawn;
        if (task.PublishedRevisionId != publication.Id) return EducationStudyTaskAvailability.Superseded;
        try { await Access.EnsureRecordedTeacherRelationAsync(task.Authority(), task.AcademicTermId, task.SchoolClassId); }
        catch (AbpAuthorizationException) { return EducationStudyTaskAvailability.Unavailable; }
        var now = DateTime.UtcNow;
        if (now < publication.StartsAtUtc) return EducationStudyTaskAvailability.Waiting;
        if (!await MaterialsAvailableAsync(task, publication)) return EducationStudyTaskAvailability.Unavailable;
        return now >= publication.EndsAtUtc ? EducationStudyTaskAvailability.Overdue : EducationStudyTaskAvailability.Available;
    }
    private async Task<EducationMyStudyTaskDto> ViewAsync(EducationStudyRecipient row, EducationStudyTask task, EducationStudyTaskRevision publication, bool body)
    {
        var status = await AvailabilityAsync(task, publication); var current = await StudentHeadAsync(row); var scope = publication.Scope(); var content = publication.Content()!;
        EducationStudyEvent? follow = row.CurrentFollowUpRevisionId is Guid fid ? await RefAsync<EducationStudyEvent>(fid) ?? throw Invalid() : null;
        if (follow != null && (follow.RecipientId != row.Id || follow.TenantId != row.TenantId || follow.Action != EducationTeachingActivityAction.TeacherFollowUp)) throw Invalid();
        var canRead = status is EducationStudyTaskAvailability.Available or EducationStudyTaskAvailability.Overdue;
        if (body && canRead && row.FirstProvidedAtUtc == null) {
            using var tracking = Repo<EducationStudyRecipient>().EnableTracking(); row = await Repo<EducationStudyRecipient>().GetAsync(row.Id);
            row.MarkProvided(DateTime.UtcNow); await Repo<EducationStudyRecipient>().UpdateManyAsync([row], autoSave: true);
        }
        string? reason = null;
        if (status == EducationStudyTaskAvailability.Superseded) reason = (await RefAsync<EducationStudyTaskRevision>(task.PublishedRevisionId!.Value))?.PublicReason;
        if (status == EducationStudyTaskAvailability.Withdrawn) {
            var query = (await Repo<EducationStudyTaskRevision>().GetQueryableAsync()).Where(x => x.TenantId == task.TenantId && x.TaskId == task.Id && x.Kind == EducationStudyTaskRevisionKind.Withdrawn);
            reason = (await AsyncExecuter.FirstOrDefaultAsync(query.OrderByDescending(x => x.RevisionNumber)))?.PublicReason;
        }
        return new() { Id = row.Id, SchoolId = row.SchoolId, TaskId = task.Id, PublicationId = publication.Id, VersionNumber = publication.RevisionNumber, Title = publication.Title,
            TeacherName = publication.TeacherName, ClassName = publication.ClassName, SubjectName = scope.SubjectName, TextbookTitle = scope.TextbookTitle,
            StartsAtUtc = publication.StartsAtUtc, EndsAtUtc = publication.EndsAtUtc, ServerNowUtc = DateTime.UtcNow, EstimatedMinutes = content.EstimatedMinutes,
            Availability = status, MaterialsAvailable = canRead, Instructions = body && canRead ? content.Instructions : null, PublicReason = reason,
            Materials = body && canRead ? content.Materials.Select(x => new EducationStudyMaterialDisplay(x.Id, x.Title)).ToList() : [],
            State = current?.Action switch { EducationTeachingActivityAction.Started => EducationTeachingActivityState.InProgress,
                EducationTeachingActivityAction.Completed => EducationTeachingActivityState.ReportedComplete, EducationTeachingActivityAction.Difficulty => EducationTeachingActivityState.Blocked,
                _ => EducationTeachingActivityState.NotStarted }, CurrentStudentRevisionId = row.CurrentStudentRevisionId, CurrentStudent = current == null ? null : StudentEvent(current),
            FirstProvidedAtUtc = row.FirstProvidedAtUtc, TeacherHandledAtUtc = current?.Action == EducationTeachingActivityAction.Difficulty && follow?.ReplyToStudentRevisionId == current.Id ? follow.RecordedAtUtc : null };
    }
    private async Task<EducationStudyEvent?> EventReceiptAsync(EducationStudyRecipient recipient, Guid operationId, bool student, string? fingerprint = null)
    {
        var row = await RefAsync<EducationStudyEvent>(operationId); if (row == null) return null;
        if (row.TenantId != recipient.TenantId || row.RecipientId != recipient.Id || row.StudentUserId != recipient.StudentUserId || row.OwnerUserId != recipient.OwnerUserId ||
            student == (row.Action == EducationTeachingActivityAction.TeacherFollowUp)) throw new AbpAuthorizationException();
        if (fingerprint != null && row.RequestFingerprint != fingerprint) throw new BusinessException(EducationStudyTaskErrors.OperationConflict); return row;
    }
    private async Task AppendEventAsync(EducationStudyRecipient root, EducationStudyEvent value)
    { root.Advance(value); await Repo<EducationStudyEvent>().InsertAsync(value, autoSave: true); await Repo<EducationStudyRecipient>().UpdateManyAsync([root], autoSave: true); }
    [Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationMyStudyTaskDto> OpenMyAsync(Guid recipientId) => OpenRecipientAsync(recipientId, null);
    [Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationMyStudyTaskDto> OpenMyForSchoolAsync(Guid recipientId, Guid schoolId, Guid? expectedPublicationId = null) => OpenRecipientAsync(recipientId, EducationRules.RequireId(schoolId), expectedPublicationId);
    private async Task<EducationMyStudyTaskDto> OpenRecipientAsync(Guid recipientId, Guid? schoolId, Guid? expectedPublicationId = null)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (row, task, publication) = await RecipientAsync(recipientId, true);
        if (schoolId != null && row.SchoolId != schoolId) throw new AbpAuthorizationException();
        if (expectedPublicationId.HasValue && publication.Id != expectedPublicationId) throw new BusinessException(EducationNotebookSourceErrors.Unavailable);
        var result = await ViewAsync(row, task, publication, true); await uow.CompleteAsync(); return result;
    }
    [RemoteService(false), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationMyStudyTaskDto> GetMyWorkloadMetadataAsync(Guid recipientId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (row, task, publication) = await RecipientAsync(recipientId, true); var result = await ViewAsync(row, task, publication, false);
        if (result.Availability == EducationStudyTaskAvailability.Waiting && !await MaterialsAvailableAsync(task, publication)) result.Availability = EducationStudyTaskAvailability.Unavailable;
        await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationMyStudyTaskDto>> GetMyListAsync(PagedResultRequestDto input, Guid? schoolId = null)
    {
        await StudentAsync(); var take = Page(input); List<EducationStudyRecipient> rows;
        using (var listing = Uow.Begin(requiresNew: true, isTransactional: false)) {
            rows = await AsyncExecuter.ToListAsync((await Repo<EducationStudyRecipient>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.StudentUserId == CurrentUser.Id && (schoolId == null || x.SchoolId == schoolId))
                .OrderByDescending(x => x.AssignedAtUtc).ThenBy(x => x.Id).Take(1001)); await listing.CompleteAsync();
        }
        if (rows.Count > 1000) throw new BusinessException(EducationLearningReportErrors.Capacity); var result = new List<EducationMyStudyTaskDto>();
        foreach (var candidate in rows) {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            try { var (row, task, publication) = await RecipientAsync(candidate.Id, true); result.Add(await ViewAsync(row, task, publication, false)); }
            catch (AbpAuthorizationException) { continue; } await uow.CompleteAsync();
        }
        return new(result.Count, result.Skip(input.SkipCount).Take(take).ToList());
    }
    [Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudentStudyEventDto> SaveMyProgressAsync(Guid recipientId, SaveEducationMyActivityDto input)
    {
        await StudentAsync(); EducationRules.RequireId(input.OperationId); var fp = EducationStudyTaskFingerprint.Progress(recipientId, input);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (row, task, publication) = await RecipientAsync(recipientId, true);
        var prior = await EventReceiptAsync(row, input.OperationId, true, fp); if (prior != null) { await uow.CompleteAsync(); return StudentEvent(prior); }
        if (input.Action == EducationTeachingActivityAction.TeacherFollowUp) throw Invalid();
        var status = await AvailabilityAsync(task, publication);
        if (input.Action == EducationTeachingActivityAction.Difficulty) {
            if (task.PublishedRevisionId != publication.Id || row.FirstProvidedAtUtc == null) throw new BusinessException(EducationStudyTaskErrors.Closed);
        } else if (status != EducationStudyTaskAvailability.Available) throw new BusinessException(EducationStudyTaskErrors.Closed);
        using var tracking = Repo<EducationStudyRecipient>().EnableTracking(); var root = await Repo<EducationStudyRecipient>().GetAsync(row.Id);
        if (root.CurrentStudentRevisionId != input.ExpectedStudentRevisionId) throw new AbpDbConcurrencyException();
        var now = DateTime.UtcNow;
        if (input.Action != EducationTeachingActivityAction.Difficulty && (now < publication.StartsAtUtc || now >= publication.EndsAtUtc)) throw new BusinessException(EducationStudyTaskErrors.Closed);
        var value = new EducationStudyEvent(input.OperationId, root, await StudentHeadAsync(root), input.Action, input.Difficulty, input.SharedText, input.ReportedMinutes,
            input.ConfirmSharingWithTeacher, input.ConfirmActivityOnly, null, null, fp, now);
        await AppendEventAsync(root, value); await uow.CompleteAsync(); return StudentEvent(value);
    }
    [Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudentStudyEventDto?> GetMyOperationAsync(Guid recipientId, Guid operationId)
    { using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (row, _, _) = await RecipientAsync(recipientId, true); var value = await EventReceiptAsync(row, operationId, true); await uow.CompleteAsync(); return value == null ? null : StudentEvent(value); }
    [Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationStudentStudyEventDto>> GetMyHistoryAsync(Guid recipientId, Guid? expectedStudentRevisionId, PagedResultRequestDto input)
    {
        var take = Page(input); using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (row, _, _) = await RecipientAsync(recipientId, true);
        if (row.CurrentStudentRevisionId != expectedStudentRevisionId) throw new AbpDbConcurrencyException();
        var query = (await Repo<EducationStudyEvent>().GetQueryableAsync()).Where(x => x.RecipientId == row.Id && x.TenantId == CurrentTenant.Id && x.Action != EducationTeachingActivityAction.TeacherFollowUp);
        var count = await AsyncExecuter.CountAsync(query); var values = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.StudentRevisionNumber).Skip(input.SkipCount).Take(take));
        await uow.CompleteAsync(); return new(count, values.Select(StudentEvent).ToList());
    }
    [Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationTeacherStudyEventDto> SaveFollowUpAsync(Guid recipientId, SaveEducationStudyFollowUpDto input)
    {
        EducationRules.RequireId(input.OperationId); var fp = EducationStudyTaskFingerprint.FollowUp(recipientId, input);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (row, _, _) = await RecipientAsync(recipientId, false);
        var prior = await EventReceiptAsync(row, input.OperationId, false, fp); if (prior != null) { await uow.CompleteAsync(); return TeacherEvent(prior); }
        using var tracking = Repo<EducationStudyRecipient>().EnableTracking(); var root = await Repo<EducationStudyRecipient>().GetAsync(row.Id);
        if (root.CurrentRevisionId != input.ExpectedRevisionId || root.ConcurrencyStamp != input.ConcurrencyStamp || root.CurrentStudentRevisionId != input.ExpectedStudentRevisionId) throw new AbpDbConcurrencyException();
        var value = new EducationStudyEvent(input.OperationId, root, await StudentHeadAsync(root), EducationTeachingActivityAction.TeacherFollowUp, null, null, null, false,
            input.ConfirmPrivateHandling, input.InternalTeacherNote, input.ExpectedStudentRevisionId, fp, DateTime.UtcNow);
        await AppendEventAsync(root, value); await uow.CompleteAsync(); return TeacherEvent(value);
    }
    [Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationTeacherStudyEventDto?> GetFollowUpOperationAsync(Guid recipientId, Guid operationId)
    { using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (row, _, _) = await RecipientAsync(recipientId, false); var value = await EventReceiptAsync(row, operationId, false); await uow.CompleteAsync(); return value == null ? null : TeacherEvent(value); }
    [Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudyProgressDto> GetProgressAsync(Guid taskId, Guid? publicationId, PagedResultRequestDto input)
    {
        var take = Page(input); using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var task = await OwnAsync(taskId);
        var selected = publicationId ?? task.PublishedRevisionId ?? task.LastPublishedRevisionId ?? throw Invalid();
        var publications = await RowsAsync<EducationStudyTaskRevision>(x => x.TaskId == task.Id && x.TenantId == CurrentTenant.Id && x.Kind == EducationStudyTaskRevisionKind.Published);
        var publication = publications.SingleOrDefault(x => x.Id == selected) ?? throw new AbpAuthorizationException();
        var rows = await RowsAsync<EducationStudyRecipient>(x => x.TaskRevisionId == publication.Id && x.TenantId == CurrentTenant.Id);
        var visible = new List<EducationStudyRecipient>();
        foreach (var row in rows) { try { await CurrentRecipientRelationAsync(row); visible.Add(row); } catch (AbpAuthorizationException) { } }
        var result = new EducationStudyProgressDto { PublicationId = selected, IsCurrent = task.PublishedRevisionId == selected, AssignedCount = rows.Count, VisibleCount = visible.Count,
            Publications = publications.OrderByDescending(x => x.RevisionNumber).Select(x => new EducationActivityVersionDto { Id = x.Id, RevisionNumber = x.RevisionNumber, RecordedAtUtc = x.RecordedAtUtc }).ToList() };
        foreach (var row in visible.OrderBy(x => x.StudentName).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take)) {
            var (current, _, _) = await RecipientAsync(row.Id, false); var view = await ViewAsync(current, task, publication, false);
            result.Students.Add(new() { Id = view.Id, SchoolId = view.SchoolId, TaskId = task.Id, PublicationId = selected, Title = view.Title, VersionNumber = view.VersionNumber, StudentName = current.StudentName,
                ClassName = view.ClassName, SubjectName = view.SubjectName, TeacherName = view.TeacherName, TextbookTitle = view.TextbookTitle, Availability = view.Availability,
                StartsAtUtc = view.StartsAtUtc, EndsAtUtc = view.EndsAtUtc, EstimatedMinutes = view.EstimatedMinutes, ServerNowUtc = view.ServerNowUtc, MaterialsAvailable = view.MaterialsAvailable,
                State = view.State, CurrentStudentRevisionId = view.CurrentStudentRevisionId, CurrentStudent = view.CurrentStudent, FirstProvidedAtUtc = view.FirstProvidedAtUtc,
                TeacherHandledAtUtc = view.TeacherHandledAtUtc, CurrentRevisionId = current.CurrentRevisionId, ConcurrencyStamp = current.ConcurrencyStamp,
                CurrentFollowUp = current.CurrentFollowUpRevisionId is Guid id ? TeacherEvent(await RefAsync<EducationStudyEvent>(id) ?? throw Invalid()) : null });
        }
        await uow.CompleteAsync(); return result;
    }
}
