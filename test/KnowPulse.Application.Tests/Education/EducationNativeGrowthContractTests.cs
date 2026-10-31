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

public class EducationNativeGrowthContractTests
{
    [Theory]
    [InlineData("GetMyContextsForSchoolAsync", "api/app/education-student-growth/my-contexts-for-school/{schoolId}")]
    [InlineData("GetMyGrowthForSchoolAsync", "api/app/education-student-growth/my-growth-for-school")]
    [InlineData("GetMyPointEvidenceForSchoolAsync", "api/app/education-student-growth/my-point-evidence-for-school")]
    public void Scoped_Growth_Routes_Should_Use_Actual_ABP_Conventions(string name, string expected)
    {
        var type = typeof(EducationStudentGrowthAppService); var method = type.GetMethod(name)!;
        var action = new ActionModel(method, method.GetCustomAttributes(true)) { ActionName = name, Controller = new ControllerModel(type.GetTypeInfo(), []) };
        foreach (var p in method.GetParameters()) action.Parameters.Add(new ParameterModel(p, p.GetCustomAttributes(true)) { Action = action, ParameterName = p.Name! });
        new ConventionalRouteBuilder(Options.Create(new AbpConventionalControllerOptions())).Build("app", "EducationStudentGrowth", action, "GET", new ConventionalControllerSetting(type.Assembly, "app", "Default")).ShouldBe(expected);
    }
    [Fact]
    public void Scoped_Wrappers_Should_Add_Only_Actual_School_Metadata_Without_Changing_Legacy_Snapshot_Fields()
    {
        typeof(EducationStudentGrowthContextsForSchoolDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "SchoolId", "Items" }.Order());
        typeof(EducationStudentGrowthForSchoolDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "SchoolId", "Value" }.Order());
        typeof(EducationStudentGrowthEvidenceForSchoolDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "SchoolId", "Evidence" }.Order());
        typeof(EducationStudentGrowthContext).GetProperties().Select(x => x.Name).ShouldNotContain("SchoolId");
    }
}
