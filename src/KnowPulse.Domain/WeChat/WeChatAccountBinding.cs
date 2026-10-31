using System;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.WeChat;

[DisableAuditing]
public class WeChatAccountBinding : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid AccountUserId { get; private set; }
    public string AppKey { get; private set; } = string.Empty;
    public string AppId { get; private set; } = string.Empty;
    public string ClientId { get; private set; } = string.Empty;
    public string CodeSecretHash { get; private set; } = string.Empty;
    public string AccountSecurityStampHash { get; private set; } = string.Empty;
    public DateTime IssuedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public string? SubjectHash { get; private set; }
    public DateTime? BoundAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? ActiveAccountKey { get; private set; }
    public string? ActiveSubjectKey { get; private set; }

    protected WeChatAccountBinding() { }

    public WeChatAccountBinding(Guid id, Guid? tenantId, Guid accountUserId, string appKey, string appId,
        string clientId, string codeSecretHash, string accountSecurityStampHash, DateTime now) : base(id)
    {
        if (id == Guid.Empty || accountUserId == Guid.Empty || now.Kind != DateTimeKind.Utc ||
            !WeChatLoginValues.IsHash(codeSecretHash) || !WeChatLoginValues.IsHash(accountSecurityStampHash)) throw Invalid();
        TenantId = tenantId; AccountUserId = accountUserId;
        AppKey = Check.NotNullOrWhiteSpace(appKey, nameof(appKey), 64);
        AppId = Check.NotNullOrWhiteSpace(appId, nameof(appId), 64);
        ClientId = Check.NotNullOrWhiteSpace(clientId, nameof(clientId), 128);
        CodeSecretHash = codeSecretHash; AccountSecurityStampHash = accountSecurityStampHash;
        IssuedAtUtc = now; ExpiresAtUtc = now.AddMinutes(WeChatLoginValues.BindingCodeMinutes);
    }

    public bool Bind(string subjectHash, DateTime now)
    {
        if (!WeChatLoginValues.IsHash(subjectHash) || now.Kind != DateTimeKind.Utc || now < IssuedAtUtc || RevokedAtUtc.HasValue) throw Invalid();
        if (BoundAtUtc.HasValue)
        {
            if (SubjectHash != subjectHash) throw Invalid();
            return false;
        }
        if (now >= ExpiresAtUtc) throw Invalid();
        SubjectHash = subjectHash; BoundAtUtc = now;
        var scope = $"{TenantId?.ToString("N") ?? "host"}|{AppId}|";
        ActiveAccountKey = WeChatLoginValues.Hash(scope + AccountUserId.ToString("N"));
        ActiveSubjectKey = WeChatLoginValues.Hash(scope + subjectHash);
        return true;
    }

    public bool Revoke(DateTime now)
    {
        if (now.Kind != DateTimeKind.Utc || now < IssuedAtUtc) throw Invalid();
        if (RevokedAtUtc.HasValue) return false;
        RevokedAtUtc = now; ActiveAccountKey = null; ActiveSubjectKey = null;
        return true;
    }

    public bool IsActive => BoundAtUtc.HasValue && !RevokedAtUtc.HasValue;
    private static BusinessException Invalid() => new(WeChatLoginErrorCodes.InvalidBinding);
}
