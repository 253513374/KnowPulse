using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.QuestionBank;

public class QuestionAnalysis : FullAuditedEntity<Guid>, IMultiTenant
{
    public const int MaxContentLength = 2048;

    public Guid? TenantId { get; protected set; }

    public Guid QuestionId { get; private set; }

    public string Content { get; private set; } = string.Empty;

    protected QuestionAnalysis()
    {
    }

    public QuestionAnalysis(Guid id, Guid? tenantId, Guid questionId, string content)
        : base(id)
    {
        TenantId = tenantId;
        QuestionId = questionId;
        Content = Check.NotNull(content, nameof(content)).Length <= MaxContentLength
            ? content
            : content[..MaxContentLength];
    }
}
