using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识分类聚合根，用于组织知识文档的业务目录。
/// 分类支持父子关系，但当前实体只保存父级编号，树形约束和排序可在后续迭代补充。
/// </summary>
public class KnowledgeCategory : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// 分类编码最大长度，编码用于稳定排序和外部导入对齐。
    /// </summary>
    public const int MaxCodeLength = 64;

    /// <summary>
    /// 分类名称最大长度。
    /// </summary>
    public const int MaxNameLength = 128;

    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧数据。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 父级分类编号。为空表示一级分类。
    /// </summary>
    public Guid? ParentId { get; private set; }

    /// <summary>
    /// 分类编码，用于列表排序和批量导入时的业务识别。
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// 分类名称，用于页面展示。
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// 是否启用。停用分类不会自动停用分类下的知识文档。
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// EF Core 反序列化使用的保护构造函数。
    /// </summary>
    protected KnowledgeCategory()
    {
    }

    /// <summary>
    /// 创建知识分类。
    /// </summary>
    public KnowledgeCategory(Guid id, Guid? tenantId, string code, string name, Guid? parentId = null)
        : base(id)
    {
        TenantId = tenantId;
        ParentId = parentId;
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), MaxCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength);
        IsActive = true;
    }

    /// <summary>
    /// 修改分类名称。
    /// </summary>
    public void Rename(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength);
    }

    /// <summary>
    /// 修改分类编码。
    /// </summary>
    public void ChangeCode(string code)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), MaxCodeLength);
    }

    /// <summary>
    /// 移动分类到新的父级；传入空值表示移动到根级。
    /// </summary>
    public void MoveTo(Guid? parentId)
    {
        ParentId = parentId;
    }

    /// <summary>
    /// 启用或停用分类。
    /// </summary>
    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }
}
