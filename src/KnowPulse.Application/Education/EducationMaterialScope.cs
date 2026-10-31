using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.RagAI;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.Education;

/// <summary>课程条件仅收窄资料授权；所有候选及引用都以当前关系型事实重新计算。</summary>
[RemoteService(false)]
public class EducationMaterialScope : KnowPulseAppService
{
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();

    public virtual async Task ValidateSourceAsync(EducationCurriculumSnapshot snapshot, Guid knowledgeItemId, Guid knowledgeVersionId,
        Guid chunkId, IReadOnlyCollection<Guid> curriculumBindingIds, IReadOnlyCollection<Guid> textbookKnowledgePointMappingIds,
        Guid? schoolKnowledgeShareId = null, CancellationToken cancellationToken = default, bool forUpdate = false)
    {
        if (curriculumBindingIds == null || textbookKnowledgePointMappingIds == null || curriculumBindingIds.Count == 0 ||
            textbookKnowledgePointMappingIds.Count == 0 || curriculumBindingIds.Count > SchoolKnowledgeShare.MaxCurriculumBindings ||
            textbookKnowledgePointMappingIds.Count > SchoolKnowledgeShare.MaxCurriculumBindings ||
            curriculumBindingIds.Contains(Guid.Empty) || textbookKnowledgePointMappingIds.Contains(Guid.Empty) ||
            curriculumBindingIds.Distinct().Count() != curriculumBindingIds.Count ||
            textbookKnowledgePointMappingIds.Distinct().Count() != textbookKnowledgePointMappingIds.Count)
            throw new AbpAuthorizationException("The original curriculum source confirmation is required.");
        var curriculumAccess = LazyServiceProvider.LazyGetRequiredService<EducationCurriculumAccessGuard>();
        using var tracking = forUpdate ? Repo<KnowledgeMaterialCurriculumBinding>().EnableTracking() : null;
        if (forUpdate)
        {
            var bindings = await AsyncExecuter.ToListAsync((await Repo<KnowledgeMaterialCurriculumBinding>().GetQueryableAsync())
                .Where(x => x.TenantId == snapshot.TenantId && curriculumBindingIds.Contains(x.Id)).OrderBy(x => x.Id), cancellationToken);
            if (bindings.Count != curriculumBindingIds.Count) throw new AbpAuthorizationException("The original curriculum bindings are required.");
            await curriculumAccess.ValidateForUseAsync(snapshot, cancellationToken);
            await LazyServiceProvider.LazyGetRequiredService<KnowledgeSourceAccessGuard>().EnsureSourceAsync(knowledgeItemId,
                knowledgeVersionId, chunkId, requireUsable: true, forUpdate: true, schoolKnowledgeShareId: schoolKnowledgeShareId);
            // The source-version barrier also contends with adding/ending a precise binding; the
            // original binding tokens prevent a direct mutation from replacing the evidence.
            await Repo<KnowledgeMaterialCurriculumBinding>().UpdateManyAsync(bindings, autoSave: true, cancellationToken: cancellationToken);
        }
        else await curriculumAccess.ValidateAsync(snapshot, cancellationToken);
        var shares = new Dictionary<Guid, SchoolKnowledgeShare>();
        if (schoolKnowledgeShareId.HasValue)
        {
            var query = await LazyServiceProvider.LazyGetRequiredService<KnowledgeSchoolSharingAppService>()
                .GetAvailableSharesQueryAsync(SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve | SchoolKnowledgeShareUses.GenerateQuestions, snapshot.SchoolId);
            var share = await AsyncExecuter.FirstOrDefaultAsync(query.Where(x => x.Id == schoolKnowledgeShareId &&
                x.KnowledgeItemId == knowledgeItemId && x.KnowledgeVersionId == knowledgeVersionId), cancellationToken);
            if (share == null) throw new AbpAuthorizationException("The original school source is no longer available for generation.");
            shares.Add(share.KnowledgeVersionId, share);
        }
        var matches = await GetMatchesAsync(snapshot, schoolKnowledgeShareId.HasValue ? KnowledgeSearchScope.SchoolShared : KnowledgeSearchScope.PersonalLibrary,
            shares, cancellationToken);
        if (!matches.TryGetValue(chunkId, out var match) || match.KnowledgeItemId != knowledgeItemId || match.KnowledgeVersionId != knowledgeVersionId)
            throw new AbpAuthorizationException("The original curriculum source is no longer available.");
        var original = schoolKnowledgeShareId.HasValue ? match.SharedBindings : match.PersonalBindings;
        if (curriculumBindingIds.Any(id => !original.ContainsKey(id)) ||
            !textbookKnowledgePointMappingIds.ToHashSet().SetEquals(curriculumBindingIds.Select(id => original[id])))
            throw new AbpAuthorizationException("The original curriculum binding and mapping pair is no longer available.");
    }

    internal async Task<Guid[]> GetShareBindingIdsAsync(KnowledgeItem item, KnowledgeVersion version, Guid schoolId)
    {
        var bindings = await Repo<KnowledgeMaterialCurriculumBinding>().GetQueryableAsync();
        var mappings = await Repo<TextbookKnowledgePointMapping>().GetQueryableAsync();
        var books = await Repo<TextbookVersion>().GetQueryableAsync();
        var ids = await AsyncExecuter.ToListAsync((
            from binding in bindings
            join mapping in mappings on binding.TextbookKnowledgePointMappingId equals mapping.Id
            join book in books on mapping.TextbookVersionId equals book.Id
            // Ended passage confirmations remain negative evidence: a new share must not reopen
            // that passage through an older whole-material confirmation. They do not authorize hits.
            where !binding.IsDeleted && (!binding.EndedAtUtc.HasValue || binding.KnowledgeChunkId.HasValue) && binding.TenantId == item.TenantId &&
                binding.OwnerUserId == item.OwnerUserId && binding.LibraryId == item.LibraryId && binding.KnowledgeItemId == item.Id && binding.KnowledgeVersionId == version.Id &&
                binding.TextbookVersionId == book.Id &&
                !mapping.IsDeleted && mapping.TenantId == item.TenantId && !book.IsDeleted && book.TenantId == item.TenantId &&
                book.SchoolId == schoolId && book.Status == TextbookVersionStatus.Published
            orderby binding.Id
            select binding.Id).Take(SchoolKnowledgeShare.MaxCurriculumBindings + 1));
        if (ids.Count > SchoolKnowledgeShare.MaxCurriculumBindings) throw new BusinessException(EducationCurriculumErrorCodes.CapacityExceeded);
        return ids.ToArray();
    }

    internal async Task<Dictionary<Guid, CurriculumChunkMatch>> GetMatchesAsync(EducationCurriculumSnapshot curriculum,
        KnowledgeSearchScope scope, IReadOnlyDictionary<Guid, SchoolKnowledgeShare> shares, CancellationToken ct)
    {
        var mappingIds = curriculum.Mappings.Select(x => x.MappingId).Distinct().ToArray();
        var schoolShares = shares.Values.Where(x => x.SchoolId == curriculum.SchoolId).ToList();
        var sharesByBindingId = schoolShares.SelectMany(share => share.GetCurriculumBindingIds().Select(id => new { Id = id, Share = share }))
            .ToDictionary(x => x.Id, x => x.Share);
        var sharedBindingIds = sharesByBindingId.Keys.ToArray();
        var sharedVersionIds = schoolShares.Select(x => x.KnowledgeVersionId).Distinct().ToArray();
        var allowPersonal = scope != KnowledgeSearchScope.SchoolShared;
        var bindings = await Repo<KnowledgeMaterialCurriculumBinding>().GetQueryableAsync();
        var versions = await Repo<KnowledgeVersion>().GetQueryableAsync();
        var items = await Repo<KnowledgeItem>().GetQueryableAsync();
        var libraries = await Repo<KnowledgeLibrary>().GetQueryableAsync();
        var chunks = await Repo<KnowledgeChunk>().GetQueryableAsync();
        var jobs = await Repo<KnowledgeIndexJob>().GetQueryableAsync();
        // Ending a precise confirmation must not reactivate a whole-material fallback for that chunk.
        var rows = await AsyncExecuter.ToListAsync((
            from binding in bindings
            join version in versions on binding.KnowledgeVersionId equals version.Id
            join item in items on version.KnowledgeItemId equals item.Id
            join library in libraries on binding.LibraryId equals library.Id
            join chunk in chunks on version.Id equals chunk.KnowledgeVersionId
            join job in jobs on chunk.KnowledgeIndexJobId equals job.Id
            let personal = allowPersonal && binding.OwnerUserId == CurrentUser.Id && item.CurrentPublishedVersionId == version.Id && version.IsCurrentPublished &&
                (binding.KnowledgeChunkId == chunk.Id || (!binding.KnowledgeChunkId.HasValue && binding.WholeMaterialAppliesToMapping && !bindings.Any(precise =>
                    precise.TenantId == curriculum.TenantId && precise.TextbookVersionId == curriculum.TextbookVersionId &&
                    precise.KnowledgeVersionId == version.Id && precise.KnowledgeChunkId == chunk.Id)))
            let shared = sharedBindingIds.Contains(binding.Id) && sharedVersionIds.Contains(version.Id) &&
                (binding.KnowledgeChunkId == chunk.Id || (!binding.KnowledgeChunkId.HasValue && binding.WholeMaterialAppliesToMapping && !bindings.Any(precise =>
                    precise.TenantId == curriculum.TenantId && precise.TextbookVersionId == curriculum.TextbookVersionId &&
                    precise.KnowledgeVersionId == version.Id && precise.KnowledgeChunkId == chunk.Id && sharedBindingIds.Contains(precise.Id))))
            where !binding.IsDeleted && !binding.EndedAtUtc.HasValue && binding.TenantId == curriculum.TenantId &&
                binding.TextbookVersionId == curriculum.TextbookVersionId && binding.KnowledgeItemId == item.Id &&
                mappingIds.Contains(binding.TextbookKnowledgePointMappingId) &&
                !version.IsDeleted && version.TenantId == curriculum.TenantId && version.Status == KnowledgeVersionStatus.Published &&
                !item.IsDeleted && item.IsActive && item.TenantId == curriculum.TenantId && item.OwnerUserId == binding.OwnerUserId && item.LibraryId == binding.LibraryId &&
                !library.IsDeleted && library.TenantId == curriculum.TenantId && library.OwnerUserId == binding.OwnerUserId &&
                !chunk.IsDeleted && chunk.IsActive && chunk.IsPublishedSnapshot && chunk.TenantId == curriculum.TenantId && chunk.KnowledgeItemId == item.Id &&
                !job.IsDeleted && job.TenantId == curriculum.TenantId && job.Status == KnowledgeIndexJobStatus.Indexed &&
                job.KnowledgeVersionId == version.Id && job.KnowledgeItemId == item.Id && (personal || shared)
            orderby chunk.Id, binding.Id
            select new { ChunkId = chunk.Id, ItemId = item.Id, VersionId = version.Id, BindingId = binding.Id, binding.OwnerUserId, binding.LibraryId,
                binding.TextbookKnowledgePointMappingId, Personal = personal, Shared = shared }).Take(SchoolKnowledgeShare.MaxCurriculumBindings + 1), ct);
        if (rows.Count > SchoolKnowledgeShare.MaxCurriculumBindings) throw new BusinessException(EducationCurriculumErrorCodes.CapacityExceeded);
        var result = new Dictionary<Guid, CurriculumChunkMatch>();
        foreach (var row in rows)
        {
            var shared = row.Shared && sharesByBindingId.TryGetValue(row.BindingId, out var share) && share.KnowledgeVersionId == row.VersionId &&
                share.OwnerUserId == row.OwnerUserId && share.LibraryId == row.LibraryId;
            if (!row.Personal && !shared) continue;
            if (!result.TryGetValue(row.ChunkId, out var match)) result[row.ChunkId] = match = new CurriculumChunkMatch(row.ItemId, row.VersionId);
            if (row.Personal) match.PersonalBindings.Add(row.BindingId, row.TextbookKnowledgePointMappingId);
            if (shared) match.SharedBindings.Add(row.BindingId, row.TextbookKnowledgePointMappingId);
        }
        return result;
    }
}

internal sealed class CurriculumChunkMatch
{
    internal Guid KnowledgeItemId { get; }
    internal Guid KnowledgeVersionId { get; }
    internal Dictionary<Guid, Guid> PersonalBindings { get; } = [];
    internal Dictionary<Guid, Guid> SharedBindings { get; } = [];

    internal CurriculumChunkMatch(Guid itemId, Guid versionId) { KnowledgeItemId = itemId; KnowledgeVersionId = versionId; }
}
