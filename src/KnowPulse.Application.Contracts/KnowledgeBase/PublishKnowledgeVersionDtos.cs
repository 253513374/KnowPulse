using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using KnowPulse.KnowledgeIndexing;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 发布知识版本的原子命令。访问策略、当前版本切换和索引任务必须在同一工作单元中完成。
/// </summary>
public class PublishKnowledgeVersionInput
{
    public Guid KnowledgeVersionId { get; set; }

    [Required]
    [MinLength(1)]
    public List<CreateKnowledgePermissionScopeDto> PermissionScopes { get; set; } = [];
}

/// <summary>
/// 发布完成后的业务事实，供页面直接展示发布版本、访问策略和索引任务。
/// </summary>
public class PublishKnowledgeVersionResultDto
{
    public KnowledgeVersionDto Version { get; set; } = new();

    public IReadOnlyList<KnowledgePermissionScopeDto> PermissionScopes { get; set; } = [];

    public KnowledgeIndexJobDto IndexJob { get; set; } = new();
}
