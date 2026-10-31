using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using KnowPulse.Localization;
using KnowPulse.Education;
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

        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.EducationWorkspace,
            l["EducationWorkspace:Title"], "/education/workspace", icon: "fas fa-school").RequireAuthenticated());
        context.Menu.AddItem(new ApplicationMenuItem(KnowPulseMenus.WeChatBindings,
            l["WeChat:Title"], "/account/wechat-bindings", icon: "fas fa-link").RequireAuthenticated());

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
            KnowPulseMenus.QuestionBankWorkbench,
            l["Menu:QuestionBankWorkbench"],
            "/question-bank",
            icon: "fas fa-table-columns"
        ).RequirePermissions(KnowPulsePermissions.QuestionBank.Questions));

        questionBank.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.Questions,
            l["Menu:QuestionDrafts"],
            "/question-bank/drafts",
            icon: "fas fa-pen-to-square"
        ).RequirePermissions(KnowPulsePermissions.QuestionBank.Questions));

        questionBank.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.QuestionReviews,
            l["Menu:QuestionReviews"],
            "/question-bank/reviews",
            icon: "fas fa-clipboard-check"
        ).RequirePermissions(KnowPulsePermissions.QuestionBank.Reviews));

        questionBank.AddItem(new ApplicationMenuItem(
            KnowPulseMenus.FormalQuestions,
            l["Menu:FormalQuestions"],
            "/question-bank/formal",
            icon: "fas fa-book"
        ).RequirePermissions(KnowPulsePermissions.QuestionBank.Questions));

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
