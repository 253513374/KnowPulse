using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

[Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
public class EducationNotebookSourceAppService : KnowPulseAppService, IEducationNotebookSourceAppService
{
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private IUnitOfWorkManager Uow => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();
    private async Task<KnowledgeItem> OwnAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        await LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAccessGuard>().CurrentAccountAsync();
        var item = await Repo<KnowledgeItem>().FindAsync(id) ?? throw new AbpAuthorizationException();
        if (!item.LibraryId.HasValue || !item.OwnerUserId.HasValue) throw new AbpAuthorizationException();
        await EnsurePersonalKnowledgeOwnerAsync(item); return item;
    }
    private async Task VersionAsync(KnowledgeItem item, Guid versionId)
    {
        using var tracking = Repo<KnowledgeVersion>().DisableTracking();
        var version = await Repo<KnowledgeVersion>().FindAsync(versionId);
        if (version == null || version.KnowledgeItemId != item.Id || version.TenantId != item.TenantId) throw new AbpAuthorizationException();
    }
    private static string Hash(object value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private async Task<EducationNotebookSourcePreviewDto> ResolveAsync(PreviewEducationNotebookSourceDto input, Guid? expectedVersion = null)
    {
        if (!Enum.IsDefined(input.Kind) || input.RecipientId == Guid.Empty || input.ComponentId == Guid.Empty)
            throw new BusinessException(EducationNotebookSourceErrors.Invalid);
        try
        {
            var metadata = input.Kind == EducationNotebookSourceKind.AssessmentQuestion
                ? await LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAppService>().NotebookQuestionSourceAsync(input.RecipientId, input.ComponentId)
                : await LazyServiceProvider.LazyGetRequiredService<EducationStudyTaskAppService>().NotebookMaterialSourceAsync(input.RecipientId, input.ComponentId);
            if (expectedVersion.HasValue && expectedVersion != metadata.SourceVersionId) return new();
            return new() { Available = true, Title = metadata.Title, SchoolId = metadata.SchoolId, Source = new() { Kind = input.Kind, RecipientId = input.RecipientId,
                SourceVersionId = metadata.SourceVersionId, ComponentId = input.ComponentId } };
        }
        catch (Exception error) when (error is AbpAuthorizationException or BusinessException or EntityNotFoundException) { return new(); }
    }
    private async Task<EducationNotebookSourceDto> DtoAsync(EducationPrivateNotebookSource entry)
    {
        var available = entry.RemovedAtUtc.HasValue ? new EducationNotebookSourcePreviewDto() : await ResolveAsync(new()
        { Kind = entry.Source.Kind, RecipientId = entry.Source.RecipientId, ComponentId = entry.Source.ComponentId }, entry.Source.SourceVersionId);
        return new() { Id = entry.Id, NoteVersionId = entry.NoteVersionId, Source = entry.Source, Removed = entry.RemovedAtUtc.HasValue,
            Available = available.Available, Title = available.Title, SchoolId = available.SchoolId, OpenUrl = !available.Available ? null : entry.Source.Kind == EducationNotebookSourceKind.AssessmentQuestion
                ? $"/education/assessment-results?recipientId={entry.Source.RecipientId:D}"
                : $"/education/study-tasks/my?recipientId={entry.Source.RecipientId:D}" };
    }
    private async Task<EducationNotebookSourcesDto> ListAsync(Guid id, Guid versionId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: false);
        var item = await OwnAsync(id); await VersionAsync(item, versionId);
        var result = new EducationNotebookSourcesDto { ConcurrencyStamp = item.ConcurrencyStamp };
        foreach (var entry in item.GetPrivateNotebookSources().Where(x => x.NoteVersionId == versionId && !x.RemovedAtUtc.HasValue))
            result.Items.Add(await DtoAsync(entry));
        await OwnAsync(id); await uow.CompleteAsync(); return result;
    }
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationNotebookSourcePreviewDto> PreviewSourceAsync(PreviewEducationNotebookSourceDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        await LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAccessGuard>().CurrentAccountAsync();
        var result = await ResolveAsync(input);
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        await LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAccessGuard>().CurrentAccountAsync();
        return result;
    }
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationNotebookSourcesDto> GetSourcesAsync(Guid id, Guid noteVersionId) => ListAsync(id, noteVersionId);

    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationNotebookSourcesDto> AddSourceAsync(Guid id, AddEducationNotebookSourceDto input)
    {
        EducationRules.RequireId(input.OperationId);
        if (input.Source == null) throw new BusinessException(EducationNotebookSourceErrors.Invalid);
        input.Source.Validate();
        var fingerprint = Hash(new { Command = "add", id, Input = input });
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            using var tracking = Repo<KnowledgeItem>().EnableTracking(); var item = await OwnAsync(id); await VersionAsync(item, input.NoteVersionId);
            if (item.FindNotebookSourceOperation(input.OperationId, fingerprint) == null)
            {
                if (item.ConcurrencyStamp != input.ConcurrencyStamp) throw new AbpDbConcurrencyException();
                var source = await ResolveAsync(new() { Kind = input.Source.Kind, RecipientId = input.Source.RecipientId, ComponentId = input.Source.ComponentId }, input.Source.SourceVersionId);
                if (!source.Available) throw new BusinessException(EducationNotebookSourceErrors.Unavailable);
                item.AddNotebookSource(input.OperationId, input.NoteVersionId, input.Source, fingerprint, DateTime.UtcNow);
                await Repo<KnowledgeItem>().UpdateManyAsync([item], autoSave: true);
            }
            await uow.CompleteAsync();
        }
        catch (AbpDbConcurrencyException)
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false); var item = await OwnAsync(id);
            if (item.FindNotebookSourceOperation(input.OperationId, fingerprint) == null) throw;
            await recovery.CompleteAsync();
        }
        return await ListAsync(id, input.NoteVersionId);
    }
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationNotebookSourcesDto> RemoveSourceAsync(Guid id, Guid sourceId, RemoveEducationNotebookSourceDto input)
    {
        EducationRules.RequireId(input.OperationId);
        var fingerprint = Hash(new { Command = "remove", id, sourceId, Input = input });
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            using var tracking = Repo<KnowledgeItem>().EnableTracking(); var item = await OwnAsync(id); await VersionAsync(item, input.NoteVersionId);
            if (item.FindNotebookSourceOperation(input.OperationId, fingerprint) == null)
            {
                if (item.ConcurrencyStamp != input.ConcurrencyStamp) throw new AbpDbConcurrencyException();
                if (item.GetPrivateNotebookSources().SingleOrDefault(x => x.Id == sourceId)?.NoteVersionId != input.NoteVersionId)
                    throw new BusinessException(EducationNotebookSourceErrors.Invalid);
                item.RemoveNotebookSource(input.OperationId, sourceId, fingerprint, DateTime.UtcNow);
                await Repo<KnowledgeItem>().UpdateManyAsync([item], autoSave: true);
            }
            await uow.CompleteAsync();
        }
        catch (AbpDbConcurrencyException)
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false); var item = await OwnAsync(id);
            if (item.FindNotebookSourceOperation(input.OperationId, fingerprint) == null) throw;
            await recovery.CompleteAsync();
        }
        return await ListAsync(id, input.NoteVersionId);
    }
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationNotebookSourceDto?> GetSourceOperationAsync(Guid id, Guid operationId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: false); var item = await OwnAsync(id);
        var entry = item.FindNotebookSourceOperation(operationId);
        if (entry != null) await VersionAsync(item, entry.NoteVersionId);
        var result = entry == null ? null : await DtoAsync(entry);
        await OwnAsync(id); await uow.CompleteAsync(); return result;
    }
}
