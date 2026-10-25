using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.RagAI;

/// <summary>
/// 知识摄取管道。稳定文本读取由本类直接处理，复杂附件通过项目内转换器隔离 MarkItDown reader，
/// 避免 DataIngestion preview 类型泄漏到业务契约和领域模型。
/// </summary>
[RemoteService(false)]
public class KnowledgeIngestionPipeline : KnowPulseAppService, IKnowledgeIngestionPipeline
{
    // MVP-1 使用简单、可预测的 chunk 策略：目标长度控制检索粒度，少量 overlap 保留跨段上下文。
    // 以后接入 DataIngestion 或更复杂 chunker 时，应继续把输出写回 KnowledgeChunk 事实表。
    private const int TargetChunkLength = 1200;
    private const int ChunkOverlapLength = 120;

    // 直接文本读取保持简单可控；复杂格式由 MarkItDown 转换器负责。
    private static readonly HashSet<string> SupportedAttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".md",
        ".markdown"
    };

    private static readonly HashSet<string> SupportedTextContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/markdown",
        "text/x-markdown",
        "application/markdown"
    };

    private readonly IRepository<KnowledgeItem, Guid> _itemRepository;
    private readonly IRepository<KnowledgeVersion, Guid> _versionRepository;
    private readonly IRepository<KnowledgeAttachment, Guid> _attachmentRepository;
    private readonly IRepository<KnowledgeIndexJob, Guid> _jobRepository;
    private readonly IRepository<KnowledgeChunk, Guid> _chunkRepository;
    private readonly IBlobContainer<KnowledgeAttachmentBlobContainer> _attachmentContainer;
    private readonly IKnowledgeDocumentMarkdownConverter _documentMarkdownConverter;

    /// <summary>
    /// 创建知识摄取管道。
    /// </summary>
    public KnowledgeIngestionPipeline(
        IRepository<KnowledgeItem, Guid> itemRepository,
        IRepository<KnowledgeVersion, Guid> versionRepository,
        IRepository<KnowledgeAttachment, Guid> attachmentRepository,
        IRepository<KnowledgeIndexJob, Guid> jobRepository,
        IRepository<KnowledgeChunk, Guid> chunkRepository,
        IBlobContainer<KnowledgeAttachmentBlobContainer> attachmentContainer,
        IKnowledgeDocumentMarkdownConverter documentMarkdownConverter)
    {
        _itemRepository = itemRepository;
        _versionRepository = versionRepository;
        _attachmentRepository = attachmentRepository;
        _jobRepository = jobRepository;
        _chunkRepository = chunkRepository;
        _attachmentContainer = attachmentContainer;
        _documentMarkdownConverter = documentMarkdownConverter;
    }

    /// <summary>
    /// 摄取指定知识版本，生成可检索 chunk 并更新索引任务状态。
    /// </summary>
    public virtual async Task<KnowledgeIngestionResult> IngestAsync(
        Guid knowledgeVersionId,
        Guid? knowledgeIndexJobId = null,
        bool completeIndexJob = true,
        CancellationToken cancellationToken = default)
    {
        var version = await _versionRepository.GetAsync(knowledgeVersionId, cancellationToken: cancellationToken);
        var item = await _itemRepository.GetAsync(version.KnowledgeItemId, cancellationToken: cancellationToken);
        var job = await GetOrCreateIndexJobAsync(version, knowledgeIndexJobId, cancellationToken);
        if (job.Status != KnowledgeIndexJobStatus.Processing)
        {
            job.Start(DateTime.UtcNow);
        }

        job.ReportProgress("reader", 5, DateTime.UtcNow);
        await _jobRepository.UpdateAsync(job, autoSave: true, cancellationToken: cancellationToken);

        try
        {
            // 摄取流程分成 reader -> processor -> chunker -> persistence。
            // 每个阶段抛出的 KnowledgeIngestionStageException 都会落到索引任务上，方便页面展示失败定位。
            var documents = await ReadDocumentsAsync(version, cancellationToken);
            await ReportJobProgressAsync(job, "reader", 15, cancellationToken);
            var processedDocuments = ProcessDocuments(documents);
            await ReportJobProgressAsync(job, "processor", 25, cancellationToken);
            var chunkDrafts = CreateChunkDrafts(processedDocuments);
            await ReportJobProgressAsync(job, "chunker", 35, cancellationToken);

            var chunkQuery = await _chunkRepository.GetQueryableAsync();
            var existingChunks = await AsyncExecuter.ToListAsync(
                chunkQuery.Where(x => x.KnowledgeIndexJobId == job.Id), cancellationToken);
            if (existingChunks.Any(x => !x.IsActive || x.Sequence >= chunkDrafts.Count ||
                x.ContentHash != ComputeContentHash(chunkDrafts[x.Sequence].Content) ||
                x.SourceTitle != Truncate(chunkDrafts[x.Sequence].SourceTitle, KnowledgeChunk.MaxSourceTitleLength) ||
                x.SourcePage != chunkDrafts[x.Sequence].SourcePage ||
                x.IsPublishedSnapshot != (version.Status == KnowledgeVersionStatus.Published)))
            {
                throw new KnowledgeIngestionStageException("persistence", "Existing chunk content changed or chunks expired; create a new index job.");
            }
            var chunksBySequence = existingChunks.ToDictionary(x => x.Sequence);

            // 同一任务重试复用分块，只有新的重建任务才使历史分块过期。
            await MarkExistingChunksOutdatedAsync(version.Id, job.Id, cancellationToken);
            await ReportJobProgressAsync(job, "persistence", 40, cancellationToken);
            var records = new List<KnowledgeChunkVectorRecord>();
            for (var index = 0; index < chunkDrafts.Count; index++)
            {
                var draft = chunkDrafts[index];
                if (chunksBySequence.TryGetValue(index, out var existingChunk))
                {
                    records.Add(MapToVectorRecord(existingChunk, item, version));
                    continue;
                }
                var chunk = new KnowledgeChunk(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    item.Id,
                    version.Id,
                    job.Id,
                    index,
                    draft.Content,
                    ComputeContentHash(draft.Content),
                    Truncate(draft.SourceTitle, KnowledgeChunk.MaxSourceTitleLength),
                    draft.SourcePage,
                    EstimateTokenCount(draft.Content),
                    isPublishedSnapshot: version.Status == KnowledgeVersionStatus.Published);

                var insertedChunk = await _chunkRepository.InsertAsync(chunk, autoSave: true, cancellationToken: cancellationToken);
                records.Add(MapToVectorRecord(insertedChunk, item, version));
            }

            if (completeIndexJob)
            {
                job.MarkIndexed(DateTime.UtcNow);
            }
            else
            {
                job.ReportProgress("embedding", 60, DateTime.UtcNow);
            }

            await _jobRepository.UpdateAsync(job, autoSave: true, cancellationToken: cancellationToken);

            return new KnowledgeIngestionResult
            {
                KnowledgeVersionId = version.Id,
                Succeeded = true,
                Chunks = records
            };
        }
        catch (KnowledgeIngestionStageException ex)
        {
            await MarkJobFailedAsync(job, ex.Stage, ex.Message, cancellationToken);
            return new KnowledgeIngestionResult
            {
                KnowledgeVersionId = version.Id,
                Succeeded = false,
                FailureStage = ex.Stage,
                FailureReason = ex.Message
            };
        }
        catch (Exception ex)
        {
            await MarkJobFailedAsync(job, "processor", "Unhandled ingestion error: " + ex.GetType().Name, cancellationToken);
            return new KnowledgeIngestionResult
            {
                KnowledgeVersionId = version.Id,
                Succeeded = false,
                FailureStage = "processor",
                FailureReason = "Unhandled ingestion error: " + ex.GetType().Name
            };
        }
    }

    /// <summary>
    /// 获取当前知识版本可复用的待处理任务，必要时创建新的手动重建任务。
    /// </summary>
    private async Task<KnowledgeIndexJob> GetOrCreateIndexJobAsync(
        KnowledgeVersion version,
        Guid? knowledgeIndexJobId,
        CancellationToken cancellationToken)
    {
        if (knowledgeIndexJobId.HasValue)
        {
            var requestedJob = await _jobRepository.GetAsync(
                knowledgeIndexJobId.Value,
                cancellationToken: cancellationToken);
            if (requestedJob.KnowledgeVersionId != version.Id)
            {
                throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeIndexJobVersionMismatch);
            }

            return requestedJob;
        }

        var queryable = await _jobRepository.GetQueryableAsync();
        // 如果已经有待处理/处理中的任务，摄取流程复用它，避免重复点击或后台重试生成多条并行任务。
        var reusableJob = await AsyncExecuter.FirstOrDefaultAsync(
            queryable
                .Where(x => x.KnowledgeVersionId == version.Id)
                .Where(x => x.Status == KnowledgeIndexJobStatus.Pending || x.Status == KnowledgeIndexJobStatus.Processing)
                .OrderByDescending(x => x.CreationTime),
            cancellationToken);

        if (reusableJob != null)
        {
            return reusableJob;
        }

        var job = new KnowledgeIndexJob(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            version.KnowledgeItemId,
            version.Id,
            KnowledgeIndexJobTriggerType.ManualRebuild,
            requestedByUserId: CurrentUser.Id);

        return await _jobRepository.InsertAsync(job, autoSave: true, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 读取知识版本正文和支持的文本附件，并统一转换为摄取文档集合。
    /// </summary>
    private async Task<List<IngestionDocument>> ReadDocumentsAsync(KnowledgeVersion version, CancellationToken cancellationToken)
    {
        var documents = new List<IngestionDocument>();
        // 知识版本正文本身就是一个可索引来源；附件只是补充来源，二者最终统一成 Markdown 文档流。
        if (!string.IsNullOrWhiteSpace(version.ContentText))
        {
            documents.Add(new IngestionDocument(version.Title, null, version.ContentText, IsMarkdown: true));
        }

        var queryable = await _attachmentRepository.GetQueryableAsync();
        var attachments = await AsyncExecuter.ToListAsync(
            queryable.Where(x => x.KnowledgeVersionId == version.Id).OrderBy(x => x.FileName),
            cancellationToken);

        foreach (var attachment in attachments)
        {
            if (IsDirectTextAttachment(attachment))
            {
                documents.Add(await ReadTextAttachmentAsync(attachment, cancellationToken));
                continue;
            }

            if (_documentMarkdownConverter.CanConvert(attachment))
            {
                documents.AddRange(await ReadConvertibleAttachmentAsync(attachment, cancellationToken));
                continue;
            }

            ThrowUnsupportedAttachment(attachment);
        }

        if (documents.Count == 0)
        {
            throw new KnowledgeIngestionStageException("reader", "No readable knowledge content was found.");
        }

        return documents;
    }

    /// <summary>
    /// 从 blob 存储读取文本附件内容。
    /// </summary>
    private async Task<IngestionDocument> ReadTextAttachmentAsync(KnowledgeAttachment attachment, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await _attachmentContainer.GetAsync(attachment.BlobName, cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
            var text = await reader.ReadToEndAsync(cancellationToken);
            return new IngestionDocument(
                attachment.FileName,
                null,
                text,
                IsMarkdownAttachment(attachment));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new KnowledgeIngestionStageException("reader", $"Failed to read attachment '{attachment.FileName}'.");
        }
    }

    /// <summary>
    /// 从 blob 存储读取复杂附件，并通过 MarkItDown 转换为 Markdown。
    /// </summary>
    private async Task<List<IngestionDocument>> ReadConvertibleAttachmentAsync(
        KnowledgeAttachment attachment,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await _attachmentContainer.GetAsync(attachment.BlobName, cancellationToken);
            var convertedDocuments = await _documentMarkdownConverter.ConvertAsync(attachment, stream, cancellationToken);
            return convertedDocuments
                .Select(x => new IngestionDocument(
                    attachment.FileName,
                    x.SourcePage,
                    x.Content,
                    IsMarkdown: true))
                .ToList();
        }
        catch (KnowledgeDocumentConversionException ex)
        {
            throw new KnowledgeIngestionStageException("reader", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new KnowledgeIngestionStageException("reader", $"Failed to read attachment '{attachment.FileName}'.");
        }
    }

    /// <summary>
    /// 判断附件是否属于可直接读取的文本或 Markdown 类型。
    /// </summary>
    private static bool IsDirectTextAttachment(KnowledgeAttachment attachment)
    {
        var extension = Path.GetExtension(attachment.FileName);
        var contentType = attachment.ContentType.Split(';', 2)[0].Trim();
        return SupportedAttachmentExtensions.Contains(extension) || SupportedTextContentTypes.Contains(contentType);
    }

    /// <summary>
    /// 抛出不支持附件的 reader 阶段错误，错误中只包含文件名和类型，不包含附件内容。
    /// </summary>
    private static void ThrowUnsupportedAttachment(KnowledgeAttachment attachment)
    {
        var extension = Path.GetExtension(attachment.FileName);
        throw new KnowledgeIngestionStageException("reader", $"Unsupported attachment type '{extension}' for '{attachment.FileName}'.");
    }

    /// <summary>
    /// 判断附件是否应按 Markdown 内容处理。
    /// </summary>
    private static bool IsMarkdownAttachment(KnowledgeAttachment attachment)
    {
        var extension = Path.GetExtension(attachment.FileName);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase) ||
               attachment.ContentType.StartsWith("text/markdown", StringComparison.OrdinalIgnoreCase) ||
               attachment.ContentType.StartsWith("application/markdown", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 对读取出的文档做标准化处理，并过滤空内容。
    /// </summary>
    private static List<IngestionDocument> ProcessDocuments(List<IngestionDocument> documents)
    {
        var processedDocuments = documents
            .Select(x => new IngestionDocument(
                x.SourceTitle,
                x.SourcePage,
                NormalizeToMarkdown(x),
                IsMarkdown: true))
            .Where(x => !string.IsNullOrWhiteSpace(x.Content))
            .ToList();

        if (processedDocuments.Count == 0)
        {
            throw new KnowledgeIngestionStageException("processor", "Readable documents produced no Markdown content.");
        }

        return processedDocuments;
    }

    /// <summary>
    /// 将普通文本或 Markdown 文档标准化为 Markdown 文本。
    /// </summary>
    private static string NormalizeToMarkdown(IngestionDocument document)
    {
        var content = NormalizeLineEndings(document.Content).Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        if (document.IsMarkdown)
        {
            return content;
        }

        // 非 Markdown 文本补一个标题，保证 chunk 和 citation 在前端至少有可读来源。
        return "# " + document.SourceTitle + "\n\n" + content;
    }

    /// <summary>
    /// 从文档集合创建 chunk 草稿。
    /// </summary>
    private static List<ChunkDraft> CreateChunkDrafts(List<IngestionDocument> documents)
    {
        var chunks = new List<ChunkDraft>();
        foreach (var document in documents)
        {
            chunks.AddRange(CreateDocumentChunks(document));
        }

        if (chunks.Count == 0)
        {
            throw new KnowledgeIngestionStageException("chunker", "No chunks were generated from the Markdown documents.");
        }

        return chunks;
    }

    /// <summary>
    /// 按段落边界和目标长度切分单篇文档。
    /// </summary>
    private static IEnumerable<ChunkDraft> CreateDocumentChunks(IngestionDocument document)
    {
        // 先按空行切块，尽量保留 Markdown 段落边界；超长段落再硬切，避免单个 chunk 过大。
        var blocks = document.Content
            .Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(SplitOversizedBlock)
            .ToList();

        var current = new StringBuilder();
        foreach (var block in blocks)
        {
            if (current.Length > 0 && current.Length + block.Length + 2 > TargetChunkLength)
            {
                yield return new ChunkDraft(current.ToString().Trim(), document.SourceTitle, document.SourcePage);
                var overlap = GetOverlapText(current.ToString());
                current.Clear();
                if (!string.IsNullOrWhiteSpace(overlap))
                {
                    current.AppendLine(overlap.Trim());
                    current.AppendLine();
                }
            }

            current.AppendLine(block.Trim());
            current.AppendLine();
        }

        if (current.Length > 0)
        {
            yield return new ChunkDraft(current.ToString().Trim(), document.SourceTitle, document.SourcePage);
        }
    }

    /// <summary>
    /// 将超长段落硬切为多个块，避免单个 chunk 超过目标长度。
    /// </summary>
    private static IEnumerable<string> SplitOversizedBlock(string block)
    {
        var normalized = block.Trim();
        if (normalized.Length <= TargetChunkLength)
        {
            yield return normalized;
            yield break;
        }

        for (var offset = 0; offset < normalized.Length; offset += TargetChunkLength)
        {
            var length = Math.Min(TargetChunkLength, normalized.Length - offset);
            yield return normalized.Substring(offset, length);
        }
    }

    /// <summary>
    /// 提取 chunk 重叠文本，用于保留跨段上下文。
    /// </summary>
    private static string GetOverlapText(string text)
    {
        var normalized = text.Trim();
        if (normalized.Length <= ChunkOverlapLength)
        {
            return normalized;
        }

        return normalized[^ChunkOverlapLength..];
    }

    /// <summary>
    /// 将指定知识版本已有 active chunk 标记为过期。
    /// </summary>
    private async Task MarkExistingChunksOutdatedAsync(Guid knowledgeVersionId, Guid currentJobId, CancellationToken cancellationToken)
    {
        var queryable = await _chunkRepository.GetQueryableAsync();
        var activeChunks = await AsyncExecuter.ToListAsync(
            queryable.Where(x => x.KnowledgeVersionId == knowledgeVersionId && x.KnowledgeIndexJobId != currentJobId && x.IsActive),
            cancellationToken);

        foreach (var chunk in activeChunks)
        {
            chunk.MarkOutdated();
            await _chunkRepository.UpdateAsync(chunk, autoSave: true, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// 将索引任务标记为指定阶段失败。
    /// </summary>
    private async Task MarkJobFailedAsync(KnowledgeIndexJob job, string stage, string reason, CancellationToken cancellationToken)
    {
        job.MarkFailed(stage, reason, DateTime.UtcNow);
        await _jobRepository.UpdateAsync(job, autoSave: true, cancellationToken: cancellationToken);
    }

    private async Task ReportJobProgressAsync(
        KnowledgeIndexJob job,
        string stage,
        int progressPercent,
        CancellationToken cancellationToken)
    {
        job.ReportProgress(stage, progressPercent, DateTime.UtcNow);
        await _jobRepository.UpdateAsync(job, autoSave: true, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 将持久化 chunk 映射为后续向量写入需要的投影记录。
    /// </summary>
    private static KnowledgeChunkVectorRecord MapToVectorRecord(KnowledgeChunk chunk, KnowledgeItem item, KnowledgeVersion version)
    {
        // 返回给后续 embedding/vector writer 的记录只是一份可重建投影。
        // PostgreSQL 中的 KnowledgeChunk 仍是可追溯事实，Qdrant payload 不应成为最终业务状态。
        return new KnowledgeChunkVectorRecord
        {
            ChunkId = chunk.Id,
            KnowledgeItemId = chunk.KnowledgeItemId,
            KnowledgeVersionId = chunk.KnowledgeVersionId,
            TenantId = chunk.TenantId,
            Content = chunk.Content,
            Payload = new KnowledgeVectorPayload
            {
                TenantId = chunk.TenantId,
                KnowledgeItemId = chunk.KnowledgeItemId,
                KnowledgeVersionId = chunk.KnowledgeVersionId,
                ChunkId = chunk.Id,
                CategoryId = item.CategoryId,
                SecurityLevel = chunk.SecurityLevelSnapshot,
                IsPublished = version.Status == KnowledgeVersionStatus.Published,
                SourceTitle = chunk.SourceTitle,
                SourcePage = chunk.SourcePage
            }
        };
    }

    /// <summary>
    /// 将不同平台换行统一为 LF。
    /// </summary>
    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    /// <summary>
    /// 粗略估算文本 token 数。
    /// </summary>
    private static int EstimateTokenCount(string content)
    {
        return Math.Max(1, content.Length / 2);
    }

    /// <summary>
    /// 计算 chunk 内容 SHA-256 hash。
    /// </summary>
    private static string ComputeContentHash(string content)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }

    /// <summary>
    /// 将字符串截断到指定最大长度。
    /// </summary>
    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    /// <summary>
    /// 摄取阶段内部文档模型。
    /// </summary>
    private sealed record IngestionDocument(string SourceTitle, int? SourcePage, string Content, bool IsMarkdown);

    /// <summary>
    /// 分块前的 chunk 草稿。
    /// </summary>
    private sealed record ChunkDraft(string Content, string SourceTitle, int? SourcePage);

    private sealed class KnowledgeIngestionStageException : Exception
    {
        /// <summary>
        /// 创建携带失败阶段的摄取异常。
        /// </summary>
        public KnowledgeIngestionStageException(string stage, string message)
            : base(message)
        {
            Stage = stage;
        }

        public string Stage { get; }
    }
}
