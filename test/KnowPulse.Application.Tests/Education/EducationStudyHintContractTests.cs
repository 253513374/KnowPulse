using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationStudyHintContractTests
{
    [Fact]
    public void Study_Hint_Routes_Should_Require_Teaching_Question_And_Material_Permissions()
    {
        var expected = new[] { EducationPermissions.LearningManage, EducationPermissions.AssessmentManage, KnowPulsePermissions.KnowledgeBase.PersonalLibrary }.Order();
        var methods = typeof(IEducationStudyHintReviewAppService).GetMethods(); methods.Length.ShouldBe(4);
        foreach (var method in methods) {
            method.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected);
            typeof(EducationStudyTaskAppService).GetMethod(method.Name)!.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected);
        }
        typeof(EducationStudyHintContextReader).GetCustomAttribute<RemoteServiceAttribute>()!.IsEnabled.ShouldBeFalse();
    }
    [Fact]
    public void Study_Hint_Command_Fingerprint_Should_Bind_Recipient_Frozen_Question_And_Exact_Confirmation()
    {
        var recipient = Guid.NewGuid(); var question = Guid.NewGuid(); var command = new SaveEducationHintReviewDto { OperationId = Guid.NewGuid(), ConfirmedExactContext = true, Reason = "逐一核对指定材料和冻结题" };
        var hash = EducationStudyTaskFingerprint.HintReview(recipient, question, command);
        var roundtrip = JsonSerializer.Deserialize<SaveEducationHintReviewDto>(JsonSerializer.Serialize(command))!;
        EducationStudyTaskFingerprint.HintReview(recipient, question, roundtrip).ShouldBe(hash);
        EducationStudyTaskFingerprint.HintReview(recipient, Guid.NewGuid(), command).ShouldNotBe(hash);
        roundtrip.ConfirmedExactContext = false; EducationStudyTaskFingerprint.HintReview(recipient, question, roundtrip).ShouldNotBe(hash);
    }
}
