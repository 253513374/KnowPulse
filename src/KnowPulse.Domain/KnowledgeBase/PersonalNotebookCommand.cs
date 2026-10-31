using System;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.KnowledgeBase;

/// <summary>A private immutable receipt; note bodies and original files stay in their existing stores.</summary>
public class PersonalNotebookCommand : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid NoteId { get; private set; }
    public PersonalNotebookCommandKind Kind { get; private set; }
    public Guid? VersionId { get; private set; }
    public int? VersionNumber { get; private set; }
    public Guid? AttachmentId { get; private set; }
    public string RequestFingerprint { get; private set; } = "";
    public string? ContentHash { get; private set; }
    public long? FileSize { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    protected PersonalNotebookCommand() { }

    public PersonalNotebookCommand(Guid id, KnowledgeItem note, PersonalNotebookCommandKind kind, string fingerprint, DateTime now,
        KnowledgeVersion? version = null, KnowledgeAttachment? attachment = null, string? contentHash = null) : base(id)
    {
        var upload = kind == PersonalNotebookCommandKind.UploadedAttachment; var deleted = kind == PersonalNotebookCommandKind.Deleted;
        if (id == Guid.Empty || !Enum.IsDefined(kind) || note.OwnerUserId is not Guid owner || owner == Guid.Empty || !note.LibraryId.HasValue || note.MaterialType != PersonalMaterialType.Note ||
            now.Kind != DateTimeKind.Utc || deleted != (version == null) || version != null && (version.KnowledgeItemId != note.Id || version.TenantId != note.TenantId) ||
            upload != (attachment != null) || attachment != null && (attachment.KnowledgeVersionId != version!.Id || attachment.TenantId != note.TenantId) ||
            !Hash(fingerprint) || upload && !Hash(contentHash) || !upload && contentHash != null) throw new BusinessException(PersonalNotebookErrors.Invalid);
        TenantId = note.TenantId; OwnerUserId = owner; NoteId = note.Id; Kind = kind; VersionId = version?.Id; VersionNumber = version?.VersionNumber;
        AttachmentId = attachment?.Id; RequestFingerprint = fingerprint; ContentHash = contentHash; FileSize = attachment?.Size; RecordedAtUtc = now;
    }
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
