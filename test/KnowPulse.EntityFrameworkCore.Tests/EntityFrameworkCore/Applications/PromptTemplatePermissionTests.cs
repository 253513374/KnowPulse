using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.AiQuestioning;
using KnowPulse.Permissions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[DependsOn(typeof(KnowPulseEntityFrameworkCoreTestModule))]
public class PromptTemplatePermissionTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.Replace(ServiceDescriptor.Singleton<IPermissionChecker>(_ => Substitute.For<IPermissionChecker>()));
    }
}

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class PromptTemplatePermissionTests : KnowPulseTestBase<PromptTemplatePermissionTestModule>
{
    [Fact]
    public async Task Should_Restrict_Template_Content_And_Inactive_Versions_By_Permission()
    {
        var granted = new HashSet<string>();
        var checker = GetRequiredService<IPermissionChecker>();
        checker.IsGrantedAsync(Arg.Any<string>()).Returns(call => Task.FromResult(granted.Contains(call.Arg<string>())));

        var repository = GetRequiredService<IRepository<AiQuestionPromptTemplate, Guid>>();
        var inactiveId = Guid.NewGuid();
        await repository.InsertAsync(new AiQuestionPromptTemplate(inactiveId, null, "inactive-permission-test", "v1",
            "private system prompt", "private user prompt", isActive: false), autoSave: true);

        var service = GetRequiredService<IAiQuestionGenerationAppService>();
        granted.Add(KnowPulsePermissions.QuestionBank.AiGeneration);
        var summaries = await service.GetPromptTemplatesAsync(new GetAiQuestionPromptTemplateListInput { MaxResultCount = 100 });
        summaries.Items.ShouldNotBeEmpty();
        summaries.Items.ShouldAllBe(x => x.IsActive && x.SystemPrompt == string.Empty && x.UserPromptTemplate == string.Empty);
        summaries.Items.ShouldNotContain(x => x.Id == inactiveId);
        var inactiveOnly = await service.GetPromptTemplatesAsync(new GetAiQuestionPromptTemplateListInput { IsActive = false });
        inactiveOnly.TotalCount.ShouldBe(0);

        granted.Clear();
        granted.Add(KnowPulsePermissions.QuestionBank.PromptTemplates);
        var full = await service.GetPromptTemplatesAsync(new GetAiQuestionPromptTemplateListInput { MaxResultCount = 100 });
        var inactive = full.Items.Single(x => x.Id == inactiveId);
        inactive.SystemPrompt.ShouldBe("private system prompt");
        inactive.UserPromptTemplate.ShouldBe("private user prompt");

        granted.Clear();
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetPromptTemplatesAsync(new GetAiQuestionPromptTemplateListInput()));
    }
}
