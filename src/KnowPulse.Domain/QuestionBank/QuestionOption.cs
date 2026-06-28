using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.QuestionBank;

public class QuestionOption : FullAuditedEntity<Guid>, IMultiTenant
{
    public const int MaxOptionKeyLength = 16;
    public const int MaxContentLength = 1024;

    public Guid? TenantId { get; protected set; }

    public Guid QuestionId { get; private set; }

    public string OptionKey { get; private set; } = string.Empty;

    public string Content { get; private set; } = string.Empty;

    public int DisplayOrder { get; private set; }

    protected QuestionOption()
    {
    }

    public QuestionOption(Guid id, Guid? tenantId, Guid questionId, string optionKey, string content, int displayOrder)
        : base(id)
    {
        TenantId = tenantId;
        QuestionId = questionId;
        OptionKey = Check.NotNullOrWhiteSpace(optionKey, nameof(optionKey), MaxOptionKeyLength);
        Content = Check.NotNullOrWhiteSpace(content, nameof(content), MaxContentLength);
        DisplayOrder = displayOrder;
    }
}
