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

public class EducationCorrectionImagesContractTests
{
    [Fact]
    public async Task Native_Correction_Download_Should_Delegate_Exact_Delivery_And_Attachment_And_Preserve_Denial()
    {
        var materials = Substitute.For<IEducationCorrectionAppService>();
        var delivery = Guid.NewGuid(); var attachment = Guid.NewGuid();
        using var file = new RemoteStreamContent(new MemoryStream([1]), "source.png", "image/png");
        materials.DownloadMyCorrectionImageAsync(delivery, attachment).Returns(file);
        var controller = new EducationCorrectionImagesController(materials) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        (await controller.DownloadAsync(delivery, attachment)).ShouldBeSameAs(file);
        controller.Response.Headers.CacheControl.ToString().ShouldBe("private, no-store"); controller.Response.Headers.Pragma.ToString().ShouldBe("no-cache");
        materials.DownloadMyCorrectionImageAsync(delivery, attachment).Returns<IRemoteStreamContent>(_ => throw new AbpAuthorizationException());
        await Should.ThrowAsync<AbpAuthorizationException>(() => controller.DownloadAsync(delivery, attachment));
    }

    [Fact]
    public void Native_Correction_Download_Should_Require_Both_Student_Permissions_And_Disable_Audit_And_Cache()
    {
        var type = typeof(EducationCorrectionImagesController);
        type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(new[] { EducationPermissions.AssessmentParticipate, EducationPermissions.LearningParticipate }.Order());
        type.GetCustomAttributes(typeof(DisableAuditingAttribute), true).Length.ShouldBe(1);
        type.GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>().Single().NoStore.ShouldBeTrue();
        type.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>().Single().Template.ShouldBe("api/education/corrections/deliveries/{deliveryId:guid}/images");
        type.GetMethod(nameof(EducationCorrectionImagesController.DownloadAsync))!.GetCustomAttributes(typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>().Single().Template.ShouldBe("{attachmentId:guid}");
    }
}
