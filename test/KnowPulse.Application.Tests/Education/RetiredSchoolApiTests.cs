using System.Linq;
using System.Reflection;
using KnowPulse.Controllers.Education;
using KnowPulse.Controllers.QuestionBank;
using KnowPulse.KnowledgeBase;
using KnowPulse.QuestionBank;
using KnowPulse.Teaching;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Xunit;

namespace KnowPulse.Education;

public class RetiredSchoolApiTests
{
    [Fact]
    public void Should_Not_Expose_Retired_School_Application_Services()
    {
        var services = typeof(KnowPulseApplicationModule).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(IApplicationService).IsAssignableFrom(type))
            .ToList();
        services.ShouldContain(typeof(TeachingAppService));
        services.ShouldContain(typeof(SchoolKnowledgeSourceAccessGuard));
        services.Where(type => type.Namespace == "KnowPulse.Education" || type.Name.Contains("SchoolKnowledge"))
            .ShouldAllBe(type => type.GetCustomAttribute<RemoteServiceAttribute>() != null &&
                !type.GetCustomAttribute<RemoteServiceAttribute>()!.IsEnabled);
        services.ShouldNotContain(type => type.Name == "KnowledgeSchoolSharingAppService" ||
            type.Namespace == "KnowPulse.Education" && type.Name.EndsWith("AppService"));

        typeof(KnowPulseApplicationContractsModule).Assembly.GetTypes()
            .Where(type => type.IsInterface && typeof(IApplicationService).IsAssignableFrom(type))
            .ShouldNotContain(type => type.Namespace == "KnowPulse.Education" || type.Name == "IKnowledgeSchoolSharingAppService");
    }

    [Fact]
    public void Should_Remove_School_Controllers_And_Keep_Private_Notebook_Attachments()
    {
        var controllers = typeof(KnowPulseHttpApiModule).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .ToList();
        controllers.ShouldContain(typeof(PersonalNotebookAttachmentsController));
        controllers.ShouldNotContain(type => type.Name.StartsWith("Education"));
        typeof(QuestionsController).GetMethod("ConfirmCurriculumAsync").ShouldBeNull();
        typeof(IQuestionAppService).GetMethod("ConfirmCurriculumAsync").ShouldBeNull();
    }
}
