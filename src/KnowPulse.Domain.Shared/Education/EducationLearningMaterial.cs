using System;
using System.Collections.Generic;

namespace KnowPulse.Education;

public interface IEducationMaterialDisplay
{
    Guid Id { get; }
    string Title { get; }
}

public interface IEducationLearningMaterial
{
    Guid Id { get; }
    string Title { get; }
    Guid KnowledgeItemId { get; }
    Guid KnowledgeVersionId { get; }
    Guid? SchoolKnowledgeShareId { get; }
    List<Guid> AttachmentIds { get; }
}

public sealed record EducationStudyMaterial : IEducationLearningMaterial
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public Guid KnowledgeItemId { get; init; }
    public Guid KnowledgeVersionId { get; init; }
    public Guid? SchoolKnowledgeShareId { get; init; }
    public List<Guid> AttachmentIds { get; init; } = [];
}
