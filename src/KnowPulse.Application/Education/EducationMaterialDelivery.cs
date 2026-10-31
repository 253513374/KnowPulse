using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace KnowPulse.Education;

internal sealed record EducationMaterialAuthority(Guid? TenantId, Guid OwnerUserId, Guid SchoolId);

// Each calling use case first checks its own recipient/publication and owns the UoW.
[RemoteService(false)]
public class EducationMaterialDelivery : KnowPulseAppService
{
    private sealed record Source(KnowledgeVersion Version, SchoolKnowledgeShare? Share);
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private async Task<T?> RefAsync<T>(Guid id) where T : class, IEntity<Guid> { using var tracking = Repo<T>().DisableTracking(); return await Repo<T>().FindAsync(id); }
    private void Scope(EducationMaterialAuthority scope, bool teacher = false)
    {
        if (!CurrentUser.IsAuthenticated || scope.TenantId != CurrentTenant.Id || scope.OwnerUserId == Guid.Empty || scope.SchoolId == Guid.Empty ||
            teacher && scope.OwnerUserId != CurrentUser.Id) throw new AbpAuthorizationException();
    }
    private async Task<Source?> SourceAsync(EducationMaterialAuthority scope, IEducationLearningMaterial material, bool download)
    {
        Scope(scope);
        var item = await RefAsync<KnowledgeItem>(material.KnowledgeItemId); var version = await RefAsync<KnowledgeVersion>(material.KnowledgeVersionId);
        if (item == null || version == null || item.TenantId != scope.TenantId || version.TenantId != scope.TenantId || !item.IsActive ||
            version.KnowledgeItemId != item.Id || version.Status != KnowledgeVersionStatus.Published || item.LibraryId == null || item.OwnerUserId == null) return null;
        var library = await RefAsync<KnowledgeLibrary>(item.LibraryId.Value); var teacher = await RefAsync<IdentityUser>(scope.OwnerUserId);
        if (library == null || library.TenantId != scope.TenantId || library.OwnerUserId != item.OwnerUserId || teacher == null || teacher.TenantId != scope.TenantId || !teacher.IsActive) return null;
        if (material.SchoolKnowledgeShareId == null) return item.OwnerUserId == scope.OwnerUserId ? new(version, null) : null;
        var query = await LazyServiceProvider.LazyGetRequiredService<KnowledgeSchoolSharingAppService>()
            .GetStudentMaterialSharesQueryAsync(scope.OwnerUserId, scope.SchoolId, download);
        var share = await AsyncExecuter.FirstOrDefaultAsync(query.Where(x => x.Id == material.SchoolKnowledgeShareId && x.KnowledgeItemId == item.Id && x.KnowledgeVersionId == version.Id));
        return share == null ? null : new(version, share);
    }
    private async Task<List<KnowledgeAttachment>?> AttachmentsAsync(EducationMaterialAuthority scope, IEducationLearningMaterial material)
    {
        using var tracking = Repo<KnowledgeAttachment>().DisableTracking();
        var rows = await Repo<KnowledgeAttachment>().GetListAsync(x => x.TenantId == scope.TenantId && x.KnowledgeVersionId == material.KnowledgeVersionId && material.AttachmentIds.Contains(x.Id));
        return rows.Count == material.AttachmentIds.Count ? rows.OrderBy(x => material.AttachmentIds.IndexOf(x.Id)).ToList() : null;
    }
    internal async Task<bool> AvailableAsync(EducationMaterialAuthority scope, IEducationLearningMaterial material)
    {
        var source = await SourceAsync(scope, material, material.AttachmentIds.Count > 0);
        return source != null && (material.AttachmentIds.Count > 0 || !string.IsNullOrWhiteSpace(source.Version.ContentText)) && await AttachmentsAsync(scope, material) != null;
    }
    private static EducationMaterialAttachmentDto Attachment(KnowledgeAttachment row) => new() { Id = row.Id, FileName = row.FileName, ContentType = row.ContentType, Size = row.Size };
    internal async Task<PagedResultDto<EducationMaterialSourceDto>> SourcesAsync(EducationMaterialAuthority scope, GetEducationMaterialSourcesDto input)
    {
        Scope(scope, teacher: true);
        if (input.SkipCount < 0 || input.MaxResultCount is < 1 or > 100 || input.Filter?.Length > 128) throw new BusinessException(EducationTeachingPlanErrors.Invalid);
        var filter = input.Filter?.Trim() ?? ""; List<EducationMaterialSourceDto> result; long count;
        if (input.SchoolShared) {
            var query = (await LazyServiceProvider.LazyGetRequiredService<KnowledgeSchoolSharingAppService>().GetAvailableSharesQueryAsync(SchoolKnowledgeShareUses.AssignToStudents, scope.SchoolId))
                .Where(x => x.OwnerUserId != scope.OwnerUserId && (filter == "" || x.Title.Contains(filter)));
            count = await AsyncExecuter.LongCountAsync(query); result = [];
            foreach (var row in await AsyncExecuter.ToListAsync(query.OrderBy(x => x.Title).ThenBy(x => x.Id).Skip(input.SkipCount).Take(input.MaxResultCount)))
                result.Add(new() { Id = row.KnowledgeVersionId, KnowledgeItemId = row.KnowledgeItemId, SchoolKnowledgeShareId = row.Id, Title = row.Title,
                    VersionNumber = (await RefAsync<KnowledgeVersion>(row.KnowledgeVersionId) ?? throw new BusinessException(EducationTeachingPlanErrors.Invalid)).VersionNumber });
        } else {
            var versions = await Repo<KnowledgeVersion>().GetQueryableAsync(); var items = await Repo<KnowledgeItem>().GetQueryableAsync(); var libraries = await Repo<KnowledgeLibrary>().GetQueryableAsync();
            var query = from version in versions join item in items on version.KnowledgeItemId equals item.Id join library in libraries on item.LibraryId equals (Guid?)library.Id
                where version.TenantId == scope.TenantId && item.TenantId == scope.TenantId && library.TenantId == scope.TenantId && item.IsActive &&
                    item.OwnerUserId == scope.OwnerUserId && library.OwnerUserId == scope.OwnerUserId && version.Status == KnowledgeVersionStatus.Published &&
                    item.CurrentPublishedVersionId == version.Id && version.IsCurrentPublished && (filter == "" || version.Title.Contains(filter))
                select new EducationMaterialSourceDto { Id = version.Id, KnowledgeItemId = item.Id, Title = version.Title, VersionNumber = version.VersionNumber };
            count = await AsyncExecuter.LongCountAsync(query); result = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.Title).ThenBy(x => x.Id).Skip(input.SkipCount).Take(input.MaxResultCount));
        }
        return new(count, result);
    }
    internal async Task<EducationMaterialSourcePreviewDto> PreviewAsync(EducationMaterialAuthority scope, EducationMaterialSourceDto input)
    {
        Scope(scope, teacher: true);
        var material = new EducationStudyMaterial { KnowledgeItemId = input.KnowledgeItemId, KnowledgeVersionId = input.Id, SchoolKnowledgeShareId = input.SchoolKnowledgeShareId };
        var source = await SourceAsync(scope, material, false) ?? throw new AbpAuthorizationException();
        var attachments = source.Share == null || source.Share.Allows(SchoolKnowledgeShareUses.Download)
            ? await AsyncExecuter.ToListAsync((await Repo<KnowledgeAttachment>().GetQueryableAsync()).Where(x => x.KnowledgeVersionId == input.Id && x.TenantId == scope.TenantId)
                .OrderBy(x => x.FileName).ThenBy(x => x.Id).Take(201)) : [];
        if (attachments.Count > 200) throw new BusinessException(EducationLearningReportErrors.Capacity);
        return new() { Source = new() { Id = source.Version.Id, KnowledgeItemId = source.Version.KnowledgeItemId, SchoolKnowledgeShareId = source.Share?.Id,
            Title = source.Share?.Title ?? source.Version.Title, VersionNumber = source.Version.VersionNumber }, ContentText = source.Version.ContentText,
            Attachments = attachments.Select(Attachment).ToList() };
    }
    internal async Task<EducationMaterialContentDto> ReadAsync(EducationMaterialAuthority scope, IEducationLearningMaterial material)
    {
        Scope(scope); var result = new EducationMaterialContentDto { Id = material.Id, Title = material.Title };
        var attachments = await AttachmentsAsync(scope, material); var source = await SourceAsync(scope, material, material.AttachmentIds.Count > 0);
        if (source != null && attachments != null && (attachments.Count > 0 || !string.IsNullOrWhiteSpace(source.Version.ContentText))) {
            result.IsAvailable = true; result.VersionNumber = source.Version.VersionNumber; result.ContentText = source.Version.ContentText; result.Attachments = attachments.Select(Attachment).ToList();
        }
        return result;
    }
    internal async Task<IRemoteStreamContent> DownloadAsync(EducationMaterialAuthority scope, IEducationLearningMaterial material, Guid attachmentId, Func<Task> recheckRecipient)
    {
        Scope(scope); if (!material.AttachmentIds.Contains(attachmentId)) throw new AbpAuthorizationException();
        var attachments = await AttachmentsAsync(scope, material);
        if (attachments == null || await SourceAsync(scope, material, true) == null) throw new BusinessException(EducationTeachingPlanErrors.MaterialUnavailable);
        var file = attachments.Single(x => x.Id == attachmentId); Stream stream;
        try { stream = await LazyServiceProvider.LazyGetRequiredService<IBlobContainer<KnowledgeAttachmentBlobContainer>>().GetAsync(file.BlobName); }
        catch (Exception error) when (error is not OperationCanceledException) { throw new BusinessException(EducationTeachingPlanErrors.MaterialUnavailable); }
        try {
            await recheckRecipient();
            if (!await AvailableAsync(scope, material)) throw new BusinessException(EducationTeachingPlanErrors.MaterialUnavailable);
            return new RemoteStreamContent(stream, file.FileName, file.ContentType, file.Size);
        } catch { await stream.DisposeAsync(); throw; }
    }
}
