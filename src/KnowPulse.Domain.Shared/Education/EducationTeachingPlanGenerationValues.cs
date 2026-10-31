using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;

namespace KnowPulse.Education;

public enum EducationPlanGenerationStatus { Pending = 0, Processing = 1, Completed = 2, Failed = 3 }
public enum EducationPlanGenerationFailure { None = 0, Authorization = 1, Source = 2, Model = 3, Output = 4, Persistence = 5, LeaseExpired = 6 }
public static class EducationPlanGenerationErrors
{
    public const string Invalid = "KnowPulse:PlanGeneration:Invalid";
    public const string Busy = "KnowPulse:PlanGeneration:Busy";
    public const string Quota = "KnowPulse:PlanGeneration:Quota";
    public const string Changed = "KnowPulse:PlanGeneration:Changed";
    public const string Conflict = "KnowPulse:PlanGeneration:Conflict";
}
public sealed class EducationPlanGenerationRequest
{
    public Guid ReportId { get; set; }
    public Guid ReportSnapshotId { get; set; }
    public Guid StudentUserId { get; set; }
    public List<Guid> TargetPointRevisionIds { get; set; } = [];
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public DateTime ReassessmentAtUtc { get; set; }
    public int MaximumEstimatedMinutes { get; set; } = 60;
    public void Validate()
    {
        if (ReportId == Guid.Empty || ReportSnapshotId == Guid.Empty || StudentUserId == Guid.Empty ||
            TargetPointRevisionIds == null || TargetPointRevisionIds.Count is < 1 or > 20 || TargetPointRevisionIds.Contains(Guid.Empty) || TargetPointRevisionIds.Distinct().Count() != TargetPointRevisionIds.Count ||
            StartsAtUtc.Kind != DateTimeKind.Utc || EndsAtUtc.Kind != DateTimeKind.Utc || ReassessmentAtUtc.Kind != DateTimeKind.Utc || StartsAtUtc >= EndsAtUtc ||
            ReassessmentAtUtc < StartsAtUtc || ReassessmentAtUtc > EndsAtUtc || MaximumEstimatedMinutes is < 5 or > 10080)
            throw new BusinessException(EducationPlanGenerationErrors.Invalid);
    }
    public void Validate(EducationTeachingPlanBasis basis)
    {
        Validate();
        if (basis.ReportId != ReportId || basis.ReportSnapshotId != ReportSnapshotId || basis.Student.StudentUserId != StudentUserId ||
            TargetPointRevisionIds.Any(id => !basis.Points.Any(p => p.Point.KnowledgePointRevisionId == id))) throw new BusinessException(EducationPlanGenerationErrors.Invalid);
    }
}
