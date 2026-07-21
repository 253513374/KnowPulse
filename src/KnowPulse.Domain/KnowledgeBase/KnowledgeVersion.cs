using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识版本聚合根，保存某个知识文档在一次编辑周期中的标题、正文和生命周期状态。
/// 版本状态是知识发布闭环的核心事实，后续索引、RAG 引用和考试题目生成都应以已发布版本为准。
/// </summary>
public class KnowledgeVersion : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// 版本标题最大长度，通常和知识标题一致，但允许每个版本保留独立标题。
    /// </summary>
    public const int MaxTitleLength = 256;

    /// <summary>
    /// MVP-0 内置正文最大长度。附件和后续文档解析内容不应直接塞入该字段。
    /// </summary>
    public const int MaxContentTextLength = 8192;

    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧数据。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 所属知识文档编号。
    /// </summary>
    public Guid KnowledgeItemId { get; private set; }

    /// <summary>
    /// 同一知识文档内递增的版本号，从 1 开始。
    /// </summary>
    public int VersionNumber { get; private set; }

    /// <summary>
    /// 当前版本标题。
    /// </summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// 当前版本正文。MVP-0 用于页面录入，后续大型附件内容应走解析和索引管道。
    /// </summary>
    public string? ContentText { get; private set; }

    /// <summary>
    /// 版本生命周期状态：草稿、已发布、已下架、已归档或已禁用。
    /// </summary>
    public KnowledgeVersionStatus Status { get; private set; }

    /// <summary>
    /// 是否为当前对外发布版本。数据库层通过过滤唯一索引保证同一知识只有一个当前发布版本。
    /// </summary>
    public bool IsCurrentPublished { get; private set; }

    /// <summary>
    /// 最近一次发布时间。
    /// </summary>
    public DateTime? PublishedTime { get; private set; }

    /// <summary>
    /// 最近一次下架时间。
    /// </summary>
    public DateTime? WithdrawnTime { get; private set; }

    /// <summary>
    /// 归档时间。归档后的版本不可再编辑为普通草稿。
    /// </summary>
    public DateTime? ArchivedTime { get; private set; }

    /// <summary>
    /// 禁用时间，预留给后续风控或管理员强制下线场景。
    /// </summary>
    public DateTime? DisabledTime { get; private set; }

    /// <summary>
    /// EF Core 反序列化使用的保护构造函数。
    /// </summary>
    protected KnowledgeVersion()
    {
    }

    /// <summary>
    /// 创建知识版本。版本创建后默认处于草稿态，必须显式发布才会成为可访问版本。
    /// </summary>
    /// <param name="id">版本编号。</param>
    /// <param name="tenantId">所属租户编号。</param>
    /// <param name="knowledgeItemId">所属知识文档编号。</param>
    /// <param name="versionNumber">知识文档内递增版本号，必须大于 0。</param>
    /// <param name="title">版本标题。</param>
    /// <param name="contentText">可选正文。</param>
    public KnowledgeVersion(
        Guid id,
        Guid? tenantId,
        Guid knowledgeItemId,
        int versionNumber,
        string title,
        string? contentText = null)
        : base(id)
    {
        if (versionNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber));
        }

        TenantId = tenantId;
        KnowledgeItemId = knowledgeItemId;
        VersionNumber = versionNumber;
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), MaxTitleLength);
        ContentText = Check.Length(contentText, nameof(contentText), MaxContentTextLength);
        Status = KnowledgeVersionStatus.Draft;
        IsCurrentPublished = false;
    }

    /// <summary>
    /// 更新草稿内容。归档版本禁止再被修改，以保护历史事实和后续引用链路。
    /// </summary>
    public void UpdateDraft(string title, string? contentText)
    {
        if (Status == KnowledgeVersionStatus.Archived)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeVersionAlreadyArchived);
        }

        Title = Check.NotNullOrWhiteSpace(title, nameof(title), MaxTitleLength);
        ContentText = Check.Length(contentText, nameof(contentText), MaxContentTextLength);
    }

    /// <summary>
    /// 发布版本，并将其标记为当前发布版本。
    /// 应用服务会先清理同一知识下其他版本的当前发布标记，再调用该方法。
    /// </summary>
    public void Publish(DateTime now)
    {
        EnsureCanPublish();

        Status = KnowledgeVersionStatus.Published;
        PublishedTime = now;
        WithdrawnTime = null;
        DisabledTime = null;
        IsCurrentPublished = true;
    }

    /// <summary>
    /// 在发布编排开始写入访问策略前校验版本是否允许发布。
    /// 该方法不改变状态，供应用服务执行无副作用的发布预检。
    /// </summary>
    public void EnsureCanPublish()
    {
        if (Status == KnowledgeVersionStatus.Archived)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeVersionAlreadyArchived);
        }
    }

    /// <summary>
    /// 下架已发布版本。只有已发布状态允许下架，避免草稿或归档版本产生错误生命周期。
    /// </summary>
    public void Withdraw(DateTime now)
    {
        if (Status != KnowledgeVersionStatus.Published)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeVersionNotPublished);
        }

        Status = KnowledgeVersionStatus.Withdrawn;
        WithdrawnTime = now;
        IsCurrentPublished = false;
    }

    /// <summary>
    /// 归档版本。归档用于保留历史，同时明确该版本不再参与当前发布。
    /// </summary>
    public void Archive(DateTime now)
    {
        Status = KnowledgeVersionStatus.Archived;
        ArchivedTime = now;
        IsCurrentPublished = false;
    }

    /// <summary>
    /// 禁用版本，预留给违规内容、质量问题或管理员强制停用场景。
    /// </summary>
    public void Disable(DateTime now)
    {
        Status = KnowledgeVersionStatus.Disabled;
        DisabledTime = now;
        IsCurrentPublished = false;
    }

    /// <summary>
    /// 清除当前发布标记。用于发布新版本前解除旧版本的唯一发布标记。
    /// </summary>
    public void ClearCurrentPublished()
    {
        IsCurrentPublished = false;
    }
}
