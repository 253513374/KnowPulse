using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.WeChat;

public interface IWeChatAccountBindingRepository : IRepository<WeChatAccountBinding, Guid>
{
    Task<WeChatAccountBinding> SaveBoundAsync(WeChatAccountBinding binding, CancellationToken cancellationToken = default);
}
