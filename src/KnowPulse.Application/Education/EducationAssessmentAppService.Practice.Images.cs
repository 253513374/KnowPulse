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
    private const long PracticeImageLimit = 5 * 1024 * 1024;
    private IBlobContainer<EducationPracticeImageBlobContainer> PracticeImages => LazyServiceProvider.LazyGetRequiredService<IBlobContainer<EducationPracticeImageBlobContainer>>();
    private static BusinessException PracticeImageUnavailable() => new(EducationPracticeErrors.ImageUnavailable);
    private static EducationPracticeImageDto PracticeImageDto(EducationPracticeImage row) => new()
    { Id = row.Id, FileName = row.FileName, ContentType = row.ContentType, Size = row.Size, ContentHash = row.ContentHash, IsUploaded = row.UploadedAtUtc.HasValue };
    private static void PracticeImageScope(EducationPracticeImage image, EducationQuestionPractice practice)
    { if (image.TenantId != practice.TenantId || image.PracticeId != practice.Id || image.StudentUserId != practice.StudentUserId) throw new AbpAuthorizationException(); }
    private async Task<byte[]?> PracticeStoredImageAsync(EducationPracticeImage image)
    {
        try {
            using var stream = await PracticeImages.GetOrNullAsync(image.BlobName); if (stream == null) return null;
            var bytes = await EducationAssessmentImageFormat.ReadStreamAsync(stream, PracticeImageLimit);
            if (bytes.LongLength != image.Size || EducationAssessmentImageFormat.Hash(bytes) != image.ContentHash) throw PracticeImageUnavailable();
            EducationAssessmentImageFormat.Detect(bytes, image.ContentType); return bytes;
        } catch (BusinessException) { throw; } catch (Exception error) when (error is not OperationCanceledException) { throw PracticeImageUnavailable(); }
    }
    private async Task<List<EducationPracticeImage>> PracticeSelectedImagesAsync(EducationQuestionPractice practice, EducationAssessmentResponseValue response)
    {
        var result = new List<EducationPracticeImage>();
        foreach (var reference in response.Images) {
            var image = await ReferenceAsync<EducationPracticeImage>(reference.AttachmentId) ?? throw PracticeImageUnavailable(); PracticeImageScope(image, practice);
            if (image.UploadedAtUtc == null || await PracticeStoredImageAsync(image) == null) throw PracticeImageUnavailable(); result.Add(image);
        }
        return result;
    }
    private async Task<EducationPracticeImage?> PracticeImageReceiptAsync(Guid id, Guid operationId, string? fingerprint = null)
    {
        var practice = await PracticeAccessAsync(id, true); var image = await ReferenceAsync<EducationPracticeImage>(operationId);
        if (image == null) return null; PracticeImageScope(image, practice);
        if (fingerprint != null && image.RequestFingerprint != fingerprint) throw new BusinessException(EducationPracticeErrors.Conflict);
        return image;
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationPracticeImageDto> UploadMyPracticeImageAsync(Guid id, Guid operationId, IRemoteStreamContent file)
    {
        await PracticeStudentAsync(); EducationRules.RequireId(operationId);
        using (var access = Uow.Begin(requiresNew: true, isTransactional: false)) { await PracticeAccessAsync(id, true); await access.CompleteAsync(); }
        var bytes = await EducationAssessmentImageFormat.ReadAsync(file, PracticeImageLimit);
        var type = EducationAssessmentImageFormat.Detect(bytes, file.ContentType); var name = EducationAssessmentImageFormat.FileName(file.FileName, type);
        var hash = EducationAssessmentImageFormat.Hash(bytes); var fingerprint = Hash(new { Command = "practice-image", id, operationId, name, type, Size = bytes.LongLength, hash });
        try {
            using var reserve = Uow.Begin(requiresNew: true, isTransactional: true); var practice = await PracticeAccessAsync(id, true);
            var prior = await PracticeImageReceiptAsync(id, operationId, fingerprint);
            if (prior?.UploadedAtUtc != null) { await reserve.CompleteAsync(); return PracticeImageDto(prior); }
            if (practice.SubmittedAtUtc.HasValue) throw PracticeInvalid(); await CurrentPracticeSourceAsync(practice, true, true);
            if (prior == null) {
                using var tracking = Repo<EducationQuestionPractice>().EnableTracking(); practice = await Repo<EducationQuestionPractice>().GetAsync(id);
                if (practice.SubmittedAtUtc.HasValue) throw PracticeInvalid();
                var images = await ReferencesAsync<EducationPracticeImage>(x => x.PracticeId == id && x.TenantId == CurrentTenant.Id);
                // Development upload protection follows correction tasks; retention/capacity policy remains separate.
                if (images.Count >= 40 || images.Sum(x => x.Size) + bytes.LongLength > 20 * 1024 * 1024) throw PracticeImageUnavailable();
                await Repo<EducationQuestionPractice>().UpdateManyAsync([practice], autoSave: true);
                await Repo<EducationPracticeImage>().InsertAsync(new(operationId, practice, name, type, bytes.LongLength, hash, fingerprint, DateTime.UtcNow), autoSave: true);
            }
            await reserve.CompleteAsync();
        } catch (Exception error) when (Recoverable(error)) {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            if (await PracticeImageReceiptAsync(id, operationId, fingerprint) == null) throw;
            await recovery.CompleteAsync();
        }
        EducationPracticeImage reserved;
        using (var read = Uow.Begin(requiresNew: true, isTransactional: false)) { reserved = await PracticeImageReceiptAsync(id, operationId, fingerprint) ?? throw PracticeImageUnavailable(); await read.CompleteAsync(); }
        try {
            if (await PracticeStoredImageAsync(reserved) == null) { using var stream = new MemoryStream(bytes, writable: false); await PracticeImages.SaveAsync(reserved.BlobName, stream, overrideExisting: false); }
        } catch (BusinessException) { throw; } catch (Exception error) when (error is not OperationCanceledException) {
            if (await PracticeStoredImageAsync(reserved) == null) throw PracticeImageUnavailable();
        }
        try {
            using var confirm = Uow.Begin(requiresNew: true, isTransactional: true); var practice = await PracticeAccessAsync(id, true);
            using var imageTracking = Repo<EducationPracticeImage>().EnableTracking(); var image = await Repo<EducationPracticeImage>().GetAsync(operationId); PracticeImageScope(image, practice);
            if (image.RequestFingerprint != fingerprint) throw new BusinessException(EducationPracticeErrors.Conflict);
            if (image.UploadedAtUtc == null) {
                if (practice.SubmittedAtUtc.HasValue) throw PracticeInvalid(); await CurrentPracticeSourceAsync(practice, true, true);
                using var rootTracking = Repo<EducationQuestionPractice>().EnableTracking(); practice = await Repo<EducationQuestionPractice>().GetAsync(id);
                if (practice.SubmittedAtUtc.HasValue) throw PracticeInvalid(); await Repo<EducationQuestionPractice>().UpdateManyAsync([practice], autoSave: true);
                if (await PracticeStoredImageAsync(image) == null) throw PracticeImageUnavailable(); image.Confirm(DateTime.UtcNow);
                await Repo<EducationPracticeImage>().UpdateManyAsync([image], autoSave: true);
            }
            await confirm.CompleteAsync(); return PracticeImageDto(image);
        } catch (Exception error) when (Recoverable(error)) {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false); var image = await PracticeImageReceiptAsync(id, operationId, fingerprint);
            if (image?.UploadedAtUtc == null) throw; await recovery.CompleteAsync(); return PracticeImageDto(image);
        }
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationPracticeImageDto?> GetMyPracticeImageOperationAsync(Guid id, Guid operationId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: false); var image = await PracticeImageReceiptAsync(id, operationId);
        await uow.CompleteAsync(); return image == null ? null : PracticeImageDto(image);
    }
    private async Task<IRemoteStreamContent> PracticeDownloadAsync(Guid id, Guid attachmentId, bool student)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: false); var practice = await PracticeAccessAsync(id, student);
        if (!student && (practice.ResponseJson == null || !EducationAssessmentResponseValue.Parse(practice.ResponseJson).Images.Any(x => x.AttachmentId == attachmentId))) throw new AbpAuthorizationException();
        var image = await ReferenceAsync<EducationPracticeImage>(attachmentId) ?? throw new AbpAuthorizationException(); PracticeImageScope(image, practice);
        if (image.UploadedAtUtc == null) throw PracticeImageUnavailable(); var bytes = await PracticeStoredImageAsync(image) ?? throw PracticeImageUnavailable();
        await PracticeAccessAsync(id, student); await uow.CompleteAsync(); return new RemoteStreamContent(new MemoryStream(bytes, writable: false), image.FileName, image.ContentType);
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual Task<IRemoteStreamContent> DownloadMyPracticeImageAsync(Guid id, Guid attachmentId) => PracticeDownloadAsync(id, attachmentId, true);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual Task<IRemoteStreamContent> DownloadPracticeImageAsync(Guid id, Guid attachmentId) => PracticeDownloadAsync(id, attachmentId, false);
}
