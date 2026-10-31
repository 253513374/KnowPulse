using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationAssessmentAppService
{
    private EducationAssessmentGradingProcessor Grading => LazyServiceProvider.LazyGetRequiredService<EducationAssessmentGradingProcessor>();
    private static T GradeDto<T>(object value) => Parse<T>(System.Text.Json.JsonSerializer.Serialize(value, Json));
    private static BusinessException IncompleteGrade() => new(EducationAssessmentErrorCodes.GradingIncomplete);

    private async Task<(EducationAssessmentTask Task, EducationAssessmentTaskRevision Revision)> GradingTaskAsync(Guid id, bool write = false, string? stamp = null)
    {
        await TeacherAsync();
        var task = await TaskAsync(id);
        var revision = await RevisionAsync(task);
        if (task.PublishedRevisionId != revision.Id) throw Invalid();
        var scope = Parse<EducationCurriculumSnapshot>(revision.CurriculumSnapshotJson);
        await Access.EnsureTeacherRelationAsync(scope, revision.AcademicTermId, revision.SchoolClassId, write);
        if (write)
        {
            using var tracking = Repo<EducationAssessmentTask>().EnableTracking();
            task = await Repo<EducationAssessmentTask>().GetAsync(id);
            if (stamp != null && task.ConcurrencyStamp != stamp) throw new AbpDbConcurrencyException();
            await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true);
            await Access.EnsureTeacherRelationAsync(scope, revision.AcademicTermId, revision.SchoolClassId);
        }
        return (task, revision);
    }

    private async Task<(EducationAssessmentSubmission Submission, EducationAssessmentAttempt Attempt)> GradeSubmissionAsync(Guid id, bool write = false)
    {
        await TeacherAsync();
        var submission = await ReferenceAsync<EducationAssessmentSubmission>(id) ?? throw new AbpAuthorizationException();
        var attempt = await ReferenceAsync<EducationAssessmentAttempt>(submission.AttemptId) ?? throw Invalid();
        if (submission.TenantId != CurrentTenant.Id || attempt.TenantId != CurrentTenant.Id || attempt.SubmissionId != id ||
            submission.StudentUserId != attempt.StudentUserId || submission.ExamPaperId != attempt.ExamPaperId) throw new AbpAuthorizationException();
        await GradingTaskAsync(attempt.TaskId, write);
        return (submission, attempt);
    }

    private async Task<EducationAssessmentGradeRevision?> GradeReceiptAsync(Guid submissionId, Guid operationId, string? fingerprint = null)
    {
        await GradeSubmissionAsync(submissionId);
        var receipt = await ReferenceAsync<EducationAssessmentGradeRevision>(operationId);
        if (receipt == null) return null;
        if (receipt.TenantId != CurrentTenant.Id || receipt.SubmissionId != submissionId) throw new AbpAuthorizationException();
        if (fingerprint != null && receipt.RequestFingerprint != fingerprint) throw Conflict();
        return receipt;
    }

    [Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentGradingOverviewDto> GetGradingOverviewAsync(Guid taskId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: false);
        var (task, _) = await GradingTaskAsync(taskId);
        var recipients = await ReferencesAsync<EducationAssessmentRecipient>(x => x.TaskId == taskId && x.TenantId == CurrentTenant.Id);
        var attempts = await ReferencesAsync<EducationAssessmentAttempt>(x => x.TaskId == taskId && x.TenantId == CurrentTenant.Id);
        var overview = new EducationAssessmentGradingOverviewDto { TaskId = taskId, PublishedRevisionId = task.PublishedRevisionId!.Value,
            ConcurrencyStamp = task.ConcurrencyStamp, ConfirmedGradeSetVersion = task.ConfirmedGradeSetVersion, RecipientCount = recipients.Count,
            UnstartedCount = recipients.Count(x => !x.CurrentAttemptId.HasValue), InProgressCount = attempts.Count(x => !x.SubmissionId.HasValue),
            SubmittedCount = attempts.Count(x => x.SubmissionId.HasValue), ClosureId = task.ClosureId, CurrentResultReleaseId = task.CurrentResultReleaseId };
        foreach (var attempt in attempts.Where(x => x.SubmissionId.HasValue))
        {
            var recipient = recipients.Single(x => x.Id == attempt.RecipientId);
            var questions = await ReferencesAsync<ExamPaperQuestion>(x => x.PaperId == attempt.ExamPaperId && x.TenantId == CurrentTenant.Id);
            var grades = await ReferencesAsync<EducationAssessmentQuestionGrade>(x => x.SubmissionId == attempt.SubmissionId && x.TenantId == CurrentTenant.Id);
            var confirmed = await ConfirmedGradesAsync(grades);
            overview.Submissions.Add(new() { SubmissionId = attempt.SubmissionId!.Value, StudentUserId = attempt.StudentUserId,
                StudentName = recipient.StudentName, AccountName = recipient.AccountName, QuestionCount = questions.Count,
                ConfirmedQuestionCount = confirmed.Count, MaxScore = questions.Sum(x => x.Score),
                TotalScore = confirmed.Count == questions.Count ? confirmed.Sum(x => x.ObtainedScore!.Value) : null });
            overview.PendingQuestionCount += questions.Count - confirmed.Count;
            if (confirmed.Count == questions.Count) overview.GradingCompleteCount++;
        }
        await uow.CompleteAsync(); return overview;
    }

    private async Task<List<EducationAssessmentGradeRevision>> ConfirmedGradesAsync(IReadOnlyCollection<EducationAssessmentQuestionGrade> grades)
    {
        var ids = grades.Where(x => x.CurrentConfirmedRevisionId.HasValue).Select(x => x.CurrentConfirmedRevisionId!.Value).ToList();
        var result = await ReferencesAsync<EducationAssessmentGradeRevision>(x => ids.Contains(x.Id) && x.TenantId == CurrentTenant.Id);
        if (result.Count != ids.Count || result.Any(r => r.Kind != EducationAssessmentGradeRevisionKind.Confirmed ||
            !r.ObtainedScore.HasValue || !grades.Any(g => g.Id == r.QuestionGradeId && g.SubmissionId == r.SubmissionId && g.PaperQuestionId == r.PaperQuestionId))) throw Invalid();
        return result;
    }

    [Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentSubmissionForGradingDto> GetSubmissionForGradingAsync(Guid submissionId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var (submission, attempt) = await GradeSubmissionAsync(submissionId, write: true);
        await Grading.InitializeInCurrentUnitOfWorkAsync(submissionId);
        var recipient = await ReferenceAsync<EducationAssessmentRecipient>(attempt.RecipientId) ?? throw Invalid();
        var task = await ReferenceAsync<EducationAssessmentTask>(attempt.TaskId) ?? throw Invalid();
        var result = new EducationAssessmentSubmissionForGradingDto { TaskId = task.Id, ConfirmedGradeSetVersion = task.ConfirmedGradeSetVersion,
            StudentUserId = attempt.StudentUserId, StudentName = recipient.StudentName, AccountName = recipient.AccountName, Submission = MapSubmission(submission) };
        var questions = await ReferencesAsync<ExamPaperQuestion>(x => x.PaperId == attempt.ExamPaperId && x.TenantId == CurrentTenant.Id);
        foreach (var question in questions.OrderBy(x => x.DisplayOrder))
        {
            var grade = (await ReferencesAsync<EducationAssessmentQuestionGrade>(x => x.SubmissionId == submissionId && x.PaperQuestionId == question.Id)).Single();
            var current = await ReferenceAsync<EducationAssessmentGradeRevision>(grade.CurrentRevisionId!.Value) ?? throw Invalid();
            var confirmed = grade.CurrentConfirmedRevisionId.HasValue ? await ReferenceAsync<EducationAssessmentGradeRevision>(grade.CurrentConfirmedRevisionId.Value) : null;
            var response = await SubmittedResponseAsync(submission, question.Id);
            var images = new List<EducationAssessmentAnswerImageDto>();
            if (response != null) foreach (var image in EducationAssessmentResponseValue.Parse(response.ResponseJson).Images)
                images.Add(await MapImageAsync(await ReferenceAsync<EducationAssessmentAnswerAttachment>(image.AttachmentId) ?? throw Invalid()));
            result.Questions.Add(new() { Id = question.Id, Type = question.Type, Title = question.Title, Stem = question.Stem, DisplayOrder = question.DisplayOrder,
                MaxScore = question.Score, Options = Parse<List<ExamPaperQuestionOptionDto>>(question.OptionsJson), CorrectAnswers = Parse<List<string>>(question.CorrectAnswersJson),
                Analysis = question.Analysis, ScoringDefinition = question.ScoringDefinitionJson == null ? null : QuestionScoringDefinition.Parse(question.ScoringDefinitionJson),
                Response = response == null ? null : EducationAssessmentResponseValue.Parse(response.ResponseJson), Images = images,
                Grade = new() { Id = grade.Id, SubmissionId = grade.SubmissionId, PaperQuestionId = grade.PaperQuestionId, RevisionNumber = grade.RevisionNumber,
                    ConcurrencyStamp = grade.ConcurrencyStamp, CurrentRevisionId = grade.CurrentRevisionId,
                    Draft = current.Kind == EducationAssessmentGradeRevisionKind.Draft ? GradeDto<EducationAssessmentGradeRevisionDto>(current) : null,
                    Confirmed = confirmed == null ? null : GradeDto<EducationAssessmentGradeRevisionDto>(confirmed) } });
        }
        await uow.CompleteAsync(); return result;
    }

    private async Task<EducationAssessmentResponseRevision?> SubmittedResponseAsync(EducationAssessmentSubmission submission, Guid questionId)
    {
        var entry = Parse<List<EducationAssessmentSubmissionManifestEntry>>(submission.ManifestJson).Single(x => x.PaperQuestionId == questionId);
        if (!entry.ResponseRevisionId.HasValue) return null;
        var response = await ReferenceAsync<EducationAssessmentResponseRevision>(entry.ResponseRevisionId.Value) ?? throw Invalid();
        if (response.TenantId != submission.TenantId || response.AttemptId != submission.AttemptId || response.StudentUserId != submission.StudentUserId ||
            response.PaperQuestionId != questionId || response.ExamPaperId != submission.ExamPaperId || response.ResponseHash != entry.ResponseHash ||
            EducationAssessmentGradeRevision.Hash(response.ResponseJson) != entry.ResponseHash) throw Invalid();
        return response;
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<IRemoteStreamContent> DownloadSubmissionImageAsync(Guid submissionId, Guid attachmentId)
    {
        var (submission, _) = await GradeSubmissionAsync(submissionId);
        var image = await ReferenceAsync<EducationAssessmentAnswerAttachment>(attachmentId) ?? throw new AbpAuthorizationException();
        var response = await SubmittedResponseAsync(submission, image.PaperQuestionId);
        if (image.TenantId != CurrentTenant.Id || image.AttemptId != submission.AttemptId || image.StudentUserId != submission.StudentUserId ||
            !image.IsUploaded || response == null || !EducationAssessmentResponseValue.Parse(response.ResponseJson).Images.Any(x => x.AttachmentId == attachmentId))
            throw new AbpAuthorizationException();
        var bytes = await StoredImageAsync(image) ?? throw Invalid();
        return new RemoteStreamContent(new System.IO.MemoryStream(bytes, writable: false), image.FileName, image.ContentType);
    }

    [Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentGradeRevisionDto> SaveGradeDraftAsync(Guid submissionId, SaveEducationAssessmentGradeDraftDto input)
    {
        await TeacherAsync(); EducationRules.RequireId(input.OperationId);
        var fp = Hash(new { Command = "grade-draft", submissionId, Input = input });
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var prior = await GradeReceiptAsync(submissionId, input.OperationId, fp);
            if (prior != null) return await CompleteAsync(uow, () => Task.FromResult(GradeDto<EducationAssessmentGradeRevisionDto>(prior)));
            var (submission, attempt) = await GradeSubmissionAsync(submissionId, write: true);
            await Grading.InitializeInCurrentUnitOfWorkAsync(submissionId);
            using var tracking = Repo<EducationAssessmentQuestionGrade>().EnableTracking();
            var grade = (await Repo<EducationAssessmentQuestionGrade>().GetListAsync(x => x.SubmissionId == submissionId && x.PaperQuestionId == input.PaperQuestionId)).SingleOrDefault() ?? throw Invalid();
            if (grade.ConcurrencyStamp != input.ConcurrencyStamp || grade.CurrentRevisionId != input.ExpectedGradeRevisionId) throw new AbpDbConcurrencyException();
            var question = await AnswerQuestionAsync(attempt, input.PaperQuestionId);
            var revision = new EducationAssessmentGradeRevision(input.OperationId, grade, submission, await SubmittedResponseAsync(submission, question.Id), question,
                EducationAssessmentGradeRevisionKind.Draft, EducationAssessmentGradingMethod.Teacher, input.PartMarks, input.Feedback, null, CurrentUser.Id,
                input.ConcurrencyStamp, fp, DateTime.UtcNow, null);
            grade.Advance(revision);
            await Repo<EducationAssessmentGradeRevision>().InsertAsync(revision, autoSave: true);
            await Repo<EducationAssessmentQuestionGrade>().UpdateManyAsync([grade], autoSave: true);
            return await CompleteAsync(uow, () => Task.FromResult(GradeDto<EducationAssessmentGradeRevisionDto>(revision)));
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            var receipt = await GradeReceiptAsync(submissionId, input.OperationId, fp); if (receipt == null) throw;
            return await CompleteAsync(recovery, () => Task.FromResult(GradeDto<EducationAssessmentGradeRevisionDto>(receipt)));
        }
    }

    [Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentGradeRevisionDto> ConfirmGradeAsync(Guid questionGradeId, ConfirmEducationAssessmentGradeDto input)
    {
        await TeacherAsync(); EducationRules.RequireId(input.OperationId);
        EducationAssessmentQuestionGrade initial;
        using (var lookup = Uow.Begin(requiresNew: true, isTransactional: false))
        { initial = await ReferenceAsync<EducationAssessmentQuestionGrade>(questionGradeId) ?? throw new AbpAuthorizationException(); await lookup.CompleteAsync(); }
        var fp = Hash(new { Command = "grade-confirm", questionGradeId, Input = input });
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var prior = await GradeReceiptAsync(initial.SubmissionId, input.OperationId, fp);
            if (prior != null) return await CompleteAsync(uow, () => Task.FromResult(GradeDto<EducationAssessmentGradeRevisionDto>(prior)));
            var (submission, attempt) = await GradeSubmissionAsync(initial.SubmissionId, write: true);
            using var taskTracking = Repo<EducationAssessmentTask>().EnableTracking();
            using var gradeTracking = Repo<EducationAssessmentQuestionGrade>().EnableTracking();
            var task = await Repo<EducationAssessmentTask>().GetAsync(initial.TaskId);
            var grade = await Repo<EducationAssessmentQuestionGrade>().GetAsync(questionGradeId);
            if (grade.ConcurrencyStamp != input.ConcurrencyStamp || grade.CurrentRevisionId != input.ExpectedDraftRevisionId ||
                task.ConfirmedGradeSetVersion != input.ExpectedGradeSetVersion) throw new AbpDbConcurrencyException();
            var draft = await ReferenceAsync<EducationAssessmentGradeRevision>(input.ExpectedDraftRevisionId) ?? throw Invalid();
            if (draft.QuestionGradeId != grade.Id || draft.Kind != EducationAssessmentGradeRevisionKind.Draft) throw Invalid();
            if (!draft.ObtainedScore.HasValue) throw IncompleteGrade();
            var question = await AnswerQuestionAsync(attempt, grade.PaperQuestionId);
            var revision = new EducationAssessmentGradeRevision(input.OperationId, grade, submission, await SubmittedResponseAsync(submission, question.Id), question,
                EducationAssessmentGradeRevisionKind.Confirmed, EducationAssessmentGradingMethod.Teacher,
                draft.Content.Parts.Select(x => new EducationAssessmentPartMark { PartId = x.PartId, ObtainedScore = x.ObtainedScore }).ToList(),
                draft.Feedback, input.Reason, CurrentUser.Id, input.ConcurrencyStamp, fp, DateTime.UtcNow, task.ConfirmedGradeSetVersion);
            grade.Advance(revision);
            await Repo<EducationAssessmentGradeRevision>().InsertAsync(revision, autoSave: true);
            await Repo<EducationAssessmentQuestionGrade>().UpdateManyAsync([grade], autoSave: true);
            task.RecordConfirmedGrade(grade, revision);
            if (task.CurrentResultReleaseId.HasValue)
            {
                var previous = await ReferenceAsync<EducationAssessmentResultRelease>(task.CurrentResultReleaseId.Value) ?? throw Invalid();
                await CreateReleaseAsync(task, await ReferenceAsync<EducationAssessmentClosure>(task.ClosureId!.Value) ?? throw Invalid(), GuidGenerator.Create(),
                    previous.ConfiguredVisibility, previous.ScoreOpensAtUtc, previous.ReviewOpensAtUtc, Hash(new { Correction = revision.Id }), input.Reason);
            }
            await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true);
            return await CompleteAsync(uow, () => Task.FromResult(GradeDto<EducationAssessmentGradeRevisionDto>(revision)));
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            var receipt = await GradeReceiptAsync(initial.SubmissionId, input.OperationId, fp); if (receipt == null) throw;
            return await CompleteAsync(recovery, () => Task.FromResult(GradeDto<EducationAssessmentGradeRevisionDto>(receipt)));
        }
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationAssessmentGradeRevisionDto?> GetGradeOperationAsync(Guid submissionId, Guid operationId)
    {
        var receipt = await GradeReceiptAsync(submissionId, operationId);
        return receipt == null ? null : GradeDto<EducationAssessmentGradeRevisionDto>(receipt);
    }
}
