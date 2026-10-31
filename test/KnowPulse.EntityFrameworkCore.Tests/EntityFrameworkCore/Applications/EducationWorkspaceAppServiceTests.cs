using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationSchoolAppServiceTests
{
    [Fact]
    public async Task Workspace_Should_Derive_Both_Roles_And_Only_The_Accounts_Own_Classes_From_Recorded_Relations()
    {
        var f = await CreateFixtureAsync();
        Guid ownAssignment;
        Guid enrollment;
        Guid otherAssignment;
        using (ChangeUser(f.Manager))
        {
            ownAssignment = (await _service.AssignTeacherAsync(f.School.Id, Assignment(f, f.Teacher))).Id;
            var english = await _service.CreateSubjectAsync(f.School.Id, new() { Code = "ENG", Name = "英语" });
            var other = Assignment(f, f.SecondTeacher); other.EducationSubjectId = english.Id;
            otherAssignment = (await _service.AssignTeacherAsync(f.School.Id, other)).Id;
            await _service.AddMembershipAsync(f.School.Id, new() { UserId = f.Teacher, Role = SchoolMemberRole.Student, EffectiveFromUtc = f.Term.StartsAtUtc });
            var input = Enrollment(f, f.Class.Id); input.StudentUserId = f.Teacher;
            enrollment = (await _service.EnrollStudentAsync(f.School.Id, input)).Id;
        }
        using var actor = _principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(
            [new(AbpClaimTypes.UserId, f.Teacher.ToString())], "Test")));
        var context = await _service.GetMyWorkspaceAsync();
        context.AccountUserId.ShouldBe(f.Teacher); context.TenantId.ShouldBeNull(); context.ManagedSchoolIds.ShouldBeEmpty();
        context.Workspaces.Count.ShouldBe(2);
        var teacher = context.Workspaces.Single(x => x.Role == SchoolMemberRole.Teacher);
        teacher.SchoolId.ShouldBe(f.School.Id); teacher.Classes.Single().RelationId.ShouldBe(ownAssignment);
        teacher.Classes.Single().EducationSubjectId.ShouldBe(f.Subject.Id);
        teacher.Classes.Single().SubjectName.ShouldBe("数学");
        var student = context.Workspaces.Single(x => x.Role == SchoolMemberRole.Student);
        student.Classes.Single().RelationId.ShouldBe(enrollment); student.Classes.Single().EducationSubjectId.ShouldBeNull();
        var json = JsonSerializer.Serialize(context);
        foreach (var hidden in new[] { f.SecondTeacher.ToString(), f.Student.ToString(), otherAssignment.ToString(), "英语", "\"ObtainedScore\":", "\"Response\":", "\"Feedback\":" })
            json.ShouldNotContain(hidden);
    }

    [Fact]
    public async Task Workspace_Should_Keep_Each_School_And_Role_Separate_For_One_Account()
    {
        var first = await CreateFixtureAsync(); var second = await CreateFixtureAsync();
        using (ChangeUser(first.Manager)) await _service.AssignTeacherAsync(first.School.Id, Assignment(first, first.Teacher));
        using (ChangeUser(second.Manager))
        {
            await _service.AddMembershipAsync(second.School.Id, new() { UserId = first.Teacher, Role = SchoolMemberRole.Student, EffectiveFromUtc = second.Term.StartsAtUtc });
            var input = Enrollment(second, second.Class.Id); input.StudentUserId = first.Teacher;
            await _service.EnrollStudentAsync(second.School.Id, input);
        }
        using var actor = ChangeUser(first.Teacher);
        var context = await _service.GetMyWorkspaceAsync(); context.Workspaces.Count.ShouldBe(2);
        context.Workspaces.Single(x => x.Role == SchoolMemberRole.Teacher).Classes.Single().SchoolClassId.ShouldBe(first.Class.Id);
        context.Workspaces.Single(x => x.Role == SchoolMemberRole.Student).Classes.Single().SchoolClassId.ShouldBe(second.Class.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Workspace_Should_Not_Grant_Teacher_Or_Student_Identity_From_Claims_Or_Manager_Membership(bool manager)
    {
        var f = await CreateFixtureAsync(); var account = manager ? f.Manager : await CreateUserAsync();
        using var actor = _principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(
            [new(AbpClaimTypes.UserId, account.ToString()), new(AbpClaimTypes.Role, "admin"),
                new(AbpClaimTypes.Role, "teacher"), new(AbpClaimTypes.Role, "student")], "Test")));
        var context = await _service.GetMyWorkspaceAsync(); context.Workspaces.ShouldBeEmpty();
        context.ManagedSchoolIds.ShouldBe(manager ? new[] { f.School.Id } : []);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Workspace_Should_Remove_Ended_Assignments_Or_Enrollments_On_The_Next_Read(bool teacher)
    {
        var f = await CreateFixtureAsync(); Guid relation;
        using (ChangeUser(f.Manager)) relation = teacher
            ? (await _service.AssignTeacherAsync(f.School.Id, Assignment(f, f.Teacher))).Id
            : (await _service.EnrollStudentAsync(f.School.Id, Enrollment(f, f.Class.Id))).Id;
        var account = teacher ? f.Teacher : f.Student;
        using (ChangeUser(account)) (await _service.GetMyWorkspaceAsync()).Workspaces.Single().Classes.Count.ShouldBe(1);
        using (ChangeUser(f.Manager))
        {
            var end = new EndEducationRelationDto { EffectiveToUtc = DateTime.UtcNow.AddSeconds(-1) };
            if (teacher) await _service.EndTeachingAssignmentAsync(f.School.Id, relation, end);
            else await _service.EndStudentEnrollmentAsync(f.School.Id, relation, end);
        }
        using (ChangeUser(account)) (await _service.GetMyWorkspaceAsync()).Workspaces.Single().Classes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Workspace_Should_Remove_Ended_School_Memberships_Without_Trusting_Role_Claims(bool teacher)
    {
        var f = await CreateFixtureAsync();
        using (ChangeUser(f.Manager))
        {
            var role = teacher ? SchoolMemberRole.Teacher : SchoolMemberRole.Student;
            var account = teacher ? f.Teacher : f.Student;
            var membership = (await _service.GetMembershipsAsync(f.School.Id, new() { UserId = account, Role = role })).Items.Single();
            await _service.EndMembershipAsync(f.School.Id, membership.Id, new() { EffectiveToUtc = DateTime.UtcNow.AddSeconds(-1) });
        }
        using var actor = ChangeUser(teacher ? f.Teacher : f.Student);
        (await _service.GetMyWorkspaceAsync()).Workspaces.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Workspace_Should_Exclude_Past_And_Future_Terms_Even_When_The_Membership_Is_Current(bool future)
    {
        var f = await CreateFixtureAsync();
        using (ChangeUser(f.Manager))
        {
            var now = DateTime.UtcNow;
            var term = await _service.CreateAcademicTermAsync(f.School.Id, new() { Code = future ? "NEXT" : "PAST", Name = "非当前学期",
                StartsAtUtc = now.AddDays(future ? 5 : -9), EndsAtUtc = now.AddDays(future ? 10 : -5) });
            var schoolClass = await _service.CreateClassAsync(f.School.Id, new() { AcademicTermId = term.Id, Code = "OTHER", Name = "非当前班级", Grade = 8 });
            var assignment = Assignment(f, f.Teacher); assignment.AcademicTermId = term.Id; assignment.SchoolClassId = schoolClass.Id;
            assignment.EffectiveFromUtc = term.StartsAtUtc; assignment.EffectiveToUtc = term.EndsAtUtc;
            await _service.AssignTeacherAsync(f.School.Id, assignment);
            var enrollment = Enrollment(f, schoolClass.Id); enrollment.EffectiveFromUtc = term.StartsAtUtc; enrollment.EffectiveToUtc = term.EndsAtUtc;
            await _service.EnrollStudentAsync(f.School.Id, enrollment);
        }
        foreach (var account in new[] { f.Teacher, f.Student })
        {
            using var actor = ChangeUser(account);
            (await _service.GetMyWorkspaceAsync()).Workspaces.Single().Classes.ShouldBeEmpty();
        }
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("unknown")]
    [InlineData("anonymous")]
    [InlineData("tenant")]
    public async Task Workspace_Should_Require_The_Current_Active_Account_In_The_Current_Tenant(string invalid)
    {
        var f = await CreateFixtureAsync();
        if (invalid == "inactive") await WithUnitOfWorkAsync(async () => { var account = await _users.GetAsync(f.Teacher); account.SetIsActive(false); await _users.UpdateAsync(account, true); });
        if (invalid == "deleted") await WithUnitOfWorkAsync(() => _users.DeleteAsync(f.Teacher, true));
        using var actor = invalid == "anonymous" ? _principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity())) : ChangeUser(invalid == "unknown" ? Guid.NewGuid() : f.Teacher);
        using var tenant = GetRequiredService<ICurrentTenant>().Change(invalid == "tenant" ? Guid.NewGuid() : null);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetMyWorkspaceAsync());
    }

    [Fact]
    public async Task Workspace_Should_Report_Function_Permissions_Separately_From_The_Recorded_Role()
    {
        var f = await CreateFixtureAsync();
        using (ChangeUser(f.Manager)) await _service.AssignTeacherAsync(f.School.Id, Assignment(f, f.Teacher));
        using var actor = ChangeUser(f.Teacher);
        var context = await WithUnitOfWorkAsync(() => CreateServiceWithPermissions(false, false).GetMyWorkspaceAsync());
        context.Workspaces.Single().Classes.Count.ShouldBe(1); context.GrantedPermissions.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("school")]
    [InlineData("term")]
    [InlineData("class")]
    [InlineData("subject")]
    public async Task Workspace_Should_Not_Expose_Deleted_School_Or_Class_Dimensions(string deleted)
    {
        var f = await CreateFixtureAsync();
        using (ChangeUser(f.Manager))
        {
            await _service.AssignTeacherAsync(f.School.Id, Assignment(f, f.Teacher));
            await _service.EnrollStudentAsync(f.School.Id, Enrollment(f, f.Class.Id));
        }
        await WithUnitOfWorkAsync(async () =>
        {
            if (deleted == "school") await _schools.DeleteAsync(f.School.Id, true);
            if (deleted == "term") await _terms.DeleteAsync(f.Term.Id, true);
            if (deleted == "class") await _classes.DeleteAsync(f.Class.Id, true);
            if (deleted == "subject") await _subjects.DeleteAsync(f.Subject.Id, true);
        });
        using (ChangeUser(f.Teacher))
        {
            var context = await _service.GetMyWorkspaceAsync();
            context.Workspaces.SelectMany(x => x.Classes).ShouldBeEmpty();
            if (deleted == "school") context.Workspaces.ShouldBeEmpty();
        }
        using (ChangeUser(f.Student))
        {
            var context = await _service.GetMyWorkspaceAsync();
            context.Workspaces.SelectMany(x => x.Classes).Count().ShouldBe(deleted == "subject" ? 1 : 0);
        }
    }

    [Fact]
    public async Task Workspace_Should_Use_The_Verified_Tenant_Account_Without_Including_Host_Schools()
    {
        var host = await CreateFixtureAsync(); var tenantId = Guid.NewGuid();
        using var tenant = GetRequiredService<ICurrentTenant>().Change(tenantId);
        var manager = await CreateUserAsync(tenantId); var teacher = await CreateUserAsync(tenantId); SchoolDto school;
        using (ChangeUser(manager))
        {
            school = await _service.CreateSchoolAsync(new() { Code = "TENANT", Name = "租户学校" });
            await _service.AddMembershipAsync(school.Id, new() { UserId = teacher, Role = SchoolMemberRole.Teacher, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1) });
        }
        using var actor = ChangeUser(teacher);
        var context = await _service.GetMyWorkspaceAsync();
        context.AccountUserId.ShouldBe(teacher); context.TenantId.ShouldBe(tenantId);
        context.Workspaces.Single().SchoolId.ShouldBe(school.Id);
        context.Workspaces.ShouldNotContain(x => x.SchoolId == host.School.Id);
    }

    [Fact]
    public async Task Workspace_Should_Be_Read_Only_And_Not_Provision_Libraries_Or_Advance_Authorization_Barriers()
    {
        var f = await CreateFixtureAsync();
        using (ChangeUser(f.Manager)) await _service.AssignTeacherAsync(f.School.Id, Assignment(f, f.Teacher));
        var schoolBefore = await WithUnitOfWorkAsync(() => _schools.GetAsync(f.School.Id));
        var userBefore = await WithUnitOfWorkAsync(() => _users.GetAsync(f.Teacher));
        var libraries = GetRequiredService<IRepository<KnowledgeLibrary, Guid>>(); var count = await WithUnitOfWorkAsync(() => libraries.CountAsync());
        using (ChangeUser(f.Teacher)) { await _service.GetMyWorkspaceAsync(); await _service.GetMyWorkspaceAsync(); }
        var schoolAfter = await WithUnitOfWorkAsync(() => _schools.GetAsync(f.School.Id));
        schoolAfter.ConcurrencyStamp.ShouldBe(schoolBefore.ConcurrencyStamp); schoolAfter.ConfigurationRevision.ShouldBe(schoolBefore.ConfigurationRevision);
        (await WithUnitOfWorkAsync(() => _users.GetAsync(f.Teacher))).ConcurrencyStamp.ShouldBe(userBefore.ConcurrencyStamp);
        (await WithUnitOfWorkAsync(() => libraries.CountAsync())).ShouldBe(count);
    }
}
