using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationLearningWorkloadAppService : IApplicationService
{
    [Authorize(EducationPermissions.LearningParticipate)]
    Task<EducationLearningWorkloadDto> GetMyLearningWorkloadAsync(GetEducationActivityWorkloadDto input, Guid? schoolId = null);
}
public class EducationLearningWorkloadItemDto
{
    public EducationLearningWorkloadKind Kind { get; set; }
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Guid PublicationId { get; set; }
    public string SubjectName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public DateTime StartsAtUtc { get; set; }
    public DateTime DueAtUtc { get; set; }
    public int EstimatedMinutes { get; set; }
    public EducationLearningWorkloadState State { get; set; }
}
public sealed record EducationLearningWorkloadDayDto(DateTime DueDateUtc, string SubjectName, int PendingCount, int KnownMinutes, int BlockedCount, int OverdueCount);
public class EducationLearningWorkloadDto
{
    public Guid? SchoolId { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public DateTime CalculatedAtUtc { get; set; }
    public int UnestimatedPlanCount { get; set; }
    public List<EducationLearningWorkloadKind> OmittedKinds { get; set; } = [];
    public List<EducationLearningWorkloadDayDto> Days { get; set; } = [];
    public List<EducationLearningWorkloadItemDto> Items { get; set; } = [];
}
