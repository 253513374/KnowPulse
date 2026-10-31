using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationTeachingPlanAppService
{
    private EducationMaterialDelivery Materials => LazyServiceProvider.LazyGetRequiredService<EducationMaterialDelivery>();
    private static EducationMaterialAuthority MaterialAuthority(EducationTeachingPlan plan) => new(plan.TenantId, plan.OwnerUserId, plan.SchoolId);
    private async Task MaterialTeacherAsync() { await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary); await TeacherAsync(); }
    private async Task ValidateMaterialsAsync(EducationTeachingPlan plan, EducationTeachingPlanContent content)
    {
        if (content == null) throw Invalid(); content.Validate(); if (content.Materials.Count == 0) return;
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        foreach (var material in content.Materials) if (!await Materials.AvailableAsync(MaterialAuthority(plan), material)) throw new BusinessException(EducationTeachingPlanErrors.MaterialUnavailable);
    }
    private async Task<bool> ActivityMaterialsAvailableAsync(EducationTeachingActivity activity)
    {
        var version = await RefAsync<EducationTeachingPlanRevision>(activity.PlanRevisionId) ?? throw Invalid();
        var references = version.Content()!.Materials.Where(x => x.StepId == activity.StepId).ToList(); if (references.Count == 0) return true;
        var plan = await RefAsync<EducationTeachingPlan>(activity.PlanId) ?? throw Invalid();
        foreach (var material in references) if (!await Materials.AvailableAsync(MaterialAuthority(plan), material)) return false;
        return true;
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationMaterialSourceDto>> GetMaterialSourcesAsync(Guid planId, GetEducationMaterialSourcesDto input)
    {
        await MaterialTeacherAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var plan = await OwnAsync(planId, write: true); var result = await Materials.SourcesAsync(MaterialAuthority(plan), input); await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationMaterialSourcePreviewDto> PreviewMaterialSourceAsync(Guid planId, EducationMaterialSourceDto input)
    {
        await MaterialTeacherAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var plan = await OwnAsync(planId, write: true); var result = await Materials.PreviewAsync(MaterialAuthority(plan), input); await uow.CompleteAsync(); return result;
    }
    private async Task<(EducationTeachingPlan Plan, EducationTeachingPlanMaterial Material)> MaterialContextAsync(Guid ownerId, Guid versionId, Guid materialId, bool student)
    {
        EducationTeachingPlan plan; EducationTeachingPlanRevision version;
        if (student) {
            await CheckMvpPermissionAsync(EducationPermissions.LearningParticipate); await StudentAsync(); var (activity, current) = await ActivityAccessAsync(ownerId, true); plan = current;
            if (plan.PublishedRevisionId != activity.PlanRevisionId || activity.FirstProvidedAtUtc == null) throw new AbpAuthorizationException();
            if (!await CanReadActivityMaterialsAsync(activity, plan)) throw new AbpAuthorizationException();
            version = await RefAsync<EducationTeachingPlanRevision>(activity.PlanRevisionId) ?? throw Invalid();
            if (await AvailabilityAsync(plan, version, student: true) != EducationTeachingPlanAvailability.Available) throw new AbpAuthorizationException();
            if (version.Basis()!.TaskIds.Count > 0) await CheckMvpPermissionAsync(EducationPermissions.AssessmentParticipate);
            return (plan, version.Content()!.Materials.SingleOrDefault(x => x.Id == materialId && x.StepId == activity.StepId) ?? throw new AbpAuthorizationException());
        }
        await MaterialTeacherAsync(); plan = await OwnAsync(ownerId, write: true); version = await RefAsync<EducationTeachingPlanRevision>(versionId) ?? throw new AbpAuthorizationException();
        if (version.PlanId != plan.Id || version.TenantId != plan.TenantId || version.OwnerUserId != plan.OwnerUserId || version.Content() == null) throw new AbpAuthorizationException();
        return (plan, version.Content()!.Materials.SingleOrDefault(x => x.Id == materialId) ?? throw new AbpAuthorizationException());
    }
    private async Task<EducationMaterialContentDto> ReadMaterialAsync(Guid ownerId, Guid versionId, Guid materialId, bool student)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (plan, material) = await MaterialContextAsync(ownerId, versionId, materialId, student);
        var result = await Materials.ReadAsync(MaterialAuthority(plan), material); await uow.CompleteAsync(); return result;
    }
    private async Task<IRemoteStreamContent> DownloadMaterialAsync(Guid ownerId, Guid versionId, Guid materialId, Guid attachmentId, bool student)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (plan, material) = await MaterialContextAsync(ownerId, versionId, materialId, student);
        var file = await Materials.DownloadAsync(MaterialAuthority(plan), material, attachmentId, () => MaterialContextAsync(ownerId, versionId, materialId, student));
        try { await uow.CompleteAsync(); return file; }
        catch { file.Dispose(); throw; }
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationMaterialContentDto> GetTeacherMaterialAsync(Guid planId, Guid revisionId, Guid materialId) => ReadMaterialAsync(planId, revisionId, materialId, false);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual Task<IRemoteStreamContent> DownloadTeacherMaterialAsync(Guid planId, Guid revisionId, Guid materialId, Guid attachmentId) => DownloadMaterialAsync(planId, revisionId, materialId, attachmentId, false);
    [Authorize(EducationPermissions.TeachingPlanView), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationMaterialContentDto> GetMyMaterialAsync(Guid activityId, Guid materialId) => ReadMaterialAsync(activityId, Guid.Empty, materialId, true);
    [Authorize(EducationPermissions.TeachingPlanView), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual Task<IRemoteStreamContent> DownloadMyMaterialAsync(Guid activityId, Guid materialId, Guid attachmentId) => DownloadMaterialAsync(activityId, Guid.Empty, materialId, attachmentId, true);
}
