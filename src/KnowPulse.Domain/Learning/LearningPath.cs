using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Learning;

/// <summary>
/// 学习路径聚合根，承载某类岗位、部门或职级应完成的知识和考试要求。
/// 路径本身只保存配置元信息，具体分配范围和内容清单由独立子表保存。
/// </summary>
public class LearningPath : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// 学习路径名称最大长度。
    /// </summary>
    public const int MaxNameLength = 256;

    /// <summary>
    /// 学习路径说明最大长度。
    /// </summary>
    public const int MaxDescriptionLength = 1024;

    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧数据。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 学习路径名称。
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// 学习路径说明。
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// 是否启用。停用后普通用户不再看到该路径，但审计和历史配置仍保留。
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// 配置版本。每次编辑或停用递增，便于后续阅读记录和历史快照关联。
    /// </summary>
    public int ConfigVersion { get; private set; }

    /// <summary>
    /// EF Core 使用的受保护构造函数。
    /// </summary>
    protected LearningPath()
    {
    }

    /// <summary>
    /// 创建学习路径主记录。具体分配范围、必学知识、推荐知识和必考考试由应用服务写入子表。
    /// </summary>
    /// <param name="id">学习路径 ID。</param>
    /// <param name="tenantId">租户 ID；宿主侧数据为空。</param>
    /// <param name="name">学习路径名称。</param>
    /// <param name="description">学习路径说明。</param>
    public LearningPath(Guid id, Guid? tenantId, string name, string? description = null)
        : base(id)
    {
        TenantId = tenantId;
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength);
        Description = Check.Length(description, nameof(description), MaxDescriptionLength);
        IsActive = true;
        ConfigVersion = 1;
    }

    /// <summary>
    /// 修改路径名称和说明，并递增配置版本。
    /// </summary>
    /// <param name="name">新的学习路径名称。</param>
    /// <param name="description">新的学习路径说明。</param>
    public void UpdateDetails(string name, string? description)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength);
        Description = Check.Length(description, nameof(description), MaxDescriptionLength);
        ConfigVersion++;
    }

    /// <summary>
    /// 停用学习路径。重复停用保持幂等，不额外递增版本。
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        ConfigVersion++;
    }
}
