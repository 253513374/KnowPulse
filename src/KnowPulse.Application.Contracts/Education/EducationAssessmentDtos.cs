using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Education;

public class EducationAssessmentDraftContentDto
{
    [Required, StringLength(256)] public string Title { get; set; } = string.Empty;
    [Required, StringLength(8192)] public string Instructions { get; set; } = string.Empty;
    [Required] public EducationCurriculumScopeDto Scope { get; set; } = new();
    public Guid ExamPaperId { get; set; }
    public EducationAssessmentRecipientScope RecipientScope { get; set; } = EducationAssessmentRecipientScope.FullClass;
    public List<Guid> StudentUserIds { get; set; } = [];
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    [Range(1, 1440)] public int DurationMinutes { get; set; } = 60;
    [Range(1, 20)] public int MaxAttempts { get; set; } = 1;
    public ExamResultVisibility ResultVisibility { get; set; } = ExamResultVisibility.Hidden;
    public bool AllowWrongQuestionCollection { get; set; }
}

public class CreateEducationAssessmentDraftDto : EducationAssessmentDraftContentDto
{
    public Guid OperationId { get; set; }
}

public class SaveEducationAssessmentDraftDto : EducationAssessmentDraftContentDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = string.Empty;
    public Guid ExpectedDraftRevisionId { get; set; }
}

public class PublishEducationAssessmentDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = string.Empty;
    public Guid ExpectedDraftRevisionId { get; set; }
    [Required, StringLength(64, MinimumLength = 64)] public string RosterFingerprint { get; set; } = string.Empty;
}

public class StartEducationAssessmentDto
{
    public Guid OperationId { get; set; }
}

public class GetEducationAssessmentTaskListInput : PagedResultRequestDto
{
    [StringLength(128)] public string? Keyword { get; set; }
    public Guid? SchoolId { get; set; }
    public Guid? SchoolClassId { get; set; }
    public bool? IsPublished { get; set; }
}

public class GetEducationStudentAssessmentListInput : PagedResultRequestDto
{
    public Guid? SchoolId { get; set; }
}

/// <summary>当前工作区；写入重放返回最新修订，精确操作回执通过 GetOperation 查询。</summary>
public class EducationAssessmentTaskDto : EntityDto<Guid>
{
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public Guid? PublishedRevisionId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public EducationAssessmentTaskRevisionDto CurrentRevision { get; set; } = new();
}

/// <summary>教师自己的不可变保存/发布回执；不能嵌入学生响应。</summary>
public class EducationAssessmentTaskRevisionDto : EntityDto<Guid>
{
    public Guid TaskId { get; set; }
    public int RevisionNumber { get; set; }
    public Guid? BasedOnRevisionId { get; set; }
    public string RequestFingerprint { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public Guid SchoolId { get; set; }
    public Guid AcademicTermId { get; set; }
    public Guid SchoolClassId { get; set; }
    public string SchoolClassName { get; set; } = string.Empty;
    public Guid TeachingAssignmentId { get; set; }
    public Guid ExamPaperId { get; set; }
    public EducationCurriculumSnapshot Curriculum { get; set; } = new();
    public EducationAssessmentRecipientScope RecipientScope { get; set; }
    public List<Guid> StudentUserIds { get; set; } = [];
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int DurationMinutes { get; set; }
    public int MaxAttempts { get; set; }
    /// <summary>教师确认的后续开放规则，不表示本切片已经开放成绩或讲评。</summary>
    public ExamResultVisibility ResultVisibility { get; set; }
    public bool AllowWrongQuestionCollection { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public string? RosterFingerprint { get; set; }
    public List<EducationAssessmentRecipientDto> Recipients { get; set; } = [];
}

public class EducationAssessmentRosterPreviewDto
{
    public Guid TaskId { get; set; }
    public Guid DraftRevisionId { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public Guid SchoolClassId { get; set; }
    public string SchoolClassName { get; set; } = string.Empty;
    public string RosterFingerprint { get; set; } = string.Empty;
    public List<EducationAssessmentRosterStudentDto> Students { get; set; } = [];
}

public class EducationAssessmentRosterStudentDto
{
    public Guid StudentUserId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
}

public class EducationAssessmentRecipientDto : EntityDto<Guid>
{
    public Guid TaskRevisionId { get; set; }
    public Guid StudentUserId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public int StartedAttemptCount { get; set; }
    public Guid? CurrentAttemptId { get; set; }
}

/// <summary>本人分配详情只包含开始前需要的信息；读取不创建作答。</summary>
public class EducationStudentAssessmentTaskDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public Guid TaskId { get; set; }
    public Guid TaskRevisionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string SchoolClassName { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public string TextbookTitle { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int DurationMinutes { get; set; }
    public int MaxAttempts { get; set; }
    public ExamResultVisibility ConfiguredResultVisibility { get; set; }
    /// <summary>由教师最新正式发布及当前开放时间计算；配置本身不开放结果。</summary>
    public ExamResultVisibility CurrentResultVisibility { get; set; } = ExamResultVisibility.Hidden;
    public int QuestionCount { get; set; }
    public bool CanStart { get; set; }
    public Guid? CurrentAttemptId { get; set; }
    public DateTime ServerNowUtc { get; set; }
}

public class EducationStudentAssessmentAttemptDto : EntityDto<Guid>
{
    public Guid RecipientId { get; set; }
    public Guid TaskRevisionId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime ServerNowUtc { get; set; }
    public EducationAssessmentAttemptStatus Status { get; set; }
    public int PaperSnapshotVersion { get; set; }
    public int ResponseSetVersion { get; set; }
    public DateTime? LastSavedAtUtc { get; set; }
    public Guid? SubmissionId { get; set; }
    public List<EducationAssessmentResponseRevisionDto> Responses { get; set; } = [];
    public EducationStudentAssessmentPaperDto Paper { get; set; } = new();
}

public class EducationStudentAssessmentPaperDto
{
    public string Title { get; set; } = string.Empty;
    public int QuestionCount { get; set; }
    public decimal TotalScore { get; set; }
    public List<EducationStudentAssessmentQuestionDto> Questions { get; set; } = [];
}

public class EducationStudentAssessmentQuestionDto : EntityDto<Guid>
{
    public QuestionType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Stem { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public decimal Score { get; set; }
    public List<ExamPaperQuestionOptionDto> Options { get; set; } = [];
    public List<ExamResponseFieldDto> ResponseFields { get; set; } = [];
}
