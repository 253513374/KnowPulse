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
            KnowPulseMenus.KnowledgeItems,
            l["Menu:KnowledgeItems"],
            "/knowledge/items",
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

        context.Menu.AddItem(knowledgeBase);

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
