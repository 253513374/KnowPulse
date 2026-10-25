using System;
using System.Collections.Generic;
using System.Linq;
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

    public Guid? OwnerTeacherId { get; private set; }

    public void AssignTeacher(Guid teacherId)
    {
        if (teacherId == Guid.Empty || (OwnerTeacherId.HasValue && OwnerTeacherId != teacherId))
        {
            throw new ArgumentException("题目归属不能为空或被改写。", nameof(teacherId));
        }

        OwnerTeacherId = teacherId;
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
        IReadOnlyCollection<QuestionAnswer> answers)
    {
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
