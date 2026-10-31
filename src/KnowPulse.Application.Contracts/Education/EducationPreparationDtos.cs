using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Education;

public class GetMaterialBindingListInput : PagedResultRequestDto
{
    public Guid? KnowledgeItemId { get; set; }
    public Guid? KnowledgeVersionId { get; set; }
    public Guid? TextbookVersionId { get; set; }
    public Guid? TextbookKnowledgePointMappingId { get; set; }
    public bool IncludeEnded { get; set; }
}
public class ConfirmKnowledgeMaterialCurriculumBindingDto
{
    public Guid IdempotencyKey { get; set; }
    public Guid KnowledgeVersionId { get; set; }
    public Guid? KnowledgeChunkId { get; set; }
    public Guid TextbookKnowledgePointMappingId { get; set; }
    public bool ConfirmWholeMaterialForThisMapping { get; set; }
}
public class EndMaterialBindingDto
{
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = string.Empty;
}
public class KnowledgeMaterialCurriculumBindingDto : EntityDto<Guid>
{
    public Guid KnowledgeItemId { get; set; }
    public Guid KnowledgeVersionId { get; set; }
    public Guid? KnowledgeChunkId { get; set; }
    public Guid TextbookVersionId { get; set; }
    public Guid TextbookKnowledgePointMappingId { get; set; }
    public EducationCurriculumSnapshot Curriculum { get; set; } = new();
    public string MaterialTitle { get; set; } = string.Empty;
    public bool WholeMaterialAppliesToMapping { get; set; }
    public DateTime ConfirmedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public bool CanUseNow { get; set; }
}
public class GetEducationMaterialListInput : PagedResultRequestDto
{
    [StringLength(128)] public string? Keyword { get; set; }
}
public class EducationMaterialVersionOptionDto : EntityDto<Guid>
{
    public Guid KnowledgeItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
}
public class EducationMaterialChunkOptionDto : EntityDto<Guid>
{
    public int Sequence { get; set; }
    public int? SourcePage { get; set; }
    public string SourceTitle { get; set; } = string.Empty;
    public string Preview { get; set; } = string.Empty;
}
public class GetLessonPreparationListInput : PagedResultRequestDto
{
    [StringLength(128)] public string? Keyword { get; set; }
    public Guid? TextbookVersionId { get; set; }
    public Guid? ChapterId { get; set; }
    public Guid? KnowledgeVersionId { get; set; }
}
public class LessonPreparationContentDto
{
    [Required, StringLength(256)] public string Title { get; set; } = string.Empty;
    [Required] public EducationCurriculumScopeDto Scope { get; set; } = new();
    [Required, StringLength(8192)] public string Objectives { get; set; } = string.Empty;
    public List<Guid> BindingIds { get; set; } = [];
    public List<Guid> QuestionIds { get; set; } = [];
    public List<Guid> ExamPaperIds { get; set; } = [];
    [StringLength(8192)] public string? Reflection { get; set; }
}
public class CreateLessonPreparationDto : LessonPreparationContentDto { public Guid IdempotencyKey { get; set; } }
public class SaveLessonPreparationDto : LessonPreparationContentDto
{
    public Guid IdempotencyKey { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = string.Empty;
}
public class CopyLessonPreparationDto
{
    public Guid IdempotencyKey { get; set; }
    [Required, StringLength(256)] public string Title { get; set; } = string.Empty;
    [Required] public EducationCurriculumScopeDto Scope { get; set; } = new();
    // Copying content never silently grants a new use of historical references.
    public List<Guid> BindingIds { get; set; } = [];
    public List<Guid> QuestionIds { get; set; } = [];
    public List<Guid> ExamPaperIds { get; set; } = [];
}
public class LessonPreparationListDto : EntityDto<Guid>
{
    public string Title { get; set; } = string.Empty;
    public Guid CurrentRevisionId { get; set; }
    public int CurrentRevisionNumber { get; set; }
    public Guid TextbookVersionId { get; set; }
    public string TextbookTitle { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}
public class LessonPreparationDto : EntityDto<Guid>
{
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public LessonPreparationRevisionDto CurrentRevision { get; set; } = new();
    public Guid? CopiedFromRevisionId { get; set; }
}
public class LessonPreparationRevisionDto : EntityDto<Guid>
{
    public Guid PreparationId { get; set; }
    public int RevisionNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public EducationCurriculumSnapshot Curriculum { get; set; } = new();
    public string Objectives { get; set; } = string.Empty;
    public string? Reflection { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public bool NeedsCurriculumConfirmation { get; set; }
    public List<LessonPreparationReferenceDto> References { get; set; } = [];
}
public class LessonPreparationReferenceDto : EntityDto<Guid>
{
    public LessonPreparationReferenceKind Kind { get; set; }
    public Guid? MaterialBindingId { get; set; }
    public Guid? KnowledgeVersionId { get; set; }
    public Guid? KnowledgeChunkId { get; set; }
    public Guid? QuestionId { get; set; }
    public Guid? ExamPaperId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? SnapshotVersion { get; set; }
    public string? SourceConcurrencyStamp { get; set; }
    public bool CanUseNow { get; set; }
}
public class GetPreparationReferenceOptionsInput : PagedResultRequestDto
{
    [Required] public EducationCurriculumScopeDto Scope { get; set; } = new();
    public LessonPreparationReferenceKind Kind { get; set; }
    [StringLength(128)] public string? Keyword { get; set; }
    public bool IncludeSchoolShared { get; set; }
}
public class EducationPreparationReferenceOptionDto : EntityDto<Guid>
{
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public LessonPreparationReferenceKind Kind { get; set; }
}
public class CopyReflectionToKnowledgeDto
{
    public Guid IdempotencyKey { get; set; }
    [Required, StringLength(256)] public string Title { get; set; } = string.Empty;
}
