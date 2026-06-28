using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// Embedding 模型版本配置。维度、collection 和模型名必须作为业务事实保存，
/// 以便后续重建索引和灰度切换时能解释每条向量记录的来源。
/// </summary>
public class EmbeddingModelVersion : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxProviderLength = 64;
    public const int MaxModelNameLength = 128;
    public const int MaxModelVersionLength = 64;
    public const int MaxCollectionNameLength = 128;
    public const int MaxDescriptionLength = 512;

    public Guid? TenantId { get; protected set; }

    public string Provider { get; private set; } = string.Empty;

    public string ModelName { get; private set; } = string.Empty;

    public string ModelVersion { get; private set; } = string.Empty;

    public int VectorDimension { get; private set; }

    public string CollectionName { get; private set; } = string.Empty;

    public bool IsDefault { get; private set; }

    public bool IsActive { get; private set; }

    public string? Description { get; private set; }

    /// <summary>
    /// EF Core 使用的受保护构造函数。
    /// </summary>
    protected EmbeddingModelVersion()
    {
    }

    /// <summary>
    /// 创建一个可追踪的 embedding 模型版本。
    /// 维度和 collection 一旦落库就代表历史索引事实，后续模型升级应创建新版本。
    /// </summary>
    public EmbeddingModelVersion(
        Guid id,
        Guid? tenantId,
        string provider,
        string modelName,
        string modelVersion,
        int vectorDimension,
        string collectionName,
        bool isDefault = false,
        string? description = null)
        : base(id)
    {
        if (vectorDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vectorDimension));
        }

        TenantId = tenantId;
        Provider = Check.NotNullOrWhiteSpace(provider, nameof(provider), MaxProviderLength);
        ModelName = Check.NotNullOrWhiteSpace(modelName, nameof(modelName), MaxModelNameLength);
        ModelVersion = Check.NotNullOrWhiteSpace(modelVersion, nameof(modelVersion), MaxModelVersionLength);
        VectorDimension = vectorDimension;
        CollectionName = Check.NotNullOrWhiteSpace(collectionName, nameof(collectionName), MaxCollectionNameLength);
        Description = Check.Length(description, nameof(description), MaxDescriptionLength);
        IsDefault = isDefault;
        IsActive = true;
    }

    /// <summary>
    /// 将该模型版本设为当前默认版本，同时确保它处于可用状态。
    /// </summary>
    public void MarkAsDefault()
    {
        IsDefault = true;
        IsActive = true;
    }

    /// <summary>
    /// 清除默认标记，但不禁用该版本；适用于切换默认模型后的历史版本保留。
    /// </summary>
    public void ClearDefault()
    {
        IsDefault = false;
    }

    /// <summary>
    /// 停用模型版本。停用后不能继续作为默认版本，但历史 embedding 记录仍可解释来源。
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
        IsDefault = false;
    }

    /// <summary>
    /// 更新模型版本说明，长度受聚合约束控制，避免配置备注无限增长。
    /// </summary>
    public void UpdateDescription(string? description)
    {
        Description = Check.Length(description, nameof(description), MaxDescriptionLength);
    }
}
