using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationTeachingPlanAppService
{
    private async Task HintTeacherAsync() { await CheckMvpPermissionAsync(EducationPermissions.AssessmentManage); await TeacherAsync(); }
    private async Task<(EducationTeachingPlanQuestionExposure Exposure, EducationTeachingPlanRevision Published, ExamPaperQuestion Question)> HintSourceAsync(Guid id, bool write = false)
    {
        await HintTeacherAsync();
        var exposure = await RefAsync<EducationTeachingPlanQuestionExposure>(id) ?? throw new AbpAuthorizationException();
        if (exposure.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        var plan = await OwnAsync(exposure.PlanId, write);
        var published = await RefAsync<EducationTeachingPlanRevision>(exposure.PlanRevisionId) ?? throw Invalid();
        _ = new EducationTeachingPlanHintReview(exposure, published);
        var question = await RefAsync<ExamPaperQuestion>(exposure.PaperQuestionId) ?? throw Invalid();
        if (published.OwnerUserId != plan.OwnerUserId || exposure.StudentUserId != plan.StudentUserId || exposure.SchoolId != plan.SchoolId ||
            exposure.EducationSubjectId != plan.EducationSubjectId || question.TenantId != plan.TenantId || question.PaperId != exposure.ExamPaperId ||
            question.QuestionDefinitionRevisionId != exposure.DefinitionRevisionId || question.OriginalQuestionId != exposure.OriginalQuestionId) throw Invalid();
        return (exposure, published, question);
    }
    private static EducationTeachingPlanHintReviewRevisionDto MapHint(EducationTeachingPlanHintReviewRevision revision) => new() {
        Id = revision.Id, ExposureId = revision.ExposureId, PlanRevisionId = revision.PlanRevisionId, RevisionNumber = revision.RevisionNumber,
        BasedOnRevisionId = revision.BasedOnRevisionId, Use = revision.Use, Reason = revision.Reason,
        RequestFingerprint = revision.RequestFingerprint, RecordedAtUtc = revision.RecordedAtUtc };

    private async Task<EducationTeachingPlanHintReviewRevision?> HintReceiptAsync(Guid exposureId, Guid operationId, string? fingerprint = null)
    {
        await HintSourceAsync(exposureId);
        var receipt = await RefAsync<EducationTeachingPlanHintReviewRevision>(operationId); if (receipt == null) return null;
        if (receipt.TenantId != CurrentTenant.Id || receipt.OwnerUserId != CurrentUser.Id || receipt.ExposureId != exposureId)
            throw new AbpAuthorizationException();
        if (fingerprint != null && receipt.RequestFingerprint != fingerprint) throw new Volo.Abp.BusinessException(EducationTeachingPlanErrors.OperationConflict);
        return receipt;
    }

    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationTeachingPlanHintContextDto> GetHintContextAsync(Guid exposureId)
    {
        await HintTeacherAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var (exposure, published, question) = await HintSourceAsync(exposureId, write: true);
        var root = await RefAsync<EducationTeachingPlanHintReview>(exposureId);
        var current = root?.CurrentRevisionId is Guid id ? await HintReceiptAsync(exposureId, id) ?? throw Invalid() : null;
        var definition = question.ScoringDefinitionJson == null ? null : QuestionScoringDefinition.Parse(question.ScoringDefinitionJson);
        var result = new EducationTeachingPlanHintContextDto { ExposureId = exposure.Id, PlanId = exposure.PlanId, PlanRevisionId = published.Id,
            PlanRevisionNumber = published.RevisionNumber, ExposedAtUtc = exposure.ExposedAtUtc, Content = PublicContent(published),
            QuestionTitle = question.Title, QuestionStem = question.Stem, ReferenceAnswer = definition?.ReferenceAnswer,
            CorrectAnswers = JsonSerializer.Deserialize<List<string>>(question.CorrectAnswersJson) ?? [],
            BlankAnswers = definition?.Blanks.Select(x => x.StemMarker + ": " + string.Join(" / ", x.AllowedAnswers)).ToList() ?? [],
            ConcurrencyStamp = root?.ConcurrencyStamp, Current = current == null ? null : MapHint(current) };
        await uow.CompleteAsync(); return result;
    }

    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationTeachingPlanHintReviewRevisionDto> SaveHintReviewAsync(Guid exposureId, SaveEducationHintReviewDto input)
    {
        await HintTeacherAsync(); EducationRules.RequireId(input.OperationId);
        var fingerprint = Hash(new { Command = "plan-context-hint-review", exposureId, input });
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var prior = await HintReceiptAsync(exposureId, input.OperationId, fingerprint);
        if (prior != null) { await uow.CompleteAsync(); return MapHint(prior); }
        var (exposure, published, _) = await HintSourceAsync(exposureId, write: true);
        if (input.Use == EducationLearningExposureUse.ConfirmedNoHints) await ValidateMaterialsAsync(await OwnAsync(exposure.PlanId), published.Content()!);
        using var tracking = Repo<EducationTeachingPlanHintReview>().EnableTracking();
        var root = await Repo<EducationTeachingPlanHintReview>().FindAsync(exposureId);
        if (root?.CurrentRevisionId != input.ExpectedRevisionId || root?.ConcurrencyStamp != input.ConcurrencyStamp) throw new AbpDbConcurrencyException();
        var created = root == null; root ??= new(exposure, published);
        var revision = new EducationTeachingPlanHintReviewRevision(input.OperationId, root, input.Use, input.ConfirmedExactContext, input.Reason,
            root.ConcurrencyStamp, fingerprint, DateTime.UtcNow);
        if (created) await Repo<EducationTeachingPlanHintReview>().InsertAsync(root, autoSave: true);
        root.Advance(revision); await Repo<EducationTeachingPlanHintReviewRevision>().InsertAsync(revision, autoSave: true);
        await Repo<EducationTeachingPlanHintReview>().UpdateManyAsync([root], autoSave: true); await uow.CompleteAsync(); return MapHint(revision);
    }

    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationTeachingPlanHintReviewRevisionDto?> GetHintReviewOperationAsync(Guid exposureId, Guid operationId)
    {
        var receipt = await HintReceiptAsync(exposureId, operationId); return receipt == null ? null : MapHint(receipt);
    }
}
