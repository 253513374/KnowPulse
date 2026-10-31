using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Controllers.Education;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Shouldly;
using Volo.Abp.Auditing;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Xunit;

namespace KnowPulse.Education;

public class EducationStudyAttachmentsContractTests
{
    [Fact]
    public async Task Native_Attachment_Should_Delegate_All_Ids_And_Preserve_Source_Denial()
    {
        var study = Substitute.For<IEducationStudyTaskAppService>();
        var recipient = Guid.NewGuid(); var material = Guid.NewGuid(); var attachment = Guid.NewGuid();
        using var file = new RemoteStreamContent(new MemoryStream([1, 2]), "material.pdf", "application/pdf");
        study.DownloadMyMaterialAsync(recipient, material, attachment).Returns(file);
        var controller = new EducationStudyAttachmentsController(study) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        (await controller.DownloadAsync(recipient, material, attachment)).ShouldBeSameAs(file);
        await study.Received(1).DownloadMyMaterialAsync(recipient, material, attachment);
        controller.Response.Headers.CacheControl.ToString().ShouldBe("private, no-store");
        controller.Response.Headers.Pragma.ToString().ShouldBe("no-cache");
        study.DownloadMyMaterialAsync(recipient, material, attachment).Returns<IRemoteStreamContent>(_ => throw new AbpAuthorizationException());
        await Should.ThrowAsync<AbpAuthorizationException>(() => controller.DownloadAsync(recipient, material, attachment));
    }

    [Fact]
    public void Native_Attachment_Route_Should_Require_Student_Authority_Without_Audit_Or_Cache()
    {
        var type = typeof(EducationStudyAttachmentsController);
        type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Policy.ShouldBe(EducationPermissions.LearningParticipate);
        type.GetCustomAttributes(typeof(DisableAuditingAttribute), true).Length.ShouldBe(1);
        type.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>().Single().Template.ShouldBe("api/education/study/recipients/{recipientId:guid}/materials/{materialId:guid}/attachments");
        type.GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>().Single().NoStore.ShouldBeTrue();
        type.GetMethod(nameof(EducationStudyAttachmentsController.DownloadAsync))!.GetCustomAttributes(typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>().Single().Template.ShouldBe("{attachmentId:guid}");
    }
}
