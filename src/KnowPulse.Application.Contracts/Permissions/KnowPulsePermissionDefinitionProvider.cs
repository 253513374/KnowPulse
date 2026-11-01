using KnowPulse.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace KnowPulse.Permissions;

/// <summary>
/// ABP 权限定义提供者，负责把 KnowPulse 自定义权限注册到权限系统。
/// 这里只定义功能权限树；知识文档级别的可见范围由业务表 KnowledgePermissionScope 管理。
/// </summary>
public class KnowPulsePermissionDefinitionProvider : PermissionDefinitionProvider
{
    /// <summary>
    /// 注册组织管理和知识库管理权限，并配置本地化显示文本。
    /// </summary>
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(KnowPulsePermissions.GroupName);

        // 根权限用于控制菜单分组，子权限用于控制具体页面和应用服务入口。
        var organization = myGroup.AddPermission(KnowPulsePermissions.Organization.Default, L("Permission:Organization"));
        organization.AddChild(KnowPulsePermissions.Organization.Departments, L("Permission:Organization.Departments"));
        organization.AddChild(KnowPulsePermissions.Organization.Positions, L("Permission:Organization.Positions"));
        organization.AddChild(KnowPulsePermissions.Organization.JobLevels, L("Permission:Organization.JobLevels"));
        organization.AddChild(KnowPulsePermissions.Organization.UserAssignments, L("Permission:Organization.UserAssignments"));

        var knowledgeBase = myGroup.AddPermission(KnowPulsePermissions.KnowledgeBase.Default, L("Permission:KnowledgeBase"));
        knowledgeBase.AddChild(KnowPulsePermissions.KnowledgeBase.PersonalLibrary, L("Permission:KnowledgeBase.PersonalLibrary"));
        knowledgeBase.AddChild(KnowPulsePermissions.KnowledgeBase.Categories, L("Permission:KnowledgeBase.Categories"));
        knowledgeBase.AddChild(KnowPulsePermissions.KnowledgeBase.Tags, L("Permission:KnowledgeBase.Tags"));
        knowledgeBase.AddChild(KnowPulsePermissions.KnowledgeBase.Items, L("Permission:KnowledgeBase.Items"));
        knowledgeBase.AddChild(KnowPulsePermissions.KnowledgeBase.Versions, L("Permission:KnowledgeBase.Versions"));
        knowledgeBase.AddChild(KnowPulsePermissions.KnowledgeBase.Attachments, L("Permission:KnowledgeBase.Attachments"));
        knowledgeBase.AddChild(KnowPulsePermissions.KnowledgeBase.PermissionScopes, L("Permission:KnowledgeBase.PermissionScopes"));
        knowledgeBase.AddChild(KnowPulsePermissions.KnowledgeBase.Feedback, L("Permission:KnowledgeBase.Feedback"));

        var knowledgeIndexing = myGroup.AddPermission(KnowPulsePermissions.KnowledgeIndexing.Default, L("Permission:KnowledgeIndexing"));
        knowledgeIndexing.AddChild(KnowPulsePermissions.KnowledgeIndexing.IndexJobs, L("Permission:KnowledgeIndexing.IndexJobs"));

        var rag = myGroup.AddPermission(KnowPulsePermissions.Rag.Default, L("Permission:Rag"));
        rag.AddChild(KnowPulsePermissions.Rag.Search, L("Permission:Rag.Search"));
        rag.AddChild(KnowPulsePermissions.Rag.Answer, L("Permission:Rag.Answer"));
        rag.AddChild(KnowPulsePermissions.Rag.AuditLogs, L("Permission:Rag.AuditLogs"));
        rag.AddChild(KnowPulsePermissions.Rag.Evaluations, L("Permission:Rag.Evaluations"));

        var aiModels = myGroup.AddPermission(KnowPulsePermissions.AiModels.Default, L("Permission:AiModels"));
        aiModels.AddChild(KnowPulsePermissions.AiModels.View, L("Permission:AiModels.View"));
        aiModels.AddChild(KnowPulsePermissions.AiModels.Manage, L("Permission:AiModels.Manage"));
        aiModels.AddChild(KnowPulsePermissions.AiModels.Test, L("Permission:AiModels.Test"));
        aiModels.AddChild(KnowPulsePermissions.AiModels.SwitchDefaults, L("Permission:AiModels.SwitchDefaults"));

        var questionBank = myGroup.AddPermission(KnowPulsePermissions.QuestionBank.Default, L("Permission:QuestionBank"));
        questionBank.AddChild(KnowPulsePermissions.QuestionBank.Questions, L("Permission:QuestionBank.Questions"));
        questionBank.AddChild(KnowPulsePermissions.QuestionBank.Reviews, L("Permission:QuestionBank.Reviews"));
        questionBank.AddChild(KnowPulsePermissions.QuestionBank.AiGeneration, L("Permission:QuestionBank.AiGeneration"));
        questionBank.AddChild(KnowPulsePermissions.QuestionBank.PromptTemplates, L("Permission:QuestionBank.PromptTemplates"));

        var exam = myGroup.AddPermission(KnowPulsePermissions.Exam.Default, L("Permission:Exam"));
        exam.AddChild(KnowPulsePermissions.Exam.Manage, L("Permission:Exam.Manage"));
        exam.AddChild(KnowPulsePermissions.Exam.Participate, L("Permission:Exam.Participate"));
        exam.AddChild(KnowPulsePermissions.Exam.Results, L("Permission:Exam.Results"));

        var learning = myGroup.AddPermission(KnowPulsePermissions.Learning.Default, L("Permission:Learning"));
        learning.AddChild(KnowPulsePermissions.Learning.Manage, L("Permission:Learning.Manage"));
        learning.AddChild(KnowPulsePermissions.Learning.View, L("Permission:Learning.View"));
    }

    /// <summary>
    /// 创建 KnowPulse 本地化资源键，避免权限定义中散落资源类型。
    /// </summary>
    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<KnowPulseResource>(name);
    }
}
