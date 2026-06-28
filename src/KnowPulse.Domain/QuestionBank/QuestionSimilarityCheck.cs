using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.QuestionBank;

public class QuestionSimilarityCheck : CreationAuditedEntity<Guid>, IMultiTenant
{
    public const int MaxSummaryLength = 512;

    public Guid? TenantId { get; protected set; }

    public Guid QuestionId { get; private set; }

    public Guid? SimilarQuestionId { get; private set; }

    public double SimilarityScore { get; private set; }

    public string? Summary { get; private set; }

    protected QuestionSimilarityCheck()
    {
    }

    public QuestionSimilarityCheck(
        Guid id,
        Guid? tenantId,
        Guid questionId,
        Guid? similarQuestionId,
        double similarityScore,
        string? summary)
        : base(id)
    {
        if (similarityScore < 0 || similarityScore > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(similarityScore));
        }

        TenantId = tenantId;
        QuestionId = questionId;
        SimilarQuestionId = similarQuestionId;
        SimilarityScore = similarityScore;
        Summary = Check.Length(summary, nameof(summary), MaxSummaryLength);
    }
}
