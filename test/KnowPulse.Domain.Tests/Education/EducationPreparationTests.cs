using System;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationPreparationTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
    [Fact]
    public void Whole_Material_Requires_Explicit_Confirmation_And_End_Is_Idempotent()
    {
        Should.Throw<BusinessException>(() => Binding(null, false)).Code.ShouldBe(EducationPreparationErrorCodes.WholeMaterialConfirmationRequired);
        var whole = Binding(null, true);
        whole.WholeMaterialAppliesToMapping.ShouldBeTrue();
        whole.End(Now.AddHours(1));
        whole.End(Now.AddHours(2));
        whole.EndedAtUtc.ShouldBe(Now.AddHours(1));
        whole.KnowledgeChunkId.ShouldBeNull();
        Binding(Guid.NewGuid(), false).WholeMaterialAppliesToMapping.ShouldBeFalse();
    }
    [Fact]
    public void A_Revision_Cannot_Advance_Another_Persons_Root_Or_Skip_History()
    {
        var preparation = Root();
        var first = Revision(preparation, 1, "首课目标");
        preparation.Advance(first);
        preparation.CurrentRevisionId.ShouldBe(first.Id);
        Should.Throw<BusinessException>(() => preparation.Advance(Revision(preparation, 3, "跳过修订")));
        Should.Throw<BusinessException>(() => preparation.Advance(Revision(Root(), 2, "他人备课")));
        var second = Revision(preparation, 2, "修订目标");
        preparation.Advance(second);
        first.Objectives.ShouldBe("首课目标");
        first.Reflection.ShouldBe("仅本人反思");
        preparation.CurrentRevisionNumber.ShouldBe(2);
    }
    [Fact]
    public void A_Material_Reference_Cannot_Masquerade_As_Question_Or_Paper()
    {
        Should.Throw<BusinessException>(() => new LessonPreparationReference(Guid.NewGuid(), null, Guid.NewGuid(),
            LessonPreparationReferenceKind.Material, "来源", Guid.NewGuid(), Guid.NewGuid(), questionId: Guid.NewGuid()));
        Should.Throw<BusinessException>(() => new LessonPreparationReference(Guid.NewGuid(), null, Guid.NewGuid(),
            LessonPreparationReferenceKind.Question, "题", questionId: Guid.NewGuid(), paperId: Guid.NewGuid()));
    }
    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void Invalid_Time_Does_Not_End_A_Confirmation(DateTimeKind kind)
    {
        var binding = Binding(Guid.NewGuid(), false);
        Should.Throw<BusinessException>(() => binding.End(DateTime.SpecifyKind(Now, kind))).Code.ShouldBe(EducationErrorCodes.UtcRequired);
        binding.EndedAtUtc.ShouldBeNull();
    }
    private static KnowledgeMaterialCurriculumBinding Binding(Guid? chunk, bool whole) => new(Guid.NewGuid(), null,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), chunk, Guid.NewGuid(), Guid.NewGuid(), "{}", "资料", whole, Now);
    private static LessonPreparation Root() => new(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "备课", new string('a', 64), Now);
    private static LessonPreparationRevision Revision(LessonPreparation root, int number, string objective) => new(Guid.NewGuid(),
        root.TenantId, root.OwnerUserId, root.Id, number, "备课修订", Guid.NewGuid(), Guid.NewGuid(), "{}", objective,
        "仅本人反思", new string('b', 64), Now.AddMinutes(number));
}
