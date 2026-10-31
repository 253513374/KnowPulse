using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationAssessmentAppService
{
    private async Task<(EvidenceSource Source, EducationQuestionExposure Exposure, EducationStudentFeedbackRevision Feedback)> HintContextAsync(Guid gradeId, Guid exposureId, bool write = false)
    {
        await TeacherAsync(); await CheckMvpPermissionAsync(EducationPermissions.FeedbackManage);
        var source = await EvidenceSourceAsync(gradeId, write);
        var exposure = await ReferenceAsync<EducationQuestionExposure>(exposureId) ?? throw new AbpAuthorizationException();
        if (exposure.TenantId != CurrentTenant.Id || exposure.SchoolId != source.Scope.SchoolId || exposure.EducationSubjectId != source.Scope.EducationSubjectId ||
            exposure.StudentUserId != source.Grade.StudentUserId || exposure.OriginalQuestionId != source.Question.OriginalQuestionId ||
            exposure.Source != EducationQuestionExposureSource.StudentFeedback || !exposure.FeedbackRevisionId.HasValue || !exposure.FeedbackId.HasValue)
            throw new AbpAuthorizationException();
        await GradingTaskAsync(exposure.TaskId);
        await LazyServiceProvider.LazyGetRequiredService<IEducationFeedbackAppService>().GetAsync(exposure.FeedbackId.Value);
        if (write)
        {
            using var tracking = Repo<IdentityUser>().EnableTracking();
            var student = await Repo<IdentityUser>().GetAsync(exposure.StudentUserId);
            if (!student.IsActive || student.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
            await Repo<IdentityUser>().UpdateManyAsync([student], autoSave: true);
            await LazyServiceProvider.LazyGetRequiredService<IEducationFeedbackAppService>().GetAsync(exposure.FeedbackId.Value);
        }
        var feedback = await ReferenceAsync<EducationStudentFeedbackRevision>(exposure.FeedbackRevisionId.Value) ?? throw Invalid();
        Own(feedback.OwnerUserId, feedback.TenantId);
        if (feedback.FeedbackId != exposure.FeedbackId || feedback.StudentUserId != exposure.StudentUserId || feedback.Kind != EducationFeedbackRevisionKind.Published ||
            feedback.Content == null || !feedback.Content.AssessmentTaskIds.Contains(exposure.TaskId)) throw Invalid();
        return (source, exposure, feedback);
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<PagedResultDto<EducationEvidenceExposureDto>> GetExposureContextsAsync(Guid questionGradeId, PagedResultRequestDto input)
    {
        var source = await EvidenceSourceAsync(questionGradeId); var take = Page(input);
        using var tracking = Repo<EducationQuestionExposure>().DisableTracking();
        var query = (await Repo<EducationQuestionExposure>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id &&
            x.StudentUserId == source.Grade.StudentUserId && x.SchoolId == source.Scope.SchoolId && x.EducationSubjectId == source.Scope.EducationSubjectId &&
            x.OriginalQuestionId == source.Question.OriginalQuestionId);
        using var planTracking = Repo<EducationTeachingPlanQuestionExposure>().DisableTracking();
        var headers = query.Select(x => new { x.Id, x.Source, x.ExposedAtUtc, FormalActivityId = (Guid?)null }).Concat(
            (await Repo<EducationTeachingPlanQuestionExposure>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id &&
                x.StudentUserId == source.Grade.StudentUserId && x.SchoolId == source.Scope.SchoolId && x.EducationSubjectId == source.Scope.EducationSubjectId &&
                x.OriginalQuestionId == source.Question.OriginalQuestionId).Select(x => new { x.Id, Source = EducationQuestionExposureSource.TeachingPlan, x.ExposedAtUtc, FormalActivityId = (Guid?)null }));
        using var fqTracking = Repo<EducationTeachingFormalQuestion>().DisableTracking(); using var fbTracking = Repo<EducationTeachingFormalAssessment>().DisableTracking();
        using var activityTracking = Repo<EducationTeachingActivity>().DisableTracking();
        var formalQuestions = await Repo<EducationTeachingFormalQuestion>().GetQueryableAsync(); var formalBindings = await Repo<EducationTeachingFormalAssessment>().GetQueryableAsync();
        var activities = await Repo<EducationTeachingActivity>().GetQueryableAsync();
        headers = headers.Concat(from question in formalQuestions join binding in formalBindings on question.BindingId equals binding.Id
            join activity in activities on binding.ActivityId equals activity.Id
            where question.TenantId == CurrentTenant.Id && binding.TenantId == CurrentTenant.Id && activity.TenantId == CurrentTenant.Id &&
                binding.StudentUserId == source.Grade.StudentUserId && binding.SchoolId == source.Scope.SchoolId &&
                binding.EducationSubjectId == source.Scope.EducationSubjectId && question.OriginalQuestionId == source.Question.OriginalQuestionId &&
                activity.FirstProvidedAtUtc.HasValue
            select new { Id = question.Id, Source = EducationQuestionExposureSource.FormalAssessmentPlan, ExposedAtUtc = activity.FirstProvidedAtUtc!.Value, FormalActivityId = (Guid?)activity.Id });
        var study = await LazyServiceProvider.LazyGetRequiredService<EducationStudyHintContextReader>().ExactAsync(source.Scope, [source.Grade.StudentUserId],
            [new(source.Question.Id, source.Question.OriginalQuestionId, source.Question.CurriculumSnapshotJson, source.Question.CurriculumPointWeightsJson)], DateTime.UtcNow);
        var knownStudy = await LazyServiceProvider.LazyGetRequiredService<EducationStudyHintContextReader>().ConfirmedAsync(source.Scope, [source.Grade.StudentUserId],
            [source.Question.OriginalQuestionId], DateTime.UtcNow);
        study.AddRange(knownStudy.Where(x => !study.Any(s => s.RecipientId == x.RecipientId && s.PaperQuestionId == x.PaperQuestionId)));
        var count = await AsyncExecuter.CountAsync(headers) + study.Count;
        var headerSkip = Math.Max(0, input.SkipCount - study.Count);
        var rows = await AsyncExecuter.ToListAsync(headers.OrderBy(x => x.ExposedAtUtc).ThenBy(x => x.Id).ThenBy(x => x.Source).Skip(headerSkip).Take(take + study.Count));
        rows.AddRange(study.Select(x => new { x.Id, Source = EducationQuestionExposureSource.StudyTask, x.ExposedAtUtc, FormalActivityId = (Guid?)null }));
        rows = rows.OrderBy(x => x.ExposedAtUtc).ThenBy(x => x.Id).ThenBy(x => x.Source).Skip(input.SkipCount - headerSkip).Take(take).ToList();
        var studyById = study.ToDictionary(x => x.Id);
        var result = new List<EducationEvidenceExposureDto>();
        var formalUses = new Dictionary<Guid, EducationLearningExposureUse?>();
        foreach (var row in rows)
        {
            var dto = new EducationEvidenceExposureDto { Id = row.Id, Source = row.Source, ExposedAtUtc = row.ExposedAtUtc,
                Use = row.Source == EducationQuestionExposureSource.AssessmentReview ? EducationLearningExposureUse.Confirmed : EducationLearningExposureUse.Unverified };
            if (row.Source == EducationQuestionExposureSource.StudyTask) {
                var context = studyById[row.Id]; dto.Use = context.Use;
                if (context.OwnerId == CurrentUser.Id && await IsMvpPermissionGrantedAsync(EducationPermissions.LearningManage) &&
                    await IsMvpPermissionGrantedAsync(KnowPulse.Permissions.KnowPulsePermissions.KnowledgeBase.PersonalLibrary)) {
                    try {
                        await LazyServiceProvider.LazyGetRequiredService<IEducationStudyHintReviewAppService>().GetHintContextAsync(context.RecipientId, context.PaperQuestionId);
                        dto.CanReview = true; dto.ReviewStudyRecipientId = context.RecipientId; dto.ReviewPaperQuestionId = context.PaperQuestionId;
                    } catch (AbpAuthorizationException) { }
                }
            }
            if (row.Source == EducationQuestionExposureSource.StudentFeedback && await IsMvpPermissionGrantedAsync(EducationPermissions.FeedbackManage))
            {
                try
                {
                    var (_, actual, _) = await HintContextAsync(questionGradeId, row.Id);
                    var root = await ReferenceAsync<EducationExposureHintReview>(row.Id);
                    var current = root?.CurrentRevisionId is Guid id ? await ReferenceAsync<EducationExposureHintReviewRevision>(id) : null;
                    dto.Use = actual.ToLearningEvidence(current).Use; dto.CanReview = true;
                }
                catch (AbpAuthorizationException) { }
            }
            if (row.Source == EducationQuestionExposureSource.TeachingPlan && await IsMvpPermissionGrantedAsync(EducationPermissions.TeachingPlanManage) &&
                await IsMvpPermissionGrantedAsync(EducationPermissions.AnalysisManage))
            {
                try {
                    var context = await LazyServiceProvider.LazyGetRequiredService<IEducationTeachingPlanAppService>().GetHintContextAsync(row.Id);
                    dto.Use = context.Current?.Use ?? EducationLearningExposureUse.Unverified; dto.CanReview = true;
                } catch (AbpAuthorizationException) { }
            }
            if (row.Source == EducationQuestionExposureSource.FormalAssessmentPlan) {
                dto.Id = EducationTeachingFormalQuestion.ExposureIdFor(row.Id);
                if (row.FormalActivityId is Guid activityId && await IsMvpPermissionGrantedAsync(EducationPermissions.TeachingPlanManage) &&
                    await IsMvpPermissionGrantedAsync(EducationPermissions.AnalysisManage)) {
                    if (!formalUses.ContainsKey(activityId)) {
                        try { formalUses[activityId] = await LazyServiceProvider.LazyGetRequiredService<EducationTeachingPlanAppService>().FormalContextUseForReviewAsync(activityId); }
                        catch (AbpAuthorizationException) { formalUses[activityId] = null; }
                    }
                    if (formalUses[activityId] is { } use) { dto.Use = use; dto.CanReview = true; dto.ReviewActivityId = activityId; }
                }
            }
            result.Add(dto);
        }
        await EvidenceSourceAsync(questionGradeId);
        return new(count, result);
    }

    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<EducationHintContextDto> GetHintContextAsync(Guid questionGradeId, Guid exposureId)
    {
        var (_, exposure, feedback) = await HintContextAsync(questionGradeId, exposureId);
        var question = await ReferenceAsync<ExamPaperQuestion>(exposure.PaperQuestionId) ?? throw Invalid();
        if (question.TenantId != exposure.TenantId || question.PaperId != exposure.ExamPaperId || question.QuestionDefinitionRevisionId != exposure.QuestionDefinitionRevisionId) throw Invalid();
        var root = await ReferenceAsync<EducationExposureHintReview>(exposureId);
        var current = root?.CurrentRevisionId is Guid id ? await HintReceiptAsync(questionGradeId, exposureId, id) : null;
        var definition = question.ScoringDefinitionJson == null ? null : QuestionScoringDefinition.Parse(question.ScoringDefinitionJson);
        return new() { ExposureId = exposureId, FeedbackRevisionId = feedback.Id, FeedbackRevisionNumber = feedback.RevisionNumber, ExposedAtUtc = exposure.ExposedAtUtc,
            PaperQuestionId = question.Id, QuestionDefinitionRevisionId = exposure.QuestionDefinitionRevisionId,
            Context = feedback.Content!.Context, Text = feedback.Content.Text, QuestionTitle = question.Title, QuestionStem = question.Stem,
            ReferenceAnswer = definition?.ReferenceAnswer,
            CorrectAnswers = Parse<List<string>>(question.CorrectAnswersJson),
            BlankAnswers = definition?.Blanks.Select(x => x.StemMarker + ": " + string.Join(" / ", x.AllowedAnswers)).ToList() ?? [],
            ConcurrencyStamp = root?.ConcurrencyStamp, Current = current == null ? null : GradeDto<EducationHintReviewRevisionDto>(current) };
    }

    private async Task<EducationExposureHintReviewRevision?> HintReceiptAsync(Guid gradeId, Guid exposureId, Guid operationId, string? fingerprint = null)
    {
        await HintContextAsync(gradeId, exposureId);
        var receipt = await ReferenceAsync<EducationExposureHintReviewRevision>(operationId); if (receipt == null) return null;
        Own(receipt.OwnerUserId, receipt.TenantId);
        if (receipt.ExposureId != exposureId) throw new AbpAuthorizationException();
        if (fingerprint != null && receipt.RequestFingerprint != fingerprint) throw Conflict();
        return receipt;
    }

    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.FeedbackManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationHintReviewRevisionDto> SaveHintReviewAsync(Guid questionGradeId, Guid exposureId, SaveEducationHintReviewDto input)
    {
        await TeacherAsync(); await CheckMvpPermissionAsync(EducationPermissions.FeedbackManage); EducationRules.RequireId(input.OperationId);
        var fingerprint = Hash(new { Command = "exposure-hint-review", questionGradeId, exposureId, input });
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var prior = await HintReceiptAsync(questionGradeId, exposureId, input.OperationId, fingerprint);
            if (prior != null) return await CompleteAsync(uow, () => Task.FromResult(GradeDto<EducationHintReviewRevisionDto>(prior)));
            var (_, exposure, feedback) = await HintContextAsync(questionGradeId, exposureId, write: true);
            using var tracking = Repo<EducationExposureHintReview>().EnableTracking();
            var root = await Repo<EducationExposureHintReview>().FindAsync(exposureId);
            EvidenceStamp(input, root?.CurrentRevisionId, root?.ConcurrencyStamp);
            var created = root == null; root ??= new(exposure, feedback);
            var revision = new EducationExposureHintReviewRevision(input.OperationId, root, input.Use, input.ConfirmedExactContext,
                input.Reason, root.ConcurrencyStamp, fingerprint, DateTime.UtcNow);
            if (created) await Repo<EducationExposureHintReview>().InsertAsync(root, autoSave: true);
            root.Advance(revision);
            await Repo<EducationExposureHintReviewRevision>().InsertAsync(revision, autoSave: true);
            await Repo<EducationExposureHintReview>().UpdateManyAsync([root], autoSave: true);
            return await CompleteAsync(uow, () => Task.FromResult(GradeDto<EducationHintReviewRevisionDto>(revision)));
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            var receipt = await HintReceiptAsync(questionGradeId, exposureId, input.OperationId, fingerprint); if (receipt == null) throw;
            return await CompleteAsync(recovery, () => Task.FromResult(GradeDto<EducationHintReviewRevisionDto>(receipt)));
        }
    }

    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.FeedbackManage)]
    public virtual async Task<EducationHintReviewRevisionDto?> GetHintReviewOperationAsync(Guid questionGradeId, Guid exposureId, Guid operationId)
    {
        var receipt = await HintReceiptAsync(questionGradeId, exposureId, operationId);
        return receipt == null ? null : GradeDto<EducationHintReviewRevisionDto>(receipt);
    }
}
