using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Blazor.Client.Components;
using KnowPulse.Permissions;
using NSubstitute;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity;
using Xunit;

namespace KnowPulse.Blazor;

public class TeacherNavigationTests
{
    [Fact]
    public async Task Should_Open_Materials_For_An_Authorized_Teacher()
    {
        var checker = WithPermissions(KnowPulsePermissions.KnowledgeBase.Items);

        (await checker.IsGrantedAsync(KnowPulsePermissions.KnowledgeBase.Items)).ShouldBeTrue();
        (await TeacherNavigation.GetHomeUrlAsync(checker)).ShouldBe("/teacher/materials");
    }

    [Theory]
    [InlineData(KnowPulsePermissions.Rag.Answer, "/teacher/answer")]
    [InlineData(KnowPulsePermissions.QuestionBank.Questions, "/teacher/questions")]
    [InlineData(KnowPulsePermissions.QuestionBank.AiGeneration, "/teacher/generate")]
    [InlineData(KnowPulsePermissions.Exam.Manage, "/teacher/tasks")]
    [InlineData(IdentityPermissions.Users.Default, "/admin")]
    [InlineData(KnowPulsePermissions.Rag.AuditLogs, "/admin")]
    [InlineData("", "/workspace")]
    public async Task Should_Open_Only_An_Available_Workspace(string permission, string expected)
    {
        (await TeacherNavigation.GetHomeUrlAsync(WithPermissions(permission))).ShouldBe(expected);
    }

    [Fact]
    public async Task Should_Prefer_The_First_Teacher_Workflow_To_Management()
    {
        var permissions = new[]
        {
            KnowPulsePermissions.KnowledgeBase.Items, KnowPulsePermissions.Rag.Answer,
            KnowPulsePermissions.QuestionBank.Questions, KnowPulsePermissions.QuestionBank.AiGeneration,
            KnowPulsePermissions.Exam.Manage, IdentityPermissions.Users.Default
        };
        var expected = new[] { "/teacher/materials", "/teacher/answer", "/teacher/questions", "/teacher/generate", "/teacher/tasks", "/admin" };

        for (var index = 0; index < permissions.Length; index++)
        {
            (await TeacherNavigation.GetHomeUrlAsync(WithPermissions(permissions.Skip(index).ToArray()))).ShouldBe(expected[index]);
        }
    }

    [Theory]
    [InlineData("/teacher/materials", true)]
    [InlineData("teacher/tasks/new?from=home#form", true)]
    [InlineData("/TEACHER/answer/", true)]
    [InlineData("/teacher", true)]
    [InlineData("/teachers/materials", false)]
    [InlineData("/teacher-copy/tasks", false)]
    [InlineData("/admin?next=/teacher/materials", false)]
    [InlineData("/", false)]
    public void Should_Keep_Teacher_Routes_Within_Their_Path_Segment(string path, bool expected)
    {
        TeacherNavigation.IsTeacherPath(path).ShouldBe(expected);
    }

    [Theory]
    [InlineData("/teacher/materials/11111111-1111-1111-1111-111111111111?version=2", "/teacher/materials")]
    [InlineData("teacher/shared/", "/teacher/shared")]
    [InlineData("/TEACHER/questions/new#form", "/teacher/questions")]
    [InlineData("/teacher/generate/11111111-1111-1111-1111-111111111111", "/teacher/questions")]
    [InlineData("/teacher/answer", "/teacher/materials")]
    [InlineData("/teacher/students", "/teacher/students")]
    [InlineData("/teacher/tasks/11111111-1111-1111-1111-111111111111/results", "/teacher/tasks")]
    [InlineData("/teacher/materials-copy", null)]
    [InlineData("/teacher/generate-copy", null)]
    [InlineData("/teacher/unknown", null)]
    [InlineData("/workspace", null)]
    public void Should_Select_The_Teacher_Section_Without_Matching_Similar_Names(string path, string? expected)
    {
        TeacherNavigation.GetSectionUrl(path).ShouldBe(expected);
    }

    private static IPermissionChecker WithPermissions(params string[] permissions)
    {
        var checker = Substitute.For<IPermissionChecker>();
        checker.IsGrantedAsync(Arg.Any<string>()).Returns(call => Task.FromResult(permissions.Contains(call.Arg<string>())));
        return checker;
    }
}
