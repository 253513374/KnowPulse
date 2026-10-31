using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationAssessmentAppService
{
    private EducationAssessmentSubmissionProcessor Submissions => LazyServiceProvider.LazyGetRequiredService<EducationAssessmentSubmissionProcessor>();
    private IBlobContainer<EducationAssessmentImageBlobContainer> Images => LazyServiceProvider.LazyGetRequiredService<IBlobContainer<EducationAssessmentImageBlobContainer>>();
    private EducationAssessmentResponseOptions ResponseLimits
    {
        get
        {
            var value = LazyServiceProvider.LazyGetRequiredService<IOptions<EducationAssessmentResponseOptions>>().Value;
            value.EnsureValid();
            return value;
        }
    }

    [Authorize(EducationPermissions.AssessmentParticipate)]
    public virtual async Task<EducationStudentAssessmentAttemptDto> GetAttemptAsync(Guid attemptId)
    {
        await StudentAsync();
        var state = await StudentAttemptStateAsync(attemptId);
        return await MapAttemptAsync(state.Attempt, state.Revision, allowClosed: true);
    }

    [Authorize(EducationPermissions.AssessmentParticipate)]
    public virtual async Task<EducationAssessmentResponseRevisionDto> SaveResponseAsync(Guid attemptId, SaveEducationAssessmentResponseDto input)
    {
        await StudentAsync();
        EducationRules.RequireId(input.OperationId);
        EducationRules.RequireId(input.PaperQuestionId);
        if (input.Response == null) throw ResponseInvalid();
        var json = input.Response.ToCanonicalJson();
        var fingerprint = Hash(new { attemptId, input.PaperQuestionId, input.PaperSnapshotVersion, input.ExpectedResponseSetVersion, Response = json });
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            await StudentAttemptStateAsync(attemptId);
            var existing = await ResponseOperationAsync(attemptId, input.OperationId, fingerprint);
            if (existing != null) return await CompleteAsync(uow, () => Task.FromResult(MapResponse(existing)));
            var state = await StudentAttemptStateAsync(attemptId, forUpdate: true);
            var question = await AnswerQuestionAsync(state.Attempt, input.PaperQuestionId, input.PaperSnapshotVersion);
            var attachments = await ReferencesAsync<EducationAssessmentAnswerAttachment>(x => x.AttemptId == attemptId &&
                x.PaperQuestionId == question.Id && x.TenantId == CurrentTenant.Id && x.UploadedAtUtc.HasValue);
            input.Response.ValidateForQuestion(question.Type, question.Type == QuestionType.TrueFalse ? ["true", "false"] :
                Parse<List<ExamPaperQuestionOptionDto>>(question.OptionsJson).Select(x => x.OptionKey).ToList(),
                question.ScoringDefinitionJson == null ? [] : QuestionScoringDefinition.Parse(question.ScoringDefinitionJson).Blanks.Select(x => x.Id).ToList(),
                attachments.Select(x => x.Id).ToList());
            // A cutoff worker only contests the attempt. Take its lock before reading
            // the write instant, including when an earlier lock holder rolled back.
            await Repo<EducationAssessmentAttempt>().UpdateManyAsync([state.Attempt], autoSave: true);
            var now = DateTime.UtcNow;
            state.Attempt.EnsureCanEdit(now, input.ExpectedResponseSetVersion);
            var revision = new EducationAssessmentResponseRevision(input.OperationId, state.Attempt, question.Id, input.PaperSnapshotVersion,
                input.ExpectedResponseSetVersion, input.Response, fingerprint, now);
            await Repo<EducationAssessmentResponseRevision>().InsertAsync(revision, autoSave: true);
            using var tracking = Repo<EducationAssessmentCurrentResponse>().EnableTracking();
            var current = (await Repo<EducationAssessmentCurrentResponse>().GetListAsync(x => x.AttemptId == attemptId && x.PaperQuestionId == question.Id)).SingleOrDefault();
            if (current == null) await Repo<EducationAssessmentCurrentResponse>().InsertAsync(new EducationAssessmentCurrentResponse(GuidGenerator.Create(), revision), autoSave: true);
            else
            {
                current.Advance(revision);
                await Repo<EducationAssessmentCurrentResponse>().UpdateManyAsync([current], autoSave: true);
            }
            state.Attempt.RecordResponse(revision, DateTime.UtcNow);
            await Repo<EducationAssessmentAttempt>().UpdateManyAsync([state.Attempt], autoSave: true);
            return await CompleteAsync(uow, () => Task.FromResult(MapResponse(revision)));
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            await StudentAttemptStateAsync(attemptId);
            var winner = await ResponseOperationAsync(attemptId, input.OperationId, fingerprint);
            if (winner == null) throw;
            return await CompleteAsync(recovery, () => Task.FromResult(MapResponse(winner)));
        }
    }

    [Authorize(EducationPermissions.AssessmentParticipate)]
    public virtual async Task<EducationAssessmentResponseRevisionDto?> GetResponseRevisionAsync(Guid attemptId, Guid operationId)
    {
        await StudentAsync();
        EducationRules.RequireId(operationId);
        await StudentAttemptStateAsync(attemptId);
        var revision = await ResponseOperationAsync(attemptId, operationId);
        return revision == null ? null : MapResponse(revision);
    }

    [Authorize(EducationPermissions.AssessmentParticipate)]
    public virtual async Task<EducationAssessmentSubmissionDto> SubmitAsync(Guid attemptId, SubmitEducationAssessmentDto input)
    {
        await StudentAsync();
        EducationRules.RequireId(input.OperationId);
        var fingerprint = input.ExpectedPreparationFingerprint == null
            ? Hash(new { attemptId, input.PaperSnapshotVersion, input.ExpectedResponseSetVersion, input.ConfirmSavedOnly })
            : Hash(new { attemptId, input.PaperSnapshotVersion, input.ExpectedResponseSetVersion, input.ConfirmSavedOnly, input.ExpectedPreparationFingerprint });
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var state = await StudentAttemptStateAsync(attemptId);
            var existing = await ReferenceAsync<EducationAssessmentSubmission>(input.OperationId);
            if (existing != null)
            {
                EnsureOwnSubmission(state.Attempt, existing);
                if (existing.RequestFingerprint != fingerprint || state.Attempt.SubmissionId != existing.Id) throw Conflict();
                return await CompleteAsync(uow, () => Task.FromResult(MapSubmission(existing)));
            }
            if (state.Attempt.SubmissionId.HasValue)
                return await CompleteAsync(uow, async () => MapSubmission(await OwnSubmissionAsync(state.Attempt)));
            state = await StudentAttemptStateAsync(attemptId, forUpdate: true);
            var paper = await ReferenceAsync<ExamPaper>(state.Attempt.ExamPaperId);
            if (paper == null || paper.TenantId != CurrentTenant.Id || paper.SnapshotVersion != input.PaperSnapshotVersion) throw ResponseInvalid();
            await Repo<EducationAssessmentAttempt>().UpdateManyAsync([state.Attempt], autoSave: true);
            if (DateTime.UtcNow < state.Attempt.ExpiresAtUtc) {
                var preview = await SubmissionPreviewAsync(state.Attempt);
                if (preview.ServerNowUtc < state.Attempt.ExpiresAtUtc) {
                    state.Attempt.EnsureCanEdit(preview.ServerNowUtc, input.ExpectedResponseSetVersion);
                    if (string.IsNullOrWhiteSpace(input.ExpectedPreparationFingerprint)) throw new BusinessException(EducationAssessmentErrorCodes.PreparationRequired);
                    if (preview.Fingerprint != input.ExpectedPreparationFingerprint) throw new BusinessException(EducationAssessmentErrorCodes.PreparationChanged);
                    if (!input.ConfirmSavedOnly && (preview.Questions.Any(x => x.IsUnanswered || x.UnansweredBlankCount > 0) || preview.UnlinkedImages.Count > 0))
                        throw new BusinessException(EducationAssessmentErrorCodes.IncompleteSubmission);
                }
            }
            var result = await Submissions.SealAsync(attemptId, input.OperationId, fingerprint, input.ExpectedResponseSetVersion, input.ConfirmSavedOnly);
            return await CompleteAsync(uow, () => Task.FromResult(MapSubmission(result)));
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            var state = await StudentAttemptStateAsync(attemptId);
            if (!state.Attempt.SubmissionId.HasValue) throw;
            var winner = await OwnSubmissionAsync(state.Attempt);
            if (winner.Id == input.OperationId && winner.RequestFingerprint != fingerprint) throw Conflict();
            return await CompleteAsync(recovery, () => Task.FromResult(MapSubmission(winner)));
        }
    }

    [Authorize(EducationPermissions.AssessmentParticipate)]
    public virtual async Task<EducationAssessmentSubmissionDto?> GetSubmissionAsync(Guid attemptId)
    {
        await StudentAsync();
        var state = await StudentAttemptStateAsync(attemptId);
        return state.Attempt.SubmissionId.HasValue ? MapSubmission(await OwnSubmissionAsync(state.Attempt)) : null;
    }

    [Authorize(EducationPermissions.AssessmentParticipate)]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentAnswerImageDto> UploadAnswerImageAsync(Guid attemptId,
        UploadEducationAssessmentAnswerImageDto input, IRemoteStreamContent file)
    {
        await StudentAsync();
        EducationRules.RequireId(input.OperationId);
        EducationRules.RequireId(input.PaperQuestionId);
        using (var check = Uow.Begin(requiresNew: true, isTransactional: false))
        {
            var state = await StudentAttemptStateAsync(attemptId);
            var question = await AnswerQuestionAsync(state.Attempt, input.PaperQuestionId);
            if (question.Type != QuestionType.Subjective) throw ResponseInvalid();
            await check.CompleteAsync();
        }
        var bytes = await EducationAssessmentImageFormat.ReadAsync(file, ResponseLimits.MaxImageBytes);
        var type = EducationAssessmentImageFormat.Detect(bytes, file.ContentType);
        var hash = EducationAssessmentImageFormat.Hash(bytes);
        if (!string.Equals(hash, input.ContentHash, StringComparison.OrdinalIgnoreCase)) throw ImageInvalid();
        var fileName = EducationAssessmentImageFormat.FileName(file.FileName, type);
        var fingerprint = Hash(new { attemptId, input.PaperQuestionId, FileName = fileName, ContentType = type, Size = bytes.LongLength, ContentHash = hash });
        EducationAssessmentAnswerAttachment pending;
        try
        {
            using var reserve = Uow.Begin(requiresNew: true, isTransactional: true);
            var state = await StudentAttemptStateAsync(attemptId);
            var existing = await ImageOperationAsync(attemptId, input.OperationId, fingerprint);
            if (existing?.IsUploaded == true) return await CompleteAsync(reserve, () => MapImageAsync(existing));
            state = await StudentAttemptStateAsync(attemptId, forUpdate: true);
            await Repo<EducationAssessmentAttempt>().UpdateManyAsync([state.Attempt], autoSave: true);
            state.Attempt.EnsureCanEdit(DateTime.UtcNow, state.Attempt.ResponseSetVersion);
            if (existing == null)
            {
                var reserved = await ReferencesAsync<EducationAssessmentAnswerAttachment>(x => x.AttemptId == attemptId && x.TenantId == CurrentTenant.Id);
                if (reserved.Count >= ResponseLimits.MaxUploadOperationsPerAttempt || reserved.Sum(x => x.Size) + bytes.LongLength > ResponseLimits.MaxAttemptImageBytes)
                    throw new BusinessException("KnowPulse:EducationAssessment:ImageLimitExceeded");
                pending = new EducationAssessmentAnswerAttachment(input.OperationId, state.Attempt, input.PaperQuestionId, fileName, type,
                    bytes.LongLength, hash, ImageBlobName(state.Attempt, input.OperationId), fingerprint, DateTime.UtcNow);
                await Repo<EducationAssessmentAnswerAttachment>().InsertAsync(pending, autoSave: true);
            }
            else pending = existing;
            await reserve.CompleteAsync();
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            await StudentAttemptStateAsync(attemptId);
            var reserved = await ImageOperationAsync(attemptId, input.OperationId, fingerprint);
            if (reserved == null) throw;
            pending = reserved;
            if (pending.IsUploaded) return await CompleteAsync(recovery, () => MapImageAsync(pending));
            await recovery.CompleteAsync();
        }
        // Blob writes are outside the reservation transaction. An exception does not prove a
        // write failed: retain pending metadata and the deterministic blob for exact recovery.
        if (await StoredImageAsync(pending) == null)
        {
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                await Images.SaveAsync(pending.BlobName, stream, overrideExisting: false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                if (await StoredImageAsync(pending) == null) throw ImageStorageFailed();
            }
        }
        if (await StoredImageAsync(pending) == null) throw ImageStorageFailed();
        try
        {
            using var confirm = Uow.Begin(requiresNew: true, isTransactional: true);
            var state = await StudentAttemptStateAsync(attemptId, forUpdate: true);
            using var tracking = Repo<EducationAssessmentAnswerAttachment>().EnableTracking();
            var attachment = await Repo<EducationAssessmentAnswerAttachment>().FindAsync(input.OperationId) ?? throw ImageInvalid();
            EnsureImageOperation(state.Attempt, attachment, fingerprint);
            if (!attachment.IsUploaded)
            {
                await Repo<EducationAssessmentAttempt>().UpdateManyAsync([state.Attempt], autoSave: true);
                attachment.MarkUploaded(state.Attempt, DateTime.UtcNow);
                await Repo<EducationAssessmentAnswerAttachment>().UpdateManyAsync([attachment], autoSave: true);
            }
            return await CompleteAsync(confirm, () => MapImageAsync(attachment));
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            await StudentAttemptStateAsync(attemptId);
            var winner = await ImageOperationAsync(attemptId, input.OperationId, fingerprint);
            if (winner?.IsUploaded != true) throw;
            return await CompleteAsync(recovery, () => MapImageAsync(winner));
        }
    }

    [Authorize(EducationPermissions.AssessmentParticipate)]
    public virtual async Task<EducationAssessmentAnswerImagePolicyDto> GetAnswerImagePolicyAsync(Guid attemptId)
    {
        await StudentAsync(); await StudentAttemptStateAsync(attemptId);
        return new() { MaxFileBytes = ResponseLimits.MaxImageBytes, MaxImagesPerResponse = EducationAssessmentResponseValue.MaxImages,
            AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"] };
    }

    [Authorize(EducationPermissions.AssessmentParticipate)]
    public virtual async Task<EducationAssessmentAnswerImageDto?> GetAnswerImageAsync(Guid attemptId, Guid operationId)
    {
        await StudentAsync();
        EducationRules.RequireId(operationId);
        await StudentAttemptStateAsync(attemptId);
        var attachment = await ImageOperationAsync(attemptId, operationId);
        return attachment == null ? null : await MapImageAsync(attachment);
    }

    [Authorize(EducationPermissions.AssessmentParticipate)]
    public virtual async Task<IRemoteStreamContent> DownloadAnswerImageAsync(Guid attemptId, Guid attachmentId)
    {
        await StudentAsync();
        await StudentAttemptStateAsync(attemptId);
        var attachment = await ImageOperationAsync(attemptId, attachmentId);
        if (attachment?.IsUploaded != true) throw ImageInvalid();
        var bytes = await StoredImageAsync(attachment) ?? throw ImageStorageFailed();
        // Safe generated storage names never enter the student DTO or Content-Disposition.
        return new RemoteStreamContent(new MemoryStream(bytes, writable: false), attachment.FileName, attachment.ContentType, bytes.LongLength);
    }

    private async Task<(EducationAssessmentAttempt Attempt, EducationAssessmentTaskRevision Revision)> StudentAttemptStateAsync(Guid id, bool forUpdate = false)
    {
        using var tracking = forUpdate ? Repo<EducationAssessmentAttempt>().EnableTracking() : null;
        var attempt = forUpdate ? await Repo<EducationAssessmentAttempt>().FindAsync(id) : await ReferenceAsync<EducationAssessmentAttempt>(id);
        if (attempt == null || attempt.TenantId != CurrentTenant.Id || attempt.StudentUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        var recipient = await StudentRecipientAsync(attempt.RecipientId);
        var revision = await StudentRevisionAsync(recipient, forUpdate);
        if (recipient.CurrentAttemptId != attempt.Id || recipient.StartedAttemptCount != attempt.AttemptNumber || attempt.TaskId != recipient.TaskId ||
            attempt.TaskRevisionId != revision.Id || attempt.ExamPaperId != revision.ExamPaperId) throw new AbpAuthorizationException();
        return (attempt, revision);
    }

    private async Task<ExamPaperQuestion> AnswerQuestionAsync(EducationAssessmentAttempt attempt, Guid id, int? snapshotVersion = null)
    {
        var question = await ReferenceAsync<ExamPaperQuestion>(id);
        var paper = await ReferenceAsync<ExamPaper>(attempt.ExamPaperId);
        if (question == null || paper == null || question.PaperId != attempt.ExamPaperId || question.TenantId != attempt.TenantId || paper.TenantId != attempt.TenantId)
            throw new AbpAuthorizationException();
        if (snapshotVersion.HasValue && paper.SnapshotVersion != snapshotVersion.Value) throw ResponseInvalid();
        return question;
    }
    private async Task<EducationAssessmentResponseRevision?> ResponseOperationAsync(Guid attemptId, Guid id, string? fingerprint = null)
    {
        var value = await ReferenceAsync<EducationAssessmentResponseRevision>(id);
        if (value == null) return null;
        if (value.AttemptId != attemptId || value.TenantId != CurrentTenant.Id || value.StudentUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        if (fingerprint != null && value.RequestFingerprint != fingerprint) throw Conflict();
        return value;
    }
    private async Task<EducationAssessmentAnswerAttachment?> ImageOperationAsync(Guid attemptId, Guid id, string? fingerprint = null)
    {
        var value = await ReferenceAsync<EducationAssessmentAnswerAttachment>(id);
        if (value == null) return null;
        if (value.AttemptId != attemptId || value.TenantId != CurrentTenant.Id || value.StudentUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        if (fingerprint != null && value.RequestFingerprint != fingerprint) throw Conflict();
        return value;
    }
    private static void EnsureImageOperation(EducationAssessmentAttempt attempt, EducationAssessmentAnswerAttachment attachment, string fingerprint)
    {
        if (attachment.TenantId != attempt.TenantId || attachment.AttemptId != attempt.Id || attachment.StudentUserId != attempt.StudentUserId) throw ImageInvalid();
        if (attachment.RequestFingerprint != fingerprint) throw Conflict();
    }
    private async Task<byte[]?> StoredImageAsync(EducationAssessmentAnswerAttachment attachment)
    {
        try
        {
            using var stream = await Images.GetOrNullAsync(attachment.BlobName);
            if (stream == null) return null;
            var bytes = await EducationAssessmentImageFormat.ReadStreamAsync(stream, ResponseLimits.MaxImageBytes);
            if (bytes.LongLength != attachment.Size || EducationAssessmentImageFormat.Hash(bytes) != attachment.ContentHash) throw ImageInvalid();
            EducationAssessmentImageFormat.Detect(bytes, attachment.ContentType);
            return bytes;
        }
        catch (BusinessException) { throw; }
        catch (Exception error) when (error is not OperationCanceledException) { throw ImageStorageFailed(); }
    }
    private async Task<EducationAssessmentSubmission> OwnSubmissionAsync(EducationAssessmentAttempt attempt)
    {
        var value = await ReferenceAsync<EducationAssessmentSubmission>(attempt.SubmissionId!.Value) ?? throw Invalid();
        EnsureOwnSubmission(attempt, value);
        return value;
    }
    private static void EnsureOwnSubmission(EducationAssessmentAttempt attempt, EducationAssessmentSubmission value)
    {
        if (value.AttemptId != attempt.Id || value.TenantId != attempt.TenantId || value.StudentUserId != attempt.StudentUserId ||
            value.ExamPaperId != attempt.ExamPaperId || value.ResponseSetVersion != attempt.ResponseSetVersion) throw Invalid();
    }
    private static EducationAssessmentResponseRevisionDto MapResponse(EducationAssessmentResponseRevision value) => new()
    {
        Id = value.Id, AttemptId = value.AttemptId, PaperQuestionId = value.PaperQuestionId, PaperSnapshotVersion = value.PaperSnapshotVersion,
        ExpectedResponseSetVersion = value.ExpectedResponseSetVersion, ResultResponseSetVersion = value.ResultResponseSetVersion,
        RequestFingerprint = value.RequestFingerprint, ResponseHash = value.ResponseHash,
        Response = EducationAssessmentResponseValue.Parse(value.ResponseJson), SavedAtUtc = value.SavedAtUtc
    };
    private static EducationAssessmentSubmissionDto MapSubmission(EducationAssessmentSubmission value) => new()
    {
        Id = value.Id, AttemptId = value.AttemptId, PaperSnapshotVersion = value.PaperSnapshotVersion, ResponseSetVersion = value.ResponseSetVersion,
        Cause = value.Cause, RequestFingerprint = value.RequestFingerprint, ConfirmedSavedOnly = value.ConfirmedSavedOnly,
        EffectiveAtUtc = value.EffectiveAtUtc, SealedAtUtc = value.SealedAtUtc,
        Manifest = Parse<List<EducationAssessmentSubmissionManifestEntry>>(value.ManifestJson)
    };
    private async Task<EducationAssessmentAnswerImageDto> MapImageAsync(EducationAssessmentAnswerAttachment value)
    {
        var current = (await ReferencesAsync<EducationAssessmentCurrentResponse>(x => x.AttemptId == value.AttemptId && x.PaperQuestionId == value.PaperQuestionId && x.TenantId == value.TenantId)).SingleOrDefault();
        var response = current == null ? null : await ReferenceAsync<EducationAssessmentResponseRevision>(current.CurrentRevisionId);
        return new EducationAssessmentAnswerImageDto
        {
            Id = value.Id, AttemptId = value.AttemptId, PaperQuestionId = value.PaperQuestionId, FileName = value.FileName,
            ContentType = value.ContentType, Size = value.Size, ContentHash = value.ContentHash, RequestedAtUtc = value.RequestedAtUtc,
            IsUploaded = value.IsUploaded, UploadedAtUtc = value.UploadedAtUtc,
            CurrentResponseRevisionId = response != null && EducationAssessmentResponseValue.Parse(response.ResponseJson).Images.Any(x => x.AttachmentId == value.Id) ? response.Id : null
        };
    }
    private static string ImageBlobName(EducationAssessmentAttempt attempt, Guid operationId)
        => $"{attempt.TenantId?.ToString("N") ?? "host"}/{attempt.StudentUserId:N}/{attempt.Id:N}/{operationId:N}";
    private static BusinessException ResponseInvalid() => new("KnowPulse:EducationAssessment:InvalidResponse");
    private static BusinessException ImageInvalid() => new("KnowPulse:EducationAssessment:InvalidImage");
    private static BusinessException ImageStorageFailed() => new("KnowPulse:EducationAssessment:ImageStorageFailed");
}
