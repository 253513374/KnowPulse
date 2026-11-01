using System;
using System.Reflection;
using System.Threading.Tasks;
using KnowPulse.Blazor.Client.Pages.QuestionBank;
using KnowPulse.AiQuestioning;
using KnowPulse.Education;
using KnowPulse.RagAI;
using KnowPulse.QuestionBank;
using NSubstitute;
using Shouldly;
using Xunit;

namespace KnowPulse.Blazor;

public class RetiredSchoolQuestionTests
{
    [Theory]
    [InlineData("curriculum")]
    [InlineData("school")]
    [InlineData("shared")]
    public void Should_Not_Resume_A_Historical_School_Generation_Task(string kind)
    {
        var task = new AiQuestionGenerationTaskDto
        {
            CurriculumSnapshot = kind == "curriculum" ? new EducationCurriculumSnapshot() : null,
            SchoolId = kind == "school" ? Guid.NewGuid() : null,
            Scope = kind == "shared" ? KnowledgeSearchScope.SchoolShared : KnowledgeSearchScope.PersonalLibrary
        };

        var canRun = typeof(AiCandidates).GetMethod("CanRunTask", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [task]);

        canRun.ShouldBe(false);
    }

    [Fact]
    public async Task Should_Not_Approve_A_Historical_School_Question()
    {
        var page = new QuestionReview();
        var questions = Substitute.For<IQuestionAppService>();
        SetProperty(page, "QuestionAppService", questions);
        SetProperty(page, "Question", new QuestionDto { Id = Guid.NewGuid(), Curriculum = new EducationCurriculumSnapshot() });

        await (Task)typeof(QuestionReview).GetMethod("ApproveAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;

        var error = typeof(QuestionReview).GetProperty("ErrorMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
        error.ShouldBeOfType<string>().ShouldContain("已停止审核");
        await questions.DidNotReceive().ApproveAsync(Arg.Any<Guid>(), Arg.Any<ReviewQuestionDto>());
    }

    private static void SetProperty(QuestionReview page, string name, object value) =>
        typeof(QuestionReview).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
}
