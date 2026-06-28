using System;
using System.Linq;
using System.Reflection;
using KnowPulse.Controllers.AiQuestioning;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Xunit;

namespace KnowPulse.AiQuestioning;

/// <summary>
/// MVP-2 BE-005 AI 出题 HTTP API 路由契约测试。
/// 该测试不启动 HTTP Host，只锁定 Controller 暴露的稳定路由、HTTP verb 和权限入口。
/// </summary>
public class AiQuestionGenerationApiContractTests
{
    /// <summary>
    /// 验证 AI 出题 Controller 的根路由和权限点。
    /// </summary>
    [Fact]
    public void Controller_Should_Expose_Ai_Generation_Root_Route_And_Permission()
    {
        typeof(AiQuestionGenerationController)
            .GetCustomAttribute<RouteAttribute>()!
            .Template
            .ShouldBe("api/question-bank/ai-generation");

        typeof(AiQuestionGenerationController)
            .GetCustomAttribute<AuthorizeAttribute>()!
            .Policy
            .ShouldBe(KnowPulsePermissions.QuestionBank.AiGeneration);
    }

    /// <summary>
    /// 验证 BE-005 要求的任务创建、任务查询、候选题查看、重试和提交审核 API 均有稳定路由。
    /// </summary>
    [Theory]
    [InlineData(nameof(AiQuestionGenerationController.GetListAsync), "GET", "tasks")]
    [InlineData(nameof(AiQuestionGenerationController.GetAsync), "GET", "tasks/{id:guid}")]
    [InlineData(nameof(AiQuestionGenerationController.GetCandidateAsync), "GET", "candidates/{id:guid}")]
    [InlineData(nameof(AiQuestionGenerationController.CreateAsync), "POST", "tasks")]
    [InlineData(nameof(AiQuestionGenerationController.RetryAsync), "POST", "tasks/{id:guid}/retry")]
    [InlineData(nameof(AiQuestionGenerationController.SubmitCandidateForReviewAsync), "POST", "candidates/{candidateId:guid}/submit-review")]
    public void Controller_Should_Expose_Be005_Task_And_Candidate_Endpoints(
        string actionName,
        string expectedVerb,
        string expectedTemplate)
    {
        var method = GetAction(actionName);

        switch (expectedVerb)
        {
            case "GET":
                method.GetCustomAttribute<HttpGetAttribute>()!.Template.ShouldBe(expectedTemplate);
                break;
            case "POST":
                method.GetCustomAttribute<HttpPostAttribute>()!.Template.ShouldBe(expectedTemplate);
                break;
            default:
                throw new InvalidOperationException("Unsupported HTTP verb in test data.");
        }
    }

    private static MethodInfo GetAction(string actionName)
    {
        return typeof(AiQuestionGenerationController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(x => x.Name == actionName);
    }
}
