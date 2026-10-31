using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationLearningReportAppService : IApplicationService
{
    [Authorize(EducationPermissions.AnalysisManage)] Task<EducationLearningReportJobDto> RequestAsync(RequestEducationLearningReportDto input);
    [Authorize(EducationPermissions.AnalysisManage)] Task<EducationLearningReportJobDto> RefreshAsync(Guid reportId, RefreshEducationLearningReportDto input);
    [Authorize(EducationPermissions.AnalysisManage)] Task<EducationLearningReportJobDto> RecoverAsync(Guid reportId, RecoverEducationLearningReportDto input);
    [Authorize(EducationPermissions.AnalysisManage)] Task<EducationLearningReportJobDto?> GetOperationAsync(Guid reportId, Guid operationId);
    [Authorize(EducationPermissions.AnalysisManage)] Task<PagedResultDto<EducationLearningReportListDto>> GetListAsync(Guid teachingAssignmentId, PagedResultRequestDto input, Guid? schoolId = null);
    [Authorize(EducationPermissions.AnalysisManage)] Task<EducationLearningReportDto> GetAsync(Guid id);
    [Authorize(EducationPermissions.AnalysisManage)] Task<EducationLearningReportDto> GetForSchoolAsync(Guid reportId, Guid schoolId, Guid teachingAssignmentId);
    [Authorize(EducationPermissions.AnalysisManage)] Task<PagedResultDto<EducationLearningReportStudentSummary>> GetStudentsAsync(Guid reportId, Guid snapshotId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.AnalysisManage)] Task<EducationLearningReportStudentResult> GetStudentAsync(Guid reportId, Guid snapshotId, Guid studentUserId);
    [Authorize(EducationPermissions.AnalysisManage)] Task<PagedResultDto<EducationLearningReportBasisDto>> GetPointEvidenceAsync(Guid reportId, Guid snapshotId, Guid studentUserId, Guid pointRevisionId, PagedResultRequestDto input, Guid? taskId = null);
    [Authorize(EducationPermissions.AnalysisManage)] Task<EducationLearningReportTrendDto> GetTrendAsync(Guid reportId, Guid snapshotId, Guid schoolId, Guid teachingAssignmentId, GetEducationLearningTrendInput input);
}
public class GetEducationLearningTrendInput : PagedResultRequestDto
{
    public Guid? StudentUserId { get; set; }
}
public class EducationLearningTrendStudentDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Account { get; set; } = "";
}
public class EducationLearningReportTrendDto : PagedResultDto<EducationLearningTrendRow>
{
    public Guid ReportId { get; set; }
    public Guid SnapshotId { get; set; }
    public Guid SchoolId { get; set; }
    public Guid TeachingAssignmentId { get; set; }
    public string ClassName { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string TextbookTitle { get; set; } = "";
    public DateTime WindowStartsAtUtc { get; set; }
    public DateTime WindowEndsAtUtc { get; set; }
    public DateTime CalculatedAtUtc { get; set; }
    public string ComparisonVersion { get; set; } = "";
    public EducationLearningTrendStudentDto? Student { get; set; }
}

public class RequestEducationLearningReportDto
{
    public Guid OperationId { get; set; }
    [Required] public EducationCurriculumScopeDto Scope { get; set; } = new();
    public DateTime WindowStartsAtUtc { get; set; }
    public DateTime WindowEndsAtUtc { get; set; }
    [MaxLength(500)] public List<Guid> TaskIds { get; set; } = [];
}
public class RefreshEducationLearningReportDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = string.Empty;
    public Guid ExpectedCurrentJobId { get; set; }
    [Required] public EducationCurriculumScopeDto Scope { get; set; } = new();
}
public class RecoverEducationLearningReportDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = string.Empty;
    public Guid ExpectedCurrentJobId { get; set; }
}
public class EducationLearningReportJobDto : EntityDto<Guid>
{
    public Guid ReportId { get; set; }
    public int Generation { get; set; }
    public Guid? RetryOfJobId { get; set; }
    public EducationLearningReportJobStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public int CompletedStudentCount { get; set; }
    public int StudentCount { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? HeartbeatAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public EducationLearningReportFailureStage FailureStage { get; set; }
    public bool CapacityExceeded { get; set; }
    public string SourceFingerprint { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
}
public class EducationLearningReportListDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public Guid TeachingAssignmentId { get; set; }
    public Guid TextbookVersionId { get; set; }
    public DateTime WindowStartsAtUtc { get; set; }
    public DateTime WindowEndsAtUtc { get; set; }
    public int Generation { get; set; }
}
public class EducationLearningReportDto : EducationLearningReportListDto
{
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public EducationLearningReportJobDto? CurrentJob { get; set; }
    public Guid? CurrentSnapshotId { get; set; }
    public int? SnapshotGeneration { get; set; }
    public bool SourceIsCurrent { get; set; }
    public bool ScopeNeedsUpdate { get; set; }
    public DateTime? CalculatedAtUtc { get; set; }
    public EducationCurriculumScopeDto Scope { get; set; } = new();
    public EducationMasteryCalculationRule? Rule { get; set; }
    public EducationLearningReportSummary? Summary { get; set; }
    public EducationLearningReportCoverageDto? Coverage { get; set; }
}

public class EducationLearningReportCoverageDto
{
    public int StudentCount { get; set; }
    public int ScoredStudentCount { get; set; }
    public int AssignedCount { get; set; }
    public int ScoredCount { get; set; }
    public int PendingGradingCount { get; set; }
    public int AbsentCount { get; set; }
    public int NotStartedCount { get; set; }
    public int InProgressCount { get; set; }
    public int AwaitingSealCount { get; set; }
    public int OutsideWindowCount { get; set; }
}
public class EducationLearningReportBasisDto
{
    public Guid QuestionGradeId { get; set; }
    public Guid GradeRevisionId { get; set; }
    public Guid TaskId { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid PaperQuestionId { get; set; }
    public Guid? QualificationRevisionId { get; set; }
    public DateTime EffectiveAtUtc { get; set; }
    public decimal MaxScore { get; set; }
    public decimal ObtainedScore { get; set; }
    public EducationLearningExclusionReason ExclusionReason { get; set; }
}
