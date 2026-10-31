using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using KnowPulse.Education;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Users;
using Volo.Abp.Validation;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public partial class EducationSchoolAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IEducationSchoolAppService _service;
    private readonly IRepository<School, Guid> _schools;
    private readonly IRepository<SchoolMembership, Guid> _memberships;
    private readonly IRepository<AcademicTerm, Guid> _terms;
    private readonly IRepository<SchoolClass, Guid> _classes;
    private readonly IRepository<EducationSubject, Guid> _subjects;
    private readonly IRepository<TeachingAssignment, Guid> _assignments;
    private readonly IRepository<StudentEnrollment, Guid> _enrollments;
    private readonly IRepository<IdentityUser, Guid> _users;
    private readonly ICurrentPrincipalAccessor _principalAccessor;

    public EducationSchoolAppServiceTests()
    {
        _service = GetRequiredService<IEducationSchoolAppService>();
        _schools = GetRequiredService<IRepository<School, Guid>>();
        _memberships = GetRequiredService<IRepository<SchoolMembership, Guid>>();
        _terms = GetRequiredService<IRepository<AcademicTerm, Guid>>();
        _classes = GetRequiredService<IRepository<SchoolClass, Guid>>();
        _subjects = GetRequiredService<IRepository<EducationSubject, Guid>>();
        _assignments = GetRequiredService<IRepository<TeachingAssignment, Guid>>();
        _enrollments = GetRequiredService<IRepository<StudentEnrollment, Guid>>();
        _users = GetRequiredService<IRepository<IdentityUser, Guid>>();
        _principalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    [Fact]
    public async Task School_Creation_Should_Require_Separate_Platform_Permission_And_Create_Real_Manager_Membership()
    {
        var manager = await CreateUserAsync();
        using var user = ChangeUser(manager);
        var manageOnly = CreateServiceWithPermissions(createGranted: false, manageGranted: true);
        await Should.ThrowAsync<AbpAuthorizationException>(() => manageOnly.CreateSchoolAsync(new CreateSchoolDto { Code = "DENIED", Name = "未授权学校" }));
        (await WithUnitOfWorkAsync(() => _schools.CountAsync())).ShouldBe(0);

        var createOnly = CreateServiceWithPermissions(createGranted: true, manageGranted: false);
        await WithUnitOfWorkAsync(async () =>
        {
            var school = await createOnly.CreateSchoolAsync(new CreateSchoolDto { Code = "school-a", Name = "真实身份学校" });
            school.Code.ShouldBe("SCHOOL-A");
            var mine = (await createOnly.GetMySchoolsAsync()).Items.Single();
            mine.Id.ShouldBe(school.Id);
            mine.Roles.ShouldBe([SchoolMemberRole.Manager]);
            var membership = await _memberships.GetAsync(x => x.SchoolId == school.Id && x.UserId == manager);
            membership.IsEffectiveAt(DateTime.UtcNow).ShouldBeTrue();
            membership.EffectiveFromUtc.Kind.ShouldBe(DateTimeKind.Utc);
            await Should.ThrowAsync<AbpAuthorizationException>(() => createOnly.CreateAcademicTermAsync(school.Id, new CreateAcademicTermDto
            {
                Code = "T1", Name = "学期", StartsAtUtc = DateTime.UtcNow, EndsAtUtc = DateTime.UtcNow.AddDays(10)
            }));
        });
    }

    [Fact]
    public async Task Generic_Function_Permissions_And_Admin_Role_Should_Not_Allow_Another_Schools_Management()
    {
        var first = await CreateFixtureAsync();
        var second = await CreateFixtureAsync();
        var revision = (await WithUnitOfWorkAsync(() => _schools.GetAsync(first.School.Id))).ConfigurationRevision;
        using (ChangeUser(second.Manager))
        {
            (await _service.GetMySchoolsAsync()).Items.Select(x => x.Id).ShouldNotContain(first.School.Id);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetSchoolConfigurationAsync(first.School.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetMembershipsAsync(first.School.Id, new GetSchoolMembershipListInput()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetTeachingAssignmentsAsync(first.School.Id, new GetSchoolRelationListInput()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetStudentEnrollmentsAsync(first.School.Id, new GetSchoolRelationListInput()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.RenameSchoolAsync(first.School.Id, new RenameSchoolDto { Name = "非法改名" }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.AddMembershipAsync(first.School.Id, new AddSchoolMembershipDto
            {
                UserId = second.Manager, Role = SchoolMemberRole.Manager, EffectiveFromUtc = DateTime.UtcNow
            }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CreateSubjectAsync(first.School.Id, new CreateEducationSubjectDto { Code = "BAD", Name = "非法学科" }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.AssignTeacherAsync(first.School.Id, Assignment(first, first.Teacher)));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.EnrollStudentAsync(first.School.Id, Enrollment(first, first.Class.Id)));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.EndMembershipAsync(first.School.Id, first.StudentMembership.Id,
                new EndEducationRelationDto { EffectiveToUtc = DateTime.UtcNow }));
        }
        (await WithUnitOfWorkAsync(() => _schools.GetAsync(first.School.Id))).ConfigurationRevision.ShouldBe(revision);
        (await WithUnitOfWorkAsync(() => _assignments.CountAsync())).ShouldBe(0);
        (await WithUnitOfWorkAsync(() => _enrollments.CountAsync())).ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Future_Or_Expired_Manager_Membership_Should_Not_Grant_Current_Access(bool future)
    {
        var manager = await CreateUserAsync();
        var now = DateTime.UtcNow;
        var school = new School(Guid.NewGuid(), null, $"S{Guid.NewGuid():N}", "无当前有效管理员的学校");
        await WithUnitOfWorkAsync(async () =>
        {
            await _schools.InsertAsync(school, autoSave: true);
            await _memberships.InsertAsync(new SchoolMembership(Guid.NewGuid(), null, school.Id, manager,
                SchoolMemberRole.Manager, now.AddDays(future ? 1 : -2), now.AddDays(future ? 2 : -1)), autoSave: true);
        });
        using var user = ChangeUser(manager);
        (await _service.GetMySchoolsAsync()).Items.ShouldBeEmpty();
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetSchoolConfigurationAsync(school.Id));
    }

    [Fact]
    public async Task Teacher_Membership_Should_Expose_Own_School_Without_Granting_Manager_Access()
    {
        var fixture = await CreateFixtureAsync();
        using var user = ChangeUser(fixture.Teacher);
        var school = (await _service.GetMySchoolsAsync()).Items.Single();
        school.Id.ShouldBe(fixture.School.Id);
        school.Roles.ShouldBe([SchoolMemberRole.Teacher]);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetSchoolConfigurationAsync(fixture.School.Id));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.EnrollStudentAsync(fixture.School.Id, Enrollment(fixture, fixture.Class.Id)));
    }

    [Fact]
    public async Task Invalid_Cross_School_Class_And_Term_References_Should_Fail_Before_Writes()
    {
        var first = await CreateFixtureAsync();
        var second = await CreateFixtureAsync();
        using var user = ChangeUser(first.Manager);
        var revision = (await _service.GetSchoolConfigurationAsync(first.School.Id)).School.ConfigurationRevision;
        var wrongAssignment = Assignment(first, first.Teacher);
        wrongAssignment.SchoolClassId = second.Class.Id;
        (await Should.ThrowAsync<BusinessException>(() => _service.AssignTeacherAsync(first.School.Id, wrongAssignment)))
            .Code.ShouldBe(EducationErrorCodes.ReferenceScopeMismatch);
        (await Should.ThrowAsync<BusinessException>(() => _service.EnrollStudentAsync(first.School.Id, Enrollment(first, second.Class.Id))))
            .Code.ShouldBe(EducationErrorCodes.ReferenceScopeMismatch);
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateClassAsync(first.School.Id, new CreateSchoolClassDto
        {
            AcademicTermId = second.Term.Id, Code = "WRONG", Name = "跨校班级", Grade = 7
        }))).Code.ShouldBe(EducationErrorCodes.ReferenceScopeMismatch);
        (await _service.GetSchoolConfigurationAsync(first.School.Id)).School.ConfigurationRevision.ShouldBe(revision);
        (await _service.GetTeachingAssignmentsAsync(first.School.Id, new GetSchoolRelationListInput())).TotalCount.ShouldBe(0);
        (await _service.GetStudentEnrollmentsAsync(first.School.Id, new GetSchoolRelationListInput())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Enrollment_Should_Deduplicate_Reject_Overlap_And_Preserve_Transfer_History()
    {
        var fixture = await CreateFixtureAsync();
        using var user = ChangeUser(fixture.Manager);
        var secondClass = await _service.CreateClassAsync(fixture.School.Id, new CreateSchoolClassDto
        {
            AcademicTermId = fixture.Term.Id, Code = "C2", Name = "七年级二班", Grade = 7
        });
        var initialInput = Enrollment(fixture, fixture.Class.Id);
        var first = await _service.EnrollStudentAsync(fixture.School.Id, initialInput);
        (await _service.EnrollStudentAsync(fixture.School.Id, initialInput)).Id.ShouldBe(first.Id);
        (await Should.ThrowAsync<BusinessException>(() => _service.EnrollStudentAsync(fixture.School.Id, Enrollment(fixture, secondClass.Id))))
            .Code.ShouldBe(EducationErrorCodes.StudentEnrollmentOverlap);
        var boundary = DateTime.UtcNow.AddMinutes(1);
        await _service.EndStudentEnrollmentAsync(fixture.School.Id, first.Id, new EndEducationRelationDto { EffectiveToUtc = boundary });
        var transfer = Enrollment(fixture, secondClass.Id);
        transfer.EffectiveFromUtc = boundary;
        var next = await _service.EnrollStudentAsync(fixture.School.Id, transfer);
        next.Id.ShouldNotBe(first.Id);
        var history = await _service.GetStudentEnrollmentsAsync(fixture.School.Id, new GetSchoolRelationListInput { UserId = fixture.Student });
        history.TotalCount.ShouldBe(2);
        var old = history.Items.Single(x => x.Id == first.Id);
        old.SchoolClassId.ShouldBe(fixture.Class.Id);
        old.EffectiveFromUtc.ShouldBe(initialInput.EffectiveFromUtc);
        old.EffectiveToUtc.ShouldBe(boundary);
        old.EffectiveToUtc!.Value.Kind.ShouldBe(DateTimeKind.Utc);
        (await _service.GetStudentEnrollmentsAsync(fixture.School.Id, new GetSchoolRelationListInput
        {
            UserId = fixture.Student, EffectiveAtUtc = boundary
        })).Items.Single().Id.ShouldBe(next.Id);
        (await _service.GetStudentEnrollmentsAsync(fixture.School.Id, new GetSchoolRelationListInput
        {
            UserId = fixture.Student, EffectiveAtUtc = boundary.AddTicks(-1)
        })).Items.Single().Id.ShouldBe(first.Id);
    }

    [Fact]
    public async Task Co_Teachers_Should_Be_Allowed_While_Same_Teacher_Overlap_Is_Rejected_And_History_Preserved()
    {
        var fixture = await CreateFixtureAsync();
        using var user = ChangeUser(fixture.Manager);
        var input = Assignment(fixture, fixture.Teacher);
        var first = await _service.AssignTeacherAsync(fixture.School.Id, input);
        (await _service.AssignTeacherAsync(fixture.School.Id, input)).Id.ShouldBe(first.Id);
        var other = await _service.AssignTeacherAsync(fixture.School.Id, Assignment(fixture, fixture.SecondTeacher));
        other.TeacherUserId.ShouldBe(fixture.SecondTeacher);
        var overlap = Assignment(fixture, fixture.Teacher);
        overlap.EffectiveFromUtc = overlap.EffectiveFromUtc.AddDays(1);
        (await Should.ThrowAsync<BusinessException>(() => _service.AssignTeacherAsync(fixture.School.Id, overlap)))
            .Code.ShouldBe(EducationErrorCodes.TeachingAssignmentOverlap);
        var boundary = DateTime.UtcNow.AddMinutes(1);
        await _service.EndTeachingAssignmentAsync(fixture.School.Id, first.Id, new EndEducationRelationDto { EffectiveToUtc = boundary });
        var newPeriod = Assignment(fixture, fixture.Teacher);
        newPeriod.EffectiveFromUtc = boundary;
        await _service.AssignTeacherAsync(fixture.School.Id, newPeriod);
        var history = await _service.GetTeachingAssignmentsAsync(fixture.School.Id, new GetSchoolRelationListInput { MaxResultCount = 100 });
        history.TotalCount.ShouldBe(3);
        var old = history.Items.Single(x => x.Id == first.Id);
        old.TeacherUserId.ShouldBe(fixture.Teacher);
        old.EducationSubjectId.ShouldBe(fixture.Subject.Id);
        old.AcademicTermId.ShouldBe(fixture.Term.Id);
        old.EffectiveFromUtc.ShouldBe(input.EffectiveFromUtc);
        old.EffectiveToUtc.ShouldBe(boundary);
    }

    [Fact]
    public async Task Role_And_Entire_Period_Coverage_Should_Be_Required_For_Teaching_And_Enrollment()
    {
        var fixture = await CreateFixtureAsync();
        using var user = ChangeUser(fixture.Manager);
        (await Should.ThrowAsync<BusinessException>(() => _service.AssignTeacherAsync(fixture.School.Id, Assignment(fixture, fixture.Student))))
            .Code.ShouldBe(EducationErrorCodes.MembershipDoesNotCoverPeriod);
        var studentInput = Enrollment(fixture, fixture.Class.Id);
        studentInput.StudentUserId = fixture.Teacher;
        (await Should.ThrowAsync<BusinessException>(() => _service.EnrollStudentAsync(fixture.School.Id, studentInput)))
            .Code.ShouldBe(EducationErrorCodes.MembershipDoesNotCoverPeriod);
        var beyondTerm = Assignment(fixture, fixture.Teacher);
        beyondTerm.EffectiveToUtc = fixture.Term.EndsAtUtc.AddTicks(1);
        (await Should.ThrowAsync<BusinessException>(() => _service.AssignTeacherAsync(fixture.School.Id, beyondTerm)))
            .Code.ShouldBe(EducationErrorCodes.PeriodOutsideTerm);
        var partialTeacher = await CreateUserAsync();
        await _service.AddMembershipAsync(fixture.School.Id, new AddSchoolMembershipDto
        {
            UserId = partialTeacher, Role = SchoolMemberRole.Teacher,
            EffectiveFromUtc = fixture.Term.StartsAtUtc, EffectiveToUtc = fixture.Term.StartsAtUtc.AddDays(2)
        });
        (await Should.ThrowAsync<BusinessException>(() => _service.AssignTeacherAsync(fixture.School.Id, Assignment(fixture, partialTeacher))))
            .Code.ShouldBe(EducationErrorCodes.MembershipDoesNotCoverPeriod);
        (await _service.GetTeachingAssignmentsAsync(fixture.School.Id, new GetSchoolRelationListInput())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Membership_End_Should_Require_Dependent_Relations_To_End_And_Retain_History()
    {
        var fixture = await CreateFixtureAsync();
        using var user = ChangeUser(fixture.Manager);
        var enrollment = await _service.EnrollStudentAsync(fixture.School.Id, Enrollment(fixture, fixture.Class.Id));
        var boundary = DateTime.UtcNow.AddMinutes(1);
        var end = new EndEducationRelationDto { EffectiveToUtc = boundary };
        (await Should.ThrowAsync<BusinessException>(() => _service.EndMembershipAsync(fixture.School.Id, fixture.StudentMembership.Id, end)))
            .Code.ShouldBe(EducationErrorCodes.MembershipHasDependentRelations);
        (await WithUnitOfWorkAsync(() => _memberships.GetAsync(fixture.StudentMembership.Id))).EffectiveToUtc.ShouldBeNull();
        await _service.EndStudentEnrollmentAsync(fixture.School.Id, enrollment.Id, end);
        var ended = await _service.EndMembershipAsync(fixture.School.Id, fixture.StudentMembership.Id, end);
        ended.EffectiveToUtc.ShouldBe(boundary);
        var history = await _service.GetMembershipsAsync(fixture.School.Id, new GetSchoolMembershipListInput { UserId = fixture.Student });
        history.Items.Single().Id.ShouldBe(fixture.StudentMembership.Id);
        (await _service.GetMembershipsAsync(fixture.School.Id, new GetSchoolMembershipListInput
        {
            UserId = fixture.Student, EffectiveAtUtc = boundary
        })).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Last_Manager_End_Should_Not_Lock_Out_The_School()
    {
        var fixture = await CreateFixtureAsync();
        using var user = ChangeUser(fixture.Manager);
        var manager = (await _service.GetMembershipsAsync(fixture.School.Id, new GetSchoolMembershipListInput
        {
            UserId = fixture.Manager, Role = SchoolMemberRole.Manager
        })).Items.Single();
        (await Should.ThrowAsync<BusinessException>(() => _service.EndMembershipAsync(fixture.School.Id, manager.Id,
            new EndEducationRelationDto { EffectiveToUtc = DateTime.UtcNow.AddDays(1) }))).Code.ShouldBe(EducationErrorCodes.LastManagerRequired);
        (await WithUnitOfWorkAsync(() => _memberships.GetAsync(manager.Id))).EffectiveToUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Explicit_Tenant_Checks_Should_Reject_Foreign_Users_Subjects_And_Malformed_Memberships()
    {
        var fixture = await CreateFixtureAsync();
        var foreignTenant = Guid.NewGuid();
        var foreignUser = await CreateUserAsync(foreignTenant);
        var teacherWithoutValidMembership = await CreateUserAsync();
        var foreignSubject = new EducationSubject(Guid.NewGuid(), foreignTenant, "FOREIGN", "外租户学科");
        await WithUnitOfWorkAsync(async () =>
        {
            await _subjects.InsertAsync(foreignSubject, autoSave: true);
            await _memberships.InsertAsync(new SchoolMembership(Guid.NewGuid(), foreignTenant, fixture.School.Id,
                teacherWithoutValidMembership, SchoolMemberRole.Teacher, fixture.Term.StartsAtUtc), autoSave: true);
        });
        using var user = ChangeUser(fixture.Manager);
        using var disabledFilter = GetRequiredService<IDataFilter>().Disable<IMultiTenant>();
        (await Should.ThrowAsync<BusinessException>(() => _service.AddMembershipAsync(fixture.School.Id, new AddSchoolMembershipDto
        {
            UserId = foreignUser, Role = SchoolMemberRole.Student, EffectiveFromUtc = fixture.Term.StartsAtUtc
        }))).Code.ShouldBe(EducationErrorCodes.ReferenceScopeMismatch);
        var wrongSubject = Assignment(fixture, fixture.Teacher);
        wrongSubject.EducationSubjectId = foreignSubject.Id;
        (await Should.ThrowAsync<BusinessException>(() => _service.AssignTeacherAsync(fixture.School.Id, wrongSubject)))
            .Code.ShouldBe(EducationErrorCodes.ReferenceScopeMismatch);
        (await Should.ThrowAsync<BusinessException>(() => _service.AssignTeacherAsync(fixture.School.Id,
            Assignment(fixture, teacherWithoutValidMembership)))).Code.ShouldBe(EducationErrorCodes.MembershipDoesNotCoverPeriod);
        (await _service.GetTeachingAssignmentsAsync(fixture.School.Id, new GetSchoolRelationListInput())).TotalCount.ShouldBe(0);
        (await _service.GetMembershipsAsync(fixture.School.Id, new GetSchoolMembershipListInput
        {
            UserId = teacherWithoutValidMembership
        })).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Account_Search_Should_Require_School_Manager_And_Function_Permission()
    {
        var fixture = await CreateFixtureAsync();
        var input = new GetSchoolAccountListInput { Keyword = "edu", MaxResultCount = 20 };
        using (ChangeUser(fixture.Teacher))
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetAccountsAsync(fixture.School.Id, input));
        var outsider = await CreateUserAsync();
        using (ChangeUser(outsider))
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetAccountsAsync(fixture.School.Id, input));
        using (ChangeUser(fixture.Manager))
        {
            var denied = CreateServiceWithPermissions(createGranted: true, manageGranted: false);
            await Should.ThrowAsync<AbpAuthorizationException>(() => denied.GetAccountsAsync(fixture.School.Id, input));
        }
    }

    [Fact]
    public async Task Account_Search_Should_Bound_Inputs_And_Return_Only_Active_Same_Tenant_Accounts()
    {
        var fixture = await CreateFixtureAsync();
        var foreign = await CreateUserAsync(Guid.NewGuid());
        var inactive = await CreateUserAsync();
        await WithUnitOfWorkAsync(async () =>
        {
            var user = await _users.GetAsync(fixture.Teacher);
            user.Name = "筛选教师";
            user.Surname = "甲";
            await _users.UpdateAsync(user, autoSave: true);
            var inactiveUser = await _users.GetAsync(inactive);
            inactiveUser.SetIsActive(false);
            await _users.UpdateAsync(inactiveUser, autoSave: true);
        });
        using var manager = ChangeUser(fixture.Manager);
        using var disabledFilter = GetRequiredService<IDataFilter>().Disable<IMultiTenant>();
        var all = await _service.GetAccountsAsync(fixture.School.Id, new GetSchoolAccountListInput { Keyword = "EDU", MaxResultCount = 50 });
        all.Items.Select(x => x.Id).ShouldContain(fixture.Teacher);
        all.Items.Select(x => x.Id).ShouldNotContain(foreign);
        all.Items.Select(x => x.Id).ShouldNotContain(inactive);
        var byName = await _service.GetAccountsAsync(fixture.School.Id, new GetSchoolAccountListInput { Keyword = "筛选", MaxResultCount = 1 });
        byName.TotalCount.ShouldBe(1);
        byName.Items.Single().DisplayName.ShouldStartWith("筛选教师 甲 (edu-");
        var byRole = await _service.GetAccountsAsync(fixture.School.Id, new GetSchoolAccountListInput
        {
            Keyword = "edu", Role = SchoolMemberRole.Student, MaxResultCount = 20
        });
        byRole.Items.Select(x => x.Id).ShouldBe([fixture.Student]);
        await Should.ThrowAsync<AbpValidationException>(() => _service.GetAccountsAsync(fixture.School.Id,
            new GetSchoolAccountListInput { Keyword = "e", MaxResultCount = 20 }));
        await Should.ThrowAsync<AbpValidationException>(() => _service.GetAccountsAsync(fixture.School.Id,
            new GetSchoolAccountListInput { Keyword = "edu", MaxResultCount = 51 }));
        await Should.ThrowAsync<AbpValidationException>(() => _service.GetAccountsAsync(fixture.School.Id,
            new GetSchoolAccountListInput { Keyword = "edu", SkipCount = -1 }));
    }

    [Fact]
    public async Task Authorized_Roster_And_Relation_Lists_Should_Return_Recognizable_Account_Names()
    {
        var fixture = await CreateFixtureAsync();
        using var manager = ChangeUser(fixture.Manager);
        await _service.AssignTeacherAsync(fixture.School.Id, Assignment(fixture, fixture.Teacher));
        await _service.EnrollStudentAsync(fixture.School.Id, Enrollment(fixture, fixture.Class.Id));
        var members = await _service.GetMembershipsAsync(fixture.School.Id, new GetSchoolMembershipListInput { MaxResultCount = 20 });
        members.Items.Single(x => x.UserId == fixture.Teacher).UserDisplayName.ShouldBe($"edu-{fixture.Teacher:N}");
        var teachers = await _service.GetTeachingAssignmentsAsync(fixture.School.Id, new GetSchoolRelationListInput());
        teachers.Items.Single().TeacherDisplayName.ShouldBe($"edu-{fixture.Teacher:N}");
        var students = await _service.GetStudentEnrollmentsAsync(fixture.School.Id, new GetSchoolRelationListInput());
        students.Items.Single().StudentDisplayName.ShouldBe($"edu-{fixture.Student:N}");
    }

    [Fact]
    public async Task Member_Import_Should_Preview_Exact_Accounts_Deduplicate_And_Retry_Without_New_Relations()
    {
        var fixture = await CreateFixtureAsync();
        var newcomer = await CreateUserAsync();
        using var manager = ChangeUser(fixture.Manager);
        var input = new SchoolMemberImportInput
        {
            EffectiveFromUtc = fixture.Term.StartsAtUtc,
            Rows =
            [
                new() { UserName = $"EDU-{newcomer:N}", Role = SchoolMemberRole.Teacher },
                new() { UserName = $" edu-{newcomer:N} ", Role = SchoolMemberRole.Teacher },
                new() { UserName = $"edu-{fixture.Student:N}", Role = SchoolMemberRole.Student }
            ]
        };
        var before = (await _service.GetMembershipsAsync(fixture.School.Id, new GetSchoolMembershipListInput())).TotalCount;
        var preview = await _service.PreviewMemberImportAsync(fixture.School.Id, input);
        preview.Rows.Select(x => x.ErrorCode).ShouldAllBe(x => x == null);
        preview.Rows[0].UserId.ShouldBe(newcomer);
        preview.Rows[1].DuplicateInInput.ShouldBeTrue();
        preview.Rows[2].AlreadyExists.ShouldBeTrue();
        (await _service.GetMembershipsAsync(fixture.School.Id, new GetSchoolMembershipListInput())).TotalCount.ShouldBe(before);
        input.ConfigurationRevision = preview.ConfigurationRevision;
        foreach (var row in preview.Rows) input.Rows[row.RowNumber - 1].ConfirmedUserId = row.UserId;
        var imported = await _service.ImportMembersAsync(fixture.School.Id, input);
        imported.CreatedCount.ShouldBe(1);
        imported.ExistingCount.ShouldBe(1);
        var retry = await _service.ImportMembersAsync(fixture.School.Id, input);
        retry.CreatedCount.ShouldBe(0);
        retry.ExistingCount.ShouldBe(2);
        (await _service.GetMembershipsAsync(fixture.School.Id, new GetSchoolMembershipListInput())).TotalCount.ShouldBe(before + 1);
        (await _service.GetTeachingAssignmentsAsync(fixture.School.Id, new GetSchoolRelationListInput())).TotalCount.ShouldBe(0);
        (await _service.GetStudentEnrollmentsAsync(fixture.School.Id, new GetSchoolRelationListInput())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Member_Import_Should_Report_Invalid_Rows_And_Write_Nothing_Including_Valid_Rows()
    {
        var fixture = await CreateFixtureAsync();
        var valid = await CreateUserAsync();
        var foreign = await CreateUserAsync(Guid.NewGuid());
        var inactive = await CreateUserAsync();
        await WithUnitOfWorkAsync(async () =>
        {
            var account = await _users.GetAsync(inactive);
            account.SetIsActive(false);
            await _users.UpdateAsync(account, autoSave: true);
        });
        using var manager = ChangeUser(fixture.Manager);
        using var disabledFilter = GetRequiredService<IDataFilter>().Disable<IMultiTenant>();
        var input = new SchoolMemberImportInput
        {
            EffectiveFromUtc = fixture.Term.StartsAtUtc.AddDays(1),
            Rows =
            [
                new() { UserName = $"edu-{valid:N}", Role = SchoolMemberRole.Student },
                new() { UserName = $"edu-{foreign:N}", Role = SchoolMemberRole.Student },
                new() { UserName = $"edu-{inactive:N}", Role = SchoolMemberRole.Teacher },
                new() { UserName = $"edu-{fixture.Teacher:N}", Role = SchoolMemberRole.Teacher },
                new() { UserName = $"edu-{valid:N}", Role = SchoolMemberRole.Manager },
                new() { UserName = "unknown-exact-account", Role = SchoolMemberRole.Student }
            ]
        };
        var preview = await _service.PreviewMemberImportAsync(fixture.School.Id, input);
        preview.Rows[0].ErrorCode.ShouldBeNull();
        foreach (var row in new[] { preview.Rows[1], preview.Rows[2], preview.Rows[5] })
        {
            row.ErrorCode.ShouldBe(EducationErrorCodes.ImportAccountUnavailable);
            row.UserId.ShouldBeNull();
            row.UserDisplayName.ShouldBeEmpty();
        }
        preview.Rows[3].ErrorCode.ShouldBe(EducationErrorCodes.MembershipOverlap);
        preview.Rows[4].ErrorCode.ShouldBe(EducationErrorCodes.InvalidRole);
        (await Should.ThrowAsync<BusinessException>(() => _service.ImportMembersAsync(fixture.School.Id, input)))
            .Code.ShouldBe(EducationErrorCodes.ImportContainsErrors);
        (await _service.GetMembershipsAsync(fixture.School.Id, new GetSchoolMembershipListInput { UserId = valid })).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Member_Import_Should_Revalidate_Preview_Identity_Configuration_And_Active_Account()
    {
        var fixture = await CreateFixtureAsync();
        var newcomer = await CreateUserAsync();
        using var manager = ChangeUser(fixture.Manager);
        var input = new SchoolMemberImportInput
        {
            EffectiveFromUtc = fixture.Term.StartsAtUtc,
            Rows = [new() { UserName = $"edu-{newcomer:N}", Role = SchoolMemberRole.Teacher }]
        };
        var preview = await _service.PreviewMemberImportAsync(fixture.School.Id, input);
        input.ConfigurationRevision = preview.ConfigurationRevision;
        (await Should.ThrowAsync<BusinessException>(() => _service.ImportMembersAsync(fixture.School.Id, input)))
            .Code.ShouldBe(EducationErrorCodes.ImportConfirmationChanged);
        input.Rows[0].ConfirmedUserId = fixture.Teacher;
        (await Should.ThrowAsync<BusinessException>(() => _service.ImportMembersAsync(fixture.School.Id, input)))
            .Code.ShouldBe(EducationErrorCodes.ImportConfirmationChanged);
        input.Rows[0].ConfirmedUserId = newcomer;
        await _service.RenameSchoolAsync(fixture.School.Id, new RenameSchoolDto { Name = "已变更学校名称" });
        (await Should.ThrowAsync<BusinessException>(() => _service.ImportMembersAsync(fixture.School.Id, input)))
            .Code.ShouldBe(EducationErrorCodes.ImportConfirmationChanged);
        input.ConfigurationRevision = (await _service.PreviewMemberImportAsync(fixture.School.Id, input)).ConfigurationRevision;
        await WithUnitOfWorkAsync(async () =>
        {
            var account = await _users.GetAsync(newcomer);
            account.SetIsActive(false);
            await _users.UpdateAsync(account, autoSave: true);
        });
        (await Should.ThrowAsync<BusinessException>(() => _service.ImportMembersAsync(fixture.School.Id, input)))
            .Code.ShouldBe(EducationErrorCodes.ImportContainsErrors);
        (await _service.GetMembershipsAsync(fixture.School.Id, new GetSchoolMembershipListInput { UserId = newcomer })).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Member_Import_Should_Require_Manager_And_Function_Permission_And_Bound_Inputs()
    {
        var fixture = await CreateFixtureAsync();
        var input = new SchoolMemberImportInput
        {
            EffectiveFromUtc = fixture.Term.StartsAtUtc,
            Rows = [new() { UserName = $"edu-{fixture.Teacher:N}", Role = SchoolMemberRole.Teacher }]
        };
        using (ChangeUser(fixture.Teacher))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.PreviewMemberImportAsync(fixture.School.Id, input));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.ImportMembersAsync(fixture.School.Id, input));
        }
        using var manager = ChangeUser(fixture.Manager);
        var denied = CreateServiceWithPermissions(createGranted: true, manageGranted: false);
        await Should.ThrowAsync<AbpAuthorizationException>(() => denied.PreviewMemberImportAsync(fixture.School.Id, input));
        await Should.ThrowAsync<AbpAuthorizationException>(() => denied.ImportMembersAsync(fixture.School.Id, input));
        input.Rows.Clear();
        await Should.ThrowAsync<AbpValidationException>(() => _service.PreviewMemberImportAsync(fixture.School.Id, input));
        input.Rows = Enumerable.Range(0, 101).Select(_ => new SchoolMemberImportRowInput { UserName = "account", Role = SchoolMemberRole.Student }).ToList();
        await Should.ThrowAsync<AbpValidationException>(() => _service.ImportMembersAsync(fixture.School.Id, input));
        input.Rows = [new() { UserName = $"edu-{fixture.Teacher:N}", Role = SchoolMemberRole.Teacher }];
        input.EffectiveFromUtc = DateTime.SpecifyKind(fixture.Term.StartsAtUtc, DateTimeKind.Unspecified);
        (await Should.ThrowAsync<BusinessException>(() => _service.PreviewMemberImportAsync(fixture.School.Id, input)))
            .Code.ShouldBe(EducationErrorCodes.UtcRequired);
    }

    private async Task<Fixture> CreateFixtureAsync()
    {
        var manager = await CreateUserAsync();
        var teacher = await CreateUserAsync();
        var secondTeacher = await CreateUserAsync();
        var student = await CreateUserAsync();
        using var user = ChangeUser(manager);
        var school = await _service.CreateSchoolAsync(new CreateSchoolDto { Code = $"S{Guid.NewGuid():N}", Name = "测试初中" });
        var now = DateTime.UtcNow;
        var term = await _service.CreateAcademicTermAsync(school.Id, new CreateAcademicTermDto
        {
            Code = "T1", Name = "初中学期", StartsAtUtc = now.AddDays(-10), EndsAtUtc = now.AddDays(90)
        });
        var schoolClass = await _service.CreateClassAsync(school.Id, new CreateSchoolClassDto
        {
            AcademicTermId = term.Id, Code = "C1", Name = "七年级一班", Grade = 7
        });
        var subject = await _service.CreateSubjectAsync(school.Id, new CreateEducationSubjectDto { Code = "MATH", Name = "数学" });
        await _service.AddMembershipAsync(school.Id, new AddSchoolMembershipDto
        {
            UserId = teacher, Role = SchoolMemberRole.Teacher, EffectiveFromUtc = term.StartsAtUtc
        });
        await _service.AddMembershipAsync(school.Id, new AddSchoolMembershipDto
        {
            UserId = secondTeacher, Role = SchoolMemberRole.Teacher, EffectiveFromUtc = term.StartsAtUtc
        });
        var studentMembership = await _service.AddMembershipAsync(school.Id, new AddSchoolMembershipDto
        {
            UserId = student, Role = SchoolMemberRole.Student, EffectiveFromUtc = term.StartsAtUtc
        });
        return new Fixture(school, term, schoolClass, subject, manager, teacher, secondTeacher, student, studentMembership);
    }

    private Task<Guid> CreateUserAsync(Guid? tenantId = null)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var id = Guid.NewGuid();
            var user = new IdentityUser(id, $"edu-{id:N}", $"edu-{id:N}@example.invalid", tenantId);
            await _users.InsertAsync(user, autoSave: true);
            return id;
        });
    }

    private IDisposable ChangeUser(Guid userId)
    {
        // 测试模块放行所有功能权限；两个真实账号都带 admin，业务拒绝必须来自学校身份。
        return _principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(AbpClaimTypes.UserId, userId.ToString()), new Claim(AbpClaimTypes.UserName, $"edu-{userId:N}"),
            new Claim(AbpClaimTypes.Role, "admin")
        ], "Test")));
    }

    private static AssignTeacherDto Assignment(Fixture fixture, Guid teacher) => new()
    {
        AcademicTermId = fixture.Term.Id, SchoolClassId = fixture.Class.Id, EducationSubjectId = fixture.Subject.Id,
        TeacherUserId = teacher, EffectiveFromUtc = fixture.Term.StartsAtUtc, EffectiveToUtc = fixture.Term.EndsAtUtc
    };

    private static EnrollStudentDto Enrollment(Fixture fixture, Guid classId) => new()
    {
        SchoolClassId = classId, StudentUserId = fixture.Student,
        EffectiveFromUtc = fixture.Term.StartsAtUtc, EffectiveToUtc = fixture.Term.EndsAtUtc
    };

    private EducationSchoolAppService CreateServiceWithPermissions(bool createGranted, bool manageGranted)
    {
        var checker = Substitute.For<IPermissionChecker>();
        checker.IsGrantedAsync(EducationPermissions.SchoolCreate).Returns(createGranted);
        checker.IsGrantedAsync(EducationPermissions.SchoolManage).Returns(manageGranted);
        var lazy = Substitute.For<IAbpLazyServiceProvider>();
        lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>());
        lazy.LazyGetRequiredService<ICurrentTenant>().Returns(GetRequiredService<ICurrentTenant>());
        lazy.LazyGetRequiredService<IAsyncQueryableExecuter>().Returns(GetRequiredService<IAsyncQueryableExecuter>());
        // ApplicationService.GuidGenerator uses LazyGetService<T>(defaultValue), not the required-service overload.
        lazy.LazyGetService<IGuidGenerator>(Arg.Any<IGuidGenerator>()).Returns(GetRequiredService<IGuidGenerator>());
        return new EducationSchoolAppService(_schools, _memberships, _terms, _classes, _subjects, _assignments, _enrollments, _users)
        {
            LazyServiceProvider = lazy
        };
    }

    private sealed record Fixture(SchoolDto School, AcademicTermDto Term, SchoolClassDto Class, EducationSubjectDto Subject,
        Guid Manager, Guid Teacher, Guid SecondTeacher, Guid Student, SchoolMembershipDto StudentMembership);
}
