using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationTeachingPlanAppService
{
    private EducationTeachingPlanGenerationOptions GenerationOptions { get { var value = LazyServiceProvider.LazyGetRequiredService<IOptions<EducationTeachingPlanGenerationOptions>>().Value; value.EnsureValid(); return value; } }
    private async Task GenerationTeacherAsync() { await CheckMvpPermissionAsync(EducationPermissions.TeachingPlanGenerate); await TeacherAsync(); }
    private async Task<EducationTeachingPlan> NewPlanAsync(Guid id, EducationTeachingPlanBasis basis, Guid termId, Guid classId)
    {
        var teacher = await RefAsync<IdentityUser>(CurrentUser.Id!.Value) ?? throw new AbpAuthorizationException(); var schoolClass = await RefAsync<SchoolClass>(classId) ?? throw new AbpAuthorizationException();
        var plan = new EducationTeachingPlan(id, basis, termId, classId, string.IsNullOrWhiteSpace(teacher.Name + teacher.Surname) ? teacher.UserName : (teacher.Name + " " + teacher.Surname).Trim(), schoolClass.Name);
        await TargetAsync(plan, true); return plan;
    }
    private async Task ValidatePeriodAsync(EducationTeachingPlan plan, DateTime starts, DateTime ends)
    {
        var teaching = await RefAsync<TeachingAssignment>(plan.TeachingAssignmentId) ?? throw Invalid(); var term = await RefAsync<AcademicTerm>(plan.AcademicTermId) ?? throw Invalid();
        var enrollment = await RefAsync<StudentEnrollment>(plan.StudentEnrollmentId) ?? throw Invalid(); var member = await RefAsync<SchoolMembership>(plan.SchoolMembershipId) ?? throw Invalid();
        if (starts < term.StartsAtUtc || starts < teaching.EffectiveFromUtc || starts < enrollment.EffectiveFromUtc || starts < member.EffectiveFromUtc ||
            ends > term.EndsAtUtc || teaching.EffectiveToUtc is DateTime end && ends > end || enrollment.EffectiveToUtc is DateTime enrolledEnd && ends > enrolledEnd || member.EffectiveToUtc is DateTime memberEnd && ends > memberEnd) throw Invalid();
    }
    private async Task<EducationTeachingPlanGenerationJob> OwnGenerationAsync(Guid id, bool write = false, bool requireGenerationPermission = true)
    {
        if (requireGenerationPermission) await GenerationTeacherAsync(); else await TeacherAsync();
        var job = await RefAsync<EducationTeachingPlanGenerationJob>(id) ?? throw new AbpAuthorizationException();
        if (job.TenantId != CurrentTenant.Id || job.OwnerUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        await TargetAsync(job.Authority(), job.AcademicTermId, job.SchoolClassId, job.StudentUserId, job.StudentEnrollmentId, job.SchoolMembershipId, true);
        if (write) job = await RefAsync<EducationTeachingPlanGenerationJob>(id) ?? throw new AbpAuthorizationException(); return job;
    }
    private async Task<bool> GenerationFreshAsync(EducationTeachingPlanGenerationJob job)
    {
        try {
            var request = job.Request(); var plan = await NewPlanAsync(job.PlanId, job.Basis(), job.AcademicTermId, job.SchoolClassId);
            await ValidatePeriodAsync(plan, request.StartsAtUtc, request.EndsAtUtc);
            return request.EndsAtUtc > DateTime.UtcNow && await FreshAsync(job.Basis());
        } catch (BusinessException) { return false; }
    }
    private async Task<EducationTeachingPlanGenerationDto> GenerationDtoAsync(EducationTeachingPlanGenerationJob job)
    {
        var fresh = await GenerationFreshAsync(job);
        var basis = job.Basis(); var savedPlan = await RefAsync<EducationTeachingPlan>(job.PlanId);
        return new() { Id = job.Id, PlanId = job.PlanId, StudentName = basis.Student.StudentName, SubjectName = basis.Curriculum.SubjectName, TextbookTitle = basis.Curriculum.TextbookTitle,
            HasSavedPlan = savedPlan != null && savedPlan.OwnerUserId == CurrentUser.Id && savedPlan.StudentUserId == job.StudentUserId && savedPlan.TenantId == CurrentTenant.Id,
            RetryOfJobId = job.RetryOfJobId, Status = job.Status, Failure = job.Failure, ConcurrencyStamp = job.ConcurrencyStamp,
            RequestedAtUtc = job.RequestedAtUtc, CompletedAtUtc = job.CompletedAtUtc, HeartbeatAtUtc = job.HeartbeatAtUtc, SourceIsCurrent = fresh, Request = job.Request(),
            Basis = fresh && job.Status == EducationPlanGenerationStatus.Completed ? job.Basis() : null, Content = fresh && job.Status == EducationPlanGenerationStatus.Completed ? job.Content() : null,
            ModelAlias = job.ModelAlias, ModelProvider = job.ModelProvider, ModelVersion = job.ModelVersion, OutputTokenLimit = job.OutputTokenLimit, Temperature = job.Temperature, PromptVersion = job.PromptVersion, PromptTokens = job.PromptTokens,
            CompletionTokens = job.CompletionTokens, DurationMilliseconds = job.DurationMilliseconds, EstimatedCostUsd = job.EstimatedCostUsd, UsageMayBeIncomplete = job.UsageMayBeIncomplete };
    }
    private async Task GenerationQuotaAsync()
    {
        // The School/account barrier held by NewPlanAsync also serializes per-owner quota checks.
        var query = (await Repo<EducationTeachingPlanGenerationJob>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.OwnerUserId == CurrentUser.Id);
        if (await AsyncExecuter.AnyAsync(query.Where(x => x.Status == EducationPlanGenerationStatus.Pending || x.Status == EducationPlanGenerationStatus.Processing))) throw new BusinessException(EducationPlanGenerationErrors.Busy);
        var day = DateTime.UtcNow.Date;
        if (await AsyncExecuter.CountAsync(query.Where(x => x.RequestedAtUtc >= day)) >= GenerationOptions.MaximumRequestsPerOwnerPerDay) throw new BusinessException(EducationPlanGenerationErrors.Quota);
    }
    [Authorize(EducationPermissions.TeachingPlanGenerate), Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationTeachingPlanGenerationDto> GenerateDraftAsync(Guid planId, GenerateEducationTeachingPlanDraftDto input)
    {
        await GenerationTeacherAsync(); EducationRules.RequireId(planId); if (input?.Request == null) throw Invalid(); EducationRules.RequireId(input.OperationId); input.Request.Validate(); var fp = Hash(new { Command = "plan-generation", planId, input });
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var prior = await RefAsync<EducationTeachingPlanGenerationJob>(input.OperationId);
        if (prior != null) {
            prior = await OwnGenerationAsync(prior.Id); if (prior.PlanId != planId || prior.RequestFingerprint != fp) throw new BusinessException(EducationPlanGenerationErrors.Conflict);
            var receipt = await GenerationDtoAsync(prior); await uow.CompleteAsync(); return receipt;
        }
        if (await RefAsync<EducationTeachingPlan>(planId) != null) throw new BusinessException(EducationPlanGenerationErrors.Conflict);
        var basis = await BasisAsync(input.Request.ReportId, input.Request.ReportSnapshotId, input.Request.StudentUserId); input.Request.Validate(basis);
        var report = await RefAsync<EducationLearningReport>(basis.ReportId) ?? throw Changed(); var plan = await NewPlanAsync(planId, basis, report.AcademicTermId, report.SchoolClassId);
        await ValidatePeriodAsync(plan, input.Request.StartsAtUtc, input.Request.EndsAtUtc); if (input.Request.EndsAtUtc <= DateTime.UtcNow) throw Invalid();
        await GenerationQuotaAsync();
        var job = new EducationTeachingPlanGenerationJob(input.OperationId, planId, input.Request, basis, report.AcademicTermId, report.SchoolClassId, fp, DateTime.UtcNow);
        await Repo<EducationTeachingPlanGenerationJob>().InsertAsync(job, autoSave: true); var result = await GenerationDtoAsync(job); await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.TeachingPlanGenerate), Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationTeachingPlanGenerationDto?> GetGenerationAsync(Guid generationId)
    {
        await GenerationTeacherAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        if (await RefAsync<EducationTeachingPlanGenerationJob>(generationId) == null) { await uow.CompleteAsync(); return null; }
        var result = await GenerationDtoAsync(await OwnGenerationAsync(generationId)); await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.TeachingPlanGenerate), Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationTeachingPlanGenerationListDto>> GetGenerationsAsync(GetEducationTeachingPlanGenerationsInput input)
    {
        await GenerationTeacherAsync(); var take = Page(input); var planId = input.PlanId; using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var now = DateTime.UtcNow; var jobs = await Repo<EducationTeachingPlanGenerationJob>().GetQueryableAsync(); var teaching = await Repo<TeachingAssignment>().GetQueryableAsync();
        var schools = await Repo<School>().GetQueryableAsync(); var terms = await Repo<AcademicTerm>().GetQueryableAsync(); var classes = await Repo<SchoolClass>().GetQueryableAsync();
        var students = await Repo<IdentityUser>().GetQueryableAsync(); var members = await Repo<SchoolMembership>().GetQueryableAsync(); var enrollments = await Repo<StudentEnrollment>().GetQueryableAsync();
        var query = from job in jobs
            join relation in teaching on job.TeachingAssignmentId equals relation.Id
            join school in schools on job.SchoolId equals school.Id
            join term in terms on job.AcademicTermId equals term.Id
            join schoolClass in classes on job.SchoolClassId equals schoolClass.Id
            join student in students on job.StudentUserId equals student.Id
            join member in members on job.SchoolMembershipId equals member.Id
            join enrollment in enrollments on job.StudentEnrollmentId equals enrollment.Id
            where job.TenantId == CurrentTenant.Id && job.OwnerUserId == CurrentUser.Id && (!planId.HasValue || job.PlanId == planId) && student.IsActive &&
                relation.EffectiveFromUtc <= now && (!relation.EffectiveToUtc.HasValue || now < relation.EffectiveToUtc) && term.StartsAtUtc <= now && now < term.EndsAtUtc &&
                member.Role == SchoolMemberRole.Student && member.EffectiveFromUtc <= now && (!member.EffectiveToUtc.HasValue || now < member.EffectiveToUtc) &&
                enrollment.EffectiveFromUtc <= now && (!enrollment.EffectiveToUtc.HasValue || now < enrollment.EffectiveToUtc) &&
                members.Any(m => m.SchoolId == job.SchoolId && m.UserId == job.OwnerUserId && m.Role == SchoolMemberRole.Teacher && m.EffectiveFromUtc <= now && (!m.EffectiveToUtc.HasValue || now < m.EffectiveToUtc))
            select job;
        var total = await AsyncExecuter.LongCountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.RequestedAtUtc).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take).Select(x => new {
            Value = new EducationTeachingPlanGenerationListDto { Id = x.Id, PlanId = x.PlanId, StudentName = x.StudentName, SubjectName = x.SubjectName, TextbookTitle = x.TextbookTitle, RetryOfJobId = x.RetryOfJobId,
                Status = x.Status, Failure = x.Failure, RequestedAtUtc = x.RequestedAtUtc, CompletedAtUtc = x.CompletedAtUtc },
            x.SchoolId, x.AcademicTermId, x.SchoolClassId, x.TeachingAssignmentId, x.StudentUserId, x.StudentEnrollmentId, x.SchoolMembershipId, x.EducationSubjectId, x.TextbookVersionId }));
        var visible = new System.Collections.Generic.List<EducationTeachingPlanGenerationListDto>();
        foreach (var group in rows.GroupBy(x => new { x.TeachingAssignmentId, x.StudentEnrollmentId, x.SchoolMembershipId, x.EducationSubjectId, x.TextbookVersionId })) {
            var row = group.First();
                await TargetAsync(new() { TenantId = CurrentTenant.Id, TeacherUserId = CurrentUser.Id!.Value, SchoolId = row.SchoolId, TeachingAssignmentId = row.TeachingAssignmentId, EducationSubjectId = row.EducationSubjectId, TextbookVersionId = row.TextbookVersionId },
                    row.AcademicTermId, row.SchoolClassId, row.StudentUserId, row.StudentEnrollmentId, row.SchoolMembershipId, false);
                visible.AddRange(group.Select(x => x.Value));
        }
        await uow.CompleteAsync(); return new(total, visible.OrderByDescending(x => x.RequestedAtUtc).ThenBy(x => x.Id).ToList());
    }
    [Authorize(EducationPermissions.TeachingPlanGenerate), Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationTeachingPlanGenerationDto> RetryGenerationAsync(Guid generationId, RetryEducationTeachingPlanGenerationDto input)
    {
        await GenerationTeacherAsync(); EducationRules.RequireId(input.OperationId); var fp = Hash(new { Command = "plan-generation-retry", generationId, input });
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var prior = await RefAsync<EducationTeachingPlanGenerationJob>(input.OperationId);
        if (prior != null) {
            prior = await OwnGenerationAsync(prior.Id); if (prior.RetryOfJobId != generationId || prior.RequestFingerprint != fp) throw new BusinessException(EducationPlanGenerationErrors.Conflict);
            var receipt = await GenerationDtoAsync(prior); await uow.CompleteAsync(); return receipt;
        }
        var failed = await OwnGenerationAsync(generationId, true);
        if (failed.ConcurrencyStamp != input.ConcurrencyStamp) throw new AbpDbConcurrencyException();
        if (failed.Status != EducationPlanGenerationStatus.Failed || await RefAsync<EducationTeachingPlan>(failed.PlanId) != null) throw new BusinessException(EducationPlanGenerationErrors.Conflict);
        if (!await GenerationFreshAsync(failed)) throw new BusinessException(EducationPlanGenerationErrors.Changed);
        await GenerationQuotaAsync();
        var job = new EducationTeachingPlanGenerationJob(input.OperationId, failed.PlanId, failed.Request(), failed.Basis(), failed.AcademicTermId, failed.SchoolClassId, fp, DateTime.UtcNow, failed.Id);
        await Repo<EducationTeachingPlanGenerationJob>().InsertAsync(job, autoSave: true); var result = await GenerationDtoAsync(job); await uow.CompleteAsync(); return result;
    }
    private async Task<(EducationTeachingPlanGenerationJob Job, EducationTeachingPlanBasis Basis)> GeneratedBasisAsync(Guid planId, SaveEducationTeachingPlanDraftDto input)
    {
        var existing = await RefAsync<EducationTeachingPlan>(planId);
        var adopted = existing?.CurrentRevisionId is Guid revisionId && existing.TenantId == CurrentTenant.Id && existing.OwnerUserId == CurrentUser.Id &&
            (await RefAsync<EducationTeachingPlanRevision>(revisionId))?.GenerationJobId == input.GenerationJobId;
        var job = await OwnGenerationAsync(input.GenerationJobId!.Value, true, requireGenerationPermission: !adopted); var request = job.Request();
        if (job.Status != EducationPlanGenerationStatus.Completed || job.PlanId != planId || request.ReportId != input.ReportId || request.ReportSnapshotId != input.ReportSnapshotId || request.StudentUserId != input.StudentUserId) throw new BusinessException(EducationPlanGenerationErrors.Conflict);
        if (!await GenerationFreshAsync(job)) throw new BusinessException(EducationPlanGenerationErrors.Changed);
        return (job, job.Basis());
    }
}
