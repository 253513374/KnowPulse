using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.RagAI;

/// <summary>
/// RAG 评测运行记录。保存一次可重复评测的聚合指标，越权召回会被标记为硬失败。
/// </summary>
public class RagEvaluationRun : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// 评测运行名称最大长度。
    /// </summary>
    public const int MaxNameLength = 256;

    /// <summary>
    /// 失败原因最大长度。
    /// </summary>
    public const int MaxFailureReasonLength = 1024;

    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧评测运行。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    public Guid? PersonalOwnerUserId { get; private set; }

    public void RestrictToPersonalOwner(Guid ownerUserId)
    {
        if (ownerUserId == Guid.Empty || (PersonalOwnerUserId.HasValue && PersonalOwnerUserId != ownerUserId))
        {
            throw new ArgumentException("A personal evaluation run cannot change its owner.", nameof(ownerUserId));
        }

        PersonalOwnerUserId = ownerUserId;
    }

    /// <summary>
    /// 评测运行名称。
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// 触发评测的用户 ID。
    /// </summary>
    public Guid? OperatorUserId { get; private set; }

    /// <summary>
    /// 评测运行状态。
    /// </summary>
    public RagEvaluationRunStatus Status { get; private set; }

    /// <summary>
    /// 评测开始时间。
    /// </summary>
    public DateTime StartedTime { get; private set; }

    /// <summary>
    /// 评测完成时间。
    /// </summary>
    public DateTime? CompletedTime { get; private set; }

    /// <summary>
    /// 本次运行的用例总数。
    /// </summary>
    public int TotalCaseCount { get; private set; }

    /// <summary>
    /// 通过用例数。
    /// </summary>
    public int PassedCaseCount { get; private set; }

    /// <summary>
    /// 失败用例数。
    /// </summary>
    public int FailedCaseCount { get; private set; }

    /// <summary>
    /// 命中率，按需要回答的问题计算。
    /// </summary>
    public double HitRate { get; private set; }

    /// <summary>
    /// 引用准确率，按实际 citation 中匹配期望引用的比例计算。
    /// </summary>
    public double CitationAccuracyRate { get; private set; }

    /// <summary>
    /// 无答案拒答率，按期望拒答用例中实际拒答的比例计算。
    /// </summary>
    public double NoAnswerRejectionRate { get; private set; }

    /// <summary>
    /// 越权召回率，按实际 citation 中命中禁止引用的比例计算。
    /// </summary>
    public double UnauthorizedRecallRate { get; private set; }

    /// <summary>
    /// 越权召回 citation 数量。
    /// </summary>
    public int UnauthorizedRecallCount { get; private set; }

    /// <summary>
    /// 是否硬失败。只要出现越权召回即为 true。
    /// </summary>
    public bool HardFailed { get; private set; }

    /// <summary>
    /// 失败原因摘要。
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// EF Core 使用的受保护构造函数。
    /// </summary>
    protected RagEvaluationRun()
    {
    }

    /// <summary>
    /// 创建一次 RAG 评测运行。
    /// </summary>
    /// <param name="id">评测运行 ID。</param>
    /// <param name="tenantId">租户 ID；宿主侧数据为空。</param>
    /// <param name="name">运行名称。</param>
    /// <param name="operatorUserId">触发评测的用户 ID。</param>
    /// <param name="startedTime">评测开始时间。</param>
    public RagEvaluationRun(Guid id, Guid? tenantId, string name, Guid? operatorUserId, DateTime startedTime)
        : base(id)
    {
        TenantId = tenantId;
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength);
        OperatorUserId = operatorUserId;
        StartedTime = startedTime;
        Status = RagEvaluationRunStatus.Running;
    }

    /// <summary>
    /// 完成评测运行并写入聚合指标。
    /// </summary>
    /// <param name="completedTime">评测完成时间。</param>
    /// <param name="totalCaseCount">用例总数。</param>
    /// <param name="passedCaseCount">通过用例数。</param>
    /// <param name="failedCaseCount">失败用例数。</param>
    /// <param name="hitRate">命中率。</param>
    /// <param name="citationAccuracyRate">引用准确率。</param>
    /// <param name="noAnswerRejectionRate">无答案拒答率。</param>
    /// <param name="unauthorizedRecallRate">越权召回率。</param>
    /// <param name="unauthorizedRecallCount">越权召回数量。</param>
    /// <param name="hardFailed">是否硬失败。</param>
    /// <param name="failureReason">失败原因摘要。</param>
    public void Complete(
        DateTime completedTime,
        int totalCaseCount,
        int passedCaseCount,
        int failedCaseCount,
        double hitRate,
        double citationAccuracyRate,
        double noAnswerRejectionRate,
        double unauthorizedRecallRate,
        int unauthorizedRecallCount,
        bool hardFailed,
        string? failureReason)
    {
        if (totalCaseCount < 0 || passedCaseCount < 0 || failedCaseCount < 0 || unauthorizedRecallCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCaseCount));
        }

        CompletedTime = completedTime;
        TotalCaseCount = totalCaseCount;
        PassedCaseCount = passedCaseCount;
        FailedCaseCount = failedCaseCount;
        HitRate = CheckRate(hitRate, nameof(hitRate));
        CitationAccuracyRate = CheckRate(citationAccuracyRate, nameof(citationAccuracyRate));
        NoAnswerRejectionRate = CheckRate(noAnswerRejectionRate, nameof(noAnswerRejectionRate));
        UnauthorizedRecallRate = CheckRate(unauthorizedRecallRate, nameof(unauthorizedRecallRate));
        UnauthorizedRecallCount = unauthorizedRecallCount;
        HardFailed = hardFailed;
        FailureReason = Check.Length(failureReason, nameof(failureReason), MaxFailureReasonLength);
        Status = hardFailed || failedCaseCount > 0
            ? RagEvaluationRunStatus.Failed
            : RagEvaluationRunStatus.Passed;
    }

    /// <summary>
    /// 校验并归一化 0 到 1 之间的评测指标。
    /// </summary>
    /// <param name="value">指标值。</param>
    /// <param name="parameterName">参数名称。</param>
    /// <returns>保留六位小数后的指标值。</returns>
    private static double CheckRate(double value, string parameterName)
    {
        if (double.IsNaN(value) || value < 0 || value > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return Math.Round(value, 6);
    }
}
