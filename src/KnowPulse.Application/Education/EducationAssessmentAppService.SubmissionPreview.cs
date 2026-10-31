using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationAssessmentAppService
{
    [Authorize(EducationPermissions.AssessmentParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentSubmissionPreviewDto> PreviewSubmissionAsync(Guid attemptId)
    {
        await StudentAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var state = await StudentAttemptStateAsync(attemptId, forUpdate: true);
        await Repo<EducationAssessmentAttempt>().UpdateManyAsync([state.Attempt], autoSave: true);
        var preview = await SubmissionPreviewAsync(state.Attempt);
        await uow.CompleteAsync(); return preview;
    }

    private async Task<EducationAssessmentSubmissionPreviewDto> SubmissionPreviewAsync(EducationAssessmentAttempt attempt)
    {
        var paper = await ReferenceAsync<ExamPaper>(attempt.ExamPaperId) ?? throw ResponseInvalid();
        var questions = (await ReferencesAsync<ExamPaperQuestion>(x => x.PaperId == paper.Id && x.TenantId == attempt.TenantId)).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToList();
        if (paper.TenantId != attempt.TenantId || questions.Count != paper.QuestionCount || questions.Count is < 1 or > EducationAssessmentSubmission.MaxQuestions) throw ResponseInvalid();
        var current = await ReferencesAsync<EducationAssessmentCurrentResponse>(x => x.AttemptId == attempt.Id && x.TenantId == attempt.TenantId);
        var ids = current.Select(x => x.CurrentRevisionId).ToList();
        var responses = await ReferencesAsync<EducationAssessmentResponseRevision>(x => ids.Contains(x.Id) && x.TenantId == attempt.TenantId);
        if (current.Count != responses.Count || current.Select(x => x.PaperQuestionId).Distinct().Count() != current.Count ||
            current.Any(x => x.ExamPaperId != paper.Id || !questions.Any(q => q.Id == x.PaperQuestionId)) ||
            (responses.Count == 0) != (attempt.ResponseSetVersion == 0) || responses.Count > 0 && responses.Max(x => x.ResultResponseSetVersion) != attempt.ResponseSetVersion) throw ResponseInvalid();
        var linked = new Dictionary<Guid, Guid>(); var headers = new List<EducationAssessmentSubmissionQuestionDto>();
        foreach (var question in questions) {
            var head = current.SingleOrDefault(x => x.PaperQuestionId == question.Id);
            var response = head == null ? null : responses.SingleOrDefault(x => x.Id == head.CurrentRevisionId) ?? throw ResponseInvalid();
            if (response != null && (response.AttemptId != attempt.Id || response.StudentUserId != attempt.StudentUserId || response.ExamPaperId != paper.Id ||
                response.PaperQuestionId != question.Id || response.PaperSnapshotVersion != paper.SnapshotVersion || response.ResultResponseSetVersion != head!.ResponseSetVersion ||
                response.ResultResponseSetVersion > attempt.ResponseSetVersion)) throw ResponseInvalid();
            var value = response == null ? null : EducationAssessmentResponseValue.Parse(response.ResponseJson);
            if (value != null && value.Type != question.Type) throw ResponseInvalid();
            foreach (var image in value?.Images ?? []) if (!linked.TryAdd(image.AttachmentId, question.Id)) throw ResponseInvalid();
            var blanks = question.ScoringDefinitionJson == null ? null : QuestionScoringDefinition.Parse(question.ScoringDefinitionJson);
            headers.Add(new() { Id = question.Id, DisplayOrder = question.DisplayOrder, Title = question.Title, SavedResponseRevisionId = response?.Id,
                IsUnanswered = value?.IsUnanswered() ?? true, SavedImageCount = value?.Images.Count ?? 0,
                UnansweredBlankCount = question.Type != QuestionType.FillBlank ? 0 : blanks?.Blanks.Count(x => value?.Blanks.Any(b => b.BlankId == x.Id && !string.IsNullOrWhiteSpace(b.Text)) != true) ?? 0 });
        }
        var uploads = (await ReferencesAsync<EducationAssessmentAnswerAttachment>(x => x.AttemptId == attempt.Id && x.TenantId == attempt.TenantId)).OrderBy(x => x.Id).ToList();
        if (uploads.Any(x => x.StudentUserId != attempt.StudentUserId || x.ExamPaperId != paper.Id || !questions.Any(q => q.Id == x.PaperQuestionId)) ||
            linked.Any(image => !uploads.Any(x => x.Id == image.Key && x.PaperQuestionId == image.Value && x.IsUploaded))) throw ResponseInvalid();
        var unlinked = uploads.Where(x => !linked.ContainsKey(x.Id)).Select(x => new EducationAssessmentAnswerImageDto {
            Id = x.Id, AttemptId = x.AttemptId, PaperQuestionId = x.PaperQuestionId, FileName = x.FileName, ContentType = x.ContentType, Size = x.Size,
            ContentHash = x.ContentHash, RequestedAtUtc = x.RequestedAtUtc, IsUploaded = x.IsUploaded, UploadedAtUtc = x.UploadedAtUtc }).ToList();
        var now = DateTime.UtcNow;
        return new() { AttemptId = attempt.Id, PaperSnapshotVersion = paper.SnapshotVersion, ResponseSetVersion = attempt.ResponseSetVersion,
            ServerNowUtc = now, ExpiresAtUtc = attempt.ExpiresAtUtc, SubmissionId = attempt.SubmissionId,
            CanSubmit = attempt.SubmissionId == null && now < attempt.ExpiresAtUtc, Questions = headers, UnlinkedImages = unlinked,
            Fingerprint = Hash(new { attempt.Id, PaperSnapshotVersion = paper.SnapshotVersion, attempt.ResponseSetVersion,
                Questions = headers.Select(x => new { x.Id, x.SavedResponseRevisionId }),
                Uploads = uploads.Select(x => new { x.Id, x.PaperQuestionId, x.ContentHash, x.Size, x.UploadedAtUtc }) }) };
    }
}
