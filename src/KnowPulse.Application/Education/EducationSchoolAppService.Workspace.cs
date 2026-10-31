using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace KnowPulse.Education;

public partial class EducationSchoolAppService
{
    [Authorize]
    public virtual async Task<EducationWorkspaceDto> GetMyWorkspaceAsync()
    {
        var userId = await GetAuthenticatedUserIdAsync();
        var tenantId = CurrentTenant.Id;
        var now = DateTime.UtcNow;
        var schools = (await GetMySchoolsAsync()).Items;
        var result = new EducationWorkspaceDto
        {
            AccountUserId = userId, TenantId = tenantId, ObservedAtUtc = now,
            ManagedSchoolIds = schools.Where(x => x.Roles.Contains(SchoolMemberRole.Manager)).Select(x => x.Id).ToList()
        };
        foreach (var permission in new[]
        {
            KnowPulsePermissions.KnowledgeBase.PersonalLibrary, EducationPermissions.SchoolManage,
            EducationPermissions.AssessmentManage, EducationPermissions.AssessmentParticipate,
            EducationPermissions.FeedbackManage, EducationPermissions.FeedbackView, EducationPermissions.AnalysisManage,
            EducationPermissions.TeachingPlanManage, EducationPermissions.TeachingPlanView,
            EducationPermissions.LearningManage, EducationPermissions.LearningParticipate
        })
        {
            if (await IsMvpPermissionGrantedAsync(permission)) result.GrantedPermissions.Add(permission);
        }

        var teacherSchools = schools.Where(x => x.Roles.Contains(SchoolMemberRole.Teacher)).Select(x => x.Id).ToList();
        var studentSchools = schools.Where(x => x.Roles.Contains(SchoolMemberRole.Student)).Select(x => x.Id).ToList();
        var terms = (await _terms.GetQueryableAsync()).Where(x => x.TenantId == tenantId && x.StartsAtUtc <= now && now < x.EndsAtUtc);
        var classes = (await _classes.GetQueryableAsync()).Where(x => x.TenantId == tenantId);
        var subjects = (await _subjects.GetQueryableAsync()).Where(x => x.TenantId == tenantId);
        var assignments = (await _assignments.GetQueryableAsync()).Where(x => x.TenantId == tenantId &&
            x.TeacherUserId == userId && teacherSchools.Contains(x.SchoolId) && x.EffectiveFromUtc <= now &&
            (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc.Value));
        var enrollments = (await _enrollments.GetQueryableAsync()).Where(x => x.TenantId == tenantId &&
            x.StudentUserId == userId && studentSchools.Contains(x.SchoolId) && x.EffectiveFromUtc <= now &&
            (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc.Value));

        var teaching = await AsyncExecuter.ToListAsync(
            from assignment in assignments
            join schoolClass in classes on assignment.SchoolClassId equals schoolClass.Id
            join term in terms on assignment.AcademicTermId equals term.Id
            join subject in subjects on assignment.EducationSubjectId equals subject.Id
            where schoolClass.SchoolId == assignment.SchoolId && schoolClass.AcademicTermId == term.Id &&
                term.SchoolId == assignment.SchoolId
            orderby term.Name, schoolClass.Grade, schoolClass.Name, subject.Name, assignment.Id
            select new
            {
                assignment.SchoolId,
                Class = new EducationWorkspaceClassDto
                {
                    RelationId = assignment.Id, SchoolClassId = schoolClass.Id, ClassName = schoolClass.Name, Grade = schoolClass.Grade,
                    AcademicTermId = term.Id, AcademicTermName = term.Name, EducationSubjectId = subject.Id, SubjectName = subject.Name
                }
            });
        var studying = await AsyncExecuter.ToListAsync(
            from enrollment in enrollments
            join schoolClass in classes on enrollment.SchoolClassId equals schoolClass.Id
            join term in terms on schoolClass.AcademicTermId equals term.Id
            where schoolClass.SchoolId == enrollment.SchoolId && term.SchoolId == enrollment.SchoolId
            orderby term.Name, schoolClass.Grade, schoolClass.Name, enrollment.Id
            select new
            {
                enrollment.SchoolId,
                Class = new EducationWorkspaceClassDto
                {
                    RelationId = enrollment.Id, SchoolClassId = schoolClass.Id, ClassName = schoolClass.Name, Grade = schoolClass.Grade,
                    AcademicTermId = term.Id, AcademicTermName = term.Name
                }
            });

        // This is a navigation snapshot; every destination still checks its current functional permission and relationship.
        foreach (var school in schools)
        {
            if (teacherSchools.Contains(school.Id)) result.Workspaces.Add(new()
            {
                SchoolId = school.Id, SchoolName = school.Name, Role = SchoolMemberRole.Teacher,
                Classes = teaching.Where(x => x.SchoolId == school.Id).Select(x => x.Class).ToList()
            });
            if (studentSchools.Contains(school.Id)) result.Workspaces.Add(new()
            {
                SchoolId = school.Id, SchoolName = school.Name, Role = SchoolMemberRole.Student,
                Classes = studying.Where(x => x.SchoolId == school.Id).Select(x => x.Class).ToList()
            });
        }
        return result;
    }
}
