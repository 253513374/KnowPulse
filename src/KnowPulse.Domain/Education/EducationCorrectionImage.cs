using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationCorrectionImage : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid DeliveryId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public long Size { get; private set; }
    public string ContentHash { get; private set; } = "";
    public string BlobName { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    public DateTime ReservedAtUtc { get; private set; }
    public DateTime? UploadedAtUtc { get; private set; }
    protected EducationCorrectionImage() { }
    public EducationCorrectionImage(Guid id, EducationCorrectionDelivery delivery, string name, string type, long size, string hash, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (size is < 1 or > 5242880 || type is not ("image/png" or "image/jpeg" or "image/webp") || name == null || name.IndexOfAny(['/', '\\', '\r', '\n', '\0']) >= 0) throw new BusinessException(EducationCorrectionErrors.Invalid);
        TenantId = delivery.TenantId; DeliveryId = delivery.Id; StudentUserId = delivery.StudentUserId; FileName = Check.NotNullOrWhiteSpace(name, nameof(name), 256);
        ContentType = type; Size = size; ContentHash = EducationAssessmentTaskRevision.RequireFingerprint(hash); RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint);
        BlobName = $"correction/{TenantId?.ToString("N") ?? "host"}/{StudentUserId:N}/{DeliveryId:N}/{id:N}"; ReservedAtUtc = EducationRules.RequireUtc(now);
    }
    public void Confirm(DateTime now) { if (EducationRules.RequireUtc(now) < ReservedAtUtc) throw new BusinessException(EducationCorrectionErrors.Invalid); UploadedAtUtc ??= now; }
}
