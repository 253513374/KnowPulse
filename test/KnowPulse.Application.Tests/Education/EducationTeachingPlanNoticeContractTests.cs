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

public class EducationTeachingPlanNoticeContractTests
{
    [Fact]
    public void Plan_Notices_Should_Expose_Only_Metadata_And_Explicit_Student_Permission()
    {
        foreach (var (name, path) in new[] { ("GetMyPlanNoticesAsync", "my-plan-notices"), ("ReadMyPlanNoticeAsync", "read-my-plan-notice") }) {
            var method = typeof(EducationTeachingPlanAppService).GetMethod(name)!;
            foreach (var m in new[] { method, typeof(IEducationTeachingPlanNoticeAppService).GetMethod(name)! })
                m.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).ShouldContain(EducationPermissions.TeachingPlanView);
            var type = typeof(EducationTeachingPlanAppService);
            var action = new ActionModel(method, method.GetCustomAttributes(true)) { ActionName = name, Controller = new ControllerModel(type.GetTypeInfo(), []) };
            foreach (var p in method.GetParameters()) action.Parameters.Add(new ParameterModel(p, p.GetCustomAttributes(true)) { Action = action, ParameterName = p.Name! });
            new ConventionalRouteBuilder(Options.Create(new AbpConventionalControllerOptions())).Build("app", "EducationTeachingPlan", action, name.StartsWith("Get") ? "GET" : "POST", new ConventionalControllerSetting(type.Assembly, "app", "Default"))
                .ShouldBe("api/app/education-teaching-plan/" + path);
        }
        typeof(EducationTeachingPlanNoticeDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] {
            "Id", "SchoolId", "PlanId", "PublishedRevisionId", "Kind", "StepId", "RecordedAtUtc", "ReadAtUtc", "CanOpen" }.Order());
    }
}
