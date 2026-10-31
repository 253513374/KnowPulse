using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
    /// 验证任务创建、任务查询、候选题查看、重试和采纳草稿 API 均有稳定路由。
    /// </summary>
    [Theory]
    [InlineData(nameof(AiQuestionGenerationController.GetListAsync), "GET", "tasks")]
    [InlineData(nameof(AiQuestionGenerationController.GetAsync), "GET", "tasks/{id:guid}")]
    [InlineData(nameof(AiQuestionGenerationController.GetCandidateAsync), "GET", "candidates/{id:guid}")]
    [InlineData(nameof(AiQuestionGenerationController.CreateAsync), "POST", "tasks")]
    [InlineData(nameof(AiQuestionGenerationController.RetryAsync), "POST", "tasks/{id:guid}/retry")]
    [InlineData("RegenerateAsync", "POST", "tasks/{id:guid}/regenerate")]
    [InlineData("UpdateCandidateAsync", "PUT", "candidates/{id:guid}")]
    [InlineData("AdoptCandidateAsDraftAsync", "POST", "candidates/{candidateId:guid}/adopt-draft")]
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
            case "PUT":
                method.GetCustomAttribute<HttpPutAttribute>()!.Template.ShouldBe(expectedTemplate);
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

    [Fact]
    public void Generation_Input_Should_Validate_Score_And_Purpose_Without_Removing_Legacy_Compatibility()
    {
        var input = new CreateAiQuestionGenerationTaskDto { KnowledgeScope = "范围", Score = 5.25m, Purpose = "Practice" };
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), errors, validateAllProperties: true).ShouldBeTrue();
        input.CurriculumScope.ShouldBeNull();
        input.Score = 0;
        Validator.TryValidateObject(input, new ValidationContext(input), errors, validateAllProperties: true).ShouldBeFalse();
        input.Score = 5;
        input.Purpose = new string('x', 65);
        errors.Clear();
        Validator.TryValidateObject(input, new ValidationContext(input), errors, validateAllProperties: true).ShouldBeFalse();
        errors.ShouldContain(x => x.MemberNames.Contains(nameof(CreateAiQuestionGenerationTaskDto.Purpose)));
    }

    [Fact]
    public void Candidate_Edit_Contract_Should_Carry_Revision_Without_A_Source_Replacement_Entrance()
    {
        var input = new UpdateAiQuestionCandidateDto { ContentRevision = -1, Title = "教师修订", Stem = "题干" };
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), errors, validateAllProperties: true).ShouldBeFalse();
        errors.ShouldContain(x => x.MemberNames.Contains(nameof(UpdateAiQuestionCandidateDto.ContentRevision)));
        var properties = typeof(UpdateAiQuestionCandidateDto).GetProperties().Select(x => x.Name).ToHashSet();
        properties.ShouldNotContain("CurriculumSnapshot");
        properties.ShouldNotContain("Citations");
        properties.ShouldNotContain("TextbookKnowledgePointMappingIds");
        properties.ShouldNotContain("SubmittedCitationChunkIds");
    }
}
