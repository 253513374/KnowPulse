using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using KnowPulse.Education;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.QuestionBank;

/// <summary>
/// 题库题目聚合根。题目必须经过审核通过后才可作为正式题进入后续组卷范围。
/// </summary>
public class Question : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxTitleLength = 256;
    public const int MaxStemLength = 4000;
    public const int MaxTagsLength = 512;

    public Guid? TenantId { get; protected set; }

    public Guid? PersonalOwnerUserId { get; private set; }

    public string? ScoringDefinitionJson { get; private set; }
    public Guid? ApprovedDefinitionRevisionId { get; private set; }

    public void RecordApprovedDefinitionRevision(Guid id)
    {
        if (Status != QuestionStatus.Approved || id == Guid.Empty)
            throw new BusinessException(KnowPulseDomainErrorCodes.QuestionReviewRequiresPendingStatus);
        ApprovedDefinitionRevisionId = id;
    }

    public void SetScoringDefinitionDraft(QuestionScoringDefinition? definition)
    {
        definition?.ValidateDraft();
        var value = definition?.ToCanonicalJson();
        if (ScoringDefinitionJson == value) return;
        ScoringDefinitionJson = value;
        InvalidateCurriculumConfirmation();
        if (Status is QuestionStatus.Approved or QuestionStatus.PendingReview)
        {
            Status = QuestionStatus.Draft;
            ReviewedByUserId = null;
            ReviewedTime = null;
        }
    }

    public void ValidateScoringDefinition(decimal? score = null, IReadOnlyCollection<QuestionCurriculumPointWeight>? pointWeights = null)
    {
        var definition = ScoringDefinitionJson == null ? null : QuestionScoringDefinition.Parse(ScoringDefinitionJson);
        if (definition == null)
        {
            if (Type == QuestionType.Subjective || CurriculumSnapshotJson != null && Type is QuestionType.FillBlank or QuestionType.MultipleChoice)
                throw new BusinessException("KnowPulse:Education:InvalidScoringDefinition");
            return;
        }
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshot = CurriculumSnapshotJson == null ? null : JsonSerializer.Deserialize<EducationCurriculumSnapshot>(CurriculumSnapshotJson, json)!;
        definition.ValidateForConfirmation(Type, Stem,
            score ?? CurriculumScore ?? (Type is QuestionType.SingleChoice or QuestionType.MultipleChoice or QuestionType.TrueFalse
                ? 1 : definition.Blanks.Sum(x => x.Score) + definition.ScoringPoints.Sum(x => x.Score)),
            snapshot?.Mappings.Select(x => x.MappingId).ToList(),
            pointWeights ?? (snapshot == null ? null : JsonSerializer.Deserialize<List<QuestionCurriculumPointWeight>>(CurriculumPointWeightsJson, json)));
    }

    public const int MaxCurriculumJsonLength = 200000;
    public string? CurriculumSnapshotJson { get; private set; }
    public string CurriculumSuggestedMappingIdsJson { get; private set; } = "[]";
    public string CurriculumSourceReferencesJson { get; private set; } = "[]";
    public string CurriculumPointWeightsJson { get; private set; } = "[]";
    public decimal? CurriculumScore { get; private set; }
    public bool CurriculumConfirmed { get; private set; }
    public Guid? CurriculumConfirmedByUserId { get; private set; }
    public DateTime? CurriculumConfirmedAtUtc { get; private set; }

    public void SetCurriculumDraft(string snapshotJson, string suggestedMappingIdsJson, string sourceReferencesJson, decimal score)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var references = JsonSerializer.Deserialize<List<EducationCurriculumSourceReference>>(CurriculumSourceReferencesJson, json) ?? [];
        foreach (var reference in JsonSerializer.Deserialize<List<EducationCurriculumSourceReference>>(sourceReferencesJson, json) ?? [])
        {
            if (!references.Any(old => JsonSerializer.Serialize(old, json) == JsonSerializer.Serialize(reference, json))) references.Add(reference);
        }
        CurriculumSnapshotJson = Check.NotNullOrWhiteSpace(snapshotJson, nameof(snapshotJson), MaxCurriculumJsonLength);
        CurriculumSuggestedMappingIdsJson = Check.NotNullOrWhiteSpace(suggestedMappingIdsJson, nameof(suggestedMappingIdsJson), MaxCurriculumJsonLength);
        CurriculumSourceReferencesJson = Check.NotNullOrWhiteSpace(JsonSerializer.Serialize(references, json), nameof(sourceReferencesJson), MaxCurriculumJsonLength);
        if (score <= 0 || score > 1000 || decimal.Round(score, 2) != score) throw new BusinessException("KnowPulse:Education:InvalidPointWeights");
        CurriculumScore = score;
        var weights = JsonSerializer.Deserialize<List<QuestionCurriculumPointWeight>>(CurriculumPointWeightsJson, json) ?? [];
        if (weights.Count > 0)
        {
            try { ValidatePointWeights(JsonSerializer.Deserialize<EducationCurriculumSnapshot>(snapshotJson, json)!, weights, score); }
            catch (BusinessException) { CurriculumPointWeightsJson = "[]"; }
        }
        InvalidateCurriculumConfirmation();
    }

    public void ConfirmCurriculum(string pointWeightsJson, decimal score, Guid userId, DateTime now)
    {
        if (CurriculumSnapshotJson == null || userId == Guid.Empty || PersonalOwnerUserId != userId || now.Kind != DateTimeKind.Utc || score <= 0 || score > 1000 || decimal.Round(score, 2) != score)
            throw new BusinessException("KnowPulse:Education:CurriculumConfirmationRequired");
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ValidatePointWeights(JsonSerializer.Deserialize<EducationCurriculumSnapshot>(CurriculumSnapshotJson, json)!,
            JsonSerializer.Deserialize<List<QuestionCurriculumPointWeight>>(pointWeightsJson, json) ?? [], score);
        ValidateScoringDefinition(score, JsonSerializer.Deserialize<List<QuestionCurriculumPointWeight>>(pointWeightsJson, json));
        CurriculumPointWeightsJson = Check.NotNullOrWhiteSpace(pointWeightsJson, nameof(pointWeightsJson), MaxCurriculumJsonLength);
        CurriculumScore = score;
        CurriculumConfirmed = true;
        CurriculumConfirmedByUserId = userId;
        CurriculumConfirmedAtUtc = now;
    }

    public void SetCurriculumPointWeightsDraft(string pointWeightsJson)
    {
        CurriculumPointWeightsJson = Check.NotNullOrWhiteSpace(pointWeightsJson, nameof(pointWeightsJson), MaxCurriculumJsonLength);
        InvalidateCurriculumConfirmation();
    }

    public static void ValidatePointWeights(EducationCurriculumSnapshot snapshot, IReadOnlyCollection<QuestionCurriculumPointWeight> weights, decimal score)
    {
        if (snapshot == null || weights == null || weights.Count is < 1 or > 3000 || score <= 0 || score > 1000 || decimal.Round(score, 2) != score ||
            weights.Any(x => x.TextbookKnowledgePointMappingId == Guid.Empty || x.Weight <= 0 || x.Weight > 1 || x.Score <= 0 || decimal.Round(x.Score, 2) != x.Score ||
                x.Score != decimal.Round(score * x.Weight, 2, MidpointRounding.AwayFromZero) ||
                !snapshot.Mappings.Any(m => m.MappingId == x.TextbookKnowledgePointMappingId)) ||
            weights.Select(x => x.TextbookKnowledgePointMappingId).Distinct().Count() != weights.Count ||
            weights.Sum(x => x.Weight) != 1 || weights.Sum(x => x.Score) != score)
            throw new BusinessException("KnowPulse:Education:InvalidPointWeights");
    }

    public void InvalidateCurriculumConfirmation()
    {
        CurriculumConfirmed = false;
        CurriculumConfirmedByUserId = null;
        CurriculumConfirmedAtUtc = null;
        if (CurriculumSnapshotJson != null && Status is QuestionStatus.Approved or QuestionStatus.PendingReview)
        {
            Status = QuestionStatus.Draft;
            ReviewedByUserId = null;
            ReviewedTime = null;
        }
    }

    /// <summary>
    /// 私有来源产生的题目永久保留归属，删除或替换题源也不能转成组织公开题。
    /// </summary>
    public void RestrictToPersonalOwner(Guid ownerUserId)
    {
        if (ownerUserId == Guid.Empty || (PersonalOwnerUserId.HasValue && PersonalOwnerUserId != ownerUserId))
        {
            throw new ArgumentException("A personal question cannot change its owner.", nameof(ownerUserId));
        }

        PersonalOwnerUserId = ownerUserId;
    }

    public QuestionType Type { get; private set; }

    public QuestionStatus Status { get; private set; }

    public QuestionDifficulty Difficulty { get; private set; }

    public QuestionCreationSource CreationSource { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Stem { get; private set; } = string.Empty;

    public Guid? ApplicablePositionId { get; private set; }

    public int SecurityLevel { get; private set; }

    public string? Tags { get; private set; }

    public DateTime? SubmittedTime { get; private set; }

    public DateTime? ReviewedTime { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }

    public bool IsUsableForExam => Status == QuestionStatus.Approved;

    protected Question()
    {
    }

    public Question(
        Guid id,
        Guid? tenantId,
        QuestionType type,
        QuestionDifficulty difficulty,
        QuestionCreationSource creationSource,
        string title,
        string stem,
        Guid? applicablePositionId = null,
        int securityLevel = 0,
        string? tags = null)
        : base(id)
    {
        TenantId = tenantId;
        if (!Enum.IsDefined(type)) throw new BusinessException("KnowPulse:Education:InvalidScoringDefinition");
        Type = type;
        Difficulty = difficulty;
        CreationSource = creationSource;
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), MaxTitleLength);
        Stem = Check.NotNullOrWhiteSpace(stem, nameof(stem), MaxStemLength);
        ApplicablePositionId = applicablePositionId;
        Tags = Check.Length(tags, nameof(tags), MaxTagsLength);
        SetSecurityLevel(securityLevel);
        Status = QuestionStatus.Draft;
    }

    public void UpdateDraft(
        QuestionType type,
        QuestionDifficulty difficulty,
        string title,
        string stem,
        Guid? applicablePositionId,
        int securityLevel,
        string? tags)
    {
        if (Status == QuestionStatus.Archived)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.QuestionCannotEditArchived);
        }

        if (!Enum.IsDefined(type)) throw new BusinessException("KnowPulse:Education:InvalidScoringDefinition");
        Type = type;
        Difficulty = difficulty;
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), MaxTitleLength);
        Stem = Check.NotNullOrWhiteSpace(stem, nameof(stem), MaxStemLength);
        ApplicablePositionId = applicablePositionId;
        Tags = Check.Length(tags, nameof(tags), MaxTagsLength);
        SetSecurityLevel(securityLevel);

        if (Status is QuestionStatus.Approved or QuestionStatus.Rejected or QuestionStatus.ReturnedForRevision or QuestionStatus.Disabled)
        {
            Status = QuestionStatus.Draft;
            ReviewedByUserId = null;
            ReviewedTime = null;
        }
    }

    public void SubmitForReview(
        IReadOnlyCollection<QuestionOption> options,
        IReadOnlyCollection<QuestionAnswer> answers,
        DateTime now)
    {
        if (Status is not QuestionStatus.Draft and not QuestionStatus.Rejected and not QuestionStatus.ReturnedForRevision)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.QuestionSubmitRequiresDraftStatus);
        }

        EnsureDefinitionIsValid(options, answers);
        Status = QuestionStatus.PendingReview;
        SubmittedTime = now;
    }

    public void Approve(Guid? reviewerUserId, DateTime now)
    {
        EnsurePendingReview();
        Status = QuestionStatus.Approved;
        ReviewedByUserId = reviewerUserId;
        ReviewedTime = now;
    }

    public void Reject(Guid? reviewerUserId, DateTime now)
    {
        EnsurePendingReview();
        Status = QuestionStatus.Rejected;
        ReviewedByUserId = reviewerUserId;
        ReviewedTime = now;
    }

    public void ReturnForRevision(Guid? reviewerUserId, DateTime now)
    {
        EnsurePendingReview();
        Status = QuestionStatus.ReturnedForRevision;
        ReviewedByUserId = reviewerUserId;
        ReviewedTime = now;
    }

    public void Disable(DateTime now)
    {
        if (Status == QuestionStatus.Archived)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.QuestionCannotDisableArchived);
        }

        Status = QuestionStatus.Disabled;
        ReviewedTime ??= now;
    }

    public void Archive(DateTime now)
    {
        Status = QuestionStatus.Archived;
        ReviewedTime ??= now;
    }

    public void EnsureDefinitionIsValid(
        IReadOnlyCollection<QuestionOption> options,
        IReadOnlyCollection<QuestionAnswer> answers,
        decimal? score = null,
        IReadOnlyCollection<QuestionCurriculumPointWeight>? pointWeights = null)
    {
        ValidateScoringDefinition(score, pointWeights);
        if (Type == QuestionType.Subjective || Type == QuestionType.FillBlank && ScoringDefinitionJson != null)
        {
            if (answers.Count != 0 || options.Count != 0)
                throw new BusinessException("KnowPulse:Education:InvalidScoringDefinition");
            return;
        }
        var answerValues = answers
            .Select(x => x.Value.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (answerValues.Count == 0)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.QuestionAnswerRequired);
        }

        if (Type is QuestionType.SingleChoice or QuestionType.MultipleChoice)
        {
            var optionKeys = options
                .Select(x => x.OptionKey.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (optionKeys.Count < 2)
            {
                throw new BusinessException(KnowPulseDomainErrorCodes.QuestionChoiceOptionsRequired);
            }

            if (answerValues.Any(answer => !optionKeys.Contains(answer, StringComparer.OrdinalIgnoreCase)))
            {
                throw new BusinessException(KnowPulseDomainErrorCodes.QuestionAnswerMustMatchOption);
            }

            if (Type == QuestionType.SingleChoice && answerValues.Count != 1)
            {
                throw new BusinessException(KnowPulseDomainErrorCodes.QuestionSingleChoiceRequiresOneAnswer);
            }

            if (Type == QuestionType.MultipleChoice && answerValues.Count < 2)
            {
                throw new BusinessException(KnowPulseDomainErrorCodes.QuestionMultipleChoiceRequiresAtLeastTwoAnswers);
            }
        }

        if (Type == QuestionType.TrueFalse)
        {
            if (answerValues.Count != 1 ||
                !IsTrueFalseAnswer(answerValues[0]))
            {
                throw new BusinessException(KnowPulseDomainErrorCodes.QuestionTrueFalseAnswerInvalid);
            }
        }
    }

    private void SetSecurityLevel(int securityLevel)
    {
        if (securityLevel < 0 || securityLevel > 10)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.InvalidSecurityLevel);
        }

        SecurityLevel = securityLevel;
    }

    private void EnsurePendingReview()
    {
        if (Status != QuestionStatus.PendingReview)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.QuestionReviewRequiresPendingStatus);
        }
    }

    private static bool IsTrueFalseAnswer(string value)
    {
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("false", StringComparison.OrdinalIgnoreCase)
               || value.Equals("对", StringComparison.OrdinalIgnoreCase)
               || value.Equals("错", StringComparison.OrdinalIgnoreCase);
    }
}
