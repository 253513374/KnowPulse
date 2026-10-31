using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationTeachingPlanActivityAppService : IApplicationService
{
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<PagedResultDto<EducationTeacherPlanCardDto>> GetTeacherPlansAsync(GetEducationTeacherPlansInput input);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationTeacherActivityProgressDto> GetActivityProgressForSchoolAsync(Guid planId, Guid? publishedRevisionId, Guid schoolId, Guid teachingAssignmentId);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationTeacherActivityEventDto> SaveActivityFollowUpForSchoolAsync(Guid activityId, Guid schoolId, Guid teachingAssignmentId, SaveEducationActivityFollowUpDto input);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationTeacherActivityEventDto?> GetActivityFollowUpOperationForSchoolAsync(Guid planId, Guid operationId, Guid schoolId, Guid teachingAssignmentId);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationTeacherActivityProgressDto> GetActivityProgressAsync(Guid planId, Guid? publishedRevisionId = null);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<PagedResultDto<EducationTeacherActivityEventDto>> GetActivityHistoryAsync(Guid activityId, Guid? expectedRevisionId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationTeacherActivityEventDto> SaveActivityFollowUpAsync(Guid activityId, SaveEducationActivityFollowUpDto input);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationTeacherActivityEventDto?> GetActivityFollowUpOperationAsync(Guid planId, Guid operationId);
    [Authorize(EducationPermissions.TeachingPlanView)] Task<EducationMyActivitiesDto> GetMyActivitiesAsync(Guid planId, Guid expectedVersionId);
    [Authorize(EducationPermissions.TeachingPlanView)] Task<EducationMyActivitiesDto> GetMyActivitiesForSchoolAsync(Guid planId, Guid expectedVersionId, Guid schoolId);
    [Authorize(EducationPermissions.TeachingPlanView), Authorize(EducationPermissions.LearningParticipate)] Task<EducationStudentActivityEventDto> SaveMyActivityAsync(Guid activityId, SaveEducationMyActivityDto input);
    [Authorize(EducationPermissions.TeachingPlanView)] Task<EducationStudentActivityEventDto?> GetMyActivityOperationAsync(Guid planId, Guid operationId);
    [Authorize(EducationPermissions.TeachingPlanView)] Task<PagedResultDto<EducationStudentActivityEventDto>> GetMyActivityHistoryAsync(Guid activityId, Guid? expectedStudentRevisionId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.TeachingPlanView)] Task<EducationActivityWorkloadDto> GetMyActivityWorkloadAsync(GetEducationActivityWorkloadDto input);
}
public class SaveEducationMyActivityDto
{
    public Guid OperationId { get; set; }
    public Guid? ExpectedStudentRevisionId { get; set; }
    public EducationTeachingActivityAction Action { get; set; }
    public EducationTeachingActivityDifficulty? Difficulty { get; set; }
    [StringLength(2000)] public string? SharedText { get; set; }
    [Range(1,1440)] public int? ReportedMinutes { get; set; }
    public bool ConfirmSharingWithTeacher { get; set; }
    public bool ConfirmActivityOnly { get; set; }
}
public class SaveEducationActivityFollowUpDto
{
    public Guid OperationId { get; set; }
    public Guid? ExpectedRevisionId { get; set; }
    public Guid ExpectedStudentRevisionId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
    [Required, StringLength(2000)] public string InternalTeacherNote { get; set; } = "";
    public bool ConfirmHandlingReceipt { get; set; }
}
public class EducationStudentActivityEventDto : EntityDto<Guid>
{
    public Guid ActivityId { get; set; }
    public int StudentRevisionNumber { get; set; }
    public EducationTeachingActivityAction Action { get; set; }
    public EducationTeachingActivityDifficulty? Difficulty { get; set; }
    public string? SharedText { get; set; }
    public int? ReportedMinutes { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}
public class EducationTeacherActivityEventDto : EntityDto<Guid>
{
    public Guid ActivityId { get; set; }
    public int RevisionNumber { get; set; }
    public Guid? BasedOnRevisionId { get; set; }
    public Guid? ReplyToStudentRevisionId { get; set; }
    public EducationTeachingActivityAction Action { get; set; }
    public EducationTeachingActivityDifficulty? Difficulty { get; set; }
    public string? SharedText { get; set; }
    public string? InternalTeacherNote { get; set; }
    public int? ReportedMinutes { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public string RequestFingerprint { get; set; } = "";
}
public class EducationStudentActivityDto : EntityDto<Guid>
{
    public bool CanStart { get; set; }
    public bool CanReadMaterials { get; set; }
    public bool MaterialsAvailable { get; set; }
    public Guid StepId { get; set; }
    public int StepOrder { get; set; }
    public Guid? CurrentStudentRevisionId { get; set; }
    public int StudentRevisionNumber { get; set; }
    public EducationTeachingActivityState State { get; set; }
    public DateTime? FirstProvidedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public EducationStudentActivityEventDto? CurrentStudent { get; set; }
    public DateTime? TeacherHandledAtUtc { get; set; }
    public EducationActivityFormalAssessmentDto? FormalAssessment { get; set; }
}
public class EducationTeacherActivityDto : EducationStudentActivityDto
{
    public Guid? CurrentRevisionId { get; set; }
    public int RevisionNumber { get; set; }
    public string ConcurrencyStamp { get; set; } = "";
    public DateTime AssignedAtUtc { get; set; }
    public EducationTeacherActivityEventDto? CurrentFollowUp { get; set; }
}
public class EducationActivityVersionDto : EntityDto<Guid>
{
    public int RevisionNumber { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}
public class EducationTeacherActivityProgressDto
{
    public Guid SchoolId { get; set; }
    public Guid TeachingAssignmentId { get; set; }
    public EducationPlanExecutionState ExecutionState { get; set; }
    public Guid PlanId { get; set; }
    public Guid PublishedRevisionId { get; set; }
    public bool IsCurrentPublication { get; set; }
    public string StudentName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public EducationStudentTeachingPlanContent Content { get; set; } = new();
    public List<EducationActivityVersionDto> Versions { get; set; } = [];
    public List<EducationTeacherActivityDto> Activities { get; set; } = [];
}
public class GetEducationTeacherPlansInput : PagedResultRequestDto
{
    public Guid SchoolId { get; set; }
    public Guid? TeachingAssignmentId { get; set; }
}
public class EducationTeacherPlanCardDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public Guid TeachingAssignmentId { get; set; }
    public Guid PublishedRevisionId { get; set; }
    public int PublishedRevisionNumber { get; set; }
    public bool IsCurrentPublication { get; set; }
    public EducationPlanExecutionState ExecutionState { get; set; }
    public string StudentName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string SubjectName { get; set; } = "";
}
public class EducationMyActivitiesDto
{
    public EducationStudentTeachingPlanDto Plan { get; set; } = new();
    public List<EducationStudentActivityDto> Activities { get; set; } = [];
}
public class GetEducationActivityWorkloadDto
{
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
}
public sealed record EducationActivityWorkloadDay(DateTime DueDateUtc, string SubjectName, int ActivityCount, int EstimatedMinutes);
public class EducationActivityWorkloadDto
{
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public DateTime CalculatedAtUtc { get; set; }
    public int UncountedPlanCount { get; set; }
    public List<EducationActivityWorkloadDay> Days { get; set; } = [];
}
