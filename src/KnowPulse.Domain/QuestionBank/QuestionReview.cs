using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.QuestionBank;

public class QuestionReview : CreationAuditedEntity<Guid>, IMultiTenant
{
    public const int MaxCommentLength = 512;

    public Guid? TenantId { get; protected set; }

    public Guid QuestionId { get; private set; }

    public Guid? ReviewerUserId { get; private set; }

    public QuestionReviewAction Action { get; private set; }

    public QuestionStatus FromStatus { get; private set; }

    public QuestionStatus ToStatus { get; private set; }

    public string? Comment { get; private set; }

    public DateTime ReviewTime { get; private set; }

    public string? OperationRequestHash { get; private set; }
    public string? ExpectedConcurrencyStamp { get; private set; }
    public Guid? PersonalOwnerUserId { get; private set; }
    public Guid? DefinitionRevisionId { get; private set; }

    public void RecordOperation(string requestHash, string expectedConcurrencyStamp, Guid? personalOwnerUserId, Guid? definitionRevisionId)
    {
        if (OperationRequestHash != null || personalOwnerUserId == Guid.Empty || definitionRevisionId == Guid.Empty)
            throw new InvalidOperationException("A review operation is immutable.");
        OperationRequestHash = Check.NotNullOrWhiteSpace(requestHash, nameof(requestHash), 64);
        if (OperationRequestHash.Length != 64) throw new ArgumentException("A SHA-256 request hash is required.", nameof(requestHash));
        ExpectedConcurrencyStamp = Check.NotNullOrWhiteSpace(expectedConcurrencyStamp, nameof(expectedConcurrencyStamp), 40);
        PersonalOwnerUserId = personalOwnerUserId;
        DefinitionRevisionId = definitionRevisionId;
    }

    protected QuestionReview()
    {
    }

    public QuestionReview(
        Guid id,
        Guid? tenantId,
        Guid questionId,
        Guid? reviewerUserId,
        QuestionReviewAction action,
        QuestionStatus fromStatus,
        QuestionStatus toStatus,
        string? comment,
        DateTime reviewTime)
        : base(id)
    {
        TenantId = tenantId;
        QuestionId = questionId;
        ReviewerUserId = reviewerUserId;
        Action = action;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Comment = Check.Length(comment, nameof(comment), MaxCommentLength);
        ReviewTime = reviewTime;
    }
}
