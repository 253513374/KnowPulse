using System;
using System.Linq;
using System.Reflection;
using KnowPulse.Controllers.RagAI;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Xunit;

namespace KnowPulse.RagAI;

/// <summary>
/// MVP-1 RAG 评测 HTTP API 路由契约测试。
/// 该测试不启动 HTTP Host，只锁定 Controller 暴露的稳定路由、HTTP verb 和权限入口。
/// </summary>
public class Mvp1RagEvaluationApiContractTests
{
    /// <summary>
    /// 验证 RAG 评测 Controller 的根路由和权限点。
    /// </summary>
    [Fact]
    public void Controller_Should_Expose_Rag_Evaluation_Root_Route_And_Permission()
    {
        typeof(RagEvaluationsController)
            .GetCustomAttribute<RouteAttribute>()!
            .Template
            .ShouldBe("api/ai/rag/evaluations");

        typeof(RagEvaluationsController)
            .GetCustomAttribute<AuthorizeAttribute>()!
            .Policy
            .ShouldBe(KnowPulsePermissions.Rag.Evaluations);
    }

    /// <summary>
    /// 验证评测用例和评测运行 API 均有稳定路由。
    /// </summary>
    [Theory]
    [InlineData(nameof(RagEvaluationsController.CreateCaseAsync), "POST", "cases")]
    [InlineData(nameof(RagEvaluationsController.UpdateCaseAsync), "PUT", "cases/{id:guid}")]
    [InlineData(nameof(RagEvaluationsController.GetCaseAsync), "GET", "cases/{id:guid}")]
    [InlineData(nameof(RagEvaluationsController.GetCaseListAsync), "GET", "cases")]
    [InlineData(nameof(RagEvaluationsController.RunAsync), "POST", "runs")]
    [InlineData(nameof(RagEvaluationsController.GetRunAsync), "GET", "runs/{id:guid}")]
    [InlineData(nameof(RagEvaluationsController.GetRunListAsync), "GET", "runs")]
    public void Controller_Should_Expose_Case_And_Run_Endpoints(
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
            case "PUT":
                method.GetCustomAttribute<HttpPutAttribute>()!.Template.ShouldBe(expectedTemplate);
                break;
            default:
                throw new InvalidOperationException("Unsupported HTTP verb in test data.");
        }
    }

    private static MethodInfo GetAction(string actionName)
    {
        return typeof(RagEvaluationsController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(x => x.Name == actionName);
    }
}
