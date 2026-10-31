using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity;
using Volo.Abp.SettingManagement;
using Volo.Abp.TenantManagement;

namespace KnowPulse.Blazor.Client.Components;

public static class AdminNavigation
{
    public const string AccessPolicy = "KnowPulse.PlatformManagement";

    public static readonly AdminNavigationItem[] Items =
    [
        new("内容运营", "运营概览", "/admin", AccessPolicy),
        new("内容运营", "知识管理", "/knowledge", KnowPulsePermissions.KnowledgeBase.Items),
        new("内容运营", "知识分类", "/knowledge/categories", KnowPulsePermissions.KnowledgeBase.Categories),
        new("内容运营", "知识标签", "/knowledge/tags", KnowPulsePermissions.KnowledgeBase.Tags),
        new("内容运营", "题库审核", "/question-bank/reviews", KnowPulsePermissions.QuestionBank.Reviews, RequiredPermission: KnowPulsePermissions.QuestionBank.Questions),
        new("内容运营", "历史考试维护", "/exam/management", KnowPulsePermissions.Exam.Manage),
        new("内容运营", "反馈修订", "/knowledge/feedback", KnowPulsePermissions.KnowledgeBase.Feedback),
        new("AI 与运行", "索引运行", "/knowledge/index-jobs", KnowPulsePermissions.KnowledgeIndexing.IndexJobs),
        new("AI 与运行", "AI 模型", "/rag/models", KnowPulsePermissions.AiModels.View),
        new("AI 与运行", "Prompt 版本", "/rag/prompt-templates", KnowPulsePermissions.QuestionBank.PromptTemplates),
        new("AI 与运行", "调用审计", "/rag/audit-logs", KnowPulsePermissions.Rag.AuditLogs),
        new("AI 与运行", "质量评测", "/rag/evaluations", KnowPulsePermissions.Rag.Evaluations),
        new("系统设置", "组织人员", "/organization/departments", KnowPulsePermissions.Organization.Departments),
        new("系统设置", "组织人员", "/organization/positions", KnowPulsePermissions.Organization.Positions),
        new("系统设置", "组织人员", "/organization/job-levels", KnowPulsePermissions.Organization.JobLevels),
        new("系统设置", "组织人员", "/organization/user-assignments", KnowPulsePermissions.Organization.UserAssignments),
        new("系统设置", "用户管理", "/identity/users", IdentityPermissions.Users.Default),
        new("系统设置", "角色与权限", "/identity/roles", IdentityPermissions.Roles.Default),
        new("系统设置", "租户管理", "/tenant-management/tenants", TenantManagementPermissions.Tenants.Default),
        new("系统设置", "系统设置", "/setting-management", SettingManagementPermissions.Emailing, SettingManagementPermissions.TimeZone)
    ];

    private static readonly string[] ManagementPermissions =
    [
        IdentityPermissions.Users.Default, IdentityPermissions.Roles.Default,
        KnowPulsePermissions.Organization.Departments, KnowPulsePermissions.Organization.Positions,
        KnowPulsePermissions.Organization.JobLevels, KnowPulsePermissions.Organization.UserAssignments,
        KnowPulsePermissions.KnowledgeBase.Categories, KnowPulsePermissions.KnowledgeBase.Tags,
        KnowPulsePermissions.KnowledgeBase.Feedback, KnowPulsePermissions.KnowledgeIndexing.IndexJobs,
        KnowPulsePermissions.QuestionBank.Reviews, KnowPulsePermissions.QuestionBank.PromptTemplates, KnowPulsePermissions.AiModels.View,
        KnowPulsePermissions.Rag.AuditLogs, KnowPulsePermissions.Rag.Evaluations,
        TenantManagementPermissions.Tenants.Default, SettingManagementPermissions.Emailing, SettingManagementPermissions.TimeZone
    ];

    public static async Task<bool> CanEnterAsync(IPermissionChecker checker) =>
        (await Task.WhenAll(ManagementPermissions.Select(permission => checker.IsGrantedAsync(permission)))).Any(granted => granted);

    public static async Task<AdminNavigationItem[]> GetVisibleItemsAsync(IPermissionChecker checker)
    {
        var granted = await Task.WhenAll(Items.Select(async item =>
        {
            if (item.Url == "/admin") return await CanEnterAsync(checker);
            var allowed = await checker.IsGrantedAsync(item.Permission)
                || item.AlternativePermission is not null && await checker.IsGrantedAsync(item.AlternativePermission);
            return allowed && (item.RequiredPermission is null || await checker.IsGrantedAsync(item.RequiredPermission));
        }));
        return Items.Where((_, index) => granted[index]).GroupBy(item => item.Title).Select(group => group.First()).ToArray();
    }

    public static bool IsManagementPath(string path)
    {
        path = Normalize(path);
        return new[] { "/admin", "/knowledge", "/organization", "/question-bank", "/exam/management", "/rag/models", "/rag/prompt-templates", "/rag/audit-logs", "/rag/evaluations", "/identity", "/tenant-management", "/setting-management" }
            .Any(prefix => Matches(path, prefix));
    }

    public static string? GetTeachingRedirect(string path)
    {
        var relative = "/" + path.TrimStart('/');
        var suffixIndex = relative.IndexOfAny(['?', '#']);
        var route = suffixIndex < 0 ? relative : relative[..suffixIndex];
        var suffix = suffixIndex < 0 ? "" : relative[suffixIndex..];
        if (route.TrimEnd('/').Equals("/exam/management/new", StringComparison.OrdinalIgnoreCase)) return "/teacher/tasks/new" + suffix;
        var match = Regex.Match(route, @"^/exam/management/([0-9a-fA-F-]{36})/(results|students/[0-9a-fA-F-]{36})/?$", RegexOptions.IgnoreCase);
        return match.Success && Guid.TryParse(match.Groups[1].Value, out _)
            && (!match.Groups[2].Value.StartsWith("students/", StringComparison.OrdinalIgnoreCase) || Guid.TryParse(match.Groups[2].Value[9..], out _))
            ? "/teacher/tasks/" + match.Groups[1].Value + "/" + match.Groups[2].Value + suffix : null;
    }

    public static string? GetTeacherLegacyRedirect(string path)
    {
        var relative = "/" + path.TrimStart('/');
        var suffixIndex = relative.IndexOfAny(['?', '#']);
        var route = (suffixIndex < 0 ? relative : relative[..suffixIndex]).TrimEnd('/');
        var suffix = suffixIndex < 0 ? "" : relative[suffixIndex..];
        const string knowledgePrefix = "/knowledge/";
        if (route.StartsWith(knowledgePrefix, StringComparison.OrdinalIgnoreCase) && Guid.TryParse(route[knowledgePrefix.Length..], out var knowledgeId))
            return $"/teacher/materials/{knowledgeId}{suffix}";
        const string questionPrefix = "/question-bank/questions/";
        if (!route.StartsWith(questionPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var questionPath = route[questionPrefix.Length..];
        if (questionPath.Equals("new", StringComparison.OrdinalIgnoreCase)) return "/teacher/questions/new" + suffix;
        if (questionPath.EndsWith("/edit", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(questionPath[..^5], out var questionId))
            return $"/teacher/questions/{questionId}/edit{suffix}";
        return null;
    }

    public static string GetSectionUrl(string path)
    {
        path = Normalize(path);
        var item = Items.Where(item => Matches(path, item.Url)).OrderByDescending(item => item.Url.Length).FirstOrDefault();
        if (item is not null)
        {
            return item.Url;
        }

        if (Matches(path, "/organization")) return "/organization/departments";
        if (Matches(path, "/question-bank")) return "/question-bank/reviews";
        return "/admin";
    }

    private static bool Matches(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => "/" + path.Split('?', '#')[0].Trim('/');
}

public sealed record AdminNavigationItem(string Group, string Title, string Url, string Permission, string? AlternativePermission = null, string? RequiredPermission = null);
