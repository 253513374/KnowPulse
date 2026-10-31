using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.AspNetCore.Mvc.Conventions;
using Volo.Abp.Application.Services;
using Xunit;

namespace KnowPulse.Education;

public class EducationConventionalRoutingTests
{
    [Theory]
    [MemberData(nameof(Services))]
    public void Detail_And_List_Should_Have_Different_Conventional_Get_Routes(Type service)
    {
        var routes = new ConventionalRouteBuilder(Options.Create(new AbpConventionalControllerOptions()));
        var setting = new ConventionalControllerSetting(service.Assembly, "app", "Default");
        string Route(string name)
        {
            var method = service.GetMethod(name)!;
            var action = new ActionModel(method, method.GetCustomAttributes(true))
            {
                ActionName = method.Name,
                Controller = new ControllerModel(service.GetTypeInfo(), service.GetCustomAttributes(true))
            };
            foreach (var parameter in method.GetParameters())
                action.Parameters.Add(new ParameterModel(parameter, parameter.GetCustomAttributes(true)) { Action = action, ParameterName = parameter.Name! });
            return routes.Build("app", service.Name, action, "GET", setting);
        }
        var detail = Route(nameof(IEducationFeedbackAppService.GetAsync));
        var list = Route(nameof(IEducationFeedbackAppService.GetListAsync));
        detail.ShouldNotBe(list);
        detail.ShouldContain("{id}");
    }

    public static IEnumerable<object[]> Services() => typeof(EducationFeedbackAppService).Assembly.GetTypes()
        .Where(type => type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters &&
            typeof(IApplicationService).IsAssignableFrom(type) &&
            type.GetMethods().Any(method => method.Name == "GetAsync") &&
            type.GetMethods().Any(method => method.Name == "GetListAsync"))
        .Select(type => new object[] { type });
}
