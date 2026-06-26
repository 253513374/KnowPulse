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
        knowledgeBase.AddChild(KnowPulsePermissions.KnowledgeBase.Categories, L("Permission:KnowledgeBase.Categories"));
        knowledgeBase.AddChild(KnowPulsePermissions.KnowledgeBase.Tags, L("Permission:KnowledgeBase.Tags"));
        knowledgeBase.AddChild(KnowPulsePermissions.KnowledgeBase.Items, L("Permission:KnowledgeBase.Items"));
        knowledgeBase.AddChild(KnowPulsePermissions.KnowledgeBase.Versions, L("Permission:KnowledgeBase.Versions"));
        knowledgeBase.AddChild(KnowPulsePermissions.KnowledgeBase.Attachments, L("Permission:KnowledgeBase.Attachments"));
        knowledgeBase.AddChild(KnowPulsePermissions.KnowledgeBase.PermissionScopes, L("Permission:KnowledgeBase.PermissionScopes"));
    }

    /// <summary>
    /// 创建 KnowPulse 本地化资源键，避免权限定义中散落资源类型。
    /// </summary>
    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<KnowPulseResource>(name);
    }
}
