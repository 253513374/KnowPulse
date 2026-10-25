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
}
