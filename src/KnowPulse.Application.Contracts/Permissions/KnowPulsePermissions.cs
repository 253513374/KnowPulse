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
        /// 管理当前用户自己的个人知识库，不授予组织资料管理权限。
        /// </summary>
        public const string PersonalLibrary = Default + ".PersonalLibrary";

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

        /// <summary>
        /// 知识反馈处理权限。
        /// </summary>
        public const string Feedback = Default + ".Feedback";
    }

    /// <summary>
    /// 知识索引管理权限集合，覆盖索引任务、重建和状态更新入口。
    /// </summary>
    public static class KnowledgeIndexing
    {
        /// <summary>
        /// 知识索引根权限，用于后续菜单分组和任务管理入口。
        /// </summary>
        public const string Default = GroupName + ".KnowledgeIndexing";

        /// <summary>
        /// 知识索引任务管理权限。
        /// </summary>
        public const string IndexJobs = Default + ".IndexJobs";
    }

    /// <summary>
    /// RAG 问答权限集合，控制搜索和问答 API 入口。
    /// </summary>
    public static class Rag
    {
        /// <summary>
        /// RAG 根权限，用于菜单或功能分组。
        /// </summary>
        public const string Default = GroupName + ".Rag";

        /// <summary>
        /// RAG 检索权限。
        /// </summary>
        public const string Search = Default + ".Search";

        /// <summary>
        /// RAG 问答权限。
        /// </summary>
        public const string Answer = Default + ".Answer";

        /// <summary>
        /// RAG 调用日志查询权限。
        /// </summary>
        public const string AuditLogs = Default + ".AuditLogs";

        /// <summary>
        /// RAG 质量评测权限。
        /// </summary>
        public const string Evaluations = Default + ".Evaluations";
    }

    /// <summary>
    /// AI 模型接入管理权限集合，控制模型目录、健康检查和业务用途默认模型切换。
    /// </summary>
    public static class AiModels
    {
        /// <summary>
        /// 模型管理根权限，用于 RAG 菜单下的模型管理入口。
        /// </summary>
        public const string Default = GroupName + ".AiModels";

        /// <summary>
        /// 查看模型目录和用途绑定。
        /// </summary>
        public const string View = Default + ".View";

        /// <summary>
        /// 新增、编辑、启停模型目录记录。
        /// </summary>
        public const string Manage = Default + ".Manage";

        /// <summary>
        /// 执行模型健康检查。
        /// </summary>
        public const string Test = Default + ".Test";

        /// <summary>
        /// 切换业务用途的默认模型。
        /// </summary>
        public const string SwitchDefaults = Default + ".SwitchDefaults";
    }

    /// <summary>
    /// 题库权限集合，控制题目管理和审核入口。
    /// </summary>
    public static class QuestionBank
    {
        /// <summary>
        /// 题库根权限，用于菜单分组显示。
        /// </summary>
        public const string Default = GroupName + ".QuestionBank";

        /// <summary>
        /// 题目管理权限，覆盖题目 CRUD、提交审核、停用和归档。
        /// </summary>
        public const string Questions = Default + ".Questions";

        /// <summary>
        /// 题目审核权限，覆盖审核通过、驳回和退回修改。
        /// </summary>
        public const string Reviews = Default + ".Reviews";

        /// <summary>
        /// AI 出题权限，覆盖候选题生成、查看、重试和提交审核。
        /// </summary>
        public const string AiGeneration = Default + ".AiGeneration";
    }

    /// <summary>
    /// 考试闭环权限集合，覆盖考试管理、在线答题和成绩查看。
    /// </summary>
    public static class Exam
    {
        /// <summary>
        /// 考试根权限，用于菜单分组显示。
        /// </summary>
        public const string Default = GroupName + ".Exam";

        /// <summary>
        /// 考试管理权限，覆盖考试计划、组卷和发布。
        /// </summary>
        public const string Manage = Default + ".Manage";

        /// <summary>
        /// 考试参加权限，覆盖开始考试、保存答案和提交。
        /// </summary>
        public const string Participate = Default + ".Participate";

        /// <summary>
        /// 成绩查看权限，覆盖成绩和错题记录查询。
        /// </summary>
        public const string Results = Default + ".Results";
    }

    /// <summary>
    /// 学习路径权限集合，控制路径配置和个人学习内容查询。
    /// </summary>
    public static class Learning
    {
        /// <summary>
        /// 学习路径根权限，用于菜单分组或功能分组。
        /// </summary>
        public const string Default = GroupName + ".Learning";

        /// <summary>
        /// 学习路径管理权限，覆盖创建、编辑、查询和停用。
        /// </summary>
        public const string Manage = Default + ".Manage";

        /// <summary>
        /// 个人学习内容查看权限，只能读取分配给当前用户的路径。
        /// </summary>
        public const string View = Default + ".View";
    }
}
