using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Xunit;

namespace KnowPulse.Education;

public class EducationFeedbackApiContractTests
{
    [Fact]
    public void Feedback_Contracts_And_Implementations_Should_Require_The_Appropriate_Functional_Permission()
    {
        foreach (var method in typeof(IEducationFeedbackAppService).GetMethods())
        {
            var student = method.Name is nameof(IEducationFeedbackAppService.GetMyListAsync) or nameof(IEducationFeedbackAppService.OpenMyAsync) or nameof(IEducationFeedbackAppService.GetMyNoticesAsync)
                or nameof(IEducationFeedbackAppService.OpenMyForSchoolAsync) or nameof(IEducationFeedbackAppService.ReadMyNoticeAsync);
            var permission = student ? EducationPermissions.FeedbackView : EducationPermissions.FeedbackManage;
            method.GetCustomAttribute<AuthorizeAttribute>()!.Policy.ShouldBe(permission);
            typeof(EducationFeedbackAppService).GetMethod(method.Name)!.GetCustomAttribute<AuthorizeAttribute>()!.Policy.ShouldBe(permission);
        }
    }

    [Fact]
    public void Student_Feedback_And_Notice_Should_Only_Contain_Approved_Public_Fields()
    {
        typeof(EducationStudentFeedbackDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "Id", "VersionId", "VersionNumber",
            "SchoolId", "SchoolName", "TeacherName", "ClassName", "SubjectName", "Visibility", "RecordedAtUtc", "OpenedAtUtc", "Kind", "Context", "OccurredAtUtc", "PeriodEndsAtUtc", "Text", "AssessmentRecipientIds" }.Order());
        typeof(EducationFeedbackNoticeDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "Id", "FeedbackId", "Kind", "RecordedAtUtc", "IsCurrentVersion", "IsRead", "MessageReadAtUtc", "CanOpen" }.Order());
        typeof(EducationFeedbackNoticeDto).GetProperties().Any(x => x.Name.Contains("Text") || x.Name.Contains("Content")).ShouldBeFalse();
    }
}
