using System;
using System.Reflection;
using System.Threading.Tasks;
using KnowPulse.Blazor.Client.Pages.QuestionBank;
using KnowPulse.QuestionBank;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Shouldly;
using Xunit;

namespace KnowPulse.Blazor;

public class QuestionEditorMergeTests
{
    [Theory]
    [InlineData("teacher/questions/new", false)]
    [InlineData("question-bank/questions/new", true)]
    public async Task Should_Keep_The_Teacher_Default_And_The_Curriculum_Entry(string route, bool curriculum)
    {
        var page = Create(route);

        await InvokeAsync(page, "OnParametersSetAsync");

        Field<bool>(page, "IsCurriculumQuestion").ShouldBe(curriculum);
    }

    [Fact]
    public async Task Should_Retry_Confirmation_Using_The_Saved_Teacher_Draft_Without_Creating_It_Again()
    {
        var page = Create("teacher/questions/new");
        var workflow = Substitute.For<IQuestionWorkflowAppService>();
        SetProperty(page, "QuestionWorkflow", workflow);
        var saved = new QuestionDto { Id = Guid.NewGuid(), Title = "判断题", Stem = "知识可以复习。" };
        workflow.CreateManualDraftAsync(Arg.Any<SaveQuestionDraftDto>()).Returns(saved);
        workflow.SaveDraftAsync(saved.Id, Arg.Any<SaveQuestionDraftDto>()).Returns(saved);
        workflow.ConfirmAsync(saved.Id).Returns(Task.FromException<QuestionDto>(new InvalidOperationException("确认失败")));
        await InvokeAsync(page, "OnParametersSetAsync");
        var form = Property<object>(page, "FormModel");
        form.GetType().GetProperty("Type")!.SetValue(form, QuestionType.TrueFalse);
        form.GetType().GetProperty("Title")!.SetValue(form, saved.Title);
        form.GetType().GetProperty("Stem")!.SetValue(form, saved.Stem);

        await InvokeAsync(page, "SaveAsync");
        await InvokeAsync(page, "SaveAsync");

        await workflow.Received(1).CreateManualDraftAsync(Arg.Any<SaveQuestionDraftDto>());
        await workflow.Received(1).SaveDraftAsync(saved.Id, Arg.Any<SaveQuestionDraftDto>());
        await workflow.Received(2).ConfirmAsync(saved.Id);
        Field<QuestionDto>(page, "SavedQuestion").Id.ShouldBe(saved.Id);
        Property<string>(page, "ErrorMessage").ShouldContain("确认失败");
    }

    private static QuestionEditor Create(string route)
    {
        var page = new QuestionEditor();
        SetProperty(page, "Navigation", new EditorNavigation(route));
        return page;
    }

    private static void SetProperty(QuestionEditor page, string name, object value) =>
        typeof(QuestionEditor).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);

    private static T Property<T>(QuestionEditor page, string name) =>
        (T)typeof(QuestionEditor).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

    private static T Field<T>(QuestionEditor page, string name) =>
        (T)typeof(QuestionEditor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

    private static Task InvokeAsync(QuestionEditor page, string name) =>
        (Task)typeof(QuestionEditor).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;

    private sealed class EditorNavigation : NavigationManager
    {
        public EditorNavigation(string route) => Initialize("http://localhost/", "http://localhost/" + route);
        protected override void NavigateToCore(string uri, bool forceLoad) => Uri = ToAbsoluteUri(uri).ToString();
    }
}
