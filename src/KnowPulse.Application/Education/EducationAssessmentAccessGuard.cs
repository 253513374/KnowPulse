using System;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

/// <summary>学生访问已分配快照的关系校验，不以教师教材身份授权学生。</summary>
[RemoteService(false)]
public class EducationAssessmentAccessGuard : KnowPulseAppService
{
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid>
        => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();

    private async Task<T?> ReferenceAsync<T>(Guid id) where T : class, IEntity<Guid>
    {
        using var tracking = Repo<T>().DisableTracking();
        return await Repo<T>().FindAsync(id);
    }

    public virtual async Task<Guid> CurrentAccountAsync()
    {
        if (!CurrentUser.IsAuthenticated || !CurrentUser.Id.HasValue) throw new AbpAuthorizationException();
        var account = await ReferenceAsync<IdentityUser>(CurrentUser.Id.Value);
        if (account == null || !account.IsActive || account.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        return account.Id;
    }

    public virtual async Task EnsureStudentAsync(Guid schoolId, Guid termId, Guid classId,
        Guid membershipId, Guid enrollmentId, Guid studentUserId, bool forUpdate = false)
    {
        if (await CurrentAccountAsync() != studentUserId) throw new AbpAuthorizationException();
        using var schoolTracking = forUpdate ? Repo<School>().EnableTracking() : null;
        using var accountTracking = forUpdate ? Repo<IdentityUser>().EnableTracking() : null;
        var schoolBaseline = forUpdate ? await Repo<School>().FindAsync(schoolId) : null;
        var accountBaseline = forUpdate ? await Repo<IdentityUser>().FindAsync(studentUserId) : null;
        await ValidateStudentAsync(schoolId, termId, classId, membershipId, enrollmentId, studentUserId);
        if (!forUpdate) return;
        if (schoolBaseline == null || schoolBaseline.TenantId != CurrentTenant.Id || accountBaseline == null ||
            accountBaseline.TenantId != CurrentTenant.Id || !accountBaseline.IsActive) throw new AbpAuthorizationException();
        // Relation writers contend on School; account deactivation contends on IdentityUser.
        // The caller owns the same business transaction as attempt creation.
        await Repo<School>().UpdateManyAsync([schoolBaseline], autoSave: true);
        await Repo<IdentityUser>().UpdateManyAsync([accountBaseline], autoSave: true);
        await CurrentAccountAsync();
        await ValidateStudentAsync(schoolId, termId, classId, membershipId, enrollmentId, studentUserId);
    }

    public virtual async Task EnsureTeacherRelationAsync(EducationCurriculumSnapshot scope, Guid termId, Guid classId, bool forUpdate = false)
    {
        if (scope.TeacherUserId != await CurrentAccountAsync() || scope.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        await EnsureRecordedTeacherRelationAsync(scope, termId, classId, forUpdate);
    }

    // Internal report workers validate the recorded owner; this does not create a caller identity or reading permission.
    public virtual async Task EnsureRecordedTeacherRelationAsync(EducationCurriculumSnapshot scope, Guid termId, Guid classId, bool forUpdate = false)
    {
        if (scope.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        var owner = await ReferenceAsync<IdentityUser>(scope.TeacherUserId);
        if (owner == null || owner.TenantId != CurrentTenant.Id || !owner.IsActive) throw new AbpAuthorizationException();
        using var schoolTracking = forUpdate ? Repo<School>().EnableTracking() : null;
        using var accountTracking = forUpdate ? Repo<IdentityUser>().EnableTracking() : null;
        var school = forUpdate ? await Repo<School>().FindAsync(scope.SchoolId) : null;
        var account = forUpdate ? await Repo<IdentityUser>().FindAsync(scope.TeacherUserId) : null;
        await ValidateTeacherRelationAsync(scope, termId, classId);
        if (!forUpdate) return;
        if (LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>().Current == null)
            throw new InvalidOperationException("Teaching writes require a business unit of work.");
        if (school == null || school.TenantId != CurrentTenant.Id || account == null || account.TenantId != CurrentTenant.Id || !account.IsActive)
            throw new AbpAuthorizationException();
        // Grading old frozen evidence needs current teaching authority, not the current
        // textbook or new-use provenance. Keep the same School -> Account lock order.
        await Repo<School>().UpdateManyAsync([school], autoSave: true);
        await Repo<IdentityUser>().UpdateManyAsync([account], autoSave: true);
        owner = await ReferenceAsync<IdentityUser>(scope.TeacherUserId);
        if (owner == null || owner.TenantId != CurrentTenant.Id || !owner.IsActive) throw new AbpAuthorizationException();
        await ValidateTeacherRelationAsync(scope, termId, classId);
    }

    public virtual async Task<TeachingAssignment> EnsureTeachingAssignmentAsync(Guid id, bool forUpdate = false)
    {
        var teaching = await ReferenceAsync<TeachingAssignment>(id) ?? throw new AbpAuthorizationException();
        await EnsureTeacherRelationAsync(new EducationCurriculumSnapshot { TenantId = teaching.TenantId, SchoolId = teaching.SchoolId,
            TeacherUserId = teaching.TeacherUserId, TeachingAssignmentId = teaching.Id, EducationSubjectId = teaching.EducationSubjectId },
            teaching.AcademicTermId, teaching.SchoolClassId, forUpdate);
        return teaching;
    }

    private async Task ValidateTeacherRelationAsync(EducationCurriculumSnapshot scope, Guid termId, Guid classId)
    {
        var now = DateTime.UtcNow;
        var school = await ReferenceAsync<School>(scope.SchoolId);
        var assignment = await ReferenceAsync<TeachingAssignment>(scope.TeachingAssignmentId);
        var term = await ReferenceAsync<AcademicTerm>(termId);
        var schoolClass = await ReferenceAsync<SchoolClass>(classId);
        if (school == null || school.TenantId != CurrentTenant.Id || assignment == null || assignment.TenantId != school.TenantId ||
            assignment.SchoolId != school.Id || assignment.TeacherUserId != scope.TeacherUserId || assignment.AcademicTermId != termId ||
            assignment.SchoolClassId != classId || assignment.EducationSubjectId != scope.EducationSubjectId || !assignment.IsEffectiveAt(now) ||
            term == null || term.TenantId != school.TenantId || term.SchoolId != school.Id || now < term.StartsAtUtc || now >= term.EndsAtUtc ||
            schoolClass == null || schoolClass.TenantId != school.TenantId || schoolClass.SchoolId != school.Id || schoolClass.AcademicTermId != term.Id)
            throw new AbpAuthorizationException();
        using var tracking = Repo<SchoolMembership>().DisableTracking();
        if (!await AsyncExecuter.AnyAsync((await Repo<SchoolMembership>().GetQueryableAsync()).Where(x => x.TenantId == school.TenantId &&
            x.SchoolId == school.Id && x.UserId == scope.TeacherUserId && x.Role == SchoolMemberRole.Teacher &&
            x.EffectiveFromUtc <= now && (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc.Value)))) throw new AbpAuthorizationException();
        // This grants only current teaching data access. An owned paper's historical visibility
        // and its old textbook selection do not grant continued access to former students.
    }

    internal async Task ValidateRecordedStudentAsync(EducationAssessmentTaskRevision revision, EducationAssessmentRecipient recipient)
    {
        var account = await ReferenceAsync<IdentityUser>(recipient.StudentUserId);
        if (account == null || account.TenantId != CurrentTenant.Id || !account.IsActive) throw new AbpAuthorizationException();
        await ValidateStudentAsync(revision.SchoolId, revision.AcademicTermId, revision.SchoolClassId,
            recipient.SchoolMembershipId, recipient.StudentEnrollmentId, recipient.StudentUserId);
    }

    private async Task ValidateStudentAsync(Guid schoolId, Guid termId, Guid classId,
        Guid membershipId, Guid enrollmentId, Guid studentUserId)
    {
        var now = DateTime.UtcNow;
        var school = await ReferenceAsync<School>(schoolId);
        var term = await ReferenceAsync<AcademicTerm>(termId);
        var schoolClass = await ReferenceAsync<SchoolClass>(classId);
        var membership = await ReferenceAsync<SchoolMembership>(membershipId);
        var enrollment = await ReferenceAsync<StudentEnrollment>(enrollmentId);
        if (school == null || school.TenantId != CurrentTenant.Id || term == null || term.TenantId != school.TenantId ||
            term.SchoolId != school.Id || now < term.StartsAtUtc || now >= term.EndsAtUtc ||
            schoolClass == null || schoolClass.TenantId != school.TenantId || schoolClass.SchoolId != school.Id ||
            schoolClass.AcademicTermId != term.Id || membership == null || membership.TenantId != school.TenantId ||
            membership.SchoolId != school.Id || membership.UserId != studentUserId || membership.Role != SchoolMemberRole.Student ||
            !membership.IsEffectiveAt(now) || enrollment == null || enrollment.TenantId != school.TenantId ||
            enrollment.SchoolId != school.Id || enrollment.SchoolClassId != schoolClass.Id || enrollment.StudentUserId != studentUserId ||
            !enrollment.IsEffectiveAt(now)) throw new AbpAuthorizationException();
    }
}
