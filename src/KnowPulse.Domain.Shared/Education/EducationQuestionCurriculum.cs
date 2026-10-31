using System;
using System.Collections.Generic;

namespace KnowPulse.Education;

public class QuestionCurriculumPointWeight
{
    public Guid TextbookKnowledgePointMappingId { get; set; }
    public decimal Weight { get; set; }
    public decimal Score { get; set; }
}

public class EducationCurriculumSourceReference
{
    public Guid KnowledgeItemId { get; set; }
    public Guid KnowledgeVersionId { get; set; }
    public Guid ChunkId { get; set; }
    public Guid? SchoolKnowledgeShareId { get; set; }
    public List<Guid> CurriculumBindingIds { get; set; } = [];
    public List<Guid> TextbookKnowledgePointMappingIds { get; set; } = [];
}
