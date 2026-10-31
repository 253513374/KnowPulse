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
using NSubstitute;
using Shouldly;
using Xunit;

namespace KnowPulse.Education;

public class EducationWorkspaceContractTests
{
    [Fact]
    public void Workspace_Should_Require_Authentication_Without_A_Client_Supplied_Account_Role_Or_School()
    {
        var contract = typeof(IEducationSchoolAppService).GetMethod(nameof(IEducationSchoolAppService.GetMyWorkspaceAsync))!;
        contract.GetParameters().ShouldBeEmpty();
        contract.GetCustomAttributes<AuthorizeAttribute>().Single().Policy.ShouldBeNull();
        typeof(EducationSchoolAppService).GetMethod(contract.Name)!.GetCustomAttributes<AuthorizeAttribute>().Single().Policy.ShouldBeNull();
        foreach (var type in new[] { typeof(EducationWorkspaceDto), typeof(EducationRoleWorkspaceDto), typeof(EducationWorkspaceClassDto) })
        {
            type.GetProperties().Select(x => x.Name).ShouldNotContain(x => x.Contains("Answer") || x.Contains("Score") || x.Contains("Feedback") || x.Contains("Content") || x.Contains("StudentUserId") || x.Contains("TeacherUserId"));
        }
    }

    [Fact]
    public async Task Workspace_And_School_Identity_Responses_Should_Disallow_Caching()
    {
        var http = new DefaultHttpContext(); var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var controller = Substitute.For<IEducationSchoolAppService>(); var result = new OkObjectResult("own-identity"); var filters = new List<IFilterMetadata>();
        await new EducationStudentGrowthNoStoreFilter().OnResultExecutionAsync(new ResultExecutingContext(action, filters, result, controller),
            () => Task.FromResult(new ResultExecutedContext(action, filters, result, controller)));
        http.Response.Headers.CacheControl.ToString().ShouldBe("private, no-store"); http.Response.Headers.Pragma.ToString().ShouldBe("no-cache");
    }
}
