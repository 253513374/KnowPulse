using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using KnowPulse.Controllers.Education;
using KnowPulse.Education;
using KnowPulse.HttpApi.KnowledgeBase;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.Conventions;
using Volo.Abp.Content;
using Xunit;

namespace KnowPulse.KnowledgeBase;

public class PersonalNotebookContractTests
{
    [Fact]
    public void Notebook_Metadata_And_Receipts_Should_Have_No_Bodies_And_Require_Own_Library()
    {
        typeof(IPersonalNotebookAppService).GetMethods().Length.ShouldBe(11);
        foreach (var m in typeof(IPersonalNotebookAppService).GetMethods())
            m.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).ShouldBe([KnowPulsePermissions.KnowledgeBase.PersonalLibrary]);
        typeof(PersonalNotebookAppService).GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).ShouldBe([KnowPulsePermissions.KnowledgeBase.PersonalLibrary]);
        foreach (var type in new[] { typeof(PersonalNotebookDto), typeof(PersonalNotebookVersionDto), typeof(PersonalNotebookReceiptDto), typeof(PersonalNotebookCommand) })
            type.GetProperties().Select(p => p.Name).ShouldNotContain(n => n.Contains("ContentText") || n.Contains("FileName") || n.Contains("StudentUserId") || n.Contains("SchoolId"));
    }
    [Fact]
    public async Task Notebook_Conventional_Responses_Should_Be_Private_NoStore()
    {
        var http = new DefaultHttpContext(); var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var controller = Substitute.For<IPersonalNotebookAppService>(); var result = new OkObjectResult("private"); var filters = new System.Collections.Generic.List<IFilterMetadata>();
        await new KnowledgeResponseNoStoreFilter().OnResultExecutionAsync(new(action, filters, result, controller), () => Task.FromResult(new ResultExecutedContext(action, filters, result, controller)));
        http.Response.Headers.CacheControl.ToString().ShouldBe("private, no-store");
    }
    [Fact]
    public async Task Notebook_File_Controller_Should_Keep_Original_Name_And_Reject_Paths()
    {
        var app = Substitute.For<IPersonalNotebookAppService>(); var controller = new PersonalNotebookAttachmentsController(app) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var bytes = new System.IO.MemoryStream([1, 2, 3]); var file = new FormFile(bytes, 0, 3, "file", "copied.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
        var note = Guid.NewGuid(); var version = Guid.NewGuid(); var op = Guid.NewGuid();
        app.UploadAttachmentAsync(note, version, op, Arg.Any<IRemoteStreamContent>()).Returns(Task.FromResult(new PersonalNotebookReceiptDto { OperationId = op }));
        (await controller.UploadAsync(note, version, op, file, Uri.EscapeDataString("我的推理.pdf"))).OperationId.ShouldBe(op);
        await app.Received(1).UploadAttachmentAsync(note, version, op, Arg.Is<IRemoteStreamContent>(x => x.FileName == "我的推理.pdf"));
        await Should.ThrowAsync<Volo.Abp.BusinessException>(() => controller.UploadAsync(note, version, op, file, "..%2Fsecret.txt"));
        controller.Response.Headers.CacheControl.ToString().ShouldBe("private, no-store");
    }
    [Theory]
    [InlineData("GetAsync", "GET", "api/app/personal-notebook/{id}")]
    [InlineData("GetListAsync", "GET", "api/app/personal-notebook")]
    [InlineData("CreateAsync", "POST", "api/app/personal-notebook")]
    [InlineData("SaveAsync", "POST", "api/app/personal-notebook/{id}/save")]
    [InlineData("DeleteAsync", "DELETE", "api/app/personal-notebook/{id}")]
    [InlineData("GetOperationAsync", "GET", "api/app/personal-notebook/operation/{operationId}")]
    [InlineData("GetVersionsAsync", "GET", "api/app/personal-notebook/{id}/versions")]
    [InlineData("GetVersionAsync", "GET", "api/app/personal-notebook/{id}/version/{versionId}")]
    [InlineData("GetAttachmentsAsync", "GET", "api/app/personal-notebook/{id}/attachments/{versionId}")]
    public void Native_Routes_Should_Match_Actual_ABP_Conventions(string name, string verb, string expected)
        => Route(typeof(PersonalNotebookAppService), "PersonalNotebook", name, verb).ShouldBe(expected);
    private static string Route(Type type, string controller, string name, string verb)
    {
        var method = type.GetMethod(name)!; var builder = new ConventionalRouteBuilder(Options.Create(new AbpConventionalControllerOptions()));
        var action = new ActionModel(method, method.GetCustomAttributes(true)) { ActionName = name, Controller = new ControllerModel(type.GetTypeInfo(), []) };
        foreach (var p in method.GetParameters()) action.Parameters.Add(new ParameterModel(p, p.GetCustomAttributes(true)) { Action = action, ParameterName = p.Name! });
        return builder.Build("app", controller, action, verb, new ConventionalControllerSetting(type.Assembly, "app", "Default"));
    }
}
