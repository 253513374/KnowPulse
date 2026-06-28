namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 知识索引任务生命周期状态。
/// 状态值会持久化到数据库和 API 响应中，追加新值时应保持既有数值稳定。
/// </summary>
public enum KnowledgeIndexJobStatus
{
    /// <summary>
    /// 已创建，等待后台 worker 或管理员手动开始。
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 正在读取、分块、生成 embedding 或写入向量库。
    /// </summary>
    Processing = 1,

    /// <summary>
    /// 当前知识版本已经完成索引。
    /// </summary>
    Indexed = 2,

    /// <summary>
    /// 索引失败，FailureStage 和 FailureReason 中保存排障摘要。
    /// </summary>
    Failed = 3,

    /// <summary>
    /// 任务结果已被版本撤回、归档、权限变化或重新索引作废。
    /// </summary>
    Outdated = 4,

    /// <summary>
    /// 索引结果已被删除或对应知识版本已被删除。
    /// </summary>
    Deleted = 5
}
