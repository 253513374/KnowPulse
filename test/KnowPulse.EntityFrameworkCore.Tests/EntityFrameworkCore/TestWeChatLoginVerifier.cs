using System.Threading;
using System.Threading.Tasks;
using KnowPulse.WeChat;
using Volo.Abp;

namespace KnowPulse.EntityFrameworkCore;

public sealed class TestWeChatLoginVerifier : IWeChatLoginVerifier
{
    public int Calls;
    public string AppId = "wx0123456789abcdef";
    public string SubjectHash = WeChatLoginValues.Hash("synthetic-wechat-identity");
    public bool Reject;

    public Task<WeChatVerifiedIdentity> VerifyAsync(string appKey, string code, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        if (Reject) throw new BusinessException(WeChatLoginErrorCodes.InvalidCode);
        return Task.FromResult(new WeChatVerifiedIdentity(AppId, SubjectHash));
    }
}
