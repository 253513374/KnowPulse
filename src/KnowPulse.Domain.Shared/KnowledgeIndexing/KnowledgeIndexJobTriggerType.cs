namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 知识索引任务触发来源。
/// 该枚举用于审计“为什么要重新生成 chunk / embedding / 向量记录”。
/// </summary>
public enum KnowledgeIndexJobTriggerType
{
    /// <summary>
    /// 知识版本发布后自动创建索引任务。
    /// </summary>
    Publish = 0,

    /// <summary>
    /// 管理员或运维人员手动触发重建。
    /// </summary>
    ManualRebuild = 1,

    /// <summary>
    /// 权限范围变化后触发重建，确保向量 payload 快照不继续使用旧权限。
    /// </summary>
    PermissionChanged = 2,

    /// <summary>
    /// embedding 模型、维度或 collection 变化后触发重建。
    /// </summary>
    ModelChanged = 3,

    /// <summary>
    /// 删除知识或版本时触发向量侧清理。
    /// </summary>
    Delete = 4
}
