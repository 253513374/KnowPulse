using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Blazor.Client.Components;
using KnowPulse.Permissions;
using NSubstitute;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Xunit;

namespace KnowPulse.Blazor;

public class AdminNavigationTests
{
    [Theory]
    [InlineData("/exam/management/new?from=old", "/teacher/tasks/new?from=old")]
    [InlineData("/exam/management/new/?from=old", "/teacher/tasks/new?from=old")]
    [InlineData("/exam/management/11111111-1111-1111-1111-111111111111/results#score", "/teacher/tasks/11111111-1111-1111-1111-111111111111/results#score")]
    [InlineData("/exam/management/11111111-1111-1111-1111-111111111111/students/22222222-2222-2222-2222-222222222222", "/teacher/tasks/11111111-1111-1111-1111-111111111111/students/22222222-2222-2222-2222-222222222222")]
    [InlineData("/exam/management/not-a-guid/results", null)]
    [InlineData("/exam/management", null)]
    public void Should_Redirect_Legacy_Teaching_Task_Links(string path, string? expected)
    {
        AdminNavigation.GetTeachingRedirect(path).ShouldBe(expected);
    }

    [Theory]
    [InlineData("/knowledge/11111111-1111-1111-1111-111111111111?version=abc", "/teacher/materials/11111111-1111-1111-1111-111111111111?version=abc")]
    [InlineData("/question-bank/questions/new#form", "/teacher/questions/new#form")]
    [InlineData("/question-bank/questions/11111111-1111-1111-1111-111111111111/edit", "/teacher/questions/11111111-1111-1111-1111-111111111111/edit")]
    [InlineData("/knowledge/upload", null)]
    [InlineData("/question-bank/questions/invalid/edit", null)]
    public void Should_Map_Old_Teacher_Addresses_Without_Mapping_Management_Pages(string path, string? expected)
    {
        AdminNavigation.GetTeacherLegacyRedirect(path).ShouldBe(expected);
    }

    [Theory]
    [InlineData("/teacher/materials", false)]
    [InlineData("teacher/tasks/new", false)]
    [InlineData("/teacher/tasks/example/results", false)]
    [InlineData("/exam/take", false)]
    [InlineData("/rag/answer", false)]
    [InlineData("/knowledgeable", false)]
    [InlineData("/exam/management-copy", false)]
    [InlineData("/", false)]
    [InlineData("/admin", true)]
    [InlineData("/KNOWLEDGE/upload?step=2", true)]
    [InlineData("knowledge/index-jobs", true)]
    [InlineData("/identity/users/", true)]
    [InlineData("/exam/management/new", true)]
    [InlineData("/exam/management/example/results#score", true)]
    [InlineData("/rag/prompt-templates", true)]
    public void Should_Keep_Teaching_And_Management_Layouts_Separate(string path, bool expected)
    {
        AdminNavigation.IsManagementPath(path).ShouldBe(expected);
    }

    [Theory]
    [InlineData("/knowledge/index-jobs?status=failed", "/knowledge/index-jobs")]
    [InlineData("/knowledge/feedback", "/knowledge/feedback")]
    [InlineData("/knowledge/123", "/knowledge")]
    [InlineData("/organization/positions", "/organization/positions")]
    [InlineData("/question-bank/review/123", "/question-bank/reviews")]
    [InlineData("/exam/management/123/results", "/exam/management")]
    public void Should_Select_The_Most_Specific_Management_Section(string path, string expected)
    {
        AdminNavigation.GetSectionUrl(path).ShouldBe(expected);
    }

    [Fact]
    public async Task Should_Allow_An_Auditor_Without_User_Management_Permission()
    {
        var checker = WithPermissions(KnowPulsePermissions.Rag.AuditLogs);
        (await AdminNavigation.CanEnterAsync(checker)).ShouldBeTrue();
        var items = await AdminNavigation.GetVisibleItemsAsync(checker);
        items.Select(item => item.Url).ShouldBe(["/admin", "/rag/audit-logs"]);
    }

    [Fact]
    public async Task Should_Keep_Ordinary_Teacher_Permissions_Out_Of_The_Platform_Entry()
    {
        var checker = WithPermissions(KnowPulsePermissions.KnowledgeBase.Items, KnowPulsePermissions.QuestionBank.Questions,
            KnowPulsePermissions.QuestionBank.AiGeneration, KnowPulsePermissions.Exam.Manage);
        (await AdminNavigation.CanEnterAsync(checker)).ShouldBeFalse();
    }

    [Fact]
    public async Task Should_Not_Show_Audit_To_An_Ai_Teacher_With_Other_Management_Permissions()
    {
        var items = await AdminNavigation.GetVisibleItemsAsync(WithPermissions(
            KnowPulsePermissions.QuestionBank.AiGeneration, KnowPulsePermissions.Organization.Positions));
        items.ShouldNotContain(item => item.Url == "/rag/audit-logs");
    }

    [Fact]
    public async Task Should_Link_An_Organization_Operator_To_Their_Authorized_Page()
    {
        var items = await AdminNavigation.GetVisibleItemsAsync(WithPermissions(KnowPulsePermissions.Organization.Positions));
        items.Single(item => item.Title == "组织人员").Url.ShouldBe("/organization/positions");
    }

    [Fact]
    public async Task Should_Require_Read_And_Review_Permissions_For_The_Review_Queue()
    {
        var readDenied = await AdminNavigation.GetVisibleItemsAsync(WithPermissions(KnowPulsePermissions.QuestionBank.Reviews));
        readDenied.ShouldNotContain(item => item.Url == "/question-bank/reviews");
        var allowed = await AdminNavigation.GetVisibleItemsAsync(WithPermissions(KnowPulsePermissions.QuestionBank.Reviews, KnowPulsePermissions.QuestionBank.Questions));
        allowed.ShouldContain(item => item.Url == "/question-bank/reviews");
    }

    private static IPermissionChecker WithPermissions(params string[] permissions)
    {
        var checker = Substitute.For<IPermissionChecker>();
        checker.IsGrantedAsync(Arg.Any<string>()).Returns(call => Task.FromResult(permissions.Contains(call.Arg<string>())));
        return checker;
    }
}
