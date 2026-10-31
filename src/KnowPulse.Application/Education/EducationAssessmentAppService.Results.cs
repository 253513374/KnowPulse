using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationAssessmentAppService
{
    private async Task<List<EducationAssessmentClosureEntry>> ClosureEntriesAsync(Guid taskId)
    {
        var recipients = await ReferencesAsync<EducationAssessmentRecipient>(x => x.TaskId == taskId && x.TenantId == CurrentTenant.Id);
        var attempts = await ReferencesAsync<EducationAssessmentAttempt>(x => x.TaskId == taskId && x.TenantId == CurrentTenant.Id);
        if (recipients.Count == 0 || attempts.Any(x => !recipients.Any(r => r.Id == x.RecipientId && r.StudentUserId == x.StudentUserId && r.CurrentAttemptId == x.Id))) throw Invalid();
        return recipients.OrderBy(x => x.Id).Select(r =>
        {
            var attempt = attempts.SingleOrDefault(x => x.Id == r.CurrentAttemptId);
            return new EducationAssessmentClosureEntry { RecipientId = r.Id, StudentUserId = r.StudentUserId, AttemptId = attempt?.Id,
                SubmissionId = attempt?.SubmissionId, Outcome = attempt == null ? EducationAssessmentRecipientOutcome.Absent : EducationAssessmentRecipientOutcome.Submitted };
        }).ToList();
    }

    private async Task<string> FinalizationFingerprintAsync(Guid taskId) => Hash(new { taskId, Entries = await ClosureEntriesAsync(taskId) });

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationAssessmentFinalizationPreviewDto> PreviewFinalizationAsync(Guid taskId)
    {
        var overview = await GetGradingOverviewAsync(taskId);
        var (_, revision) = await GradingTaskAsync(taskId);
        var result = GradeDto<EducationAssessmentFinalizationPreviewDto>(overview);
        result.EndsAtUtc = revision.EndsAtUtc; result.ServerNowUtc = DateTime.UtcNow;
        result.FinalizationFingerprint = await FinalizationFingerprintAsync(taskId);
        result.CanClose = result.ServerNowUtc >= revision.EndsAtUtc && !overview.ClosureId.HasValue;
        result.CanPublish = overview.ClosureId.HasValue && overview.InProgressCount == 0 && overview.PendingQuestionCount == 0 &&
            overview.SubmittedCount == overview.GradingCompleteCount;
        return result;
    }

    [Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentClosureDto> CloseTaskAsync(Guid taskId, CloseEducationAssessmentTaskDto input)
    {
        await TeacherAsync(); EducationRules.RequireId(input.OperationId);
        var fp = Hash(new { Command = "close-task", taskId, Input = input });
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            await GradingTaskAsync(taskId);
            var prior = await ReferenceAsync<EducationAssessmentClosure>(input.OperationId);
            if (prior != null)
            {
                if (prior.TaskId != taskId || prior.RequestFingerprint != fp) throw Conflict();
                return await CompleteAsync(uow, () => Task.FromResult(GradeDto<EducationAssessmentClosureDto>(prior)));
            }
            var (task, revision) = await GradingTaskAsync(taskId, write: true, input.ConcurrencyStamp);
            if (task.ClosureId.HasValue || revision.Id != input.ExpectedPublishedRevisionId || DateTime.UtcNow < revision.EndsAtUtc) throw Invalid();
            if (await FinalizationFingerprintAsync(taskId) != input.FinalizationFingerprint) throw new AbpDbConcurrencyException();
            var attempts = await ReferencesAsync<EducationAssessmentAttempt>(x => x.TaskId == taskId && x.TenantId == CurrentTenant.Id);
            foreach (var attempt in attempts.Where(x => !x.SubmissionId.HasValue).OrderBy(x => x.Id))
                await LazyServiceProvider.LazyGetRequiredService<EducationAssessmentSubmissionProcessor>().SealAsync(attempt.Id, GuidGenerator.Create(), null, null, false);
            var entries = await ClosureEntriesAsync(taskId);
            var manifest = new EducationAssessmentClosureValue { Entries = entries }; manifest.Validate();
            var closure = new EducationAssessmentClosure(input.OperationId, task, revision, manifest, input.ConcurrencyStamp, fp, DateTime.UtcNow);
            await Repo<EducationAssessmentClosure>().InsertAsync(closure, autoSave: true);
            task.Close(closure); await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true);
            return await CompleteAsync(uow, () => Task.FromResult(GradeDto<EducationAssessmentClosureDto>(closure)));
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            await GradingTaskAsync(taskId);
            var receipt = await ReferenceAsync<EducationAssessmentClosure>(input.OperationId);
            if (receipt == null) throw;
            if (receipt.TaskId != taskId || receipt.RequestFingerprint != fp) throw Conflict();
            return await CompleteAsync(recovery, () => Task.FromResult(GradeDto<EducationAssessmentClosureDto>(receipt)));
        }
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationAssessmentClosureDto?> GetClosureOperationAsync(Guid taskId, Guid operationId)
    {
        await GradingTaskAsync(taskId);
        var value = await ReferenceAsync<EducationAssessmentClosure>(operationId);
        if (value == null) return null;
        if (value.TenantId != CurrentTenant.Id || value.TaskId != taskId) throw new AbpAuthorizationException();
        return GradeDto<EducationAssessmentClosureDto>(value);
    }

    private async Task<EducationAssessmentResultRelease> CreateReleaseAsync(EducationAssessmentTask task, EducationAssessmentClosure closure,
        Guid operationId, ExamResultVisibility visibility, DateTime? scoreAt, DateTime? reviewAt, string fingerprint, string? reason, string? expectedStamp = null)
    {
        var entries = await ClosureEntriesAsync(task.Id);
        if (new EducationAssessmentClosureValue { Entries = entries }.ToCanonicalJson() != closure.ManifestJson) throw Invalid();
        var release = new EducationAssessmentResultRelease(operationId, task, closure, visibility, scoreAt, reviewAt, fingerprint, reason, DateTime.UtcNow, expectedStamp);
        var items = new List<EducationAssessmentResultReleaseItem>();
        foreach (var entry in closure.Manifest.Entries.Where(x => x.Outcome == EducationAssessmentRecipientOutcome.Submitted))
        {
            var submission = await ReferenceAsync<EducationAssessmentSubmission>(entry.SubmissionId!.Value) ?? throw Invalid();
            var questions = await ReferencesAsync<ExamPaperQuestion>(x => x.PaperId == submission.ExamPaperId && x.TenantId == CurrentTenant.Id);
            var grades = await ReferencesAsync<EducationAssessmentQuestionGrade>(x => x.SubmissionId == submission.Id && x.TenantId == CurrentTenant.Id);
            var confirmed = await ConfirmedGradesAsync(grades);
            if (confirmed.Count != questions.Count || questions.Count == 0 || questions.Any(q => !confirmed.Any(g => g.PaperQuestionId == q.Id))) throw IncompleteGrade();
            items.Add(new EducationAssessmentResultReleaseItem(GuidGenerator.Create(), release, entry, confirmed));
        }
        if (items.Count != closure.ValidSubmissionCount) throw Invalid();
        await Repo<EducationAssessmentResultRelease>().InsertAsync(release, autoSave: true);
        await Repo<EducationAssessmentResultReleaseItem>().InsertManyAsync(items, autoSave: true);
        task.Release(release);
        if (release.ConfiguredVisibility != ExamResultVisibility.Hidden) {
            var publication = await ReferenceAsync<EducationAssessmentTaskRevision>(release.TaskRevisionId) ?? throw Invalid();
            var recipients = (await ReferencesAsync<EducationAssessmentRecipient>(x => x.TaskId == task.Id && x.TenantId == CurrentTenant.Id)).ToDictionary(x => x.Id);
            await Repo<EducationAssessmentReminder>().InsertManyAsync(entries.Select(entry => EducationAssessmentReminder.ResultReleased(GuidGenerator.Create(),
                recipients[entry.RecipientId], publication, release, entry, Hash(new { Command = "assessment-result-notice", Release = release.Id, Recipient = entry.RecipientId }), release.ReleasedAtUtc)).ToList(), autoSave: true);
        }
        return release;
    }

    private async Task<EducationAssessmentResultReleaseDto> MapReleaseAsync(EducationAssessmentResultRelease release)
    {
        var dto = GradeDto<EducationAssessmentResultReleaseDto>(release);
        dto.Items = (await ReferencesAsync<EducationAssessmentResultReleaseItem>(x => x.ResultReleaseId == release.Id && x.TenantId == CurrentTenant.Id))
            .Select(GradeDto<EducationAssessmentResultReleaseItemDto>).ToList();
        return dto;
    }

    [Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentResultReleaseDto> PublishResultsAsync(Guid taskId, PublishEducationAssessmentResultsDto input)
    {
        await TeacherAsync(); EducationRules.RequireId(input.OperationId);
        var fp = Hash(new { Command = "release-results", taskId, Input = input });
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            await GradingTaskAsync(taskId);
            var prior = await ReferenceAsync<EducationAssessmentResultRelease>(input.OperationId);
            if (prior != null)
            {
                if (prior.TaskId != taskId || prior.RequestFingerprint != fp) throw Conflict();
                return await CompleteAsync(uow, () => MapReleaseAsync(prior));
            }
            var (task, revision) = await GradingTaskAsync(taskId, write: true, input.ConcurrencyStamp);
            if (task.ClosureId != input.ExpectedClosureId || task.ConfirmedGradeSetVersion != input.ExpectedGradeSetVersion) throw new AbpDbConcurrencyException();
            if (input.ConfiguredVisibility != revision.ResultVisibility && !input.ConfirmVisibilityChange) throw Invalid();
            var closure = await ReferenceAsync<EducationAssessmentClosure>(input.ExpectedClosureId) ?? throw Invalid();
            var release = await CreateReleaseAsync(task, closure, input.OperationId, input.ConfiguredVisibility, input.ScoreOpensAtUtc,
                input.ReviewOpensAtUtc, fp, input.Reason, input.ConcurrencyStamp);
            await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true);
            return await CompleteAsync(uow, () => MapReleaseAsync(release));
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            await GradingTaskAsync(taskId);
            var receipt = await ReferenceAsync<EducationAssessmentResultRelease>(input.OperationId); if (receipt == null) throw;
            if (receipt.TaskId != taskId || receipt.RequestFingerprint != fp) throw Conflict();
            return await CompleteAsync(recovery, () => MapReleaseAsync(receipt));
        }
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationAssessmentResultReleaseDto?> GetReleaseOperationAsync(Guid taskId, Guid operationId)
    {
        await GradingTaskAsync(taskId);
        var value = await ReferenceAsync<EducationAssessmentResultRelease>(operationId);
        if (value == null) return null;
        if (value.TenantId != CurrentTenant.Id || value.TaskId != taskId) throw new AbpAuthorizationException();
        return await MapReleaseAsync(value);
    }

    private async Task<ExamResultVisibility> ResultVisibilityAsync(Guid taskId)
    {
        var task = await ReferenceAsync<EducationAssessmentTask>(taskId);
        var release = task?.CurrentResultReleaseId is Guid id ? await ReferenceAsync<EducationAssessmentResultRelease>(id) : null;
        return release != null && release.TaskId == taskId && release.TenantId == CurrentTenant.Id
            ? release.VisibilityAt(DateTime.UtcNow) : ExamResultVisibility.Hidden;
    }

    [Authorize(EducationPermissions.AssessmentParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentStudentResultDto> GetMyResultAsync(Guid recipientId)
        => await MyResultCoreAsync(recipientId);

    [Authorize(EducationPermissions.AssessmentParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentStudentResultDto> GetMyResultForSchoolAsync(Guid recipientId, Guid schoolId, Guid? expectedTaskRevisionId = null, Guid? expectedResultReleaseId = null)
        => await MyResultCoreAsync(recipientId, EducationRules.RequireId(schoolId), expectedTaskRevisionId, expectedResultReleaseId);

    private async Task<EducationAssessmentStudentResultDto> MyResultCoreAsync(Guid recipientId, Guid? schoolId = null, Guid? expectedTaskRevisionId = null, Guid? expectedResultReleaseId = null)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        if (schoolId.HasValue) { await StudentAsync(); var recipient = await StudentRecipientAsync(recipientId); var revision = await StudentRevisionAsync(recipient);
            if (revision.SchoolId != schoolId) throw new AbpAuthorizationException();
            if (expectedTaskRevisionId.HasValue && revision.Id != expectedTaskRevisionId) throw new BusinessException(EducationNotebookSourceErrors.Unavailable); }
        var result = await MyResultInCurrentUnitOfWorkAsync(recipientId, acquireReviewBarrier: true, expectedResultReleaseId: expectedResultReleaseId);
        await uow.CompleteAsync(); return result;
    }

    private async Task<EducationAssessmentStudentResultDto> MyResultInCurrentUnitOfWorkAsync(Guid recipientId, bool acquireReviewBarrier, bool recordExposure = true, Guid? expectedResultReleaseId = null)
    {
        await StudentAsync();
        var recipient = await StudentRecipientAsync(recipientId); await StudentRevisionAsync(recipient);
        var task = await ReferenceAsync<EducationAssessmentTask>(recipient.TaskId) ?? throw Invalid();
        if (expectedResultReleaseId.HasValue && task.CurrentResultReleaseId != expectedResultReleaseId) throw new BusinessException(EducationLearningReportErrors.SourceChanged);
        var result = new EducationAssessmentStudentResultDto { TaskId = task.Id, RecipientId = recipientId, AttemptId = recipient.CurrentAttemptId };
        if (task.ClosureId.HasValue)
        {
            var closure = await ReferenceAsync<EducationAssessmentClosure>(task.ClosureId.Value) ?? throw Invalid();
            var entry = closure.Manifest.Entries.Single(x => x.RecipientId == recipientId && x.StudentUserId == CurrentUser.Id);
            result.Outcome = entry.Outcome; result.SubmissionId = entry.SubmissionId;
        }
        if (!task.CurrentResultReleaseId.HasValue) return result;
        var release = await ReferenceAsync<EducationAssessmentResultRelease>(task.CurrentResultReleaseId.Value) ?? throw Invalid();
        if (release.TaskId != task.Id || release.TenantId != CurrentTenant.Id) throw Invalid();
        result.CurrentResultVisibility = release.VisibilityAt(DateTime.UtcNow);
        if (result.CurrentResultVisibility == ExamResultVisibility.Hidden) return result;
        if (result.CurrentResultVisibility == ExamResultVisibility.FullReview && acquireReviewBarrier)
        {
            var revision = await ReferenceAsync<EducationAssessmentTaskRevision>(recipient.TaskRevisionId) ?? throw Invalid();
            await Access.EnsureStudentAsync(revision.SchoolId, revision.AcademicTermId, revision.SchoolClassId,
                recipient.SchoolMembershipId, recipient.StudentEnrollmentId, recipient.StudentUserId, forUpdate: true);
            return await MyResultInCurrentUnitOfWorkAsync(recipientId, acquireReviewBarrier: false, recordExposure, expectedResultReleaseId);
        }
        result.ResultReleaseId = release.Id; result.ReleasedAtUtc = release.ReleasedAtUtc;
        result.ScoreOpensAtUtc = release.ScoreOpensAtUtc; result.ReviewOpensAtUtc = release.ReviewOpensAtUtc;
        var item = (await ReferencesAsync<EducationAssessmentResultReleaseItem>(x => x.ResultReleaseId == release.Id && x.RecipientId == recipientId &&
            x.StudentUserId == CurrentUser.Id && x.TenantId == CurrentTenant.Id)).SingleOrDefault();
        if (item == null)
        { if (result.Outcome != EducationAssessmentRecipientOutcome.Absent) throw Invalid(); return result; }
        result.TotalScore = item.TotalScore; result.MaxScore = item.MaxScore; result.SubmissionId = item.SubmissionId;
        if (result.CurrentResultVisibility != ExamResultVisibility.FullReview) return result;
        var sourceRevision = await ReferenceAsync<EducationAssessmentTaskRevision>(recipient.TaskRevisionId) ?? throw Invalid();
        result.AllowQuestionCollection = sourceRevision.AllowWrongQuestionCollection && task.PublishedRevisionId == sourceRevision.Id && release.ConfirmedGradeSetVersion == task.ConfirmedGradeSetVersion;
        var submission = await ReferenceAsync<EducationAssessmentSubmission>(item.SubmissionId) ?? throw Invalid();
        foreach (var reference in item.Grades.Grades)
        {
            result.Questions.Add(await StudentReleasedQuestionAsync(submission, reference));
        }
        if (recordExposure) await LazyServiceProvider.LazyGetRequiredService<EducationQuestionExposureRecorder>().RecordAsync(recipient, release);
        result.Questions = result.Questions.OrderBy(x => x.DisplayOrder).ToList(); return result;
    }
    private async Task<EducationAssessmentStudentQuestionResultDto> StudentReleasedQuestionAsync(EducationAssessmentSubmission submission, EducationAssessmentReleasedGradeReference reference)
    {
        var grade = await ReferenceAsync<EducationAssessmentGradeRevision>(reference.GradeRevisionId) ?? throw Invalid();
        var question = await ReferenceAsync<ExamPaperQuestion>(reference.PaperQuestionId) ?? throw Invalid();
        if (grade.SubmissionId != submission.Id || grade.TenantId != submission.TenantId || grade.QuestionGradeId != reference.QuestionGradeId || grade.PaperQuestionId != question.Id ||
            question.PaperId != submission.ExamPaperId || question.TenantId != submission.TenantId || grade.Kind != EducationAssessmentGradeRevisionKind.Confirmed) throw Invalid();
        var response = await SubmittedResponseAsync(submission, question.Id);
        var definition = question.ScoringDefinitionJson == null ? null : QuestionScoringDefinition.Parse(question.ScoringDefinitionJson);
        // Review permits the rubric, never the teacher's private curriculum/provenance identifiers.
        if (definition != null) { foreach (var blank in definition.Blanks) blank.TextbookKnowledgePointMappingId = null; foreach (var point in definition.ScoringPoints) point.TextbookKnowledgePointMappingId = null; }
        return new() { Id = question.Id, Type = question.Type, Title = question.Title, Stem = question.Stem, DisplayOrder = question.DisplayOrder,
            MaxScore = grade.MaxScore, ObtainedScore = grade.ObtainedScore!.Value, Response = response == null ? null : EducationAssessmentResponseValue.Parse(response.ResponseJson),
            Options = Parse<List<ExamPaperQuestionOptionDto>>(question.OptionsJson), CorrectAnswers = Parse<List<string>>(question.CorrectAnswersJson), Analysis = question.Analysis, ScoringDefinition = definition, Feedback = grade.Feedback,
            Parts = grade.Content.Parts.Select(x => new EducationAssessmentStudentGradePartDto { PartId = x.PartId, MaxScore = x.MaxScore, ObtainedScore = x.ObtainedScore }).ToList() };
    }
}
