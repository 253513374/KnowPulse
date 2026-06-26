using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识附件元数据聚合根。
/// 业务主库只保存文件名、Blob 名称、内容类型和大小，真实文件内容由 ABP BlobStoring/MinIO 保存。
/// </summary>
public class KnowledgeAttachment : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// 原始文件名最大长度，避免用户上传异常长文件名影响数据库和页面显示。
    /// </summary>
    public const int MaxFileNameLength = 256;

    /// <summary>
    /// Blob 对象名称最大长度，包含租户、知识、版本和附件编号路径。
    /// </summary>
    public const int MaxBlobNameLength = 512;

    /// <summary>
    /// Content-Type 最大长度。
    /// </summary>
    public const int MaxContentTypeLength = 128;

    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧数据。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 附件所属知识版本编号。
    /// </summary>
    public Guid KnowledgeVersionId { get; private set; }

    /// <summary>
    /// 归一化后的文件名，用于页面展示和下载文件名。
    /// </summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>
    /// BlobStoring 中的对象名称，用于定位 MinIO 或其他存储中的真实文件。
    /// </summary>
    public string BlobName { get; private set; } = string.Empty;

    /// <summary>
    /// 文件内容类型。未知类型会在应用服务中归一化为 application/octet-stream。
    /// </summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>
    /// 文件大小，单位为字节。
    /// </summary>
    public long Size { get; private set; }

    /// <summary>
    /// EF Core 反序列化使用的保护构造函数。
    /// </summary>
    protected KnowledgeAttachment()
    {
    }

    /// <summary>
    /// 创建知识附件元数据。该构造函数不写入文件内容，文件保存由应用服务先完成。
    /// </summary>
    /// <param name="id">附件编号。</param>
    /// <param name="tenantId">所属租户编号。</param>
    /// <param name="knowledgeVersionId">所属知识版本编号。</param>
    /// <param name="fileName">归一化后的文件名。</param>
    /// <param name="blobName">Blob 对象名称。</param>
    /// <param name="contentType">文件内容类型。</param>
    /// <param name="size">文件大小，不能为负数。</param>
    public KnowledgeAttachment(Guid id, Guid? tenantId, Guid knowledgeVersionId, string fileName, string blobName, string contentType, long size)
        : base(id)
    {
        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        TenantId = tenantId;
        KnowledgeVersionId = knowledgeVersionId;
        FileName = Check.NotNullOrWhiteSpace(fileName, nameof(fileName), MaxFileNameLength);
        BlobName = Check.NotNullOrWhiteSpace(blobName, nameof(blobName), MaxBlobNameLength);
        ContentType = Check.NotNullOrWhiteSpace(contentType, nameof(contentType), MaxContentTypeLength);
        Size = size;
    }
}
