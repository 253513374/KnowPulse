using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.Conventions;
using Xunit;

namespace KnowPulse.Education;

public class EducationLearningTrendContractTests
{
    [Fact]
    public void Trend_Should_Be_A_Snapshot_And_School_Checked_Read_With_No_Comparison_Keys_In_The_Response()
    {
        var type = typeof(EducationLearningReportAppService); var name = nameof(IEducationLearningReportAppService.GetTrendAsync); var method = type.GetMethod(name)!;
        var action = new ActionModel(method, method.GetCustomAttributes(true)) { ActionName = name, Controller = new ControllerModel(type.GetTypeInfo(), []) };
        foreach (var p in method.GetParameters()) action.Parameters.Add(new ParameterModel(p, p.GetCustomAttributes(true)) { Action = action, ParameterName = p.Name! });
        new ConventionalRouteBuilder(Options.Create(new AbpConventionalControllerOptions())).Build("app", "EducationLearningReport", action, "GET", new ConventionalControllerSetting(type.Assembly, "app", "Default")).ShouldBe("api/app/education-learning-report/trend");
        method.GetParameters().Select(p => p.Name).ShouldBe(["reportId", "snapshotId", "schoolId", "teachingAssignmentId", "input"]);
        typeof(EducationLearningTrendRow).GetProperties().Select(p => p.Name).ShouldNotContain(n => n.Contains("Body") || n.Contains("Definition") || n.Contains("Answer") || n.Contains("Fingerprint") || n.Contains("StudentUserId"));
        typeof(EducationLearningTrendStudentDto).GetProperties().Select(p => p.Name).Order().ShouldBe(new[] { "Id", "Name", "Account" }.Order());
    }
}
