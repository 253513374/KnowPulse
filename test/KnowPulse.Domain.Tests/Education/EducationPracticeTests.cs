using System;
using System.Linq;
using KnowPulse.QuestionBank;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationPracticeTests
{
    [Fact]
    public void Practice_Should_Require_An_Actual_Response_Without_Changing_The_Original_Text()
    {
        var response = new EducationAssessmentResponseValue { Type = QuestionType.Subjective, Text = "  自己的新解法\n " };
        var original = response.ToCanonicalJson(); EducationQuestionPractice.ValidateResponse(response); response.ToCanonicalJson().ShouldBe(original);
        Should.Throw<BusinessException>(() => EducationQuestionPractice.ValidateResponse(new() { Type = QuestionType.Subjective, Text = "  \n" }));
        Should.Throw<BusinessException>(() => EducationQuestionPractice.ValidateResponse(new() { Type = QuestionType.SingleChoice }));
    }
    [Fact]
    public void Practice_Should_Bound_Selected_Images_And_Reject_Ambiguous_Responses()
    {
        var response = new EducationAssessmentResponseValue { Type = QuestionType.Subjective,
            Images = Enumerable.Range(0, 5).Select(_ => new EducationAssessmentImageResponse { AttachmentId = Guid.NewGuid() }).ToList() };
        EducationQuestionPractice.ValidateResponse(response);
        response.Images.Add(new() { AttachmentId = Guid.NewGuid() });
        Should.Throw<BusinessException>(() => EducationQuestionPractice.ValidateResponse(response));
        response.Images.RemoveAt(5); response.Images[1].AttachmentId = response.Images[0].AttachmentId;
        Should.Throw<BusinessException>(() => EducationQuestionPractice.ValidateResponse(response));
        Should.Throw<BusinessException>(() => EducationQuestionPractice.ValidateResponse(new() { Type = QuestionType.SingleChoice, SelectedOptionKeys = ["A"], Text = "混合无效答案" }));
    }
}
