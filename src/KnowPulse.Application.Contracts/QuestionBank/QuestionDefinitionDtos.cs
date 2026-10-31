using System;
using System.Collections.Generic;
using KnowPulse.Education;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.QuestionBank;

/// <summary>审核通过时的完整题目事实。没有当前状态、审计标记或后续审核列表。</summary>
public class QuestionDefinitionSnapshotDto
{
    public int SchemaVersion { get; set; } = 1;
    public QuestionType Type { get; set; }
    public QuestionDifficulty Difficulty { get; set; }
    public QuestionCreationSource CreationSource { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Stem { get; set; } = string.Empty;
    public Guid? ApplicablePositionId { get; set; }
    public int SecurityLevel { get; set; }
    public string? Tags { get; set; }
    public string? Analysis { get; set; }
    public List<CreateUpdateQuestionOptionDto> Options { get; set; } = [];
    public List<CreateUpdateQuestionAnswerDto> Answers { get; set; } = [];
    public List<string> KnowledgePointNames { get; set; } = [];
    public List<CreateUpdateQuestionSourceDto> Sources { get; set; } = [];
    public QuestionScoringDefinition? ScoringDefinition { get; set; }
    public EducationCurriculumSnapshot? Curriculum { get; set; }
    public List<Guid> CurriculumSuggestedMappingIds { get; set; } = [];
    public List<QuestionCurriculumPointWeight> CurriculumPointWeights { get; set; } = [];
    public List<EducationCurriculumSourceReference> CurriculumSources { get; set; } = [];
    public decimal? CurriculumScore { get; set; }
    public bool CurriculumConfirmed { get; set; }
    public Guid? CurriculumConfirmedByUserId { get; set; }
    public DateTime? CurriculumConfirmedAtUtc { get; set; }
}

public class QuestionDefinitionRevisionDto : CreationAuditedEntityDto<Guid>
{
    public Guid QuestionId { get; set; }
    public int RevisionNumber { get; set; }
    public Guid ReviewOperationId { get; set; }
    public Guid ReviewedByUserId { get; set; }
    public DateTime ReviewedAtUtc { get; set; }
    public string DefinitionHash { get; set; } = string.Empty;
    public QuestionDefinitionSnapshotDto Definition { get; set; } = new();
}

/// <summary>一次已提交审核决策的冻结回执，不表示题目现在仍是该状态。</summary>
public class QuestionReviewOperationDto
{
    public Guid Id { get; set; }
    public Guid QuestionId { get; set; }
    public QuestionReviewAction Action { get; set; }
    public QuestionStatus FromStatus { get; set; }
    public QuestionStatus ToStatus { get; set; }
    public Guid? ReviewerUserId { get; set; }
    public DateTime ReviewTime { get; set; }
    public string? Comment { get; set; }
    public string? ExpectedConcurrencyStamp { get; set; }
    public string? RequestHash { get; set; }
    public Guid? DefinitionRevisionId { get; set; }
}
