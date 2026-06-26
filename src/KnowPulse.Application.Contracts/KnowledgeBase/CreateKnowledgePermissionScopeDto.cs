using System;
using System.ComponentModel.DataAnnotations;

namespace KnowPulse.KnowledgeBase;

public class CreateKnowledgePermissionScopeDto
{
    public KnowledgePermissionScopeType ScopeType { get; set; }
    public Guid? TargetId { get; set; }

    [Range(0, int.MaxValue)]
    public int? MinimumSecurityLevel { get; set; }
}
