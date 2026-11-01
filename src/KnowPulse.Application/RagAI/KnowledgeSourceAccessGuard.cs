using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.AiQuestioning;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.QuestionBank;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.RagAI;

/// <summary>
/// 衍生内容使用来源前统一回到主库检查归属、引用关系和当前访问策略。
/// 历史查看可引用旧版本；新使用必须使用当前来源或明确授权且已索引的学校共享版本。
/// </summary>
[RemoteService(false)]
public class KnowledgeSourceAccessGuard : KnowPulseAppService
{
    private IRepository<T, Guid> Repository<T>() where T : class, Volo.Abp.Domain.Entities.IEntity<Guid>
        => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();

    public void EnsurePersonalOwner(Guid? personalOwnerUserId, Guid? tenantId)
    {
        if (!CurrentUser.IsAuthenticated || tenantId != CurrentTenant.Id ||
            (personalOwnerUserId.HasValue && (personalOwnerUserId == Guid.Empty || personalOwnerUserId != CurrentUser.Id)))
        {
            throw Denied();
        }
    }

    public async Task<KnowledgeItem?> EnsureSourceAsync(
        Guid? itemId,
        Guid? versionId,
        Guid? chunkId,
        bool requireUsable = false, bool forUpdate = false, Guid? schoolKnowledgeShareId = null)
        => (await EnsureSourceMetadataAsync(itemId, versionId, chunkId, requireUsable, forUpdate, schoolKnowledgeShareId)).Item;

    // 内部元数据出口：共享来源不读取条目可能已经更新为私有修订的标题、摘要。
    internal async Task<(KnowledgeItem? Item, string? Title, string? Summary, bool IsShared, Guid? SchoolKnowledgeShareId)> EnsureSourceMetadataAsync(
        Guid? itemId,
        Guid? versionId,
        Guid? chunkId,
        bool requireUsable = false, bool forUpdate = false, Guid? schoolKnowledgeShareId = null)
    {
        if (forUpdate)
            return await EnsureProtectedSourceAsync(itemId, versionId, chunkId, requireUsable, schoolKnowledgeShareId);
        // Reference reads must not attach another teacher's aggregates through a later source FK.
        using var chunkTracking = Repository<KnowledgeChunk>().DisableTracking();
        using var versionTracking = Repository<KnowledgeVersion>().DisableTracking();
        using var itemTracking = Repository<KnowledgeItem>().DisableTracking();
        using var libraryTracking = Repository<KnowledgeLibrary>().DisableTracking();
        using var shareTracking = Repository<SchoolKnowledgeShare>().DisableTracking();
        if (!CurrentUser.IsAuthenticated)
        {
            throw Denied();
        }

        if (!itemId.HasValue && !versionId.HasValue && !chunkId.HasValue)
        {
            if (schoolKnowledgeShareId.HasValue) throw Denied();
            return (null, null, null, false, null);
        }

        KnowledgeChunk? chunk = null;
        if (chunkId.HasValue)
        {
            chunk = await Repository<KnowledgeChunk>().FindAsync(chunkId.Value);
            if (chunk == null || chunk.TenantId != CurrentTenant.Id ||
                (itemId.HasValue && itemId != chunk.KnowledgeItemId) ||
                (versionId.HasValue && versionId != chunk.KnowledgeVersionId))
            {
                throw Denied();
            }

            itemId = chunk.KnowledgeItemId;
            versionId = chunk.KnowledgeVersionId;
        }

        KnowledgeVersion? version = null;
        if (versionId.HasValue)
        {
            version = await Repository<KnowledgeVersion>().FindAsync(versionId.Value);
            if (version == null || version.TenantId != CurrentTenant.Id ||
                (itemId.HasValue && itemId != version.KnowledgeItemId))
            {
                throw Denied();
            }

            itemId = version.KnowledgeItemId;
        }

        if (!itemId.HasValue)
        {
            throw Denied();
        }

        var item = await Repository<KnowledgeItem>().FindAsync(itemId.Value);
        if (item == null || item.TenantId != CurrentTenant.Id)
        {
            throw Denied();
        }

        var ownOldVersionForNewUse = requireUsable && version != null &&
            (!version.IsCurrentPublished || item.CurrentPublishedVersionId != version.Id);
        if (schoolKnowledgeShareId.HasValue || item.OwnerUserId.HasValue && (item.OwnerUserId != CurrentUser.Id || ownOldVersionForNewUse))
        {
            // 仅具体版本或分块能使用共享授权；条目引用不能隐式访问作者当前私有版本。
            if (version == null)
            {
                throw Denied();
            }

            var uses = requireUsable
                ? SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve | SchoolKnowledgeShareUses.GenerateQuestions
                : SchoolKnowledgeShareUses.Read;
            var shares = await LazyServiceProvider.LazyGetRequiredService<SchoolKnowledgeSourceAccessGuard>()
                .GetAvailableSharesQueryAsync(uses);
            var share = await AsyncExecuter.FirstOrDefaultAsync(shares.Where(x =>
                x.KnowledgeItemId == item.Id && x.KnowledgeVersionId == version.Id &&
                (!schoolKnowledgeShareId.HasValue || x.Id == schoolKnowledgeShareId.Value))
                .OrderBy(x => x.SharedAtUtc).ThenBy(x => x.Id));
            if (share == null)
            {
                throw Denied();
            }

            if (requireUsable)
            {
                var jobs = await Repository<KnowledgeIndexJob>().GetQueryableAsync();
                var chunks = await Repository<KnowledgeChunk>().GetQueryableAsync();
                var requiredChunkId = chunk?.Id;
                if (!await AsyncExecuter.AnyAsync(chunks.Where(candidate => !candidate.IsDeleted && candidate.IsActive &&
                    candidate.IsPublishedSnapshot && candidate.TenantId == item.TenantId &&
                    candidate.KnowledgeItemId == item.Id && candidate.KnowledgeVersionId == version.Id &&
                    (!requiredChunkId.HasValue || candidate.Id == requiredChunkId.Value) &&
                    jobs.Any(job => !job.IsDeleted && job.TenantId == item.TenantId &&
                        job.Id == candidate.KnowledgeIndexJobId && job.KnowledgeItemId == item.Id &&
                        job.KnowledgeVersionId == version.Id && job.Status == KnowledgeIndexJobStatus.Indexed))))
                {
                    throw Denied();
                }
            }

            return (item, share.Title, share.Summary, true, share.Id);
        }

        await EnsurePersonalKnowledgeOwnerAsync(item);
        var scopeQuery = await Repository<KnowledgePermissionScope>().GetQueryableAsync();
        var scopes = await AsyncExecuter.ToListAsync(scopeQuery.Where(x => x.KnowledgeItemId == item.Id && x.IsEnabled));
        var context = await LazyServiceProvider.LazyGetRequiredService<IKnowledgeAccessContextProvider>().CreateAsync();
        if (!KnowledgeAccessEvaluator.CanAccess(item, scopes, context, allowLegacy: IsLegacyAdministrator))
        {
            throw Denied();
        }

        if (requireUsable)
        {
            if (version == null && item.CurrentPublishedVersionId.HasValue)
            {
                version = await Repository<KnowledgeVersion>().FindAsync(item.CurrentPublishedVersionId.Value);
            }

            if (!item.IsActive || version == null || version.TenantId != item.TenantId ||
                version.KnowledgeItemId != item.Id || version.Status != KnowledgeVersionStatus.Published ||
                !version.IsCurrentPublished || item.CurrentPublishedVersionId != version.Id ||
                (chunk != null && (!chunk.IsActive || !chunk.IsPublishedSnapshot)))
            {
                throw Denied();
            }
        }

        return (item, version?.Title ?? item.Title, item.Summary, false, null);
    }

    private async Task<(KnowledgeItem? Item, string? Title, string? Summary, bool IsShared, Guid? SchoolKnowledgeShareId)> EnsureProtectedSourceAsync(
        Guid? itemId, Guid? versionId, Guid? chunkId, bool requireUsable, Guid? schoolKnowledgeShareId)
    {
        if (LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>().Current == null)
            throw new InvalidOperationException("A new source use must be committed in its caller's transaction.");
        if (!itemId.HasValue && !versionId.HasValue && !chunkId.HasValue)
            return await EnsureSourceMetadataAsync(itemId, versionId, chunkId, requireUsable, schoolKnowledgeShareId: schoolKnowledgeShareId);
        using var itemTracking = Repository<KnowledgeItem>().EnableTracking();
        using var versionTracking = Repository<KnowledgeVersion>().EnableTracking();
        using var chunkTracking = Repository<KnowledgeChunk>().EnableTracking();
        using var libraryTracking = Repository<KnowledgeLibrary>().EnableTracking();
        using var jobTracking = Repository<KnowledgeIndexJob>().EnableTracking();
        using var shareTracking = Repository<SchoolKnowledgeShare>().EnableTracking();
        using var schoolTracking = Repository<School>().EnableTracking();
        using var userTracking = Repository<IdentityUser>().EnableTracking();

        // Capture original concurrency tokens before authorization. Ending a binding or revoking a
        // share writes this same version; source lifecycle and school changes also contend below.
        var chunk = chunkId.HasValue ? await Repository<KnowledgeChunk>().FindAsync(chunkId.Value) : null;
        versionId ??= chunk?.KnowledgeVersionId;
        var version = versionId.HasValue ? await Repository<KnowledgeVersion>().FindAsync(versionId.Value) : null;
        itemId ??= version?.KnowledgeItemId ?? chunk?.KnowledgeItemId;
        var item = itemId.HasValue ? await Repository<KnowledgeItem>().FindAsync(itemId.Value) : null;
        if (item == null || item.TenantId != CurrentTenant.Id ||
            chunkId.HasValue && (chunk == null || chunk.TenantId != CurrentTenant.Id || chunk.KnowledgeItemId != item.Id) ||
            versionId.HasValue && (version == null || version.TenantId != CurrentTenant.Id || version.KnowledgeItemId != item.Id))
            throw Denied();
        version ??= item.CurrentPublishedVersionId.HasValue ? await Repository<KnowledgeVersion>().FindAsync(item.CurrentPublishedVersionId.Value) : null;
        var library = item.LibraryId.HasValue ? await Repository<KnowledgeLibrary>().FindAsync(item.LibraryId.Value) : null;
        var job = chunk == null ? null : await Repository<KnowledgeIndexJob>().FindAsync(chunk.KnowledgeIndexJobId);
        SchoolKnowledgeShare? share = null;
        School? school = null;
        if (schoolKnowledgeShareId.HasValue || item.OwnerUserId.HasValue &&
            (item.OwnerUserId != CurrentUser.Id || requireUsable && (version == null || !version.IsCurrentPublished || item.CurrentPublishedVersionId != version.Id)))
        {
            var uses = requireUsable ? SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve | SchoolKnowledgeShareUses.GenerateQuestions : SchoolKnowledgeShareUses.Read;
            var shares = await LazyServiceProvider.LazyGetRequiredService<SchoolKnowledgeSourceAccessGuard>()
                .GetAvailableSharesQueryAsync(uses);
            var requiredVersionId = version?.Id;
            var allowed = await AsyncExecuter.FirstOrDefaultAsync(shares.Where(x => x.KnowledgeItemId == item.Id &&
                requiredVersionId.HasValue && x.KnowledgeVersionId == requiredVersionId.Value && (!schoolKnowledgeShareId.HasValue || x.Id == schoolKnowledgeShareId.Value))
                .OrderBy(x => x.SharedAtUtc).ThenBy(x => x.Id));
            if (allowed == null) throw Denied();
            share = await Repository<SchoolKnowledgeShare>().GetAsync(allowed.Id);
            school = await Repository<School>().GetAsync(share.SchoolId);
            schoolKnowledgeShareId = share.Id;
        }
        var userIds = new[] { CurrentUser.Id, item.OwnerUserId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().OrderBy(x => x).ToArray();
        var users = await AsyncExecuter.ToListAsync((await Repository<IdentityUser>().GetQueryableAsync()).Where(x => userIds.Contains(x.Id)));
        if (users.Any(x => !x.IsActive || x.IsDeleted || x.TenantId != CurrentTenant.Id) || item.OwnerUserId.HasValue && users.Count != userIds.Length)
            throw Denied();
        await EnsureSourceMetadataAsync(itemId, versionId, chunkId, requireUsable, schoolKnowledgeShareId: schoolKnowledgeShareId);
        if (school != null) await Repository<School>().UpdateManyAsync([school], autoSave: true);
        if (users.Count > 0) await Repository<IdentityUser>().UpdateManyAsync(users.OrderBy(x => x.Id), autoSave: true);
        await Repository<KnowledgeItem>().UpdateManyAsync([item], autoSave: true);
        if (version != null) await Repository<KnowledgeVersion>().UpdateManyAsync([version], autoSave: true);
        if (library != null) await Repository<KnowledgeLibrary>().UpdateManyAsync([library], autoSave: true);
        if (chunk != null) await Repository<KnowledgeChunk>().UpdateManyAsync([chunk], autoSave: true);
        if (job != null) await Repository<KnowledgeIndexJob>().UpdateManyAsync([job], autoSave: true);
        if (share != null) await Repository<SchoolKnowledgeShare>().UpdateManyAsync([share], autoSave: true);
        return await EnsureSourceMetadataAsync(itemId, versionId, chunkId, requireUsable, schoolKnowledgeShareId: schoolKnowledgeShareId);
    }

    public async Task<bool> CanReadSourceAsync(Guid? itemId, Guid? versionId, Guid? chunkId)
    {
        try
        {
            await EnsureSourceAsync(itemId, versionId, chunkId);
            return true;
        }
        catch (AbpAuthorizationException)
        {
            return false;
        }
    }

    public async Task EnsureQuestionAsync(Question question, bool requireUsableSources = false, bool forUpdate = false)
    {
        EnsurePersonalOwner(question.PersonalOwnerUserId, question.TenantId);
        if (question.OwnerTeacherId.HasValue || (!question.PersonalOwnerUserId.HasValue && question.CurriculumSnapshotJson == null))
            EnsureTeachingOwner(question.OwnerTeacherId);
        var query = await Repository<QuestionSource>().GetQueryableAsync();
        var sources = await AsyncExecuter.ToListAsync(query.Where(x => x.QuestionId == question.Id));
        foreach (var source in sources.OrderBy(x => x.KnowledgeVersionId).ThenBy(x => x.Id))
        {
            if (requireUsableSources && !source.SchoolKnowledgeShareId.HasValue)
            {
                var metadata = await EnsureSourceMetadataAsync(source.KnowledgeItemId, source.KnowledgeVersionId, source.ChunkId, requireUsable: true);
                if (metadata.IsShared) throw Denied(); // An old reference without its original share cannot guess another live school's grant.
            }
            await EnsureSourceAsync(source.KnowledgeItemId, source.KnowledgeVersionId, source.ChunkId, requireUsableSources, forUpdate, source.SchoolKnowledgeShareId);
        }
        if (forUpdate) await Repository<Question>().UpdateManyAsync([question], autoSave: true);
    }

    public async Task<bool> CanReadQuestionAsync(Question question)
    {
        try
        {
            await EnsureQuestionAsync(question);
            return true;
        }
        catch (AbpAuthorizationException)
        {
            return false;
        }
    }

    public async Task<bool> CanReadQuestionAsync(Guid questionId)
    {
        var question = await Repository<Question>().FindAsync(questionId);
        return question != null && await CanReadQuestionAsync(question);
    }

    public async Task EnsureTaskAsync(AiQuestionGenerationTask task)
    {
        EnsurePersonalOwner(task.PersonalOwnerUserId, task.TenantId);
        if (task.OwnerTeacherId.HasValue || !task.PersonalOwnerUserId.HasValue)
            EnsureTeachingOwner(task.OwnerTeacherId);
        var candidates = await Repository<AiQuestionCandidate>().GetQueryableAsync();
        var citations = await Repository<AiQuestionCandidateCitation>().GetQueryableAsync();
        var sources = await AsyncExecuter.ToListAsync(citations.Where(x =>
            candidates.Where(candidate => candidate.TaskId == task.Id).Select(candidate => candidate.Id).Contains(x.CandidateId)));
        foreach (var source in sources)
        {
            await EnsureSourceAsync(source.KnowledgeItemId, source.KnowledgeVersionId, source.ChunkId, schoolKnowledgeShareId: source.SchoolKnowledgeShareId);
        }
    }

    public async Task<bool> CanReadTaskAsync(AiQuestionGenerationTask task)
    {
        try
        {
            await EnsureTaskAsync(task);
            return true;
        }
        catch (AbpAuthorizationException)
        {
            return false;
        }
    }

    public async Task<AiQuestionGenerationTask> EnsureCandidateAsync(AiQuestionCandidate candidate)
    {
        var task = await Repository<AiQuestionGenerationTask>().FindAsync(candidate.TaskId);
        if (task == null || candidate.TenantId != task.TenantId)
        {
            throw Denied();
        }

        await EnsureTaskAsync(task);
        return task;
    }

    private static AbpAuthorizationException Denied()
        => new("The requested content or its knowledge source is not available to the current user.");
}
