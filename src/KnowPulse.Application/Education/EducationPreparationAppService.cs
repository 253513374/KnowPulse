using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.Permissions;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

[Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
public class EducationPreparationAppService : KnowPulseAppService, IEducationPreparationAppService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private IUnitOfWorkManager Uow => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();
    private EducationCurriculumAccessGuard Curriculum => LazyServiceProvider.LazyGetRequiredService<EducationCurriculumAccessGuard>();
    private KnowledgeSourceAccessGuard Sources => LazyServiceProvider.LazyGetRequiredService<KnowledgeSourceAccessGuard>();

    private async Task<T?> ReferenceAsync<T>(Guid id) where T : class, IEntity<Guid>
    {
        using var tracking = Repo<T>().DisableTracking();
        return await Repo<T>().FindAsync(id);
    }
    private async Task<List<T>> ReferencesAsync<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity<Guid>
    {
        using var tracking = Repo<T>().DisableTracking();
        return await Repo<T>().GetListAsync(predicate);
    }
    private async Task CheckAccessAsync()
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        if (!CurrentUser.IsAuthenticated || !CurrentUser.Id.HasValue) throw new AbpAuthorizationException();
        var user = await ReferenceAsync<IdentityUser>(CurrentUser.Id.Value);
        if (user == null || !user.IsActive || user.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
    }
    private void Own(Guid owner, Guid? tenant)
    {
        if (owner != CurrentUser.Id || tenant != CurrentTenant.Id) throw new AbpAuthorizationException();
    }
    private static BusinessException Conflict() => new(EducationPreparationErrorCodes.ConflictingOperation);
    private static void Stamp(string actual, string expected)
    {
        if (string.IsNullOrWhiteSpace(expected) || actual != expected) throw new AbpDbConcurrencyException();
    }
    private static int Page(PagedResultRequestDto input)
    {
        if (input.SkipCount < 0 || input.MaxResultCount < 1) throw new BusinessException(EducationPreparationErrorCodes.InvalidSource);
        return Math.Min(input.MaxResultCount, 100);
    }
    private static async Task<T> CompleteAsync<T>(IUnitOfWork uow, Func<Task<T>> map)
    {
        await uow.SaveChangesAsync();
        var result = await map();
        await uow.CompleteAsync();
        return result;
    }

    public virtual async Task<KnowledgeMaterialCurriculumBindingDto> ConfirmBindingAsync(ConfirmKnowledgeMaterialCurriculumBindingDto input)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        await CheckAccessAsync();
        EducationRules.RequireId(input.IdempotencyKey);
        EducationRules.RequireId(input.KnowledgeVersionId);
        EducationRules.RequireId(input.TextbookKnowledgePointMappingId);
        if (input.KnowledgeChunkId == Guid.Empty || (!input.KnowledgeChunkId.HasValue && !input.ConfirmWholeMaterialForThisMapping))
            throw new BusinessException(EducationPreparationErrorCodes.WholeMaterialConfirmationRequired);
        var existing = await ReferenceAsync<KnowledgeMaterialCurriculumBinding>(input.IdempotencyKey);
        if (existing != null)
        {
            Own(existing.OwnerUserId, existing.TenantId);
            if (existing.KnowledgeVersionId != input.KnowledgeVersionId || existing.KnowledgeChunkId != input.KnowledgeChunkId ||
                existing.TextbookKnowledgePointMappingId != input.TextbookKnowledgePointMappingId) throw Conflict();
            return await CompleteAsync(uow, () => MapBindingAsync(existing));
        }
        var snapshot = await Curriculum.GetMappingAsync(input.TextbookKnowledgePointMappingId, forUpdate: true);
        var version = await ReferenceAsync<KnowledgeVersion>(input.KnowledgeVersionId) ?? throw new AbpAuthorizationException();
        var item = await ReferenceAsync<KnowledgeItem>(version.KnowledgeItemId) ?? throw new AbpAuthorizationException();
        if (!item.OwnerUserId.HasValue || !item.LibraryId.HasValue) throw new AbpAuthorizationException();
        Own(item.OwnerUserId.Value, item.TenantId);
        var library = await ReferenceAsync<KnowledgeLibrary>(item.LibraryId.Value);
        if (library == null || library.OwnerUserId != item.OwnerUserId || library.TenantId != item.TenantId)
            throw new AbpAuthorizationException();
        // The shared source guard captures and forces the same private-version barrier used by archive/end commands.
        await AuthorizeMaterialAsync(item.Id, version.Id, input.KnowledgeChunkId, requireCurrent: true, forUpdate: true);
        var binding = new KnowledgeMaterialCurriculumBinding(input.IdempotencyKey, item.TenantId, item.OwnerUserId.Value,
            item.LibraryId.Value, item.Id, version.Id, input.KnowledgeChunkId, snapshot.TextbookVersionId,
            input.TextbookKnowledgePointMappingId, JsonSerializer.Serialize(snapshot, Json), version.Title,
            input.ConfirmWholeMaterialForThisMapping, DateTime.UtcNow);
        await Repo<KnowledgeMaterialCurriculumBinding>().InsertAsync(binding);
        return await CompleteAsync(uow, () => MapBindingAsync(binding));
    }

    public virtual async Task<KnowledgeMaterialCurriculumBindingDto> EndBindingAsync(Guid id, EndMaterialBindingDto input)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        await CheckAccessAsync();
        var binding = await Repo<KnowledgeMaterialCurriculumBinding>().GetAsync(id);
        Own(binding.OwnerUserId, binding.TenantId);
        if (!binding.EndedAtUtc.HasValue)
        {
            Stamp(binding.ConcurrencyStamp, input.ConcurrencyStamp);
            var version = await ReferenceAsync<KnowledgeVersion>(binding.KnowledgeVersionId) ?? throw new AbpAuthorizationException();
            if (version.TenantId != binding.TenantId || version.KnowledgeItemId != binding.KnowledgeItemId) throw new AbpAuthorizationException();
            await Repo<KnowledgeVersion>().UpdateManyAsync([version], autoSave: true);
            binding.End(DateTime.UtcNow);
            await Repo<KnowledgeMaterialCurriculumBinding>().UpdateAsync(binding);
        }
        return await CompleteAsync(uow, () => MapBindingAsync(binding));
    }

    public virtual async Task<PagedResultDto<KnowledgeMaterialCurriculumBindingDto>> GetMyBindingsAsync(GetMaterialBindingListInput input)
    {
        await CheckAccessAsync();
        var take = Page(input);
        using var tracking = Repo<KnowledgeMaterialCurriculumBinding>().DisableTracking();
        var query = (await Repo<KnowledgeMaterialCurriculumBinding>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.OwnerUserId == CurrentUser.Id);
        if (!input.IncludeEnded) query = query.Where(x => !x.EndedAtUtc.HasValue);
        if (input.KnowledgeItemId.HasValue) query = query.Where(x => x.KnowledgeItemId == input.KnowledgeItemId);
        if (input.KnowledgeVersionId.HasValue) query = query.Where(x => x.KnowledgeVersionId == input.KnowledgeVersionId);
        if (input.TextbookVersionId.HasValue) query = query.Where(x => x.TextbookVersionId == input.TextbookVersionId);
        if (input.TextbookKnowledgePointMappingId.HasValue) query = query.Where(x => x.TextbookKnowledgePointMappingId == input.TextbookKnowledgePointMappingId);
        var count = await AsyncExecuter.CountAsync(query);
        var result = new List<KnowledgeMaterialCurriculumBindingDto>();
        foreach (var row in await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.ConfirmedAtUtc).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take)))
            result.Add(await MapBindingAsync(row));
        return new PagedResultDto<KnowledgeMaterialCurriculumBindingDto>(count, result);
    }

    public virtual async Task<PagedResultDto<EducationMaterialVersionOptionDto>> GetMyMaterialVersionsAsync(GetEducationMaterialListInput input)
    {
        await CheckAccessAsync();
        var take = Page(input);
        using var versionsTracking = Repo<KnowledgeVersion>().DisableTracking();
        using var itemsTracking = Repo<KnowledgeItem>().DisableTracking();
        using var librariesTracking = Repo<KnowledgeLibrary>().DisableTracking();
        var query = from version in await Repo<KnowledgeVersion>().GetQueryableAsync()
                    join item in await Repo<KnowledgeItem>().GetQueryableAsync() on version.KnowledgeItemId equals item.Id
                    join library in await Repo<KnowledgeLibrary>().GetQueryableAsync() on item.LibraryId equals (Guid?)library.Id
                    where version.TenantId == CurrentTenant.Id && item.TenantId == CurrentTenant.Id && item.OwnerUserId == CurrentUser.Id &&
                        library.TenantId == CurrentTenant.Id && library.OwnerUserId == CurrentUser.Id &&
                        item.LibraryId.HasValue && item.IsActive && item.CurrentPublishedVersionId == version.Id &&
                        version.Status == KnowledgeVersionStatus.Published && version.IsCurrentPublished
                    select new EducationMaterialVersionOptionDto { Id = version.Id, KnowledgeItemId = item.Id, Title = version.Title, VersionNumber = version.VersionNumber };
        if (!string.IsNullOrWhiteSpace(input.Keyword)) query = query.Where(x => x.Title.Contains(input.Keyword));
        return new PagedResultDto<EducationMaterialVersionOptionDto>(await AsyncExecuter.CountAsync(query),
            await AsyncExecuter.ToListAsync(query.OrderBy(x => x.Title).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take)));
    }

    public virtual async Task<PagedResultDto<EducationMaterialChunkOptionDto>> GetMaterialChunksAsync(Guid versionId, PagedResultRequestDto input)
    {
        await CheckAccessAsync();
        var take = Page(input);
        var version = await ReferenceAsync<KnowledgeVersion>(versionId) ?? throw new AbpAuthorizationException();
        var item = await ReferenceAsync<KnowledgeItem>(version.KnowledgeItemId) ?? throw new AbpAuthorizationException();
        if (!item.OwnerUserId.HasValue) throw new AbpAuthorizationException();
        Own(item.OwnerUserId.Value, item.TenantId);
        await AuthorizeMaterialAsync(item.Id, version.Id, null, requireCurrent: true);
        using var chunkTracking = Repo<KnowledgeChunk>().DisableTracking();
        using var jobTracking = Repo<KnowledgeIndexJob>().DisableTracking();
        var jobs = await Repo<KnowledgeIndexJob>().GetQueryableAsync();
        var query = (await Repo<KnowledgeChunk>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id &&
            x.KnowledgeItemId == item.Id && x.KnowledgeVersionId == version.Id && x.IsActive && x.IsPublishedSnapshot &&
            jobs.Any(job => job.Id == x.KnowledgeIndexJobId && job.TenantId == x.TenantId && job.KnowledgeVersionId == x.KnowledgeVersionId && job.Status == KnowledgeIndexJobStatus.Indexed));
        var count = await AsyncExecuter.CountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.Sequence).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take));
        return new PagedResultDto<EducationMaterialChunkOptionDto>(count, rows.Select(x => new EducationMaterialChunkOptionDto
        { Id = x.Id, Sequence = x.Sequence, SourcePage = x.SourcePage, SourceTitle = x.SourceTitle, Preview = x.Content[..Math.Min(160, x.Content.Length)] }).ToList());
    }

    public virtual async Task<LessonPreparationDto> CreatePreparationAsync(CreateLessonPreparationDto input)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        await CheckAccessAsync();
        return await CreateCoreAsync(input.IdempotencyKey, input, null, Fingerprint(input, "create"), uow);
    }
    public virtual async Task<LessonPreparationDto> SavePreparationAsync(Guid id, SaveLessonPreparationDto input)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        await CheckAccessAsync();
        var preparation = await Repo<LessonPreparation>().GetAsync(id);
        Own(preparation.OwnerUserId, preparation.TenantId);
        EducationRules.RequireId(input.IdempotencyKey);
        var fingerprint = Fingerprint(input, "save", id);
        var repeated = await ReferenceAsync<LessonPreparationRevision>(input.IdempotencyKey);
        if (repeated != null)
        {
            Own(repeated.OwnerUserId, repeated.TenantId);
            if (repeated.PreparationId != id || repeated.RequestFingerprint != fingerprint) throw Conflict();
            return await CompleteAsync(uow, () => MapPreparationAsync(preparation));
        }
        Stamp(preparation.ConcurrencyStamp, input.ConcurrencyStamp);
        await AddRevisionAsync(preparation, input.IdempotencyKey, input, fingerprint);
        return await CompleteAsync(uow, () => MapPreparationAsync(preparation));
    }
    public virtual async Task<LessonPreparationDto> CopyPreparationAsync(Guid sourceRevisionId, CopyLessonPreparationDto input)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        await CheckAccessAsync();
        var source = await ReferenceAsync<LessonPreparationRevision>(sourceRevisionId) ?? throw new AbpAuthorizationException();
        Own(source.OwnerUserId, source.TenantId);
        var content = new LessonPreparationContentDto { Title = input.Title, Scope = input.Scope, Objectives = source.Objectives,
            Reflection = source.Reflection, BindingIds = input.BindingIds, QuestionIds = input.QuestionIds, ExamPaperIds = input.ExamPaperIds };
        return await CreateCoreAsync(input.IdempotencyKey, content, source.Id, Fingerprint(content, "copy", source.Id), uow);
    }
    private async Task<LessonPreparationDto> CreateCoreAsync(Guid operationId, LessonPreparationContentDto input, Guid? copiedFrom,
        string fingerprint, IUnitOfWork uow)
    {
        EducationRules.RequireId(operationId);
        var existing = await ReferenceAsync<LessonPreparation>(operationId);
        if (existing != null)
        {
            Own(existing.OwnerUserId, existing.TenantId);
            if (existing.CreateFingerprint != fingerprint || existing.CopiedFromRevisionId != copiedFrom) throw Conflict();
            return await CompleteAsync(uow, () => MapPreparationAsync(existing));
        }
        var library = await LazyServiceProvider.LazyGetRequiredService<IKnowledgeLibraryAppService>().GetMyLibraryAsync();
        var preparation = new LessonPreparation(operationId, CurrentTenant.Id, CurrentUser.Id!.Value, library.Id, input.Title,
            fingerprint, DateTime.UtcNow, copiedFrom);
        await Repo<LessonPreparation>().InsertAsync(preparation);
        await AddRevisionAsync(preparation, GuidGenerator.Create(), input, fingerprint);
        return await CompleteAsync(uow, () => MapPreparationAsync(preparation));
    }
    private async Task AddRevisionAsync(LessonPreparation preparation, Guid operationId, LessonPreparationContentDto input, string fingerprint)
    {
        ValidateIds(input.BindingIds); ValidateIds(input.QuestionIds); ValidateIds(input.ExamPaperIds);
        var snapshot = await Curriculum.ResolveAsync(input.Scope);
        await Curriculum.ValidateForUseAsync(snapshot);
        var references = await BuildReferencesAsync(input, snapshot, operationId);
        var revision = new LessonPreparationRevision(operationId, preparation.TenantId, preparation.OwnerUserId, preparation.Id,
            preparation.CurrentRevisionNumber + 1, input.Title, snapshot.SchoolId, snapshot.TextbookVersionId,
            JsonSerializer.Serialize(snapshot, Json), input.Objectives, input.Reflection, fingerprint, DateTime.UtcNow);
        await Repo<LessonPreparationRevision>().InsertAsync(revision, autoSave: true);
        foreach (var mapping in snapshot.Mappings)
            await Repo<LessonPreparationRevisionMapping>().InsertAsync(new LessonPreparationRevisionMapping(GuidGenerator.Create(),
                preparation.TenantId, revision.Id, snapshot.TextbookVersionId, mapping.MappingId, mapping.ChapterId));
        foreach (var reference in references) await Repo<LessonPreparationReference>().InsertAsync(reference);
        preparation.Advance(revision);
        // Actual root CAS remains explicit even though all children are immutable.
        await Repo<LessonPreparation>().UpdateManyAsync([preparation]);
    }
    private static void ValidateIds(List<Guid>? ids)
    {
        if (ids == null || ids.Count > 200 || ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Count)
            throw new BusinessException(EducationPreparationErrorCodes.CapacityExceeded);
    }
    private static string Fingerprint(LessonPreparationContentDto input, string operation, Guid? sourceId = null)
    {
        ValidateIds(input.BindingIds); ValidateIds(input.QuestionIds); ValidateIds(input.ExamPaperIds);
        if (input.Scope == null || input.Scope.TextbookKnowledgePointMappingIds == null) throw new BusinessException(EducationPreparationErrorCodes.CurriculumMismatch);
        return Hash(new { operation, sourceId, input.Title, input.Objectives, input.Reflection, input.Scope.TeacherTextbookSelectionId,
            Mappings = input.Scope.TextbookKnowledgePointMappingIds.Order().ToArray(), Bindings = input.BindingIds.Order().ToArray(),
            Questions = input.QuestionIds.Order().ToArray(), Papers = input.ExamPaperIds.Order().ToArray() });
    }
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));

    public virtual async Task<LessonPreparationDto> GetPreparationAsync(Guid id)
    {
        await CheckAccessAsync();
        return await MapPreparationAsync(await OwnPreparationAsync(id));
    }
    public virtual async Task<LessonPreparationRevisionDto> GetRevisionAsync(Guid id, Guid revisionId)
    {
        await CheckAccessAsync();
        await OwnPreparationAsync(id);
        var revision = await ReferenceAsync<LessonPreparationRevision>(revisionId) ?? throw new AbpAuthorizationException();
        Own(revision.OwnerUserId, revision.TenantId);
        if (revision.PreparationId != id) throw new AbpAuthorizationException();
        return await MapRevisionAsync(revision);
    }
    public virtual async Task<PagedResultDto<LessonPreparationRevisionDto>> GetRevisionsAsync(Guid id, PagedResultRequestDto input)
    {
        await CheckAccessAsync();
        await OwnPreparationAsync(id);
        var take = Page(input);
        using var tracking = Repo<LessonPreparationRevision>().DisableTracking();
        var query = (await Repo<LessonPreparationRevision>().GetQueryableAsync()).Where(x => x.PreparationId == id && x.TenantId == CurrentTenant.Id && x.OwnerUserId == CurrentUser.Id);
        var count = await AsyncExecuter.CountAsync(query);
        var result = new List<LessonPreparationRevisionDto>();
        foreach (var revision in await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.RevisionNumber).Skip(input.SkipCount).Take(take)))
            result.Add(await MapRevisionAsync(revision));
        return new PagedResultDto<LessonPreparationRevisionDto>(count, result);
    }
    public virtual async Task<PagedResultDto<LessonPreparationListDto>> GetMyPreparationsAsync(GetLessonPreparationListInput input)
    {
        await CheckAccessAsync();
        var take = Page(input);
        using var rootTracking = Repo<LessonPreparation>().DisableTracking();
        using var revisionTracking = Repo<LessonPreparationRevision>().DisableTracking();
        using var mappingTracking = Repo<LessonPreparationRevisionMapping>().DisableTracking();
        using var refTracking = Repo<LessonPreparationReference>().DisableTracking();
        var revisions = await Repo<LessonPreparationRevision>().GetQueryableAsync();
        var mappings = await Repo<LessonPreparationRevisionMapping>().GetQueryableAsync();
        var references = await Repo<LessonPreparationReference>().GetQueryableAsync();
        var query = (await Repo<LessonPreparation>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.OwnerUserId == CurrentUser.Id && x.CurrentRevisionId.HasValue);
        if (!string.IsNullOrWhiteSpace(input.Keyword)) query = query.Where(x => x.Title.Contains(input.Keyword) || revisions.Any(r => r.PreparationId == x.Id &&
            (r.Objectives.Contains(input.Keyword) || (r.Reflection != null && r.Reflection.Contains(input.Keyword)))));
        if (input.TextbookVersionId.HasValue) query = query.Where(x => revisions.Any(r => r.PreparationId == x.Id && r.TextbookVersionId == input.TextbookVersionId));
        if (input.ChapterId.HasValue) query = query.Where(x => revisions.Any(r => r.PreparationId == x.Id && mappings.Any(m => m.RevisionId == r.Id && m.ChapterId == input.ChapterId)));
        if (input.KnowledgeVersionId.HasValue) query = query.Where(x => revisions.Any(r => r.PreparationId == x.Id && references.Any(s => s.RevisionId == r.Id && s.KnowledgeVersionId == input.KnowledgeVersionId)));
        var count = await AsyncExecuter.CountAsync(query);
        var result = new List<LessonPreparationListDto>();
        foreach (var row in await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take)))
        {
            var revision = await ReferenceAsync<LessonPreparationRevision>(row.CurrentRevisionId!.Value) ?? throw new AbpAuthorizationException();
            var snapshot = Snapshot(revision.CurriculumSnapshotJson);
            result.Add(new LessonPreparationListDto { Id = row.Id, Title = row.Title, CurrentRevisionId = revision.Id,
                CurrentRevisionNumber = revision.RevisionNumber, TextbookVersionId = snapshot.TextbookVersionId, TextbookTitle = snapshot.TextbookTitle, UpdatedAtUtc = row.UpdatedAtUtc });
        }
        return new PagedResultDto<LessonPreparationListDto>(count, result);
    }
    private async Task<LessonPreparation> OwnPreparationAsync(Guid id)
    {
        var preparation = await ReferenceAsync<LessonPreparation>(id) ?? throw new AbpAuthorizationException();
        Own(preparation.OwnerUserId, preparation.TenantId);
        return preparation;
    }
    private async Task<LessonPreparationDto> MapPreparationAsync(LessonPreparation preparation)
    {
        if (!preparation.CurrentRevisionId.HasValue) throw new BusinessException(EducationPreparationErrorCodes.InvalidSource);
        var revision = await ReferenceAsync<LessonPreparationRevision>(preparation.CurrentRevisionId.Value) ?? throw new AbpAuthorizationException();
        Own(revision.OwnerUserId, revision.TenantId);
        if (revision.PreparationId != preparation.Id) throw new AbpAuthorizationException();
        return new LessonPreparationDto { Id = preparation.Id, ConcurrencyStamp = preparation.ConcurrencyStamp,
            CopiedFromRevisionId = preparation.CopiedFromRevisionId, CurrentRevision = await MapRevisionAsync(revision) };
    }
    private async Task<LessonPreparationRevisionDto> MapRevisionAsync(LessonPreparationRevision revision)
    {
        var snapshot = Snapshot(revision.CurriculumSnapshotJson);
        var available = await CurriculumAvailableAsync(snapshot);
        var dto = new LessonPreparationRevisionDto { Id = revision.Id, PreparationId = revision.PreparationId,
            RevisionNumber = revision.RevisionNumber, Title = revision.Title, Curriculum = snapshot,
            Objectives = revision.Objectives, Reflection = revision.Reflection, RecordedAtUtc = revision.RecordedAtUtc, NeedsCurriculumConfirmation = !available };
        foreach (var reference in await ReferencesAsync<LessonPreparationReference>(x => x.RevisionId == revision.Id && x.TenantId == revision.TenantId))
        {
            var usable = available && await ReferenceUsableAsync(reference, snapshot);
            dto.References.Add(new LessonPreparationReferenceDto { Id = reference.Id, Kind = reference.Kind,
                MaterialBindingId = reference.MaterialBindingId, KnowledgeVersionId = reference.KnowledgeVersionId, KnowledgeChunkId = reference.KnowledgeChunkId,
                QuestionId = reference.QuestionId, ExamPaperId = reference.ExamPaperId, Title = reference.TitleSnapshot,
                SnapshotVersion = reference.SnapshotVersion, SourceConcurrencyStamp = reference.SourceConcurrencyStamp, CanUseNow = usable });
        }
        return dto;
    }
    private static EducationCurriculumSnapshot Snapshot(string value)
    {
        try { return JsonSerializer.Deserialize<EducationCurriculumSnapshot>(value, Json) ?? throw new JsonException(); }
        catch (JsonException) { throw new BusinessException(EducationPreparationErrorCodes.CurriculumMismatch); }
    }
    private async Task<bool> CurriculumAvailableAsync(EducationCurriculumSnapshot snapshot)
    {
        try { await Curriculum.ValidateAsync(snapshot); return true; }
        catch (AbpAuthorizationException) { return false; }
        catch (BusinessException) { return false; }
    }
    private async Task<KnowledgeMaterialCurriculumBindingDto> MapBindingAsync(KnowledgeMaterialCurriculumBinding binding)
    {
        var usable = false;
        if (!binding.EndedAtUtc.HasValue)
        {
            try
            {
                await Curriculum.GetMappingAsync(binding.TextbookKnowledgePointMappingId);
                await AuthorizeMaterialAsync(binding.KnowledgeItemId, binding.KnowledgeVersionId, binding.KnowledgeChunkId, true);
                usable = true;
            }
            catch (AbpAuthorizationException) { }
            catch (BusinessException) { }
        }
        return new KnowledgeMaterialCurriculumBindingDto { Id = binding.Id, KnowledgeItemId = binding.KnowledgeItemId,
            KnowledgeVersionId = binding.KnowledgeVersionId, KnowledgeChunkId = binding.KnowledgeChunkId, TextbookVersionId = binding.TextbookVersionId,
            TextbookKnowledgePointMappingId = binding.TextbookKnowledgePointMappingId, Curriculum = Snapshot(binding.CurriculumSnapshotJson),
            MaterialTitle = binding.MaterialTitle, WholeMaterialAppliesToMapping = binding.WholeMaterialAppliesToMapping,
            ConfirmedAtUtc = binding.ConfirmedAtUtc, EndedAtUtc = binding.EndedAtUtc, ConcurrencyStamp = binding.ConcurrencyStamp, CanUseNow = usable };
    }

    // The guard disables reference tracking for reads and explicitly captures source roots for new uses.
    private async Task<string> AuthorizeMaterialAsync(Guid itemId, Guid versionId, Guid? chunkId, bool requireCurrent,
        bool forUpdate = false, Guid? schoolKnowledgeShareId = null)
    {
        var metadata = await Sources.EnsureSourceMetadataAsync(itemId, versionId, chunkId, requireCurrent, forUpdate, schoolKnowledgeShareId);
        var version = await ReferenceAsync<KnowledgeVersion>(versionId) ?? throw new AbpAuthorizationException();
        var item = metadata.Item ?? throw new AbpAuthorizationException();
        if (version.TenantId != CurrentTenant.Id || version.KnowledgeItemId != itemId || !item.IsActive ||
            (requireCurrent && version.Status != KnowledgeVersionStatus.Published)) throw new AbpAuthorizationException();
        if (chunkId.HasValue)
        {
            var chunk = await ReferenceAsync<KnowledgeChunk>(chunkId.Value) ?? throw new AbpAuthorizationException();
            var job = await ReferenceAsync<KnowledgeIndexJob>(chunk.KnowledgeIndexJobId);
            if (chunk.KnowledgeItemId != itemId || chunk.KnowledgeVersionId != versionId || chunk.TenantId != CurrentTenant.Id ||
                !chunk.IsActive || !chunk.IsPublishedSnapshot || job == null || job.TenantId != CurrentTenant.Id ||
                job.KnowledgeItemId != itemId || job.KnowledgeVersionId != versionId || job.Status != KnowledgeIndexJobStatus.Indexed) throw new AbpAuthorizationException();
        }
        var title = metadata.Title ?? version.Title;
        return title;
    }
    private static void Match(EducationCurriculumSnapshot scope, string? sourceJson)
    {
        if (sourceJson == null) throw new BusinessException(EducationPreparationErrorCodes.CurriculumMismatch);
        var source = Snapshot(sourceJson);
        var ids = scope.Mappings.Select(x => x.MappingId).ToHashSet();
        if (source.TenantId != scope.TenantId || source.SchoolId != scope.SchoolId || source.EducationSubjectId != scope.EducationSubjectId ||
            source.TextbookVersionId != scope.TextbookVersionId || source.Mappings.Count == 0 || source.Mappings.Any(x => !ids.Contains(x.MappingId)))
            throw new BusinessException(EducationPreparationErrorCodes.CurriculumMismatch);
    }
    private async Task<List<SchoolKnowledgeShare>> AvailableSharesAsync(EducationCurriculumSnapshot scope)
    {
        using var tracking = Repo<SchoolKnowledgeShare>().DisableTracking();
        var query = await LazyServiceProvider.LazyGetRequiredService<KnowledgeSchoolSharingAppService>()
            .GetAvailableSharesQueryAsync(SchoolKnowledgeShareUses.Read, scope.SchoolId);
        var shares = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.Id).Take(1001));
        if (shares.Count > 1000) throw new BusinessException(EducationPreparationErrorCodes.CapacityExceeded);
        return shares;
    }
    private async Task<string> AuthorizeBindingAsync(KnowledgeMaterialCurriculumBinding binding, EducationCurriculumSnapshot scope,
        List<SchoolKnowledgeShare>? shares = null, bool forUpdate = false)
    {
        if (binding.TenantId != CurrentTenant.Id || binding.EndedAtUtc.HasValue || binding.TextbookVersionId != scope.TextbookVersionId ||
            (!binding.KnowledgeChunkId.HasValue && !binding.WholeMaterialAppliesToMapping) ||
            !scope.Mappings.Any(x => x.MappingId == binding.TextbookKnowledgePointMappingId))
            throw new BusinessException(EducationPreparationErrorCodes.CurriculumMismatch);
        var item = await ReferenceAsync<KnowledgeItem>(binding.KnowledgeItemId) ?? throw new AbpAuthorizationException();
        var library = await ReferenceAsync<KnowledgeLibrary>(binding.LibraryId);
        if (item.OwnerUserId != binding.OwnerUserId || item.LibraryId != binding.LibraryId || library == null || library.OwnerUserId != binding.OwnerUserId ||
            library.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        var personalCurrent = binding.OwnerUserId == CurrentUser.Id && item.CurrentPublishedVersionId == binding.KnowledgeVersionId;
        SchoolKnowledgeShare? share = null;
        if (!personalCurrent)
        {
            shares ??= await AvailableSharesAsync(scope);
            share = shares.FirstOrDefault(x => x.OwnerUserId == binding.OwnerUserId && x.LibraryId == binding.LibraryId &&
                x.KnowledgeItemId == binding.KnowledgeItemId && x.KnowledgeVersionId == binding.KnowledgeVersionId && x.GetCurriculumBindingIds().Contains(binding.Id));
            if (share == null) throw new AbpAuthorizationException();
        }
        // Reading an explicitly shared lesson source needs Read; generation validates its stronger uses separately.
        var title = await AuthorizeMaterialAsync(binding.KnowledgeItemId, binding.KnowledgeVersionId, binding.KnowledgeChunkId,
            personalCurrent, forUpdate, share?.Id);
        var version = await ReferenceAsync<KnowledgeVersion>(binding.KnowledgeVersionId) ?? throw new AbpAuthorizationException();
        if (version.Status != KnowledgeVersionStatus.Published || (personalCurrent && !version.IsCurrentPublished)) throw new AbpAuthorizationException();
        return share?.Title ?? title;
    }
    private async Task<string> AuthorizeQuestionAsync(Guid id, EducationCurriculumSnapshot scope, bool forUpdate = false)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        var question = await ReferenceAsync<Question>(id) ?? throw new AbpAuthorizationException();
        if (!question.CurriculumConfirmed || question.Status is QuestionStatus.Archived or QuestionStatus.Disabled)
            throw new BusinessException(EducationPreparationErrorCodes.InvalidSource);
        Match(scope, question.CurriculumSnapshotJson);
        using (var read = Uow.Begin(requiresNew: true, isTransactional: false))
        {
            await Sources.EnsureQuestionAsync(question, requireUsableSources: true);
            await read.CompleteAsync();
        }
        await LazyServiceProvider.LazyGetRequiredService<QuestionCurriculumAccessGuard>().EnsureAsync(question, forUpdate: forUpdate);
        var title = question.Title;
        return title;
    }
    private async Task<string> AuthorizePaperAsync(Guid id, EducationCurriculumSnapshot scope, bool forUpdate = false)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);
        using (var read = Uow.Begin(requiresNew: true, isTransactional: false))
        {
            try { await LazyServiceProvider.LazyGetRequiredService<IExamAppService>().GetSavedPaperAsync(id); }
            catch (EntityNotFoundException) { throw new AbpAuthorizationException(); }
            await read.CompleteAsync();
        }
        var paper = await ReferenceAsync<ExamPaper>(id) ?? throw new AbpAuthorizationException();
        Sources.EnsurePersonalOwner(paper.PersonalOwnerUserId, paper.TenantId);
        Match(scope, paper.CurriculumSnapshotJson);
        var rows = await ReferencesAsync<ExamPaperQuestion>(x => x.PaperId == id && x.TenantId == CurrentTenant.Id);
        if (rows.Count == 0 || rows.Count != paper.QuestionCount) throw new AbpAuthorizationException();
        foreach (var row in rows)
        {
            Match(scope, row.CurriculumSnapshotJson);
            if (!row.CurriculumConfirmedAtUtc.HasValue) throw new BusinessException(EducationPreparationErrorCodes.InvalidSource);
            await AuthorizeQuestionAsync(row.OriginalQuestionId, scope, forUpdate);
        }
        var title = paper.Title;
        return title;
    }
    private async Task<List<LessonPreparationReference>> BuildReferencesAsync(LessonPreparationContentDto input, EducationCurriculumSnapshot scope, Guid revisionId)
    {
        var result = new List<LessonPreparationReference>();
        List<SchoolKnowledgeShare>? shares = input.BindingIds.Count > 0 ? await AvailableSharesAsync(scope) : null;
        foreach (var id in input.BindingIds)
        {
            var binding = await ReferenceAsync<KnowledgeMaterialCurriculumBinding>(id) ?? throw new AbpAuthorizationException();
            var title = await AuthorizeBindingAsync(binding, scope, shares, forUpdate: true);
            var currentBinding = await ReferenceAsync<KnowledgeMaterialCurriculumBinding>(binding.Id) ?? throw new AbpAuthorizationException();
            if (currentBinding.EndedAtUtc.HasValue || currentBinding.ConcurrencyStamp != binding.ConcurrencyStamp) throw Conflict();
            result.Add(new LessonPreparationReference(GuidGenerator.Create(), CurrentTenant.Id, revisionId, LessonPreparationReferenceKind.Material,
                title, binding.Id, binding.KnowledgeVersionId, binding.KnowledgeChunkId));
        }
        foreach (var id in input.QuestionIds)
        {
            var question = await ReferenceAsync<Question>(id) ?? throw new AbpAuthorizationException();
            var title = await AuthorizeQuestionAsync(id, scope, forUpdate: true);
            result.Add(new LessonPreparationReference(GuidGenerator.Create(), CurrentTenant.Id, revisionId, LessonPreparationReferenceKind.Question,
                title, questionId: id, sourceStamp: question.ConcurrencyStamp));
        }
        foreach (var id in input.ExamPaperIds)
        {
            var title = await AuthorizePaperAsync(id, scope, forUpdate: true);
            var paper = await ReferenceAsync<ExamPaper>(id) ?? throw new AbpAuthorizationException();
            result.Add(new LessonPreparationReference(GuidGenerator.Create(), CurrentTenant.Id, revisionId, LessonPreparationReferenceKind.ExamPaper,
                title, paperId: id, snapshotVersion: paper.SnapshotVersion, sourceStamp: paper.ConcurrencyStamp));
        }
        return result;
    }
    private async Task<bool> ReferenceUsableAsync(LessonPreparationReference reference, EducationCurriculumSnapshot scope)
    {
        try
        {
            if (reference.Kind == LessonPreparationReferenceKind.Material)
            {
                var binding = await ReferenceAsync<KnowledgeMaterialCurriculumBinding>(reference.MaterialBindingId!.Value) ?? throw new AbpAuthorizationException();
                if (binding.KnowledgeVersionId != reference.KnowledgeVersionId || binding.KnowledgeChunkId != reference.KnowledgeChunkId) return false;
                await AuthorizeBindingAsync(binding, scope);
            }
            else if (reference.Kind == LessonPreparationReferenceKind.Question)
            {
                await AuthorizeQuestionAsync(reference.QuestionId!.Value, scope);
            }
            else await AuthorizePaperAsync(reference.ExamPaperId!.Value, scope);
            return true;
        }
        catch (AbpAuthorizationException) { return false; }
        catch (BusinessException) { return false; }
    }

    public virtual async Task<PagedResultDto<EducationPreparationReferenceOptionDto>> GetReferenceOptionsAsync(GetPreparationReferenceOptionsInput input)
    {
        await CheckAccessAsync();
        var take = Page(input);
        var scope = await Curriculum.ResolveAsync(input.Scope);
        var result = new List<EducationPreparationReferenceOptionDto>();
        if (input.Kind == LessonPreparationReferenceKind.Material)
        {
            var shares = input.IncludeSchoolShared ? await AvailableSharesAsync(scope) : [];
            var frozenIds = shares.SelectMany(x => x.GetCurriculumBindingIds()).Distinct().ToArray();
            var mappingIds = scope.Mappings.Select(x => x.MappingId).ToArray();
            using var tracking = Repo<KnowledgeMaterialCurriculumBinding>().DisableTracking();
            var query = (await Repo<KnowledgeMaterialCurriculumBinding>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && !x.EndedAtUtc.HasValue &&
                x.TextbookVersionId == scope.TextbookVersionId && mappingIds.Contains(x.TextbookKnowledgePointMappingId) &&
                (x.OwnerUserId == CurrentUser.Id || frozenIds.Contains(x.Id)));
            var rows = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.Id).Take(1001));
            if (rows.Count > 1000) throw new BusinessException(EducationPreparationErrorCodes.CapacityExceeded);
            foreach (var binding in rows)
            {
                try
                {
                    var title = await AuthorizeBindingAsync(binding, scope, shares);
                    if (!string.IsNullOrWhiteSpace(input.Keyword) && !title.Contains(input.Keyword, StringComparison.OrdinalIgnoreCase)) continue;
                    var mapping = scope.Mappings.Single(x => x.MappingId == binding.TextbookKnowledgePointMappingId);
                    result.Add(new EducationPreparationReferenceOptionDto { Id = binding.Id, Title = title, Kind = input.Kind,
                        Detail = $"{mapping.ChapterTitle} · {mapping.KnowledgePointName} · {L[binding.KnowledgeChunkId.HasValue ? "EducationPreparation:Chunk" : "EducationPreparation:WholeMaterial"]}" });
                }
                catch (AbpAuthorizationException) { }
                catch (BusinessException) { }
            }
        }
        else if (input.Kind == LessonPreparationReferenceKind.Question)
        {
            await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
            using var tracking = Repo<Question>().DisableTracking();
            var query = (await Repo<Question>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.PersonalOwnerUserId == CurrentUser.Id &&
                x.CurriculumSnapshotJson != null && x.CurriculumConfirmed && x.Status != QuestionStatus.Archived && x.Status != QuestionStatus.Disabled);
            if (!string.IsNullOrWhiteSpace(input.Keyword)) query = query.Where(x => x.Title.Contains(input.Keyword));
            var rows = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.Id).Take(1001));
            if (rows.Count > 1000) throw new BusinessException(EducationPreparationErrorCodes.CapacityExceeded);
            foreach (var row in rows)
            {
                try { var title = await AuthorizeQuestionAsync(row.Id, scope); result.Add(new EducationPreparationReferenceOptionDto { Id = row.Id, Title = title, Kind = input.Kind, Detail = L["EducationPreparation:ConfirmedQuestion"].Value }); }
                catch (AbpAuthorizationException) { }
                catch (BusinessException) { }
            }
        }
        else if (input.Kind == LessonPreparationReferenceKind.ExamPaper)
        {
            await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);
            using var tracking = Repo<ExamPaper>().DisableTracking();
            var query = (await Repo<ExamPaper>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.PersonalOwnerUserId == CurrentUser.Id && x.CurriculumSnapshotJson != null);
            if (!string.IsNullOrWhiteSpace(input.Keyword)) query = query.Where(x => x.Title.Contains(input.Keyword));
            var rows = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.Id).Take(1001));
            if (rows.Count > 1000) throw new BusinessException(EducationPreparationErrorCodes.CapacityExceeded);
            foreach (var row in rows)
            {
                try { var title = await AuthorizePaperAsync(row.Id, scope); result.Add(new EducationPreparationReferenceOptionDto { Id = row.Id, Title = title, Kind = input.Kind, Detail = L["EducationPreparation:ReferencePaperDetail", row.QuestionCount, row.SnapshotVersion].Value }); }
                catch (AbpAuthorizationException) { }
                catch (BusinessException) { }
            }
        }
        else throw new BusinessException(EducationPreparationErrorCodes.InvalidSource);
        var ordered = result.OrderBy(x => x.Title).ThenBy(x => x.Id).ToList();
        return new PagedResultDto<EducationPreparationReferenceOptionDto>(ordered.Count, ordered.Skip(input.SkipCount).Take(take).ToList());
    }

    public virtual async Task<Guid> CopyReflectionToMyLibraryAsync(Guid preparationId, Guid revisionId, CopyReflectionToKnowledgeDto input)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        await CheckAccessAsync();
        await OwnPreparationAsync(preparationId);
        EducationRules.RequireId(input.IdempotencyKey);
        var revision = await ReferenceAsync<LessonPreparationRevision>(revisionId) ?? throw new AbpAuthorizationException();
        Own(revision.OwnerUserId, revision.TenantId);
        if (revision.PreparationId != preparationId) throw new AbpAuthorizationException();
        if (string.IsNullOrWhiteSpace(revision.Reflection)) throw new BusinessException(EducationPreparationErrorCodes.ReflectionRequired);
        var fingerprint = Hash(new { preparationId, revisionId, input.Title });
        var existing = await ReferenceAsync<KnowledgeItem>(input.IdempotencyKey);
        if (existing != null)
        {
            if (!existing.OwnerUserId.HasValue) throw new AbpAuthorizationException();
            Own(existing.OwnerUserId.Value, existing.TenantId);
            if (existing.GetProperty<string>("PreparationReflectionFingerprint") != fingerprint) throw Conflict();
            return await CompleteAsync(uow, () => Task.FromResult(existing.Id));
        }
        var library = await LazyServiceProvider.LazyGetRequiredService<IKnowledgeLibraryAppService>().GetMyLibraryAsync();
        var item = new KnowledgeItem(input.IdempotencyKey, CurrentTenant.Id, input.Title, libraryId: library.Id, ownerUserId: CurrentUser.Id);
        item.OrganizePersonalMaterial(PersonalMaterialType.Reflection, revision.RecordedAtUtc.Year, Snapshot(revision.CurriculumSnapshotJson).SubjectName, []);
        item.SetProperty("PreparationReflectionFingerprint", fingerprint);
        item.SetProperty("PreparationRevisionId", revision.Id);
        await Repo<KnowledgeItem>().InsertAsync(item, autoSave: true);
        await Repo<KnowledgeVersion>().InsertAsync(new KnowledgeVersion(GuidGenerator.Create(), CurrentTenant.Id, item.Id, 1, input.Title, revision.Reflection));
        return await CompleteAsync(uow, () => Task.FromResult(item.Id));
    }
}
