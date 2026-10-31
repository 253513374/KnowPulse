using System.Threading;
using System.Threading.Tasks;

namespace KnowPulse.WeChat;

public sealed record WeChatVerifiedIdentity(string AppId, string SubjectHash);

public interface IWeChatLoginVerifier
{
    Task<WeChatVerifiedIdentity> VerifyAsync(string appKey, string code, CancellationToken cancellationToken = default);
}
