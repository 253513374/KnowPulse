using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Volo.Abp;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Linq;
using Volo.Abp.Security.Claims;

namespace KnowPulse.KnowledgeBase;

internal sealed record PersonalExportSourceRef(string Kind, Guid Id, Guid ParentId, string Stamp);
internal sealed record PersonalExportSnapshot(List<KnowledgeItem> Items, List<KnowledgeVersion> Versions,
    List<KnowledgeAttachment> Attachments)
{
    internal List<PersonalExportSourceRef> References => Items.Select(x => new PersonalExportSourceRef("item", x.Id, Guid.Empty, x.ConcurrencyStamp))
        .Concat(Versions.Select(x => new PersonalExportSourceRef("version", x.Id, x.KnowledgeItemId, x.ConcurrencyStamp)))
        .Concat(Attachments.Select(x => new PersonalExportSourceRef("attachment", x.Id, x.KnowledgeVersionId, x.ConcurrencyStamp))).ToList();
}

internal static class PersonalKnowledgeExportSource
{
    internal static async Task EnsureOwnerAsync(IAbpLazyServiceProvider services, PersonalKnowledgeExport job, CancellationToken ct = default)
    {
        var executor = services.LazyGetRequiredService<IAsyncQueryableExecuter>();
        var users = await services.LazyGetRequiredService<IRepository<IdentityUser, Guid>>().GetQueryableAsync();
        var account = await executor.FirstOrDefaultAsync(users.Where(x => !x.IsDeleted && x.IsActive &&
            x.Id == job.OwnerUserId && x.TenantId == job.TenantId), ct);
        var libraries = await services.LazyGetRequiredService<IRepository<KnowledgeLibrary, Guid>>().GetQueryableAsync();
        if (account == null || job.LibraryId != job.OwnerUserId || !await executor.AnyAsync(libraries.Where(x =>
            !x.IsDeleted && x.Id == job.LibraryId && x.OwnerUserId == job.OwnerUserId && x.TenantId == job.TenantId), ct))
            throw new BusinessException(PersonalKnowledgeExportErrorCodes.AccountUnavailable);

        // A worker has no request principal. Rebuild permission context from the current account and its recorded roles.
        var claims = new List<Claim> { new(AbpClaimTypes.UserId, account.Id.ToString()) };
        if (account.TenantId.HasValue) claims.Add(new Claim(AbpClaimTypes.TenantId, account.TenantId.Value.ToString()));
        var roles = await services.LazyGetRequiredService<IdentityUserManager>().GetRolesAsync(account);
        claims.AddRange(roles.Select(role => new Claim(AbpClaimTypes.Role, role)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "PersonalExport", AbpClaimTypes.UserName, AbpClaimTypes.Role));
        if (!await services.LazyGetRequiredService<IPermissionChecker>().IsGrantedAsync(principal, KnowPulsePermissions.KnowledgeBase.PersonalLibrary))
            throw new BusinessException(PersonalKnowledgeExportErrorCodes.AccountUnavailable);
    }

    internal static async Task<PersonalExportSnapshot> CaptureAsync(IAbpLazyServiceProvider services,
        PersonalKnowledgeExport job, PersonalKnowledgeExportOptions limits, CancellationToken ct)
    {
        await EnsureOwnerAsync(services, job, ct);
        var executor = services.LazyGetRequiredService<IAsyncQueryableExecuter>();
        var itemQuery = (await services.LazyGetRequiredService<IRepository<KnowledgeItem, Guid>>().GetQueryableAsync())
            .Where(x => !x.IsDeleted && x.TenantId == job.TenantId && x.LibraryId == job.LibraryId && x.OwnerUserId == job.OwnerUserId);
        var versionQuery = (await services.LazyGetRequiredService<IRepository<KnowledgeVersion, Guid>>().GetQueryableAsync())
            .Where(x => !x.IsDeleted && x.TenantId == job.TenantId && itemQuery.Any(item => item.Id == x.KnowledgeItemId));
        var attachmentQuery = (await services.LazyGetRequiredService<IRepository<KnowledgeAttachment, Guid>>().GetQueryableAsync())
            .Where(x => !x.IsDeleted && x.TenantId == job.TenantId && versionQuery.Any(version => version.Id == x.KnowledgeVersionId));
        var items = await executor.ToListAsync(itemQuery.OrderBy(x => x.Id).Take(limits.MaxItems + 1), ct);
        var versions = await executor.ToListAsync(versionQuery.OrderBy(x => x.Id).Take(limits.MaxVersions + 1), ct);
        var attachments = await executor.ToListAsync(attachmentQuery.OrderBy(x => x.Id).Take(limits.MaxAttachments + 1), ct);
        if (items.Count > limits.MaxItems || versions.Count > limits.MaxVersions || attachments.Count > limits.MaxAttachments)
            throw new BusinessException(PersonalKnowledgeExportErrorCodes.LimitExceeded);
        return new PersonalExportSnapshot(items, versions, attachments);
    }

    internal static async Task ValidateAsync(IAbpLazyServiceProvider services, PersonalKnowledgeExport job,
        PersonalKnowledgeExportOptions limits, bool checkStamps, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(job.SourceManifestJson)) throw new BusinessException(PersonalKnowledgeExportErrorCodes.SourceUnavailable);
        var references = JsonSerializer.Deserialize<List<PersonalExportSourceRef>>(job.SourceManifestJson)
            ?? throw new BusinessException(PersonalKnowledgeExportErrorCodes.SourceUnavailable);
        var snapshot = await CaptureAsync(services, job, limits, ct);
        var current = snapshot.References.ToDictionary(x => (x.Kind, x.Id));
        if (references.Any(reference => !current.TryGetValue((reference.Kind, reference.Id), out var row) ||
            row.ParentId != reference.ParentId || (checkStamps && row.Stamp != reference.Stamp)) ||
            (checkStamps && references.Count != current.Count))
            throw new BusinessException(checkStamps ? PersonalKnowledgeExportErrorCodes.SourceChanged : PersonalKnowledgeExportErrorCodes.SourceUnavailable);
    }
}
