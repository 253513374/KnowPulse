using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Users;

namespace KnowPulse.AiQuestioning;

/// <summary>
/// 题目相似度检测服务。
/// AI 候选题生成后会调用该服务查找最相似的已有题目，给审核人员提供重复风险提示。
/// </summary>
public interface IQuestionSimilarityService
{
    /// <summary>
    /// 查找与输入标题和题干最相似的题目。
    /// 返回空表示没有达到审核提示阈值；ignoredQuestionId 用于后续编辑场景排除当前题。
    /// </summary>
    Task<QuestionSimilarityResult?> FindMostSimilarAsync(
        string title,
        string stem,
        Guid? ignoredQuestionId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 题目相似度检测结果。
/// 只返回最高相似题，避免候选题页面被低价值重复提示淹没。
/// </summary>
public class QuestionSimilarityResult
{
    /// <summary>
    /// 最相似的已有题目 ID。
    /// </summary>
    public Guid QuestionId { get; set; }

    /// <summary>
    /// 相似度分值，范围 0 到 1。
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// 展示给审核人员的简短提示。
    /// </summary>
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// 基于题干文本 shingles 的轻量相似题检测实现。
/// MVP-2 使用本地算法满足审核提示需求，后续可以在不改变接口的情况下替换为向量检索或专用相似度索引。
/// </summary>
public class QuestionSimilarityService : IQuestionSimilarityService, ITransientDependency
{
    /// <summary>
    /// 达到该分值才在候选题审核页提示相似题。
    /// 阈值偏高是为了减少误报，让人工注意力集中在真正可能重复的题目上。
    /// </summary>
    public const double ReviewWarningThreshold = 0.7;

    private readonly IRepository<Question, Guid> _questionRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly ICurrentUser _currentUser;
    private readonly KnowledgeSourceAccessGuard _sourceAccess;

    /// <summary>
    /// 创建相似题检测服务。
    /// </summary>
    public QuestionSimilarityService(
        IRepository<Question, Guid> questionRepository,
        IAsyncQueryableExecuter asyncExecuter,
        ICurrentUser currentUser,
        KnowledgeSourceAccessGuard sourceAccess)
    {
        _questionRepository = questionRepository;
        _asyncExecuter = asyncExecuter;
        _currentUser = currentUser;
        _sourceAccess = sourceAccess;
    }

    /// <summary>
    /// 在未归档题目中查找最高相似题。
    /// 这里只返回超过阈值的最高分结果，页面据此展示“疑似相似题”提示。
    /// </summary>
    public async Task<QuestionSimilarityResult?> FindMostSimilarAsync(
        string title,
        string stem,
        Guid? ignoredQuestionId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUser.Id.HasValue) return null;
        var queryable = await _questionRepository.GetQueryableAsync();
        queryable = queryable.Where(x => x.OwnerTeacherId == _currentUser.Id ||
            (x.OwnerTeacherId == null && (_currentUser.IsInRole("admin") || x.PersonalOwnerUserId == _currentUser.Id ||
                x.CurriculumSnapshotJson != null)));
        var questions = await _asyncExecuter.ToListAsync(
            queryable.Where(x =>
                x.Status != QuestionStatus.Archived &&
                (!ignoredQuestionId.HasValue || x.Id != ignoredQuestionId.Value)),
            cancellationToken);

        var target = Normalize(title + " " + stem);
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var visibleQuestions = new List<Question>();
        foreach (var question in questions)
        {
            if (await _sourceAccess.CanReadQuestionAsync(question))
            {
                visibleQuestions.Add(question);
            }
        }

        var best = visibleQuestions
            .Select(question => new
            {
                Question = question,
                Score = CalculateSimilarity(target, Normalize(question.Title + " " + question.Stem))
            })
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        if (best == null || best.Score < ReviewWarningThreshold)
        {
            return null;
        }

        return new QuestionSimilarityResult
        {
            QuestionId = best.Question.Id,
            Score = Math.Round(best.Score, 2),
            Summary = $"疑似相似题：{Trim(best.Question.Title, 120)}"
        };
    }

    /// <summary>
    /// 计算两个规范化文本的相似度。
    /// 完全相同和包含关系直接给高分，否则使用二元 shingles 的 Jaccard 相似度。
    /// </summary>
    private static double CalculateSimilarity(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return 0;
        }

        if (left.Equals(right, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (left.Contains(right, StringComparison.OrdinalIgnoreCase) ||
            right.Contains(left, StringComparison.OrdinalIgnoreCase))
        {
            return 0.9;
        }

        var leftShingles = ToShingles(left);
        var rightShingles = ToShingles(right);
        if (leftShingles.Count == 0 || rightShingles.Count == 0)
        {
            return 0;
        }

        var intersection = leftShingles.Intersect(rightShingles).Count();
        var union = leftShingles.Union(rightShingles).Count();
        return union == 0 ? 0 : (double)intersection / union;
    }

    /// <summary>
    /// 将文本拆成二元 shingles。
    /// 中文没有天然空格分词，二元片段能在不引入分词依赖的情况下捕捉局部重复。
    /// </summary>
    private static HashSet<string> ToShingles(string value)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (value.Length <= 2)
        {
            result.Add(value);
            return result;
        }

        for (var i = 0; i < value.Length - 1; i++)
        {
            result.Add(value.Substring(i, 2));
        }

        return result;
    }

    /// <summary>
    /// 规范化题目文本，只保留字母、数字和 CJK 字符，降低标点和空白对相似度的干扰。
    /// </summary>
    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || IsCjk(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// 判断字符是否属于常用 CJK 统一表意文字范围。
    /// </summary>
    private static bool IsCjk(char ch)
    {
        return ch is >= '\u4e00' and <= '\u9fff';
    }

    /// <summary>
    /// 裁剪相似题标题，保证页面提示简短稳定。
    /// </summary>
    private static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "...";
    }
}
