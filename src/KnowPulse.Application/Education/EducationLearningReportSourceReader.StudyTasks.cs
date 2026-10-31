using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KnowPulse.Education;

public partial class EducationLearningReportSourceReader
{
    private async Task<(List<EducationLearningQuestionExposure> Values, List<ExposureVersion> Versions)> StudyExposuresAsync(
        EducationCurriculumSnapshot scope, List<Guid> students, List<Guid> originals, List<FrozenQuestion>? questions, DateTime end, CancellationToken ct)
    {
        var reader = LazyServiceProvider.LazyGetRequiredService<EducationStudyHintContextReader>();
        var values = new List<EducationLearningQuestionExposure>(); var versions = new List<ExposureVersion>();
        if (questions != null) {
            var contexts = await reader.ExactAsync(scope, students, questions.Select(x => new EducationStudyHintQuestion(x.Id, x.OriginalId, x.CurriculumJson, x.WeightsJson)).ToList(), end, ct);
            foreach (var context in contexts) {
                values.Add(new(context.Id, context.StudentId, context.OriginalId, context.ExposedAtUtc, context.Use, context.DecisionId, context.PaperQuestionId));
                versions.Add(new(context.Id, context.StudentId, context.OriginalId, context.ExposedAtUtc, context.DecisionId));
            }
        } else {
            // Internal callers without exact frozen questions cannot infer a no-hints decision.
            foreach (var contact in await reader.ContactsAsync(scope, students, end, ct)) {
                var assigned = Parse<EducationCurriculumSnapshot>(contact.ScopeJson);
                if (!assigned.Mappings.Any(x => scope.Mappings.Any(y => y.KnowledgePointId == x.KnowledgePointId))) continue;
                foreach (var original in originals) {
                    var id = EducationStudentGrowthSnapshot.StableId($"education-study-context-v1|{contact.RecipientId:D}|{original:D}");
                    values.Add(new(id, contact.StudentId, original, contact.ProvidedAtUtc, EducationLearningExposureUse.Unverified));
                    versions.Add(new(id, contact.StudentId, original, contact.ProvidedAtUtc, null)); Capacity(values.Count, MaxHistory);
                }
            }
        }
        foreach (var known in await reader.ConfirmedAsync(scope, students, originals, end, ct)) {
            values.Add(new(known.Id, known.StudentId, known.OriginalId, known.ExposedAtUtc, EducationLearningExposureUse.Confirmed, known.DecisionId));
            versions.Add(new(known.Id, known.StudentId, known.OriginalId, known.ExposedAtUtc, known.DecisionId)); Capacity(values.Count, MaxHistory);
        }
        return (values, versions);
    }
}
