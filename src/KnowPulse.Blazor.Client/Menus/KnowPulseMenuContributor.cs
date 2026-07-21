using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using KnowPulse.Localization;
using KnowPulse.MultiTenancy;
using KnowPulse.Permissions;
using Volo.Abp.Account.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity.Blazor;
using Volo.Abp.SettingManagement.Blazor.Menus;
using Volo.Abp.TenantManagement.Blazor.Navigation;
using Volo.Abp.UI.Navigation;

namespace KnowPulse.Blazor.Client.Menus;

/// <summary>
/// Blazor 客户端菜单贡献者。
/// 这里负责把 KnowPulse 的组织管理、知识库管理菜单挂到 ABP 主菜单，
/// 并通过功能权限控制菜单是否显示；具体知识文档的业务可见性仍由后端应用服务判断。
/// </summary>
public class KnowPulseMenuContributor : IMenuContributor
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// 创建 Blazor 菜单贡献器。
    /// </summary>
    public KnowPulseMenuContributor(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// 根据 ABP 当前菜单名称分发到主菜单或用户菜单配置逻辑。
    /// </summary>
    public async Task ConfigureMenuAsync(MenuConfigurationContext context)
    {
        if (context.Menu.Name == StandardMenus.Main)
        {
            await ConfigureMainMenuAsync(context);
        }
        else if (context.Menu.Name == StandardMenus.User)
        {
            await ConfigureUserMenuAsync(context);
        }
    }

    /// <summary>
    /// 配置主导航菜单。组织管理和知识库菜单都绑定到 KnowPulse 自定义权限。
    /// </summary>
    private Task ConfigureMainMenuAsync(MenuConfigurationContext context)
    {
        var l = context.GetLocalizer<KnowPulseResource>();

        context.Menu.Items.Insert(
            0,
            new ApplicationMenuItem(
                KnowPulseMenus.Home,
                l["Menu:Home"],
                "/",
                icon: "fas fa-home"
            )
        );

        var organization = new ApplicationMenuItem(
            KnowPulseMenus.Organization,
            l["Menu:Organization"],
            icon: "fas fa-sitemap"
        ).RequirePermissions(KnowPulsePermissions.Organization.Default);

        organization.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.Departments,
            l["Menu:Departments"],
            "/organization/departments",
            icon: "fas fa-building"
        ).RequirePermissions(KnowPulsePermissions.Organization.Departments));

        organization.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.Positions,
            l["Menu:Positions"],
            "/organization/positions",
            icon: "fas fa-id-badge"
        ).RequirePermissions(KnowPulsePermissions.Organization.Positions));

        organization.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.JobLevels,
            l["Menu:JobLevels"],
            "/organization/job-levels",
            icon: "fas fa-layer-group"
        ).RequirePermissions(KnowPulsePermissions.Organization.JobLevels));

        organization.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.UserAssignments,
            l["Menu:UserAssignments"],
            "/organization/user-assignments",
            icon: "fas fa-user-tag"
        ).RequirePermissions(KnowPulsePermissions.Organization.UserAssignments));

        context.Menu.AddItem(organization);

        // 知识库菜单仅表示用户拥有进入该功能区的权限；
        // 每个知识文档是否可见还会在 KnowledgeItemAppService 中按权限范围二次过滤。
        var knowledgeBase = new ApplicationMenuItem(
            KnowPulseMenus.KnowledgeBase,
            l["Menu:KnowledgeBase"],
            icon: "fas fa-book-open"
        ).RequirePermissions(KnowPulsePermissions.KnowledgeBase.Default);

        knowledgeBase.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.KnowledgeUpload,
            l["Menu:KnowledgeUpload"],
            "/knowledge/upload",
            icon: "fas fa-upload"
        ).RequirePermissions(KnowPulsePermissions.KnowledgeBase.Items));

        knowledgeBase.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.KnowledgeItems,
            l["Menu:KnowledgeItems"],
            "/knowledge",
            icon: "fas fa-file-lines"
        ).RequirePermissions(KnowPulsePermissions.KnowledgeBase.Items));

        knowledgeBase.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.KnowledgeCategories,
            l["Menu:KnowledgeCategories"],
            "/knowledge/categories",
            icon: "fas fa-folder-tree"
        ).RequirePermissions(KnowPulsePermissions.KnowledgeBase.Categories));

        knowledgeBase.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.KnowledgeTags,
            l["Menu:KnowledgeTags"],
            "/knowledge/tags",
            icon: "fas fa-tags"
        ).RequirePermissions(KnowPulsePermissions.KnowledgeBase.Tags));

        knowledgeBase.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.KnowledgePermissionScopes,
            l["Menu:KnowledgePermissionScopes"],
            "/knowledge/permission-scopes",
            icon: "fas fa-shield-halved"
        ).RequirePermissions(KnowPulsePermissions.KnowledgeBase.PermissionScopes));

        knowledgeBase.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.KnowledgeIndexJobs,
            l["Menu:KnowledgeIndexJobs"],
            "/knowledge/index-jobs",
            icon: "fas fa-list-check"
        ).RequirePermissions(KnowPulsePermissions.KnowledgeIndexing.IndexJobs));

        knowledgeBase.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.KnowledgeFeedback,
            l["Menu:KnowledgeFeedback"],
            "/knowledge/feedback",
            icon: "fas fa-comment-dots"
        ).RequirePermissions(KnowPulsePermissions.KnowledgeBase.Feedback));

        context.Menu.AddItem(knowledgeBase);

        var rag = new ApplicationMenuItem(
            KnowPulseMenus.Rag,
            l["Menu:Rag"],
            icon: "fas fa-comments"
        ).RequirePermissions(KnowPulsePermissions.Rag.Default);

        rag.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.RagAnswer,
            l["Menu:RagAnswer"],
            "/rag/answer",
            icon: "fas fa-message"
        ).RequirePermissions(KnowPulsePermissions.Rag.Answer));

        rag.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.AiModels,
            l["Menu:AiModels"],
            "/rag/models",
            icon: "fas fa-sliders"
        ).RequirePermissions(KnowPulsePermissions.AiModels.View));

        context.Menu.AddItem(rag);

        var questionBank = new ApplicationMenuItem(
            KnowPulseMenus.QuestionBank,
            l["Menu:QuestionBank"],
            icon: "fas fa-clipboard-question"
        ).RequirePermissions(KnowPulsePermissions.QuestionBank.Default);

        questionBank.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.Questions,
            l["Menu:Questions"],
            "/question-bank/questions",
            icon: "fas fa-list-check"
        ).RequirePermissions(KnowPulsePermissions.QuestionBank.Questions));

        questionBank.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.AiQuestionCandidates,
            l["Menu:AiQuestionCandidates"],
            "/question-bank/ai-candidates",
            icon: "fas fa-wand-magic-sparkles"
        ).RequirePermissions(KnowPulsePermissions.QuestionBank.AiGeneration));

        context.Menu.AddItem(questionBank);

        var learning = new ApplicationMenuItem(
            KnowPulseMenus.Learning,
            l["Menu:Learning"],
            icon: "fas fa-graduation-cap"
        ).RequirePermissions(KnowPulsePermissions.Learning.View);

        learning.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.MyLearning,
            l["Menu:MyLearning"],
            "/learning/my",
            icon: "fas fa-book-reader"
        ).RequirePermissions(KnowPulsePermissions.Learning.View));

        context.Menu.AddItem(learning);

        var exam = new ApplicationMenuItem(
            KnowPulseMenus.Exam,
            l["Menu:Exam"],
            icon: "fas fa-file-signature"
        ).RequirePermissions(KnowPulsePermissions.Exam.Default);

        exam.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.ExamManagement,
            l["Menu:ExamManagement"],
            "/exam/management",
            icon: "fas fa-clipboard-list"
        ).RequirePermissions(KnowPulsePermissions.Exam.Manage));

        exam.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.ExamTake,
            l["Menu:ExamTake"],
            "/exam/take",
            icon: "fas fa-pen-to-square"
        ).RequirePermissions(KnowPulsePermissions.Exam.Participate));

        context.Menu.AddItem(exam);

        var administration = context.Menu.GetAdministration();

        administration.SetSubItemOrder(TenantManagementMenuNames.GroupName, 1);

        administration.SetSubItemOrder(IdentityMenuNames.GroupName, 2);
        administration.SetSubItemOrder(SettingManagementMenus.GroupName, 3);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 配置用户菜单中的账户管理入口，跳转到认证服务的账户管理页面。
    /// </summary>
    private Task ConfigureUserMenuAsync(MenuConfigurationContext context)
    {
        var accountStringLocalizer = context.GetLocalizer<AccountResource>();

        var authServerUrl = _configuration["AuthServer:Authority"] ?? "";

        context.Menu.AddItem(new ApplicationMenuItem(
            "Account.Manage",
            accountStringLocalizer["MyAccount"],
            $"{authServerUrl.EnsureEndsWith('/')}Account/Manage",
            icon: "fa fa-cog",
            order: 1000,
            target: "_blank").RequireAuthenticated());

        return Task.CompletedTask;
    }
}
