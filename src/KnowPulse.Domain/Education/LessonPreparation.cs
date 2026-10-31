using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

/// <summary>个人备课只推进当前修订指针；历次内容和课程证据不原地改写。</summary>
public class LessonPreparation : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid LibraryId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public Guid? CurrentRevisionId { get; private set; }
    public int CurrentRevisionNumber { get; private set; }
    public Guid? CopiedFromRevisionId { get; private set; }
    public string CreateFingerprint { get; private set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; private set; }

    protected LessonPreparation() { }
    public LessonPreparation(Guid id, Guid? tenantId, Guid ownerUserId, Guid libraryId, string title,
        string createFingerprint, DateTime now, Guid? copiedFromRevisionId = null) : base(EducationRules.RequireId(id))
    {
        TenantId = tenantId;
        OwnerUserId = EducationRules.RequireId(ownerUserId);
        LibraryId = EducationRules.RequireId(libraryId);
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), 256);
        CreateFingerprint = Check.NotNullOrWhiteSpace(createFingerprint, nameof(createFingerprint), 64);
        if (copiedFromRevisionId == Guid.Empty) throw new BusinessException(EducationPreparationErrorCodes.InvalidSource);
        CopiedFromRevisionId = copiedFromRevisionId;
        UpdatedAtUtc = EducationRules.RequireUtc(now);
    }
    public void Advance(LessonPreparationRevision revision)
    {
        if (revision.PreparationId != Id || revision.OwnerUserId != OwnerUserId || revision.TenantId != TenantId ||
            revision.RevisionNumber != CurrentRevisionNumber + 1 || revision.RecordedAtUtc < UpdatedAtUtc)
            throw new BusinessException(EducationPreparationErrorCodes.InvalidSource);
        CurrentRevisionId = revision.Id;
        CurrentRevisionNumber = revision.RevisionNumber;
        Title = revision.Title;
        UpdatedAtUtc = revision.RecordedAtUtc;
    }
}

public class LessonPreparationRevision : FullAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid PreparationId { get; private set; }
    public int RevisionNumber { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public Guid SchoolId { get; private set; }
    public Guid TextbookVersionId { get; private set; }
    public string CurriculumSnapshotJson { get; private set; } = string.Empty;
    public string Objectives { get; private set; } = string.Empty;
    public string? Reflection { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;

    protected LessonPreparationRevision() { }
    public LessonPreparationRevision(Guid id, Guid? tenantId, Guid ownerUserId, Guid preparationId, int revisionNumber,
        string title, Guid schoolId, Guid textbookVersionId, string curriculumSnapshotJson, string objectives,
        string? reflection, string requestFingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (revisionNumber < 1) throw new BusinessException(EducationPreparationErrorCodes.InvalidSource);
        TenantId = tenantId;
        OwnerUserId = EducationRules.RequireId(ownerUserId);
        PreparationId = EducationRules.RequireId(preparationId);
        RevisionNumber = revisionNumber;
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), 256);
        SchoolId = EducationRules.RequireId(schoolId);
        TextbookVersionId = EducationRules.RequireId(textbookVersionId);
        CurriculumSnapshotJson = Check.NotNullOrWhiteSpace(curriculumSnapshotJson, nameof(curriculumSnapshotJson), 200000);
        Objectives = Check.NotNullOrWhiteSpace(objectives, nameof(objectives), 8192);
        Reflection = Check.Length(reflection, nameof(reflection), 8192);
        RequestFingerprint = Check.NotNullOrWhiteSpace(requestFingerprint, nameof(requestFingerprint), 64);
        RecordedAtUtc = EducationRules.RequireUtc(now);
    }
}

public class LessonPreparationRevisionMapping : FullAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid RevisionId { get; private set; }
    public Guid TextbookVersionId { get; private set; }
    public Guid TextbookKnowledgePointMappingId { get; private set; }
    public Guid ChapterId { get; private set; }
    protected LessonPreparationRevisionMapping() { }
    public LessonPreparationRevisionMapping(Guid id, Guid? tenantId, Guid revisionId, Guid textbookVersionId, Guid mappingId, Guid chapterId)
        : base(EducationRules.RequireId(id))
    {
        TenantId = tenantId;
        RevisionId = EducationRules.RequireId(revisionId);
        TextbookVersionId = EducationRules.RequireId(textbookVersionId);
        TextbookKnowledgePointMappingId = EducationRules.RequireId(mappingId);
        ChapterId = EducationRules.RequireId(chapterId);
    }
}

public class LessonPreparationReference : FullAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid RevisionId { get; private set; }
    public LessonPreparationReferenceKind Kind { get; private set; }
    public Guid? MaterialBindingId { get; private set; }
    public Guid? KnowledgeVersionId { get; private set; }
    public Guid? KnowledgeChunkId { get; private set; }
    public Guid? QuestionId { get; private set; }
    public Guid? ExamPaperId { get; private set; }
    public string TitleSnapshot { get; private set; } = string.Empty;
    public int? SnapshotVersion { get; private set; }
    public string? SourceConcurrencyStamp { get; private set; }

    protected LessonPreparationReference() { }
    public LessonPreparationReference(Guid id, Guid? tenantId, Guid revisionId, LessonPreparationReferenceKind kind,
        string title, Guid? bindingId = null, Guid? versionId = null, Guid? chunkId = null,
        Guid? questionId = null, Guid? paperId = null, int? snapshotVersion = null, string? sourceStamp = null)
        : base(EducationRules.RequireId(id))
    {
        if (bindingId == Guid.Empty || versionId == Guid.Empty || chunkId == Guid.Empty || questionId == Guid.Empty || paperId == Guid.Empty ||
            (kind == LessonPreparationReferenceKind.Material && (!bindingId.HasValue || !versionId.HasValue || questionId.HasValue || paperId.HasValue)) ||
            (kind == LessonPreparationReferenceKind.Question && (!questionId.HasValue || bindingId.HasValue || versionId.HasValue || chunkId.HasValue || paperId.HasValue)) ||
            (kind == LessonPreparationReferenceKind.ExamPaper && (!paperId.HasValue || bindingId.HasValue || versionId.HasValue || chunkId.HasValue || questionId.HasValue)) ||
            !Enum.IsDefined(kind))
            throw new BusinessException(EducationPreparationErrorCodes.InvalidSource);
        TenantId = tenantId;
        RevisionId = EducationRules.RequireId(revisionId);
        Kind = kind;
        MaterialBindingId = bindingId;
        KnowledgeVersionId = versionId;
        KnowledgeChunkId = chunkId;
        QuestionId = questionId;
        ExamPaperId = paperId;
        TitleSnapshot = Check.NotNullOrWhiteSpace(title, nameof(title), 256);
        SnapshotVersion = snapshotVersion;
        SourceConcurrencyStamp = Check.Length(sourceStamp, nameof(sourceStamp), 40);
    }
}
