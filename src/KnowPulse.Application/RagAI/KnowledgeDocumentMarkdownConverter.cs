using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Options;

namespace KnowPulse.RagAI;

/// <summary>
/// 将非文本附件转换为 Markdown 的项目内接口。
/// 预览 reader 和外部进程只允许出现在 Application 实现层，不能进入业务契约或领域模型。
/// </summary>
public interface IKnowledgeDocumentMarkdownConverter
{
    /// <summary>
    /// 判断附件是否属于当前配置允许转换的类型。
    /// </summary>
    bool CanConvert(KnowledgeAttachment attachment);

    /// <summary>
    /// 将附件流转换为 Markdown 片段。
    /// </summary>
    Task<IReadOnlyList<ConvertedMarkdownDocument>> ConvertAsync(
        KnowledgeAttachment attachment,
        Stream stream,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 单个附件转换出的 Markdown 文档片段。
/// </summary>
public sealed record ConvertedMarkdownDocument(string Content, int? SourcePage);

/// <summary>
/// 附件转换失败。消息只允许描述阶段、类型和配置问题，不应包含完整附件内容。
/// </summary>
public sealed class KnowledgeDocumentConversionException : Exception
{
    public KnowledgeDocumentConversionException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// 基于 Microsoft.Extensions.DataIngestion.MarkItDown 的附件 Markdown 转换器。
/// </summary>
public class MarkItDownKnowledgeDocumentMarkdownConverter : IKnowledgeDocumentMarkdownConverter
{
    private static readonly string[] CandidateExecutableNames =
    [
        "markitdown",
        "markitdown.exe",
        "markitdown.cmd",
        "markitdown.bat"
    ];

    private readonly RagAiOptions _options;

    /// <summary>
    /// 创建 MarkItDown 转换器。
    /// </summary>
    public MarkItDownKnowledgeDocumentMarkdownConverter(IOptions<RagAiOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// 只让明确列入配置的扩展名进入 MarkItDown，避免对图片、音频、视频作出未验证承诺。
    /// </summary>
    public bool CanConvert(KnowledgeAttachment attachment)
    {
        if (!_options.Ingestion.EnableMarkItDown)
        {
            return false;
        }

        var extension = Path.GetExtension(attachment.FileName);
        return !string.IsNullOrWhiteSpace(extension) &&
               _options.Ingestion.MarkItDownExtensions.Any(x => x.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 调用 MarkItDownReader 读取附件流，并把 DataIngestion 文档元素转换为项目内 Markdown 片段。
    /// </summary>
    public async Task<IReadOnlyList<ConvertedMarkdownDocument>> ConvertAsync(
        KnowledgeAttachment attachment,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var executable = ResolveExecutable();
        try
        {
            var reader = new MarkItDownReader(executable, _options.Ingestion.ExtractImages);
            var document = await reader.ReadAsync(
                stream,
                attachment.FileName,
                NormalizeContentType(attachment.ContentType),
                cancellationToken);

            return ExtractMarkdownDocuments(attachment.FileName, document);
        }
        catch (KnowledgeDocumentConversionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new KnowledgeDocumentConversionException(
                $"MarkItDown failed for '{attachment.FileName}': {ex.GetType().Name}.");
        }
    }

    private IReadOnlyList<ConvertedMarkdownDocument> ExtractMarkdownDocuments(
        string fileName,
        IngestionDocument document)
    {
        var markdownByPage = document
            .EnumerateContent()
            .Select(element => new
            {
                element.PageNumber,
                Markdown = element.GetMarkdown()
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Markdown))
            .GroupBy(x => x.PageNumber)
            .Select(group => new ConvertedMarkdownDocument(
                JoinMarkdown(group.Select(x => x.Markdown)),
                group.Key))
            .Where(x => !string.IsNullOrWhiteSpace(x.Content))
            .ToList();

        if (markdownByPage.Count == 0)
        {
            throw new KnowledgeDocumentConversionException($"MarkItDown produced no Markdown content for '{fileName}'.");
        }

        var totalLength = markdownByPage.Sum(x => x.Content.Length);
        if (totalLength > _options.Ingestion.MaxConvertedMarkdownLength)
        {
            throw new KnowledgeDocumentConversionException(
                $"MarkItDown output for '{fileName}' exceeded {_options.Ingestion.MaxConvertedMarkdownLength} characters.");
        }

        return markdownByPage;
    }

    private FileInfo ResolveExecutable()
    {
        var configuredPath = _options.Ingestion.MarkItDownExecutablePath;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (TryResolveConfiguredPath(configuredPath, out var configuredFile))
            {
                return configuredFile;
            }

            throw new KnowledgeDocumentConversionException(
                $"MarkItDown executable was not found at configured path '{configuredPath}'.");
        }

        foreach (var executableName in CandidateExecutableNames)
        {
            if (TryResolveFromPath(executableName, out var resolvedFile))
            {
                return resolvedFile;
            }
        }

        throw new KnowledgeDocumentConversionException(
            "MarkItDown executable was not found. Configure RagAI:Ingestion:MarkItDownExecutablePath or add markitdown to PATH.");
    }

    private static bool TryResolveConfiguredPath(string configuredPath, out FileInfo file)
    {
        if (Path.GetDirectoryName(configuredPath) is { Length: > 0 })
        {
            file = new FileInfo(configuredPath);
            return file.Exists;
        }

        return TryResolveFromPath(configuredPath, out file);
    }

    private static bool TryResolveFromPath(string executableName, out FileInfo file)
    {
        foreach (var directory in GetPathDirectories())
        {
            var candidate = new FileInfo(Path.Combine(directory, executableName));
            if (candidate.Exists)
            {
                file = candidate;
                return true;
            }
        }

        file = new FileInfo(executableName);
        return false;
    }

    private static IEnumerable<string> GetPathDirectories()
    {
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists);
    }

    private static string JoinMarkdown(IEnumerable<string> markdownBlocks)
    {
        var builder = new StringBuilder();
        foreach (var block in markdownBlocks)
        {
            var markdown = block.Trim();
            if (markdown.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(markdown);
        }

        return builder.ToString();
    }

    private static string NormalizeContentType(string contentType)
    {
        return contentType.Split(';', 2)[0].Trim();
    }
}
