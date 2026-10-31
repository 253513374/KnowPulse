using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.QuestionBank;

/// <summary>正式题定义只在审核事务内创建，没有编辑或删除用途。</summary>
public class QuestionDefinitionRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    public const int MaxDefinitionJsonLength = 1000000;
    public Guid? TenantId { get; private set; }
    public Guid QuestionId { get; private set; }
    public Guid? PersonalOwnerUserId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid ReviewOperationId { get; private set; }
    public Guid ReviewedByUserId { get; private set; }
    public DateTime ReviewedAtUtc { get; private set; }
    public string DefinitionHash { get; private set; } = string.Empty;
    public string DefinitionJson { get; private set; } = string.Empty;

    protected QuestionDefinitionRevision() { }

    public QuestionDefinitionRevision(Guid id, Guid? tenantId, Guid questionId, Guid? personalOwnerUserId,
        int revisionNumber, Guid reviewOperationId, Guid reviewedByUserId, DateTime reviewedAtUtc,
        string definitionHash, string definitionJson) : base(id)
    {
        if (id == Guid.Empty || questionId == Guid.Empty || reviewOperationId == Guid.Empty || reviewedByUserId == Guid.Empty ||
            personalOwnerUserId == Guid.Empty || revisionNumber < 1 || reviewedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("An immutable definition requires its exact question and review facts.");
        TenantId = tenantId;
        QuestionId = questionId;
        PersonalOwnerUserId = personalOwnerUserId;
        RevisionNumber = revisionNumber;
        ReviewOperationId = reviewOperationId;
        ReviewedByUserId = reviewedByUserId;
        ReviewedAtUtc = reviewedAtUtc;
        DefinitionHash = Check.NotNullOrWhiteSpace(definitionHash, nameof(definitionHash), 64);
        if (DefinitionHash.Length != 64) throw new ArgumentException("A SHA-256 definition hash is required.", nameof(definitionHash));
        DefinitionJson = Check.NotNullOrWhiteSpace(definitionJson, nameof(definitionJson), MaxDefinitionJsonLength);
    }
}
