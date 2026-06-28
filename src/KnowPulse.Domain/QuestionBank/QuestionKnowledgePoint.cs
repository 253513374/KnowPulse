using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.QuestionBank;

public class QuestionKnowledgePoint : FullAuditedEntity<Guid>, IMultiTenant
{
    public const int MaxNameLength = 128;

    public Guid? TenantId { get; protected set; }

    public Guid QuestionId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    protected QuestionKnowledgePoint()
    {
    }

    public QuestionKnowledgePoint(Guid id, Guid? tenantId, Guid questionId, string name)
        : base(id)
    {
        TenantId = tenantId;
        QuestionId = questionId;
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength);
    }
}
