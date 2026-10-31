using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace KnowPulse.Education;

public sealed class EducationCurriculumScopeDto
{
    public Guid TeacherTextbookSelectionId { get; set; }
    [Required, MinLength(1), MaxLength(3000)]
    public List<Guid> TextbookKnowledgePointMappingIds { get; set; } = [];
}
