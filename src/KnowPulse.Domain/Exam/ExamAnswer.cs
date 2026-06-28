using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Exam;

public class ExamAnswer : FullAuditedEntity<Guid>, IMultiTenant
{
    public const int MaxAnswerJsonLength = 4000;
    public const int MaxIdempotencyKeyLength = 128;

    public Guid? TenantId { get; protected set; }

    public Guid AttemptId { get; private set; }

    public Guid PaperQuestionId { get; private set; }

    public string AnswerJson { get; private set; } = "[]";

    public int SaveVersion { get; private set; }

    public string? LastSaveIdempotencyKey { get; private set; }

    public DateTime SavedTime { get; private set; }

    protected ExamAnswer()
    {
    }

    public ExamAnswer(
        Guid id,
        Guid? tenantId,
        Guid attemptId,
        Guid paperQuestionId,
        string answerJson,
        int saveVersion,
        string? idempotencyKey,
        DateTime savedTime)
        : base(id)
    {
        TenantId = tenantId;
        AttemptId = attemptId;
        PaperQuestionId = paperQuestionId;
        SetAnswer(answerJson, saveVersion, idempotencyKey, savedTime);
    }

    public bool Save(string answerJson, int? clientVersion, string? idempotencyKey, DateTime savedTime)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey) &&
            string.Equals(LastSaveIdempotencyKey, idempotencyKey, StringComparison.Ordinal))
        {
            return false;
        }

        var nextVersion = clientVersion.HasValue && clientVersion.Value > SaveVersion
            ? clientVersion.Value
            : SaveVersion + 1;

        SetAnswer(answerJson, nextVersion, idempotencyKey, savedTime);
        return true;
    }

    private void SetAnswer(string answerJson, int saveVersion, string? idempotencyKey, DateTime savedTime)
    {
        if (saveVersion <= 0)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamAnswerVersionInvalid);
        }

        AnswerJson = Check.NotNull(answerJson, nameof(answerJson)).Length <= MaxAnswerJsonLength
            ? answerJson
            : throw new BusinessException(KnowPulseDomainErrorCodes.ExamAnswerTooLarge);
        SaveVersion = saveVersion;
        LastSaveIdempotencyKey = Check.Length(idempotencyKey, nameof(idempotencyKey), MaxIdempotencyKeyLength);
        SavedTime = savedTime;
    }
}
