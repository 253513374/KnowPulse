using KnowPulse.KnowledgeIndexing;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 将知识条目、当前版本和最新索引任务的独立事实合成为管理端展示状态。
/// </summary>
public static class KnowledgeManagementStatusResolver
{
    /// <summary>
    /// 解析知识管理状态。版本和索引状态仍保留为独立事实，不把合成结果写回数据库。
    /// </summary>
    public static KnowledgeManagementStatus Resolve(
        bool isItemActive,
        KnowledgeVersionStatus? currentVersionStatus,
        KnowledgeVersionStatus? latestVersionStatus,
        KnowledgeIndexJobStatus? latestIndexStatus)
    {
        if (!isItemActive || currentVersionStatus == KnowledgeVersionStatus.Disabled)
        {
            return KnowledgeManagementStatus.Disabled;
        }

        if (currentVersionStatus == KnowledgeVersionStatus.Published)
        {
            return latestIndexStatus switch
            {
                KnowledgeIndexJobStatus.Pending or KnowledgeIndexJobStatus.Processing
                    => KnowledgeManagementStatus.Publishing,
                KnowledgeIndexJobStatus.Indexed
                    => KnowledgeManagementStatus.Ready,
                _ => KnowledgeManagementStatus.NeedsAttention
            };
        }

        return latestVersionStatus switch
        {
            KnowledgeVersionStatus.Withdrawn => KnowledgeManagementStatus.Withdrawn,
            KnowledgeVersionStatus.Archived => KnowledgeManagementStatus.Archived,
            KnowledgeVersionStatus.Disabled => KnowledgeManagementStatus.Disabled,
            _ => KnowledgeManagementStatus.Draft
        };
    }
}
