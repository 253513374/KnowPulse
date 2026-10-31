using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Education;

public class CurriculumContextDto
{
    public Guid SchoolId { get; set; }
    public bool CanManage { get; set; }
    public List<EducationSubjectDto> Subjects { get; set; } = [];
    public List<MyTeachingContextDto> TeachingContexts { get; set; } = [];
}

public class MyTeachingContextDto : EntityDto<Guid>
{
    public Guid AcademicTermId { get; set; }
    public string TermName { get; set; } = string.Empty;
    public Guid SchoolClassId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public int Grade { get; set; }
    public Guid EducationSubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
}

public class GetTextbookListInput : PagedResultRequestDto
{
    public Guid? EducationSubjectId { get; set; }
    [Range(7, 9)] public int? Grade { get; set; }
    public bool IncludeUnpublished { get; set; }
}

public class TextbookVersionDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public Guid EducationSubjectId { get; set; }
    public string CatalogCode { get; set; } = string.Empty;
    public int CatalogRevision { get; set; }
    public Guid? PreviousVersionId { get; set; }
    public string Stage { get; set; } = "JuniorSecondary";
    public int Grade { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Edition { get; set; } = string.Empty;
    public string Volume { get; set; } = string.Empty;
    public TextbookVersionStatus Status { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public List<TextbookChapterDto> Chapters { get; set; } = [];
    public List<TextbookKnowledgePointMappingDto> Mappings { get; set; } = [];
}

public class CreateTextbookDraftDto
{
    public Guid IdempotencyKey { get; set; }
    public Guid SchoolId { get; set; }
    public Guid EducationSubjectId { get; set; }
    [Required, StringLength(64)] public string CatalogCode { get; set; } = string.Empty;
    [Range(7, 9)] public int Grade { get; set; } = 7;
    [Required, StringLength(256)] public string Title { get; set; } = string.Empty;
    [Required, StringLength(128)] public string Publisher { get; set; } = string.Empty;
    [Required, StringLength(128)] public string Edition { get; set; } = string.Empty;
    [Required, StringLength(64)] public string Volume { get; set; } = string.Empty;
}

public class ReviseTextbookDto
{
    public Guid IdempotencyKey { get; set; }
}

public class SaveTextbookDraftDto
{
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = string.Empty;
    [Range(7, 9)] public int Grade { get; set; } = 7;
    [Required, StringLength(256)] public string Title { get; set; } = string.Empty;
    [Required, StringLength(128)] public string Publisher { get; set; } = string.Empty;
    [Required, StringLength(128)] public string Edition { get; set; } = string.Empty;
    [Required, StringLength(64)] public string Volume { get; set; } = string.Empty;
    public List<SaveTextbookChapterDto> Chapters { get; set; } = [];
    public List<SaveTextbookMappingDto> Mappings { get; set; } = [];
}

public class SaveTextbookChapterDto : EntityDto<Guid>
{
    public Guid? ParentChapterId { get; set; }
    [Required, StringLength(64)] public string Code { get; set; } = string.Empty;
    [Required, StringLength(256)] public string Title { get; set; } = string.Empty;
    [Range(0, int.MaxValue)] public int DisplayOrder { get; set; }
}

public class TextbookChapterDto : SaveTextbookChapterDto
{
    public bool HasConfirmedMapping { get; set; }
}

public class SaveTextbookMappingDto
{
    public Guid ChapterId { get; set; }
    public Guid KnowledgePointRevisionId { get; set; }
}

public class TextbookKnowledgePointMappingDto : EntityDto<Guid>
{
    public Guid ChapterId { get; set; }
    public Guid KnowledgePointId { get; set; }
    public Guid KnowledgePointRevisionId { get; set; }
    public string KnowledgePointCode { get; set; } = string.Empty;
    public string KnowledgePointName { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public Guid ConfirmedByUserId { get; set; }
    public DateTime ConfirmedAtUtc { get; set; }
}

public class CourseKnowledgePointDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public Guid EducationSubjectId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int CurrentRevisionNumber { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public List<CourseKnowledgePointRevisionDto> Revisions { get; set; } = [];
}

public class CourseKnowledgePointRevisionDto : EntityDto<Guid>
{
    public Guid KnowledgePointId { get; set; }
    public int RevisionNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentRevisionId { get; set; }
}

public class CreateCourseKnowledgePointDto
{
    public Guid IdempotencyKey { get; set; }
    public Guid SchoolId { get; set; }
    public Guid EducationSubjectId { get; set; }
    [Required, StringLength(64)] public string Code { get; set; } = string.Empty;
    [Required, StringLength(128)] public string Name { get; set; } = string.Empty;
    public Guid? ParentRevisionId { get; set; }
}

public class CreateKnowledgePointRevisionDto
{
    public Guid IdempotencyKey { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = string.Empty;
    [Required, StringLength(128)] public string Name { get; set; } = string.Empty;
    public Guid? ParentRevisionId { get; set; }
}

public class SelectTeacherTextbookDto
{
    public Guid IdempotencyKey { get; set; }
    public Guid TextbookVersionId { get; set; }
}

public class TeacherTextbookSelectionDto : EntityDto<Guid>
{
    public Guid TeachingAssignmentId { get; set; }
    public Guid TextbookVersionId { get; set; }
    public string TextbookTitle { get; set; } = string.Empty;
    public int CatalogRevision { get; set; }
    public TextbookVersionStatus TextbookStatus { get; set; }
    public DateTime SelectedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
}
