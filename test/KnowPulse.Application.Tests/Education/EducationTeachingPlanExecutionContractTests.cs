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

public class EducationTeachingPlanExecutionContractTests
{
    [Fact]
    public void Execution_Decisions_Should_Have_Queryable_Recovery_Routes_And_Require_Both_Teacher_Permissions()
    {
        var type = typeof(EducationTeachingPlanAppService);
        var routes = new[] { ("GetExecutionReviewAsync", "execution-review"), ("DecideExecutionAsync", "decide-execution"), ("GetExecutionOperationAsync", "execution-operation"), ("GetExecutionHistoryAsync", "execution-history") };
        foreach (var (name, path) in routes) {
            foreach (var method in new[] { type.GetMethod(name)!, typeof(IEducationTeachingPlanExecutionAppService).GetMethod(name)! })
                method.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).ShouldBe([EducationPermissions.TeachingPlanManage, EducationPermissions.AnalysisManage]);
            var target = type.GetMethod(name)!; var action = new ActionModel(target, target.GetCustomAttributes(true)) { ActionName = name, Controller = new ControllerModel(type.GetTypeInfo(), []) };
            foreach (var p in target.GetParameters()) action.Parameters.Add(new ParameterModel(p, p.GetCustomAttributes(true)) { Action = action, ParameterName = p.Name! });
            var route = new ConventionalRouteBuilder(Options.Create(new AbpConventionalControllerOptions())).Build("app", "EducationTeachingPlan", action, name.StartsWith("Get") ? "GET" : "POST", new ConventionalControllerSetting(type.Assembly, "app", "Default"));
            route.ShouldBe("api/app/education-teaching-plan/" + path + (name is "GetExecutionReviewAsync" or "DecideExecutionAsync" ? "/{planId}" : ""));
        }
    }
    [Fact]
    public void Student_Execution_Status_Should_Expose_No_Private_Decision_Basis_Or_Reason()
    {
        var names = typeof(EducationStudentTeachingPlanDto).GetProperties().Select(x => x.Name).ToList();
        names.ShouldContain("ExecutionState"); names.ShouldContain("ExecutionChangedAtUtc");
        names.ShouldNotContain(x => x.Contains("Decision") || x.Contains("Reason") || x.Contains("Evidence") || x.Contains("Concurrency"));
        typeof(EducationPlanExecutionActivityEvidence).GetProperties().Select(x => x.Name).ShouldNotContain(x => x.Contains("Text") || x.Contains("Note") || x.Contains("Answer") || x.Contains("Score"));
    }
}
