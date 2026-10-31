using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace KnowPulse.Education;

internal sealed record EducationClassStudent(Guid UserId, Guid MembershipId, Guid EnrollmentId, string Name, string AccountName,
    DateTime MembershipFrom, DateTime? MembershipTo, DateTime EnrollmentFrom, DateTime? EnrollmentTo);

[RemoteService(false)]
public class EducationClassRoster : KnowPulseAppService
{
    internal async Task<List<EducationClassStudent>> GetAsync(EducationCurriculumSnapshot scope, TeachingAssignment assignment, List<Guid>? selectedIds = null)
    {
        if (scope.TenantId != CurrentTenant.Id || scope.TeacherUserId != CurrentUser.Id || assignment.TenantId != scope.TenantId || assignment.SchoolId != scope.SchoolId ||
            assignment.Id != scope.TeachingAssignmentId || assignment.TeacherUserId != scope.TeacherUserId) throw new AbpAuthorizationException();
        var enrollmentRepo = LazyServiceProvider.LazyGetRequiredService<IRepository<StudentEnrollment, Guid>>();
        var membershipRepo = LazyServiceProvider.LazyGetRequiredService<IRepository<SchoolMembership, Guid>>();
        var userRepo = LazyServiceProvider.LazyGetRequiredService<IRepository<IdentityUser, Guid>>();
        using var e = enrollmentRepo.DisableTracking(); using var m = membershipRepo.DisableTracking(); using var u = userRepo.DisableTracking();
        var enrollments = await enrollmentRepo.GetQueryableAsync(); var members = await membershipRepo.GetQueryableAsync(); var accounts = await userRepo.GetQueryableAsync(); var now = DateTime.UtcNow;
        var students = from enrollment in enrollments join member in members on enrollment.StudentUserId equals member.UserId join account in accounts on enrollment.StudentUserId equals account.Id
            where enrollment.TenantId == scope.TenantId && enrollment.SchoolId == scope.SchoolId && enrollment.SchoolClassId == assignment.SchoolClassId &&
                enrollment.EffectiveFromUtc <= now && (enrollment.EffectiveToUtc == null || now < enrollment.EffectiveToUtc) &&
                member.TenantId == scope.TenantId && member.SchoolId == scope.SchoolId && member.Role == SchoolMemberRole.Student && member.EffectiveFromUtc <= now &&
                (member.EffectiveToUtc == null || now < member.EffectiveToUtc) && account.TenantId == scope.TenantId && account.IsActive && (selectedIds == null || selectedIds.Contains(account.Id))
            orderby account.Id select new { Enrollment = enrollment, Membership = member, Account = account };
        var rows = await AsyncExecuter.ToListAsync(students.Take(EducationAssessmentTaskRevision.MaxRecipients + 1));
        if (rows.Count > EducationAssessmentTaskRevision.MaxRecipients || rows.Select(x => x.Account.Id).Distinct().Count() != rows.Count) throw new BusinessException(EducationAssessmentErrorCodes.CapacityExceeded);
        return rows.Select(x => new EducationClassStudent(x.Account.Id, x.Membership.Id, x.Enrollment.Id,
            string.Join(" ", new[] { x.Account.Name, x.Account.Surname }.Where(n => !string.IsNullOrWhiteSpace(n))) is { Length: > 0 } name ? name : x.Account.UserName,
            x.Account.UserName, x.Membership.EffectiveFromUtc, x.Membership.EffectiveToUtc, x.Enrollment.EffectiveFromUtc, x.Enrollment.EffectiveToUtc)).ToList();
    }
}
