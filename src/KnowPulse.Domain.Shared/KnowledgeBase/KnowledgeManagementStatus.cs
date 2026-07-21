namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 管理端根据知识版本生命周期和索引任务合成的展示状态。
/// 该状态不持久化，只用于帮助管理员判断知识是否可用于问答以及下一步动作。
/// </summary>
public enum KnowledgeManagementStatus
{
    Draft = 0,
    Publishing = 1,
    Ready = 2,
    NeedsAttention = 3,
    Withdrawn = 4,
    Archived = 5,
    Disabled = 6
}
