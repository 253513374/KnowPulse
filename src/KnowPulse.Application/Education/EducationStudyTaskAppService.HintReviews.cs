using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.Permissions;
using KnowPulse.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationStudyTaskAppService
{
    private async Task HintTeacherAsync() { await TeacherAsync(); await CheckMvpPermissionAsync(EducationPermissions.AssessmentManage); await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary); }
    private sealed class HintCandidate
    {
        public ExamPaperQuestion Question { get; set; } = default!;
        public string AssessmentTitle { get; set; } = "";
    }
    private async Task<IQueryable<HintCandidate>> HintCandidatesQueryAsync(EducationStudyRecipient row, EducationStudyTask task)
    {
        var recipients = await Repo<EducationAssessmentRecipient>().GetQueryableAsync(); var revisions = await Repo<EducationAssessmentTaskRevision>().GetQueryableAsync();
        var papers = await Repo<ExamPaper>().GetQueryableAsync(); var questions = await Repo<ExamPaperQuestion>().GetQueryableAsync();
        return from recipient in recipients join revision in revisions on recipient.TaskRevisionId equals revision.Id join paper in papers on revision.ExamPaperId equals paper.Id
            join question in questions on paper.Id equals question.PaperId
            where recipient.TenantId == task.TenantId && revision.TenantId == task.TenantId && paper.TenantId == task.TenantId && question.TenantId == task.TenantId &&
                recipient.StudentUserId == row.StudentUserId && recipient.TaskId == revision.TaskId && revision.OwnerUserId == task.OwnerUserId &&
                revision.PublishedAtUtc.HasValue && revision.SchoolId == task.SchoolId && revision.AcademicTermId == task.AcademicTermId && revision.SchoolClassId == task.SchoolClassId &&
                revision.TeachingAssignmentId == task.TeachingAssignmentId && paper.PersonalOwnerUserId == task.OwnerUserId && paper.SnapshotVersion > 0 &&
                question.QuestionDefinitionRevisionId.HasValue && question.CurriculumSnapshotJson != null && question.CurriculumPointWeightsJson != null
            select new HintCandidate { Question = question, AssessmentTitle = revision.Title };
    }
    private async Task<(EducationStudyRecipient Recipient, EducationStudyTask Task, EducationStudyTaskRevision Publication, ExamPaperQuestion Question)> HintSourceAsync(Guid recipientId, Guid paperQuestionId)
    {
        await HintTeacherAsync(); var (row, task, publication) = await RecipientAsync(recipientId, false);
        if (row.FirstProvidedAtUtc == null) throw new AbpAuthorizationException();
        var source = await AsyncExecuter.FirstOrDefaultAsync((await HintCandidatesQueryAsync(row, task)).Where(x => x.Question.Id == paperQuestionId)) ?? throw new AbpAuthorizationException();
        if (!EducationStudyHintScope.Overlaps(publication.Scope(), source.Question.CurriculumSnapshotJson, source.Question.CurriculumPointWeightsJson)) throw new AbpAuthorizationException();
        return (row, task, publication, source.Question);
    }
    private static EducationStudyHintReviewRevisionDto HintRevision(EducationStudyHintReviewRevision row) => new() { Id = row.Id, RecipientId = row.RecipientId, PaperQuestionId = row.PaperQuestionId,
        RevisionNumber = row.RevisionNumber, BasedOnRevisionId = row.BasedOnRevisionId, Use = row.Use, Reason = row.Reason, RequestFingerprint = row.RequestFingerprint, RecordedAtUtc = row.RecordedAtUtc };
    private async Task<EducationStudyHintReviewRevision?> HintReceiptAsync(Guid recipientId, Guid paperQuestionId, Guid operationId, string? fingerprint = null)
    {
        await HintSourceAsync(recipientId, paperQuestionId); var value = await RefAsync<EducationStudyHintReviewRevision>(operationId); if (value == null) return null;
        if (value.TenantId != CurrentTenant.Id || value.OwnerUserId != CurrentUser.Id || value.RecipientId != recipientId || value.PaperQuestionId != paperQuestionId ||
            value.ReviewId != EducationStudyHintReview.Identity(recipientId, paperQuestionId)) throw new AbpAuthorizationException();
        if (fingerprint != null && value.RequestFingerprint != fingerprint) throw new BusinessException(EducationStudyTaskErrors.OperationConflict); return value;
    }
    [Authorize(EducationPermissions.LearningManage), Authorize(EducationPermissions.AssessmentManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationStudyHintCandidateDto>> GetHintCandidatesAsync(Guid recipientId, PagedResultRequestDto input)
    {
        await HintTeacherAsync(); var take = Page(input); using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (row, task, publication) = await RecipientAsync(recipientId, false);
        if (row.FirstProvidedAtUtc == null) throw new AbpAuthorizationException();
        var scope = publication.Scope();
        var candidates = await AsyncExecuter.ToListAsync((await HintCandidatesQueryAsync(row, task)).OrderBy(x => x.Question.Id)
            .Select(x => new { x.Question.Id, x.Question.Title, x.Question.DisplayOrder, x.AssessmentTitle, x.Question.CurriculumSnapshotJson, x.Question.CurriculumPointWeightsJson }).Take(3001));
        if (candidates.Count > 3000) throw new BusinessException(EducationLearningReportErrors.Capacity);
        var values = candidates.Where(x => EducationStudyHintScope.Overlaps(scope, x.CurriculumSnapshotJson, x.CurriculumPointWeightsJson)).DistinctBy(x => x.Id).ToList();
        var result = new PagedResultDto<EducationStudyHintCandidateDto>(values.Count, values.Skip(input.SkipCount).Take(take)
            .Select(x => new EducationStudyHintCandidateDto { Id = x.Id, QuestionTitle = x.Title, DisplayOrder = x.DisplayOrder, AssessmentTitle = x.AssessmentTitle }).ToList());
        await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.LearningManage), Authorize(EducationPermissions.AssessmentManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudyHintContextDto> GetHintContextAsync(Guid recipientId, Guid paperQuestionId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (row, task, publication, question) = await HintSourceAsync(recipientId, paperQuestionId);
        var root = await RefAsync<EducationStudyHintReview>(EducationStudyHintReview.Identity(row.Id, question.Id));
        if (root != null && (root.RecipientId != row.Id || root.OwnerUserId != row.OwnerUserId || root.StudentUserId != row.StudentUserId || root.PaperQuestionId != question.Id ||
            root.ExamPaperId != question.PaperId || root.ExposedAtUtc != row.FirstProvidedAtUtc)) throw Invalid();
        var current = root?.CurrentRevisionId is Guid id ? await HintReceiptAsync(row.Id, question.Id, id) ?? throw Invalid() : null;
        var scoring = question.ScoringDefinitionJson == null ? null : QuestionScoringDefinition.Parse(question.ScoringDefinitionJson); var scope = publication.Scope(); var content = publication.Content()!;
        var result = new EducationStudyHintContextDto { RecipientId = row.Id, TaskId = task.Id, PublicationId = publication.Id, VersionNumber = publication.RevisionNumber, StudentName = row.StudentName,
            ExposedAtUtc = row.FirstProvidedAtUtc!.Value, TaskTitle = publication.Title, Instructions = content.Instructions, StartsAtUtc = content.StartsAtUtc, EndsAtUtc = content.EndsAtUtc,
            EstimatedMinutes = content.EstimatedMinutes, SubjectName = scope.SubjectName, TextbookTitle = scope.TextbookTitle,
            ClassName = publication.ClassName, MaterialsAvailable = await MaterialsAvailableAsync(task, publication), Materials = content.Materials.Select(x => new EducationStudyMaterialDisplay(x.Id, x.Title)).ToList(),
            DefinitionRevisionId = question.QuestionDefinitionRevisionId!.Value, ConcurrencyStamp = root?.ConcurrencyStamp, Current = current == null ? null : HintRevision(current),
            Question = new() { PaperQuestionId = question.Id, Title = question.Title, Stem = question.Stem, ReferenceAnswer = scoring?.ReferenceAnswer, Analysis = question.Analysis,
                Options = System.Text.Json.JsonSerializer.Deserialize<List<ExamPaperQuestionOptionDto>>(question.OptionsJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? [],
                CorrectAnswers = System.Text.Json.JsonSerializer.Deserialize<List<string>>(question.CorrectAnswersJson) ?? [],
                BlankAnswers = scoring?.Blanks.Select(x => x.StemMarker + ": " + string.Join(" / ", x.AllowedAnswers)).ToList() ?? [], ScoringPoints = scoring?.ScoringPoints.Select(x => x.Description).ToList() ?? [] } };
        await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.LearningManage), Authorize(EducationPermissions.AssessmentManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudyHintReviewRevisionDto> SaveHintReviewAsync(Guid recipientId, Guid paperQuestionId, SaveEducationHintReviewDto input)
    {
        await HintTeacherAsync(); EducationRules.RequireId(input.OperationId); var fingerprint = EducationStudyTaskFingerprint.HintReview(recipientId, paperQuestionId, input);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var prior = await HintReceiptAsync(recipientId, paperQuestionId, input.OperationId, fingerprint);
        if (prior != null) { await uow.CompleteAsync(); return HintRevision(prior); }
        var (row, task, publication, question) = await HintSourceAsync(recipientId, paperQuestionId);
        if (input.Use == EducationLearningExposureUse.ConfirmedNoHints && !await MaterialsAvailableAsync(task, publication)) throw new BusinessException(EducationTeachingPlanErrors.MaterialUnavailable);
        var key = EducationStudyHintReview.Identity(recipientId, paperQuestionId); using var tracking = Repo<EducationStudyHintReview>().EnableTracking();
        var root = await Repo<EducationStudyHintReview>().FindAsync(key);
        if (root?.CurrentRevisionId != input.ExpectedRevisionId || root?.ConcurrencyStamp != input.ConcurrencyStamp) throw new AbpDbConcurrencyException();
        var created = root == null; root ??= new(row, publication, question);
        if (root.TenantId != row.TenantId || root.RecipientId != row.Id || root.OwnerUserId != row.OwnerUserId || root.StudentUserId != row.StudentUserId ||
            root.PaperQuestionId != question.Id || root.ExamPaperId != question.PaperId || root.ExposedAtUtc != row.FirstProvidedAtUtc) throw Invalid();
        var revision = new EducationStudyHintReviewRevision(input.OperationId, root, input.Use, input.ConfirmedExactContext, input.Reason, fingerprint, DateTime.UtcNow);
        if (created) await Repo<EducationStudyHintReview>().InsertAsync(root, autoSave: true);
        root.Advance(revision); await Repo<EducationStudyHintReviewRevision>().InsertAsync(revision, autoSave: true); await Repo<EducationStudyHintReview>().UpdateManyAsync([root], autoSave: true);
        await uow.CompleteAsync(); return HintRevision(revision);
    }
    [Authorize(EducationPermissions.LearningManage), Authorize(EducationPermissions.AssessmentManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudyHintReviewRevisionDto?> GetHintReviewOperationAsync(Guid recipientId, Guid paperQuestionId, Guid operationId)
    { using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var receipt = await HintReceiptAsync(recipientId, paperQuestionId, operationId); await uow.CompleteAsync(); return receipt == null ? null : HintRevision(receipt); }
}
