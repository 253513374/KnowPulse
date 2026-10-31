using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationTeachingPlanAppService
{
    private async Task<(IQueryable<Guid> Members, IQueryable<Guid> Enrollments)> WorkloadStudentScopeAsync(Guid? schoolId)
    {
        var now = DateTime.UtcNow;
        var members = (await Repo<SchoolMembership>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.UserId == CurrentUser.Id && (schoolId == null || x.SchoolId == schoolId) && x.Role == SchoolMemberRole.Student &&
            x.EffectiveFromUtc <= now && (x.EffectiveToUtc == null || now < x.EffectiveToUtc)).Select(x => x.Id);
        var enrollments = await Repo<StudentEnrollment>().GetQueryableAsync(); var classes = await Repo<SchoolClass>().GetQueryableAsync(); var terms = await Repo<AcademicTerm>().GetQueryableAsync(); var schools = await Repo<School>().GetQueryableAsync();
        var active = from enrollment in enrollments join schoolClass in classes on enrollment.SchoolClassId equals schoolClass.Id join term in terms on schoolClass.AcademicTermId equals term.Id join school in schools on enrollment.SchoolId equals school.Id
            where enrollment.TenantId == CurrentTenant.Id && enrollment.StudentUserId == CurrentUser.Id && (schoolId == null || enrollment.SchoolId == schoolId) && enrollment.EffectiveFromUtc <= now && (enrollment.EffectiveToUtc == null || now < enrollment.EffectiveToUtc) &&
                schoolClass.TenantId == enrollment.TenantId && schoolClass.SchoolId == enrollment.SchoolId && term.TenantId == enrollment.TenantId && term.SchoolId == enrollment.SchoolId && term.StartsAtUtc <= now && now < term.EndsAtUtc && school.TenantId == enrollment.TenantId
            select enrollment.Id;
        return (members, active);
    }
    private async Task<List<Guid>> WorkloadIdsAsync(IQueryable<Guid> query)
    {
        var ids = await AsyncExecuter.ToListAsync(query.OrderBy(x => x).Take(1001));
        if (ids.Count > 1000) throw new BusinessException(EducationLearningWorkloadErrors.Capacity); return ids;
    }
    private static bool WorkloadWindow(GetEducationActivityWorkloadDto window, DateTime due) => due >= window.StartsAtUtc && due < window.EndsAtUtc;
    private static EducationLearningWorkloadState WorkloadState(DateTime now, DateTime start, DateTime due, bool blocked) => now >= due
        ? EducationLearningWorkloadState.Overdue : blocked ? EducationLearningWorkloadState.Blocked : now < start ? EducationLearningWorkloadState.Scheduled : EducationLearningWorkloadState.Ready;
    private static void AddWorkload(EducationLearningWorkloadDto result, EducationLearningWorkloadItemDto item)
    {
        if (result.Items.Count >= 3000) throw new BusinessException(EducationLearningWorkloadErrors.Capacity); result.Items.Add(item);
    }
    [Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationLearningWorkloadDto> GetMyLearningWorkloadAsync(GetEducationActivityWorkloadDto input, Guid? schoolId = null)
    {
        await CheckMvpPermissionAsync(EducationPermissions.LearningParticipate); await Access.CurrentAccountAsync();
        if (input == null || input.StartsAtUtc.Kind != DateTimeKind.Utc || input.EndsAtUtc.Kind != DateTimeKind.Utc || input.StartsAtUtc >= input.EndsAtUtc || input.EndsAtUtc - input.StartsAtUtc > TimeSpan.FromDays(31)) throw new BusinessException(EducationLearningWorkloadErrors.InvalidWindow);
        var result = new EducationLearningWorkloadDto { SchoolId = schoolId, StartsAtUtc = input.StartsAtUtc, EndsAtUtc = input.EndsAtUtc };
        var plansAllowed = await IsMvpPermissionGrantedAsync(EducationPermissions.TeachingPlanView);
        var assessmentsAllowed = await IsMvpPermissionGrantedAsync(EducationPermissions.AssessmentParticipate);
        if (plansAllowed) await PlanWorkloadAsync(input, result, assessmentsAllowed); else result.OmittedKinds.Add(EducationLearningWorkloadKind.PlanActivity);
        await StudyWorkloadAsync(input, result);
        if (assessmentsAllowed) { await CorrectionWorkloadAsync(input, result); await AssessmentWorkloadAsync(input, result); }
        else result.OmittedKinds.AddRange([EducationLearningWorkloadKind.Correction, EducationLearningWorkloadKind.Assessment]);
        await CheckMvpPermissionAsync(EducationPermissions.LearningParticipate); await Access.CurrentAccountAsync();
        if (plansAllowed && !await IsMvpPermissionGrantedAsync(EducationPermissions.TeachingPlanView)) {
            result.Items.RemoveAll(x => x.Kind == EducationLearningWorkloadKind.PlanActivity); result.UnestimatedPlanCount = 0; result.OmittedKinds.Add(EducationLearningWorkloadKind.PlanActivity);
        }
        if (assessmentsAllowed && !await IsMvpPermissionGrantedAsync(EducationPermissions.AssessmentParticipate)) {
            result.Items.RemoveAll(x => x.Kind is EducationLearningWorkloadKind.Assessment or EducationLearningWorkloadKind.Correction);
            result.OmittedKinds.AddRange([EducationLearningWorkloadKind.Assessment, EducationLearningWorkloadKind.Correction]);
        }
        result.CalculatedAtUtc = DateTime.UtcNow;
        result.Items = result.Items.OrderBy(x => x.DueAtUtc).ThenBy(x => x.SubjectName).ThenBy(x => x.Kind).ThenBy(x => x.Id).ToList();
        result.Days = result.Items.GroupBy(x => new { Day = x.DueAtUtc.Date, x.SubjectName }).OrderBy(x => x.Key.Day).ThenBy(x => x.Key.SubjectName)
            .Select(x => new EducationLearningWorkloadDayDto(x.Key.Day, x.Key.SubjectName, x.Count(),
                x.Where(v => v.State is EducationLearningWorkloadState.Ready or EducationLearningWorkloadState.Scheduled).Sum(v => v.EstimatedMinutes),
                x.Count(v => v.State == EducationLearningWorkloadState.Blocked), x.Count(v => v.State == EducationLearningWorkloadState.Overdue))).ToList();
        return result;
    }
    private async Task PlanWorkloadAsync(GetEducationActivityWorkloadDto input, EducationLearningWorkloadDto result, bool assessmentsAllowed)
    {
        List<Guid> ids;
        using (var listing = Uow.Begin(requiresNew: true, isTransactional: false)) {
            var (members, enrollments) = await WorkloadStudentScopeAsync(result.SchoolId);
            ids = await WorkloadIdsAsync((await Repo<EducationTeachingPlan>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.StudentUserId == CurrentUser.Id && x.PublishedRevisionId != null && members.Contains(x.SchoolMembershipId) && enrollments.Contains(x.StudentEnrollmentId)).Select(x => x.Id)); await listing.CompleteAsync();
        }
        foreach (var id in ids) {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            try {
                var plan = await RefAsync<EducationTeachingPlan>(id) ?? throw Invalid(); await StudentAccessAsync(plan); plan = await RefAsync<EducationTeachingPlan>(id) ?? throw Invalid();
                if (plan.PublishedRevisionId == null) { await uow.CompleteAsync(); continue; }
                var view = await StudentDtoAsync(plan, false);
                if (view.Availability != EducationTeachingPlanAvailability.Available) {
                    // Withheld plan dates and step counts must not become a calendar-query side channel.
                    result.UnestimatedPlanCount++;
                    await uow.CompleteAsync(); continue;
                }
                var version = await RefAsync<EducationTeachingPlanRevision>(view.VersionId) ?? throw Invalid(); var content = version.Content()!;
                var rows = await RowsAsync<EducationTeachingActivity>(x => x.PlanRevisionId == version.Id && x.TenantId == CurrentTenant.Id);
                foreach (var step in content.Steps.Where(x => WorkloadWindow(input, x.DueAtUtc))) {
                    var activity = rows.SingleOrDefault(x => x.StepId == step.Id); var current = activity == null ? null : (await ActivityHeadsAsync(activity)).Student;
                    if (current?.Action == EducationTeachingActivityAction.Completed) continue;
                    if (plan.ExecutionState != EducationPlanExecutionState.Running && current?.Action != EducationTeachingActivityAction.Started) continue;
                    var formal = activity == null ? null : await FormalProgressAsync(activity, checkStart: false);
                    // A real bound assessment owns its own deadline and duration in the assessment section.
                    if (assessmentsAllowed && formal?.CanOpen == true) continue;
                    var requiresAssessment = step.Kind is EducationTeachingPlanStepKind.Diagnostic or EducationTeachingPlanStepKind.Reassessment;
                    var blocked = current?.Action == EducationTeachingActivityAction.Difficulty || requiresAssessment;
                    foreach (var material in content.Materials.Where(x => x.StepId == step.Id)) if (!await Materials.AvailableAsync(MaterialAuthority(plan), material)) blocked = true;
                    AddWorkload(result, new() { Kind = EducationLearningWorkloadKind.PlanActivity, Id = step.Id, SourceId = plan.Id,
                        PublicationId = version.Id, SubjectName = plan.SubjectName, ClassName = plan.ClassName, StartsAtUtc = content.StartsAtUtc, DueAtUtc = step.DueAtUtc,
                        EstimatedMinutes = step.EstimatedMinutes, State = WorkloadState(DateTime.UtcNow, content.StartsAtUtc, step.DueAtUtc, blocked) });
                }
            } catch (AbpAuthorizationException) { continue; }
            await uow.CompleteAsync();
        }
    }
    private async Task StudyWorkloadAsync(GetEducationActivityWorkloadDto input, EducationLearningWorkloadDto result)
    {
        List<Guid> ids;
        using (var listing = Uow.Begin(requiresNew: true, isTransactional: false)) {
            var (members, enrollments) = await WorkloadStudentScopeAsync(result.SchoolId);
            var recipients = await Repo<EducationStudyRecipient>().GetQueryableAsync(); var tasks = await Repo<EducationStudyTask>().GetQueryableAsync(); var versions = await Repo<EducationStudyTaskRevision>().GetQueryableAsync();
            ids = await WorkloadIdsAsync(from row in recipients join task in tasks on row.TaskId equals task.Id join version in versions on row.TaskRevisionId equals version.Id
                where row.TenantId == CurrentTenant.Id && row.StudentUserId == CurrentUser.Id && task.PublishedRevisionId == row.TaskRevisionId && members.Contains(row.SchoolMembershipId) && enrollments.Contains(row.StudentEnrollmentId) && version.EndsAtUtc >= input.StartsAtUtc && version.EndsAtUtc < input.EndsAtUtc select row.Id);
            await listing.CompleteAsync();
        }
        var service = LazyServiceProvider.LazyGetRequiredService<EducationStudyTaskAppService>();
        foreach (var id in ids) {
            EducationMyStudyTaskDto row; try { row = await service.GetMyWorkloadMetadataAsync(id); } catch (AbpAuthorizationException) { continue; }
            if (row.Availability is EducationStudyTaskAvailability.Withdrawn or EducationStudyTaskAvailability.Superseded || row.State == EducationTeachingActivityState.ReportedComplete || !WorkloadWindow(input, row.EndsAtUtc)) continue;
            AddWorkload(result, new() { Kind = EducationLearningWorkloadKind.StudyTask, Id = row.Id, SourceId = row.TaskId, PublicationId = row.PublicationId,
                SubjectName = row.SubjectName, ClassName = row.ClassName, StartsAtUtc = row.StartsAtUtc, DueAtUtc = row.EndsAtUtc, EstimatedMinutes = row.EstimatedMinutes,
                State = WorkloadState(row.ServerNowUtc, row.StartsAtUtc, row.EndsAtUtc, row.Availability == EducationStudyTaskAvailability.Unavailable || row.State == EducationTeachingActivityState.Blocked) });
        }
    }
    private async Task CorrectionWorkloadAsync(GetEducationActivityWorkloadDto input, EducationLearningWorkloadDto result)
    {
        List<Guid> ids;
        using (var listing = Uow.Begin(requiresNew: true, isTransactional: false)) {
            var (members, enrollments) = await WorkloadStudentScopeAsync(result.SchoolId);
            var deliveries = await Repo<EducationCorrectionDelivery>().GetQueryableAsync(); var tasks = await Repo<EducationCorrectionTask>().GetQueryableAsync();
            ids = await WorkloadIdsAsync(from row in deliveries join task in tasks on row.TaskId equals task.Id where row.TenantId == CurrentTenant.Id && row.StudentUserId == CurrentUser.Id && task.PublishedRevisionId == row.PublicationId && members.Contains(task.SchoolMembershipId) && enrollments.Contains(task.StudentEnrollmentId) select row.Id);
            await listing.CompleteAsync();
        }
        var service = LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAppService>();
        foreach (var id in ids) {
            EducationMyCorrectionDto row; try { row = await service.GetMyCorrectionWorkloadMetadataAsync(id); } catch (AbpAuthorizationException) { continue; }
            if (row.Availability is EducationCorrectionAvailability.Withdrawn or EducationCorrectionAvailability.Superseded || !WorkloadWindow(input, row.DueAtUtc) || row.WorkState is EducationCorrectionWorkState.AwaitingReview or EducationCorrectionWorkState.Accepted) continue;
            AddWorkload(result, new() { Kind = EducationLearningWorkloadKind.Correction, Id = row.Id, SourceId = row.TaskId, PublicationId = row.PublicationId,
                SubjectName = row.SubjectName, ClassName = row.ClassName, StartsAtUtc = row.StartsAtUtc, DueAtUtc = row.DueAtUtc, EstimatedMinutes = row.EstimatedMinutes,
                State = WorkloadState(row.ServerNowUtc, row.StartsAtUtc, row.DueAtUtc, row.WorkState == null || row.Availability is EducationCorrectionAvailability.ResultUnavailable or EducationCorrectionAvailability.SourceChanged or EducationCorrectionAvailability.Unavailable) });
        }
    }
    private async Task AssessmentWorkloadAsync(GetEducationActivityWorkloadDto input, EducationLearningWorkloadDto result)
    {
        List<Guid> ids;
        using (var listing = Uow.Begin(requiresNew: true, isTransactional: false)) {
            var (members, enrollments) = await WorkloadStudentScopeAsync(result.SchoolId);
            var recipients = await Repo<EducationAssessmentRecipient>().GetQueryableAsync(); var tasks = await Repo<EducationAssessmentTask>().GetQueryableAsync();
            ids = await WorkloadIdsAsync(from row in recipients join task in tasks on row.TaskId equals task.Id where row.TenantId == CurrentTenant.Id && row.StudentUserId == CurrentUser.Id && task.PublishedRevisionId == row.TaskRevisionId && task.ClosureId == null && members.Contains(row.SchoolMembershipId) && enrollments.Contains(row.StudentEnrollmentId) select row.Id);
            await listing.CompleteAsync();
        }
        var service = LazyServiceProvider.LazyGetRequiredService<IEducationAssessmentAppService>(); var guard = LazyServiceProvider.LazyGetRequiredService<EducationTeachingFormalAssessmentGuard>();
        foreach (var id in ids) {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: false);
            try {
                var row = await service.GetMyStudentTaskAsync(id); var task = await RefAsync<EducationAssessmentTask>(row.TaskId) ?? throw Invalid();
                if (task.ClosureId != null || task.PublishedRevisionId != row.TaskRevisionId) { await uow.CompleteAsync(); continue; }
                var attempt = row.CurrentAttemptId is Guid aid ? await RefAsync<EducationAssessmentAttempt>(aid) ?? throw Invalid() : null;
                if (attempt != null && (attempt.RecipientId != row.Id || attempt.StudentUserId != CurrentUser.Id || attempt.TenantId != CurrentTenant.Id || attempt.TaskRevisionId != row.TaskRevisionId)) throw Invalid();
                if (attempt?.SubmissionId != null) { await uow.CompleteAsync(); continue; }
                if (attempt == null && await guard.IsPlanExecutionStoppedAsync(id)) { await uow.CompleteAsync(); continue; }
                var due = attempt?.ExpiresAtUtc ?? row.EndsAtUtc; if (due > row.EndsAtUtc) due = row.EndsAtUtc;
                if (WorkloadWindow(input, due)) {
                    var now = DateTime.UtcNow; var blocked = attempt == null && !await guard.CanStartAsync(id);
                    var minutes = attempt == null ? row.DurationMinutes : Math.Clamp((int)Math.Ceiling((due - now).TotalMinutes), 0, row.DurationMinutes);
                    AddWorkload(result, new() { Kind = EducationLearningWorkloadKind.Assessment, Id = row.Id, SourceId = row.TaskId, PublicationId = row.TaskRevisionId,
                        SubjectName = row.SubjectName, ClassName = row.SchoolClassName, StartsAtUtc = row.StartsAtUtc, DueAtUtc = due, EstimatedMinutes = minutes,
                        State = WorkloadState(now, row.StartsAtUtc, due, blocked) });
                }
            } catch (AbpAuthorizationException) { continue; }
            await uow.CompleteAsync();
        }
    }
}
