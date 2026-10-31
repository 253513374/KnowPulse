using System;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationCurriculumTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Publishing_Should_Freeze_Metadata_And_Chapter_Edits_While_Retirement_Keeps_The_Original_Version()
    {
        var book = Book();
        var chapter = new TextbookChapter(Guid.NewGuid(), null, book.Id, null, "C1", "原章节", 1);
        book.Publish(Now);
        book.Publish(Now.AddDays(1));
        book.PublishedAtUtc.ShouldBe(Now);
        Should.Throw<BusinessException>(() => book.UpdateDraft(8, "修改名称", "出版社", "2026", "下册"))
            .Code.ShouldBe(EducationCurriculumErrorCodes.ImmutableVersion);
        Should.Throw<BusinessException>(() => chapter.UpdateDraft(book, null, "C1", "修改章节", 1))
            .Code.ShouldBe(EducationCurriculumErrorCodes.ImmutableVersion);
        book.Retire(Now.AddDays(2));
        book.Retire(Now.AddDays(3));
        book.Title.ShouldBe("测试教材");
        book.Edition.ShouldBe("2025版");
        book.CatalogRevision.ShouldBe(1);
        chapter.Title.ShouldBe("原章节");
        book.RetiredAtUtc.ShouldBe(Now.AddDays(2));
        Should.Throw<BusinessException>(() => book.Publish(Now.AddDays(4)))
            .Code.ShouldBe(EducationCurriculumErrorCodes.ImmutableVersion);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Invalid_Publication_Or_Retirement_Time_Should_Not_Change_State(DateTimeKind kind)
    {
        var book = Book();
        var invalid = DateTime.SpecifyKind(Now, kind);
        Should.Throw<BusinessException>(() => book.Publish(invalid)).Code.ShouldBe(EducationErrorCodes.UtcRequired);
        book.Status.ShouldBe(TextbookVersionStatus.Draft);
        book.PublishedAtUtc.ShouldBeNull();
        book.Publish(Now);
        Should.Throw<BusinessException>(() => book.Retire(invalid)).Code.ShouldBe(EducationErrorCodes.UtcRequired);
        book.Status.ShouldBe(TextbookVersionStatus.Published);
        book.RetiredAtUtc.ShouldBeNull();
    }

    [Fact]
    public void Chapter_Edit_Should_Require_Its_Own_Draft_Root()
    {
        var book = Book();
        var chapter = new TextbookChapter(Guid.NewGuid(), null, book.Id, null, "C1", "原章节", 0);
        Should.Throw<BusinessException>(() => chapter.UpdateDraft(Book(), null, "C2", "越界修改", 1))
            .Code.ShouldBe(EducationCurriculumErrorCodes.ReferenceMismatch);
        chapter.Code.ShouldBe("C1");
        Should.Throw<BusinessException>(() => chapter.UpdateDraft(book, chapter.Id, "C2", "自父章节", 1))
            .Code.ShouldBe(EducationCurriculumErrorCodes.InvalidStructure);
        chapter.ParentChapterId.ShouldBeNull();
    }

    [Fact]
    public void A_New_Point_Revision_Should_Leave_Original_Mapping_Name_And_Revision_Unchanged()
    {
        var point = new CourseKnowledgePoint(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "KP1");
        var first = new CourseKnowledgePointRevision(Guid.NewGuid(), null, point.Id, point.AdvanceRevision(), "原知识点", null);
        var mapping = new TextbookKnowledgePointMapping(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(),
            point.Id, first.Id, Guid.NewGuid(), Now);
        var revised = new CourseKnowledgePointRevision(Guid.NewGuid(), null, point.Id, point.AdvanceRevision(), "修订名称", null);
        revised.KnowledgePointId.ShouldBe(first.KnowledgePointId);
        revised.RevisionNumber.ShouldBe(2);
        first.Name.ShouldBe("原知识点");
        first.RevisionNumber.ShouldBe(1);
        mapping.KnowledgePointRevisionId.ShouldBe(first.Id);
        mapping.ConfirmedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public void Ending_A_Selection_Should_Keep_Teacher_Assignment_And_Textbook_Facts()
    {
        var teacher = Guid.NewGuid();
        var assignment = Guid.NewGuid();
        var textbook = Guid.NewGuid();
        var selection = new TeacherTextbookSelection(Guid.NewGuid(), null, Guid.NewGuid(), teacher, assignment, textbook, Now);
        Should.Throw<BusinessException>(() => selection.End(Now.AddTicks(-1))).Code.ShouldBe(EducationErrorCodes.InvalidEffectivePeriod);
        selection.EndedAtUtc.ShouldBeNull();
        selection.End(Now.AddDays(1));
        selection.End(Now.AddDays(2));
        selection.EndedAtUtc.ShouldBe(Now.AddDays(1));
        selection.TeacherUserId.ShouldBe(teacher);
        selection.TeachingAssignmentId.ShouldBe(assignment);
        selection.TextbookVersionId.ShouldBe(textbook);
        selection.SelectedAtUtc.ShouldBe(Now);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(10)]
    public void Textbooks_Should_Restrict_The_First_Slice_To_Junior_Secondary(int grade)
    {
        Should.Throw<BusinessException>(() => new TextbookVersion(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(),
            "BOOK", 1, grade, "教材", "出版社", "版次", "册次")).Code.ShouldBe(EducationErrorCodes.InvalidGrade);
    }

    private static TextbookVersion Book() => new(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(),
        "BOOK", 1, 7, "测试教材", "测试出版社", "2025版", "上册");
}
