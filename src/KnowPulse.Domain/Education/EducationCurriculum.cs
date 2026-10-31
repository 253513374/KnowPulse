using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class TextbookVersion : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid EducationSubjectId { get; private set; }
    public string CatalogCode { get; private set; } = string.Empty;
    public int CatalogRevision { get; private set; }
    public Guid? PreviousVersionId { get; private set; }
    public string Stage => "JuniorSecondary";
    public int Grade { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Publisher { get; private set; } = string.Empty;
    public string Edition { get; private set; } = string.Empty;
    public string Volume { get; private set; } = string.Empty;
    public TextbookVersionStatus Status { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public DateTime? RetiredAtUtc { get; private set; }

    protected TextbookVersion() { }
    public TextbookVersion(Guid id, Guid? tenantId, Guid schoolId, Guid subjectId, string code, int revision,
        int grade, string title, string publisher, string edition, string volume, Guid? previousVersionId = null)
        : base(EducationRules.RequireId(id))
    {
        if (revision < 1 || previousVersionId == Guid.Empty || previousVersionId == id)
            throw new BusinessException(EducationCurriculumErrorCodes.InvalidStructure);
        TenantId = tenantId;
        SchoolId = EducationRules.RequireId(schoolId);
        EducationSubjectId = EducationRules.RequireId(subjectId);
        CatalogCode = EducationRules.NormalizeCode(code, 64);
        CatalogRevision = revision;
        PreviousVersionId = previousVersionId;
        UpdateDraft(grade, title, publisher, edition, volume);
    }

    public void EnsureDraft()
    {
        if (Status != TextbookVersionStatus.Draft) throw new BusinessException(EducationCurriculumErrorCodes.ImmutableVersion);
    }
    public void UpdateDraft(int grade, string title, string publisher, string edition, string volume)
    {
        EnsureDraft();
        if (grade is < 7 or > 9) throw new BusinessException(EducationErrorCodes.InvalidGrade);
        var validatedTitle = Check.NotNullOrWhiteSpace(title, nameof(title), 256).Trim();
        var validatedPublisher = Check.NotNullOrWhiteSpace(publisher, nameof(publisher), 128).Trim();
        var validatedEdition = Check.NotNullOrWhiteSpace(edition, nameof(edition), 128).Trim();
        var validatedVolume = Check.NotNullOrWhiteSpace(volume, nameof(volume), 64).Trim();
        Grade = grade;
        Title = validatedTitle;
        Publisher = validatedPublisher;
        Edition = validatedEdition;
        Volume = validatedVolume;
    }
    public void Publish(DateTime now)
    {
        if (Status == TextbookVersionStatus.Published) return;
        EnsureDraft();
        EducationRules.RequireUtc(now);
        Status = TextbookVersionStatus.Published;
        PublishedAtUtc = now;
    }
    public void Retire(DateTime now)
    {
        if (Status == TextbookVersionStatus.Retired) return;
        if (Status != TextbookVersionStatus.Published) throw new BusinessException(EducationCurriculumErrorCodes.PublishedVersionRequired);
        EducationRules.RequireUtc(now);
        Status = TextbookVersionStatus.Retired;
        RetiredAtUtc = now;
    }
}

public class TextbookChapter : FullAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid TextbookVersionId { get; private set; }
    public Guid? ParentChapterId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }
    protected TextbookChapter() { }
    public TextbookChapter(Guid id, Guid? tenantId, Guid textbookVersionId, Guid? parentId, string code, string title, int order)
        : base(EducationRules.RequireId(id))
    {
        TenantId = tenantId;
        TextbookVersionId = EducationRules.RequireId(textbookVersionId);
        SetDraft(parentId, code, title, order);
    }
    public void UpdateDraft(TextbookVersion book, Guid? parentId, string code, string title, int order)
    {
        book.EnsureDraft();
        if (book.Id != TextbookVersionId || book.TenantId != TenantId)
            throw new BusinessException(EducationCurriculumErrorCodes.ReferenceMismatch);
        SetDraft(parentId, code, title, order);
    }
    private void SetDraft(Guid? parentId, string code, string title, int order)
    {
        if (parentId == Guid.Empty || parentId == Id || order < 0) throw new BusinessException(EducationCurriculumErrorCodes.InvalidStructure);
        var validatedCode = EducationRules.NormalizeCode(code, 64);
        var validatedTitle = Check.NotNullOrWhiteSpace(title, nameof(title), 256).Trim();
        ParentChapterId = parentId;
        Code = validatedCode;
        Title = validatedTitle;
        DisplayOrder = order;
    }
}

public class CourseKnowledgePoint : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid EducationSubjectId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public int CurrentRevisionNumber { get; private set; }
    protected CourseKnowledgePoint() { }
    public CourseKnowledgePoint(Guid id, Guid? tenantId, Guid schoolId, Guid subjectId, string code)
        : base(EducationRules.RequireId(id))
    {
        TenantId = tenantId;
        SchoolId = EducationRules.RequireId(schoolId);
        EducationSubjectId = EducationRules.RequireId(subjectId);
        Code = EducationRules.NormalizeCode(code, 64);
    }
    public int AdvanceRevision() => CurrentRevisionNumber = checked(CurrentRevisionNumber + 1);
}

public class CourseKnowledgePointRevision : FullAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid KnowledgePointId { get; private set; }
    public int RevisionNumber { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid? ParentRevisionId { get; private set; }
    protected CourseKnowledgePointRevision() { }
    public CourseKnowledgePointRevision(Guid id, Guid? tenantId, Guid pointId, int revision, string name, Guid? parentId)
        : base(EducationRules.RequireId(id))
    {
        if (revision < 1 || parentId == Guid.Empty || parentId == id) throw new BusinessException(EducationCurriculumErrorCodes.InvalidStructure);
        TenantId = tenantId;
        KnowledgePointId = EducationRules.RequireId(pointId);
        RevisionNumber = revision;
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), 128).Trim();
        ParentRevisionId = parentId;
    }
}

public class TextbookKnowledgePointMapping : FullAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid TextbookVersionId { get; private set; }
    public Guid ChapterId { get; private set; }
    public Guid KnowledgePointId { get; private set; }
    public Guid KnowledgePointRevisionId { get; private set; }
    public Guid ConfirmedByUserId { get; private set; }
    public DateTime ConfirmedAtUtc { get; private set; }
    protected TextbookKnowledgePointMapping() { }
    public TextbookKnowledgePointMapping(Guid id, Guid? tenantId, Guid versionId, Guid chapterId, Guid pointId, Guid revisionId, Guid userId, DateTime now)
        : base(EducationRules.RequireId(id))
    {
        TenantId = tenantId;
        TextbookVersionId = EducationRules.RequireId(versionId);
        ChapterId = EducationRules.RequireId(chapterId);
        KnowledgePointId = EducationRules.RequireId(pointId);
        KnowledgePointRevisionId = EducationRules.RequireId(revisionId);
        ConfirmedByUserId = EducationRules.RequireId(userId);
        ConfirmedAtUtc = EducationRules.RequireUtc(now);
    }
}

public class TeacherTextbookSelection : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid TeacherUserId { get; private set; }
    public Guid TeachingAssignmentId { get; private set; }
    public Guid TextbookVersionId { get; private set; }
    public DateTime SelectedAtUtc { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }
    protected TeacherTextbookSelection() { }
    public TeacherTextbookSelection(Guid id, Guid? tenantId, Guid schoolId, Guid teacherId, Guid assignmentId, Guid textbookId, DateTime now)
        : base(EducationRules.RequireId(id))
    {
        TenantId = tenantId;
        SchoolId = EducationRules.RequireId(schoolId);
        TeacherUserId = EducationRules.RequireId(teacherId);
        TeachingAssignmentId = EducationRules.RequireId(assignmentId);
        TextbookVersionId = EducationRules.RequireId(textbookId);
        SelectedAtUtc = EducationRules.RequireUtc(now);
    }
    public void End(DateTime now)
    {
        EducationRules.RequireUtc(now);
        if (now < SelectedAtUtc) throw new BusinessException(EducationErrorCodes.InvalidEffectivePeriod);
        EndedAtUtc ??= now;
    }
}
