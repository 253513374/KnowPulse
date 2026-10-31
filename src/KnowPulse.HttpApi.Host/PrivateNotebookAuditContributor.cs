using System;
using Volo.Abp.Auditing;

namespace KnowPulse;

public sealed class PrivateNotebookAuditContributor : AuditLogContributor
{
    public override void PostContribute(AuditLogContributionContext context)
    {
        var url = context.AuditInfo.Url;
        if (url != null && (url.StartsWith("/api/app/personal-notebook", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("/api/app/knowledge-library/my-items", StringComparison.OrdinalIgnoreCase)))
            context.AuditInfo.Url = url.Split('?')[0];
    }
}
