using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.RagAI;

/// <summary>
/// RAG 质量评测用例。用例只描述问题、期望 citation、期望拒答和权限上下文，不保存模型回答正文。
/// </summary>
public class RagEvaluationCase : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// 评测用例名称最大长度。
    /// </summary>
    public const int MaxNameLength = 256;

    /// <summary>
    /// 评测问题最大长度。
    /// </summary>
    public const int MaxQuestionLength = 1024;

    /// <summary>
    /// 权限上下文 JSON 最大长度。
    /// </summary>
    public const int MaxPermissionContextJsonLength = 4096;

    /// <summary>
    /// 引用期望 JSON 最大长度。
    /// </summary>
    public const int MaxCitationExpectationJsonLength = 8192;

    /// <summary>
    /// 用例备注最大长度。
    /// </summary>
    public const int MaxNotesLength = 1024;

    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧评测用例。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    public Guid? PersonalOwnerUserId { get; private set; }

    public void RestrictToPersonalOwner(Guid ownerUserId)
    {
        if (ownerUserId == Guid.Empty || (PersonalOwnerUserId.HasValue && PersonalOwnerUserId != ownerUserId))
        {
            throw new ArgumentException("A personal evaluation case cannot change its owner.", nameof(ownerUserId));
        }

        PersonalOwnerUserId = ownerUserId;
    }

    /// <summary>
    /// 评测用例名称。
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// 待评测的问题文本。
    /// </summary>
    public string Question { get; private set; } = string.Empty;

    /// <summary>
    /// 是否期望系统拒答。为 true 时不要求命中引用，要求回答无 citation。
    /// </summary>
    public bool ExpectedRefusal { get; private set; }

    /// <summary>
    /// 用例是否启用。默认运行只执行启用用例。
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// 权限上下文 JSON，记录租户、用户、部门、岗位、角色和安全等级声明。
    /// </summary>
    public string PermissionContextJson { get; private set; } = "{}";

    /// <summary>
    /// 期望引用 JSON，记录应被命中的知识条目、知识版本或 chunk。
    /// </summary>
    public string ExpectedCitationsJson { get; private set; } = "[]";

    /// <summary>
    /// 禁止引用 JSON，命中这些引用会计入越权召回并导致硬失败。
    /// </summary>
    public string ForbiddenCitationsJson { get; private set; } = "[]";

    /// <summary>
    /// 用例备注，用于说明数据准备、评测意图或人工验收补充信息。
    /// </summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// EF Core 使用的受保护构造函数。
    /// </summary>
    protected RagEvaluationCase()
    {
    }

    /// <summary>
    /// 创建 RAG 评测用例。
    /// </summary>
    /// <param name="id">评测用例 ID。</param>
    /// <param name="tenantId">租户 ID；宿主侧数据为空。</param>
    /// <param name="name">用例名称。</param>
    /// <param name="question">待评测问题。</param>
    /// <param name="expectedRefusal">是否期望拒答。</param>
    /// <param name="permissionContextJson">权限上下文 JSON。</param>
    /// <param name="expectedCitationsJson">期望引用 JSON。</param>
    /// <param name="forbiddenCitationsJson">禁止引用 JSON。</param>
    /// <param name="notes">备注。</param>
    public RagEvaluationCase(
        Guid id,
        Guid? tenantId,
        string name,
        string question,
        bool expectedRefusal,
        string permissionContextJson,
        string expectedCitationsJson,
        string forbiddenCitationsJson,
        string? notes = null)
        : base(id)
    {
        TenantId = tenantId;
        IsActive = true;
        Update(name, question, expectedRefusal, permissionContextJson, expectedCitationsJson, forbiddenCitationsJson, notes);
    }

    /// <summary>
    /// 更新评测用例内容。
    /// </summary>
    /// <param name="name">用例名称。</param>
    /// <param name="question">待评测问题。</param>
    /// <param name="expectedRefusal">是否期望拒答。</param>
    /// <param name="permissionContextJson">权限上下文 JSON。</param>
    /// <param name="expectedCitationsJson">期望引用 JSON。</param>
    /// <param name="forbiddenCitationsJson">禁止引用 JSON。</param>
    /// <param name="notes">备注。</param>
    public void Update(
        string name,
        string question,
        bool expectedRefusal,
        string permissionContextJson,
        string expectedCitationsJson,
        string forbiddenCitationsJson,
        string? notes = null)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength);
        Question = Check.NotNullOrWhiteSpace(question, nameof(question), MaxQuestionLength);
        ExpectedRefusal = expectedRefusal;
        PermissionContextJson = CheckJson(permissionContextJson, nameof(permissionContextJson), MaxPermissionContextJsonLength, "{}");
        ExpectedCitationsJson = CheckJson(expectedCitationsJson, nameof(expectedCitationsJson), MaxCitationExpectationJsonLength, "[]");
        ForbiddenCitationsJson = CheckJson(forbiddenCitationsJson, nameof(forbiddenCitationsJson), MaxCitationExpectationJsonLength, "[]");
        Notes = Check.Length(notes, nameof(notes), MaxNotesLength);
    }

    /// <summary>
    /// 启用或停用评测用例。
    /// </summary>
    /// <param name="isActive">是否启用。</param>
    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }

    /// <summary>
    /// 校验 JSON 字符串长度，并为空值写入稳定默认 JSON。
    /// </summary>
    /// <param name="value">待校验 JSON 字符串。</param>
    /// <param name="parameterName">参数名称。</param>
    /// <param name="maxLength">最大长度。</param>
    /// <param name="defaultValue">空值时使用的默认 JSON。</param>
    /// <returns>归一化后的 JSON 字符串。</returns>
    private static string CheckJson(string? value, string parameterName, int maxLength, string defaultValue)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
        return Check.NotNull(normalized, parameterName).Length <= maxLength
            ? normalized
            : throw new ArgumentException($"{parameterName} exceeds {maxLength} characters.", parameterName);
    }
}
