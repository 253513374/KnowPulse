using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Exam;

namespace KnowPulse.Education;

public partial class EducationLearningReportSourceReader
{
    internal async Task<List<EducationLearningTrendBasis>> TrendBasesAsync(EducationLearningReportSource source)
    {
        if (source.TenantId != CurrentTenant.Id || source.OwnerUserId != CurrentUser.Id || source.Assessments.Count > MaxTasks) throw Invalid();
        var ids = source.Assessments.Select(x => x.ExamPaperId).Distinct().ToList();
        var papers = await BoundedAsync((await QueryAsync<ExamPaper>()).Where(x => ids.Contains(x.Id) && x.TenantId == source.TenantId).Select(x => new { x.Id, x.PersonalOwnerUserId, x.SnapshotVersion, x.QuestionCount, x.TotalScore }), MaxTasks, default);
        // Compare only immutable paper snapshots; no current question body or mutable question-bank definition is read.
        var questions = await BoundedAsync((await QueryAsync<ExamPaperQuestion>()).Where(x => ids.Contains(x.PaperId) && x.TenantId == source.TenantId).Select(x => new { x.Id, x.PaperId, x.DisplayOrder, x.Type, x.Difficulty, x.Score, x.MultipleChoiceScoringMode, x.QuestionDefinitionRevisionId, x.CurriculumSnapshotJson }), MaxRows, default);
        var result = new List<EducationLearningTrendBasis>();
        foreach (var a in source.Assessments)
        {
            var paper = papers.SingleOrDefault(x => x.Id == a.ExamPaperId) ?? throw Changed(); var rows = questions.Where(x => x.PaperId == paper.Id).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToList();
            if (paper.PersonalOwnerUserId != source.OwnerUserId || paper.SnapshotVersion != a.PaperSnapshotVersion || rows.Count != paper.QuestionCount || paper.QuestionCount == 0 || rows.Sum(x => x.Score) != paper.TotalScore) throw Changed();
            var scopes = rows.Where(x => x.CurriculumSnapshotJson != null).Select(x => Parse<EducationCurriculumSnapshot>(x.CurriculumSnapshotJson!)).ToList();
            if (scopes.Any(x => x.TenantId != source.TenantId || x.SchoolId != source.Curriculum.SchoolId || x.EducationSubjectId != source.Curriculum.EducationSubjectId || x.TextbookVersionId != source.Curriculum.TextbookVersionId)) throw Invalid();
            var coverage = scopes.Count != rows.Count || scopes.Any(x => x.Mappings == null || x.Mappings.Count == 0) ? null : JsonSerializer.Serialize(scopes.SelectMany(x => x.Mappings).Select(x => x.KnowledgePointRevisionId).Distinct().Order().ToArray());
            var difficulty = JsonSerializer.Serialize(rows.GroupBy(x => x.Difficulty).OrderBy(x => x.Key).Select(x => new { Difficulty = x.Key, Count = x.Count() }).ToArray());
            var scoring = rows.Any(x => !x.QuestionDefinitionRevisionId.HasValue) ? null : JsonSerializer.Serialize(rows.Select(x => new { x.QuestionDefinitionRevisionId, x.Type, x.Score, x.MultipleChoiceScoringMode }).ToArray());
            result.Add(new(a.TaskId, paper.QuestionCount, paper.TotalScore, coverage, difficulty, scoring));
        }
        return result;
    }
}
