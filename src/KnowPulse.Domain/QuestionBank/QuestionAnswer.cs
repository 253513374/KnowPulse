using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.QuestionBank;

public class QuestionAnswer : FullAuditedEntity<Guid>, IMultiTenant
{
    public const int MaxValueLength = 512;

    public Guid? TenantId { get; protected set; }

    public Guid QuestionId { get; private set; }

    public string Value { get; private set; } = string.Empty;

    public int DisplayOrder { get; private set; }

    protected QuestionAnswer()
    {
    }

    public QuestionAnswer(Guid id, Guid? tenantId, Guid questionId, string value, int displayOrder)
        : base(id)
    {
        TenantId = tenantId;
        QuestionId = questionId;
        Value = Check.NotNullOrWhiteSpace(value, nameof(value), MaxValueLength);
        DisplayOrder = displayOrder;
    }
}
