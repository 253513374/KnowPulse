using System.Collections.Generic;
using System.Threading.Tasks;
using KnowPulse.HttpApi.Education;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace KnowPulse.Education;

public class EducationStudentGrowthCacheTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Teacher_Notes_Feedback_And_Reports_Should_Disallow_Response_Storage(bool feedback)
    {
        var http = new DefaultHttpContext(); var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        object controller = feedback ? Substitute.For<IEducationFeedbackAppService>() : Substitute.For<IEducationLearningReportAppService>();
        var result = new OkObjectResult("private-teacher-body"); var filters = new List<IFilterMetadata>();
        await new EducationStudentGrowthNoStoreFilter().OnResultExecutionAsync(new ResultExecutingContext(action, filters, result, controller),
            () => Task.FromResult(new ResultExecutedContext(action, filters, result, controller)));
        http.Response.Headers.CacheControl.ToString().ShouldBe("private, no-store"); http.Response.Headers.Pragma.ToString().ShouldBe("no-cache");
    }
    [Fact]
    public async Task Practice_Responses_And_Files_Should_Use_Private_No_Store_Headers()
    {
        var http = new DefaultHttpContext(); var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var controller = Substitute.For<IEducationQuestionPracticeAppService>(); var result = new OkObjectResult("private-copy"); var filters = new List<IFilterMetadata>();
        await new EducationStudentGrowthNoStoreFilter().OnResultExecutionAsync(new ResultExecutingContext(action, filters, result, controller),
            () => Task.FromResult(new ResultExecutedContext(action, filters, result, controller)));
        http.Response.Headers.CacheControl.ToString().ShouldBe("private, no-store"); http.Response.Headers.Pragma.ToString().ShouldBe("no-cache");
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Growth_Response_Should_Disallow_Storage_Without_Changing_Unrelated_Responses(bool growth)
    {
        var http = new DefaultHttpContext(); http.Response.Headers.CacheControl = "original";
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        object controller = growth ? new EducationStudentGrowthAppService() : new object();
        var result = new OkObjectResult("safe-projection"); var filters = new List<IFilterMetadata>(); var executed = false;
        var context = new ResultExecutingContext(action, filters, result, controller);
        await new EducationStudentGrowthNoStoreFilter().OnResultExecutionAsync(context, () => {
            executed = true; return Task.FromResult(new ResultExecutedContext(action, filters, result, controller)); });
        executed.ShouldBeTrue(); http.Response.Headers.CacheControl.ToString().ShouldBe(growth ? "private, no-store" : "original");
        http.Response.Headers.Pragma.ToString().ShouldBe(growth ? "no-cache" : "");
    }
}
