using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Xunit;

namespace KnowPulse.Education;

public class EducationCorrectionContractTests
{
    [Fact]
    public void Correction_Routes_Should_Require_Both_Real_Assessment_And_Learning_Permissions()
    {
        var methods = typeof(IEducationCorrectionAppService).GetMethods(); methods.Length.ShouldBe(23);
        foreach (var method in methods) {
            var student = method.Name.Contains("MyCorrection");
            var expected = student ? new[] { EducationPermissions.AssessmentParticipate, EducationPermissions.LearningParticipate } : new[] { EducationPermissions.AssessmentManage, EducationPermissions.LearningManage };
            method.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected.Order());
            typeof(EducationAssessmentAppService).GetMethod(method.Name)!.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected.Order());
        }
    }
    [Fact]
    public void Student_Correction_Projection_Should_Not_Expose_Teacher_Storage_Or_Private_Source_Identifiers()
    {
        typeof(EducationMyCorrectionDto).GetProperties().Select(x => x.Name).ShouldNotContain(x => x == "QuestionGradeId" || x == "GradeRevisionId" || x == "ResultReleaseId" || x == "Scope" || x == "OwnerUserId" || x == "ConcurrencyStamp");
        typeof(EducationCorrectionImageDto).GetProperties().Select(x => x.Name).ShouldNotContain(x => x == "BlobName" || x == "StudentUserId" || x == "RequestFingerprint");
        typeof(EducationCorrectionReviewDto).GetProperties().Select(x => x.Name).ShouldNotContain(x => x == "InternalNote" || x == "OwnerUserId" || x == "ConcurrencyStamp");
    }
    [Fact]
    public void Correction_Operation_Fingerprint_Should_Bind_Exact_Recipient_Content_And_Sharing_Confirmation()
    {
        var delivery = Guid.NewGuid(); var input = new SubmitEducationCorrectionDto { OperationId = Guid.NewGuid(), PreviewFingerprint = new('a', 64), Content = new() { CurrentSolution = "过程", CheckingStrategy = "验算" }, ConfirmSharingWithTeacher = true };
        var fp = EducationCorrectionFingerprint.Submit(delivery, input); var copy = JsonSerializer.Deserialize<SubmitEducationCorrectionDto>(JsonSerializer.Serialize(input))!;
        EducationCorrectionFingerprint.Submit(delivery, copy).ShouldBe(fp);
        EducationCorrectionFingerprint.Submit(Guid.NewGuid(), copy).ShouldNotBe(fp);
        copy.ConfirmSharingWithTeacher = false; EducationCorrectionFingerprint.Submit(delivery, copy).ShouldNotBe(fp);
        copy.ConfirmSharingWithTeacher = true; copy.Content = copy.Content with { Images = [new(Guid.NewGuid(), 90)] }; EducationCorrectionFingerprint.Submit(delivery, copy).ShouldNotBe(fp);
    }
}
