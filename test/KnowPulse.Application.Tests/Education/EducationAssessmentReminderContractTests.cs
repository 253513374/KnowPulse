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

public class EducationAssessmentReminderContractTests
{
    [Fact]
    public void Reminder_Routes_And_Permissions_Should_Be_Explicit_And_Receipts_Contain_No_Teaching_Body()
    {
        var type = typeof(EducationAssessmentAppService);
        foreach (var method in typeof(IEducationAssessmentReminderAppService).GetMethods()) {
            var permission = method.Name.Contains("My") ? EducationPermissions.AssessmentParticipate : EducationPermissions.AssessmentManage;
            method.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).ShouldBe([permission]);
            type.GetMethod(method.Name)!.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).ShouldBe([permission]);
        }
        foreach (var (name, path) in new[] { ("GetReminderTargetsAsync", "reminder-targets"), ("GetReminderPreviewAsync", "reminder-preview"),
            ("SendReminderAsync", "send-reminder"), ("GetReminderOperationAsync", "reminder-operation"), ("GetMyRemindersAsync", "my-reminders"), ("GetMyNoticesAsync", "my-notices"), ("ReadMyReminderAsync", "{id}/read-my-reminder/{schoolId}") }) {
            var method = type.GetMethod(name)!; var action = new ActionModel(method, method.GetCustomAttributes(true)) { ActionName = name, Controller = new ControllerModel(type.GetTypeInfo(), []) };
            foreach (var p in method.GetParameters()) action.Parameters.Add(new ParameterModel(p, p.GetCustomAttributes(true)) { Action = action, ParameterName = p.Name! });
            new ConventionalRouteBuilder(Options.Create(new AbpConventionalControllerOptions())).Build("app", "EducationAssessment", action, name.StartsWith("Get") ? "GET" : "POST", new ConventionalControllerSetting(type.Assembly, "app", "Default"))
                .ShouldBe("api/app/education-assessment/" + path);
        }
        typeof(EducationAssessmentReminderDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "Id", "Kind", "ResultReleaseId", "CanOpen", "SchoolId", "TaskId", "PublicationId", "RecipientId", "RecordedAtUtc", "AvailableAtUtc", "ReadAtUtc", "WeChatDelivery" }.Order());
    }
}
