using System.Linq;
using System.Collections.Generic;

namespace KnowPulse.Education;

public static class EducationStudyHintScope
{
    public static bool Overlaps(EducationCurriculumSnapshot assigned, string? questionScopeJson, string? weightsJson)
    {
        if (questionScopeJson == null || weightsJson == null) return false;
        var question = EducationLearningReportJson.Read<EducationCurriculumSnapshot>(questionScopeJson, EducationLearningReportJson.MaxSourceBytes);
        var weights = EducationLearningReportJson.Read<List<QuestionCurriculumPointWeight>>(weightsJson, EducationLearningReportJson.MaxSourceBytes);
        return assigned.TenantId == question.TenantId && assigned.SchoolId == question.SchoolId && assigned.EducationSubjectId == question.EducationSubjectId &&
            question.Mappings.Any(m => assigned.Mappings.Any(a => a.KnowledgePointId == m.KnowledgePointId) &&
                weights.Any(w => w.TextbookKnowledgePointMappingId == m.MappingId && w.Score > 0 && w.Weight > 0));
    }
}
