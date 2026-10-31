using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using KnowPulse.Blazor.Client.Components;
using KnowPulse.Localization;
using KnowPulse.Education;
using KnowPulse.MultiTenancy;
using KnowPulse.Permissions;
using Volo.Abp.Account.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.UI.Navigation;

namespace KnowPulse.Blazor.Client.Menus;

/// <summary>
/// Blazor 客户端菜单贡献者。
/// 教师工作区与平台后台使用独立入口；数据可见性继续由后端应用服务判断。
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
    /// 教师导航保留教学入口，获授管理能力的账号另有后台入口。
    /// </summary>
    private async Task ConfigureMainMenuAsync(MenuConfigurationContext context)
    {
        var l = context.GetLocalizer<KnowPulseResource>();

        context.Menu.AddItem(new ApplicationMenuItem("KnowPulse.Teacher.Materials", "我的资料", "/teacher/materials", icon: "fas fa-book-open", order: 0).RequirePermissions(KnowPulsePermissions.KnowledgeBase.Items));
        context.Menu.AddItem(new ApplicationMenuItem("KnowPulse.Teacher.Shared", "共享给我的", "/teacher/shared", icon: "fas fa-share-nodes", order: 1).RequirePermissions(KnowPulsePermissions.KnowledgeBase.Items));
        context.Menu.AddItem(new ApplicationMenuItem("KnowPulse.Teacher.Answer", "资料问答", "/teacher/answer", icon: "fas fa-comments", order: 2).RequirePermissions(KnowPulsePermissions.Rag.Answer));
        context.Menu.AddItem(new ApplicationMenuItem("KnowPulse.Teacher.Questions", "出题与题库", "/teacher/questions", icon: "fas fa-clipboard-question", order: 3).RequirePermissions(KnowPulsePermissions.QuestionBank.Questions));
        context.Menu.AddItem(new ApplicationMenuItem("KnowPulse.Teacher.Students", "我的学生", "/teacher/students", icon: "fas fa-user-group", order: 4).RequirePermissions(KnowPulsePermissions.Exam.Manage));
        context.Menu.AddItem(new ApplicationMenuItem("KnowPulse.Teacher.Tasks", "答题任务", "/teacher/tasks", icon: "fas fa-file-signature", order: 5).RequirePermissions(KnowPulsePermissions.Exam.Manage));
        if (await AdminNavigation.CanEnterAsync(context.ServiceProvider.GetRequiredService<IPermissionChecker>()))
        {
            context.Menu.AddItem(new ApplicationMenuItem("KnowPulse.Administration", "平台管理后台", "/admin", icon: "fas fa-shield-halved", order: 100));
        }
        context.Menu.Items.Remove(context.Menu.GetAdministration());

        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationWorkspace,
            l["EducationWorkspace:Title"], "/education/workspace", icon: "fas fa-school").RequireAuthenticated());
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.WeChatBindings,
            l["WeChat:Title"], "/account/wechat-bindings", icon: "fas fa-link").RequireAuthenticated());
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationAssessmentResults,
            l["EducationGrading:MyResults"], "/education/assessment-results", icon: "fas fa-poll")
            .RequirePermissions(EducationPermissions.AssessmentParticipate));
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationStudentGrowth,
            l["EducationGrowth:Title"], "/education/growth", icon: "fas fa-chart-line")
            .RequirePermissions(EducationPermissions.AssessmentParticipate));
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationTeachingPlans,
            l["EducationPlan:Title"], "/education/teaching-plans", icon: "fas fa-list-check")
            .RequirePermissions(EducationPermissions.TeachingPlanManage, EducationPermissions.AnalysisManage));
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationMyTeachingPlans,
            l["EducationPlan:MyTitle"], "/education/teaching-plans/my", icon: "fas fa-tasks")
            .RequirePermissions(EducationPermissions.TeachingPlanView));
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationStudyTasks,
            l["EducationStudy:Title"], "/education/study-tasks", icon: "fas fa-book-reader")
            .RequirePermissions(EducationPermissions.LearningManage));
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationMyStudyTasks,
            l["EducationStudy:MyTitle"], "/education/study-tasks/my", icon: "fas fa-book-open")
            .RequirePermissions(EducationPermissions.LearningParticipate));
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationCorrections,
            l["EducationCorrection:Title"], "/education/corrections", icon: "fas fa-pen")
            .RequirePermissions(EducationPermissions.AssessmentManage, EducationPermissions.LearningManage));
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationMyCorrections,
            l["EducationCorrection:MyTitle"], "/education/corrections/my", icon: "fas fa-edit")
            .RequirePermissions(EducationPermissions.AssessmentParticipate, EducationPermissions.LearningParticipate));
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationLearningWorkload,
            l["EducationWorkload:Title"], "/education/workload", icon: "fas fa-calendar-alt")
            .RequirePermissions(EducationPermissions.LearningParticipate));
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationNotebook,
            l["EducationNotebook:Title"], "/education/notebook", icon: "fas fa-book")
            .RequirePermissions(KnowPulsePermissions.KnowledgeBase.PersonalLibrary));
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationPractice,
            l["EducationPractice:Title"], "/education/practice", icon: "fas fa-redo")
            .RequirePermissions(EducationPermissions.AssessmentParticipate, EducationPermissions.LearningParticipate));
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationFeedback,
            l["EducationFeedback:Title"], "/education/feedback", icon: "fas fa-comment-medical")
            .RequirePermissions(EducationPermissions.FeedbackManage));
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationLearningReports,
            l["EducationReport:Title"], "/education/learning-reports", icon: "fas fa-chart-bar")
            .RequirePermissions(EducationPermissions.AnalysisManage));
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationAnalysisRules,
            l["EducationReport:RulesTitle"], "/education/analysis-rules", icon: "fas fa-sliders-h")
            .RequirePermissions(EducationPermissions.SchoolManage));
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationMyFeedback,
            l["EducationFeedback:MyFeedback"], "/education/feedback/my", icon: "fas fa-comment-dots")
            .RequirePermissions(EducationPermissions.FeedbackView));
        context.Menu.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.PersonalKnowledgeLibrary,
            l["Menu:PersonalKnowledgeLibrary"],
            "/my-knowledge",
            icon: "fas fa-book-reader"
        ).RequirePermissions(KnowPulsePermissions.KnowledgeBase.PersonalLibrary));
        context.Menu.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.SchoolKnowledge,
            l["Menu:SchoolKnowledge"],
            "/school-knowledge",
            icon: "fas fa-book-open"
        ).RequirePermissions(KnowPulsePermissions.KnowledgeBase.PersonalLibrary));
        context.Menu.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.EducationSchools,
            l["Menu:EducationSchools"],
            "/education/schools",
            icon: "fas fa-school"
        ));
        context.Menu.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.EducationCurriculum,
            l["Menu:EducationCurriculum"],
            "/education/curriculum",
            icon: "fas fa-book"
        ));
        context.Menu.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.EducationPreparations,
            l["Menu:EducationPreparations"],
            "/education/preparations",
            icon: "fas fa-clipboard"
        ).RequirePermissions(KnowPulsePermissions.KnowledgeBase.PersonalLibrary));
        context.Menu.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.EducationPapers,
            l["Menu:EducationPapers"],
            "/education/papers",
            icon: "fas fa-file-alt"
        ).RequirePermissions(KnowPulsePermissions.Exam.Manage));
        context.Menu.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.EducationAssessmentTasks,
            l["Menu:EducationAssessmentTasks"],
            "/education/assessment-tasks",
            icon: "fas fa-tasks"
        ).RequirePermissions(EducationPermissions.AssessmentManage));
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
