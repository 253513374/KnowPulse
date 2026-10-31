using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using KnowPulse.HttpApi.Education;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Shouldly;
using Xunit;

namespace KnowPulse.Education;

public class EducationTeachingPlanContractTests
{
    [Fact]
    public void Teaching_Plan_Endpoints_Should_Require_Student_Permission_Or_Both_Teacher_Plan_And_Analysis_Permissions()
    {
        var methods = typeof(IEducationTeachingPlanAppService).GetMethods(); methods.Length.ShouldBe(17);
        foreach (var method in methods) {
            var permissions = method.Name is "GenerateDraftAsync" or "GetGenerationAsync" or "GetGenerationsAsync" or "RetryGenerationAsync" ? new[] { EducationPermissions.TeachingPlanGenerate, EducationPermissions.TeachingPlanManage, EducationPermissions.AnalysisManage } :
                method.Name is "GetMyListAsync" or "OpenMyAsync" ? new[] { EducationPermissions.TeachingPlanView } :
                method.Name is "GetHintContextAsync" or "SaveHintReviewAsync" or "GetHintReviewOperationAsync" ?
                    new[] { EducationPermissions.TeachingPlanManage, EducationPermissions.AnalysisManage, EducationPermissions.AssessmentManage } :
                    new[] { EducationPermissions.TeachingPlanManage, EducationPermissions.AnalysisManage };
            method.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(permissions.Order());
            typeof(EducationTeachingPlanAppService).GetMethod(method.Name)!.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(permissions.Order());
        }
    }
    [Fact]
    public void Student_Plan_And_Nested_Content_Should_Have_An_Explicit_Public_Field_Allowlist()
    {
        var fields = new Dictionary<Type, string[]> {
            [typeof(EducationStudentTeachingPlanDto)] = ["Id", "SchoolId", "VersionId", "VersionNumber", "TeacherName", "ClassName", "SubjectName", "RecordedAtUtc", "Availability", "Content", "ExecutionState", "ExecutionChangedAtUtc"],
            [typeof(EducationStudentTeachingPlanContent)] = ["Title", "StudentGoal", "TeacherActions", "SuccessCriteria", "StartsAtUtc", "EndsAtUtc", "ReassessmentAtUtc", "Targets", "Steps", "Materials"],
            [typeof(EducationStudentTeachingMaterial)] = ["Id", "StepId", "Title"],
            [typeof(EducationStudentTeachingPlanPoint)] = ["Code", "Name", "RevisionNumber"],
            [typeof(EducationTeachingPlanStep)] = ["Id", "Kind", "Title", "Instructions", "EstimatedMinutes", "DueAtUtc"] };
        foreach (var (type, expected) in fields) type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(x => x.Name).Order().ShouldBe(expected.Order());
        new EducationStudentTeachingPlanDto().Content.ShouldBeNull();
    }
    [Fact]
    public async Task Teaching_Plan_And_Evidence_Responses_Should_Use_The_Private_No_Store_Filter()
    {
        foreach (var controller in new object[] { new EducationTeachingPlanAppService(), new EducationAssessmentAppService() }) {
            var http = new DefaultHttpContext(); var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
            var filters = new List<IFilterMetadata>(); var result = new OkObjectResult("approved-context"); var continued = 0;
            await new EducationStudentGrowthNoStoreFilter().OnResultExecutionAsync(new ResultExecutingContext(action, filters, result, controller),
                () => { continued++; return Task.FromResult(new ResultExecutedContext(action, filters, result, controller)); });
            http.Response.Headers.CacheControl.ToString().ShouldBe("private, no-store"); http.Response.Headers.Pragma.ToString().ShouldBe("no-cache");
            continued.ShouldBe(1);
        }
    }
}
