using System.Threading;
using System.Threading.Tasks;
using KnowPulse.WeChat;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;

namespace KnowPulse.EntityFrameworkCore;

public class WeChatAccountBindingRepository : EfCoreRepository<KnowPulseDbContext, WeChatAccountBinding, System.Guid>, IWeChatAccountBindingRepository
{
    public WeChatAccountBindingRepository(IDbContextProvider<KnowPulseDbContext> provider) : base(provider) { }

    public async Task<WeChatAccountBinding> SaveBoundAsync(WeChatAccountBinding binding, CancellationToken cancellationToken = default)
    {
        try { return await UpdateAsync(binding, autoSave: true, cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw; }
        // Constraint failures must not disclose stored binding keys through provider exceptions.
        catch (DbUpdateException) { throw new BusinessException(WeChatLoginErrorCodes.InvalidBinding); }
    }
}
