using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Volo.Abp.Authorization.Permissions;

namespace KnowPulse.Blazor.Client.Components;

public static class TeacherNavigation
{
    public static readonly TeacherNavigationItem[] Items =
    [
        new("我的资料", "/teacher/materials", KnowPulsePermissions.KnowledgeBase.Items),
        new("共享资料", "/teacher/shared", KnowPulsePermissions.KnowledgeBase.Items),
        new("出题与题库", "/teacher/questions", KnowPulsePermissions.QuestionBank.Questions),
        new("我的学生", "/teacher/students", KnowPulsePermissions.Exam.Manage),
        new("答题任务", "/teacher/tasks", KnowPulsePermissions.Exam.Manage)
    ];

    public static async Task<string> GetHomeUrlAsync(IPermissionChecker checker)
    {
        (string Permission, string Url)[] destinations =
        [
            (KnowPulsePermissions.KnowledgeBase.Items, "/teacher/materials"),
            (KnowPulsePermissions.Rag.Answer, "/teacher/answer"),
            (KnowPulsePermissions.QuestionBank.Questions, "/teacher/questions"),
            (KnowPulsePermissions.QuestionBank.AiGeneration, "/teacher/generate"),
            (KnowPulsePermissions.Exam.Manage, "/teacher/tasks")
        ];
        foreach (var destination in destinations)
        {
            if (await checker.IsGrantedAsync(destination.Permission)) return destination.Url;
        }

        return await AdminNavigation.CanEnterAsync(checker) ? "/admin" : "/workspace";
    }

    public static bool IsTeacherPath(string path) => Matches(Normalize(path), "/teacher");

    public static string? GetSectionUrl(string path)
    {
        path = Normalize(path);
        if (Matches(path, "/teacher/generate")) return "/teacher/questions";
        if (Matches(path, "/teacher/answer")) return "/teacher/materials";
        return Items.FirstOrDefault(item => Matches(path, item.Url))?.Url;
    }

    private static bool Matches(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => "/" + path.Split('?', '#')[0].Trim('/');
}

public sealed record TeacherNavigationItem(string Title, string Url, string Permission);
