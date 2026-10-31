using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using NSubstitute;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Fact]
    public async Task Assessment_Draft_Options_Should_Use_The_Real_Class_And_Bounded_Saved_Paper_Pages_Without_Changing_References()
    {
        var f = await FixtureAsync();
        var student = await AssessmentStudentAsync(f);
        var otherClass = await WithUnitOfWorkAsync(async () =>
        {
            var selection = await Repo<TeacherTextbookSelection>().GetAsync(f.ReaderSelection);
            var assignment = await Repo<TeachingAssignment>().GetAsync(selection.TeachingAssignmentId);
            var row = new SchoolClass(Guid.NewGuid(), null, f.SchoolId, assignment.AcademicTermId, "OTHER", "另一个班级", 7);
            await Repo<SchoolClass>().InsertAsync(row, autoSave: true);
            return row.Id;
        });
        await AssessmentStudentAsync(f, otherClass);
        var inactive = await AssessmentStudentAsync(f);
        await WithUnitOfWorkAsync(async () =>
        {
            var account = await Repo<IdentityUser>().GetAsync(inactive.UserId);
            account.SetIsActive(false);
            await Repo<IdentityUser>().UpdateManyAsync([account], autoSave: true);
        });
        using var teacher = User(f.Reader);
        var input = await AssessmentInputAsync(f);
        var another = await AssessmentInputAsync(f);
        var before = await WithUnitOfWorkAsync(async () =>
        {
            var selection = await Repo<TeacherTextbookSelection>().GetAsync(f.ReaderSelection);
            var assignment = await Repo<TeachingAssignment>().GetAsync(selection.TeachingAssignmentId);
            var term = await Repo<AcademicTerm>().GetAsync(assignment.AcademicTermId);
            var school = await Repo<School>().GetAsync(f.SchoolId);
            return (school.ConcurrencyStamp, selectionStamp: selection.ConcurrencyStamp, assignment.EffectiveFromUtc,
                assignment.EffectiveToUtc, term.StartsAtUtc, term.EndsAtUtc);
        });
        var first = await Assessments.GetDraftOptionsAsync(new() { Scope = input.Scope, MaxResultCount = 1 });
        first.Students.Select(x => x.StudentUserId).ShouldBe(new[] { student.UserId });
        first.Students.Single().AccountName.ShouldStartWith("student-");
        first.SavedPapers.Count.ShouldBe(1);
        first.HasMoreSavedPapers.ShouldBeTrue();
        first.NextPaperSkipCount.ShouldBe(1);
        first.EarliestStartsAtUtc.ShouldBe(before.StartsAtUtc > before.EffectiveFromUtc ? before.StartsAtUtc : before.EffectiveFromUtc);
        first.LatestEndsAtUtc.ShouldBe(before.EffectiveToUtc.HasValue && before.EffectiveToUtc.Value < before.EndsAtUtc
            ? before.EffectiveToUtc.Value : before.EndsAtUtc);
        var second = await Assessments.GetDraftOptionsAsync(new() { Scope = input.Scope, SkipCount = first.NextPaperSkipCount, MaxResultCount = 1 });
        second.SavedPapers.Count.ShouldBe(1);
        second.HasMoreSavedPapers.ShouldBeFalse();
        first.SavedPapers.Concat(second.SavedPapers).Select(x => x.Id).Order()
            .ShouldBe(new[] { input.ExamPaperId, another.ExamPaperId }.Order());
        await WithUnitOfWorkAsync(async () =>
        {
            (await Repo<School>().GetAsync(f.SchoolId)).ConcurrencyStamp.ShouldBe(before.ConcurrencyStamp);
            (await Repo<TeacherTextbookSelection>().GetAsync(f.ReaderSelection)).ConcurrencyStamp.ShouldBe(before.selectionStamp);
            (await Repo<EducationAssessmentTask>().GetListAsync()).ShouldBeEmpty();
        });
        using (User(f.Owner))
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetDraftOptionsAsync(new() { Scope = input.Scope }));
        using (User(student.UserId))
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetDraftOptionsAsync(new() { Scope = input.Scope }));
        using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetDraftOptionsAsync(new() { Scope = input.Scope }));
    }

    [Fact]
    public async Task Assessment_Draft_Options_Should_Check_Their_Functional_Permission_Before_Reading_The_Class()
    {
        var checker = Substitute.For<IPermissionChecker>();
        checker.IsGrantedAsync(EducationPermissions.AssessmentManage).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>();
        lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>());
        var denied = new EducationAssessmentAppService { LazyServiceProvider = lazy };
        await Should.ThrowAsync<AbpAuthorizationException>(() => denied.GetDraftOptionsAsync(new()));
        await checker.Received(1).IsGrantedAsync(EducationPermissions.AssessmentManage);
    }
}
