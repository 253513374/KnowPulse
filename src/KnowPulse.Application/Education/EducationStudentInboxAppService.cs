using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

[Authorize]
public class EducationStudentInboxAppService : KnowPulseAppService, IEducationStudentInboxAppService
{
    private IEducationSchoolAppService Schools => LazyServiceProvider.LazyGetRequiredService<IEducationSchoolAppService>();
    private IEducationAssessmentReminderAppService Assessments => LazyServiceProvider.LazyGetRequiredService<IEducationAssessmentReminderAppService>();
    private IEducationFeedbackAppService Feedback => LazyServiceProvider.LazyGetRequiredService<IEducationFeedbackAppService>();
    private IEducationTeachingPlanNoticeAppService Plans => LazyServiceProvider.LazyGetRequiredService<IEducationTeachingPlanNoticeAppService>();

    [Authorize, UnitOfWork(IsDisabled = true)]
    public virtual async Task<List<EducationInboxSource>> GetSourcesAsync(EducationInboxScopeDto input)
    {
        EducationRules.RequireId(input.SchoolId);
        await LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAccessGuard>().CurrentAccountAsync();
        var workspace = await Schools.GetMyWorkspaceAsync();
        if (!workspace.Workspaces.Any(x => x.SchoolId == input.SchoolId && x.Role == SchoolMemberRole.Student && x.Classes.Count > 0)) return [];
        return new[] { EducationInboxSource.Assessment, EducationInboxSource.Feedback, EducationInboxSource.TeachingPlan }.Where(x => workspace.GrantedPermissions.Contains(Permission(x))).ToList();
    }
    private static string Permission(EducationInboxSource source) => source switch {
        EducationInboxSource.Assessment => EducationPermissions.AssessmentParticipate,
        EducationInboxSource.Feedback => EducationPermissions.FeedbackView,
        EducationInboxSource.TeachingPlan => EducationPermissions.TeachingPlanView,
        _ => throw new AbpAuthorizationException() };
    private async Task CheckSourceAsync(Guid schoolId, EducationInboxSource source)
    {
        await CheckMvpPermissionAsync(Permission(source));
        if (!(await GetSourcesAsync(new() { SchoolId = schoolId })).Contains(source)) throw new AbpAuthorizationException();
    }
    private static EducationInboxMessageDto Map(Guid school, EducationAssessmentReminderDto row) => new() { Id = row.Id, SchoolId = school,
        Source = EducationInboxSource.Assessment, Kind = (EducationInboxMessageKind)row.Kind, ReferenceId = row.RecipientId, PublicationId = row.PublicationId,
        ResultReleaseId = row.ResultReleaseId, AvailableAtUtc = row.AvailableAtUtc, ReadAtUtc = row.ReadAtUtc, CanOpen = row.CanOpen, WeChatDelivery = row.WeChatDelivery };
    private static EducationInboxMessageDto Map(Guid school, EducationFeedbackNoticeDto row) => new() { Id = row.Id, SchoolId = school,
        Source = EducationInboxSource.Feedback, Kind = row.Kind == EducationFeedbackRevisionKind.Withdrawn ? EducationInboxMessageKind.FeedbackWithdrawn : EducationInboxMessageKind.FeedbackChanged,
        ReferenceId = row.FeedbackId, PublicationId = row.Id, AvailableAtUtc = row.RecordedAtUtc, ReadAtUtc = row.MessageReadAtUtc, CanOpen = row.CanOpen };
    private static EducationInboxMessageDto Map(Guid school, EducationTeachingPlanNoticeDto row) => new() { Id = row.Id, SchoolId = school,
        Source = EducationInboxSource.TeachingPlan, Kind = row.Kind switch { EducationTeachingPlanNoticeKind.Published => EducationInboxMessageKind.PlanPublished,
            EducationTeachingPlanNoticeKind.Withdrawn => EducationInboxMessageKind.PlanWithdrawn, EducationTeachingPlanNoticeKind.ExecutionChanged => EducationInboxMessageKind.PlanExecutionChanged,
            EducationTeachingPlanNoticeKind.DifficultyHandled => EducationInboxMessageKind.DifficultyHandled, _ => throw new AbpAuthorizationException() },
        ReferenceId = row.PlanId, PublicationId = row.PublishedRevisionId, StepId = row.StepId, AvailableAtUtc = row.RecordedAtUtc, ReadAtUtc = row.ReadAtUtc, CanOpen = row.CanOpen };

    [Authorize, UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationInboxMessageDto>> GetMyListAsync(GetEducationInboxInput input)
    {
        await CheckSourceAsync(input.SchoolId, input.Source);
        if (input.SkipCount < 0 || input.MaxResultCount is < 1 or > 100) throw new AbpAuthorizationException();
        if (input.Source == EducationInboxSource.Assessment) {
            var page = await Assessments.GetMyNoticesAsync(new() { SchoolId = input.SchoolId, SkipCount = input.SkipCount, MaxResultCount = input.MaxResultCount });
            return new(page.TotalCount, page.Items.Select(x => Map(input.SchoolId, x)).ToList());
        }
        if (input.Source == EducationInboxSource.TeachingPlan) {
            var plans = await Plans.GetMyPlanNoticesAsync(new() { SchoolId = input.SchoolId, SkipCount = input.SkipCount, MaxResultCount = input.MaxResultCount });
            return new(plans.TotalCount, plans.Items.Select(x => Map(input.SchoolId, x)).ToList());
        }
        var feedback = await Feedback.GetMyNoticesAsync(new() { SkipCount = input.SkipCount, MaxResultCount = input.MaxResultCount }, input.SchoolId);
        return new(feedback.TotalCount, feedback.Items.Select(x => Map(input.SchoolId, x)).ToList());
    }

    [Authorize, UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationInboxMessageDto> ReadMyAsync(ReadEducationInboxInput input)
    {
        await CheckSourceAsync(input.SchoolId, input.Source); EducationRules.RequireId(input.MessageId);
        if (input.Source == EducationInboxSource.Assessment) return Map(input.SchoolId, await Assessments.ReadMyReminderAsync(input.MessageId, input.SchoolId));
        if (input.Source == EducationInboxSource.TeachingPlan) return Map(input.SchoolId, await Plans.ReadMyPlanNoticeAsync(input.MessageId, input.SchoolId));
        return Map(input.SchoolId, await Feedback.ReadMyNoticeAsync(input.MessageId, input.SchoolId));
    }
}
