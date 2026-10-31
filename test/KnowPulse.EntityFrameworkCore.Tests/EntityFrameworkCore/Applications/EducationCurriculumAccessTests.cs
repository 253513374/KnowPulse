using System;
using System.Threading.Tasks;
using KnowPulse.Education;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Identity;
using Volo.Abp.Uow;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Fact]
    public async Task Curriculum_Reads_Should_Not_Change_Reference_Concurrency_Stamps()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var before = await CurriculumReferenceStampsAsync(f);
        var guard = GetRequiredService<EducationCurriculumAccessGuard>();
        var snapshot = await guard.ResolveAsync(Search(f, KnowPulse.RagAI.KnowledgeSearchScope.PersonalLibrary).CurriculumScope!);
        await guard.ValidateAsync(snapshot);
        await guard.GetMappingAsync(f.MappingA);
        (await CurriculumReferenceStampsAsync(f)).ShouldBe(before);
    }

    [Fact]
    public async Task New_Curriculum_Use_Should_Protect_School_And_Account_Without_Editing_Textbook_Selection()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var before = await CurriculumReferenceStampsAsync(f);
        var guard = GetRequiredService<EducationCurriculumAccessGuard>();
        var snapshot = await guard.ResolveAsync(Search(f, KnowPulse.RagAI.KnowledgeSearchScope.PersonalLibrary).CurriculumScope!);
        await WithUnitOfWorkAsync(() => guard.ValidateForUseAsync(snapshot));
        var after = await CurriculumReferenceStampsAsync(f);
        after.School.ShouldNotBe(before.School);
        after.Account.ShouldNotBe(before.Account);
        after.Book.ShouldBe(before.Book);
        after.Selection.ShouldBe(before.Selection);
    }

    [Fact]
    public async Task Forged_Point_Revision_In_Snapshot_Should_Be_Rejected()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var guard = GetRequiredService<EducationCurriculumAccessGuard>();
        var snapshot = await guard.ResolveAsync(Search(f, KnowPulse.RagAI.KnowledgeSearchScope.PersonalLibrary).CurriculumScope!);
        snapshot.Mappings[0].KnowledgePointRevisionId = Guid.NewGuid();
        (await Should.ThrowAsync<BusinessException>(() => guard.ValidateAsync(snapshot))).Code
            .ShouldBe(EducationCurriculumErrorCodes.ReferenceMismatch);
    }

    [Fact]
    public async Task Ended_Selection_Should_Block_New_Use_Of_An_Old_Snapshot()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var guard = GetRequiredService<EducationCurriculumAccessGuard>();
        var snapshot = await guard.ResolveAsync(Search(f, KnowPulse.RagAI.KnowledgeSearchScope.PersonalLibrary).CurriculumScope!);
        await WithUnitOfWorkAsync(async () =>
        {
            var selection = await Repo<TeacherTextbookSelection>().GetAsync(f.ReaderSelection);
            selection.End(DateTime.UtcNow);
            await Repo<TeacherTextbookSelection>().UpdateManyAsync([selection], autoSave: true);
        });
        await Should.ThrowAsync<AbpAuthorizationException>(() => guard.ValidateForUseAsync(snapshot));
    }

    [Fact]
    public async Task Disabled_Account_Should_Block_Empty_Source_Curriculum_Use()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var guard = GetRequiredService<EducationCurriculumAccessGuard>();
        var snapshot = await guard.ResolveAsync(Search(f, KnowPulse.RagAI.KnowledgeSearchScope.PersonalLibrary).CurriculumScope!);
        await WithUnitOfWorkAsync(async () =>
        {
            var account = await Repo<IdentityUser>().GetAsync(f.Reader);
            account.SetIsActive(false);
            await Repo<IdentityUser>().UpdateManyAsync([account], autoSave: true);
        });
        await Should.ThrowAsync<AbpAuthorizationException>(() => guard.ValidateForUseAsync(snapshot));
    }

    [Fact]
    public async Task Material_Organization_Should_Not_Require_A_Current_Teaching_Assignment()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        await WithUnitOfWorkAsync(async () =>
        {
            var selection = await Repo<TeacherTextbookSelection>().GetAsync(f.ReaderSelection);
            var assignment = await Repo<TeachingAssignment>().GetAsync(selection.TeachingAssignmentId);
            assignment.EndAt(DateTime.UtcNow);
            await Repo<TeachingAssignment>().UpdateManyAsync([assignment], autoSave: true);
        });
        var guard = GetRequiredService<EducationCurriculumAccessGuard>();
        var mapping = await WithUnitOfWorkAsync(() => guard.GetMappingAsync(f.MappingA, forUpdate: true));
        mapping.Mappings.ShouldHaveSingleItem();
        mapping.TeachingAssignmentId.ShouldBe(Guid.Empty);
        await Should.ThrowAsync<AbpAuthorizationException>(() => guard.ResolveAsync(Search(f, KnowPulse.RagAI.KnowledgeSearchScope.PersonalLibrary).CurriculumScope!));
    }

    [Fact]
    public async Task New_Use_Should_Reject_A_School_Changed_After_The_Command_Loaded_Its_Baseline()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var guard = GetRequiredService<EducationCurriculumAccessGuard>();
        var snapshot = await guard.ResolveAsync(Search(f, KnowPulse.RagAI.KnowledgeSearchScope.PersonalLibrary).CurriculumScope!);
        using var command = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
        var baseline = await Repo<School>().GetAsync(f.SchoolId);
        using (var concurrent = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true))
        {
            var changed = await Repo<School>().GetAsync(f.SchoolId);
            changed.AdvanceConfigurationRevision();
            await Repo<School>().UpdateManyAsync([changed], autoSave: true);
            await concurrent.CompleteAsync();
        }
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => guard.ValidateForUseAsync(snapshot));
        baseline.Id.ShouldBe(f.SchoolId);
    }

    private Task<CurriculumReferenceStamps> CurriculumReferenceStampsAsync(Fixture f) => WithUnitOfWorkAsync(async () => new CurriculumReferenceStamps(
        (await Repo<School>().GetAsync(f.SchoolId)).ConcurrencyStamp,
        (await Repo<IdentityUser>().GetAsync(f.Reader)).ConcurrencyStamp,
        (await Repo<TextbookVersion>().GetAsync(f.BookId)).ConcurrencyStamp,
        (await Repo<TeacherTextbookSelection>().GetAsync(f.ReaderSelection)).ConcurrencyStamp));

    private sealed record CurriculumReferenceStamps(string School, string Account, string Book, string Selection);
}
