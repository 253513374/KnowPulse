using System;
using System.Threading.Tasks;
using KnowPulse.Exam;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationAssessmentAppService
{
    private sealed record EvidenceSource(EducationAssessmentQuestionGrade Grade, EducationAssessmentSubmission Submission,
        EducationAssessmentAttempt Attempt, ExamPaperQuestion Question, EducationAssessmentTaskRevision TaskRevision, EducationCurriculumSnapshot Scope);

    private async Task<EvidenceSource> EvidenceSourceAsync(Guid id, bool write = false)
    {
        await TeacherAsync();
        var grade = await ReferenceAsync<EducationAssessmentQuestionGrade>(id) ?? throw new AbpAuthorizationException();
        Own(grade.OwnerUserId, grade.TenantId);
        var (submission, attempt) = await GradeSubmissionAsync(grade.SubmissionId, write);
        var question = await AnswerQuestionAsync(attempt, grade.PaperQuestionId);
        var revision = await ReferenceAsync<EducationAssessmentTaskRevision>(attempt.TaskRevisionId) ?? throw Invalid();
        if (grade.TaskId != attempt.TaskId || grade.AttemptId != attempt.Id || grade.StudentUserId != attempt.StudentUserId ||
            grade.ExamPaperId != question.PaperId || !question.QuestionDefinitionRevisionId.HasValue) throw Invalid();
        return new(grade, submission, attempt, question, revision, Parse<EducationCurriculumSnapshot>(revision.CurriculumSnapshotJson));
    }

    private static void EvidenceStamp(EducationEvidenceDecisionInput input, Guid? current, string? stamp)
    {
        if (input.ExpectedRevisionId != current || input.ConcurrencyStamp != stamp) throw new AbpDbConcurrencyException();
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationQuestionEvidenceDto> GetEvidenceAsync(Guid questionGradeId)
    {
        var source = await EvidenceSourceAsync(questionGradeId);
        var root = await ReferenceAsync<EducationEvidenceQualification>(questionGradeId);
        var recipient = await ReferenceAsync<EducationAssessmentRecipient>(source.Attempt.RecipientId) ?? throw Invalid();
        var current = root?.CurrentRevisionId is Guid id ? await QualificationReceiptAsync(questionGradeId, id) : null;
        return new() { QuestionGradeId = questionGradeId, TaskId = source.Grade.TaskId, SubmissionId = source.Grade.SubmissionId,
            TaskTitle = source.TaskRevision.Title, StudentName = recipient.StudentName, QuestionTitle = source.Question.Title, QuestionStem = source.Question.Stem,
            ConcurrencyStamp = root?.ConcurrencyStamp, Current = current == null ? null : GradeDto<EducationEvidenceQualificationRevisionDto>(current) };
    }

    private async Task<EducationEvidenceQualificationRevision?> QualificationReceiptAsync(Guid gradeId, Guid operationId, string? fingerprint = null)
    {
        await EvidenceSourceAsync(gradeId);
        var receipt = await ReferenceAsync<EducationEvidenceQualificationRevision>(operationId);
        if (receipt == null) return null;
        Own(receipt.OwnerUserId, receipt.TenantId);
        if (receipt.QuestionGradeId != gradeId) throw new AbpAuthorizationException();
        if (fingerprint != null && receipt.RequestFingerprint != fingerprint) throw Conflict();
        return receipt;
    }

    [Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationEvidenceQualificationRevisionDto> SaveEvidenceQualificationAsync(Guid questionGradeId, SaveEducationEvidenceQualificationDto input)
    {
        await TeacherAsync(); EducationRules.RequireId(input.OperationId);
        var fingerprint = Hash(new { Command = "evidence-qualification", questionGradeId, input });
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var prior = await QualificationReceiptAsync(questionGradeId, input.OperationId, fingerprint);
            if (prior != null) return await CompleteAsync(uow, () => Task.FromResult(GradeDto<EducationEvidenceQualificationRevisionDto>(prior)));
            var source = await EvidenceSourceAsync(questionGradeId, write: true);
            using var tracking = Repo<EducationEvidenceQualification>().EnableTracking();
            var root = await Repo<EducationEvidenceQualification>().FindAsync(questionGradeId);
            EvidenceStamp(input, root?.CurrentRevisionId, root?.ConcurrencyStamp);
            var created = root == null; root ??= new(source.Grade, source.Question);
            var revision = new EducationEvidenceQualificationRevision(input.OperationId, root, input.Use, input.NoHintsOrAnswersConfirmed,
                input.NewRequirementsConfirmed, input.OriginalRelationshipsChecked, input.Reason, root.ConcurrencyStamp, fingerprint, DateTime.UtcNow);
            if (created) await Repo<EducationEvidenceQualification>().InsertAsync(root, autoSave: true);
            root.Advance(revision);
            await Repo<EducationEvidenceQualificationRevision>().InsertAsync(revision, autoSave: true);
            await Repo<EducationEvidenceQualification>().UpdateManyAsync([root], autoSave: true);
            return await CompleteAsync(uow, () => Task.FromResult(GradeDto<EducationEvidenceQualificationRevisionDto>(revision)));
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            var receipt = await QualificationReceiptAsync(questionGradeId, input.OperationId, fingerprint); if (receipt == null) throw;
            return await CompleteAsync(recovery, () => Task.FromResult(GradeDto<EducationEvidenceQualificationRevisionDto>(receipt)));
        }
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationEvidenceQualificationRevisionDto?> GetQualificationOperationAsync(Guid questionGradeId, Guid operationId)
    {
        var receipt = await QualificationReceiptAsync(questionGradeId, operationId);
        return receipt == null ? null : GradeDto<EducationEvidenceQualificationRevisionDto>(receipt);
    }
}
