using System;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.EntityFrameworkCore;

public abstract class KnowPulseEntityFrameworkCoreTestBase : KnowPulseTestBase<KnowPulseEntityFrameworkCoreTestModule>
{
    protected async Task<KnowledgeItemDto> CreateLegacyKnowledgeAsync(CreateKnowledgeItemDto input)
    {
        // 旧企业权限场景显式保留无教师归属；新教师资料仍必须走应用服务创建。
        var item = await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<KnowledgeItem, Guid>>().InsertAsync(
            new KnowledgeItem(Guid.NewGuid(), GetRequiredService<ICurrentTenant>().Id,
                input.Title, input.CategoryId, input.Summary), autoSave: true));
        return await GetRequiredService<IKnowledgeItemAppService>().GetAsync(item.Id);
    }

    protected Task<SchoolKnowledgeShare> SeedHistoricalSchoolShareAsync(Guid itemId, Guid versionId,
        Guid schoolId, SchoolKnowledgeShareUses uses, string? summary = null) => WithUnitOfWorkAsync(async () =>
    {
        var item = await GetRequiredService<IRepository<KnowledgeItem, Guid>>().GetAsync(itemId);
        var version = await GetRequiredService<IRepository<KnowledgeVersion, Guid>>().GetAsync(versionId);
        return await GetRequiredService<IRepository<SchoolKnowledgeShare, Guid>>().InsertAsync(
            new SchoolKnowledgeShare(Guid.NewGuid(), item.TenantId, item.OwnerUserId!.Value,
                item.LibraryId!.Value, item.Id, version.Id, schoolId, version.Title, summary ?? item.Summary,
                uses, DateTime.UtcNow), autoSave: true);
    });

    protected Task RevokeHistoricalSchoolShareAsync(Guid shareId) => WithUnitOfWorkAsync(async () =>
    {
        var shares = GetRequiredService<IRepository<SchoolKnowledgeShare, Guid>>();
        var share = await shares.GetAsync(shareId);
        share.Revoke(DateTime.UtcNow);
        await shares.UpdateAsync(share, autoSave: true);
    });
}
