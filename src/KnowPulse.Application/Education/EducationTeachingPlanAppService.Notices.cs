using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationTeachingPlanAppService : IEducationTeachingPlanNoticeAppService
{
    private async Task<EducationTeachingPlanNoticeDto> PlanNoticeDtoAsync(EducationTeachingPlanNotice notice, EducationTeachingPlan plan)
    {
        var version = await RefAsync<EducationTeachingPlanRevision>(notice.PublishedRevisionId) ?? throw Invalid();
        var canOpen = plan.PublishedRevisionId == version.Id && notice.Kind != EducationTeachingPlanNoticeKind.Withdrawn &&
            (version.Basis()!.TaskIds.Count == 0 || await IsMvpPermissionGrantedAsync(EducationPermissions.AssessmentParticipate)) &&
            await AvailabilityAsync(plan, version, student: true) == EducationTeachingPlanAvailability.Available;
        Guid? step = null;
        if (notice.ActivityId is Guid activityId) {
            var activity = await RefAsync<EducationTeachingActivity>(activityId) ?? throw Invalid();
            var follow = await RefAsync<EducationTeachingActivityRevision>(notice.ActivityRevisionId!.Value) ?? throw Invalid();
            step = activity.StepId;
            canOpen = canOpen && activity.CurrentFollowUpRevisionId == follow.Id && activity.CurrentStudentRevisionId == follow.ReplyToStudentRevisionId;
        }
        return new() { Id = notice.Id, SchoolId = notice.SchoolId, PlanId = plan.Id, PublishedRevisionId = version.Id, Kind = notice.Kind,
            StepId = step, RecordedAtUtc = notice.RecordedAtUtc, ReadAtUtc = notice.ReadAtUtc, CanOpen = canOpen };
    }

    [Authorize(EducationPermissions.TeachingPlanView), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationTeachingPlanNoticeDto>> GetMyPlanNoticesAsync(GetEducationPlanNoticesInput input)
    {
        await StudentAsync(); EducationRules.RequireId(input.SchoolId); var take = Page(input);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: false); var now = DateTime.UtcNow;
        using var tracking = Repo<EducationTeachingPlanNotice>().DisableTracking();
        var notices = await Repo<EducationTeachingPlanNotice>().GetQueryableAsync(); var plans = await Repo<EducationTeachingPlan>().GetQueryableAsync();
        var members = await Repo<SchoolMembership>().GetQueryableAsync(); var enrollments = await Repo<StudentEnrollment>().GetQueryableAsync();
        var schools = await Repo<School>().GetQueryableAsync(); var terms = await Repo<AcademicTerm>().GetQueryableAsync(); var classes = await Repo<SchoolClass>().GetQueryableAsync();
        var query = from n in notices join p in plans on n.PlanId equals p.Id join m in members on p.SchoolMembershipId equals m.Id
            join e in enrollments on p.StudentEnrollmentId equals e.Id join s in schools on p.SchoolId equals s.Id
            join term in terms on p.AcademicTermId equals term.Id join c in classes on p.SchoolClassId equals c.Id
            where n.TenantId == CurrentTenant.Id && n.StudentUserId == CurrentUser.Id && n.SchoolId == input.SchoolId && n.RecordedAtUtc <= now &&
                p.TenantId == n.TenantId && m.TenantId == n.TenantId && e.TenantId == n.TenantId && s.TenantId == n.TenantId && term.TenantId == n.TenantId && c.TenantId == n.TenantId &&
                term.SchoolId == p.SchoolId && c.SchoolId == p.SchoolId && c.AcademicTermId == term.Id && m.Role == SchoolMemberRole.Student &&
                m.EffectiveFromUtc <= now && (!m.EffectiveToUtc.HasValue || now < m.EffectiveToUtc) &&
                e.EffectiveFromUtc <= now && (!e.EffectiveToUtc.HasValue || now < e.EffectiveToUtc) && term.StartsAtUtc <= now && now < term.EndsAtUtc
            select n;
        var total = await AsyncExecuter.LongCountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.RecordedAtUtc).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take));
        var items = new List<EducationTeachingPlanNoticeDto>();
        foreach (var notice in rows) {
            var plan = await RefAsync<EducationTeachingPlan>(notice.PlanId) ?? throw new AbpAuthorizationException();
            await StudentAccessAsync(plan, write: false); items.Add(await PlanNoticeDtoAsync(notice, plan));
        }
        await uow.CompleteAsync(); return new(total, items);
    }

    [Authorize(EducationPermissions.TeachingPlanView), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationTeachingPlanNoticeDto> ReadMyPlanNoticeAsync(Guid noticeId, Guid schoolId)
    {
        await StudentAsync(); EducationRules.RequireId(noticeId); EducationRules.RequireId(schoolId);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var notice = await RefAsync<EducationTeachingPlanNotice>(noticeId) ?? throw new AbpAuthorizationException();
        if (notice.TenantId != CurrentTenant.Id || notice.StudentUserId != CurrentUser.Id || notice.SchoolId != schoolId) throw new AbpAuthorizationException();
        var plan = await RefAsync<EducationTeachingPlan>(notice.PlanId) ?? throw new AbpAuthorizationException(); await StudentAccessAsync(plan);
        plan = await RefAsync<EducationTeachingPlan>(notice.PlanId) ?? throw new AbpAuthorizationException();
        using var tracking = Repo<EducationTeachingPlanNotice>().EnableTracking(); notice = await Repo<EducationTeachingPlanNotice>().GetAsync(noticeId);
        if (!notice.ReadAtUtc.HasValue) { notice.MarkRead(DateTime.UtcNow); await Repo<EducationTeachingPlanNotice>().UpdateManyAsync([notice], autoSave: true); }
        var result = await PlanNoticeDtoAsync(notice, plan); await uow.CompleteAsync(); return result;
    }
}
