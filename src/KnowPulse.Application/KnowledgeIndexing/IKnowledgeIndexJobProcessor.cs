using System;
using System.Threading;
using System.Threading.Tasks;

namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 后台 worker 与管理员立即执行入口共享的索引任务处理器。
/// </summary>
public interface IKnowledgeIndexJobProcessor
{
    Task ProcessAsync(Guid knowledgeIndexJobId, bool ignoreRetrySchedule = false, CancellationToken cancellationToken = default);
}
