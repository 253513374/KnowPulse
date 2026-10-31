using System;
using System.Collections.Generic;
using KnowPulse.Education;
using KnowPulse.QuestionBank;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Exam;

public class ExamPlanDto : FullAuditedEntityDto<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid PaperId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int DurationMinutes { get; set; }
    public decimal PassingScore { get; set; }
    public int MaxAttempts { get; set; }
    public ExamResultVisibility ResultVisibility { get; set; }
    public ExamPlanStatus Status { get; set; }
    public ExamPaperDto? Paper { get; set; }
    public List<ExamAssignmentDto> Assignments { get; set; } = new();
}

public class ExamPaperDto : FullAuditedEntityDto<Guid>
{
    public Guid? OperationId { get; set; }
    public EducationCurriculumSnapshot? Curriculum { get; set; }
    public string Title { get; set; } = string.Empty;
    public ExamPaperBuildMode BuildMode { get; set; }
    public Guid? ApplicablePositionId { get; set; }
    public int QuestionCount { get; set; }
    public decimal TotalScore { get; set; }
    public int SnapshotVersion { get; set; }
    public List<ExamPaperQuestionDto> Questions { get; set; } = new();
}

public class ExamPaperQuestionDto : EntityDto<Guid>
{
    /// <summary>仅教师管理视角返回；学生控件信息见 ResponseFields。</summary>
    public QuestionScoringDefinition? ScoringDefinition { get; set; }
    public Guid? QuestionDefinitionRevisionId { get; set; }
    public List<ExamResponseFieldDto> ResponseFields { get; set; } = [];
    public EducationCurriculumSnapshot? Curriculum { get; set; }
    public List<QuestionCurriculumPointWeight> CurriculumPointWeights { get; set; } = [];
    public List<EducationCurriculumScoringPointDto> CurriculumScoringPoints { get; set; } = [];
    public List<EducationCurriculumSourceReference> CurriculumSources { get; set; } = [];
    public Guid? CurriculumConfirmedByUserId { get; set; }
    public DateTime? CurriculumConfirmedAtUtc { get; set; }
    public Guid PaperId { get; set; }
    public Guid OriginalQuestionId { get; set; }
    public QuestionType Type { get; set; }
    public QuestionDifficulty Difficulty { get; set; }
    public QuestionCreationSource OriginalQuestionCreationSource { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Stem { get; set; } = string.Empty;
    public List<ExamPaperQuestionOptionDto> Options { get; set; } = new();
    public List<string> CorrectAnswers { get; set; } = new();
    public string? Analysis { get; set; }
    public string? KnowledgePointsSnapshot { get; set; }
    public string? SourceSnapshot { get; set; }
    public decimal Score { get; set; }
    public MultipleChoiceScoringMode MultipleChoiceScoringMode { get; set; }
    public int DisplayOrder { get; set; }
}

public class ExamResponseFieldDto
{
    public Guid Id { get; set; }
    public int DisplayOrder { get; set; }
    public string StemMarker { get; set; } = string.Empty;
}

public class ExamPaperQuestionOptionDto
{
    public string OptionKey { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

public class ExamAssignmentDto : EntityDto<Guid>
{
    public Guid PlanId { get; set; }
    public ExamAssignmentScopeType ScopeType { get; set; }
    public Guid? TargetId { get; set; }
    public bool IsActive { get; set; }
}

public class ExamAttemptDto : FullAuditedEntityDto<Guid>
{
    public Guid PlanId { get; set; }
    public Guid PaperId { get; set; }
    public Guid UserId { get; set; }
    public int AttemptNumber { get; set; }
    public ExamAttemptStatus Status { get; set; }
    public DateTime StartedTime { get; set; }
    public DateTime ExpiresTime { get; set; }
    public DateTime? LastSavedTime { get; set; }
    public DateTime? SubmittedTime { get; set; }
    public DateTime? ScoredTime { get; set; }
    public ExamPlanDto? Plan { get; set; }
    public ExamPaperDto? Paper { get; set; }
    public List<ExamAnswerDto> Answers { get; set; } = new();
    public ExamScoreDto? Score { get; set; }
}

public class ExamAnswerDto : EntityDto<Guid>
{
    public Guid AttemptId { get; set; }
    public Guid PaperQuestionId { get; set; }
    public List<string> Values { get; set; } = new();
    public int SaveVersion { get; set; }
    public DateTime SavedTime { get; set; }
}

public class ExamScoreDto : FullAuditedEntityDto<Guid>
{
    public Guid PlanId { get; set; }
    public Guid PaperId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid UserId { get; set; }
    public decimal TotalScore { get; set; }
    public decimal ObtainedScore { get; set; }
    public decimal PassingScore { get; set; }
    public bool IsPassed { get; set; }
    public DateTime ScoredTime { get; set; }
    public string ScoringVersion { get; set; } = string.Empty;
    public List<WrongQuestionRecordDto> WrongQuestions { get; set; } = new();
}

public class WrongQuestionRecordDto : EntityDto<Guid>
{
    public Guid ScoreId { get; set; }
    public Guid PlanId { get; set; }
    public Guid PaperId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid PaperQuestionId { get; set; }
    public Guid OriginalQuestionId { get; set; }
    public Guid UserId { get; set; }
    public string QuestionSnapshot { get; set; } = string.Empty;
    public List<string> UserAnswers { get; set; } = new();
    public List<string> CorrectAnswers { get; set; } = new();
    public string? AnalysisSnapshot { get; set; }
    public decimal MaxScore { get; set; }
    public decimal ObtainedScore { get; set; }
}
