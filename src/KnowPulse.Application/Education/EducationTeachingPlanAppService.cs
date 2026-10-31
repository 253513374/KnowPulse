using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
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
[ExposeServices(typeof(IEducationTeachingPlanAppService), typeof(IEducationTeachingPlanActivityAppService), typeof(IEducationTeachingFormalAssessmentAppService), typeof(IEducationTeachingMaterialAppService), typeof(IEducationLearningWorkloadAppService), typeof(IEducationTeachingPlanExecutionAppService), typeof(IEducationTeachingPlanNoticeAppService), IncludeSelf = true)]
public partial class EducationTeachingPlanAppService : KnowPulseAppService, IEducationTeachingPlanAppService, IEducationTeachingPlanActivityAppService, IEducationTeachingFormalAssessmentAppService, IEducationTeachingMaterialAppService, IEducationLearningWorkloadAppService
{
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private IUnitOfWorkManager Uow => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();
    private EducationAssessmentAccessGuard Access => LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAccessGuard>();
    private EducationLearningReportSourceReader Reader => LazyServiceProvider.LazyGetRequiredService<EducationLearningReportSourceReader>();
    private async Task<T?> RefAsync<T>(Guid id) where T : class, IEntity<Guid> { using var tracking = Repo<T>().DisableTracking(); return await Repo<T>().FindAsync(id); }
    private async Task<List<T>> RowsAsync<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity<Guid> { using var tracking = Repo<T>().DisableTracking(); return await Repo<T>().GetListAsync(predicate); }
    private async Task TeacherAsync() { await CheckMvpPermissionAsync(EducationPermissions.TeachingPlanManage); await CheckMvpPermissionAsync(EducationPermissions.AnalysisManage); await Access.CurrentAccountAsync(); }
    private async Task StudentAsync() { await CheckMvpPermissionAsync(EducationPermissions.TeachingPlanView); await Access.CurrentAccountAsync(); }
    private static readonly JsonSerializerOptions CommandJson = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { EducationTeachingPlanJson.PreserveEmptyMaterialCompatibility } } };
    private static string Hash(object input) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input, CommandJson))));
    private static BusinessException Invalid() => new(EducationTeachingPlanErrors.Invalid);
    private static BusinessException Changed() => new(EducationTeachingPlanErrors.SourceChanged);
    private static int Page(PagedResultRequestDto input) { if (input.SkipCount < 0 || input.MaxResultCount is < 1 or > 100) throw Invalid(); return input.MaxResultCount; }
    private static EducationTeachingPlanRevisionDto Map(EducationTeachingPlanRevision revision) => new() { Id = revision.Id, PlanId = revision.PlanId,
        RevisionNumber = revision.RevisionNumber, BasedOnRevisionId = revision.BasedOnRevisionId, SourceRevisionId = revision.SourceRevisionId, Kind = revision.Kind,
        Basis = revision.Basis(), Content = revision.Content(), InternalNotes = revision.InternalNotes, Reason = revision.Reason, RecordedAtUtc = revision.RecordedAtUtc, RequestFingerprint = revision.RequestFingerprint, GenerationJobId = revision.GenerationJobId };
    private async Task<EducationTeachingPlanRevisionDto?> ReceiptAsync(Guid planId, Guid id, string? fingerprint = null)
    {
        await TeacherAsync(); var plan = await RefAsync<EducationTeachingPlan>(planId); if (plan == null) return null;
        await OwnAsync(planId); var revision = await RefAsync<EducationTeachingPlanRevision>(id); if (revision == null) return null;
        if (revision.PlanId != planId || revision.TenantId != CurrentTenant.Id || revision.OwnerUserId != CurrentUser.Id || fingerprint != null && revision.RequestFingerprint != fingerprint)
            throw new BusinessException(EducationTeachingPlanErrors.OperationConflict);
        return Map(revision);
    }
    private async Task AppendAsync(EducationTeachingPlan plan, EducationTeachingPlanRevision revision)
    { plan.Advance(revision); await Repo<EducationTeachingPlanRevision>().InsertAsync(revision, autoSave: true); await Repo<EducationTeachingPlan>().UpdateManyAsync([plan], autoSave: true); }
    private static void Stamp(EducationTeachingPlan plan, string? stamp, Guid? revision)
    { if (plan.ConcurrencyStamp != stamp || plan.CurrentRevisionId != revision) throw new AbpDbConcurrencyException(); }

    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationTeachingPlanRevisionDto> SaveDraftAsync(Guid planId, SaveEducationTeachingPlanDraftDto input)
    {
        await TeacherAsync(); EducationRules.RequireId(planId); EducationRules.RequireId(input.OperationId); var fp = Hash(new { Command = "plan-draft", planId, input });
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var prior = await ReceiptAsync(planId, input.OperationId, fp); if (prior != null) { await uow.CompleteAsync(); return prior; }
        var generated = input.GenerationJobId.HasValue ? await GeneratedBasisAsync(planId, input) : default;
        var basis = generated.Job != null ? generated.Basis : await BasisAsync(input.ReportId, input.ReportSnapshotId, input.StudentUserId);
        using var tracking = Repo<EducationTeachingPlan>().EnableTracking(); var plan = await Repo<EducationTeachingPlan>().FindAsync(planId); var created = plan == null;
        if (created)
        {
            if (input.ExpectedRevisionId != null || input.ConcurrencyStamp != null) throw new AbpDbConcurrencyException();
            var report = await RefAsync<EducationLearningReport>(input.ReportId) ?? throw Changed();
            plan = await NewPlanAsync(planId, basis, report.AcademicTermId, report.SchoolClassId);
        }
        else { await OwnAsync(planId); Stamp(plan!, input.ConcurrencyStamp, input.ExpectedRevisionId); }
        await ValidateMaterialsAsync(plan!, input.Content);
        var revision = new EducationTeachingPlanRevision(input.OperationId, plan!, EducationTeachingPlanRevisionKind.Draft, basis, input.Content, input.InternalNotes, null, null, plan!.ConcurrencyStamp, fp, DateTime.UtcNow);
        if (generated.Job != null) revision.LinkGeneration(generated.Job);
        if (created) await Repo<EducationTeachingPlan>().InsertAsync(plan!, autoSave: true);
        await AppendAsync(plan, revision); await uow.CompleteAsync(); return Map(revision);
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationTeachingPlanDto> GetAsync(Guid id)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var plan = await OwnAsync(id, write: true); var current = await RefAsync<EducationTeachingPlanRevision>(plan.CurrentRevisionId ?? throw Invalid()) ?? throw Invalid();
        var published = plan.PublishedRevisionId is Guid publishedId ? await RefAsync<EducationTeachingPlanRevision>(publishedId) : null;
        var fresh = false;
        if (current.Basis() is { } basis) { try { fresh = await FreshAsync(basis); } catch (BusinessException) { } catch (AbpAuthorizationException) { } }
        var result = new EducationTeachingPlanDto { Id = plan.Id, StudentName = plan.StudentName, ClassName = plan.ClassName, SubjectName = plan.SubjectName,
            RevisionNumber = plan.RevisionNumber, ExecutionState = plan.ExecutionState, PublishedRevisionId = plan.PublishedRevisionId, LastPublishedRevisionId = plan.LastPublishedRevisionId, ConcurrencyStamp = plan.ConcurrencyStamp, Current = Map(current),
            SourceIsCurrent = fresh, StudentAvailability = await AvailabilityAsync(plan, published) };
        await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)]
    public virtual async Task<PagedResultDto<EducationTeachingPlanListDto>> GetListAsync(PagedResultRequestDto input)
    {
        await TeacherAsync(); var take = Page(input); var result = new List<EducationTeachingPlanListDto>();
        using var tracking = Repo<EducationTeachingPlan>().DisableTracking(); var query = (await Repo<EducationTeachingPlan>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.OwnerUserId == CurrentUser.Id);
        var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.CreationTime).ThenBy(x => x.Id).Take(1001));
        if (rows.Count > 1000) throw new BusinessException(EducationLearningReportErrors.Capacity);
        foreach (var plan in rows) {
            try { await TargetAsync(plan, false); } catch (AbpAuthorizationException) { continue; }
            result.Add(new() { Id = plan.Id, StudentName = plan.StudentName, ClassName = plan.ClassName, SubjectName = plan.SubjectName, ExecutionState = plan.ExecutionState, RevisionNumber = plan.RevisionNumber, PublishedRevisionId = plan.PublishedRevisionId });
        }
        return new(result.Count, result.Skip(input.SkipCount).Take(take).ToList());
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)]
    public virtual async Task<EducationTeachingPlanRevisionDto> GetRevisionAsync(Guid planId, Guid revisionId)
    { await OwnAsync(planId); return await ReceiptAsync(planId, revisionId) ?? throw Invalid(); }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)]
    public virtual Task<EducationTeachingPlanRevisionDto?> GetOperationAsync(Guid planId, Guid operationId) => ReceiptAsync(planId, operationId);

    private async Task<EducationTeachingPlanRevisionDto> TransitionAsync(Guid planId, TransitionEducationTeachingPlanDto input, EducationTeachingPlanRevisionKind kind)
    {
        await TeacherAsync(); EducationRules.RequireId(input.OperationId); var fp = Hash(new { Command = "plan-transition", planId, kind, input });
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var prior = await ReceiptAsync(planId, input.OperationId, fp); if (prior != null) { await uow.CompleteAsync(); return prior; }
        await OwnAsync(planId, write: true); using var tracking = Repo<EducationTeachingPlan>().EnableTracking(); var plan = await Repo<EducationTeachingPlan>().GetAsync(planId);
        Stamp(plan, input.ConcurrencyStamp, input.ExpectedRevisionId); var source = await RefAsync<EducationTeachingPlanRevision>(input.ExpectedRevisionId) ?? throw Invalid();
        if (kind != EducationTeachingPlanRevisionKind.Withdrawn)
        {
            if (!input.ConfirmRecipientAndContent || !input.ConfirmInlineMaterialRights || !input.ConfirmStudentSafeText) throw Invalid();
            var basis = source.Basis() ?? throw Invalid(); if (!await FreshAsync(basis)) throw Changed();
            var content = source.Content()!;
            await ValidateMaterialsAsync(plan, content);
            await ValidatePeriodAsync(plan, content.StartsAtUtc, content.EndsAtUtc);
        }
        var revision = new EducationTeachingPlanRevision(input.OperationId, plan, kind, kind == EducationTeachingPlanRevisionKind.Withdrawn ? null : source.Basis(),
            kind == EducationTeachingPlanRevisionKind.Withdrawn ? null : source.Content(), kind == EducationTeachingPlanRevisionKind.Withdrawn ? null : source.InternalNotes,
            input.Reason, source, input.ConcurrencyStamp, fp, DateTime.UtcNow);
        await AppendAsync(plan, revision);
        if (kind == EducationTeachingPlanRevisionKind.Published) await EnsureActivitiesAsync(revision);
        if (kind is EducationTeachingPlanRevisionKind.Published or EducationTeachingPlanRevisionKind.Withdrawn)
            await Repo<EducationTeachingPlanNotice>().InsertAsync(EducationTeachingPlanNotice.Content(GuidGenerator.Create(), plan, revision), autoSave: true);
        await uow.CompleteAsync(); return Map(revision);
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationTeachingPlanRevisionDto> ConfirmAsync(Guid planId, TransitionEducationTeachingPlanDto input) => TransitionAsync(planId, input, EducationTeachingPlanRevisionKind.Confirmed);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationTeachingPlanRevisionDto> PublishAsync(Guid planId, TransitionEducationTeachingPlanDto input) => TransitionAsync(planId, input, EducationTeachingPlanRevisionKind.Published);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationTeachingPlanRevisionDto> WithdrawAsync(Guid planId, TransitionEducationTeachingPlanDto input) => TransitionAsync(planId, input, EducationTeachingPlanRevisionKind.Withdrawn);

    private async Task StudentAccessAsync(EducationTeachingPlan plan, bool write = true)
    {
        if (plan.TenantId != CurrentTenant.Id || plan.StudentUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        await Access.EnsureStudentAsync(plan.SchoolId, plan.AcademicTermId, plan.SchoolClassId, plan.SchoolMembershipId, plan.StudentEnrollmentId, plan.StudentUserId, forUpdate: write);
    }
    private async Task<EducationStudentTeachingPlanDto> StudentDtoAsync(EducationTeachingPlan plan, bool body)
    {
        var versionId = plan.PublicRevisionId ?? throw Invalid();
        var version = await RefAsync<EducationTeachingPlanRevision>(versionId) ?? throw Invalid();
        var status = await AvailabilityAsync(plan, version, student: true);
        if (status == EducationTeachingPlanAvailability.Available && version.Basis()!.TaskIds.Count > 0)
            await CheckMvpPermissionAsync(EducationPermissions.AssessmentParticipate);
        var dto = new EducationStudentTeachingPlanDto { Id = plan.Id, SchoolId = plan.SchoolId, VersionId = version.Id, VersionNumber = version.RevisionNumber, TeacherName = plan.TeacherName,
            ClassName = plan.ClassName, SubjectName = plan.SubjectName, Availability = status, RecordedAtUtc = version.RecordedAtUtc, ExecutionState = plan.ExecutionState, ExecutionChangedAtUtc = plan.ExecutionChangedAtUtc };
        if (!body || status != EducationTeachingPlanAvailability.Available) return dto;
        var basis = version.Basis()!; dto.Content = PublicContent(version);
        await EnsureActivitiesAsync(version, provided: true);
        var existing = (await RowsAsync<EducationTeachingPlanQuestionExposure>(x => x.PlanRevisionId == version.Id && x.StudentUserId == plan.StudentUserId && x.TenantId == CurrentTenant.Id)).Select(x => x.Id).ToHashSet();
        var exposures = basis.Questions.Select(x => new EducationTeachingPlanQuestionExposure(plan, version, x, DateTime.UtcNow)).Where(x => !existing.Contains(x.Id)).ToList();
        foreach (var batch in exposures.Chunk(100)) await Repo<EducationTeachingPlanQuestionExposure>().InsertManyAsync(batch, autoSave: true);
        return dto;
    }
    private static EducationStudentTeachingPlanContent PublicContent(EducationTeachingPlanRevision version)
    {
        var content = version.Content()!; var basis = version.Basis()!;
        return new() { Title = content.Title, StudentGoal = content.StudentGoal, TeacherActions = content.TeacherActions, SuccessCriteria = content.SuccessCriteria,
            StartsAtUtc = content.StartsAtUtc, EndsAtUtc = content.EndsAtUtc, ReassessmentAtUtc = content.ReassessmentAtUtc, Steps = content.Steps,
            Materials = content.Materials.Select(x => new EducationStudentTeachingMaterial(x.Id, x.StepId, x.Title)).ToList(),
            Targets = content.TargetPointRevisionIds.Select(id => { var point = basis.Curriculum.Mappings.First(x => x.KnowledgePointRevisionId == id);
                return new EducationStudentTeachingPlanPoint(point.KnowledgePointCode, point.KnowledgePointName, point.RevisionNumber); }).ToList() };
    }
    [Authorize(EducationPermissions.TeachingPlanView), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationStudentTeachingPlanDto>> GetMyListAsync(PagedResultRequestDto input, Guid? schoolId = null)
    {
        await StudentAsync(); var take = Page(input); List<EducationTeachingPlan> rows;
        using (var listing = Uow.Begin(requiresNew: true, isTransactional: false)) {
            using var tracking = Repo<EducationTeachingPlan>().DisableTracking();
            rows = await AsyncExecuter.ToListAsync((await Repo<EducationTeachingPlan>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.StudentUserId == CurrentUser.Id && (schoolId == null || x.SchoolId == schoolId) && x.LastPublishedRevisionId != null)
                .OrderByDescending(x => x.CreationTime).ThenBy(x => x.Id).Take(1001)); await listing.CompleteAsync();
        }
        if (rows.Count > 1000) throw new BusinessException(EducationLearningReportErrors.Capacity);
        var result = new List<EducationStudentTeachingPlanDto>();
        foreach (var baseline in rows) {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            try { await StudentAccessAsync(baseline); } catch (AbpAuthorizationException) { continue; }
            var current = await RefAsync<EducationTeachingPlan>(baseline.Id) ?? throw Invalid(); result.Add(await StudentDtoAsync(current, false)); await uow.CompleteAsync();
        }
        return new(result.Count, result.Skip(input.SkipCount).Take(take).ToList());
    }
    [Authorize(EducationPermissions.TeachingPlanView), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudentTeachingPlanDto> OpenMyAsync(Guid planId, Guid expectedVersionId)
    {
        await StudentAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var plan = await RefAsync<EducationTeachingPlan>(planId) ?? throw new AbpAuthorizationException(); await StudentAccessAsync(plan);
        plan = await RefAsync<EducationTeachingPlan>(planId) ?? throw Invalid();
        if (plan.LastPublishedRevisionId == null) throw new AbpAuthorizationException();
        if (plan.PublicRevisionId != expectedVersionId) throw new AbpDbConcurrencyException();
        var result = await StudentDtoAsync(plan, true); await uow.CompleteAsync(); return result;
    }
}
