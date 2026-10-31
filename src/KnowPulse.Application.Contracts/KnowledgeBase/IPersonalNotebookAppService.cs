using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace KnowPulse.KnowledgeBase;

public interface IPersonalNotebookAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PagedResultDto<KnowledgeItemDto>> GetListAsync(GetPersonalKnowledgeItemListInput input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PersonalNotebookDto> GetAsync(Guid id);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PagedResultDto<PersonalNotebookVersionDto>> GetVersionsAsync(Guid id, PagedResultRequestDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PersonalNotebookVersionContentDto> GetVersionAsync(Guid id, Guid versionId);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PagedResultDto<KnowledgeAttachmentDto>> GetAttachmentsAsync(Guid id, Guid versionId, PagedResultRequestDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PersonalNotebookReceiptDto> CreateAsync(CreatePersonalNotebookDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PersonalNotebookReceiptDto> SaveAsync(Guid id, SavePersonalNotebookDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PersonalNotebookReceiptDto> DeleteAsync(Guid id, DeletePersonalNotebookDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PersonalNotebookReceiptDto?> GetOperationAsync(Guid operationId);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PersonalNotebookReceiptDto> UploadAttachmentAsync(Guid id, Guid versionId, Guid operationId, IRemoteStreamContent file);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<IRemoteStreamContent> DownloadAttachmentAsync(Guid id, Guid versionId, Guid attachmentId);
}
public class PersonalNotebookMetadataDto
{
    [Required, StringLength(256)] public string Title { get; set; } = "";
    [StringLength(1024)] public string? Summary { get; set; }
    [Range(1, 9999)] public int? MaterialYear { get; set; }
    [StringLength(128)] public string? SubjectName { get; set; }
    [MaxLength(20)] public List<string> CustomTags { get; set; } = [];
}
public class PersonalNotebookContentDto : PersonalNotebookMetadataDto
{
    [StringLength(8192)] public string? ContentText { get; set; }
}
public class CreatePersonalNotebookDto : PersonalNotebookContentDto
{
    public Guid OperationId { get; set; }
}
public class SavePersonalNotebookDto : CreatePersonalNotebookDto
{
    public Guid? ExpectedVersionId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
}
public class DeletePersonalNotebookDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
}
public class PersonalNotebookDto : PersonalNotebookMetadataDto
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public string ConcurrencyStamp { get; set; } = "";
    public Guid? LatestVersionId { get; set; }
    public int LatestVersionNumber { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
public class PersonalNotebookVersionDto
{
    public Guid Id { get; set; }
    public Guid NoteId { get; set; }
    public int VersionNumber { get; set; }
    public string Title { get; set; } = "";
    public KnowledgeVersionStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
public class PersonalNotebookVersionContentDto : PersonalNotebookVersionDto
{
    public string? ContentText { get; set; }
}
public class PersonalNotebookReceiptDto
{
    public Guid OperationId { get; set; }
    public Guid NoteId { get; set; }
    public PersonalNotebookCommandKind Kind { get; set; }
    public Guid? VersionId { get; set; }
    public int? VersionNumber { get; set; }
    public Guid? AttachmentId { get; set; }
    public string? ContentHash { get; set; }
    public long? FileSize { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}
