using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Content;

namespace KnowPulse.Controllers.Education;

[Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
[DisableAuditing]
[Route("api/knowledge/notebooks/{id:guid}/versions/{versionId:guid}/attachments")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PersonalNotebookAttachmentsController : KnowPulseController
{
    private readonly IPersonalNotebookAppService _notebooks;
    public PersonalNotebookAttachmentsController(IPersonalNotebookAppService notebooks) => _notebooks = notebooks;
    [HttpGet("{attachmentId:guid}")]
    public Task<IRemoteStreamContent> DownloadAsync(Guid id, Guid versionId, Guid attachmentId)
    {
        Response.Headers.CacheControl = "private, no-store"; Response.Headers.Pragma = "no-cache";
        return _notebooks.DownloadAttachmentAsync(id, versionId, attachmentId);
    }
    [HttpPost("operations/{operationId:guid}")]
    public async Task<PersonalNotebookReceiptDto> UploadAsync(Guid id, Guid versionId, Guid operationId, IFormFile file,
        [FromHeader(Name = "X-KnowPulse-File-Name")] string? encodedName = null)
    {
        Response.Headers.CacheControl = "private, no-store"; Response.Headers.Pragma = "no-cache";
        Check.NotNull(file, nameof(file)); string name;
        try { name = encodedName == null ? file.FileName : Uri.UnescapeDataString(encodedName); }
        catch (UriFormatException) { throw new BusinessException(PersonalNotebookErrors.Invalid); }
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || name.Any(char.IsControl) || name.IndexOfAny(['/', '\\']) >= 0) throw new BusinessException(PersonalNotebookErrors.Invalid);
        using var content = new RemoteStreamContent(file.OpenReadStream(), name, file.ContentType, file.Length);
        return await _notebooks.UploadAttachmentAsync(id, versionId, operationId, content);
    }
}
