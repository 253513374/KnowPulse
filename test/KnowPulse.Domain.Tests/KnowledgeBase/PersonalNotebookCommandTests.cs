using System;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.KnowledgeBase;

public class PersonalNotebookCommandTests
{
    private static KnowledgeItem Note() { var n = new KnowledgeItem(Guid.NewGuid(), null, "本人笔记", libraryId: Guid.NewGuid(), ownerUserId: Guid.NewGuid()); n.OrganizePersonalMaterial(PersonalMaterialType.Note, null, null, []); return n; }
    [Fact]
    public void Receipt_Should_Reject_Cross_Note_Version_And_Cross_Version_File()
    {
        var n = Note(); var v = new KnowledgeVersion(Guid.NewGuid(), null, n.Id, 1, "第一版", "私有正文");
        var foreign = new KnowledgeVersion(Guid.NewGuid(), null, Guid.NewGuid(), 1, "其他笔记");
        var file = new KnowledgeAttachment(Guid.NewGuid(), null, foreign.Id, "私有.pdf", "blob", "application/pdf", 3); var hash = new string('a', 64);
        Should.Throw<BusinessException>(() => new PersonalNotebookCommand(Guid.NewGuid(), n, PersonalNotebookCommandKind.Created, hash, DateTime.UtcNow, foreign));
        Should.Throw<BusinessException>(() => new PersonalNotebookCommand(Guid.NewGuid(), n, PersonalNotebookCommandKind.UploadedAttachment, hash, DateTime.UtcNow, v, file, hash));
        var r = new PersonalNotebookCommand(Guid.NewGuid(), n, PersonalNotebookCommandKind.Created, hash, DateTime.UtcNow, v);
        r.VersionNumber.ShouldBe(1); r.OwnerUserId.ShouldBe(n.OwnerUserId!.Value); r.ContentHash.ShouldBeNull();
    }
    [Fact]
    public void Deleted_Receipt_Should_Have_No_Version_File_Hash_Or_Private_Body()
    {
        var n = Note(); var hash = new string('a', 64); var v = new KnowledgeVersion(Guid.NewGuid(), null, n.Id, 1, "第一版");
        Should.Throw<BusinessException>(() => new PersonalNotebookCommand(Guid.NewGuid(), n, PersonalNotebookCommandKind.Deleted, hash, DateTime.UtcNow, v));
        Should.Throw<BusinessException>(() => new PersonalNotebookCommand(Guid.NewGuid(), n, PersonalNotebookCommandKind.Deleted, hash, DateTime.Now));
        Should.Throw<BusinessException>(() => new PersonalNotebookCommand(Guid.NewGuid(), n, PersonalNotebookCommandKind.Deleted, hash.ToUpperInvariant(), DateTime.UtcNow));
        var r = new PersonalNotebookCommand(Guid.NewGuid(), n, PersonalNotebookCommandKind.Deleted, hash, DateTime.UtcNow);
        r.VersionId.ShouldBeNull(); r.AttachmentId.ShouldBeNull(); r.FileSize.ShouldBeNull();
    }
}
