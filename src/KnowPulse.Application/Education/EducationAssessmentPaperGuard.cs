using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.Education;

/// <summary>首次发布复核保存试卷的正式依据，不以历史阅读权授予新的教学用途。</summary>
[RemoteService(false)]
public class EducationAssessmentPaperGuard : KnowPulseAppService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid>
        => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private EducationCurriculumAccessGuard Curriculum => LazyServiceProvider.LazyGetRequiredService<EducationCurriculumAccessGuard>();

    private async Task<T?> ReferenceAsync<T>(Guid id) where T : class, IEntity<Guid>
    {
        using var tracking = Repo<T>().DisableTracking();
        return await Repo<T>().FindAsync(id);
    }

    public virtual async Task<ExamPaper> EnsureAsync(Guid paperId, EducationCurriculumSnapshot scope, bool forUpdate = false)
    {
        var paper = await ReferenceAsync<ExamPaper>(paperId);
        if (paper == null || paper.TenantId != CurrentTenant.Id || paper.PersonalOwnerUserId != CurrentUser.Id ||
            paper.SaveOperationRequestHash == null || paper.CurriculumSnapshotJson == null) throw new AbpAuthorizationException();
        var paperScope = Parse<EducationCurriculumSnapshot>(paper.CurriculumSnapshotJson);
        if (!SameScope(scope, paperScope)) throw Invalid();
        if (forUpdate) await Curriculum.ValidateForUseAsync(scope);
        else await Curriculum.ValidateAsync(scope);
        using var rowsTracking = Repo<ExamPaperQuestion>().DisableTracking();
        var rows = await Repo<ExamPaperQuestion>().GetListAsync(x => x.PaperId == paper.Id && x.TenantId == paper.TenantId);
        if (rows.Count == 0 || rows.Count != paper.QuestionCount || rows.Sum(x => x.Score) != paper.TotalScore ||
            !rows.Select(x => x.DisplayOrder).Order().SequenceEqual(Enumerable.Range(1, rows.Count))) throw Invalid();
        foreach (var row in rows.OrderBy(x => x.OriginalQuestionId))
        {
            if (!row.QuestionDefinitionRevisionId.HasValue) throw Invalid();
            var revision = await ReferenceAsync<QuestionDefinitionRevision>(row.QuestionDefinitionRevisionId.Value);
            if (revision == null || revision.QuestionId != row.OriginalQuestionId || revision.TenantId != paper.TenantId ||
                revision.PersonalOwnerUserId != paper.PersonalOwnerUserId ||
                revision.DefinitionHash != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(revision.DefinitionJson)))) throw Invalid();
            var definition = Parse<QuestionDefinitionSnapshotDto>(revision.DefinitionJson);
            EnsureFrozenDefinition(row, definition, scope);
            using var questionTracking = forUpdate ? Repo<Question>().EnableTracking() : null;
            var question = forUpdate ? await Repo<Question>().FindAsync(row.OriginalQuestionId) : await ReferenceAsync<Question>(row.OriginalQuestionId);
            EnsureCurrentQuestion(question, paper);
            // Editing creates a later draft/revision, but does not rewrite this approved snapshot.
            // Explicit disable/archive/delete revokes new publication; its root CAS protects this decision.
            if (forUpdate) await Repo<Question>().UpdateManyAsync([question!], autoSave: true);
            if (forUpdate) EnsureCurrentQuestion(await ReferenceAsync<Question>(row.OriginalQuestionId), paper);
            var sourceAccess = LazyServiceProvider.LazyGetRequiredService<KnowledgeSourceAccessGuard>();
            foreach (var source in definition.Sources.OrderBy(x => x.KnowledgeVersionId).ThenBy(x => x.ChunkId))
                await sourceAccess.EnsureSourceAsync(source.KnowledgeItemId, source.KnowledgeVersionId, source.ChunkId,
                    requireUsable: true, forUpdate: forUpdate, schoolKnowledgeShareId: source.SchoolKnowledgeShareId);
            foreach (var source in definition.CurriculumSources.OrderBy(x => x.KnowledgeVersionId).ThenBy(x => x.ChunkId))
                await LazyServiceProvider.LazyGetRequiredService<EducationMaterialScope>().ValidateSourceAsync(definition.Curriculum!,
                    source.KnowledgeItemId, source.KnowledgeVersionId, source.ChunkId, source.CurriculumBindingIds,
                    source.TextbookKnowledgePointMappingIds, source.SchoolKnowledgeShareId, forUpdate: forUpdate);
        }
        return paper;
    }

    private static void EnsureCurrentQuestion(Question? question, ExamPaper paper)
    {
        if (question == null || question.IsDeleted || question.TenantId != paper.TenantId ||
            question.PersonalOwnerUserId != paper.PersonalOwnerUserId || question.Status is QuestionStatus.Disabled or QuestionStatus.Archived)
            throw Invalid();
    }

    private static void EnsureFrozenDefinition(ExamPaperQuestion row, QuestionDefinitionSnapshotDto definition, EducationCurriculumSnapshot scope)
    {
        if (definition.SchemaVersion != 1 || definition.Curriculum == null || !definition.CurriculumConfirmed ||
            definition.CurriculumConfirmedByUserId != scope.TeacherUserId || !definition.CurriculumConfirmedAtUtc.HasValue ||
            !SameScope(scope, definition.Curriculum, allowSubset: true) || definition.CurriculumScore != row.Score ||
            row.CurriculumConfirmedByUserId != definition.CurriculumConfirmedByUserId ||
            row.CurriculumConfirmedAtUtc != definition.CurriculumConfirmedAtUtc || row.Type != definition.Type ||
            row.Difficulty != definition.Difficulty || row.OriginalQuestionCreationSource != definition.CreationSource ||
            row.Title != definition.Title || row.Stem != definition.Stem || row.Analysis != definition.Analysis ||
            !Equal(Parse<EducationCurriculumSnapshot>(row.CurriculumSnapshotJson), definition.Curriculum) ||
            !PointWeights(Parse<List<QuestionCurriculumPointWeight>>(row.CurriculumPointWeightsJson))
                .SequenceEqual(PointWeights(definition.CurriculumPointWeights)) ||
            !Equal(Sources(Parse<List<EducationCurriculumSourceReference>>(row.CurriculumSourceReferencesJson)), Sources(definition.CurriculumSources)) ||
            (row.ScoringDefinitionJson == null ? null : QuestionScoringDefinition.Parse(row.ScoringDefinitionJson).ToCanonicalJson()) != definition.ScoringDefinition?.ToCanonicalJson()) throw Invalid();
        var options = Parse<List<ExamPaperQuestionOptionDto>>(row.OptionsJson).OrderBy(x => x.DisplayOrder).ThenBy(x => x.OptionKey, StringComparer.Ordinal)
            .Select(x => (x.OptionKey, x.Content, x.DisplayOrder));
        if (!options.SequenceEqual(definition.Options.OrderBy(x => x.DisplayOrder).ThenBy(x => x.OptionKey, StringComparer.Ordinal)
                .Select(x => (x.OptionKey, x.Content, x.DisplayOrder))) ||
            !Parse<List<string>>(row.CorrectAnswersJson).Order(StringComparer.Ordinal)
                .SequenceEqual(definition.Answers.Select(x => x.Value).Order(StringComparer.Ordinal))) throw Invalid();
        Question.ValidatePointWeights(definition.Curriculum, definition.CurriculumPointWeights, row.Score);
        if (definition.ScoringDefinition == null && row.Type is QuestionType.FillBlank or QuestionType.MultipleChoice or QuestionType.Subjective) throw Invalid();
        definition.ScoringDefinition?.ValidateForConfirmation(row.Type, row.Stem, row.Score,
            definition.Curriculum.Mappings.Select(x => x.MappingId).ToList(), definition.CurriculumPointWeights);
        if (row.Type == QuestionType.MultipleChoice && row.MultipleChoiceScoringMode != definition.ScoringDefinition!.MultipleChoiceScoringMode) throw Invalid();
    }

    private static bool SameScope(EducationCurriculumSnapshot left, EducationCurriculumSnapshot right, bool allowSubset = false)
        => left.TenantId == right.TenantId && left.TeacherUserId == right.TeacherUserId && left.SchoolId == right.SchoolId &&
           left.TeachingAssignmentId == right.TeachingAssignmentId && left.TeacherTextbookSelectionId == right.TeacherTextbookSelectionId &&
           left.TextbookVersionId == right.TextbookVersionId && left.EducationSubjectId == right.EducationSubjectId && left.Grade == right.Grade &&
           left.CatalogRevision == right.CatalogRevision && right.Mappings.Count > 0 &&
           right.Mappings.All(x => left.Mappings.Any(y => x.MappingId == y.MappingId && x.ChapterId == y.ChapterId &&
               x.KnowledgePointId == y.KnowledgePointId && x.KnowledgePointRevisionId == y.KnowledgePointRevisionId)) &&
           (allowSubset || left.Mappings.Count == right.Mappings.Count);

    private static object Sources(IEnumerable<EducationCurriculumSourceReference> rows) => rows.OrderBy(x => x.KnowledgeItemId)
        .ThenBy(x => x.KnowledgeVersionId).ThenBy(x => x.ChunkId).ThenBy(x => x.SchoolKnowledgeShareId).Select(x => new
        {
            x.KnowledgeItemId, x.KnowledgeVersionId, x.ChunkId, x.SchoolKnowledgeShareId,
            Bindings = x.CurriculumBindingIds.OrderBy(id => id).ToArray(), Mappings = x.TextbookKnowledgePointMappingIds.OrderBy(id => id).ToArray()
        }).ToArray();
    private static IEnumerable<(Guid MappingId, decimal Weight, decimal Score)> PointWeights(IEnumerable<QuestionCurriculumPointWeight> rows)
        => rows.OrderBy(x => x.TextbookKnowledgePointMappingId).Select(x => (x.TextbookKnowledgePointMappingId, x.Weight, x.Score));
    private static bool Equal<T>(T left, T right) => JsonSerializer.Serialize(left, Json) == JsonSerializer.Serialize(right, Json);
    private static T Parse<T>(string? json)
    {
        try { return json == null ? throw Invalid() : JsonSerializer.Deserialize<T>(json, Json) ?? throw Invalid(); }
        catch (JsonException) { throw Invalid(); }
    }
    private static BusinessException Invalid() => new("KnowPulse:Education:FormalDefinitionRequired");
}
