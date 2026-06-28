using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.RagAI;

/// <summary>
/// RAG 评测逐用例结果。记录命中、引用准确性、拒答和越权召回判定。
/// </summary>
public class RagEvaluationRunCaseResult : CreationAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>
    /// 问题摘要最大长度。
    /// </summary>
    public const int MaxQuestionSummaryLength = 256;

    /// <summary>
    /// 失败原因最大长度。
    /// </summary>
    public const int MaxFailureReasonLength = 1024;

    /// <summary>
    /// 回答摘要最大长度。
    /// </summary>
    public const int MaxAnswerSummaryLength = 512;

    /// <summary>
    /// 实际引用 JSON 最大长度。
    /// </summary>
    public const int MaxActualCitationsJsonLength = 8192;

    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧逐用例结果。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 所属评测运行 ID。
    /// </summary>
    public Guid EvaluationRunId { get; private set; }

    /// <summary>
    /// 评测用例 ID。
    /// </summary>
    public Guid EvaluationCaseId { get; private set; }

    /// <summary>
    /// 问题摘要，不保存超长原始问题。
    /// </summary>
    public string QuestionSummary { get; private set; } = string.Empty;

    /// <summary>
    /// 用例是否期望拒答。
    /// </summary>
    public bool ExpectedRefusal { get; private set; }

    /// <summary>
    /// 实际回答是否拒答。
    /// </summary>
    public bool ActualRefusal { get; private set; }

    /// <summary>
    /// 期望引用数量。
    /// </summary>
    public int ExpectedCitationCount { get; private set; }

    /// <summary>
    /// 实际引用数量。
    /// </summary>
    public int ActualCitationCount { get; private set; }

    /// <summary>
    /// 实际引用中命中期望引用的数量。
    /// </summary>
    public int MatchedExpectedCitationCount { get; private set; }

    /// <summary>
    /// 实际引用中命中禁止引用的数量。
    /// </summary>
    public int UnauthorizedCitationCount { get; private set; }

    /// <summary>
    /// 用例是否命中期望结果。
    /// </summary>
    public bool Hit { get; private set; }

    /// <summary>
    /// 本用例引用准确率。
    /// </summary>
    public double CitationAccuracyRate { get; private set; }

    /// <summary>
    /// 本用例是否通过。
    /// </summary>
    public bool Passed { get; private set; }

    /// <summary>
    /// 本用例失败原因。
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// 回答摘要，不保存完整模型输出。
    /// </summary>
    public string? AnswerSummary { get; private set; }

    /// <summary>
    /// 实际引用 JSON，只保存 citation 标识信息。
    /// </summary>
    public string ActualCitationsJson { get; private set; } = "[]";

    /// <summary>
    /// EF Core 使用的受保护构造函数。
    /// </summary>
    protected RagEvaluationRunCaseResult()
    {
    }

    /// <summary>
    /// 创建逐用例评测结果。
    /// </summary>
    /// <param name="id">逐用例结果 ID。</param>
    /// <param name="tenantId">租户 ID；宿主侧数据为空。</param>
    /// <param name="evaluationRunId">所属评测运行 ID。</param>
    /// <param name="evaluationCaseId">评测用例 ID。</param>
    /// <param name="questionSummary">问题摘要。</param>
    /// <param name="expectedRefusal">是否期望拒答。</param>
    /// <param name="actualRefusal">实际是否拒答。</param>
    /// <param name="expectedCitationCount">期望引用数量。</param>
    /// <param name="actualCitationCount">实际引用数量。</param>
    /// <param name="matchedExpectedCitationCount">命中期望引用数量。</param>
    /// <param name="unauthorizedCitationCount">越权引用数量。</param>
    /// <param name="hit">是否命中期望。</param>
    /// <param name="citationAccuracyRate">引用准确率。</param>
    /// <param name="passed">是否通过。</param>
    /// <param name="failureReason">失败原因。</param>
    /// <param name="answerSummary">回答摘要。</param>
    /// <param name="actualCitationsJson">实际引用 JSON。</param>
    public RagEvaluationRunCaseResult(
        Guid id,
        Guid? tenantId,
        Guid evaluationRunId,
        Guid evaluationCaseId,
        string questionSummary,
        bool expectedRefusal,
        bool actualRefusal,
        int expectedCitationCount,
        int actualCitationCount,
        int matchedExpectedCitationCount,
        int unauthorizedCitationCount,
        bool hit,
        double citationAccuracyRate,
        bool passed,
        string? failureReason,
        string? answerSummary,
        string actualCitationsJson)
        : base(id)
    {
        if (expectedCitationCount < 0 ||
            actualCitationCount < 0 ||
            matchedExpectedCitationCount < 0 ||
            unauthorizedCitationCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actualCitationCount));
        }

        if (double.IsNaN(citationAccuracyRate) || citationAccuracyRate < 0 || citationAccuracyRate > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(citationAccuracyRate));
        }

        TenantId = tenantId;
        EvaluationRunId = evaluationRunId;
        EvaluationCaseId = evaluationCaseId;
        QuestionSummary = TrimRequired(questionSummary, nameof(questionSummary), MaxQuestionSummaryLength);
        ExpectedRefusal = expectedRefusal;
        ActualRefusal = actualRefusal;
        ExpectedCitationCount = expectedCitationCount;
        ActualCitationCount = actualCitationCount;
        MatchedExpectedCitationCount = matchedExpectedCitationCount;
        UnauthorizedCitationCount = unauthorizedCitationCount;
        Hit = hit;
        CitationAccuracyRate = Math.Round(citationAccuracyRate, 6);
        Passed = passed;
        FailureReason = Check.Length(failureReason, nameof(failureReason), MaxFailureReasonLength);
        AnswerSummary = Check.Length(answerSummary, nameof(answerSummary), MaxAnswerSummaryLength);
        ActualCitationsJson = Check.NotNull(actualCitationsJson, nameof(actualCitationsJson)).Length <= MaxActualCitationsJsonLength
            ? actualCitationsJson
            : actualCitationsJson[..MaxActualCitationsJsonLength];
    }

    /// <summary>
    /// 校验必填字符串并按最大长度截断。
    /// </summary>
    /// <param name="value">待处理字符串。</param>
    /// <param name="parameterName">参数名称。</param>
    /// <param name="maxLength">最大长度。</param>
    /// <returns>归一化后的字符串。</returns>
    private static string TrimRequired(string value, string parameterName, int maxLength)
    {
        var normalized = Check.NotNullOrWhiteSpace(value, parameterName);
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].TrimEnd();
    }
}
