using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using KnowPulse.Controllers.Education;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.AspNetCore.Mvc.Conventions;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Xunit;

namespace KnowPulse.Education;

public class EducationNativePracticeContractTests
{
    [Fact]
    public async Task Native_Practice_Image_Get_Should_Use_Both_Permissions_Own_Service_And_NoStore_Without_Suppressing_Denial()
    {
        var api = Substitute.For<IEducationQuestionPracticeAppService>(); var id = Guid.NewGuid(); var image = Guid.NewGuid();
        using var file = new RemoteStreamContent(new MemoryStream([1]), "own.png", "image/png");
        api.DownloadMyPracticeImageAsync(id, image).Returns(file);
        var controller = new EducationPracticeImagesController(api) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        (await controller.DownloadAsync(id, image)).ShouldBeSameAs(file);
        controller.Response.Headers.CacheControl.ToString().ShouldBe("private, no-store");
        controller.Response.Headers.Pragma.ToString().ShouldBe("no-cache");
        api.DownloadMyPracticeImageAsync(id, image).Returns<Task<IRemoteStreamContent>>(_ => throw new AbpAuthorizationException());
        await Should.ThrowAsync<AbpAuthorizationException>(() => controller.DownloadAsync(id, image));
        typeof(EducationPracticeImagesController).GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order()
            .ShouldBe(new[] { EducationPermissions.AssessmentParticipate, EducationPermissions.LearningParticipate }.Order());
        typeof(EducationPracticeImagesController).GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/education/practices/{id:guid}/images");
    }

    [Fact]
    public void Native_Practice_Uses_Existing_Conventional_Routes_And_Separate_School_Checked_Entrypoints()
    {
        var type = typeof(EducationAssessmentAppService); var builder = new ConventionalRouteBuilder(Options.Create(new AbpConventionalControllerOptions()));
        string Route(string name, string verb)
        {
            var method = type.GetMethod(name)!; var action = new ActionModel(method, method.GetCustomAttributes(true)) { ActionName = name, Controller = new ControllerModel(type.GetTypeInfo(), []) };
            foreach (var p in method.GetParameters()) action.Parameters.Add(new ParameterModel(p, p.GetCustomAttributes(true)) { Action = action, ParameterName = p.Name! });
            return builder.Build("app", "EducationAssessment", action, verb, new ConventionalControllerSetting(type.Assembly, "app", "Default"));
        }
        Route(nameof(IEducationQuestionPracticeAppService.StartMyPracticeForSchoolAsync), "POST").ShouldBe("api/app/education-assessment/start-my-practice-for-school");
        Route(nameof(IEducationQuestionPracticeAppService.GetMyPracticeForSchoolAsync), "GET").ShouldBe("api/app/education-assessment/my-practice-for-school");
        Route(nameof(IEducationQuestionPracticeAppService.GetMyPracticeStartOperationAsync), "GET").ShouldBe("api/app/education-assessment/my-practice-start-operation");
        Route(nameof(IEducationQuestionPracticeAppService.SubmitMyPracticeAsync), "POST").ShouldBe("api/app/education-assessment/{id}/submit-my-practice");
    }
}
