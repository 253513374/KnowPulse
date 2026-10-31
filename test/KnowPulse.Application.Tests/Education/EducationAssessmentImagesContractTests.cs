using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Controllers.Education;
using KnowPulse.Education;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Shouldly;
using Volo.Abp.Auditing;
using Volo.Abp.Content;
using Xunit;

namespace KnowPulse.Education;

public class EducationAssessmentImagesContractTests
{
    [Fact]
    public async Task Native_Get_Should_Delegate_Exact_Original_Image_Ids_And_Disable_Caching()
    {
        var attempts = Substitute.For<IEducationAssessmentAppService>();
        var attemptId = Guid.NewGuid(); var imageId = Guid.NewGuid();
        using var file = new RemoteStreamContent(new MemoryStream([1, 2, 3]), "answer.png", "image/png");
        attempts.DownloadAnswerImageAsync(attemptId, imageId).Returns(file);
        var controller = new EducationAssessmentImagesController(attempts) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        (await controller.DownloadAsync(attemptId, imageId)).ShouldBeSameAs(file);
        await attempts.Received(1).DownloadAnswerImageAsync(attemptId, imageId);
        controller.Response.Headers.CacheControl.ToString().ShouldBe("private, no-store");
        controller.Response.Headers.Pragma.ToString().ShouldBe("no-cache");
    }

    [Fact]
    public void Native_Image_Endpoint_Should_Require_Student_Permission_And_Route_Both_Ids_Without_Audit()
    {
        var type = typeof(EducationAssessmentImagesController);
        type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Policy.ShouldBe(EducationPermissions.AssessmentParticipate);
        type.GetCustomAttributes(typeof(DisableAuditingAttribute), true).Length.ShouldBe(1);
        type.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>().Single().Template.ShouldBe("api/education/assessment/attempts/{attemptId:guid}/images");
        type.GetMethod(nameof(EducationAssessmentImagesController.DownloadAsync))!.GetCustomAttributes(typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>().Single().Template.ShouldBe("{attachmentId:guid}");
    }
}
