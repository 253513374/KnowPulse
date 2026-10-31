using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using NSubstitute;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationStudentInboxAppService Inbox => GetRequiredService<IEducationStudentInboxAppService>();

    [Fact]
    public async Task Student_Inbox_Should_Keep_Message_Read_Separate_From_Feedback_Body_And_Reject_Wrong_Scopes()
    {
        var f = await GradingFixtureAsync(false); var id = Guid.NewGuid(); Guid version;
        using (User(f.Source.School.Reader)) {
            await Feedback.SaveDraftAsync(id, FeedbackInput(f, "FEEDBACK-BODY-NOT-IN-MESSAGE")); version = (await PublishFeedbackAsync(id)).Id;
            (await Inbox.GetSourcesAsync(new() { SchoolId = f.Source.School.SchoolId })).ShouldBeEmpty();
        }
        using (User(f.Source.Student.UserId)) {
            (await Inbox.GetSourcesAsync(new() { SchoolId = f.Source.School.SchoolId })).ShouldBe([EducationInboxSource.Assessment, EducationInboxSource.Feedback, EducationInboxSource.TeachingPlan]);
            var page = await Inbox.GetMyListAsync(new() { SchoolId = f.Source.School.SchoolId, Source = EducationInboxSource.Feedback }); var message = page.Items.Single();
            JsonSerializer.Serialize(page).ShouldNotContain("FEEDBACK-BODY-NOT-IN-MESSAGE"); message.ReadAtUtc.ShouldBeNull(); message.CanOpen.ShouldBeTrue(); message.ReferenceId.ShouldBe(id); message.PublicationId.ShouldBe(version);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Inbox.ReadMyAsync(new() { SchoolId = Guid.NewGuid(), Source = EducationInboxSource.Feedback, MessageId = message.Id }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.OpenMyForSchoolAsync(id, version, Guid.NewGuid()));
            var read = await Inbox.ReadMyAsync(new() { SchoolId = f.Source.School.SchoolId, Source = EducationInboxSource.Feedback, MessageId = message.Id }); read.ReadAtUtc.ShouldNotBeNull();
            var receipt = (await Feedback.GetMyNoticesAsync(new())).Items.Single(); receipt.IsRead.ShouldBeFalse(); receipt.MessageReadAtUtc.ShouldBe(read.ReadAtUtc);
            (await Inbox.ReadMyAsync(new() { SchoolId = f.Source.School.SchoolId, Source = EducationInboxSource.Feedback, MessageId = message.Id })).ReadAtUtc.ShouldBe(read.ReadAtUtc);
            (await Feedback.OpenMyForSchoolAsync(id, version, f.Source.School.SchoolId)).Text.ShouldBe("FEEDBACK-BODY-NOT-IN-MESSAGE");
            (await Feedback.GetMyNoticesAsync(new())).Items.Single().IsRead.ShouldBeTrue();
            (await Inbox.GetMyListAsync(new() { SchoolId = f.Source.School.SchoolId, Source = EducationInboxSource.Assessment })).Items.ShouldAllBe(x => x.Source == EducationInboxSource.Assessment);
        }
        using (User(f.Absent.UserId)) { await Should.ThrowAsync<AbpAuthorizationException>(() => Inbox.ReadMyAsync(new() { SchoolId = f.Source.School.SchoolId, Source = EducationInboxSource.Feedback, MessageId = version })); }
        Guid withdrawn;
        using (User(f.Source.School.Reader)) { var current = await Feedback.GetAsync(id); withdrawn = (await Feedback.WithdrawAsync(id, new() { OperationId = Guid.NewGuid(), ConcurrencyStamp = current.ConcurrencyStamp, ExpectedPublishedRevisionId = version, Reason = "PRIVATE-WITHDRAWAL-REASON" })).Id; }
        using (User(f.Source.Student.UserId)) {
            var row = (await Inbox.GetMyListAsync(new() { SchoolId = f.Source.School.SchoolId, Source = EducationInboxSource.Feedback })).Items.Single(x => x.Id == withdrawn); row.Kind.ShouldBe(EducationInboxMessageKind.FeedbackWithdrawn); row.CanOpen.ShouldBeFalse();
            var read = await Inbox.ReadMyAsync(new() { SchoolId = f.Source.School.SchoolId, Source = EducationInboxSource.Feedback, MessageId = withdrawn }); read.ReadAtUtc.ShouldNotBeNull();
            var notice = (await Feedback.GetMyNoticesAsync(new())).Items.Single(x => x.Id == withdrawn); notice.IsRead.ShouldBeFalse(); notice.MessageReadAtUtc.ShouldBe(read.ReadAtUtc);
            JsonSerializer.Serialize(row).ShouldNotContain("PRIVATE-WITHDRAWAL-REASON");
        }
    }

    [Theory]
    [InlineData(EducationInboxSource.Assessment)]
    [InlineData(EducationInboxSource.Feedback)]
    [InlineData(EducationInboxSource.TeachingPlan)]
    public async Task Student_Inbox_Should_Reject_Denied_Source_Permission_Before_Resolving_Any_Source(EducationInboxSource source)
    {
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(source switch { EducationInboxSource.Assessment => EducationPermissions.AssessmentParticipate,
            EducationInboxSource.Feedback => EducationPermissions.FeedbackView, _ => EducationPermissions.TeachingPlanView }).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker); lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>());
        var denied = new EducationStudentInboxAppService { LazyServiceProvider = lazy };
        await Should.ThrowAsync<AbpAuthorizationException>(() => denied.GetMyListAsync(new() { SchoolId = Guid.NewGuid(), Source = source }));
        await Should.ThrowAsync<AbpAuthorizationException>(() => denied.ReadMyAsync(new() { SchoolId = Guid.NewGuid(), Source = source, MessageId = Guid.NewGuid() }));
    }
}
