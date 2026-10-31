using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Xunit;

namespace KnowPulse.Education;

public class EducationPracticeContractTests
{
    [Fact]
    public void Practice_Should_Require_Real_Role_Permissions_And_Have_Body_Free_Lists_And_Receipts()
    {
        var methods = typeof(IEducationQuestionPracticeAppService).GetMethods(); methods.Length.ShouldBe(15);
        foreach (var method in methods) {
            var student = method.Name.Contains("MyPractice");
            var expected = student ? new[] { EducationPermissions.AssessmentParticipate, EducationPermissions.LearningParticipate } : new[] { EducationPermissions.AssessmentManage, EducationPermissions.LearningManage };
            method.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected.Order());
            typeof(EducationAssessmentAppService).GetMethod(method.Name)!.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected.Order());
        }
        typeof(EducationPracticeListDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "Id", "SchoolId", "RecipientId", "PaperQuestionId", "StartedAtUtc", "SubmittedAtUtc" }.Order());
        typeof(EducationPracticeOperationDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "PracticeId", "OperationId", "IsSubmission", "RequestFingerprint", "RecordedAtUtc" }.Order());
        typeof(EducationPracticeImageDto).GetProperties().Select(x => x.Name).ShouldNotContain(x => x == "BlobName" || x == "StudentUserId" || x == "RequestFingerprint");
        typeof(EducationPracticePartResult).GetProperties().Select(x => x.Name).ShouldNotContain("TextbookKnowledgePointMappingId");
    }
    [Fact]
    public void Practice_Fingerprint_Should_Bind_Source_Response_Preview_And_Explicit_Sharing()
    {
        var recipient = Guid.NewGuid(); var start = new StartEducationPracticeDto { OperationId = Guid.NewGuid(), PaperQuestionId = Guid.NewGuid(), ExpectedResultReleaseId = Guid.NewGuid() };
        var first = EducationPracticeFingerprint.Start(recipient, start); EducationPracticeFingerprint.Start(Guid.NewGuid(), start).ShouldNotBe(first);
        start.ExpectedResultReleaseId = Guid.NewGuid(); EducationPracticeFingerprint.Start(recipient, start).ShouldNotBe(first);
        var input = new SubmitEducationPracticeDto { OperationId = Guid.NewGuid(), PreparationFingerprint = new('a', 64), ConfirmSharingWithTeacher = true, ConfirmPracticeOnly = true,
            Response = new() { Type = QuestionBank.QuestionType.Subjective, Text = "我的复练" } };
        var fp = EducationPracticeFingerprint.Submit(recipient, input); var copy = JsonSerializer.Deserialize<SubmitEducationPracticeDto>(JsonSerializer.Serialize(input))!;
        EducationPracticeFingerprint.Submit(recipient, copy).ShouldBe(fp); copy.ConfirmSharingWithTeacher = false; EducationPracticeFingerprint.Submit(recipient, copy).ShouldNotBe(fp);
        copy.ConfirmSharingWithTeacher = true; copy.Response.Text = "更改内容"; EducationPracticeFingerprint.Submit(recipient, copy).ShouldNotBe(fp);
    }
}
