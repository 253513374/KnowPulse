using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.QuestionBank;

public class QuestionSource : FullAuditedEntity<Guid>, IMultiTenant
{
    public const int MaxSourceTitleLength = 256;
    public const int MaxSourceExcerptLength = 512;
    public const int MaxSourceHashLength = 64;
    public const int MaxManualSourceDescriptionLength = 512;

    public Guid? TenantId { get; protected set; }

    public Guid QuestionId { get; private set; }

    public Guid? KnowledgeItemId { get; private set; }

    public Guid? KnowledgeVersionId { get; private set; }

    public Guid? ChunkId { get; private set; }

    public string? SourceTitle { get; private set; }

    public string? SourceExcerpt { get; private set; }

    public string? SourceHash { get; private set; }

    public string? ManualSourceDescription { get; private set; }

    protected QuestionSource()
    {
    }

    public QuestionSource(
        Guid id,
        Guid? tenantId,
        Guid questionId,
        Guid? knowledgeItemId,
        Guid? knowledgeVersionId,
        Guid? chunkId,
        string? sourceTitle,
        string? sourceExcerpt,
        string? sourceHash,
        string? manualSourceDescription)
        : base(id)
    {
        if (!knowledgeItemId.HasValue &&
            !knowledgeVersionId.HasValue &&
            !chunkId.HasValue &&
            string.IsNullOrWhiteSpace(manualSourceDescription))
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.QuestionSourceRequired);
        }

        TenantId = tenantId;
        QuestionId = questionId;
        KnowledgeItemId = knowledgeItemId;
        KnowledgeVersionId = knowledgeVersionId;
        ChunkId = chunkId;
        SourceTitle = Check.Length(sourceTitle, nameof(sourceTitle), MaxSourceTitleLength);
        SourceExcerpt = Check.Length(sourceExcerpt, nameof(sourceExcerpt), MaxSourceExcerptLength);
        SourceHash = Check.Length(sourceHash, nameof(sourceHash), MaxSourceHashLength);
        ManualSourceDescription = Check.Length(manualSourceDescription, nameof(manualSourceDescription), MaxManualSourceDescriptionLength);
    }
}
