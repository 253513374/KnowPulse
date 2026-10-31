using System.Collections.Generic;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.HttpApi.KnowledgeBase;
using KnowPulse.KnowledgeBase;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace KnowPulse.KnowledgeBase;

public class KnowledgeResponseCacheTests
{
    [Theory]
    [InlineData("library")]
    [InlineData("items")]
    [InlineData("management")]
    [InlineData("export")]
    [InlineData("notebook-source")]
    [InlineData("unrelated")]
    public async Task Private_Knowledge_Response_Should_Disallow_Caching_For_All_Owner_Content_Services(string service)
    {
        object controller = service switch
        {
            "library" => Substitute.For<IKnowledgeLibraryAppService>(),
            "items" => Substitute.For<IKnowledgeItemAppService>(),
            "management" => Substitute.For<IKnowledgeManagementAppService>(),
            "export" => Substitute.For<IKnowledgePersonalExportAppService>(),
            "notebook-source" => Substitute.For<IEducationNotebookSourceAppService>(),
            _ => new object()
        };
        var http = new DefaultHttpContext(); http.Response.Headers.CacheControl = "original";
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata>(); var result = new OkObjectResult("private content");
        var context = new ResultExecutingContext(action, filters, result, controller); var executed = false;
        await new KnowledgeResponseNoStoreFilter().OnResultExecutionAsync(context, () =>
        {
            executed = true;
            return Task.FromResult(new ResultExecutedContext(action, filters, result, controller));
        });
        executed.ShouldBeTrue();
        http.Response.Headers.CacheControl.ToString().ShouldBe(service == "unrelated" ? "original" : "private, no-store");
        http.Response.Headers.Pragma.ToString().ShouldBe(service == "unrelated" ? "" : "no-cache");
    }
}
