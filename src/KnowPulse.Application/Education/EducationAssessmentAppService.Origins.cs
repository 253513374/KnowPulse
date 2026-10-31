using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationAssessmentAppService
{
    private async Task<(EvidenceSource First, EvidenceSource Second, Guid Id)> EvidencePairAsync(Guid firstId, Guid secondId, bool write = false)
    {
        var first = await EvidenceSourceAsync(firstId); var second = await EvidenceSourceAsync(secondId);
        if (first.Scope.SchoolId != second.Scope.SchoolId || first.Scope.EducationSubjectId != second.Scope.EducationSubjectId ||
            first.Question.OriginalQuestionId == second.Question.OriginalQuestionId) throw new Volo.Abp.BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        if (write)
        {
            foreach (var taskId in new[] { first.Grade.TaskId, second.Grade.TaskId }.Distinct().Order()) await GradingTaskAsync(taskId, write: true);
            first = await EvidenceSourceAsync(firstId); second = await EvidenceSourceAsync(secondId);
        }
        if (first.Question.OriginalQuestionId.CompareTo(second.Question.OriginalQuestionId) > 0) (first, second) = (second, first);
        return (first, second, EducationQuestionOriginRelation.RelationId(CurrentTenant.Id, CurrentUser.Id!.Value,
            first.Scope.SchoolId, first.Scope.EducationSubjectId, first.Question.OriginalQuestionId, second.Question.OriginalQuestionId));
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationOriginCandidatesDto> GetOriginCandidatesAsync(Guid questionGradeId, PagedResultRequestDto input)
    {
        var source = await EvidenceSourceAsync(questionGradeId); var take = Page(input);
        using var gradesTracking = Repo<EducationAssessmentQuestionGrade>().DisableTracking();
        using var tasksTracking = Repo<EducationAssessmentTask>().DisableTracking();
        using var revisionsTracking = Repo<EducationAssessmentTaskRevision>().DisableTracking();
        using var questionsTracking = Repo<ExamPaperQuestion>().DisableTracking();
        using var teachingTracking = Repo<TeachingAssignment>().DisableTracking();
        var grades = await Repo<EducationAssessmentQuestionGrade>().GetQueryableAsync(); var tasks = await Repo<EducationAssessmentTask>().GetQueryableAsync();
        var revisions = await Repo<EducationAssessmentTaskRevision>().GetQueryableAsync(); var questions = await Repo<ExamPaperQuestion>().GetQueryableAsync();
        var assignments = await Repo<TeachingAssignment>().GetQueryableAsync(); var now = DateTime.UtcNow;
        var query = from grade in grades join task in tasks on grade.TaskId equals task.Id
            join revision in revisions on task.PublishedRevisionId equals (Guid?)revision.Id
            join question in questions on grade.PaperQuestionId equals question.Id
            join teaching in assignments on revision.TeachingAssignmentId equals teaching.Id
            where grade.TenantId == CurrentTenant.Id && grade.OwnerUserId == CurrentUser.Id && grade.StudentUserId == source.Grade.StudentUserId &&
                task.TenantId == CurrentTenant.Id && task.OwnerUserId == CurrentUser.Id && revision.TenantId == CurrentTenant.Id &&
                question.TenantId == CurrentTenant.Id && question.OriginalQuestionId != source.Question.OriginalQuestionId &&
                revision.SchoolId == source.Scope.SchoolId && teaching.TenantId == CurrentTenant.Id && teaching.EducationSubjectId == source.Scope.EducationSubjectId &&
                teaching.EffectiveFromUtc <= now && (!teaching.EffectiveToUtc.HasValue || now < teaching.EffectiveToUtc.Value)
            orderby revision.PublishedAtUtc descending, grade.Id
            select grade.Id;
        var rows = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(take + 1));
        var result = new EducationOriginCandidatesDto { HasMore = rows.Count > take, NextSkipCount = checked(input.SkipCount + Math.Min(take, rows.Count)) };
        foreach (var id in rows.Take(take))
        {
            try
            {
                var (candidate, _, _) = await EvidencePairAsync(id, questionGradeId);
                var actual = candidate.Grade.Id == id ? candidate : await EvidenceSourceAsync(id);
                result.Items.Add(new() { QuestionGradeId = id, TaskTitle = actual.TaskRevision.Title, QuestionTitle = actual.Question.Title, QuestionStem = actual.Question.Stem });
            }
            catch (AbpAuthorizationException) { }
        }
        await EvidenceSourceAsync(questionGradeId);
        return result;
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationOriginRelationDto> GetOriginRelationAsync(Guid questionGradeId, Guid secondQuestionGradeId)
    {
        var (_, _, id) = await EvidencePairAsync(questionGradeId, secondQuestionGradeId);
        var root = await ReferenceAsync<EducationQuestionOriginRelation>(id);
        var current = root?.CurrentRevisionId is Guid revisionId ? await OriginReceiptAsync(questionGradeId, secondQuestionGradeId, revisionId) : null;
        return new() { RelationId = id, ConcurrencyStamp = root?.ConcurrencyStamp, Current = current == null ? null : GradeDto<EducationOriginRelationRevisionDto>(current) };
    }

    private async Task<EducationQuestionOriginRelationRevision?> OriginReceiptAsync(Guid first, Guid second, Guid operationId, string? fingerprint = null)
    {
        var (_, _, id) = await EvidencePairAsync(first, second);
        var receipt = await ReferenceAsync<EducationQuestionOriginRelationRevision>(operationId); if (receipt == null) return null;
        Own(receipt.OwnerUserId, receipt.TenantId);
        if (receipt.RelationId != id) throw new AbpAuthorizationException();
        var (recordedFirst, recordedSecond, recordedId) = await EvidencePairAsync(receipt.FirstQuestionGradeId, receipt.SecondQuestionGradeId);
        if (recordedId != id || recordedFirst.Question.QuestionDefinitionRevisionId != receipt.FirstDefinitionRevisionId ||
            recordedSecond.Question.QuestionDefinitionRevisionId != receipt.SecondDefinitionRevisionId) throw Invalid();
        if (fingerprint != null && receipt.RequestFingerprint != fingerprint) throw Conflict();
        return receipt;
    }

    [Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationOriginRelationRevisionDto> SaveOriginRelationAsync(Guid questionGradeId, SaveEducationOriginRelationDto input)
    {
        await TeacherAsync(); EducationRules.RequireId(input.OperationId);
        var fingerprint = Hash(new { Command = "question-origin-relation", questionGradeId, input });
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var prior = await OriginReceiptAsync(questionGradeId, input.SecondQuestionGradeId, input.OperationId, fingerprint);
            if (prior != null) return await CompleteAsync(uow, () => Task.FromResult(GradeDto<EducationOriginRelationRevisionDto>(prior)));
            var (first, second, id) = await EvidencePairAsync(questionGradeId, input.SecondQuestionGradeId, write: true);
            using var tracking = Repo<EducationQuestionOriginRelation>().EnableTracking();
            var root = await Repo<EducationQuestionOriginRelation>().FindAsync(id);
            EvidenceStamp(input, root?.CurrentRevisionId, root?.ConcurrencyStamp);
            var created = root == null; root ??= new(CurrentTenant.Id, CurrentUser.Id!.Value, first.Scope.SchoolId, first.Scope.EducationSubjectId,
                first.Question.OriginalQuestionId, second.Question.OriginalQuestionId);
            var revision = new EducationQuestionOriginRelationRevision(input.OperationId, root, first.Grade, first.Question, second.Grade, second.Question,
                input.IsEnabled, input.Reason, root.ConcurrencyStamp, fingerprint, DateTime.UtcNow);
            if (created) await Repo<EducationQuestionOriginRelation>().InsertAsync(root, autoSave: true);
            root.Advance(revision);
            await Repo<EducationQuestionOriginRelationRevision>().InsertAsync(revision, autoSave: true);
            await Repo<EducationQuestionOriginRelation>().UpdateManyAsync([root], autoSave: true);
            return await CompleteAsync(uow, () => Task.FromResult(GradeDto<EducationOriginRelationRevisionDto>(revision)));
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            var receipt = await OriginReceiptAsync(questionGradeId, input.SecondQuestionGradeId, input.OperationId, fingerprint); if (receipt == null) throw;
            return await CompleteAsync(recovery, () => Task.FromResult(GradeDto<EducationOriginRelationRevisionDto>(receipt)));
        }
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationOriginRelationRevisionDto?> GetOriginOperationAsync(Guid questionGradeId, Guid secondQuestionGradeId, Guid operationId)
    {
        var receipt = await OriginReceiptAsync(questionGradeId, secondQuestionGradeId, operationId);
        return receipt == null ? null : GradeDto<EducationOriginRelationRevisionDto>(receipt);
    }
}
