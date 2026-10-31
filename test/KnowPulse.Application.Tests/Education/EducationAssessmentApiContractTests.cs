using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationAssessmentApiContractTests
{
    [Fact]
    public void Assessment_Endpoints_Should_Declare_The_Corresponding_Functional_Permission()
    {
        var studentMethods = new HashSet<string>
        {
            nameof(IEducationAssessmentAppService.GetMyStudentListAsync),
            nameof(IEducationAssessmentAppService.GetMyStudentTaskAsync),
            nameof(IEducationAssessmentAppService.StartOrResumeAsync),
            nameof(IEducationAssessmentAppService.GetAttemptAsync),
            nameof(IEducationAssessmentAppService.SaveResponseAsync),
            nameof(IEducationAssessmentAppService.GetResponseRevisionAsync),
            nameof(IEducationAssessmentAppService.SubmitAsync),
            nameof(IEducationAssessmentAppService.GetSubmissionAsync),
            nameof(IEducationAssessmentAppService.PreviewSubmissionAsync),
            nameof(IEducationAssessmentAppService.GetAnswerImagePolicyAsync),
            nameof(IEducationAssessmentAppService.UploadAnswerImageAsync),
            nameof(IEducationAssessmentAppService.GetAnswerImageAsync),
            nameof(IEducationAssessmentAppService.DownloadAnswerImageAsync)
        };
        foreach (var method in typeof(IEducationAssessmentAppService).GetMethods())
        {
            var permission = studentMethods.Contains(method.Name)
                ? EducationPermissions.AssessmentParticipate : EducationPermissions.AssessmentManage;
            var contract = method.GetCustomAttribute<AuthorizeAttribute>();
            contract.ShouldNotBeNull();
            contract.Policy.ShouldBe(permission);
            var implementation = typeof(EducationAssessmentAppService).GetMethod(method.Name)!
                .GetCustomAttribute<AuthorizeAttribute>();
            implementation.ShouldNotBeNull();
            implementation.Policy.ShouldBe(permission);
        }
    }

    [Fact]
    public void Student_Assessment_Contracts_Should_Contain_Only_The_Approved_Safe_Fields()
    {
        // These are release-boundary allowlists, including every nested question/control DTO.
        // A new teacher-only field must not quietly become part of a conventional HTTP response.
        var fields = new Dictionary<Type, string[]>
        {
            [typeof(EducationStudentAssessmentTaskDto)] = ["Id", "SchoolId", "TaskId", "TaskRevisionId", "Title", "Instructions",
                "SchoolClassName", "SubjectName", "TextbookTitle", "StartsAtUtc", "EndsAtUtc", "DurationMinutes",
                "MaxAttempts", "ConfiguredResultVisibility", "CurrentResultVisibility", "QuestionCount", "CanStart",
                "CurrentAttemptId", "ServerNowUtc"],
            [typeof(EducationStudentAssessmentAttemptDto)] = ["Id", "RecipientId", "TaskRevisionId", "AttemptNumber",
                "StartedAtUtc", "ExpiresAtUtc", "ServerNowUtc", "Paper", "Status", "PaperSnapshotVersion",
                "ResponseSetVersion", "LastSavedAtUtc", "SubmissionId", "Responses"],
            [typeof(EducationStudentAssessmentPaperDto)] = ["Title", "QuestionCount", "TotalScore", "Questions"],
            [typeof(EducationStudentAssessmentQuestionDto)] = ["Id", "Type", "Title", "Stem", "DisplayOrder", "Score", "Options", "ResponseFields"],
            [typeof(ExamPaperQuestionOptionDto)] = ["OptionKey", "Content", "DisplayOrder"],
            [typeof(ExamResponseFieldDto)] = ["Id", "DisplayOrder", "StemMarker"],
            [typeof(EducationAssessmentResponseRevisionDto)] = ["Id", "AttemptId", "PaperQuestionId", "PaperSnapshotVersion",
                "ExpectedResponseSetVersion", "ResultResponseSetVersion", "RequestFingerprint", "ResponseHash", "Response", "SavedAtUtc"],
            [typeof(EducationAssessmentResponseValue)] = ["SchemaVersion", "Type", "SelectedOptionKeys", "Blanks", "Text", "Images"],
            [typeof(EducationAssessmentBlankResponse)] = ["BlankId", "Text"],
            [typeof(EducationAssessmentImageResponse)] = ["AttachmentId", "RotationDegrees"],
            [typeof(EducationAssessmentAnswerImagePolicyDto)] = ["MaxFileBytes", "MaxImagesPerResponse", "AllowedContentTypes"],
            [typeof(EducationAssessmentSubmissionPreviewDto)] = ["AttemptId", "PaperSnapshotVersion", "ResponseSetVersion", "ServerNowUtc", "ExpiresAtUtc", "CanSubmit", "SubmissionId", "Fingerprint", "Questions", "UnlinkedImages"],
            [typeof(EducationAssessmentSubmissionQuestionDto)] = ["Id", "DisplayOrder", "Title", "SavedResponseRevisionId", "IsUnanswered", "UnansweredBlankCount", "SavedImageCount"],
            [typeof(EducationAssessmentSubmissionDto)] = ["Id", "AttemptId", "PaperSnapshotVersion", "ResponseSetVersion",
                "Cause", "RequestFingerprint", "ConfirmedSavedOnly", "EffectiveAtUtc", "SealedAtUtc", "Manifest"],
            [typeof(EducationAssessmentSubmissionManifestEntry)] = ["PaperQuestionId", "ResponseRevisionId", "ResponseHash"],
            [typeof(EducationAssessmentAnswerImageDto)] = ["Id", "AttemptId", "PaperQuestionId", "FileName", "ContentType",
                "Size", "ContentHash", "RequestedAtUtc", "IsUploaded", "UploadedAtUtc", "CurrentResponseRevisionId"]
        };
        foreach (var (type, names) in fields)
        {
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(x => x.Name).Order()
                .ShouldBe(names.Order());
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var nested = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (nested.IsGenericType && nested.GetGenericTypeDefinition() == typeof(List<>))
                    nested = nested.GetGenericArguments()[0];
                if (!nested.IsPrimitive && !nested.IsEnum && nested != typeof(string) && nested != typeof(Guid) &&
                    nested != typeof(DateTime) && nested != typeof(decimal))
                    fields.ContainsKey(nested).ShouldBeTrue($"Unapproved nested student response type: {nested.Name}");
            }
        }
        new EducationStudentAssessmentTaskDto().CurrentResultVisibility.ShouldBe(ExamResultVisibility.Hidden);
    }

    [Theory]
    [InlineData(typeof(EducationAssessmentAccessGuard))]
    [InlineData(typeof(EducationAssessmentPaperGuard))]
    [InlineData(typeof(EducationAssessmentSubmissionProcessor))]
    [InlineData(typeof(EducationAssessmentGradingProcessor))]
    [InlineData(typeof(EducationQuestionExposureRecorder))]
    public void Assessment_Guards_Should_Not_Expose_Conventional_Http_Actions(Type type)
    {
        var remoteService = type.GetCustomAttribute<RemoteServiceAttribute>();
        remoteService.ShouldNotBeNull();
        remoteService.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Grading_Endpoints_Should_Declare_Teacher_Or_Participant_Permission_On_Contract_And_Implementation()
    {
        var students = new[] { nameof(IEducationAssessmentGradingAppService.GetMyResultAsync), nameof(IEducationAssessmentGradingAppService.GetMyResultForSchoolAsync),
            nameof(IEducationAssessmentGradingAppService.GetMyScoreReviewAsync), nameof(IEducationAssessmentGradingAppService.RequestScoreReviewAsync) };
        typeof(IEducationAssessmentGradingAppService).GetMethods().Length.ShouldBe(18);
        foreach (var method in typeof(IEducationAssessmentGradingAppService).GetMethods())
        {
            var expected = students.Contains(method.Name) ? EducationPermissions.AssessmentParticipate : EducationPermissions.AssessmentManage;
            method.GetCustomAttribute<AuthorizeAttribute>()!.Policy.ShouldBe(expected);
            typeof(EducationAssessmentAppService).GetMethod(method.Name)!.GetCustomAttribute<AuthorizeAttribute>()!.Policy.ShouldBe(expected);
        }
        typeof(EducationAssessmentStudentResultDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[]
        { "TaskId", "RecipientId", "AttemptId", "SubmissionId", "Outcome", "CurrentResultVisibility", "ResultReleaseId", "ReleasedAtUtc",
          "ScoreOpensAtUtc", "ReviewOpensAtUtc", "TotalScore", "MaxScore", "Questions", "AllowQuestionCollection" }.Order());
        var questionFields = typeof(EducationAssessmentStudentQuestionResultDto).GetProperties().Select(x => x.Name).ToList();
        questionFields.ShouldNotContain("SourceSnapshot"); questionFields.ShouldNotContain("CurriculumSources");
        questionFields.ShouldNotContain("MappingEvidence"); questionFields.ShouldNotContain("CurrentDraftRevisionId");
    }

    [Fact]
    public void Released_Student_Questions_And_Rubrics_Should_Keep_An_Explicit_Field_Allowlist()
    {
        var fields = new Dictionary<Type, string[]>
        {
            [typeof(EducationAssessmentStudentQuestionResultDto)] = ["Id", "Options", "Type", "Title", "Stem", "DisplayOrder", "MaxScore",
                "ObtainedScore", "Response", "CorrectAnswers", "Analysis", "ScoringDefinition", "Parts", "Feedback"],
            [typeof(EducationAssessmentStudentGradePartDto)] = ["PartId", "MaxScore", "ObtainedScore"],
            [typeof(QuestionScoringDefinition)] = ["SchemaVersion", "MultipleChoiceScoringMode", "ReferenceAnswer", "Blanks", "ScoringPoints"],
            [typeof(QuestionBlankScoringRule)] = ["Id", "DisplayOrder", "StemMarker", "Score", "AllowedAnswers", "TextRule", "TrimWhitespace",
                "FormatInstructions", "TextbookKnowledgePointMappingId"],
            [typeof(QuestionSubjectiveScoringPoint)] = ["Id", "DisplayOrder", "Description", "Score", "TextbookKnowledgePointMappingId"]
        };
        foreach (var (type, names) in fields)
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(x => x.Name).Order().ShouldBe(names.Order());
        typeof(EducationAssessmentStudentQuestionResultDto).GetProperty(nameof(EducationAssessmentStudentQuestionResultDto.Parts))!
            .PropertyType.ShouldBe(typeof(List<EducationAssessmentStudentGradePartDto>));
        new EducationAssessmentStudentResultDto().CurrentResultVisibility.ShouldBe(ExamResultVisibility.Hidden);
        new EducationAssessmentStudentResultDto().TotalScore.ShouldBeNull();
    }

    [Fact]
    public void Evidence_Endpoints_Should_Require_Grading_Authority_And_Additional_Feedback_Authority_For_Context_Text()
    {
        var methods = typeof(IEducationAssessmentEvidenceAppService).GetMethods(); methods.Length.ShouldBe(11);
        foreach (var method in methods)
        {
            var expected = new[] { EducationPermissions.AssessmentManage }.Concat(method.Name.Contains("Hint") ? [EducationPermissions.FeedbackManage] : Array.Empty<string>()).Order();
            method.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected);
            typeof(EducationAssessmentAppService).GetMethod(method.Name)!.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected);
        }
    }
}
