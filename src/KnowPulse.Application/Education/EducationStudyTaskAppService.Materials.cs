using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationStudyTaskAppService
{
    private async Task MaterialTeacherAsync() { await TeacherAsync(); await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary); }
    [Authorize(EducationPermissions.LearningManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationMaterialSourceDto>> GetMaterialSourcesAsync(Guid taskId, GetEducationMaterialSourcesDto input)
    {
        await MaterialTeacherAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var task = await OwnAsync(taskId);
        var result = await MaterialDelivery.SourcesAsync(Authority(task), input); await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.LearningManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationMaterialSourcePreviewDto> PreviewMaterialSourceAsync(Guid taskId, EducationMaterialSourceDto input)
    {
        await MaterialTeacherAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var task = await OwnAsync(taskId);
        var result = await MaterialDelivery.PreviewAsync(Authority(task), input); await uow.CompleteAsync(); return result;
    }
    private async Task<(EducationStudyTask Task, EducationStudyMaterial Material)> MaterialContextAsync(Guid ownerId, Guid revisionId, Guid materialId, bool student, bool acquireBarrier = true)
    {
        EducationStudyTask task; EducationStudyTaskRevision revision;
        if (student) {
            var (row, current, publication) = await RecipientAsync(ownerId, true, acquireBarrier); task = current; revision = publication;
            var status = await AvailabilityAsync(task, revision);
            if (row.FirstProvidedAtUtc == null || status is not (EducationStudyTaskAvailability.Available or EducationStudyTaskAvailability.Overdue)) throw new AbpAuthorizationException();
        } else {
            await MaterialTeacherAsync(); task = await OwnAsync(ownerId); revision = await RefAsync<EducationStudyTaskRevision>(revisionId) ?? throw new AbpAuthorizationException();
            if (revision.TaskId != task.Id || revision.OwnerUserId != task.OwnerUserId || revision.TenantId != task.TenantId) throw new AbpAuthorizationException();
        }
        return (task, revision.Content()?.Materials.SingleOrDefault(x => x.Id == materialId) ?? throw new AbpAuthorizationException());
    }
    private async Task<EducationMaterialContentDto> ReadMaterialAsync(Guid ownerId, Guid revisionId, Guid materialId, bool student)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (task, material) = await MaterialContextAsync(ownerId, revisionId, materialId, student);
        var result = await MaterialDelivery.ReadAsync(Authority(task), material); await uow.CompleteAsync(); return result;
    }
    private async Task<IRemoteStreamContent> DownloadMaterialAsync(Guid ownerId, Guid revisionId, Guid materialId, Guid attachmentId, bool student)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (task, material) = await MaterialContextAsync(ownerId, revisionId, materialId, student);
        var file = await MaterialDelivery.DownloadAsync(Authority(task), material, attachmentId, () => MaterialContextAsync(ownerId, revisionId, materialId, student));
        try { await uow.CompleteAsync(); return file; } catch { file.Dispose(); throw; }
    }
    [Authorize(EducationPermissions.LearningManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationMaterialContentDto> GetTeacherMaterialAsync(Guid taskId, Guid revisionId, Guid materialId) => ReadMaterialAsync(taskId, revisionId, materialId, false);
    [Authorize(EducationPermissions.LearningManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual Task<IRemoteStreamContent> DownloadTeacherMaterialAsync(Guid taskId, Guid revisionId, Guid materialId, Guid attachmentId) => DownloadMaterialAsync(taskId, revisionId, materialId, attachmentId, false);
    [Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationMaterialContentDto> GetMyMaterialAsync(Guid recipientId, Guid materialId) => ReadMaterialAsync(recipientId, Guid.Empty, materialId, true);
    [Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual Task<IRemoteStreamContent> DownloadMyMaterialAsync(Guid recipientId, Guid materialId, Guid attachmentId) => DownloadMaterialAsync(recipientId, Guid.Empty, materialId, attachmentId, true);
}
