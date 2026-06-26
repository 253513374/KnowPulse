namespace KnowPulse.Permissions;

/// <summary>
/// KnowPulse 自定义权限常量。
/// 这些权限控制功能入口和应用服务访问；知识文档自身的可见性还会再经过 KnowledgePermissionScope 做业务授权。
/// </summary>
public static class KnowPulsePermissions
{
    /// <summary>
    /// 权限分组名称，对应 ABP 权限管理页面中的 KnowPulse 分组。
    /// </summary>
    public const string GroupName = "KnowPulse";

    /// <summary>
    /// 组织管理权限集合，覆盖部门、岗位、职级和用户组织关系。
    /// </summary>
    public static class Organization
    {
        /// <summary>
        /// 组织管理根权限，用于菜单分组显示。
        /// </summary>
        public const string Default = GroupName + ".Organization";

        /// <summary>
        /// 部门管理权限。
        /// </summary>
        public const string Departments = Default + ".Departments";

        /// <summary>
        /// 岗位管理权限。
        /// </summary>
        public const string Positions = Default + ".Positions";

        /// <summary>
        /// 职级管理权限。
        /// </summary>
        public const string JobLevels = Default + ".JobLevels";

        /// <summary>
        /// 用户组织关系管理权限。
        /// </summary>
        public const string UserAssignments = Default + ".UserAssignments";
    }

    /// <summary>
    /// 知识库管理权限集合，覆盖分类、标签、文档、版本、附件和权限范围。
    /// </summary>
    public static class KnowledgeBase
    {
        /// <summary>
        /// 知识库根权限，用于菜单分组显示。
        /// </summary>
        public const string Default = GroupName + ".KnowledgeBase";

        /// <summary>
        /// 知识分类管理权限。
        /// </summary>
        public const string Categories = Default + ".Categories";

        /// <summary>
        /// 知识标签管理权限。
        /// </summary>
        public const string Tags = Default + ".Tags";

        /// <summary>
        /// 知识文档管理权限。
        /// </summary>
        public const string Items = Default + ".Items";

        /// <summary>
        /// 知识版本管理权限。
        /// </summary>
        public const string Versions = Default + ".Versions";

        /// <summary>
        /// 知识附件管理权限。
        /// </summary>
        public const string Attachments = Default + ".Attachments";

        /// <summary>
        /// 知识权限范围管理权限。
        /// </summary>
        public const string PermissionScopes = Default + ".PermissionScopes";
    }
}
