using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationTeachingPlanNotice : AggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid PublishedRevisionId { get; private set; }
    public EducationTeachingPlanNoticeKind Kind { get; private set; }
    public Guid? ContentRevisionId { get; private set; }
    public Guid? DecisionId { get; private set; }
    public Guid? ActivityId { get; private set; }
    public Guid? ActivityRevisionId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    protected EducationTeachingPlanNotice() { }
    private EducationTeachingPlanNotice(Guid id, EducationTeachingPlan plan, Guid publication, EducationTeachingPlanNoticeKind kind, DateTime now)
        : base(EducationRules.RequireId(id))
    {
        if (plan.LastPublishedRevisionId != publication) throw Invalid();
        TenantId = plan.TenantId; PlanId = plan.Id; SchoolId = plan.SchoolId; StudentUserId = plan.StudentUserId;
        OwnerUserId = plan.OwnerUserId; PublishedRevisionId = EducationRules.RequireId(publication); Kind = kind; RecordedAtUtc = EducationRules.RequireUtc(now);
    }
    public static EducationTeachingPlanNotice Content(Guid id, EducationTeachingPlan plan, EducationTeachingPlanRevision revision)
    {
        if (revision.PlanId != plan.Id || revision.StudentUserId != plan.StudentUserId || revision.OwnerUserId != plan.OwnerUserId || revision.TenantId != plan.TenantId ||
            plan.PublicRevisionId != revision.Id || revision.Kind is not (EducationTeachingPlanRevisionKind.Published or EducationTeachingPlanRevisionKind.Withdrawn)) throw Invalid();
        return new(id, plan, plan.LastPublishedRevisionId ?? throw Invalid(), revision.Kind == EducationTeachingPlanRevisionKind.Published
            ? EducationTeachingPlanNoticeKind.Published : EducationTeachingPlanNoticeKind.Withdrawn, revision.RecordedAtUtc) { ContentRevisionId = revision.Id };
    }
    public static EducationTeachingPlanNotice Execution(Guid id, EducationTeachingPlan plan, EducationTeachingPlanDecisionRevision decision)
    {
        if (decision.PlanId != plan.Id || decision.StudentUserId != plan.StudentUserId || decision.OwnerUserId != plan.OwnerUserId || decision.TenantId != plan.TenantId ||
            plan.CurrentDecisionId != decision.Id || plan.PublishedRevisionId != decision.PublishedRevisionId || decision.Before == decision.After) throw Invalid();
        return new(id, plan, decision.PublishedRevisionId, EducationTeachingPlanNoticeKind.ExecutionChanged, decision.RecordedAtUtc) { DecisionId = decision.Id };
    }
    public static EducationTeachingPlanNotice Handled(Guid id, EducationTeachingPlan plan, EducationTeachingActivity activity, EducationTeachingActivityRevision revision)
    {
        if (activity.PlanId != plan.Id || activity.PlanRevisionId != plan.PublishedRevisionId || activity.StudentUserId != plan.StudentUserId || activity.OwnerUserId != plan.OwnerUserId ||
            activity.TenantId != plan.TenantId || revision.ActivityId != activity.Id || revision.TenantId != plan.TenantId || revision.StudentUserId != plan.StudentUserId ||
            revision.OwnerUserId != plan.OwnerUserId || revision.Action != EducationTeachingActivityAction.TeacherFollowUp || revision.ReplyToStudentRevisionId == null ||
            revision.ReplyToStudentRevisionId != activity.CurrentStudentRevisionId || activity.CurrentFollowUpRevisionId != revision.Id) throw Invalid();
        return new(id, plan, activity.PlanRevisionId, EducationTeachingPlanNoticeKind.DifficultyHandled, revision.RecordedAtUtc) { ActivityId = activity.Id, ActivityRevisionId = revision.Id };
    }
    public void MarkRead(DateTime now)
    {
        if (EducationRules.RequireUtc(now) < RecordedAtUtc) throw Invalid();
        ReadAtUtc ??= now;
    }
    private static BusinessException Invalid() => new(EducationTeachingPlanErrors.Invalid);
}
