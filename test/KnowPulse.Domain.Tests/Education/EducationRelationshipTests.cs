using System;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationRelationshipTests
{
    private static readonly DateTime Start = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Boundary = Start.AddDays(30);

    [Fact]
    public void Effective_Period_Should_Be_Half_Open_And_Allow_Adjacent_Relations()
    {
        var membership = new SchoolMembership(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), SchoolMemberRole.Teacher, Start, Boundary);
        membership.IsEffectiveAt(Start).ShouldBeTrue();
        membership.IsEffectiveAt(Boundary.AddTicks(-1)).ShouldBeTrue();
        membership.IsEffectiveAt(Boundary).ShouldBeFalse();
        EducationRules.Overlaps(Start, Boundary, Boundary, null).ShouldBeFalse();
        EducationRules.Overlaps(Start, null, Boundary, null).ShouldBeTrue();
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void Effective_Dates_Should_Reject_Non_Utc(DateTimeKind kind)
    {
        var invalid = DateTime.SpecifyKind(Start, kind);
        Should.Throw<BusinessException>(() => new SchoolMembership(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), SchoolMemberRole.Student, invalid))
            .Code.ShouldBe(EducationErrorCodes.UtcRequired);
        Should.Throw<BusinessException>(() => new AcademicTerm(Guid.NewGuid(), null, Guid.NewGuid(), "T1", "学期", Start, invalid.AddDays(1)))
            .Code.ShouldBe(EducationErrorCodes.UtcRequired);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(10)]
    public void Class_Should_Accept_Only_Junior_Secondary_Grades(int grade)
    {
        Should.Throw<BusinessException>(() => new SchoolClass(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "C1", "班级", grade))
            .Code.ShouldBe(EducationErrorCodes.InvalidGrade);
    }

    [Fact]
    public void Membership_Should_Reject_Unknown_Role_Empty_User_And_Empty_Period()
    {
        Should.Throw<BusinessException>(() => new SchoolMembership(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), (SchoolMemberRole)999, Start))
            .Code.ShouldBe(EducationErrorCodes.InvalidRole);
        Should.Throw<BusinessException>(() => new StudentEnrollment(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Start))
            .Code.ShouldBe(EducationErrorCodes.InvalidIdentifier);
        Should.Throw<BusinessException>(() => new TeachingAssignment(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Start, Start)).Code.ShouldBe(EducationErrorCodes.InvalidEffectivePeriod);
    }

    [Fact]
    public void Ending_Enrollment_Should_Keep_Original_Student_Class_And_Start()
    {
        var schoolId = Guid.NewGuid();
        var classId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var enrollment = new StudentEnrollment(Guid.NewGuid(), null, schoolId, classId, studentId, Start);
        enrollment.EndAt(Boundary);
        enrollment.EndAt(Boundary);
        enrollment.SchoolId.ShouldBe(schoolId);
        enrollment.SchoolClassId.ShouldBe(classId);
        enrollment.StudentUserId.ShouldBe(studentId);
        enrollment.EffectiveFromUtc.ShouldBe(Start);
        enrollment.EffectiveToUtc.ShouldBe(Boundary);
        Should.Throw<BusinessException>(() => enrollment.EndAt(Boundary.AddDays(1)))
            .Code.ShouldBe(EducationErrorCodes.CannotExtendEndedRelation);
        enrollment.EffectiveToUtc.ShouldBe(Boundary);
    }

    [Fact]
    public void Ending_Assignment_Should_Keep_Teacher_Subject_And_Term()
    {
        var teacher = Guid.NewGuid();
        var subject = Guid.NewGuid();
        var term = Guid.NewGuid();
        var assignment = new TeachingAssignment(Guid.NewGuid(), null, Guid.NewGuid(), term, Guid.NewGuid(), subject, teacher, Start, Boundary.AddDays(10));
        assignment.EndAt(Boundary);
        assignment.TeacherUserId.ShouldBe(teacher);
        assignment.EducationSubjectId.ShouldBe(subject);
        assignment.AcademicTermId.ShouldBe(term);
        assignment.EffectiveFromUtc.ShouldBe(Start);
        assignment.IsEffectiveAt(Boundary).ShouldBeFalse();
    }
}
