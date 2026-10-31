using System;
using KnowPulse.QuestionBank;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Exam;

public class ExamPaperQuestion : FullAuditedEntity<Guid>, IMultiTenant
{
    public const int MaxTitleLength = 256;
    public const int MaxStemLength = 4000;
    public const int MaxJsonLength = 8000;
    public const int MaxAnalysisLength = 2048;
    public const int MaxKnowledgePointsLength = 1024;
    public const int MaxSourceSnapshotLength = 1024;

    public Guid? TenantId { get; protected set; }

    public Guid PaperId { get; private set; }

    public Guid OriginalQuestionId { get; private set; }

    public QuestionType Type { get; private set; }

    public QuestionDifficulty Difficulty { get; private set; }

    public QuestionCreationSource OriginalQuestionCreationSource { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Stem { get; private set; } = string.Empty;

    public string OptionsJson { get; private set; } = "[]";

    public string CorrectAnswersJson { get; private set; } = "[]";

    public string? Analysis { get; private set; }

    public string? KnowledgePointsSnapshot { get; private set; }

    public string? SourceSnapshot { get; private set; }

    public string? ScoringDefinitionJson { get; private set; }
    public Guid? QuestionDefinitionRevisionId { get; private set; }

    public void FreezeDefinition(Question question)
    {
        if (!question.IsUsableForExam || question.Id != OriginalQuestionId || ScoringDefinitionJson != null || QuestionDefinitionRevisionId.HasValue)
            throw new BusinessException(KnowPulseDomainErrorCodes.QuestionReviewRequiresPendingStatus);
        if (question.CurriculumSnapshotJson != null && !question.ApprovedDefinitionRevisionId.HasValue)
            throw new BusinessException("KnowPulse:Education:QuestionDefinitionRevisionRequired");
        question.ValidateScoringDefinition(Score);
        ScoringDefinitionJson = question.ScoringDefinitionJson;
        QuestionDefinitionRevisionId = question.ApprovedDefinitionRevisionId;
        if (ScoringDefinitionJson != null && Type == QuestionType.MultipleChoice)
            MultipleChoiceScoringMode = QuestionScoringDefinition.Parse(ScoringDefinitionJson).MultipleChoiceScoringMode!.Value;
    }

    public string? CurriculumSnapshotJson { get; private set; }
    public string? CurriculumPointWeightsJson { get; private set; }
    public string? CurriculumSourceReferencesJson { get; private set; }
    public Guid? CurriculumConfirmedByUserId { get; private set; }
    public DateTime? CurriculumConfirmedAtUtc { get; private set; }

    public void FreezeCurriculum(Question question)
    {
        if (CurriculumSnapshotJson != null || question.CurriculumSnapshotJson == null || !question.CurriculumConfirmed || question.CurriculumScore != Score)
            throw new BusinessException("KnowPulse:Education:CurriculumConfirmationRequired");
        CurriculumSnapshotJson = question.CurriculumSnapshotJson;
        CurriculumPointWeightsJson = question.CurriculumPointWeightsJson;
        CurriculumSourceReferencesJson = question.CurriculumSourceReferencesJson;
        CurriculumConfirmedByUserId = question.CurriculumConfirmedByUserId;
        CurriculumConfirmedAtUtc = question.CurriculumConfirmedAtUtc;
    }

    public decimal Score { get; private set; }

    public MultipleChoiceScoringMode MultipleChoiceScoringMode { get; private set; }

    public int DisplayOrder { get; private set; }

    protected ExamPaperQuestion()
    {
    }

    public ExamPaperQuestion(
        Guid id,
        Guid? tenantId,
        Guid paperId,
        Guid originalQuestionId,
        QuestionType type,
        QuestionDifficulty difficulty,
        QuestionCreationSource originalQuestionCreationSource,
        string title,
        string stem,
        string optionsJson,
        string correctAnswersJson,
        string? analysis,
        string? knowledgePointsSnapshot,
        string? sourceSnapshot,
        decimal score,
        MultipleChoiceScoringMode multipleChoiceScoringMode,
        int displayOrder)
        : base(id)
    {
        TenantId = tenantId;
        PaperId = paperId;
        OriginalQuestionId = originalQuestionId;
        Type = type;
        Difficulty = difficulty;
        OriginalQuestionCreationSource = originalQuestionCreationSource;
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), MaxTitleLength);
        Stem = Check.NotNullOrWhiteSpace(stem, nameof(stem), MaxStemLength);
        OptionsJson = Check.NotNull(optionsJson, nameof(optionsJson)).Length <= MaxJsonLength
            ? optionsJson
            : throw new BusinessException(KnowPulseDomainErrorCodes.ExamPaperSnapshotTooLarge);
        CorrectAnswersJson = Check.NotNull(correctAnswersJson, nameof(correctAnswersJson)).Length <= MaxJsonLength
            ? correctAnswersJson
            : throw new BusinessException(KnowPulseDomainErrorCodes.ExamPaperSnapshotTooLarge);
        Analysis = Check.Length(analysis, nameof(analysis), MaxAnalysisLength);
        KnowledgePointsSnapshot = Check.Length(knowledgePointsSnapshot, nameof(knowledgePointsSnapshot), MaxKnowledgePointsLength);
        SourceSnapshot = Check.Length(sourceSnapshot, nameof(sourceSnapshot), MaxSourceSnapshotLength);
        Score = score > 0 ? score : throw new BusinessException(KnowPulseDomainErrorCodes.ExamQuestionScoreInvalid);
        MultipleChoiceScoringMode = multipleChoiceScoringMode;
        DisplayOrder = displayOrder;
    }
}
