using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Volo.Abp.Content;
using Volo.Abp.Auditing;

namespace KnowPulse.KnowledgeBase;

[Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
public class PersonalNotebookAppService : KnowPulseAppService, IPersonalNotebookAppService
{
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private IUnitOfWorkManager Uow => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();
    private KnowledgeItemAppService Items => LazyServiceProvider.LazyGetRequiredService<KnowledgeItemAppService>();
    private IKnowledgeLibraryAppService Library => LazyServiceProvider.LazyGetRequiredService<IKnowledgeLibraryAppService>();
    private async Task AccountAsync()
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        await LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAccessGuard>().CurrentAccountAsync();
    }
    private async Task<KnowledgeItem> OwnAsync(Guid id, bool includeDeleted = false)
    {
        await AccountAsync(); using var deleted = includeDeleted ? LazyServiceProvider.LazyGetRequiredService<IDataFilter>().Disable<ISoftDelete>() : null;
        var note = await Repo<KnowledgeItem>().FindAsync(id) ?? throw new AbpAuthorizationException();
        if (note.MaterialType != PersonalMaterialType.Note || !note.LibraryId.HasValue || !note.OwnerUserId.HasValue) throw new AbpAuthorizationException();
        await EnsurePersonalKnowledgeOwnerAsync(note); return note;
    }
    private async Task<KnowledgeVersion> VersionAsync(KnowledgeItem note, Guid versionId)
    {
        var v = await Repo<KnowledgeVersion>().FindAsync(versionId) ?? throw new AbpAuthorizationException();
        if (v.KnowledgeItemId != note.Id || v.TenantId != note.TenantId) throw new AbpAuthorizationException(); return v;
    }
    private async Task<KnowledgeVersion?> LatestAsync(Guid id) => await AsyncExecuter.FirstOrDefaultAsync((await Repo<KnowledgeVersion>().GetQueryableAsync())
        .Where(x => x.KnowledgeItemId == id && x.TenantId == CurrentTenant.Id).OrderByDescending(x => x.VersionNumber));
    private static int Take(PagedResultRequestDto input)
    { if (input.SkipCount < 0 || input.MaxResultCount is < 1 or > 100) throw new BusinessException(PersonalNotebookErrors.Invalid); return input.MaxResultCount; }
    private static string Hash(object value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static void Operation(Guid id) { if (id == Guid.Empty) throw new BusinessException(PersonalNotebookErrors.Invalid); }
    private static PersonalNotebookReceiptDto Receipt(PersonalNotebookCommand c) => new() { OperationId = c.Id, NoteId = c.NoteId, Kind = c.Kind,
        VersionId = c.VersionId, VersionNumber = c.VersionNumber, AttachmentId = c.AttachmentId, ContentHash = c.ContentHash, FileSize = c.FileSize, RecordedAtUtc = c.RecordedAtUtc };
    private static PersonalNotebookVersionDto VersionDto(KnowledgeVersion v) => new() { Id = v.Id, NoteId = v.KnowledgeItemId, VersionNumber = v.VersionNumber,
        Title = v.Title, Status = v.Status, CreatedAtUtc = DateTime.SpecifyKind(v.CreationTime, DateTimeKind.Utc) };
    private async Task<PersonalNotebookCommand?> PriorAsync(Guid operationId, string? fingerprint = null)
    {
        await AccountAsync(); var c = await Repo<PersonalNotebookCommand>().FindAsync(operationId); if (c == null) return null;
        if (c.OwnerUserId != CurrentUser.Id || c.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        var note = await OwnAsync(c.NoteId, includeDeleted: true);
        if (c.VersionId.HasValue) await VersionAsync(note, c.VersionId.Value);
        if (fingerprint != null && c.RequestFingerprint != fingerprint) throw new BusinessException(PersonalNotebookErrors.OperationConflict);
        return c;
    }
    private async Task<PersonalNotebookReceiptDto> WriteAsync(Guid operationId, string fingerprint, Func<Task<PersonalNotebookCommand>> write)
    {
        Operation(operationId);
        try {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var prior = await PriorAsync(operationId, fingerprint);
            var c = prior ?? await write();
            if (prior == null) await Repo<PersonalNotebookCommand>().InsertAsync(c, autoSave: true);
            await AccountAsync(); await uow.CompleteAsync(); return Receipt(c);
        } catch (Exception error) when (error is not AbpAuthorizationException and not OperationCanceledException) {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            var c = await PriorAsync(operationId, fingerprint); if (c == null) throw;
            await recovery.CompleteAsync(); return Receipt(c);
        }
    }
    private static void Content(KnowledgeItem note, PersonalNotebookContentDto input)
    {
        if (input.CustomTags == null || input.CustomTags.Count > 20) throw new BusinessException(PersonalNotebookErrors.Invalid);
        note.Rename(input.Title); note.ChangeSummary(input.Summary);
        note.OrganizePersonalMaterial(PersonalMaterialType.Note, input.MaterialYear, input.SubjectName, input.CustomTags);
    }

    public virtual async Task<PagedResultDto<KnowledgeItemDto>> GetListAsync(GetPersonalKnowledgeItemListInput input)
    { await AccountAsync(); Take(input); input.MaterialType = PersonalMaterialType.Note; var result = await Library.GetMyItemsAsync(input); await AccountAsync(); return result; }

    public virtual async Task<PersonalNotebookDto> GetAsync(Guid id)
    {
        var note = await OwnAsync(id); var v = await LatestAsync(id); await OwnAsync(id);
        return new() { Id = note.Id, OwnerUserId = note.OwnerUserId!.Value, ConcurrencyStamp = note.ConcurrencyStamp, Title = note.Title, Summary = note.Summary,
            SubjectName = note.SubjectName, MaterialYear = note.MaterialYear, CustomTags = note.GetPersonalTags().ToList(), LatestVersionId = v?.Id,
            LatestVersionNumber = v?.VersionNumber ?? 0, UpdatedAtUtc = DateTime.SpecifyKind(note.LastModificationTime ?? note.CreationTime, DateTimeKind.Utc) };
    }
    public virtual async Task<PagedResultDto<PersonalNotebookVersionDto>> GetVersionsAsync(Guid id, PagedResultRequestDto input)
    {
        await OwnAsync(id); var take = Take(input); var query = (await Repo<KnowledgeVersion>().GetQueryableAsync()).Where(x => x.KnowledgeItemId == id && x.TenantId == CurrentTenant.Id);
        var count = await AsyncExecuter.CountAsync(query); var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.VersionNumber).Skip(input.SkipCount).Take(take));
        await OwnAsync(id); return new(count, rows.Select(VersionDto).ToList());
    }
    public virtual async Task<PersonalNotebookVersionContentDto> GetVersionAsync(Guid id, Guid versionId)
    {
        var v = await VersionAsync(await OwnAsync(id), versionId); await OwnAsync(id);
        return new() { Id = v.Id, NoteId = id, VersionNumber = v.VersionNumber, Title = v.Title, Status = v.Status, ContentText = v.ContentText,
            CreatedAtUtc = DateTime.SpecifyKind(v.CreationTime, DateTimeKind.Utc) };
    }
    public virtual async Task<PagedResultDto<KnowledgeAttachmentDto>> GetAttachmentsAsync(Guid id, Guid versionId, PagedResultRequestDto input)
    {
        await VersionAsync(await OwnAsync(id), versionId); var take = Take(input);
        var query = (await Repo<KnowledgeAttachment>().GetQueryableAsync()).Where(x => x.KnowledgeVersionId == versionId && x.TenantId == CurrentTenant.Id);
        var count = await AsyncExecuter.CountAsync(query); var rows = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.CreationTime).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take));
        await OwnAsync(id); return new(count, rows.Select(x => new KnowledgeAttachmentDto { Id = x.Id, KnowledgeVersionId = x.KnowledgeVersionId, FileName = x.FileName, ContentType = x.ContentType, Size = x.Size }).ToList());
    }
    [UnitOfWork(IsDisabled = true)]
    public virtual Task<PersonalNotebookReceiptDto> CreateAsync(CreatePersonalNotebookDto input)
        => WriteAsync(input.OperationId, Hash(new { Command = "create-note", Input = input }), async () => {
            var library = await Library.GetMyLibraryAsync();
            var note = new KnowledgeItem(GuidGenerator.Create(), CurrentTenant.Id, input.Title, summary: input.Summary, libraryId: library.Id, ownerUserId: CurrentUser.Id);
            Content(note, input); await Repo<KnowledgeItem>().InsertAsync(note, autoSave: true);
            var v = await Items.AppendVersionAsync(note, GuidGenerator.Create(), input.Title, input.ContentText);
            return new(input.OperationId, note, PersonalNotebookCommandKind.Created, Hash(new { Command = "create-note", Input = input }), DateTime.UtcNow, v);
        });
    [UnitOfWork(IsDisabled = true)]
    public virtual Task<PersonalNotebookReceiptDto> SaveAsync(Guid id, SavePersonalNotebookDto input)
        => WriteAsync(input.OperationId, Hash(new { Command = "save-note", id, Input = input }), async () => {
            using var tracking = Repo<KnowledgeItem>().EnableTracking(); var note = await OwnAsync(id);
            if (input.ExpectedVersionId == Guid.Empty || note.ConcurrencyStamp != input.ConcurrencyStamp || (await LatestAsync(id))?.Id != input.ExpectedVersionId)
                throw new BusinessException(PersonalNotebookErrors.Changed);
            Content(note, input); var v = await Items.AppendVersionAsync(note, GuidGenerator.Create(), input.Title, input.ContentText);
            return new(input.OperationId, note, PersonalNotebookCommandKind.SavedVersion, Hash(new { Command = "save-note", id, Input = input }), DateTime.UtcNow, v);
        });
    [UnitOfWork(IsDisabled = true)]
    public virtual Task<PersonalNotebookReceiptDto> DeleteAsync(Guid id, DeletePersonalNotebookDto input)
        => WriteAsync(input.OperationId, Hash(new { Command = "delete-note", id, Input = input }), async () => {
            using var tracking = Repo<KnowledgeItem>().EnableTracking(); var note = await OwnAsync(id);
            if (note.ConcurrencyStamp != input.ConcurrencyStamp) throw new BusinessException(PersonalNotebookErrors.Changed);
            await Repo<KnowledgeItem>().UpdateManyAsync([note], autoSave: true); await Items.DeleteAsync(id);
            return new(input.OperationId, note, PersonalNotebookCommandKind.Deleted, Hash(new { Command = "delete-note", id, Input = input }), DateTime.UtcNow);
        });
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<PersonalNotebookReceiptDto?> GetOperationAsync(Guid operationId)
    {
        Operation(operationId); using var read = Uow.Begin(requiresNew: true, isTransactional: false);
        var c = await PriorAsync(operationId); await AccountAsync(); await read.CompleteAsync(); return c == null ? null : Receipt(c);
    }
    [UnitOfWork(IsDisabled = true)]
    [DisableAuditing]
    public virtual async Task<PersonalNotebookReceiptDto> UploadAttachmentAsync(Guid id, Guid versionId, Guid operationId, IRemoteStreamContent file)
    {
        Operation(operationId); Check.NotNull(file, nameof(file)); long limit;
        using (var read = Uow.Begin(requiresNew: true, isTransactional: false)) {
            await VersionAsync(await OwnAsync(id), versionId); var policy = LazyServiceProvider.LazyGetRequiredService<IOptions<PersonalKnowledgeAttachmentOptions>>().Value;
            policy.EnsureValid(); limit = policy.MaxFileBytes; await read.CompleteAsync();
        }
        var name = KnowledgeItemAppService.NormalizeFileName(file.FileName); var type = KnowledgeItemAppService.NormalizeContentType(file.ContentType);
        using var source = file.GetStream(); using var bytes = new MemoryStream(); var buffer = new byte[81920];
        while (true) { var remaining = limit - bytes.Length; var read = await source.ReadAsync(buffer.AsMemory(0, remaining < buffer.Length ? (int)remaining + 1 : buffer.Length));
            if (read == 0) break; if (read > remaining) throw new BusinessException(KnowPulseDomainErrorCodes.PersonalKnowledgeAttachmentTooLarge);
            await bytes.WriteAsync(buffer.AsMemory(0, read)); }
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length))));
        var fingerprint = Hash(new { Command = "note-attachment", id, versionId, operationId, name, type, Size = bytes.Length, Hash = hash });
        using (var read = Uow.Begin(requiresNew: true, isTransactional: false)) {
            var prior = await PriorAsync(operationId, fingerprint); await read.CompleteAsync(); if (prior != null) return Receipt(prior);
        }
        try {
            bytes.Position = 0; using var upload = new RemoteStreamContent(bytes, name, type, bytes.Length, disposeStream: false);
            PersonalNotebookCommand? command = null;
            await Items.UploadNotebookAttachmentAsync(id, versionId, operationId, upload, async attachment => {
                var note = await OwnAsync(id); var v = await VersionAsync(note, versionId); var a = await Repo<KnowledgeAttachment>().GetAsync(attachment.Id);
                command = new(operationId, note, PersonalNotebookCommandKind.UploadedAttachment, fingerprint, DateTime.UtcNow, v, a, hash);
                await Repo<PersonalNotebookCommand>().InsertAsync(command, autoSave: true); await AccountAsync();
            });
            return Receipt(command ?? throw new BusinessException(PersonalNotebookErrors.Invalid));
        } catch (Exception error) when (error is not AbpAuthorizationException and not OperationCanceledException) {
            using var read = Uow.Begin(requiresNew: true, isTransactional: false); var prior = await PriorAsync(operationId, fingerprint); if (prior == null) throw;
            await read.CompleteAsync(); return Receipt(prior);
        }
    }
    public virtual async Task<IRemoteStreamContent> DownloadAttachmentAsync(Guid id, Guid versionId, Guid attachmentId)
    {
        await VersionAsync(await OwnAsync(id), versionId);
        var file = await Items.DownloadAttachmentAsync(id, versionId, attachmentId);
        try { await OwnAsync(id); return file; }
        catch { file.Dispose(); throw; }
    }
}
