using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using KnowPulse.HttpApi.Education;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace KnowPulse.Education;

public class EducationQuestionCollectionContractTests
{
    [Fact]
    public async Task Collection_Metadata_And_Receipts_Should_Use_Private_NoStore_Headers()
    {
        var http = new DefaultHttpContext(); var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var controller = Substitute.For<IEducationQuestionCollectionAppService>(); var result = new OkObjectResult("metadata-only"); var filters = new List<IFilterMetadata>();
        await new EducationStudentGrowthNoStoreFilter().OnResultExecutionAsync(new ResultExecutingContext(action, filters, result, controller),
            () => Task.FromResult(new ResultExecutedContext(action, filters, result, controller)));
        http.Response.Headers.CacheControl.ToString().ShouldBe("private, no-store"); http.Response.Headers.Pragma.ToString().ShouldBe("no-cache");
    }
    [Fact]
    public void Collection_Should_Require_Both_Student_Permissions_And_Return_Only_References()
    {
        var expected = new[] { EducationPermissions.AssessmentParticipate, EducationPermissions.LearningParticipate };
        var methods = typeof(IEducationQuestionCollectionAppService).GetMethods(); methods.Length.ShouldBe(4);
        foreach (var method in methods) {
            method.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected.Order());
            typeof(EducationAssessmentAppService).GetMethod(method.Name)!.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected.Order());
        }
        typeof(EducationQuestionCollectionDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "Id", "SchoolId", "RecipientId", "PaperQuestionId", "RevisionNumber", "IsCollected", "RecordedAtUtc", "SourceAvailable", "TaskTitle" }.Order());
        typeof(EducationQuestionCollectionStateDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "SchoolId", "RecipientId", "PaperQuestionId", "Current", "CanCollect", "ResultReleaseId" }.Order());
    }
}
