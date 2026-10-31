using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationTeachingPlanAppService
{
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationTeacherPlanCardDto>> GetTeacherPlansAsync(GetEducationTeacherPlansInput input)
    {
        await TeacherAsync(); EducationRules.RequireId(input.SchoolId); var take = Page(input);
        if (input.TeachingAssignmentId.HasValue) EducationRules.RequireId(input.TeachingAssignmentId.Value);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var now = DateTime.UtcNow;
        var plans = await Repo<EducationTeachingPlan>().GetQueryableAsync(); var versions = await Repo<EducationTeachingPlanRevision>().GetQueryableAsync();
        var teaching = await Repo<TeachingAssignment>().GetQueryableAsync(); var schools = await Repo<School>().GetQueryableAsync();
        var terms = await Repo<AcademicTerm>().GetQueryableAsync(); var classes = await Repo<SchoolClass>().GetQueryableAsync();
        var members = await Repo<SchoolMembership>().GetQueryableAsync(); var enrollments = await Repo<StudentEnrollment>().GetQueryableAsync(); var students = await Repo<IdentityUser>().GetQueryableAsync();
        var query = from plan in plans
            join version in versions on plan.LastPublishedRevisionId equals version.Id
            join relation in teaching on plan.TeachingAssignmentId equals relation.Id
            join school in schools on plan.SchoolId equals school.Id
            join term in terms on plan.AcademicTermId equals term.Id
            join schoolClass in classes on plan.SchoolClassId equals schoolClass.Id
            join member in members on plan.SchoolMembershipId equals member.Id
            join enrollment in enrollments on plan.StudentEnrollmentId equals enrollment.Id
            join student in students on plan.StudentUserId equals student.Id
            where plan.TenantId == CurrentTenant.Id && plan.OwnerUserId == CurrentUser.Id && plan.SchoolId == input.SchoolId && version.Kind == EducationTeachingPlanRevisionKind.Published &&
                (!input.TeachingAssignmentId.HasValue || plan.TeachingAssignmentId == input.TeachingAssignmentId) && student.IsActive &&
                relation.EffectiveFromUtc <= now && (!relation.EffectiveToUtc.HasValue || now < relation.EffectiveToUtc) && term.StartsAtUtc <= now && now < term.EndsAtUtc &&
                member.Role == SchoolMemberRole.Student && member.EffectiveFromUtc <= now && (!member.EffectiveToUtc.HasValue || now < member.EffectiveToUtc) &&
                enrollment.EffectiveFromUtc <= now && (!enrollment.EffectiveToUtc.HasValue || now < enrollment.EffectiveToUtc) &&
                members.Any(m => m.SchoolId == plan.SchoolId && m.UserId == plan.OwnerUserId && m.Role == SchoolMemberRole.Teacher && m.EffectiveFromUtc <= now && (!m.EffectiveToUtc.HasValue || now < m.EffectiveToUtc))
            select new { plan, version.RecordedAtUtc, PublishedRevisionNumber = version.RevisionNumber };
        var count = await AsyncExecuter.LongCountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.RecordedAtUtc).ThenBy(x => x.plan.Id).Skip(input.SkipCount).Take(take));
        var items = new System.Collections.Generic.List<EducationTeacherPlanCardDto>();
        foreach (var row in rows) {
            await TargetAsync(row.plan, false);
            items.Add(new() { Id = row.plan.Id, SchoolId = row.plan.SchoolId, TeachingAssignmentId = row.plan.TeachingAssignmentId,
                PublishedRevisionId = row.plan.LastPublishedRevisionId!.Value, PublishedRevisionNumber = row.PublishedRevisionNumber,
                IsCurrentPublication = row.plan.PublishedRevisionId == row.plan.LastPublishedRevisionId, ExecutionState = row.plan.ExecutionState,
                StudentName = row.plan.StudentName, ClassName = row.plan.ClassName, SubjectName = row.plan.SubjectName });
        }
        await uow.CompleteAsync(); return new(count, items);
    }
}
