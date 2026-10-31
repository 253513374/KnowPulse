using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
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
[ExposeServices(typeof(IEducationAssessmentAppService), typeof(IEducationAssessmentGradingAppService), typeof(IEducationAssessmentEvidenceAppService), typeof(IEducationCorrectionAppService), typeof(IEducationQuestionPracticeAppService), typeof(IEducationQuestionCollectionAppService), typeof(IEducationAssessmentReminderAppService), IncludeSelf = true)]
public partial class EducationAssessmentAppService : KnowPulseAppService, IEducationAssessmentAppService, IEducationAssessmentGradingAppService, IEducationAssessmentEvidenceAppService, IEducationCorrectionAppService, IEducationQuestionPracticeAppService, IEducationQuestionCollectionAppService, IEducationAssessmentReminderAppService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private IUnitOfWorkManager Uow => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();
    private EducationCurriculumAccessGuard Curriculum => LazyServiceProvider.LazyGetRequiredService<EducationCurriculumAccessGuard>();
    private EducationAssessmentAccessGuard Access => LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAccessGuard>();
    private EducationAssessmentPaperGuard Papers => LazyServiceProvider.LazyGetRequiredService<EducationAssessmentPaperGuard>();
    private async Task<T?> ReferenceAsync<T>(Guid id) where T : class, IEntity<Guid>
    {
        using var tracking = Repo<T>().DisableTracking();
        return await Repo<T>().FindAsync(id);
    }
    private async Task<List<T>> ReferencesAsync<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity<Guid>
    {
        using var tracking = Repo<T>().DisableTracking();
        return await Repo<T>().GetListAsync(predicate);
    }
    private async Task TeacherAsync()
    {
        await CheckMvpPermissionAsync(EducationPermissions.AssessmentManage);
        await Access.CurrentAccountAsync();
    }
    private async Task StudentAsync()
    {
        await CheckMvpPermissionAsync(EducationPermissions.AssessmentParticipate);
        await Access.CurrentAccountAsync();
    }
    private void Own(Guid owner, Guid? tenant)
    {
        if (owner != CurrentUser.Id || tenant != CurrentTenant.Id) throw new AbpAuthorizationException();
    }
    private async Task<EducationAssessmentTask> TaskAsync(Guid id, bool forUpdate = false)
    {
        var task = forUpdate ? await Repo<EducationAssessmentTask>().FindAsync(id) : await ReferenceAsync<EducationAssessmentTask>(id);
        if (task == null) throw new AbpAuthorizationException();
        Own(task.OwnerUserId, task.TenantId);
        return task;
    }
    private async Task<EducationAssessmentTaskRevision> RevisionAsync(EducationAssessmentTask task)
    {
        var revision = task.CurrentRevisionId.HasValue ? await ReferenceAsync<EducationAssessmentTaskRevision>(task.CurrentRevisionId.Value) : null;
        if (revision == null || revision.TaskId != task.Id || revision.TenantId != task.TenantId || revision.OwnerUserId != task.OwnerUserId ||
            revision.RevisionNumber != task.CurrentRevisionNumber) throw Invalid();
        return revision;
    }
    private static BusinessException Invalid() => new(EducationAssessmentErrorCodes.InvalidRevision);
    private static BusinessException Conflict() => new(EducationAssessmentErrorCodes.ConflictingOperation);
    private static void Stamp(EducationAssessmentTask task, string stamp, Guid expectedRevision)
    {
        if (string.IsNullOrWhiteSpace(stamp) || task.ConcurrencyStamp != stamp || task.CurrentRevisionId != expectedRevision)
            throw new AbpDbConcurrencyException();
    }
    private static string Hash(object value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));
    private static T Parse<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, Json) ?? throw Invalid(); }
        catch (JsonException) { throw Invalid(); }
    }
    private async Task<T> CompleteAsync<T>(IUnitOfWork uow, Func<Task<T>> map)
    {
        await uow.SaveChangesAsync();
        var result = await map();
        await uow.CompleteAsync();
        return result;
    }
    private static bool Recoverable(Exception error) => error is not BusinessException && error is not AbpAuthorizationException && error is not OperationCanceledException;
    private static int Page(PagedResultRequestDto input)
    {
        if (input.SkipCount < 0 || input.MaxResultCount < 1) throw Invalid();
        return Math.Min(input.MaxResultCount, 100);
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationAssessmentTaskDto> CreateDraftAsync(CreateEducationAssessmentDraftDto input)
    {
        await TeacherAsync();
        EducationRules.RequireId(input.OperationId);
        ValidateContentShape(input);
        var fingerprint = ContentFingerprint("create", input.OperationId, input);
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var existing = await FindOperationAsync(input.OperationId, input.OperationId, fingerprint);
            if (existing != null) return await CompleteAsync(uow, async () => await MapTaskAsync(await TaskAsync(existing.TaskId)));
            var scope = await Curriculum.ResolveAsync(input.Scope);
            var assignment = await AssignmentAsync(scope);
            var schoolClass = await ReferenceAsync<SchoolClass>(assignment.SchoolClassId) ?? throw Invalid();
            await ValidateWindowAsync(input.StartsAtUtc, input.EndsAtUtc, assignment);
            await Papers.EnsureAsync(input.ExamPaperId, scope, forUpdate: true);
            var now = DateTime.UtcNow;
            var task = new EducationAssessmentTask(input.OperationId, CurrentTenant.Id, CurrentUser.Id!.Value, fingerprint, now);
            await Repo<EducationAssessmentTask>().InsertAsync(task, autoSave: true);
            var revision = CreateRevision(input.OperationId, task, null, input, scope, assignment, schoolClass.Name, fingerprint, now);
            await Repo<EducationAssessmentTaskRevision>().InsertAsync(revision, autoSave: true);
            task.AdvanceDraft(revision);
            await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true);
            return await CompleteAsync(uow, () => MapTaskAsync(task));
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            var winner = await FindOperationAsync(input.OperationId, input.OperationId, fingerprint);
            if (winner == null) throw;
            return await CompleteAsync(recovery, async () => await MapTaskAsync(await TaskAsync(winner.TaskId)));
        }
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationAssessmentTaskDto> SaveDraftAsync(Guid id, SaveEducationAssessmentDraftDto input)
    {
        await TeacherAsync();
        EducationRules.RequireId(input.OperationId);
        ValidateContentShape(input);
        var fingerprint = ContentFingerprint("save", id, input, input.ExpectedDraftRevisionId, input.ConcurrencyStamp);
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var existing = await FindOperationAsync(id, input.OperationId, fingerprint);
            if (existing != null) return await CompleteAsync(uow, async () => await MapTaskAsync(await TaskAsync(id)));
            var task = await TaskAsync(id, forUpdate: true);
            Stamp(task, input.ConcurrencyStamp, input.ExpectedDraftRevisionId);
            if (task.PublishedRevisionId.HasValue) throw new BusinessException(EducationAssessmentErrorCodes.AlreadyPublished);
            var previous = await RevisionAsync(task);
            var scope = await Curriculum.ResolveAsync(input.Scope);
            var assignment = await AssignmentAsync(scope);
            var schoolClass = await ReferenceAsync<SchoolClass>(assignment.SchoolClassId) ?? throw Invalid();
            await ValidateWindowAsync(input.StartsAtUtc, input.EndsAtUtc, assignment);
            await Papers.EnsureAsync(input.ExamPaperId, scope, forUpdate: true);
            var revision = CreateRevision(input.OperationId, task, previous, input, scope, assignment, schoolClass.Name, fingerprint, DateTime.UtcNow);
            await Repo<EducationAssessmentTaskRevision>().InsertAsync(revision, autoSave: true);
            task.AdvanceDraft(revision);
            await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true);
            return await CompleteAsync(uow, () => MapTaskAsync(task));
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            if (await FindOperationAsync(id, input.OperationId, fingerprint) == null) throw;
            return await CompleteAsync(recovery, async () => await MapTaskAsync(await TaskAsync(id)));
        }
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationAssessmentTaskDto> GetAsync(Guid id)
    {
        await TeacherAsync();
        return await MapTaskAsync(await TaskAsync(id));
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<PagedResultDto<EducationAssessmentTaskDto>> GetMyListAsync(GetEducationAssessmentTaskListInput input)
    {
        await TeacherAsync();
        var take = Page(input);
        using var taskTracking = Repo<EducationAssessmentTask>().DisableTracking();
        using var revisionTracking = Repo<EducationAssessmentTaskRevision>().DisableTracking();
        var tasks = await Repo<EducationAssessmentTask>().GetQueryableAsync();
        var revisions = await Repo<EducationAssessmentTaskRevision>().GetQueryableAsync();
        var query = from task in tasks join revision in revisions on task.CurrentRevisionId equals (Guid?)revision.Id
            where task.TenantId == CurrentTenant.Id && task.OwnerUserId == CurrentUser.Id && revision.TaskId == task.Id &&
                  revision.OwnerUserId == task.OwnerUserId && revision.TenantId == task.TenantId
            select new { Task = task, Revision = revision };
        if (input.SchoolId.HasValue) query = query.Where(x => x.Revision.SchoolId == input.SchoolId);
        if (input.SchoolClassId.HasValue) query = query.Where(x => x.Revision.SchoolClassId == input.SchoolClassId);
        if (input.IsPublished.HasValue) query = query.Where(x => x.Task.PublishedRevisionId.HasValue == input.IsPublished.Value);
        if (!string.IsNullOrWhiteSpace(input.Keyword)) query = query.Where(x => x.Revision.Title.Contains(input.Keyword.Trim()));
        using var assignmentTracking = Repo<TeachingAssignment>().DisableTracking();
        using var membershipTracking = Repo<SchoolMembership>().DisableTracking();
        using var termTracking = Repo<AcademicTerm>().DisableTracking();
        using var classTracking = Repo<SchoolClass>().DisableTracking();
        using var schoolTracking = Repo<School>().DisableTracking();
        var assignments = await Repo<TeachingAssignment>().GetQueryableAsync();
        var memberships = await Repo<SchoolMembership>().GetQueryableAsync();
        var terms = await Repo<AcademicTerm>().GetQueryableAsync();
        var classes = await Repo<SchoolClass>().GetQueryableAsync();
        var schools = await Repo<School>().GetQueryableAsync();
        var now = DateTime.UtcNow;
        query = query.Where(x => !x.Revision.PublishedAtUtc.HasValue && x.Revision.SelectedStudentIdsJson == "[]" ||
            assignments.Any(a => a.Id == x.Revision.TeachingAssignmentId && a.TenantId == x.Task.TenantId && a.TeacherUserId == x.Task.OwnerUserId &&
                a.SchoolId == x.Revision.SchoolId && a.SchoolClassId == x.Revision.SchoolClassId && a.AcademicTermId == x.Revision.AcademicTermId &&
                a.EffectiveFromUtc <= now && (!a.EffectiveToUtc.HasValue || now < a.EffectiveToUtc.Value)) &&
            memberships.Any(m => m.TenantId == x.Task.TenantId && m.SchoolId == x.Revision.SchoolId && m.UserId == x.Task.OwnerUserId &&
                m.Role == SchoolMemberRole.Teacher && m.EffectiveFromUtc <= now && (!m.EffectiveToUtc.HasValue || now < m.EffectiveToUtc.Value)) &&
            terms.Any(t => t.Id == x.Revision.AcademicTermId && t.TenantId == x.Task.TenantId && t.SchoolId == x.Revision.SchoolId && t.StartsAtUtc <= now && now < t.EndsAtUtc) &&
            classes.Any(c => c.Id == x.Revision.SchoolClassId && c.TenantId == x.Task.TenantId && c.SchoolId == x.Revision.SchoolId && c.AcademicTermId == x.Revision.AcademicTermId) &&
            schools.Any(s => s.Id == x.Revision.SchoolId && s.TenantId == x.Task.TenantId));
        var count = await AsyncExecuter.CountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.Task.UpdatedAtUtc).ThenBy(x => x.Task.Id).Skip(input.SkipCount).Take(take));
        var result = new List<EducationAssessmentTaskDto>();
        foreach (var row in rows) result.Add(await MapTaskAsync(row.Task));
        return new PagedResultDto<EducationAssessmentTaskDto>(count, result);
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationAssessmentRosterPreviewDto> PreviewRecipientsAsync(Guid id)
    {
        await TeacherAsync();
        var task = await TaskAsync(id);
        if (task.PublishedRevisionId.HasValue) throw new BusinessException(EducationAssessmentErrorCodes.AlreadyPublished);
        var revision = await RevisionAsync(task);
        var roster = await RosterAsync(revision);
        return new EducationAssessmentRosterPreviewDto
        {
            TaskId = id, DraftRevisionId = revision.Id, ConcurrencyStamp = task.ConcurrencyStamp,
            SchoolClassId = revision.SchoolClassId, SchoolClassName = revision.SchoolClassName,
            RosterFingerprint = RosterFingerprint(revision, roster),
            Students = roster.Select(x => new EducationAssessmentRosterStudentDto
                { StudentUserId = x.UserId, StudentName = x.Name, AccountName = x.AccountName }).ToList()
        };
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationAssessmentTaskRevisionDto> PublishAsync(Guid id, PublishEducationAssessmentDto input)
    {
        await TeacherAsync();
        EducationRules.RequireId(input.OperationId);
        var fingerprint = Hash(new { Command = "publish", TaskId = id, input.ExpectedDraftRevisionId, input.ConcurrencyStamp, input.RosterFingerprint });
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var existing = await FindOperationAsync(id, input.OperationId, fingerprint);
            if (existing != null) return await CompleteAsync(uow, () => MapRevisionAsync(existing));
            var task = await TaskAsync(id, forUpdate: true);
            Stamp(task, input.ConcurrencyStamp, input.ExpectedDraftRevisionId);
            if (task.PublishedRevisionId.HasValue) throw new BusinessException(EducationAssessmentErrorCodes.AlreadyPublished);
            var previous = await RevisionAsync(task);
            var scope = Parse<EducationCurriculumSnapshot>(previous.CurriculumSnapshotJson);
            await ValidateWindowAsync(previous.StartsAtUtc, previous.EndsAtUtc, await AssignmentAsync(scope));
            var roster = await RosterAsync(previous);
            if (RosterFingerprint(previous, roster) != input.RosterFingerprint) throw new BusinessException(EducationAssessmentErrorCodes.RosterChanged);
            using var accountTracking = Repo<IdentityUser>().EnableTracking();
            var ids = roster.Select(x => x.UserId).ToList();
            var accounts = await Repo<IdentityUser>().GetListAsync(x => ids.Contains(x.Id) && x.TenantId == CurrentTenant.Id);
            if (accounts.Count != ids.Count || accounts.Any(x => !x.IsActive)) throw new BusinessException(EducationAssessmentErrorCodes.RosterChanged);
            // All new teaching uses acquire School before student accounts, as student start does.
            // Keep the account baselines captured above so deactivation during source validation still loses CAS.
            await Papers.EnsureAsync(previous.ExamPaperId, scope, forUpdate: true);
            await Repo<IdentityUser>().UpdateManyAsync(accounts.OrderBy(x => x.Id).ToList(), autoSave: true);
            var confirmed = await RosterAsync(previous);
            if (RosterFingerprint(previous, confirmed) != input.RosterFingerprint) throw new BusinessException(EducationAssessmentErrorCodes.RosterChanged);
            var now = DateTime.UtcNow;
            var revision = CopyPublished(input.OperationId, task, previous, fingerprint, now, input.RosterFingerprint);
            await Repo<EducationAssessmentTaskRevision>().InsertAsync(revision, autoSave: true);
            var recipients = confirmed.Select(x => new EducationAssessmentRecipient(GuidGenerator.Create(), revision,
                x.UserId, x.MembershipId, x.EnrollmentId, x.Name, x.AccountName)).ToList();
            await Repo<EducationAssessmentRecipient>().InsertManyAsync(recipients, autoSave: true);
            task.Publish(revision);
            await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true);
            await Repo<EducationAssessmentReminder>().InsertManyAsync(recipients.Select(r => EducationAssessmentReminder.Assigned(GuidGenerator.Create(), r, revision,
                Hash(new { Command = "assessment-assigned-notice", Recipient = r.Id, Publication = revision.Id }), now)).ToList(), autoSave: true);
            return await CompleteAsync(uow, () => MapRevisionAsync(revision));
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            var winner = await FindOperationAsync(id, input.OperationId, fingerprint);
            if (winner == null) throw;
            return await CompleteAsync(recovery, () => MapRevisionAsync(winner));
        }
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationAssessmentTaskRevisionDto?> GetOperationAsync(Guid id, Guid operationId)
    {
        await TeacherAsync();
        EducationRules.RequireId(operationId);
        await TaskAsync(id);
        var revision = await ReferenceAsync<EducationAssessmentTaskRevision>(operationId);
        if (revision == null) return null;
        if (revision.TaskId != id) throw new AbpAuthorizationException();
        Own(revision.OwnerUserId, revision.TenantId);
        return await MapRevisionAsync(revision);
    }

    [Authorize(EducationPermissions.AssessmentParticipate)]
    public virtual async Task<PagedResultDto<EducationStudentAssessmentTaskDto>> GetMyStudentListAsync(GetEducationStudentAssessmentListInput input)
    {
        await StudentAsync();
        var take = Page(input);
        using var recipientTracking = Repo<EducationAssessmentRecipient>().DisableTracking();
        using var taskTracking = Repo<EducationAssessmentTask>().DisableTracking();
        using var revisionTracking = Repo<EducationAssessmentTaskRevision>().DisableTracking();
        using var membershipTracking = Repo<SchoolMembership>().DisableTracking();
        using var enrollmentTracking = Repo<StudentEnrollment>().DisableTracking();
        using var termTracking = Repo<AcademicTerm>().DisableTracking();
        using var classTracking = Repo<SchoolClass>().DisableTracking();
        using var schoolTracking = Repo<School>().DisableTracking();
        var recipients = await Repo<EducationAssessmentRecipient>().GetQueryableAsync();
        var tasks = await Repo<EducationAssessmentTask>().GetQueryableAsync();
        var revisions = await Repo<EducationAssessmentTaskRevision>().GetQueryableAsync();
        var memberships = await Repo<SchoolMembership>().GetQueryableAsync();
        var enrollments = await Repo<StudentEnrollment>().GetQueryableAsync();
        var terms = await Repo<AcademicTerm>().GetQueryableAsync();
        var classes = await Repo<SchoolClass>().GetQueryableAsync();
        var schools = await Repo<School>().GetQueryableAsync();
        var now = DateTime.UtcNow;
        // Filter the exact frozen student relations in SQL before bounded paging; an active
        // school role alone never makes another recipient's assessment visible.
        var query = from recipient in recipients
            join task in tasks on recipient.TaskId equals task.Id
            join revision in revisions on recipient.TaskRevisionId equals revision.Id
            join membership in memberships on recipient.SchoolMembershipId equals membership.Id
            join enrollment in enrollments on recipient.StudentEnrollmentId equals enrollment.Id
            join term in terms on revision.AcademicTermId equals term.Id
            join schoolClass in classes on recipient.SchoolClassId equals schoolClass.Id
            join school in schools on recipient.SchoolId equals school.Id
            where recipient.TenantId == CurrentTenant.Id && recipient.StudentUserId == CurrentUser.Id &&
                  task.TenantId == recipient.TenantId && task.PublishedRevisionId == revision.Id &&
                  revision.TenantId == recipient.TenantId && revision.TaskId == task.Id && revision.OwnerUserId == task.OwnerUserId &&
                  revision.PublishedAtUtc.HasValue && revision.SchoolId == recipient.SchoolId && revision.SchoolClassId == recipient.SchoolClassId &&
                  membership.TenantId == recipient.TenantId && membership.SchoolId == recipient.SchoolId && membership.UserId == recipient.StudentUserId &&
                  membership.Role == SchoolMemberRole.Student && membership.EffectiveFromUtc <= now && (membership.EffectiveToUtc == null || now < membership.EffectiveToUtc) &&
                  enrollment.TenantId == recipient.TenantId && enrollment.SchoolId == recipient.SchoolId && enrollment.StudentUserId == recipient.StudentUserId &&
                  enrollment.SchoolClassId == recipient.SchoolClassId && enrollment.EffectiveFromUtc <= now && (enrollment.EffectiveToUtc == null || now < enrollment.EffectiveToUtc) &&
                  term.TenantId == recipient.TenantId && term.SchoolId == recipient.SchoolId && term.StartsAtUtc <= now && now < term.EndsAtUtc &&
                  schoolClass.TenantId == recipient.TenantId && schoolClass.SchoolId == recipient.SchoolId && schoolClass.AcademicTermId == term.Id &&
                  school.TenantId == recipient.TenantId
            select recipient;
        if (input.SchoolId.HasValue) query = query.Where(x => x.SchoolId == input.SchoolId);
        var count = await AsyncExecuter.CountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.CreationTime).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take));
        var result = new List<EducationStudentAssessmentTaskDto>();
        foreach (var recipient in rows) result.Add(await MapStudentTaskAsync(recipient, await StudentRevisionAsync(recipient)));
        return new PagedResultDto<EducationStudentAssessmentTaskDto>(count, result);
    }

    [Authorize(EducationPermissions.AssessmentParticipate)]
    public virtual async Task<EducationStudentAssessmentTaskDto> GetMyStudentTaskAsync(Guid recipientId)
    {
        await StudentAsync();
        var recipient = await StudentRecipientAsync(recipientId);
        return await MapStudentTaskAsync(recipient, await StudentRevisionAsync(recipient));
    }

    [Authorize(EducationPermissions.AssessmentParticipate)]
    public virtual async Task<EducationStudentAssessmentAttemptDto> StartOrResumeAsync(Guid recipientId, StartEducationAssessmentDto input)
    {
        await StudentAsync();
        EducationRules.RequireId(input.OperationId);
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            using var tracking = Repo<EducationAssessmentRecipient>().EnableTracking();
            var recipient = await StudentRecipientAsync(recipientId, forUpdate: true);
            var revision = await StudentRevisionAsync(recipient, forUpdate: true);
            using var taskTracking = Repo<EducationAssessmentTask>().EnableTracking();
            var startTask = await Repo<EducationAssessmentTask>().GetAsync(recipient.TaskId);
            await Repo<EducationAssessmentTask>().UpdateManyAsync([startTask], autoSave: true);
            if (startTask.ClosureId.HasValue) throw new BusinessException(EducationAssessmentErrorCodes.AttemptClosed);
            var priorOperation = await ReferenceAsync<EducationAssessmentAttempt>(input.OperationId);
            if (priorOperation != null && (priorOperation.RecipientId != recipient.Id || priorOperation.StudentUserId != CurrentUser.Id || priorOperation.TenantId != CurrentTenant.Id)) throw Conflict();
            if (recipient.CurrentAttemptId.HasValue)
                return await CompleteAsync(uow, async () => await MapAttemptAsync(await CurrentAttemptAsync(recipient, revision), revision));
            if (priorOperation != null || recipient.StartedAttemptCount != 0) throw Invalid();
            await LazyServiceProvider.LazyGetRequiredService<EducationTeachingFormalAssessmentGuard>().EnsureCanStartAsync(recipient.Id);
            var attempt = new EducationAssessmentAttempt(input.OperationId, revision, recipient, DateTime.UtcNow);
            await Repo<EducationAssessmentAttempt>().InsertAsync(attempt, autoSave: true);
            recipient.RecordStartedAttempt(attempt);
            await Repo<EducationAssessmentRecipient>().UpdateManyAsync([recipient], autoSave: true);
            return await CompleteAsync(uow, () => MapAttemptAsync(attempt, revision));
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            var recipient = await StudentRecipientAsync(recipientId);
            var revision = await StudentRevisionAsync(recipient);
            var operation = await ReferenceAsync<EducationAssessmentAttempt>(input.OperationId);
            if (operation != null && (operation.RecipientId != recipient.Id || operation.StudentUserId != CurrentUser.Id || operation.TenantId != CurrentTenant.Id)) throw Conflict();
            if (!recipient.CurrentAttemptId.HasValue) throw;
            // A CAS loser recovers the one committed recipient winner, not another start time.
            return await CompleteAsync(recovery, async () => await MapAttemptAsync(await CurrentAttemptAsync(recipient, revision), revision));
        }
    }

    private async Task<EducationAssessmentRecipient> StudentRecipientAsync(Guid id, bool forUpdate = false)
    {
        var recipient = forUpdate ? await Repo<EducationAssessmentRecipient>().FindAsync(id) : await ReferenceAsync<EducationAssessmentRecipient>(id);
        if (recipient == null || recipient.TenantId != CurrentTenant.Id || recipient.StudentUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        return recipient;
    }
    private async Task<EducationAssessmentTaskRevision> StudentRevisionAsync(EducationAssessmentRecipient recipient, bool forUpdate = false)
    {
        var revision = await ReferenceAsync<EducationAssessmentTaskRevision>(recipient.TaskRevisionId);
        var task = await ReferenceAsync<EducationAssessmentTask>(recipient.TaskId);
        if (revision == null || task == null || task.TenantId != CurrentTenant.Id || revision.TenantId != task.TenantId ||
            revision.TaskId != task.Id || revision.OwnerUserId != task.OwnerUserId || task.PublishedRevisionId != revision.Id ||
            !revision.PublishedAtUtc.HasValue || revision.SchoolId != recipient.SchoolId || revision.SchoolClassId != recipient.SchoolClassId) throw new AbpAuthorizationException();
        await Access.EnsureStudentAsync(revision.SchoolId, revision.AcademicTermId, revision.SchoolClassId,
            recipient.SchoolMembershipId, recipient.StudentEnrollmentId, recipient.StudentUserId, forUpdate);
        return revision;
    }
    private async Task<EducationAssessmentAttempt> CurrentAttemptAsync(EducationAssessmentRecipient recipient, EducationAssessmentTaskRevision revision)
    {
        var attempt = recipient.CurrentAttemptId.HasValue ? await ReferenceAsync<EducationAssessmentAttempt>(recipient.CurrentAttemptId.Value) : null;
        if (attempt == null || attempt.RecipientId != recipient.Id || attempt.TaskId != recipient.TaskId || attempt.TaskRevisionId != revision.Id ||
            attempt.StudentUserId != recipient.StudentUserId || attempt.TenantId != recipient.TenantId || attempt.ExamPaperId != revision.ExamPaperId ||
            attempt.AttemptNumber != recipient.StartedAttemptCount) throw Invalid();
        revision.EnsureCanStart(DateTime.UtcNow);
        attempt.EnsureCanResume(DateTime.UtcNow);
        return attempt;
    }

    private async Task<TeachingAssignment> AssignmentAsync(EducationCurriculumSnapshot scope)
    {
        var assignment = await ReferenceAsync<TeachingAssignment>(scope.TeachingAssignmentId);
        if (assignment == null || assignment.TenantId != scope.TenantId || assignment.SchoolId != scope.SchoolId ||
            assignment.TeacherUserId != scope.TeacherUserId || assignment.EducationSubjectId != scope.EducationSubjectId ||
            !assignment.IsEffectiveAt(DateTime.UtcNow)) throw new AbpAuthorizationException();
        return assignment;
    }
    private async Task ValidateWindowAsync(DateTime startsAtUtc, DateTime endsAtUtc, TeachingAssignment assignment)
    {
        EducationRules.ValidatePeriod(startsAtUtc, endsAtUtc);
        var term = await ReferenceAsync<AcademicTerm>(assignment.AcademicTermId) ?? throw Invalid();
        if (startsAtUtc < term.StartsAtUtc || endsAtUtc > term.EndsAtUtc || startsAtUtc < assignment.EffectiveFromUtc ||
            assignment.EffectiveToUtc.HasValue && endsAtUtc > assignment.EffectiveToUtc.Value || endsAtUtc <= DateTime.UtcNow)
            throw new BusinessException(EducationAssessmentErrorCodes.OutsideWindow);
    }
    private async Task<List<EducationClassStudent>> RosterAsync(EducationAssessmentTaskRevision revision)
    {
        var scope = Parse<EducationCurriculumSnapshot>(revision.CurriculumSnapshotJson);
        await Curriculum.ValidateAsync(scope);
        var assignment = await AssignmentAsync(scope);
        if (assignment.SchoolClassId != revision.SchoolClassId || assignment.AcademicTermId != revision.AcademicTermId ||
            assignment.Id != revision.TeachingAssignmentId || assignment.SchoolId != revision.SchoolId) throw Invalid();
        var selected = Parse<List<Guid>>(revision.SelectedStudentIdsJson);
        var rows = await CurrentClassRosterAsync(scope, assignment, revision.RecipientScope == EducationAssessmentRecipientScope.FullClass ? null : selected);
        if (rows.Count == 0 ||
            revision.RecipientScope == EducationAssessmentRecipientScope.SelectedStudents && rows.Count != selected.Count)
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidRecipientScope);
        return rows;
    }
    private static string RosterFingerprint(EducationAssessmentTaskRevision revision, List<EducationClassStudent> students)
        => Hash(new { revision.Id, revision.SchoolId, revision.AcademicTermId, revision.SchoolClassId, revision.TeachingAssignmentId,
            revision.RecipientScope, Selected = Parse<List<Guid>>(revision.SelectedStudentIdsJson), Students = students.OrderBy(x => x.UserId).ToArray() });
    private async Task<EducationAssessmentTaskRevision?> FindOperationAsync(Guid taskId, Guid operationId, string fingerprint)
    {
        var revision = await ReferenceAsync<EducationAssessmentTaskRevision>(operationId);
        if (revision == null) return null;
        Own(revision.OwnerUserId, revision.TenantId);
        if (revision.TaskId != taskId || revision.RequestFingerprint != fingerprint) throw Conflict();
        var task = await TaskAsync(taskId);
        if (revision.PublishedAtUtc.HasValue && task.PublishedRevisionId != revision.Id) throw Invalid();
        return revision;
    }
    private static string ContentFingerprint(string command, Guid id, EducationAssessmentDraftContentDto input, Guid? previous = null, string? stamp = null)
        => Hash(new { Command = command, TaskId = id, Previous = previous, Stamp = stamp, Title = input.Title.Trim(), Instructions = input.Instructions.Trim(),
            input.ExamPaperId, input.Scope.TeacherTextbookSelectionId, Mappings = input.Scope.TextbookKnowledgePointMappingIds.OrderBy(x => x).ToArray(),
            input.RecipientScope, Students = input.StudentUserIds.OrderBy(x => x).ToArray(), input.StartsAtUtc, input.EndsAtUtc,
            input.DurationMinutes, input.MaxAttempts, input.ResultVisibility, input.AllowWrongQuestionCollection });
    private static void ValidateContentShape(EducationAssessmentDraftContentDto input)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 256 || string.IsNullOrWhiteSpace(input.Instructions) ||
            input.Instructions.Length > 8192 || input.Scope == null || input.Scope.TextbookKnowledgePointMappingIds == null ||
            input.StudentUserIds == null || input.StudentUserIds.Count > EducationAssessmentTaskRevision.MaxRecipients)
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidDefinition);
        EducationRules.RequireId(input.ExamPaperId);
    }
    private static EducationAssessmentTaskRevision CreateRevision(Guid id, EducationAssessmentTask task, EducationAssessmentTaskRevision? previous,
        EducationAssessmentDraftContentDto input, EducationCurriculumSnapshot scope, TeachingAssignment assignment, string className, string fingerprint, DateTime now)
        => new(id, task.TenantId, task.OwnerUserId, task.Id, task.CurrentRevisionNumber + 1, previous?.Id, input.Title, input.Instructions,
            scope.SchoolId, assignment.AcademicTermId, assignment.SchoolClassId, className, assignment.Id, input.ExamPaperId,
            JsonSerializer.Serialize(scope, Json), input.RecipientScope, input.StudentUserIds, input.StartsAtUtc, input.EndsAtUtc,
            input.DurationMinutes, input.MaxAttempts, input.ResultVisibility, input.AllowWrongQuestionCollection, fingerprint, now);
    private static EducationAssessmentTaskRevision CopyPublished(Guid id, EducationAssessmentTask task, EducationAssessmentTaskRevision previous,
        string fingerprint, DateTime now, string rosterFingerprint)
        => new(id, task.TenantId, task.OwnerUserId, task.Id, task.CurrentRevisionNumber + 1, previous.Id, previous.Title, previous.Instructions,
            previous.SchoolId, previous.AcademicTermId, previous.SchoolClassId, previous.SchoolClassName, previous.TeachingAssignmentId,
            previous.ExamPaperId, previous.CurriculumSnapshotJson, previous.RecipientScope, Parse<List<Guid>>(previous.SelectedStudentIdsJson),
            previous.StartsAtUtc, previous.EndsAtUtc, previous.DurationMinutes, previous.MaxAttempts, previous.ResultVisibility,
            previous.AllowWrongQuestionCollection, fingerprint, now, now, rosterFingerprint);

    private async Task<EducationAssessmentTaskDto> MapTaskAsync(EducationAssessmentTask task) => new()
    {
        Id = task.Id, ConcurrencyStamp = task.ConcurrencyStamp, PublishedRevisionId = task.PublishedRevisionId, UpdatedAtUtc = task.UpdatedAtUtc,
        CurrentRevision = await MapRevisionAsync(await RevisionAsync(task))
    };
    private async Task<EducationAssessmentTaskRevisionDto> MapRevisionAsync(EducationAssessmentTaskRevision revision)
    {
        Own(revision.OwnerUserId, revision.TenantId);
        if (revision.PublishedAtUtc.HasValue || Parse<List<Guid>>(revision.SelectedStudentIdsJson).Count > 0)
            await Access.EnsureTeacherRelationAsync(Parse<EducationCurriculumSnapshot>(revision.CurriculumSnapshotJson), revision.AcademicTermId, revision.SchoolClassId);
        var recipients = await ReferencesAsync<EducationAssessmentRecipient>(x => x.TaskRevisionId == revision.Id && x.TenantId == revision.TenantId);
        if (revision.PublishedAtUtc.HasValue && recipients.Count == 0) throw Invalid();
        return new EducationAssessmentTaskRevisionDto
        {
            Id = revision.Id, TaskId = revision.TaskId, RevisionNumber = revision.RevisionNumber, BasedOnRevisionId = revision.BasedOnRevisionId,
            RequestFingerprint = revision.RequestFingerprint, Title = revision.Title, Instructions = revision.Instructions,
            SchoolId = revision.SchoolId, AcademicTermId = revision.AcademicTermId, SchoolClassId = revision.SchoolClassId,
            SchoolClassName = revision.SchoolClassName, TeachingAssignmentId = revision.TeachingAssignmentId, ExamPaperId = revision.ExamPaperId,
            Curriculum = Parse<EducationCurriculumSnapshot>(revision.CurriculumSnapshotJson), RecipientScope = revision.RecipientScope,
            StudentUserIds = Parse<List<Guid>>(revision.SelectedStudentIdsJson), StartsAtUtc = revision.StartsAtUtc, EndsAtUtc = revision.EndsAtUtc,
            DurationMinutes = revision.DurationMinutes, MaxAttempts = revision.MaxAttempts, ResultVisibility = revision.ResultVisibility,
            AllowWrongQuestionCollection = revision.AllowWrongQuestionCollection, RecordedAtUtc = revision.RecordedAtUtc,
            PublishedAtUtc = revision.PublishedAtUtc, RosterFingerprint = revision.RosterFingerprint,
            Recipients = recipients.OrderBy(x => x.StudentUserId).Select(x => new EducationAssessmentRecipientDto
            { Id = x.Id, TaskRevisionId = x.TaskRevisionId, StudentUserId = x.StudentUserId, StudentName = x.StudentName,
                AccountName = x.AccountName, StartedAttemptCount = x.StartedAttemptCount, CurrentAttemptId = x.CurrentAttemptId }).ToList()
        };
    }
    private async Task<EducationStudentAssessmentTaskDto> MapStudentTaskAsync(EducationAssessmentRecipient recipient, EducationAssessmentTaskRevision revision)
    {
        var paper = await ReferenceAsync<ExamPaper>(revision.ExamPaperId);
        if (paper == null || paper.TenantId != revision.TenantId) throw Invalid();
        var scope = Parse<EducationCurriculumSnapshot>(revision.CurriculumSnapshotJson);
        var now = DateTime.UtcNow;
        var attempt = recipient.CurrentAttemptId.HasValue ? await ReferenceAsync<EducationAssessmentAttempt>(recipient.CurrentAttemptId.Value) : null;
        return new EducationStudentAssessmentTaskDto
        {
            Id = recipient.Id, SchoolId = revision.SchoolId, TaskId = recipient.TaskId, TaskRevisionId = revision.Id, Title = revision.Title, Instructions = revision.Instructions,
            SchoolClassName = revision.SchoolClassName, SubjectName = scope.SubjectName, TextbookTitle = scope.TextbookTitle,
            StartsAtUtc = revision.StartsAtUtc, EndsAtUtc = revision.EndsAtUtc, DurationMinutes = revision.DurationMinutes, MaxAttempts = revision.MaxAttempts,
            ConfiguredResultVisibility = revision.ResultVisibility, CurrentResultVisibility = await ResultVisibilityAsync(recipient.TaskId),
            QuestionCount = paper.QuestionCount, CurrentAttemptId = recipient.CurrentAttemptId, ServerNowUtc = now,
            CanStart = now >= revision.StartsAtUtc && now < revision.EndsAtUtc && (!recipient.CurrentAttemptId.HasValue && await LazyServiceProvider.LazyGetRequiredService<EducationTeachingFormalAssessmentGuard>().CanStartAsync(recipient.Id) ||
                attempt != null && attempt.RecipientId == recipient.Id && attempt.StudentUserId == CurrentUser.Id &&
                !attempt.SubmissionId.HasValue && now < attempt.ExpiresAtUtc)
        };
    }
    private async Task<EducationStudentAssessmentAttemptDto> MapAttemptAsync(EducationAssessmentAttempt attempt, EducationAssessmentTaskRevision revision, bool allowClosed = false, int retry = 0)
    {
        if (!allowClosed) attempt.EnsureCanResume(DateTime.UtcNow);
        var paper = await ReferenceAsync<ExamPaper>(revision.ExamPaperId);
        if (paper == null || paper.TenantId != revision.TenantId || attempt.ExamPaperId != paper.Id) throw Invalid();
        var rows = await ReferencesAsync<ExamPaperQuestion>(x => x.PaperId == paper.Id && x.TenantId == revision.TenantId);
        if (rows.Count != paper.QuestionCount || rows.Count == 0) throw Invalid();
        var current = await ReferencesAsync<EducationAssessmentCurrentResponse>(x => x.AttemptId == attempt.Id && x.TenantId == attempt.TenantId);
        var responseIds = current.Select(x => x.CurrentRevisionId).ToList();
        var responses = await ReferencesAsync<EducationAssessmentResponseRevision>(x => responseIds.Contains(x.Id) && x.TenantId == attempt.TenantId);
        if (responses.Count != current.Count || current.Any(x => !rows.Any(q => q.Id == x.PaperQuestionId) || !responses.Any(r => r.Id == x.CurrentRevisionId &&
            r.AttemptId == attempt.Id && r.StudentUserId == attempt.StudentUserId && r.PaperQuestionId == x.PaperQuestionId))) throw Invalid();
        // A second device or the cutoff worker may commit between these reads. Re-read
        // business state without tracking; read-only recovery must not rotate a CAS stamp.
        var latest = await ReferenceAsync<EducationAssessmentAttempt>(attempt.Id);
        if (latest == null || latest.TenantId != attempt.TenantId || latest.StudentUserId != attempt.StudentUserId ||
            latest.TaskRevisionId != revision.Id || latest.RecipientId != attempt.RecipientId || latest.ExamPaperId != paper.Id) throw Invalid();
        if (latest.ResponseSetVersion != attempt.ResponseSetVersion || latest.SubmissionId != attempt.SubmissionId || latest.LastSavedAtUtc != attempt.LastSavedAtUtc)
        {
            // ponytail: At most three complete reads; frequent real contention can justify an atomic query projection later.
            if (retry >= 2) throw new BusinessException(EducationAssessmentErrorCodes.ResponseVersionConflict);
            return await MapAttemptAsync(latest, revision, allowClosed, retry + 1);
        }
        if (!allowClosed) latest.EnsureCanResume(DateTime.UtcNow);
        if (current.Any(x => !responses.Any(r => r.Id == x.CurrentRevisionId && r.ResultResponseSetVersion == x.ResponseSetVersion)) ||
            (current.Count == 0 ? 0 : current.Max(x => x.ResponseSetVersion)) != latest.ResponseSetVersion) throw Invalid();
        return new EducationStudentAssessmentAttemptDto
        {
            Id = attempt.Id, RecipientId = attempt.RecipientId, TaskRevisionId = attempt.TaskRevisionId, AttemptNumber = attempt.AttemptNumber,
            StartedAtUtc = attempt.StartedAtUtc, ExpiresAtUtc = attempt.ExpiresAtUtc, ServerNowUtc = DateTime.UtcNow,
            Status = attempt.SubmissionId.HasValue ? EducationAssessmentAttemptStatus.Submitted : EducationAssessmentAttemptStatus.InProgress,
            PaperSnapshotVersion = paper.SnapshotVersion, ResponseSetVersion = attempt.ResponseSetVersion,
            LastSavedAtUtc = attempt.LastSavedAtUtc, SubmissionId = attempt.SubmissionId,
            Responses = responses.OrderBy(x => rows.Single(q => q.Id == x.PaperQuestionId).DisplayOrder).Select(MapResponse).ToList(),
            // Student DTOs deliberately have no slot for answers, criteria, provenance or teacher curriculum IDs.
            Paper = new EducationStudentAssessmentPaperDto
            {
                Title = paper.Title, QuestionCount = paper.QuestionCount, TotalScore = paper.TotalScore,
                Questions = rows.OrderBy(x => x.DisplayOrder).Select(x => new EducationStudentAssessmentQuestionDto
                {
                    Id = x.Id, Type = x.Type, Title = x.Title, Stem = x.Stem, DisplayOrder = x.DisplayOrder, Score = x.Score,
                    Options = Parse<List<ExamPaperQuestionOptionDto>>(x.OptionsJson),
                    ResponseFields = x.ScoringDefinitionJson == null ? [] : QuestionScoringDefinition.Parse(x.ScoringDefinitionJson).Blanks
                        .OrderBy(b => b.DisplayOrder).Select(b => new ExamResponseFieldDto { Id = b.Id, DisplayOrder = b.DisplayOrder, StemMarker = b.StemMarker }).ToList()
                }).ToList()
            }
        };
    }
}
