using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using KnowPulse.Education;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 指定学校对指定版本的一次授权。元数据是共享时的快照，私有新版本不会更新它。
/// 撤回后保留记录；再次授权建立新记录，不复活已撤回授权。
/// </summary>
public class SchoolKnowledgeShare : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxCurriculumBindings = 3000;
    public const int MaxCurriculumBindingIdsJsonLength = MaxCurriculumBindings * 39 + 2;
    private const SchoolKnowledgeShareUses AllUses = SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve |
        SchoolKnowledgeShareUses.GenerateQuestions | SchoolKnowledgeShareUses.Download | SchoolKnowledgeShareUses.Export | SchoolKnowledgeShareUses.AssignToStudents;

    public Guid? TenantId { get; protected set; }
    public Guid OwnerUserId { get; private set; }
    public Guid LibraryId { get; private set; }
    public Guid KnowledgeItemId { get; private set; }
    public Guid KnowledgeVersionId { get; private set; }
    public Guid SchoolId { get; private set; }
    /// <summary>本次共享确认的课程关联；后来新增的个人关联不会自动加入，结束关联仍阻止新使用。</summary>
    public string CurriculumBindingIdsJson { get; private set; } = "[]";
    public string Title { get; private set; } = string.Empty;
    public string? Summary { get; private set; }
    public SchoolKnowledgeShareUses Uses { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTime SharedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    protected SchoolKnowledgeShare() { }

    public SchoolKnowledgeShare(Guid id, Guid? tenantId, Guid ownerUserId, Guid libraryId,
        Guid knowledgeItemId, Guid knowledgeVersionId, Guid schoolId, string title, string? summary,
        SchoolKnowledgeShareUses uses, DateTime sharedAtUtc, IEnumerable<Guid>? curriculumBindingIds = null) : base(RequireId(id))
    {
        EnsureValidUses(uses);
        RequireUtc(sharedAtUtc);
        TenantId = tenantId;
        OwnerUserId = RequireId(ownerUserId);
        LibraryId = RequireId(libraryId);
        KnowledgeItemId = RequireId(knowledgeItemId);
        KnowledgeVersionId = RequireId(knowledgeVersionId);
        SchoolId = RequireId(schoolId);
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), KnowledgeItem.MaxTitleLength);
        Summary = Check.Length(summary, nameof(summary), KnowledgeItem.MaxSummaryLength);
        Uses = uses;
        SharedAtUtc = sharedAtUtc;
        var bindings = (curriculumBindingIds ?? Array.Empty<Guid>()).Distinct().OrderBy(x => x).ToArray();
        if (bindings.Length > MaxCurriculumBindings || bindings.Contains(Guid.Empty))
            throw new BusinessException(EducationCurriculumErrorCodes.CapacityExceeded);
        CurriculumBindingIdsJson = JsonSerializer.Serialize(bindings);
    }

    public Guid[] GetCurriculumBindingIds() => JsonSerializer.Deserialize<Guid[]>(CurriculumBindingIdsJson) ?? Array.Empty<Guid>();

    public static void EnsureValidUses(SchoolKnowledgeShareUses uses)
    {
        if ((uses & SchoolKnowledgeShareUses.Read) == 0 || (uses & ~AllUses) != 0 ||
            ((uses & SchoolKnowledgeShareUses.GenerateQuestions) != 0 && (uses & SchoolKnowledgeShareUses.Retrieve) == 0))
            throw new BusinessException(SchoolKnowledgeSharingErrorCodes.InvalidUses);
    }

    public bool Allows(SchoolKnowledgeShareUses use)
    {
        if ((use & ~AllUses) != 0) throw new BusinessException(SchoolKnowledgeSharingErrorCodes.InvalidUses);
        return !IsRevoked && !IsDeleted && use != 0 && (Uses & use) == use;
    }

    public void Revoke(DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        if (nowUtc < SharedAtUtc) throw new ArgumentOutOfRangeException(nameof(nowUtc));
        if (IsRevoked) return;
        IsRevoked = true;
        RevokedAtUtc = nowUtc;
    }

    private static Guid RequireId(Guid value) => value != Guid.Empty
        ? value : throw new ArgumentException("A nonempty identifier is required.");

    private static void RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("A UTC timestamp is required.");
    }
}
