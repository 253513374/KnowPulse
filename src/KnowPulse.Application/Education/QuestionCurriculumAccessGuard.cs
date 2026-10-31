using System.Collections.Generic;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.QuestionBank;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.Education;

[RemoteService(false)]
public class QuestionCurriculumAccessGuard : KnowPulseAppService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static List<EducationCurriculumScoringPointDto> GetScoringPoints(string? snapshotJson, string? weightsJson)
    {
        if (snapshotJson == null || weightsJson == null) return [];
        var snapshot = JsonSerializer.Deserialize<EducationCurriculumSnapshot>(snapshotJson, JsonOptions)!;
        var weights = JsonSerializer.Deserialize<List<QuestionCurriculumPointWeight>>(weightsJson, JsonOptions) ?? [];
        return weights.Select(weight =>
        {
            var mapping = snapshot.Mappings.Single(x => x.MappingId == weight.TextbookKnowledgePointMappingId);
            return new EducationCurriculumScoringPointDto
            {
                TextbookKnowledgePointMappingId = mapping.MappingId, Weight = weight.Weight, Score = weight.Score,
                KnowledgePointId = mapping.KnowledgePointId, KnowledgePointRevisionId = mapping.KnowledgePointRevisionId,
                KnowledgePointCode = mapping.KnowledgePointCode, KnowledgePointName = mapping.KnowledgePointName,
                ChapterCode = mapping.ChapterCode, ChapterTitle = mapping.ChapterTitle
            };
        }).ToList();
    }

    internal static bool MatchesScope(Question question, EducationCurriculumSnapshot? scope)
    {
        if (scope == null) return question.CurriculumSnapshotJson == null;
        if (question.CurriculumSnapshotJson == null || !question.CurriculumConfirmed) return false;
        var actual = JsonSerializer.Deserialize<EducationCurriculumSnapshot>(question.CurriculumSnapshotJson, JsonOptions);
        var weights = JsonSerializer.Deserialize<List<QuestionCurriculumPointWeight>>(question.CurriculumPointWeightsJson, JsonOptions) ?? [];
        var selectedIds = scope.Mappings.Select(x => x.MappingId).ToHashSet();
        return actual != null && actual.TeacherUserId == scope.TeacherUserId && actual.TenantId == scope.TenantId &&
            actual.TextbookVersionId == scope.TextbookVersionId && actual.SchoolId == scope.SchoolId &&
            actual.TeacherTextbookSelectionId == scope.TeacherTextbookSelectionId && weights.Count > 0 &&
            weights.All(x => selectedIds.Contains(x.TextbookKnowledgePointMappingId));
    }

    public virtual async Task EnsureAsync(Question question, bool requireConfirmed = true, bool forUpdate = false)
    {
        var repository = LazyServiceProvider.LazyGetRequiredService<IRepository<Question, Guid>>();
        using var tracking = forUpdate ? repository.EnableTracking() : null;
        if (forUpdate)
        {
            var tracked = await repository.GetAsync(question.Id);
            if (tracked.ConcurrencyStamp != question.ConcurrencyStamp || tracked.CurriculumSnapshotJson != question.CurriculumSnapshotJson ||
                tracked.CurriculumPointWeightsJson != question.CurriculumPointWeightsJson || tracked.CurriculumSourceReferencesJson != question.CurriculumSourceReferencesJson ||
                tracked.CurriculumConfirmed != question.CurriculumConfirmed || tracked.ScoringDefinitionJson != question.ScoringDefinitionJson ||
                tracked.ApprovedDefinitionRevisionId != question.ApprovedDefinitionRevisionId)
                throw new AbpDbConcurrencyException();
            question = tracked;
        }
        if (question.CurriculumSnapshotJson == null) return;
        if (question.TenantId != CurrentTenant.Id || !CurrentUser.Id.HasValue || question.PersonalOwnerUserId != CurrentUser.Id)
            throw new AbpAuthorizationException();
        var snapshot = JsonSerializer.Deserialize<EducationCurriculumSnapshot>(question.CurriculumSnapshotJson, JsonOptions)
            ?? throw new BusinessException("KnowPulse:Education:CurriculumConfirmationRequired");
        if (requireConfirmed)
        {
            if (!question.CurriculumConfirmed || question.CurriculumConfirmedByUserId != question.PersonalOwnerUserId ||
                !question.CurriculumConfirmedAtUtc.HasValue || !question.CurriculumScore.HasValue)
                throw new BusinessException("KnowPulse:Education:CurriculumConfirmationRequired");
            Question.ValidatePointWeights(snapshot,
                JsonSerializer.Deserialize<List<QuestionCurriculumPointWeight>>(question.CurriculumPointWeightsJson, JsonOptions) ?? [], question.CurriculumScore.Value);
            question.ValidateScoringDefinition();
        }
        var access = LazyServiceProvider.LazyGetRequiredService<EducationCurriculumAccessGuard>();
        if (forUpdate)
        {
            // Manual curriculum questions can have no knowledge citations, but still require a
            // current teaching scope protected until the new use commits.
            await access.ValidateForUseAsync(snapshot);
        }
        else await access.ValidateAsync(snapshot);
        foreach (var source in (JsonSerializer.Deserialize<List<EducationCurriculumSourceReference>>(question.CurriculumSourceReferencesJson, JsonOptions) ?? [])
            .OrderBy(x => x.KnowledgeVersionId).ThenBy(x => x.ChunkId))
            await LazyServiceProvider.LazyGetRequiredService<EducationMaterialScope>().ValidateSourceAsync(snapshot,
                source.KnowledgeItemId, source.KnowledgeVersionId, source.ChunkId, source.CurriculumBindingIds,
                source.TextbookKnowledgePointMappingIds, source.SchoolKnowledgeShareId, forUpdate: forUpdate);
        if (forUpdate) await repository.UpdateManyAsync([question], autoSave: true);
    }
}
