using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationAssessmentAppService
{
    private const long CorrectionImageLimit = 5 * 1024 * 1024;
    private IBlobContainer<EducationCorrectionImageBlobContainer> CorrectionImages => LazyServiceProvider.LazyGetRequiredService<IBlobContainer<EducationCorrectionImageBlobContainer>>();
    private static BusinessException CorrectionImageUnavailable() => new(EducationCorrectionErrors.ImageUnavailable);
    private static EducationCorrectionImageDto CorrectionImage(EducationCorrectionImage value) => new() { Id = value.Id, FileName = value.FileName, ContentType = value.ContentType, Size = value.Size, ContentHash = value.ContentHash, IsUploaded = value.UploadedAtUtc.HasValue };
    private async Task<byte[]?> CorrectionStoredImageAsync(EducationCorrectionImage value)
    {
        try {
            using var stream = await CorrectionImages.GetOrNullAsync(value.BlobName); if (stream == null) return null;
            var bytes = await EducationAssessmentImageFormat.ReadStreamAsync(stream, CorrectionImageLimit);
            if (bytes.LongLength != value.Size || EducationAssessmentImageFormat.Hash(bytes) != value.ContentHash) throw CorrectionImageUnavailable();
            EducationAssessmentImageFormat.Detect(bytes, value.ContentType); return bytes;
        } catch (BusinessException) { throw; } catch (Exception error) when (error is not OperationCanceledException) { throw CorrectionImageUnavailable(); }
    }
    private static void CorrectionImageScope(EducationCorrectionImage image, EducationCorrectionDelivery delivery)
    { if (image.TenantId != delivery.TenantId || image.DeliveryId != delivery.Id || image.StudentUserId != delivery.StudentUserId) throw new AbpAuthorizationException(); }
    private async Task<List<EducationCorrectionImage>> CorrectionSelectedImagesAsync(EducationCorrectionDelivery delivery, EducationCorrectionContent content)
    {
        var values = new List<EducationCorrectionImage>();
        foreach (var reference in content.Images) {
            var image = await ReferenceAsync<EducationCorrectionImage>(reference.AttachmentId) ?? throw CorrectionImageUnavailable(); CorrectionImageScope(image, delivery);
            if (image.UploadedAtUtc == null || await CorrectionStoredImageAsync(image) == null) throw CorrectionImageUnavailable(); values.Add(image);
        }
        return values;
    }
    private async Task EnsureCorrectionImageWriteAsync(EducationCorrectionDelivery delivery, EducationCorrectionTask root, EducationCorrectionTaskRevision publication)
    {
        var gate = await CorrectionGateAsync(root); if (delivery.FirstProvidedAtUtc == null || await CorrectionAvailabilityAsync(root, publication, gate) != EducationCorrectionAvailability.Available) throw new BusinessException(EducationCorrectionErrors.Closed);
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationCorrectionImageDto> UploadMyCorrectionImageAsync(Guid deliveryId, Guid operationId, IRemoteStreamContent file)
    {
        await CorrectionStudentAsync(); EducationRules.RequireId(operationId);
        byte[] bytes; string type, name, fingerprint;
        using (var reserve = Uow.Begin(requiresNew: true, isTransactional: true)) {
            var (delivery, root, publication) = await CorrectionDeliveryAsync(deliveryId, true);
            bytes = await EducationAssessmentImageFormat.ReadAsync(file, CorrectionImageLimit); type = EducationAssessmentImageFormat.Detect(bytes, file.ContentType); name = EducationAssessmentImageFormat.FileName(file.FileName, type);
            var hash = EducationAssessmentImageFormat.Hash(bytes); fingerprint = Hash(new { Command = "correction-image", deliveryId, operationId, name, type, Size = bytes.LongLength, hash });
            var prior = await ReferenceAsync<EducationCorrectionImage>(operationId);
            if (prior != null) { CorrectionImageScope(prior, delivery); if (prior.RequestFingerprint != fingerprint) throw new BusinessException(EducationCorrectionErrors.Conflict);
                if (prior.UploadedAtUtc != null) { await reserve.CompleteAsync(); return CorrectionImage(prior); } }
            await EnsureCorrectionImageWriteAsync(delivery, root, publication);
            if (prior == null) {
                var rows = await ReferencesAsync<EducationCorrectionImage>(x => x.DeliveryId == delivery.Id && x.TenantId == CurrentTenant.Id);
                // Development protection limits follow assessment uploads; school retention/capacity policy remains separate.
                if (rows.Count >= 40 || rows.Sum(x => x.Size) + bytes.LongLength > 20 * 1024 * 1024) throw CorrectionImageUnavailable();
                await Repo<EducationCorrectionImage>().InsertAsync(new(operationId, delivery, name, type, bytes.LongLength, hash, fingerprint, DateTime.UtcNow), autoSave: true);
            }
            await reserve.CompleteAsync();
        }
        EducationCorrectionImage reserved;
        using (var fetch = Uow.Begin(requiresNew: true, isTransactional: false)) { var (delivery, _, _) = await CorrectionDeliveryAsync(deliveryId, true);
            reserved = await ReferenceAsync<EducationCorrectionImage>(operationId) ?? throw CorrectionImageUnavailable(); CorrectionImageScope(reserved, delivery);
            if (reserved.RequestFingerprint != fingerprint) throw new BusinessException(EducationCorrectionErrors.Conflict); await fetch.CompleteAsync(); }
        try {
            if (await CorrectionStoredImageAsync(reserved) == null) { using var stream = new MemoryStream(bytes, writable: false); await CorrectionImages.SaveAsync(reserved.BlobName, stream, overrideExisting: false); }
        } catch (BusinessException) { throw; } catch (Exception error) when (error is not OperationCanceledException) {
            if (await CorrectionStoredImageAsync(reserved) == null) throw CorrectionImageUnavailable();
        }
        using var confirm = Uow.Begin(requiresNew: true, isTransactional: true); var (currentDelivery, currentRoot, currentPublication) = await CorrectionDeliveryAsync(deliveryId, true);
        using var tracking = Repo<EducationCorrectionImage>().EnableTracking(); var value = await Repo<EducationCorrectionImage>().GetAsync(operationId); CorrectionImageScope(value, currentDelivery);
        if (value.RequestFingerprint != fingerprint) throw new BusinessException(EducationCorrectionErrors.Conflict);
        if (value.UploadedAtUtc == null) { await EnsureCorrectionImageWriteAsync(currentDelivery, currentRoot, currentPublication); if (await CorrectionStoredImageAsync(value) == null) throw CorrectionImageUnavailable();
            value.Confirm(DateTime.UtcNow); await Repo<EducationCorrectionImage>().UpdateManyAsync([value], autoSave: true); }
        await confirm.CompleteAsync(); return CorrectionImage(value);
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationCorrectionImageDto?> GetMyCorrectionImageOperationAsync(Guid deliveryId, Guid operationId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (delivery, _, _) = await CorrectionDeliveryAsync(deliveryId, true);
        var value = await ReferenceAsync<EducationCorrectionImage>(operationId); if (value != null) CorrectionImageScope(value, delivery); await uow.CompleteAsync(); return value == null ? null : CorrectionImage(value);
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<IRemoteStreamContent> DownloadMyCorrectionImageAsync(Guid deliveryId, Guid attachmentId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (delivery, _, _) = await CorrectionDeliveryAsync(deliveryId, true);
        var value = await ReferenceAsync<EducationCorrectionImage>(attachmentId) ?? throw new AbpAuthorizationException(); CorrectionImageScope(value, delivery);
        if (value.UploadedAtUtc == null) throw CorrectionImageUnavailable(); var bytes = await CorrectionStoredImageAsync(value) ?? throw CorrectionImageUnavailable();
        await CorrectionDeliveryAsync(deliveryId, true); await uow.CompleteAsync(); return new RemoteStreamContent(new MemoryStream(bytes, writable: false), value.FileName, value.ContentType);
    }
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<IRemoteStreamContent> DownloadCorrectionImageAsync(Guid submissionId, Guid attachmentId)
    {
        await CorrectionTeacherAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var submission = await ReferenceAsync<EducationCorrectionSubmission>(submissionId) ?? throw new AbpAuthorizationException(); var (delivery, _, _) = await CorrectionDeliveryAsync(submission.DeliveryId, false);
        if (submission.TenantId != delivery.TenantId || submission.OwnerUserId != delivery.OwnerUserId || submission.StudentUserId != delivery.StudentUserId || !submission.Content().Images.Any(x => x.AttachmentId == attachmentId)) throw new AbpAuthorizationException();
        var value = await ReferenceAsync<EducationCorrectionImage>(attachmentId) ?? throw new AbpAuthorizationException(); CorrectionImageScope(value, delivery);
        if (value.UploadedAtUtc == null) throw CorrectionImageUnavailable(); var bytes = await CorrectionStoredImageAsync(value) ?? throw CorrectionImageUnavailable();
        await CorrectionDeliveryAsync(delivery.Id, false); await uow.CompleteAsync(); return new RemoteStreamContent(new MemoryStream(bytes, writable: false), value.FileName, value.ContentType);
    }
}
