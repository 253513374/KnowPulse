using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Exam;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

[Authorize]
public partial class EducationFeedbackAppService : KnowPulseAppService, IEducationFeedbackAppService
{
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private IUnitOfWorkManager Uow => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();
    private EducationAssessmentAccessGuard Access => LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAccessGuard>();
    private async Task<T?> RefAsync<T>(Guid id) where T : class, IEntity<Guid>
    { using var tracking = Repo<T>().DisableTracking(); return await Repo<T>().FindAsync(id); }
    private async Task<List<T>> RowsAsync<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity<Guid>
    { using var tracking = Repo<T>().DisableTracking(); return await Repo<T>().GetListAsync(predicate); }
    private async Task AuthorizeAsync(bool teacher)
    { await CheckMvpPermissionAsync(teacher ? EducationPermissions.FeedbackManage : EducationPermissions.FeedbackView); await Access.CurrentAccountAsync(); }
    private static string Hash(object input) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));
    private static BusinessException Invalid() => new(EducationFeedbackErrorCodes.Invalid);
    private static BusinessException Conflict() => new(EducationFeedbackErrorCodes.OperationConflict);
    private static int Page(PagedResultRequestDto input)
    { if (input.SkipCount < 0 || input.MaxResultCount is < 1 or > 100) throw Invalid(); return input.MaxResultCount; }
    private static string Name(IdentityUser account) => string.IsNullOrWhiteSpace(account.Name + account.Surname) ? account.UserName : (account.Name + " " + account.Surname).Trim();
    private static EducationFeedbackRecipientInput RecipientInput(EducationStudentFeedback item) => new()
    { TeachingAssignmentId = item.TeachingAssignmentId, StudentEnrollmentId = item.StudentEnrollmentId, SchoolMembershipId = item.SchoolMembershipId };

    private async Task<EducationFeedbackRecipient> TargetAsync(EducationFeedbackRecipientInput input, bool write = false, Guid? expectedSchoolId = null)
    {
        var teaching = await Access.EnsureTeachingAssignmentAsync(input.TeachingAssignmentId, write);
        if (expectedSchoolId.HasValue && teaching.SchoolId != expectedSchoolId) throw new AbpAuthorizationException();
        var enrollment = await RefAsync<StudentEnrollment>(input.StudentEnrollmentId) ?? throw new AbpAuthorizationException();
        var member = await RefAsync<SchoolMembership>(input.SchoolMembershipId) ?? throw new AbpAuthorizationException();
        var account = await RefAsync<IdentityUser>(enrollment.StudentUserId) ?? throw new AbpAuthorizationException();
        var now = DateTime.UtcNow;
        if (enrollment.TenantId != CurrentTenant.Id || member.TenantId != CurrentTenant.Id || account.TenantId != CurrentTenant.Id || !account.IsActive ||
            enrollment.SchoolId != teaching.SchoolId || enrollment.SchoolClassId != teaching.SchoolClassId || !enrollment.IsEffectiveAt(now) ||
            member.SchoolId != teaching.SchoolId || member.UserId != enrollment.StudentUserId || member.Role != SchoolMemberRole.Student || !member.IsEffectiveAt(now))
            throw new AbpAuthorizationException();
        if (write)
        {
            using var tracking = Repo<IdentityUser>().EnableTracking();
            var student = await Repo<IdentityUser>().GetAsync(account.Id);
            if (!student.IsActive || student.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
            await Repo<IdentityUser>().UpdateManyAsync([student], autoSave: true);
            return await TargetAsync(input, expectedSchoolId: expectedSchoolId);
        }
        var teacher = await RefAsync<IdentityUser>(teaching.TeacherUserId) ?? throw new AbpAuthorizationException();
        var schoolClass = await RefAsync<SchoolClass>(teaching.SchoolClassId) ?? throw Invalid();
        var subject = await RefAsync<EducationSubject>(teaching.EducationSubjectId) ?? throw Invalid();
        if (subject.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        return new(teaching, enrollment, member, Name(teacher), Name(account), account.UserName, schoolClass.Name, subject.Name);
    }

    private async Task<EducationStudentFeedback> TeacherFeedbackAsync(Guid id, bool write = false)
    {
        await AuthorizeAsync(true);
        var item = await RefAsync<EducationStudentFeedback>(id) ?? throw new AbpAuthorizationException();
        if (item.OwnerUserId != CurrentUser.Id || item.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        await TargetAsync(RecipientInput(item), write);
        return item;
    }
    private static EducationFeedbackRevisionDto RevisionDto(EducationStudentFeedbackRevision revision) => new()
    { Id = revision.Id, FeedbackId = revision.FeedbackId, RevisionNumber = revision.RevisionNumber, BasedOnRevisionId = revision.BasedOnRevisionId,
      DraftRevisionId = revision.DraftRevisionId, PreviousPublishedRevisionId = revision.PreviousPublishedRevisionId, Kind = revision.Kind,
      Content = revision.Content, Reason = revision.Reason, RequestFingerprint = revision.RequestFingerprint, RecordedAtUtc = revision.RecordedAtUtc };
    private static EducationTeacherObservationDto ObservationDto(EducationTeacherObservation observation) => new()
    { Id = observation.Id, SchoolId = observation.SchoolId, TeachingAssignmentId = observation.TeachingAssignmentId,
      StudentEnrollmentId = observation.StudentEnrollmentId, SchoolMembershipId = observation.SchoolMembershipId,
      Text = observation.Text, ObservedAtUtc = observation.ObservedAtUtc, RequestFingerprint = observation.RequestFingerprint };

    [Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<List<EducationFeedbackTeachingDto>> GetTeachingListAsync()
    {
        await AuthorizeAsync(true); var now = DateTime.UtcNow; var result = new List<EducationFeedbackTeachingDto>();
        var rows = await RowsAsync<TeachingAssignment>(x => x.TeacherUserId == CurrentUser.Id && x.TenantId == CurrentTenant.Id &&
            x.EffectiveFromUtc <= now && (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc.Value));
        foreach (var row in rows.OrderBy(x => x.Id))
        {
            try { await Access.EnsureTeachingAssignmentAsync(row.Id); }
            catch (AbpAuthorizationException) { continue; }
            result.Add(new() { Id = row.Id, SchoolName = (await RefAsync<School>(row.SchoolId))!.Name,
                ClassName = (await RefAsync<SchoolClass>(row.SchoolClassId))!.Name, SubjectName = (await RefAsync<EducationSubject>(row.EducationSubjectId))!.Name });
        }
        return result;
    }

    [Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<PagedResultDto<EducationFeedbackTargetDto>> GetStudentsAsync(Guid teachingAssignmentId, PagedResultRequestDto input)
    {
        await AuthorizeAsync(true); var take = Page(input); var teaching = await Access.EnsureTeachingAssignmentAsync(teachingAssignmentId); var now = DateTime.UtcNow;
        var enrollments = await RowsAsync<StudentEnrollment>(x => x.SchoolId == teaching.SchoolId && x.SchoolClassId == teaching.SchoolClassId && x.TenantId == CurrentTenant.Id &&
            x.EffectiveFromUtc <= now && (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc.Value));
        var result = new List<EducationFeedbackTargetDto>();
        foreach (var enrollment in enrollments.OrderBy(x => x.StudentUserId))
        {
            var member = (await RowsAsync<SchoolMembership>(x => x.SchoolId == teaching.SchoolId && x.UserId == enrollment.StudentUserId &&
                x.TenantId == CurrentTenant.Id && x.Role == SchoolMemberRole.Student && x.EffectiveFromUtc <= now && (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc.Value))).SingleOrDefault();
            if (member == null) continue;
            var account = await RefAsync<IdentityUser>(enrollment.StudentUserId);
            if (account == null || account.TenantId != CurrentTenant.Id || !account.IsActive) continue;
            result.Add(new() { StudentEnrollmentId = enrollment.Id, SchoolMembershipId = member.Id, StudentUserId = enrollment.StudentUserId,
                StudentName = Name(account), AccountName = account.UserName });
        }
        return new(result.Count, result.Skip(input.SkipCount).Take(take).ToList());
    }

    [Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<EducationFeedbackTargetContextDto> GetTargetAsync(EducationFeedbackRecipientInput input)
    {
        await AuthorizeAsync(true);
        var target = await TargetAsync(input);
        return new()
        {
            SchoolId = target.Teaching.SchoolId,
            Teaching = new() { Id = target.Teaching.Id, SchoolName = (await RefAsync<School>(target.Teaching.SchoolId))!.Name, ClassName = target.ClassName, SubjectName = target.SubjectName },
            Student = new() { StudentEnrollmentId = target.Enrollment.Id, SchoolMembershipId = target.Membership.Id, StudentUserId = target.Enrollment.StudentUserId, StudentName = target.StudentName, AccountName = target.AccountName }
        };
    }

    [Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<List<EducationFeedbackAssessmentGateDto>> GetAssessmentListAsync(EducationFeedbackRecipientInput input)
    {
        await AuthorizeAsync(true); var target = await TargetAsync(input);
        var recipients = await RowsAsync<EducationAssessmentRecipient>(x => x.StudentUserId == target.Enrollment.StudentUserId &&
            x.StudentEnrollmentId == input.StudentEnrollmentId && x.SchoolMembershipId == input.SchoolMembershipId && x.TenantId == CurrentTenant.Id);
        var result = new List<EducationFeedbackAssessmentGateDto>();
        foreach (var recipient in recipients)
        {
            var task = await RefAsync<EducationAssessmentTask>(recipient.TaskId);
            var revision = await RefAsync<EducationAssessmentTaskRevision>(recipient.TaskRevisionId);
            if (task == null || revision == null || task.OwnerUserId != CurrentUser.Id || task.PublishedRevisionId != revision.Id ||
                revision.TeachingAssignmentId != input.TeachingAssignmentId || revision.TenantId != CurrentTenant.Id) continue;
            var release = task.CurrentResultReleaseId.HasValue ? await RefAsync<EducationAssessmentResultRelease>(task.CurrentResultReleaseId.Value) : null;
            result.Add(new() { TaskId = task.Id, Title = revision.Title, ResultReleaseId = release?.Id,
                IsReviewOpen = release != null && release.TaskId == task.Id && release.TenantId == CurrentTenant.Id && release.TaskRevisionId == revision.Id && release.VisibilityAt(DateTime.UtcNow) == ExamResultVisibility.FullReview });
        }
        return result.OrderBy(x => x.Title).ThenBy(x => x.TaskId).ToList();
    }

    [Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<EducationFeedbackDto?> GetMyWorkspaceAsync(Guid feedbackId)
    {
        await AuthorizeAsync(true); EducationRules.RequireId(feedbackId);
        if (await RefAsync<EducationStudentFeedback>(feedbackId) == null) return null;
        return await GetAsync(feedbackId);
    }

    [Authorize(EducationPermissions.FeedbackManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationTeacherObservationDto> CreateObservationAsync(CreateEducationTeacherObservationDto input)
        => await CreateObservationCoreAsync(input);

    [Authorize(EducationPermissions.FeedbackManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationTeacherObservationDto> CreateObservationForSchoolAsync(Guid schoolId, CreateEducationTeacherObservationDto input)
        => await CreateObservationCoreAsync(input, EducationRules.RequireId(schoolId));

    private async Task<EducationTeacherObservationDto> CreateObservationCoreAsync(CreateEducationTeacherObservationDto input, Guid? schoolId = null)
    {
        await AuthorizeAsync(true); EducationRules.RequireId(input.OperationId); var fingerprint = Hash(new { Command = "observation", input });
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var target = await TargetAsync(input, write: true, expectedSchoolId: schoolId);
        var previous = await RefAsync<EducationTeacherObservation>(input.OperationId);
        if (previous != null)
        {
            if (previous.TenantId != CurrentTenant.Id || previous.OwnerUserId != CurrentUser.Id || previous.RequestFingerprint != fingerprint) throw Conflict();
            await uow.CompleteAsync(); return ObservationDto(previous);
        }
        var observation = new EducationTeacherObservation(input.OperationId, target, input.Text, input.ObservedAtUtc, fingerprint);
        await Repo<EducationTeacherObservation>().InsertAsync(observation, autoSave: true); await uow.CompleteAsync(); return ObservationDto(observation);
    }

    [Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<PagedResultDto<EducationTeacherObservationDto>> GetObservationsAsync(EducationFeedbackRecipientInput input, PagedResultRequestDto page)
        => await ObservationsCoreAsync(input, page);

    [Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<PagedResultDto<EducationTeacherObservationDto>> GetObservationsForSchoolAsync(Guid schoolId, EducationFeedbackRecipientInput input, PagedResultRequestDto page)
        => await ObservationsCoreAsync(input, page, EducationRules.RequireId(schoolId));

    private async Task<PagedResultDto<EducationTeacherObservationDto>> ObservationsCoreAsync(EducationFeedbackRecipientInput input, PagedResultRequestDto page, Guid? schoolId = null)
    {
        await AuthorizeAsync(true); var take = Page(page); await TargetAsync(input, expectedSchoolId: schoolId);
        var query = (await Repo<EducationTeacherObservation>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.OwnerUserId == CurrentUser.Id &&
            x.TeachingAssignmentId == input.TeachingAssignmentId && x.StudentEnrollmentId == input.StudentEnrollmentId && x.SchoolMembershipId == input.SchoolMembershipId);
        var count = await AsyncExecuter.CountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.ObservedAtUtc).ThenBy(x => x.Id).Skip(page.SkipCount).Take(take));
        return new(count, rows.Select(ObservationDto).ToList());
    }

    [Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<EducationTeacherObservationDto?> GetObservationOperationAsync(Guid schoolId, Guid operationId, EducationFeedbackRecipientInput input)
    {
        await AuthorizeAsync(true); EducationRules.RequireId(schoolId); EducationRules.RequireId(operationId);
        await TargetAsync(input, expectedSchoolId: schoolId);
        var row = await RefAsync<EducationTeacherObservation>(operationId);
        if (row == null) return null;
        if (row.TenantId != CurrentTenant.Id || row.OwnerUserId != CurrentUser.Id || row.SchoolId != schoolId ||
            row.TeachingAssignmentId != input.TeachingAssignmentId || row.StudentEnrollmentId != input.StudentEnrollmentId || row.SchoolMembershipId != input.SchoolMembershipId)
            throw new AbpAuthorizationException();
        return ObservationDto(row);
    }

    private async Task<List<EducationAssessmentRecipient>> AssessmentRecipientsAsync(EducationStudentFeedback item, EducationFeedbackContent content)
    {
        var result = new List<EducationAssessmentRecipient>();
        foreach (var taskId in content.AssessmentTaskIds.OrderBy(x => x))
        {
            var task = await RefAsync<EducationAssessmentTask>(taskId) ?? throw new AbpAuthorizationException();
            var revision = task.PublishedRevisionId.HasValue ? await RefAsync<EducationAssessmentTaskRevision>(task.PublishedRevisionId.Value) : null;
            if (task.TenantId != item.TenantId || task.OwnerUserId != item.OwnerUserId || revision == null || revision.TeachingAssignmentId != item.TeachingAssignmentId ||
                revision.SchoolId != item.SchoolId || revision.SchoolClassId != item.SchoolClassId || revision.TenantId != item.TenantId) throw new AbpAuthorizationException();
            var recipient = (await RowsAsync<EducationAssessmentRecipient>(x => x.TaskId == task.Id && x.TaskRevisionId == revision.Id && x.StudentUserId == item.StudentUserId && x.TenantId == item.TenantId)).SingleOrDefault();
            if (recipient == null || recipient.StudentEnrollmentId != item.StudentEnrollmentId || recipient.SchoolMembershipId != item.SchoolMembershipId) throw new AbpAuthorizationException();
            result.Add(recipient);
        }
        if (content.SelectedObservationId.HasValue)
        {
            var observation = await RefAsync<EducationTeacherObservation>(content.SelectedObservationId.Value) ?? throw new AbpAuthorizationException();
            if (observation.OwnerUserId != item.OwnerUserId || observation.TenantId != item.TenantId || observation.StudentUserId != item.StudentUserId ||
                observation.TeachingAssignmentId != item.TeachingAssignmentId || observation.StudentEnrollmentId != item.StudentEnrollmentId) throw new AbpAuthorizationException();
        }
        return result;
    }

    private async Task<EducationFeedbackRevisionDto?> ReceiptAsync(Guid feedbackId, Guid operationId, string? fingerprint = null)
    {
        await TeacherFeedbackAsync(feedbackId);
        var receipt = await RefAsync<EducationStudentFeedbackRevision>(operationId); if (receipt == null) return null;
        if (receipt.TenantId != CurrentTenant.Id || receipt.FeedbackId != feedbackId || receipt.OwnerUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        if (fingerprint != null && receipt.RequestFingerprint != fingerprint) throw Conflict();
        return RevisionDto(receipt);
    }

    [Authorize(EducationPermissions.FeedbackManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationFeedbackRevisionDto> SaveDraftAsync(Guid feedbackId, SaveEducationFeedbackDraftDto input)
    {
        await AuthorizeAsync(true); EducationRules.RequireId(feedbackId); EducationRules.RequireId(input.OperationId);
        var fingerprint = Hash(new { Command = "feedback-draft", feedbackId, input });
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var target = await TargetAsync(input, write: true);
        using var tracking = Repo<EducationStudentFeedback>().EnableTracking();
        var item = await Repo<EducationStudentFeedback>().FindAsync(feedbackId);
        if (item == null)
        {
            if (input.ExpectedRevisionId.HasValue || !string.IsNullOrEmpty(input.ConcurrencyStamp)) throw new AbpDbConcurrencyException();
            item = new EducationStudentFeedback(feedbackId, target); await Repo<EducationStudentFeedback>().InsertAsync(item, autoSave: true);
        }
        else
        {
            if (item.TenantId != CurrentTenant.Id || item.OwnerUserId != CurrentUser.Id || item.TeachingAssignmentId != input.TeachingAssignmentId ||
                item.StudentEnrollmentId != input.StudentEnrollmentId || item.SchoolMembershipId != input.SchoolMembershipId) throw new AbpAuthorizationException();
            var previous = await RefAsync<EducationStudentFeedbackRevision>(input.OperationId);
            if (previous != null)
            { if (previous.FeedbackId != item.Id || previous.RequestFingerprint != fingerprint) throw Conflict(); await uow.CompleteAsync(); return RevisionDto(previous); }
            if (item.ConcurrencyStamp != input.ConcurrencyStamp || item.CurrentRevisionId != input.ExpectedRevisionId) throw new AbpDbConcurrencyException();
        }
        if (input.Content == null) throw Invalid(); input.Content.ToCanonicalJson();
        var recipients = await AssessmentRecipientsAsync(item, input.Content);
        var revision = new EducationStudentFeedbackRevision(input.OperationId, item, EducationFeedbackRevisionKind.Draft, input.Content, null, null, item.ConcurrencyStamp, fingerprint, DateTime.UtcNow);
        await AppendAsync(item, revision, recipients); await uow.CompleteAsync(); return RevisionDto(revision);
    }

    private async Task AppendAsync(EducationStudentFeedback item, EducationStudentFeedbackRevision revision, List<EducationAssessmentRecipient> recipients)
    {
        item.Advance(revision); await Repo<EducationStudentFeedbackRevision>().InsertAsync(revision, autoSave: true);
        if (recipients.Count > 0) await Repo<EducationFeedbackAssessment>().InsertManyAsync(recipients.Select(r => new EducationFeedbackAssessment(GuidGenerator.Create(), revision, r)), autoSave: true);
        if (revision.Kind != EducationFeedbackRevisionKind.Draft) await Repo<EducationFeedbackNotice>().InsertAsync(new(revision), autoSave: true);
        await Repo<EducationStudentFeedback>().UpdateManyAsync([item], autoSave: true);
    }

    private async Task<List<EducationFeedbackAssessmentGateDto>> GatesAsync(EducationStudentFeedbackRevision revision)
    {
        var result = new List<EducationFeedbackAssessmentGateDto>();
        var links = await RowsAsync<EducationFeedbackAssessment>(x => x.FeedbackRevisionId == revision.Id && x.FeedbackId == revision.FeedbackId && x.TenantId == CurrentTenant.Id);
        if (links.Count != revision.Content!.AssessmentTaskIds.Count) throw Invalid();
        foreach (var link in links.OrderBy(x => x.TaskId))
        {
            var task = await RefAsync<EducationAssessmentTask>(link.TaskId) ?? throw Invalid();
            var taskRevision = await RefAsync<EducationAssessmentTaskRevision>(link.TaskRevisionId) ?? throw Invalid();
            var release = task.CurrentResultReleaseId.HasValue ? await RefAsync<EducationAssessmentResultRelease>(task.CurrentResultReleaseId.Value) : null;
            result.Add(new() { TaskId = link.TaskId, Title = taskRevision.Title, ResultReleaseId = release?.Id,
                IsReviewOpen = task.TenantId == revision.TenantId && task.PublishedRevisionId == link.TaskRevisionId && release != null &&
                    release.TenantId == revision.TenantId && release.TaskId == task.Id && release.TaskRevisionId == link.TaskRevisionId && release.VisibilityAt(DateTime.UtcNow) == ExamResultVisibility.FullReview });
        }
        return result;
    }

    private async Task<EducationFeedbackVisibility> VisibilityAsync(EducationStudentFeedback item)
    {
        if (!item.PublishedRevisionId.HasValue) return item.LastPublishedRevisionId.HasValue ? EducationFeedbackVisibility.Withdrawn : EducationFeedbackVisibility.Draft;
        var revision = await RefAsync<EducationStudentFeedbackRevision>(item.PublishedRevisionId.Value) ?? throw Invalid();
        return (await GatesAsync(revision)).All(x => x.IsReviewOpen) ? EducationFeedbackVisibility.Open : EducationFeedbackVisibility.WaitingForReview;
    }
    private async Task<EducationFeedbackDto> MapAsync(EducationStudentFeedback item)
    {
        var current = await RefAsync<EducationStudentFeedbackRevision>(item.CurrentRevisionId!.Value) ?? throw Invalid();
        return new() { Id = item.Id, TeachingAssignmentId = item.TeachingAssignmentId, StudentEnrollmentId = item.StudentEnrollmentId,
            SchoolMembershipId = item.SchoolMembershipId, StudentUserId = item.StudentUserId, StudentName = item.StudentName, AccountName = item.AccountName,
            ClassName = item.ClassName, SubjectName = item.SubjectName, ConcurrencyStamp = item.ConcurrencyStamp, CurrentRevision = RevisionDto(current),
            Draft = item.CurrentDraftRevisionId.HasValue ? RevisionDto((await RefAsync<EducationStudentFeedbackRevision>(item.CurrentDraftRevisionId.Value))!) : null,
            Published = item.PublishedRevisionId.HasValue ? RevisionDto((await RefAsync<EducationStudentFeedbackRevision>(item.PublishedRevisionId.Value))!) : null,
            Visibility = await VisibilityAsync(item), PublishedVersionOpenedAtUtc = item.PublishedRevisionId.HasValue ? (await RefAsync<EducationFeedbackRead>(item.PublishedRevisionId.Value))?.OpenedAtUtc : null };
    }

    [Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<EducationFeedbackDto> GetAsync(Guid id) => await MapAsync(await TeacherFeedbackAsync(id));

    [Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<PagedResultDto<EducationFeedbackDto>> GetListAsync(Guid teachingAssignmentId, PagedResultRequestDto input)
    {
        await AuthorizeAsync(true); var take = Page(input); await Access.EnsureTeachingAssignmentAsync(teachingAssignmentId);
        var rows = await RowsAsync<EducationStudentFeedback>(x => x.TeachingAssignmentId == teachingAssignmentId && x.TenantId == CurrentTenant.Id && x.OwnerUserId == CurrentUser.Id);
        var allowed = new List<EducationStudentFeedback>();
        foreach (var row in rows) { try { await TargetAsync(RecipientInput(row)); allowed.Add(row); } catch (AbpAuthorizationException) { } }
        var result = new List<EducationFeedbackDto>();
        foreach (var row in allowed.OrderByDescending(x => x.CreationTime).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take)) result.Add(await MapAsync(row));
        return new(allowed.Count, result);
    }

    [Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<EducationFeedbackPublishPreviewDto> PreviewPublishAsync(Guid feedbackId)
    {
        var item = await TeacherFeedbackAsync(feedbackId);
        var draft = item.CurrentDraftRevisionId.HasValue ? await RefAsync<EducationStudentFeedbackRevision>(item.CurrentDraftRevisionId.Value) : null;
        if (draft == null || item.CurrentRevisionId != draft.Id) throw Invalid();
        await AssessmentRecipientsAsync(item, draft.Content!);
        var gates = await GatesAsync(draft);
        return new() { Feedback = await MapAsync(item), Assessments = gates, PreviewFingerprint = PreviewHash(item, draft, gates) };
    }
    private static string PreviewHash(EducationStudentFeedback item, EducationStudentFeedbackRevision draft, List<EducationFeedbackAssessmentGateDto> gates) =>
        Hash(new { item.Id, item.StudentUserId, item.StudentEnrollmentId, item.SchoolMembershipId, item.TeachingAssignmentId,
            item.ConcurrencyStamp, DraftId = draft.Id, draft.ContentJson, Assessments = gates.Select(x => new { x.TaskId, x.ResultReleaseId }) });

    [Authorize(EducationPermissions.FeedbackManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationFeedbackRevisionDto> PublishAsync(Guid feedbackId, PublishEducationFeedbackDto input)
    {
        await AuthorizeAsync(true); EducationRules.RequireId(input.OperationId); var fingerprint = Hash(new { Command = "feedback-publish", feedbackId, input });
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        await TeacherFeedbackAsync(feedbackId, write: true);
        var previous = await ReceiptAsync(feedbackId, input.OperationId, fingerprint);
        if (previous != null) { await uow.CompleteAsync(); return previous; }
        using var tracking = Repo<EducationStudentFeedback>().EnableTracking();
        var item = await Repo<EducationStudentFeedback>().GetAsync(feedbackId);
        if (item.ConcurrencyStamp != input.ConcurrencyStamp || item.CurrentRevisionId != input.ExpectedDraftRevisionId || item.CurrentDraftRevisionId != input.ExpectedDraftRevisionId)
            throw new AbpDbConcurrencyException();
        if (!input.ConfirmRecipientAndContent) throw Invalid();
        var draft = await RefAsync<EducationStudentFeedbackRevision>(input.ExpectedDraftRevisionId) ?? throw Invalid();
        var recipients = await AssessmentRecipientsAsync(item, draft.Content!); var gates = await GatesAsync(draft);
        if (input.PreviewFingerprint != PreviewHash(item, draft, gates)) throw new BusinessException(EducationFeedbackErrorCodes.PreviewChanged);
        var revision = new EducationStudentFeedbackRevision(input.OperationId, item, EducationFeedbackRevisionKind.Published, draft.Content, draft.Id, input.Reason,
            input.ConcurrencyStamp, fingerprint, DateTime.UtcNow);
        await AppendAsync(item, revision, recipients); await uow.CompleteAsync(); return RevisionDto(revision);
    }

    [Authorize(EducationPermissions.FeedbackManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationFeedbackRevisionDto> WithdrawAsync(Guid feedbackId, WithdrawEducationFeedbackDto input)
    {
        await AuthorizeAsync(true); EducationRules.RequireId(input.OperationId); var fingerprint = Hash(new { Command = "feedback-withdraw", feedbackId, input });
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        await TeacherFeedbackAsync(feedbackId, write: true);
        var previous = await ReceiptAsync(feedbackId, input.OperationId, fingerprint);
        if (previous != null) { await uow.CompleteAsync(); return previous; }
        using var tracking = Repo<EducationStudentFeedback>().EnableTracking();
        var item = await Repo<EducationStudentFeedback>().GetAsync(feedbackId);
        if (item.ConcurrencyStamp != input.ConcurrencyStamp || item.PublishedRevisionId != input.ExpectedPublishedRevisionId) throw new AbpDbConcurrencyException();
        var revision = new EducationStudentFeedbackRevision(input.OperationId, item, EducationFeedbackRevisionKind.Withdrawn, null, null, input.Reason, input.ConcurrencyStamp, fingerprint, DateTime.UtcNow);
        await AppendAsync(item, revision, []); await uow.CompleteAsync(); return RevisionDto(revision);
    }

    [Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<EducationFeedbackRevisionDto?> GetOperationAsync(Guid feedbackId, Guid operationId)
    {
        await AuthorizeAsync(true); EducationRules.RequireId(feedbackId); EducationRules.RequireId(operationId);
        // A first save may not have created a root; a missing receipt does not prove the write failed.
        if (await RefAsync<EducationStudentFeedback>(feedbackId) == null) return null;
        return await ReceiptAsync(feedbackId, operationId);
    }

    private async Task StudentAccessAsync(EducationStudentFeedback item, bool write = false)
    {
        if (item.StudentUserId != CurrentUser.Id || item.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        await Access.EnsureStudentAsync(item.SchoolId, item.AcademicTermId, item.SchoolClassId, item.SchoolMembershipId, item.StudentEnrollmentId, item.StudentUserId, write);
    }
    private async Task<List<EducationStudentFeedback>> StudentItemsAsync(Guid? schoolId = null)
    {
        var rows = await RowsAsync<EducationStudentFeedback>(x => x.StudentUserId == CurrentUser.Id && x.TenantId == CurrentTenant.Id && x.LastPublishedRevisionId != null && (!schoolId.HasValue || x.SchoolId == schoolId));
        var result = new List<EducationStudentFeedback>();
        foreach (var row in rows)
        {
            try { await StudentAccessAsync(row); } catch (AbpAuthorizationException) { continue; }
            if (await VisibilityAsync(row) is EducationFeedbackVisibility.Open or EducationFeedbackVisibility.Withdrawn) result.Add(row);
        }
        return result;
    }
    private async Task<EducationStudentFeedbackDto> StudentDtoAsync(EducationStudentFeedback item, bool includeText = false)
    {
        var visibility = await VisibilityAsync(item);
        var versionId = item.PublishedRevisionId ?? (await RowsAsync<EducationStudentFeedbackRevision>(x => x.FeedbackId == item.Id && x.Kind == EducationFeedbackRevisionKind.Withdrawn))
            .OrderByDescending(x => x.RevisionNumber).First().Id;
        var revision = await RefAsync<EducationStudentFeedbackRevision>(versionId) ?? throw Invalid();
        var dto = new EducationStudentFeedbackDto { Id = item.Id, SchoolId = item.SchoolId, SchoolName = (await RefAsync<School>(item.SchoolId))?.Name ?? string.Empty, VersionId = revision.Id, VersionNumber = revision.RevisionNumber, TeacherName = item.TeacherName,
            ClassName = item.ClassName, SubjectName = item.SubjectName, Visibility = visibility, RecordedAtUtc = revision.RecordedAtUtc,
            OpenedAtUtc = (await RefAsync<EducationFeedbackRead>(revision.Id))?.OpenedAtUtc };
        if (includeText && visibility == EducationFeedbackVisibility.Open)
        {
            var content = revision.Content!; dto.Kind = content.Kind; dto.Context = content.Context; dto.OccurredAtUtc = content.OccurredAtUtc;
            dto.PeriodEndsAtUtc = content.PeriodEndsAtUtc; dto.Text = content.Text;
            dto.AssessmentRecipientIds = (await RowsAsync<EducationFeedbackAssessment>(x => x.FeedbackRevisionId == revision.Id)).Select(x => x.RecipientId).ToList();
        }
        return dto;
    }

    [Authorize(EducationPermissions.FeedbackView)]
    public virtual async Task<PagedResultDto<EducationStudentFeedbackDto>> GetMyListAsync(PagedResultRequestDto input, Guid? schoolId = null)
    {
        await AuthorizeAsync(false); var take = Page(input); var rows = await StudentItemsAsync(schoolId); var result = new List<EducationStudentFeedbackDto>();
        foreach (var row in rows.OrderByDescending(x => x.CreationTime).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take)) result.Add(await StudentDtoAsync(row));
        return new(rows.Count, result);
    }

    [Authorize(EducationPermissions.FeedbackView), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationStudentFeedbackDto> OpenMyAsync(Guid feedbackId, Guid expectedVersionId) => OpenMyCoreAsync(feedbackId, expectedVersionId, null);

    [Authorize(EducationPermissions.FeedbackView), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationStudentFeedbackDto> OpenMyForSchoolAsync(Guid feedbackId, Guid expectedVersionId, Guid schoolId)
        => OpenMyCoreAsync(feedbackId, expectedVersionId, EducationRules.RequireId(schoolId));

    private async Task<EducationStudentFeedbackDto> OpenMyCoreAsync(Guid feedbackId, Guid expectedVersionId, Guid? schoolId)
    {
        await AuthorizeAsync(false);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var baseline = await RefAsync<EducationStudentFeedback>(feedbackId) ?? throw new AbpAuthorizationException();
        if (schoolId.HasValue && baseline.SchoolId != schoolId) throw new AbpAuthorizationException();
        await StudentAccessAsync(baseline, write: true);
        var item = await RefAsync<EducationStudentFeedback>(feedbackId) ?? throw new AbpAuthorizationException();
        if (await VisibilityAsync(item) is not (EducationFeedbackVisibility.Open or EducationFeedbackVisibility.Withdrawn)) throw new BusinessException(EducationFeedbackErrorCodes.NotOpen);
        var dto = await StudentDtoAsync(item, includeText: true);
        if (dto.VersionId != expectedVersionId) throw new AbpDbConcurrencyException();
        if (dto.Visibility == EducationFeedbackVisibility.Open)
        {
            var published = await RefAsync<EducationStudentFeedbackRevision>(dto.VersionId) ?? throw Invalid();
            var links = await RowsAsync<EducationFeedbackAssessment>(x => x.FeedbackRevisionId == published.Id && x.FeedbackId == item.Id && x.TenantId == CurrentTenant.Id);
            foreach (var link in links.OrderBy(x => x.TaskId))
            {
                var task = await RefAsync<EducationAssessmentTask>(link.TaskId) ?? throw Invalid();
                var recipient = await RefAsync<EducationAssessmentRecipient>(link.RecipientId) ?? throw Invalid();
                var release = task.CurrentResultReleaseId.HasValue ? await RefAsync<EducationAssessmentResultRelease>(task.CurrentResultReleaseId.Value) : null;
                if (release == null) throw Invalid();
                await LazyServiceProvider.LazyGetRequiredService<EducationQuestionExposureRecorder>().RecordAsync(recipient, release, published);
            }
        }
        var read = await RefAsync<EducationFeedbackRead>(dto.VersionId);
        if (read == null)
        { read = new EducationFeedbackRead((await RefAsync<EducationStudentFeedbackRevision>(dto.VersionId))!, DateTime.UtcNow); await Repo<EducationFeedbackRead>().InsertAsync(read, autoSave: true); }
        dto.OpenedAtUtc = read.OpenedAtUtc; await uow.CompleteAsync(); return dto;
    }

    [Authorize(EducationPermissions.FeedbackView)]
    public virtual async Task<PagedResultDto<EducationFeedbackNoticeDto>> GetMyNoticesAsync(PagedResultRequestDto input, Guid? schoolId = null)
    {
        await AuthorizeAsync(false); var take = Page(input); var items = await StudentItemsAsync(schoolId); var ids = items.Select(x => x.Id).ToList();
        var rows = await RowsAsync<EducationFeedbackNotice>(x => x.TenantId == CurrentTenant.Id && x.StudentUserId == CurrentUser.Id && ids.Contains(x.FeedbackId));
        var result = new List<EducationFeedbackNoticeDto>();
        foreach (var row in rows.OrderByDescending(x => x.RecordedAtUtc).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take))
        {
            var item = items.Single(x => x.Id == row.FeedbackId);
            var visibleVersion = await StudentDtoAsync(item);
            result.Add(new() { Id = row.Id, FeedbackId = row.FeedbackId, Kind = row.Kind, RecordedAtUtc = row.RecordedAtUtc,
                IsCurrentVersion = visibleVersion.VersionId == row.Id,
                MessageReadAtUtc = row.MessageReadAtUtc, CanOpen = visibleVersion.VersionId == row.Id && visibleVersion.Visibility == EducationFeedbackVisibility.Open,
                IsRead = await RefAsync<EducationFeedbackRead>(row.Id) != null });
        }
        return new(rows.Count, result);
    }

    [Authorize(EducationPermissions.FeedbackView), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationFeedbackNoticeDto> ReadMyNoticeAsync(Guid noticeId, Guid schoolId)
    {
        await AuthorizeAsync(false); EducationRules.RequireId(noticeId); EducationRules.RequireId(schoolId);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var notice = await RefAsync<EducationFeedbackNotice>(noticeId) ?? throw new AbpAuthorizationException();
        if (notice.StudentUserId != CurrentUser.Id || notice.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        var item = await RefAsync<EducationStudentFeedback>(notice.FeedbackId) ?? throw new AbpAuthorizationException();
        if (item.SchoolId != schoolId) throw new AbpAuthorizationException();
        await StudentAccessAsync(item, write: true);
        item = await RefAsync<EducationStudentFeedback>(notice.FeedbackId) ?? throw new AbpAuthorizationException();
        var visible = await StudentDtoAsync(item);
        if (visible.Visibility is not (EducationFeedbackVisibility.Open or EducationFeedbackVisibility.Withdrawn)) throw new BusinessException(EducationFeedbackErrorCodes.NotOpen);
        using var tracking = Repo<EducationFeedbackNotice>().EnableTracking(); notice = await Repo<EducationFeedbackNotice>().GetAsync(noticeId);
        if (!notice.MessageReadAtUtc.HasValue) { notice.MarkMessageRead(DateTime.UtcNow); await Repo<EducationFeedbackNotice>().UpdateManyAsync([notice], autoSave: true); }
        var result = new EducationFeedbackNoticeDto { Id = notice.Id, FeedbackId = notice.FeedbackId, Kind = notice.Kind, RecordedAtUtc = notice.RecordedAtUtc,
            IsCurrentVersion = visible.VersionId == notice.Id, CanOpen = visible.VersionId == notice.Id && visible.Visibility == EducationFeedbackVisibility.Open,
            MessageReadAtUtc = notice.MessageReadAtUtc, IsRead = await RefAsync<EducationFeedbackRead>(noticeId) != null };
        await uow.CompleteAsync(); return result;
    }
}
