namespace KnowPulse.Blazor.Client.Menus;

/// <summary>
/// Blazor 菜单名称常量，集中维护组织、知识库和 RAG 页面在 ABP 菜单系统中的唯一标识。
/// </summary>
public class KnowPulseMenus
{
    public const string WeChatBindings = Prefix + ".Account.WeChatBindings";
    private const string Prefix = "KnowPulse";

    /// <summary>
    /// 首页菜单项名称。
    /// </summary>
    public const string Home = Prefix + ".Home";

    /// <summary>
    /// 组织管理菜单分组名称。
    /// </summary>
    public const string Organization = Prefix + ".Organization";

    /// <summary>
    /// 部门管理页面菜单项名称。
    /// </summary>
    public const string Departments = Organization + ".Departments";

    /// <summary>
    /// 岗位管理页面菜单项名称。
    /// </summary>
    public const string Positions = Organization + ".Positions";

    /// <summary>
    /// 职级管理页面菜单项名称。
    /// </summary>
    public const string JobLevels = Organization + ".JobLevels";

    /// <summary>
    /// 用户组织归属页面菜单项名称。
    /// </summary>
    public const string UserAssignments = Organization + ".UserAssignments";

    /// <summary>
    /// 知识库管理菜单分组名称。
    /// </summary>
    public const string KnowledgeBase = Prefix + ".KnowledgeBase";

    public const string PersonalKnowledgeLibrary = Prefix + ".PersonalKnowledgeLibrary";



    /// <summary>
    /// 知识上传引导页面菜单项名称。
    /// </summary>
    public const string KnowledgeUpload = KnowledgeBase + ".Upload";

    /// <summary>
    /// 知识文档管理页面菜单项名称。
    /// </summary>
    public const string KnowledgeItems = KnowledgeBase + ".Items";

    /// <summary>
    /// 知识分类管理页面菜单项名称。
    /// </summary>
    public const string KnowledgeCategories = KnowledgeBase + ".Categories";

    /// <summary>
    /// 知识标签管理页面菜单项名称。
    /// </summary>
    public const string KnowledgeTags = KnowledgeBase + ".Tags";

    /// <summary>
    /// 知识权限范围管理页面菜单项名称。
    /// </summary>
    public const string KnowledgePermissionScopes = KnowledgeBase + ".PermissionScopes";

    /// <summary>
    /// 知识索引任务页面菜单项名称。
    /// </summary>
    public const string KnowledgeIndexJobs = KnowledgeBase + ".IndexJobs";

    /// <summary>
    /// 知识反馈处理页面菜单项名称。
    /// </summary>
    public const string KnowledgeFeedback = KnowledgeBase + ".Feedback";

    /// <summary>
    /// RAG 功能菜单分组名称。
    /// </summary>
    public const string Rag = Prefix + ".Rag";

    /// <summary>
    /// RAG 问答页面菜单项名称。
    /// </summary>
    public const string RagAnswer = Rag + ".Answer";

    /// <summary>
    /// RAG 模型管理页面菜单项名称。
    /// </summary>
    public const string AiModels = Rag + ".AiModels";

    /// <summary>
    /// 题库菜单分组名称。
    /// </summary>
    public const string QuestionBank = Prefix + ".QuestionBank";

    /// <summary>
    /// 题库工作台菜单项名称。
    /// </summary>
    public const string QuestionBankWorkbench = QuestionBank + ".Workbench";

    /// <summary>
    /// 题目草稿页面菜单项名称。
    /// </summary>
    public const string Questions = QuestionBank + ".Questions";

    /// <summary>
    /// 题目审核队列菜单项名称。
    /// </summary>
    public const string QuestionReviews = QuestionBank + ".Reviews";

    /// <summary>
    /// 正式题库菜单项名称。
    /// </summary>
    public const string FormalQuestions = QuestionBank + ".FormalQuestions";

    /// <summary>
    /// AI 候选题页面菜单项名称。
    /// </summary>
    public const string AiQuestionCandidates = QuestionBank + ".AiQuestionCandidates";

    /// <summary>
    /// 学习菜单分组名称。
    /// </summary>
    public const string Learning = Prefix + ".Learning";

    /// <summary>
    /// 个人学习中心页面菜单项名称。
    /// </summary>
    public const string MyLearning = Learning + ".MyLearning";

    /// <summary>
    /// 考试菜单分组名称。
    /// </summary>
    public const string Exam = Prefix + ".Exam";

    /// <summary>
    /// 考试管理页面菜单项名称。
    /// </summary>
    public const string ExamManagement = Exam + ".Management";

    /// <summary>
    /// 在线答题页面菜单项名称。
    /// </summary>
    public const string ExamTake = Exam + ".Take";
}
