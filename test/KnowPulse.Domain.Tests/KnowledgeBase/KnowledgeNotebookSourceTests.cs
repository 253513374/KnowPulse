using System;
using System.Linq;
using KnowPulse.Education;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.KnowledgeBase;

public class KnowledgeNotebookSourceTests
{
    private static KnowledgeItem Note() { var owner = Guid.NewGuid(); return new(Guid.NewGuid(), null, "私有笔记", libraryId: owner, ownerUserId: owner); }
    private static EducationNotebookSourcePointer Source() => new() { Kind = EducationNotebookSourceKind.AssessmentQuestion,
        RecipientId = Guid.NewGuid(), SourceVersionId = Guid.NewGuid(), ComponentId = Guid.NewGuid() };
    private static string Fingerprint(char value) => new(value, 64);

    [Fact]
    public void Bookmark_Should_Keep_Exact_Version_And_Stable_Add_Remove_Receipts()
    {
        var note = Note(); var version = Guid.NewGuid(); var source = Source(); var operation = Guid.NewGuid(); var now = DateTime.UtcNow;
        var added = note.AddNotebookSource(operation, version, source, Fingerprint('a'), now);
        note.AddNotebookSource(operation, version, source, Fingerprint('a'), now.AddMinutes(1)).ShouldBe(added);
        note.GetPrivateNotebookSources().Single().Source.ShouldBe(source);
        Should.Throw<BusinessException>(() => note.AddNotebookSource(operation, version, source, Fingerprint('b'), now)).Code.ShouldBe(EducationNotebookSourceErrors.Conflict);
        Should.Throw<BusinessException>(() => note.AddNotebookSource(Guid.NewGuid(), version, source, Fingerprint('b'), now)).Code.ShouldBe(EducationNotebookSourceErrors.Conflict);
        var removal = Guid.NewGuid(); var removed = note.RemoveNotebookSource(removal, operation, Fingerprint('c'), now.AddMinutes(1));
        note.RemoveNotebookSource(removal, operation, Fingerprint('c'), now.AddMinutes(2)).ShouldBe(removed);
        note.FindNotebookSourceOperation(operation).ShouldBe(removed);
        note.FindNotebookSourceOperation(removal).ShouldBe(removed);
        note.AddNotebookSource(Guid.NewGuid(), version, source, Fingerprint('d'), now.AddMinutes(2));
        note.GetPrivateNotebookSources().Count(x => !x.RemovedAtUtc.HasValue).ShouldBe(1);
    }

    [Fact]
    public void Bookmark_Should_Validate_Pointers_And_Bound_History_Without_Prune()
    {
        var note = Note(); var version = Guid.NewGuid(); var now = DateTime.UtcNow;
        Should.Throw<BusinessException>(() => note.AddNotebookSource(Guid.NewGuid(), version, Source() with { SourceVersionId = Guid.Empty }, Fingerprint('a'), now));
        Should.Throw<BusinessException>(() => note.AddNotebookSource(Guid.NewGuid(), version, Source() with { Kind = (EducationNotebookSourceKind)99 }, Fingerprint('a'), now));
        Should.Throw<BusinessException>(() => note.AddNotebookSource(Guid.NewGuid(), version, Source(), "invalid", now));
        for (var i = 0; i < KnowledgeItem.MaxNotebookSourcesPerVersion; i++) note.AddNotebookSource(Guid.NewGuid(), version, Source(), Fingerprint('a'), now);
        Should.Throw<BusinessException>(() => note.AddNotebookSource(Guid.NewGuid(), version, Source(), Fingerprint('a'), now)).Code.ShouldBe(EducationNotebookSourceErrors.Limit);
        for (var i = KnowledgeItem.MaxNotebookSourcesPerVersion; i < KnowledgeItem.MaxNotebookSourceRecords; i++)
            note.AddNotebookSource(Guid.NewGuid(), Guid.NewGuid(), Source(), Fingerprint('a'), now);
        Should.Throw<BusinessException>(() => note.AddNotebookSource(Guid.NewGuid(), Guid.NewGuid(), Source(), Fingerprint('a'), now)).Code.ShouldBe(EducationNotebookSourceErrors.Limit);
        note.GetPrivateNotebookSources().Count.ShouldBe(KnowledgeItem.MaxNotebookSourceRecords);
    }

    [Fact]
    public void Bookmark_Should_Reject_Organization_Items_And_Corrupt_Stored_Records()
    {
        Should.Throw<BusinessException>(() => new KnowledgeItem(Guid.NewGuid(), null, "组织知识").GetPrivateNotebookSources());
        var note = Note(); note.ExtraProperties["PrivateNotebookSourcesV1"] = "not-json";
        Should.Throw<BusinessException>(() => note.GetPrivateNotebookSources());
        note.ExtraProperties["PrivateNotebookSourcesV1"] = "[null]";
        Should.Throw<BusinessException>(() => note.GetPrivateNotebookSources());
    }
}
