using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;

namespace KnowPulse.WeChat;

public interface IWeChatAccountAppService : IApplicationService
{
    [Authorize] Task<ListResultDto<WeChatApplicationDto>> GetApplicationsAsync();
    [Authorize] Task<PagedResultDto<WeChatAccountBindingDto>> GetMyBindingsAsync(PagedResultRequestDto input);
    [Authorize] Task<WeChatBindingCodeDto> IssueBindingCodeAsync(IssueWeChatBindingCodeDto input);
    [Authorize] Task<WeChatAccountBindingDto> RevokeMyBindingAsync(Guid id);
}

public interface IWeChatBindingExchange
{
    Task<WeChatBindingReceiptDto> ExchangeBindingCodeAsync(ExchangeWeChatBindingCodeDto input);
}

public class WeChatApplicationDto
{
    public string AppKey { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class WeChatAccountBindingDto : EntityDto<Guid>
{
    public string AppKey { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? BoundAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public bool RequiresRebinding { get; set; }
}

[DisableAuditing]
public class WeChatBindingCodeDto
{
    public string BindingCode { get; set; } = string.Empty;
    public string AppKey { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}

[DisableAuditing]
public class IssueWeChatBindingCodeDto
{
    public Guid ExpectedAccountUserId { get; set; }
    public Guid? ExpectedTenantId { get; set; }
    [Required, StringLength(64)] public string AppKey { get; set; } = string.Empty;
    [Required, StringLength(256)] public string Password { get; set; } = string.Empty;
    public bool ConfirmOwnWeChatAndAccount { get; set; }
}

[DisableAuditing]
public class ExchangeWeChatBindingCodeDto
{
    [Required, StringLength(64)] public string AppKey { get; set; } = string.Empty;
    [Required, StringLength(80)] public string BindingCode { get; set; } = string.Empty;
    [Required, StringLength(512)] public string Code { get; set; } = string.Empty;
}

public class WeChatBindingReceiptDto
{
    public Guid BindingId { get; set; }
    public Guid AccountUserId { get; set; }
    public Guid? TenantId { get; set; }
    public DateTime BoundAtUtc { get; set; }
}
