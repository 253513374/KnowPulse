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
