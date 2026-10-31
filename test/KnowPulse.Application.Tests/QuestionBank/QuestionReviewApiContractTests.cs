using System;
using System.Threading.Tasks;
using KnowPulse.Controllers.QuestionBank;
using NSubstitute;
using Xunit;

namespace KnowPulse.QuestionBank;

public class QuestionReviewApiContractTests
{
    [Theory]
    [InlineData(QuestionReviewDecision.Approve)]
    [InlineData(QuestionReviewDecision.Reject)]
    [InlineData(QuestionReviewDecision.ReturnForRevision)]
    public async Task Http_Review_Should_Preserve_The_Seen_Version_And_Recovery_Operation(QuestionReviewDecision decision)
    {
        var workflow = Substitute.For<IQuestionWorkflowAppService>();
        var controller = new QuestionsController(Substitute.For<IQuestionAppService>(), workflow);
        var questionId = Guid.NewGuid();
        var input = new ReviewQuestionDto { OperationId = Guid.NewGuid(), ConcurrencyStamp = "seen-version", Comment = "审核依据" };
        _ = decision switch
        {
            QuestionReviewDecision.Approve => await controller.ApproveAsync(questionId, input),
            QuestionReviewDecision.Reject => await controller.RejectAsync(questionId, input),
            _ => await controller.ReturnForRevisionAsync(questionId, input)
        };
        await workflow.Received(1).DecideReviewAsync(questionId, Arg.Is<DecideQuestionReviewDto>(x =>
            x.Decision == decision && x.OperationId == input.OperationId && x.ConcurrencyStamp == input.ConcurrencyStamp && x.Comment == input.Comment));
    }
}
