using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class KnowledgeMaterialCurriculumBinding : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid LibraryId { get; private set; }
    public Guid KnowledgeItemId { get; private set; }
    public Guid KnowledgeVersionId { get; private set; }
    public Guid? KnowledgeChunkId { get; private set; }
    public Guid TextbookVersionId { get; private set; }
    public Guid TextbookKnowledgePointMappingId { get; private set; }
    public string CurriculumSnapshotJson { get; private set; } = string.Empty;
    public string MaterialTitle { get; private set; } = string.Empty;
    public bool WholeMaterialAppliesToMapping { get; private set; }
    public DateTime ConfirmedAtUtc { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }

    protected KnowledgeMaterialCurriculumBinding() { }
    public KnowledgeMaterialCurriculumBinding(Guid id, Guid? tenantId, Guid ownerUserId, Guid libraryId,
        Guid itemId, Guid versionId, Guid? chunkId, Guid textbookVersionId, Guid mappingId,
        string curriculumSnapshotJson, string materialTitle, bool confirmWholeMaterial, DateTime now)
        : base(EducationRules.RequireId(id))
    {
        if (chunkId == Guid.Empty || (!chunkId.HasValue && !confirmWholeMaterial))
            throw new BusinessException(EducationPreparationErrorCodes.WholeMaterialConfirmationRequired);
        TenantId = tenantId;
        OwnerUserId = EducationRules.RequireId(ownerUserId);
        LibraryId = EducationRules.RequireId(libraryId);
        KnowledgeItemId = EducationRules.RequireId(itemId);
        KnowledgeVersionId = EducationRules.RequireId(versionId);
        KnowledgeChunkId = chunkId;
        TextbookVersionId = EducationRules.RequireId(textbookVersionId);
        TextbookKnowledgePointMappingId = EducationRules.RequireId(mappingId);
        CurriculumSnapshotJson = Check.NotNullOrWhiteSpace(curriculumSnapshotJson, nameof(curriculumSnapshotJson), 200000);
        MaterialTitle = Check.NotNullOrWhiteSpace(materialTitle, nameof(materialTitle), 256);
        WholeMaterialAppliesToMapping = !chunkId.HasValue;
        ConfirmedAtUtc = EducationRules.RequireUtc(now);
    }
    public void End(DateTime now)
    {
        EducationRules.RequireUtc(now);
        if (now < ConfirmedAtUtc) throw new BusinessException(EducationErrorCodes.InvalidEffectivePeriod);
        EndedAtUtc ??= now;
    }
}
