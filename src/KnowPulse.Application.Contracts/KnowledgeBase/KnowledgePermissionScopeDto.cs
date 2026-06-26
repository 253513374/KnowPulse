using System;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.KnowledgeBase;

public class KnowledgePermissionScopeDto : FullAuditedEntityDto<Guid>
{
    public Guid KnowledgeItemId { get; set; }
    public KnowledgePermissionScopeType ScopeType { get; set; }
    public Guid? TargetId { get; set; }
    public int? MinimumSecurityLevel { get; set; }
    public bool IsEnabled { get; set; }
}
