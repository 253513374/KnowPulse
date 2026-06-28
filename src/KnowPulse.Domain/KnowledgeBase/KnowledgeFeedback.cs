using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识反馈聚合根，记录用户对知识内容的问题、过期、错误、缺失和建议。
/// 反馈处理只维护纠错工单状态和关联修订版本，不直接修改已发布知识内容。
/// </summary>
public class KnowledgeFeedback : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// 反馈标题最大长度。
    /// </summary>
    public const int MaxTitleLength = 256;

    /// <summary>
    /// 反馈说明最大长度。
    /// </summary>
    public const int MaxDescriptionLength = 2048;

    /// <summary>
    /// 处理结论最大长度。
    /// </summary>
    public const int MaxResolutionLength = 2048;

    /// <summary>
    /// 关闭原因最大长度。
    /// </summary>
    public const int MaxCloseReasonLength = 1024;

    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧数据。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 反馈来源知识文档编号。
    /// </summary>
    public Guid KnowledgeItemId { get; private set; }

    /// <summary>
    /// 反馈来源知识版本编号；缺失类反馈可以不绑定具体版本。
    /// </summary>
    public Guid? KnowledgeVersionId { get; private set; }

    /// <summary>
    /// 提交反馈的用户编号。
    /// </summary>
    public Guid SubmitterUserId { get; private set; }

    /// <summary>
    /// 反馈类型。
    /// </summary>
    public KnowledgeFeedbackType Type { get; private set; }

    /// <summary>
    /// 反馈处理状态。
    /// </summary>
    public KnowledgeFeedbackStatus Status { get; private set; }

    /// <summary>
    /// 反馈标题。
    /// </summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// 反馈详细说明。
    /// </summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>
    /// 最近处理人编号。
    /// </summary>
    public Guid? HandlerUserId { get; private set; }

    /// <summary>
    /// 最近处理时间。
    /// </summary>
    public DateTime? HandledTime { get; private set; }

    /// <summary>
    /// 处理结论或处理备注。
    /// </summary>
    public string? Resolution { get; private set; }

    /// <summary>
    /// 管理员关联的修订版本编号。
    /// </summary>
    public Guid? LinkedKnowledgeVersionId { get; private set; }

    /// <summary>
    /// 关闭时间。
    /// </summary>
    public DateTime? ClosedTime { get; private set; }

    /// <summary>
    /// 关闭原因。
    /// </summary>
    public string? CloseReason { get; private set; }

    /// <summary>
    /// EF Core 反序列化使用的保护构造函数。
    /// </summary>
    protected KnowledgeFeedback()
    {
    }

    /// <summary>
    /// 创建知识反馈，初始状态固定为已提交。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <param name="tenantId">租户 ID；宿主侧数据为空。</param>
    /// <param name="knowledgeItemId">反馈来源知识条目 ID。</param>
    /// <param name="knowledgeVersionId">反馈来源知识版本 ID。</param>
    /// <param name="submitterUserId">提交反馈的用户 ID。</param>
    /// <param name="type">反馈类型。</param>
    /// <param name="title">反馈标题。</param>
    /// <param name="description">反馈详细说明。</param>
    public KnowledgeFeedback(
        Guid id,
        Guid? tenantId,
        Guid knowledgeItemId,
        Guid? knowledgeVersionId,
        Guid submitterUserId,
        KnowledgeFeedbackType type,
        string title,
        string description)
        : base(id)
    {
        TenantId = tenantId;
        KnowledgeItemId = knowledgeItemId;
        KnowledgeVersionId = knowledgeVersionId;
        SubmitterUserId = submitterUserId;
        Type = type;
        Status = KnowledgeFeedbackStatus.Submitted;
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), MaxTitleLength);
        Description = Check.NotNullOrWhiteSpace(description, nameof(description), MaxDescriptionLength);
    }

    /// <summary>
    /// 管理员开始处理反馈。
    /// </summary>
    /// <param name="handlerUserId">处理人用户 ID。</param>
    /// <param name="now">处理时间。</param>
    /// <param name="resolution">初步处理备注。</param>
    /// <exception cref="BusinessException">反馈已关闭或已处理时抛出。</exception>
    public void StartProcessing(Guid handlerUserId, DateTime now, string? resolution = null)
    {
        if (Status == KnowledgeFeedbackStatus.Closed || Status == KnowledgeFeedbackStatus.Resolved)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeFeedbackCannotProcess);
        }

        Status = KnowledgeFeedbackStatus.Processing;
        SetHandler(handlerUserId, now, resolution);
    }

    /// <summary>
    /// 关联修订版本。该操作只记录关联关系，不修改或发布知识版本。
    /// </summary>
    /// <param name="handlerUserId">处理人用户 ID。</param>
    /// <param name="now">处理时间。</param>
    /// <param name="linkedKnowledgeVersionId">关联的修订版本 ID。</param>
    /// <param name="resolution">关联版本时的处理备注。</param>
    /// <exception cref="BusinessException">反馈已关闭时抛出。</exception>
    public void LinkKnowledgeVersion(Guid handlerUserId, DateTime now, Guid linkedKnowledgeVersionId, string? resolution = null)
    {
        EnsureNotClosed();

        if (Status == KnowledgeFeedbackStatus.Submitted)
        {
            Status = KnowledgeFeedbackStatus.Processing;
        }

        LinkedKnowledgeVersionId = linkedKnowledgeVersionId;
        SetHandler(handlerUserId, now, resolution);
    }

    /// <summary>
    /// 标记反馈已处理，可同时绑定修订版本。
    /// </summary>
    /// <param name="handlerUserId">处理人用户 ID。</param>
    /// <param name="now">处理时间。</param>
    /// <param name="resolution">处理结论。</param>
    /// <param name="linkedKnowledgeVersionId">可选关联修订版本 ID。</param>
    /// <exception cref="BusinessException">反馈已关闭或当前状态不允许完成时抛出。</exception>
    public void Resolve(Guid handlerUserId, DateTime now, string resolution, Guid? linkedKnowledgeVersionId = null)
    {
        EnsureNotClosed();

        if (Status != KnowledgeFeedbackStatus.Submitted && Status != KnowledgeFeedbackStatus.Processing)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeFeedbackCannotResolve);
        }

        Status = KnowledgeFeedbackStatus.Resolved;
        if (linkedKnowledgeVersionId.HasValue)
        {
            LinkedKnowledgeVersionId = linkedKnowledgeVersionId.Value;
        }

        SetHandler(handlerUserId, now, Check.NotNullOrWhiteSpace(resolution, nameof(resolution), MaxResolutionLength));
    }

    /// <summary>
    /// 关闭反馈。关闭后的反馈不能再处理或关联版本。
    /// </summary>
    /// <param name="handlerUserId">处理人用户 ID。</param>
    /// <param name="now">关闭时间。</param>
    /// <param name="closeReason">关闭原因。</param>
    /// <exception cref="BusinessException">反馈已关闭时抛出。</exception>
    public void Close(Guid handlerUserId, DateTime now, string? closeReason = null)
    {
        EnsureNotClosed();

        Status = KnowledgeFeedbackStatus.Closed;
        ClosedTime = now;
        CloseReason = Check.Length(closeReason, nameof(closeReason), MaxCloseReasonLength);
        SetHandler(handlerUserId, now, Resolution);
    }

    /// <summary>
    /// 校验反馈未关闭。
    /// </summary>
    /// <exception cref="BusinessException">反馈已关闭时抛出。</exception>
    private void EnsureNotClosed()
    {
        if (Status == KnowledgeFeedbackStatus.Closed)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeFeedbackClosed);
        }
    }

    /// <summary>
    /// 写入最近处理人、处理时间和处理备注。
    /// </summary>
    /// <param name="handlerUserId">处理人用户 ID。</param>
    /// <param name="now">处理时间。</param>
    /// <param name="resolution">处理备注或处理结论。</param>
    private void SetHandler(Guid handlerUserId, DateTime now, string? resolution)
    {
        HandlerUserId = handlerUserId;
        HandledTime = now;
        Resolution = Check.Length(resolution, nameof(resolution), MaxResolutionLength);
    }
}
