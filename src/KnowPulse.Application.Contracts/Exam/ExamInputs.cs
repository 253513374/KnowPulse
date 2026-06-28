using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using KnowPulse.QuestionBank;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Exam;

public class GetExamPlanListInput : PagedAndSortedResultRequestDto
{
    public string? Keyword { get; set; }
    public ExamPlanStatus? Status { get; set; }
}

public class GetExamScoreListInput : PagedAndSortedResultRequestDto
{
    public Guid? PlanId { get; set; }
    public Guid? UserId { get; set; }
    public bool? IsPassed { get; set; }
}

public class CreateExamPlanDto
{
    [Required]
    [StringLength(256)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1024)]
    public string? Description { get; set; }

    [Required]
    public DateTime StartTime { get; set; }

    [Required]
    public DateTime EndTime { get; set; }

    [Range(1, 1440)]
    public int DurationMinutes { get; set; } = 60;

    [Range(0, 100000)]
    public decimal PassingScore { get; set; } = 60;

    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 1;

    public ExamResultVisibility ResultVisibility { get; set; } = ExamResultVisibility.ScoreOnly;

    [Required]
    public GenerateExamPaperDto Paper { get; set; } = new();

    public List<ExamAssignmentInputDto> Assignments { get; set; } = new();
}

public class GenerateExamPaperDto
{
    [Required]
    [StringLength(256)]
    public string Title { get; set; } = string.Empty;

    public ExamPaperBuildMode BuildMode { get; set; } = ExamPaperBuildMode.Random;

    public Guid? ApplicablePositionId { get; set; }

    public bool Randomize { get; set; }

    public List<Guid> FixedQuestionIds { get; set; } = new();

    public List<ExamPaperRuleDto> Rules { get; set; } = new();
}

public class ExamPaperRuleDto
{
    public QuestionType? Type { get; set; }
    public QuestionDifficulty? Difficulty { get; set; }

    [StringLength(128)]
    public string? KnowledgePoint { get; set; }

    [Range(1, 200)]
    public int Count { get; set; } = 1;

    [Range(0.01, 1000)]
    public decimal ScorePerQuestion { get; set; } = 5;

    public MultipleChoiceScoringMode MultipleChoiceScoringMode { get; set; } = MultipleChoiceScoringMode.AllOrNothing;
}

public class ExamAssignmentInputDto
{
    public ExamAssignmentScopeType ScopeType { get; set; } = ExamAssignmentScopeType.All;
    public Guid? TargetId { get; set; }
}

public class SaveExamAnswersDto
{
    [StringLength(128)]
    public string? IdempotencyKey { get; set; }

    public List<SaveExamAnswerItemDto> Answers { get; set; } = new();
}

public class SaveExamAnswerItemDto
{
    public Guid PaperQuestionId { get; set; }
    public List<string> Values { get; set; } = new();
    public int? ClientVersion { get; set; }
}

public class SubmitExamAttemptDto
{
    [StringLength(128)]
    public string? IdempotencyKey { get; set; }
}
