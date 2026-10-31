using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.Conventions;
using Xunit;

namespace KnowPulse.Education;

public class EducationStudentInboxContractTests
{
    [Fact]
    public void Inbox_And_Scoped_Feedback_Routes_Should_Keep_Metadata_And_Source_Checks_Separate()
    {
        foreach (var (type, controller, name, path) in new[] {
            (typeof(EducationStudentInboxAppService), "EducationStudentInbox", "GetSourcesAsync", "sources"),
            (typeof(EducationStudentInboxAppService), "EducationStudentInbox", "GetMyListAsync", "my-list"),
            (typeof(EducationStudentInboxAppService), "EducationStudentInbox", "ReadMyAsync", "read-my"),
            (typeof(EducationFeedbackAppService), "EducationFeedback", "ReadMyNoticeAsync", "read-my-notice"),
            (typeof(EducationFeedbackAppService), "EducationFeedback", "OpenMyForSchoolAsync", "open-my-for-school") }) {
            var method = type.GetMethod(name)!; method.IsDefined(typeof(AuthorizeAttribute)).ShouldBeTrue();
            var action = new ActionModel(method, method.GetCustomAttributes(true)) { ActionName = name, Controller = new ControllerModel(type.GetTypeInfo(), []) };
            foreach (var p in method.GetParameters()) action.Parameters.Add(new ParameterModel(p, p.GetCustomAttributes(true)) { Action = action, ParameterName = p.Name! });
            var prefix = controller == "EducationFeedback" ? "education-feedback" : "education-student-inbox";
            new ConventionalRouteBuilder(Options.Create(new AbpConventionalControllerOptions())).Build("app", controller, action, name.StartsWith("Get") ? "GET" : "POST", new ConventionalControllerSetting(type.Assembly, "app", "Default"))
                .ShouldBe("api/app/" + prefix + "/" + path);
        }
        typeof(EducationInboxMessageDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "Id", "SchoolId", "Source", "Kind", "ReferenceId", "PublicationId", "ResultReleaseId", "StepId", "AvailableAtUtc", "ReadAtUtc", "CanOpen", "WeChatDelivery" }.Order());
    }
}
