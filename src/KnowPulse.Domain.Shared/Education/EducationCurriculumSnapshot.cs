using System;
using System.Collections.Generic;

namespace KnowPulse.Education;

public sealed class EducationCurriculumSnapshot
{
    public Guid? TenantId { get; set; }
    public Guid TeacherUserId { get; set; }
    public Guid SchoolId { get; set; }
    public Guid TeachingAssignmentId { get; set; }
    public Guid TeacherTextbookSelectionId { get; set; }
    public Guid TextbookVersionId { get; set; }
    public Guid EducationSubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public int Grade { get; set; }
    public string CatalogCode { get; set; } = string.Empty;
    public int CatalogRevision { get; set; }
    public string TextbookTitle { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Edition { get; set; } = string.Empty;
    public string Volume { get; set; } = string.Empty;
    public DateTime CapturedAtUtc { get; set; }
    public List<EducationCurriculumMappingSnapshot> Mappings { get; set; } = [];
}

public sealed class EducationCurriculumMappingSnapshot
{
    public Guid MappingId { get; set; }
    public Guid ChapterId { get; set; }
    public string ChapterCode { get; set; } = string.Empty;
    public string ChapterTitle { get; set; } = string.Empty;
    public Guid KnowledgePointId { get; set; }
    public Guid KnowledgePointRevisionId { get; set; }
    public string KnowledgePointCode { get; set; } = string.Empty;
    public string KnowledgePointName { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
}
