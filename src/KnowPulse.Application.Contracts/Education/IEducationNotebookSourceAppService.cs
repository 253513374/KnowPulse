using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationNotebookSourceAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)] Task<EducationNotebookSourcePreviewDto> PreviewSourceAsync(PreviewEducationNotebookSourceDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)] Task<EducationNotebookSourcesDto> GetSourcesAsync(Guid id, Guid noteVersionId);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)] Task<EducationNotebookSourcesDto> AddSourceAsync(Guid id, AddEducationNotebookSourceDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)] Task<EducationNotebookSourcesDto> RemoveSourceAsync(Guid id, Guid sourceId, RemoveEducationNotebookSourceDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)] Task<EducationNotebookSourceDto?> GetSourceOperationAsync(Guid id, Guid operationId);
}
public class PreviewEducationNotebookSourceDto
{
    public EducationNotebookSourceKind Kind { get; set; }
    public Guid RecipientId { get; set; }
    public Guid ComponentId { get; set; }
}
public class AddEducationNotebookSourceDto
{
    public Guid OperationId { get; set; }
    public Guid NoteVersionId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
    [Required] public EducationNotebookSourcePointer Source { get; set; } = new();
}
public class RemoveEducationNotebookSourceDto
{
    public Guid OperationId { get; set; }
    public Guid NoteVersionId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
}
public class EducationNotebookSourcePreviewDto
{
    public bool Available { get; set; }
    public Guid? SchoolId { get; set; }
    public EducationNotebookSourcePointer? Source { get; set; }
    public string? Title { get; set; }
}
public class EducationNotebookSourceDto
{
    public Guid Id { get; set; }
    public Guid NoteVersionId { get; set; }
    public EducationNotebookSourcePointer Source { get; set; } = new();
    public bool Removed { get; set; }
    public bool Available { get; set; }
    public Guid? SchoolId { get; set; }
    public string? Title { get; set; }
    public string? OpenUrl { get; set; }
}
public class EducationNotebookSourcesDto
{
    public string ConcurrencyStamp { get; set; } = "";
    public List<EducationNotebookSourceDto> Items { get; set; } = [];
}
