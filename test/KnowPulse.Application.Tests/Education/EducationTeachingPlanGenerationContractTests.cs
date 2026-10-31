using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.Conventions;
using Xunit;

namespace KnowPulse.Education;

public class EducationTeachingPlanGenerationContractTests
{
    [Fact]
    public void Plan_Generation_Should_Use_Queryable_Recovery_Routes_And_Never_Expose_The_Internal_Worker_As_Http()
    {
        var type = typeof(EducationTeachingPlanAppService);
        foreach (var name in new[] { nameof(IEducationTeachingPlanAppService.GetGenerationAsync), nameof(IEducationTeachingPlanAppService.GetGenerationsAsync) }) {
            var method = type.GetMethod(name)!; var action = new ActionModel(method, method.GetCustomAttributes(true)) { ActionName = name, Controller = new ControllerModel(type.GetTypeInfo(), []) };
            foreach (var p in method.GetParameters()) action.Parameters.Add(new ParameterModel(p, p.GetCustomAttributes(true)) { Action = action, ParameterName = p.Name! });
            var route = new ConventionalRouteBuilder(Options.Create(new AbpConventionalControllerOptions())).Build("app", "EducationTeachingPlan", action, "GET", new ConventionalControllerSetting(type.Assembly, "app", "Default"));
            route.ShouldBe(name == nameof(IEducationTeachingPlanAppService.GetGenerationAsync) ? "api/app/education-teaching-plan/generation/{generationId}" : "api/app/education-teaching-plan/generations");
        }
        type.GetMethod(nameof(EducationTeachingPlanAppService.ExecuteGenerationAsync))!.GetCustomAttribute<RemoteServiceAttribute>()!.IsEnabled.ShouldBeFalse();
        typeof(EducationTeachingPlanGenerationListDto).GetProperties().Select(x => x.Name).ShouldNotContain(n => n.Contains("Basis") || n.Contains("Content") || n.Contains("Request") && n != "RequestedAtUtc");
        typeof(EducationTeachingPlanGenerationDto).GetProperties().Select(x => x.Name).ShouldNotContain(n => n.EndsWith("Json") || n.EndsWith("Hash") || n == "RunId" || n == "LeaseExpiresAtUtc");
    }
    [Fact]
    public void Optional_Generation_Provenance_Should_Preserve_Legacy_Manual_Command_Json()
    {
        var input = new SaveEducationTeachingPlanDraftDto(); JsonSerializer.Serialize(input).ShouldNotContain("GenerationJobId");
        input.GenerationJobId = Guid.NewGuid(); JsonSerializer.Serialize(input).ShouldContain("GenerationJobId");
    }
}
