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

public partial class EducationTeachingPlanAppService
{
    private async Task<List<EducationTeachingActivity>> EnsureActivitiesAsync(EducationTeachingPlanRevision published, bool provided = false)
    {
        if (published.Kind != EducationTeachingPlanRevisionKind.Published || Uow.Current == null) throw Invalid();
        using var tracking = Repo<EducationTeachingActivity>().EnableTracking();
        var rows = await Repo<EducationTeachingActivity>().GetListAsync(x => x.PlanRevisionId == published.Id && x.TenantId == CurrentTenant.Id);
        var content = published.Content()!; var now = DateTime.UtcNow;
        foreach (var step in content.Steps) {
            var row = rows.SingleOrDefault(x => x.StepId == step.Id);
            if (row == null) { row = new(published, step.Id, now); await Repo<EducationTeachingActivity>().InsertAsync(row, autoSave: true); rows.Add(row); }
            if (row.StudentUserId != published.StudentUserId || row.OwnerUserId != published.OwnerUserId || row.PlanId != published.PlanId ||
                row.StepKind != step.Kind || row.EstimatedMinutes != step.EstimatedMinutes || row.DueAtUtc != step.DueAtUtc || row.StartsAtUtc != content.StartsAtUtc) throw Invalid();
            if (provided && row.FirstProvidedAtUtc == null) { row.MarkProvided(now); await Repo<EducationTeachingActivity>().UpdateManyAsync([row], autoSave: true); }
        }
        if (rows.Count != content.Steps.Count) throw Invalid(); return rows;
    }
    private static EducationStudentActivityEventDto StudentEvent(EducationTeachingActivityRevision row)
    {
        if (row.Action == EducationTeachingActivityAction.TeacherFollowUp || row.StudentRevisionNumber == null) throw new AbpAuthorizationException();
        return new() { Id = row.Id, ActivityId = row.ActivityId, StudentRevisionNumber = row.StudentRevisionNumber.Value, Action = row.Action,
            Difficulty = row.Difficulty, SharedText = row.SharedText, ReportedMinutes = row.ReportedMinutes, RecordedAtUtc = row.RecordedAtUtc };
    }
    private static EducationTeacherActivityEventDto TeacherEvent(EducationTeachingActivityRevision row) => new() { Id = row.Id, ActivityId = row.ActivityId,
        RevisionNumber = row.RevisionNumber, BasedOnRevisionId = row.BasedOnRevisionId, ReplyToStudentRevisionId = row.ReplyToStudentRevisionId,
        Action = row.Action, Difficulty = row.Difficulty, SharedText = row.SharedText, InternalTeacherNote = row.InternalTeacherNote,
        ReportedMinutes = row.ReportedMinutes, RecordedAtUtc = row.RecordedAtUtc, RequestFingerprint = row.RequestFingerprint };
    private async Task<(EducationTeachingActivityRevision? Student, EducationTeachingActivityRevision? FollowUp)> ActivityHeadsAsync(EducationTeachingActivity root)
    {
        var student = root.CurrentStudentRevisionId is Guid sid ? await RefAsync<EducationTeachingActivityRevision>(sid) ?? throw Invalid() : null;
        var follow = root.CurrentFollowUpRevisionId is Guid fid ? await RefAsync<EducationTeachingActivityRevision>(fid) ?? throw Invalid() : null;
        foreach (var value in new[] { student, follow }.Where(x => x != null)) if (value!.ActivityId != root.Id || value.TenantId != root.TenantId ||
            value.OwnerUserId != root.OwnerUserId || value.StudentUserId != root.StudentUserId || value.StepKind != root.StepKind) throw Invalid();
        if (student?.Action == EducationTeachingActivityAction.TeacherFollowUp || follow != null && follow.Action != EducationTeachingActivityAction.TeacherFollowUp) throw Invalid();
        return (student, follow);
    }
    private static EducationTeachingActivityState ActivityState(EducationTeachingActivityRevision? student) => student?.Action switch {
        EducationTeachingActivityAction.Started => EducationTeachingActivityState.InProgress,
        EducationTeachingActivityAction.Completed => EducationTeachingActivityState.ReportedComplete,
        EducationTeachingActivityAction.Difficulty => EducationTeachingActivityState.Blocked, _ => EducationTeachingActivityState.NotStarted };
    private async Task<bool> CanReadActivityMaterialsAsync(EducationTeachingActivity row, EducationTeachingPlan plan, EducationActivityFormalAssessmentDto? formal = null)
    {
        if (plan.ExecutionState == EducationPlanExecutionState.Running) return true;
        formal ??= await FormalProgressAsync(row, checkStart: false); if (formal?.AttemptId != null) return true;
        return await AsyncExecuter.AnyAsync((await Repo<EducationTeachingActivityRevision>().GetQueryableAsync()).Where(x => x.ActivityId == row.Id &&
            x.TenantId == plan.TenantId && x.StudentUserId == plan.StudentUserId && x.Action == EducationTeachingActivityAction.Started));
    }
    private async Task<EducationTeacherActivityDto> ActivityViewAsync(EducationTeachingActivity row, EducationTeachingPlan plan)
    {
        var (student, follow) = await ActivityHeadsAsync(row);
        var formal = await FormalProgressAsync(row);
        var state = ActivityState(student);
        var materialsAvailable = await ActivityMaterialsAvailableAsync(row);
        if (!materialsAvailable) state = EducationTeachingActivityState.Blocked;
        if (student?.Action != EducationTeachingActivityAction.Difficulty) {
            if (formal?.SealedAtUtc != null) state = EducationTeachingActivityState.AssessmentSealed;
            else if (formal?.IsStopped == true) state = EducationTeachingActivityState.Blocked;
            else if (formal?.AttemptId != null) state = EducationTeachingActivityState.InProgress;
        }
        return new() { Id = row.Id, CanStart = plan.PublishedRevisionId == row.PlanRevisionId && plan.ExecutionState == EducationPlanExecutionState.Running, MaterialsAvailable = materialsAvailable, StepId = row.StepId, StepOrder = row.StepOrder, CurrentStudentRevisionId = row.CurrentStudentRevisionId, StudentRevisionNumber = row.StudentRevisionNumber,
            State = state, CanReadMaterials = await CanReadActivityMaterialsAsync(row, plan, formal), FormalAssessment = formal, FirstProvidedAtUtc = row.FirstProvidedAtUtc,
            CompletedAtUtc = student?.Action == EducationTeachingActivityAction.Completed ? student.RecordedAtUtc : null,
            CurrentStudent = student == null ? null : StudentEvent(student),
            TeacherHandledAtUtc = student?.Action == EducationTeachingActivityAction.Difficulty && follow?.ReplyToStudentRevisionId == student.Id ? follow.RecordedAtUtc : null,
            CurrentRevisionId = row.CurrentRevisionId, RevisionNumber = row.RevisionNumber, ConcurrencyStamp = row.ConcurrencyStamp, AssignedAtUtc = row.AssignedAtUtc,
            CurrentFollowUp = follow == null ? null : TeacherEvent(follow) };
    }
    private static EducationStudentActivityDto PublicActivity(EducationTeacherActivityDto row) => new() { Id = row.Id, CanStart = row.CanStart, MaterialsAvailable = row.MaterialsAvailable, StepId = row.StepId, StepOrder = row.StepOrder,
        CurrentStudentRevisionId = row.CurrentStudentRevisionId, StudentRevisionNumber = row.StudentRevisionNumber, State = row.State, CanReadMaterials = row.CanReadMaterials,
        FirstProvidedAtUtc = row.FirstProvidedAtUtc, CompletedAtUtc = row.CompletedAtUtc, CurrentStudent = row.CurrentStudent, TeacherHandledAtUtc = row.TeacherHandledAtUtc, FormalAssessment = row.FormalAssessment };
    private async Task<(EducationTeachingActivity Root, EducationTeachingPlan Plan)> ActivityAccessAsync(Guid id, bool student)
    {
        var row = await RefAsync<EducationTeachingActivity>(id) ?? throw new AbpAuthorizationException();
        if (row.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        EducationTeachingPlan plan;
        if (student) { await StudentAsync(); plan = await RefAsync<EducationTeachingPlan>(row.PlanId) ?? throw new AbpAuthorizationException(); await StudentAccessAsync(plan); }
        else plan = await OwnAsync(row.PlanId, write: true);
        plan = await RefAsync<EducationTeachingPlan>(plan.Id) ?? throw Invalid();
        row = await RefAsync<EducationTeachingActivity>(id) ?? throw Invalid();
        if (row.PlanId != plan.Id || row.StudentUserId != plan.StudentUserId || row.OwnerUserId != plan.OwnerUserId) throw Invalid();
        return (row, plan);
    }
    private async Task<EducationTeachingActivityRevision?> ActivityReceiptAsync(Guid planId, Guid operationId, bool student, Guid? activityId = null, string? fingerprint = null)
    {
        if (student) { await StudentAsync(); var plan = await RefAsync<EducationTeachingPlan>(planId) ?? throw new AbpAuthorizationException(); await StudentAccessAsync(plan); }
        else await OwnAsync(planId, write: true);
        var receipt = await RefAsync<EducationTeachingActivityRevision>(operationId); if (receipt == null) return null;
        var root = await RefAsync<EducationTeachingActivity>(receipt.ActivityId) ?? throw Invalid();
        if (root.PlanId != planId || root.TenantId != CurrentTenant.Id || receipt.TenantId != CurrentTenant.Id ||
            student && (receipt.StudentUserId != CurrentUser.Id || receipt.Action == EducationTeachingActivityAction.TeacherFollowUp) ||
            !student && (receipt.OwnerUserId != CurrentUser.Id || receipt.Action != EducationTeachingActivityAction.TeacherFollowUp) ||
            activityId.HasValue && root.Id != activityId) throw new AbpAuthorizationException();
        if (fingerprint != null && receipt.RequestFingerprint != fingerprint) throw new BusinessException(EducationTeachingPlanErrors.OperationConflict);
        return receipt;
    }
    private async Task AppendActivityAsync(EducationTeachingActivity root, EducationTeachingActivityRevision revision)
    {
        root.Advance(revision); await Repo<EducationTeachingActivityRevision>().InsertAsync(revision, autoSave: true);
        await Repo<EducationTeachingActivity>().UpdateManyAsync([root], autoSave: true);
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationTeacherActivityProgressDto> GetActivityProgressAsync(Guid planId, Guid? publishedRevisionId = null) => GetActivityProgressCoreAsync(planId, publishedRevisionId, null);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationTeacherActivityProgressDto> GetActivityProgressForSchoolAsync(Guid planId, Guid? publishedRevisionId, Guid schoolId, Guid teachingAssignmentId)
        => GetActivityProgressCoreAsync(planId, publishedRevisionId, EducationRules.RequireId(schoolId), EducationRules.RequireId(teachingAssignmentId));
    private async Task<EducationTeacherActivityProgressDto> GetActivityProgressCoreAsync(Guid planId, Guid? publishedRevisionId, Guid? schoolId, Guid? teachingAssignmentId = null)
    {
        await TeacherAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        await OwnAsync(planId, write: true); var plan = await RefAsync<EducationTeachingPlan>(planId) ?? throw Invalid();
        if (schoolId != null && plan.SchoolId != schoolId || teachingAssignmentId != null && plan.TeachingAssignmentId != teachingAssignmentId) throw new AbpAuthorizationException();
        var id = publishedRevisionId ?? plan.PublishedRevisionId ?? plan.LastPublishedRevisionId ?? throw Invalid();
        var version = await RefAsync<EducationTeachingPlanRevision>(id) ?? throw Invalid();
        if (version.PlanId != plan.Id || version.TenantId != CurrentTenant.Id || version.OwnerUserId != CurrentUser.Id || version.Kind != EducationTeachingPlanRevisionKind.Published) throw Invalid();
        using var tracking = Repo<EducationTeachingPlanRevision>().DisableTracking();
        var versions = await AsyncExecuter.ToListAsync((await Repo<EducationTeachingPlanRevision>().GetQueryableAsync()).Where(x => x.PlanId == planId &&
            x.TenantId == CurrentTenant.Id && x.Kind == EducationTeachingPlanRevisionKind.Published).OrderByDescending(x => x.RevisionNumber)
            .Select(x => new EducationActivityVersionDto { Id = x.Id, RevisionNumber = x.RevisionNumber, RecordedAtUtc = x.RecordedAtUtc }).Take(1001));
        if (versions.Count > 1000) throw new BusinessException(EducationLearningReportErrors.Capacity);
        var rows = plan.PublishedRevisionId == version.Id ? await EnsureActivitiesAsync(version) : await RowsAsync<EducationTeachingActivity>(x => x.TenantId == CurrentTenant.Id && x.PlanRevisionId == version.Id);
        var result = new EducationTeacherActivityProgressDto { PlanId = plan.Id, SchoolId = plan.SchoolId, TeachingAssignmentId = plan.TeachingAssignmentId, ExecutionState = plan.ExecutionState, PublishedRevisionId = version.Id,
            StudentName = plan.StudentName, ClassName = plan.ClassName, SubjectName = plan.SubjectName, Content = PublicContent(version),
            IsCurrentPublication = plan.PublishedRevisionId == version.Id, Versions = versions };
        foreach (var row in rows) result.Activities.Add(await ActivityViewAsync(row, plan));
        await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationTeacherActivityEventDto>> GetActivityHistoryAsync(Guid activityId, Guid? expectedRevisionId, PagedResultRequestDto input)
    {
        await TeacherAsync(); var take = Page(input); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var (root, _) = await ActivityAccessAsync(activityId, false);
        if (root.CurrentRevisionId != expectedRevisionId) throw new AbpDbConcurrencyException();
        using var tracking = Repo<EducationTeachingActivityRevision>().DisableTracking();
        var query = (await Repo<EducationTeachingActivityRevision>().GetQueryableAsync()).Where(x => x.ActivityId == activityId && x.TenantId == CurrentTenant.Id);
        var count = await AsyncExecuter.CountAsync(query); var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.RevisionNumber).Skip(input.SkipCount).Take(take));
        await uow.CompleteAsync(); return new(count, rows.Select(TeacherEvent).ToList());
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationTeacherActivityEventDto> SaveActivityFollowUpAsync(Guid activityId, SaveEducationActivityFollowUpDto input) => SaveActivityFollowUpCoreAsync(activityId, input, null);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationTeacherActivityEventDto> SaveActivityFollowUpForSchoolAsync(Guid activityId, Guid schoolId, Guid teachingAssignmentId, SaveEducationActivityFollowUpDto input)
        => SaveActivityFollowUpCoreAsync(activityId, input, EducationRules.RequireId(schoolId), EducationRules.RequireId(teachingAssignmentId));
    private async Task<EducationTeacherActivityEventDto> SaveActivityFollowUpCoreAsync(Guid activityId, SaveEducationActivityFollowUpDto input, Guid? schoolId, Guid? teachingAssignmentId = null)
    {
        await TeacherAsync(); EducationRules.RequireId(input.OperationId); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var (_, plan) = await ActivityAccessAsync(activityId, false); var fp = Hash(new { Command = "activity-follow-up", activityId, input });
        if (schoolId != null && plan.SchoolId != schoolId || teachingAssignmentId != null && plan.TeachingAssignmentId != teachingAssignmentId) throw new AbpAuthorizationException();
        var prior = await ActivityReceiptAsync(plan.Id, input.OperationId, false, activityId, fp);
        if (prior != null) { await uow.CompleteAsync(); return TeacherEvent(prior); }
        using var tracking = Repo<EducationTeachingActivity>().EnableTracking(); var root = await Repo<EducationTeachingActivity>().GetAsync(activityId);
        if (root.PlanRevisionId != plan.PublishedRevisionId) throw Changed();
        if (root.CurrentRevisionId != input.ExpectedRevisionId || root.ConcurrencyStamp != input.ConcurrencyStamp ||
            root.CurrentStudentRevisionId != input.ExpectedStudentRevisionId) throw new AbpDbConcurrencyException();
        var (current, _) = await ActivityHeadsAsync(root);
        var row = new EducationTeachingActivityRevision(input.OperationId, root, current, EducationTeachingActivityAction.TeacherFollowUp,
            null, null, null, false, input.ConfirmHandlingReceipt, input.InternalTeacherNote, input.ExpectedStudentRevisionId, fp, DateTime.UtcNow);
        await AppendActivityAsync(root, row);
        await Repo<EducationTeachingPlanNotice>().InsertAsync(EducationTeachingPlanNotice.Handled(GuidGenerator.Create(), plan, root, row), autoSave: true);
        await uow.CompleteAsync(); return TeacherEvent(row);
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationTeacherActivityEventDto?> GetActivityFollowUpOperationAsync(Guid planId, Guid operationId) => GetActivityFollowUpOperationCoreAsync(planId, operationId, null);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationTeacherActivityEventDto?> GetActivityFollowUpOperationForSchoolAsync(Guid planId, Guid operationId, Guid schoolId, Guid teachingAssignmentId)
        => GetActivityFollowUpOperationCoreAsync(planId, operationId, EducationRules.RequireId(schoolId), EducationRules.RequireId(teachingAssignmentId));
    private async Task<EducationTeacherActivityEventDto?> GetActivityFollowUpOperationCoreAsync(Guid planId, Guid operationId, Guid? schoolId, Guid? teachingAssignmentId = null)
    {
        await TeacherAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        if (schoolId != null || teachingAssignmentId != null) {
            var plan = await OwnAsync(planId, write: true);
            if (plan.SchoolId != schoolId || plan.TeachingAssignmentId != teachingAssignmentId) throw new AbpAuthorizationException();
        }
        var row = await ActivityReceiptAsync(planId, operationId, false); await uow.CompleteAsync(); return row == null ? null : TeacherEvent(row);
    }
    [Authorize(EducationPermissions.TeachingPlanView), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationMyActivitiesDto> GetMyActivitiesAsync(Guid planId, Guid expectedVersionId) => GetMyActivitiesCoreAsync(planId, expectedVersionId, null);
    [Authorize(EducationPermissions.TeachingPlanView), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationMyActivitiesDto> GetMyActivitiesForSchoolAsync(Guid planId, Guid expectedVersionId, Guid schoolId) => GetMyActivitiesCoreAsync(planId, expectedVersionId, EducationRules.RequireId(schoolId));
    private async Task<EducationMyActivitiesDto> GetMyActivitiesCoreAsync(Guid planId, Guid expectedVersionId, Guid? schoolId)
    {
        await StudentAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var plan = await RefAsync<EducationTeachingPlan>(planId) ?? throw new AbpAuthorizationException(); await StudentAccessAsync(plan);
        plan = await RefAsync<EducationTeachingPlan>(planId) ?? throw Invalid();
        if (schoolId != null && plan.SchoolId != schoolId) throw new AbpAuthorizationException();
        if (plan.LastPublishedRevisionId == null) throw new AbpAuthorizationException();
        if (plan.PublicRevisionId != expectedVersionId) throw new AbpDbConcurrencyException();
        var result = new EducationMyActivitiesDto { Plan = await StudentDtoAsync(plan, true) };
        var activityVersionId = plan.PublishedRevisionId ?? plan.LastPublishedRevisionId;
        foreach (var row in await RowsAsync<EducationTeachingActivity>(x => x.TenantId == CurrentTenant.Id && x.PlanRevisionId == activityVersionId &&
            (result.Plan.Content != null || x.FirstProvidedAtUtc != null)))
            result.Activities.Add(PublicActivity(await ActivityViewAsync(row, plan)));
        await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.TeachingPlanView), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudentActivityEventDto> SaveMyActivityAsync(Guid activityId, SaveEducationMyActivityDto input)
    {
        await CheckMvpPermissionAsync(EducationPermissions.LearningParticipate); await StudentAsync(); EducationRules.RequireId(input.OperationId);
        if (input.Action == EducationTeachingActivityAction.TeacherFollowUp) throw Invalid();
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var (_, plan) = await ActivityAccessAsync(activityId, true); var fp = Hash(new { Command = "student-activity", activityId, input });
        var prior = await ActivityReceiptAsync(plan.Id, input.OperationId, true, activityId, fp);
        if (prior != null) { await uow.CompleteAsync(); return StudentEvent(prior); }
        using var tracking = Repo<EducationTeachingActivity>().EnableTracking(); var root = await Repo<EducationTeachingActivity>().GetAsync(activityId);
        if (plan.PublishedRevisionId != root.PlanRevisionId) throw Changed();
        if (root.CurrentStudentRevisionId != input.ExpectedStudentRevisionId) throw new AbpDbConcurrencyException();
        if (input.Action == EducationTeachingActivityAction.Started) {
            if (plan.ExecutionState != EducationPlanExecutionState.Running) throw new BusinessException(EducationTeachingPlanErrors.AssessmentStopped);
            var formal = await FormalProgressAsync(root, checkStart: false);
            if (formal?.IsStopped == true || formal?.SealedAtUtc != null) throw Changed();
        }
        if (input.Action != EducationTeachingActivityAction.Difficulty) {
            var version = await RefAsync<EducationTeachingPlanRevision>(root.PlanRevisionId) ?? throw Invalid();
            if (await AvailabilityAsync(plan, version, student: true) != EducationTeachingPlanAvailability.Available) throw Changed();
            if (version.Basis()!.TaskIds.Count > 0) await CheckMvpPermissionAsync(EducationPermissions.AssessmentParticipate);
            if (!await ActivityMaterialsAvailableAsync(root)) throw new BusinessException(EducationTeachingPlanErrors.MaterialUnavailable);
        }
        var (current, _) = await ActivityHeadsAsync(root);
        var row = new EducationTeachingActivityRevision(input.OperationId, root, current, input.Action, input.Difficulty, input.SharedText,
            input.ReportedMinutes, input.ConfirmSharingWithTeacher, input.ConfirmActivityOnly, null, null, fp, DateTime.UtcNow);
        await AppendActivityAsync(root, row); await uow.CompleteAsync(); return StudentEvent(row);
    }
    [Authorize(EducationPermissions.TeachingPlanView), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudentActivityEventDto?> GetMyActivityOperationAsync(Guid planId, Guid operationId)
    {
        await StudentAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var row = await ActivityReceiptAsync(planId, operationId, true); await uow.CompleteAsync(); return row == null ? null : StudentEvent(row);
    }
    [Authorize(EducationPermissions.TeachingPlanView), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationStudentActivityEventDto>> GetMyActivityHistoryAsync(Guid activityId, Guid? expectedStudentRevisionId, PagedResultRequestDto input)
    {
        await StudentAsync(); var take = Page(input); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var (root, _) = await ActivityAccessAsync(activityId, true);
        if (root.CurrentStudentRevisionId != expectedStudentRevisionId) throw new AbpDbConcurrencyException();
        using var tracking = Repo<EducationTeachingActivityRevision>().DisableTracking();
        var query = (await Repo<EducationTeachingActivityRevision>().GetQueryableAsync()).Where(x => x.ActivityId == activityId && x.TenantId == CurrentTenant.Id &&
            x.Action != EducationTeachingActivityAction.TeacherFollowUp && x.StudentUserId == CurrentUser.Id);
        var count = await AsyncExecuter.CountAsync(query); var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.StudentRevisionNumber).Skip(input.SkipCount).Take(take));
        await uow.CompleteAsync(); return new(count, rows.Select(StudentEvent).ToList());
    }
    [Authorize(EducationPermissions.TeachingPlanView), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationActivityWorkloadDto> GetMyActivityWorkloadAsync(GetEducationActivityWorkloadDto input)
    {
        await StudentAsync();
        if (input.StartsAtUtc.Kind != DateTimeKind.Utc || input.EndsAtUtc.Kind != DateTimeKind.Utc || input.StartsAtUtc >= input.EndsAtUtc ||
            input.EndsAtUtc - input.StartsAtUtc > TimeSpan.FromDays(31)) throw Invalid();
        List<EducationTeachingPlan> plans;
        using (var listing = Uow.Begin(requiresNew: true, isTransactional: false)) {
            using var tracking = Repo<EducationTeachingPlan>().DisableTracking();
            plans = await AsyncExecuter.ToListAsync((await Repo<EducationTeachingPlan>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id &&
                x.StudentUserId == CurrentUser.Id && x.LastPublishedRevisionId != null).OrderBy(x => x.Id).Take(1001)); await listing.CompleteAsync();
        }
        if (plans.Count > 1000) throw new BusinessException(EducationLearningReportErrors.Capacity);
        var values = new List<(DateTime Day, string Subject, int Minutes)>(); var result = new EducationActivityWorkloadDto { StartsAtUtc = input.StartsAtUtc, EndsAtUtc = input.EndsAtUtc };
        foreach (var candidate in plans) {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            try { await StudentAccessAsync(candidate); } catch (AbpAuthorizationException) { continue; }
            var plan = await RefAsync<EducationTeachingPlan>(candidate.Id) ?? throw Invalid();
            var view = await StudentDtoAsync(plan, false);
            if (view.Availability != EducationTeachingPlanAvailability.Available) { result.UncountedPlanCount++; await uow.CompleteAsync(); continue; }
            var version = await RefAsync<EducationTeachingPlanRevision>(view.VersionId) ?? throw Invalid();
            var activities = await RowsAsync<EducationTeachingActivity>(x => x.TenantId == CurrentTenant.Id && x.PlanRevisionId == version.Id);
            foreach (var step in version.Content()!.Steps.Where(x => x.DueAtUtc >= input.StartsAtUtc && x.DueAtUtc < input.EndsAtUtc)) {
                var root = activities.SingleOrDefault(x => x.StepId == step.Id);
                var current = root == null ? null : (await ActivityHeadsAsync(root)).Student;
                if (plan.ExecutionState != EducationPlanExecutionState.Running && current?.Action != EducationTeachingActivityAction.Started) continue;
                var formal = root == null ? null : await FormalProgressAsync(root, checkStart: false);
                if (current?.Action != EducationTeachingActivityAction.Completed && formal?.SealedAtUtc == null && formal?.IsStopped != true)
                    values.Add((step.DueAtUtc.Date, plan.SubjectName, step.EstimatedMinutes));
            }
            await uow.CompleteAsync();
        }
        result.CalculatedAtUtc = DateTime.UtcNow;
        result.Days = values.GroupBy(x => new { x.Day, x.Subject }).OrderBy(x => x.Key.Day).ThenBy(x => x.Key.Subject)
            .Select(x => new EducationActivityWorkloadDay(x.Key.Day, x.Key.Subject, x.Count(), x.Sum(v => v.Minutes))).ToList();
        return result;
    }
}
