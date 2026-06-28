using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.AiQuestioning;

/// <summary>
/// AI 出题 prompt 模板聚合根。
/// 模板版本是 AI 候选题可追溯性的核心字段：同一批候选题必须能回到当时使用的系统提示词和用户提示词模板。
/// </summary>
public class AiQuestionPromptTemplate : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// 模板业务键最大长度，例如 mvp2-ai-question-candidate。
    /// </summary>
    public const int MaxTemplateKeyLength = 128;

    /// <summary>
    /// 模板版本最大长度，例如 mvp2-ai-question-v1。
    /// </summary>
    public const int MaxVersionLength = 64;

    /// <summary>
    /// 模板说明最大长度。
    /// </summary>
    public const int MaxDescriptionLength = 512;

    /// <summary>
    /// 系统提示词最大长度。
    /// </summary>
    public const int MaxSystemPromptLength = 4000;

    /// <summary>
    /// 用户提示词模板最大长度，包含上下文占位符和结构化输出要求。
    /// </summary>
    public const int MaxUserPromptTemplateLength = 8000;

    /// <summary>
    /// 所属租户。为空表示宿主侧模板。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 模板业务键，同一业务键可以有多个版本。
    /// </summary>
    public string TemplateKey { get; private set; } = string.Empty;

    /// <summary>
    /// 模板版本。任务会保存该版本，保证生成结果可回放和排查。
    /// </summary>
    public string Version { get; private set; } = string.Empty;

    /// <summary>
    /// 模板用途说明。
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// 系统提示词，表达安全边界：只能依据授权知识片段生成候选题。
    /// </summary>
    public string SystemPrompt { get; private set; } = string.Empty;

    /// <summary>
    /// 用户提示词模板，包含题型、难度、数量、知识范围和 RAG context 占位符。
    /// </summary>
    public string UserPromptTemplate { get; private set; } = string.Empty;

    /// <summary>
    /// 是否启用该模板版本。
    /// 任务保存 PromptTemplateId 和 Version，因此模板停用不会影响历史任务追溯。
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// EF Core 使用的构造函数。
    /// </summary>
    protected AiQuestionPromptTemplate()
    {
    }

    /// <summary>
    /// 创建 AI 出题 prompt 模板。
    /// 构造时只校验模板元数据和长度，不执行 prompt 内容语义校验。
    /// </summary>
    public AiQuestionPromptTemplate(
        Guid id,
        Guid? tenantId,
        string templateKey,
        string version,
        string systemPrompt,
        string userPromptTemplate,
        string? description = null,
        bool isActive = true)
        : base(id)
    {
        TenantId = tenantId;
        TemplateKey = Check.NotNullOrWhiteSpace(templateKey, nameof(templateKey), MaxTemplateKeyLength);
        Version = Check.NotNullOrWhiteSpace(version, nameof(version), MaxVersionLength);
        Description = Check.Length(description, nameof(description), MaxDescriptionLength);
        SystemPrompt = Check.NotNullOrWhiteSpace(systemPrompt, nameof(systemPrompt), MaxSystemPromptLength);
        UserPromptTemplate = Check.NotNullOrWhiteSpace(userPromptTemplate, nameof(userPromptTemplate), MaxUserPromptTemplateLength);
        IsActive = isActive;
    }

    /// <summary>
    /// 启用当前模板版本，使后续任务可选择它作为生成规则。
    /// </summary>
    public void Activate()
    {
        IsActive = true;
    }

    /// <summary>
    /// 停用当前模板版本。
    /// 停用只影响后续任务选择，不删除历史生成任务的追溯信息。
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
    }
}
