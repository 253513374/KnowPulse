using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识文档聚合根，表示业务上可被分类、授权和发布版本的知识条目。
/// 文档本身只保存稳定的元信息，正文内容由 <see cref="KnowledgeVersion"/> 承载，
/// 这样可以在保留历史版本的同时明确当前对外发布的版本。
/// </summary>
public partial class KnowledgeItem : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// 标题最大长度，和数据库字段长度、前端表单校验保持一致。
    /// </summary>
    public const int MaxTitleLength = 256;

    /// <summary>
    /// 摘要最大长度，用于列表展示和知识检索摘要，不承载完整正文。
    /// </summary>
    public const int MaxSummaryLength = 1024;

    public const int MaxSubjectNameLength = 128;
    public const int MaxPersonalTags = 20;
    public const int MaxPersonalTagsLength = MaxPersonalTags * (KnowledgeTag.MaxNameLength + 1) + 1;

    public PersonalMaterialType MaterialType { get; private set; }
    public int? MaterialYear { get; private set; }
    /// <summary>本人整理用的学科文字，选择学科不产生任教或学校权限。</summary>
    public string? SubjectName { get; private set; }
    /// <summary>以换行边界保存规范化标签，精确匹配不把子串当作同一标签。</summary>
    public string? PersonalTags { get; private set; }

    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧数据。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 个人知识库编号。与所有者必须同时存在；存量组织知识的两者均为空。
    /// </summary>
    public Guid? LibraryId { get; private set; }

    /// <summary>
    /// 个人资料所有者，不以审计创建者推断，也不能通过更新资料转移。
    /// </summary>
    public Guid? OwnerUserId { get; private set; }

    /// <summary>
    /// 知识分类编号。允许为空，表示该知识暂未归类。
    /// </summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>
    /// 当前发布版本编号。为空表示该知识还没有可对外访问的已发布版本。
    /// </summary>
    public Guid? CurrentPublishedVersionId { get; private set; }

    /// <summary>
    /// 知识文档标题，用于列表、搜索结果和引用来源显示。
    /// </summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// 知识文档摘要，通常由人工维护，后续也可以接入 AI 建议但仍由业务主库保存。
    /// </summary>
    public string? Summary { get; private set; }

    /// <summary>
    /// 是否启用该知识文档。停用后仍保留历史记录和版本数据，便于审计追溯。
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// EF Core 反序列化使用的保护构造函数。
    /// </summary>
    protected KnowledgeItem()
    {
    }

    /// <summary>
    /// 创建一个新的知识文档聚合根。
    /// </summary>
    /// <param name="id">聚合根编号。</param>
    /// <param name="tenantId">所属租户编号。</param>
    /// <param name="title">知识标题，不能为空且不能超过最大长度。</param>
    /// <param name="categoryId">可选分类编号。</param>
    /// <param name="summary">可选摘要。</param>
    public KnowledgeItem(
        Guid id,
        Guid? tenantId,
        string title,
        Guid? categoryId = null,
        string? summary = null,
        Guid? libraryId = null,
        Guid? ownerUserId = null)
        : base(id)
    {
        if (libraryId.HasValue != ownerUserId.HasValue || libraryId == Guid.Empty || ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("Personal knowledge requires both a valid library and its owner.", nameof(libraryId));
        }

        TenantId = tenantId;
        LibraryId = libraryId;
        OwnerUserId = ownerUserId;
        CategoryId = categoryId;
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), MaxTitleLength);
        Summary = Check.Length(summary, nameof(summary), MaxSummaryLength);
        IsActive = true;
    }

    /// <summary>
    /// 修改知识标题，并复用领域层长度和非空校验。
    /// </summary>
    public void Rename(string title)
    {
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), MaxTitleLength);
    }

    /// <summary>
    /// 修改知识摘要。传入空值表示清空摘要。
    /// </summary>
    public void ChangeSummary(string? summary)
    {
        Summary = Check.Length(summary, nameof(summary), MaxSummaryLength);
    }

    /// <summary>
    /// 调整所属分类。传入空值表示移出当前分类。
    /// </summary>
    public void ChangeCategory(Guid? categoryId)
    {
        CategoryId = categoryId;
    }

    public void OrganizePersonalMaterial(PersonalMaterialType materialType, int? materialYear, string? subjectName, IEnumerable<string>? tags)
    {
        if (!LibraryId.HasValue || !OwnerUserId.HasValue) throw new BusinessException(PersonalKnowledgeOrganizationErrorCodes.InvalidOrganization);
        if (!Enum.IsDefined(materialType) || (materialYear.HasValue && materialYear is < 1 or > 9999))
            throw new BusinessException(PersonalKnowledgeOrganizationErrorCodes.InvalidOrganization);
        var subject = string.IsNullOrWhiteSpace(subjectName) ? null : subjectName.Trim();
        if (subject != null && (subject.Length > MaxSubjectNameLength || subject.Any(char.IsControl)))
            throw new BusinessException(PersonalKnowledgeOrganizationErrorCodes.InvalidOrganization);
        var normalizedTags = new List<string>();
        foreach (var rawTag in tags ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(rawTag)) continue;
            var tag = rawTag.Trim();
            if (tag.Length > KnowledgeTag.MaxNameLength || tag.Any(char.IsControl))
                throw new BusinessException(PersonalKnowledgeOrganizationErrorCodes.InvalidOrganization);
            if (!normalizedTags.Contains(tag, StringComparer.Ordinal)) normalizedTags.Add(tag);
            if (normalizedTags.Count > MaxPersonalTags) throw new BusinessException(PersonalKnowledgeOrganizationErrorCodes.InvalidOrganization);
        }
        MaterialType = materialType;
        MaterialYear = materialYear;
        SubjectName = subject;
        PersonalTags = normalizedTags.Count == 0 ? null : "\n" + string.Join("\n", normalizedTags) + "\n";
    }

    public string[] GetPersonalTags() => PersonalTags?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

    /// <summary>
    /// 设置当前发布版本。发布、下架、归档流程都会通过该字段维护列表态的当前版本。
    /// </summary>
    public void SetCurrentPublishedVersion(Guid? versionId)
    {
        CurrentPublishedVersionId = versionId;
    }

    /// <summary>
    /// 启用或停用知识文档。该状态不直接删除内容，避免破坏版本和审计链路。
    /// </summary>
    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }
}
