using System;
using KnowPulse.Education;

namespace KnowPulse.Blazor.Client.Pages.Education;

public sealed class EducationCorrectionLocalState
{
    public Guid? AccountId { get; set; }
    public Guid? TenantId { get; set; }
    public Guid DeliveryId { get; set; }
    public Guid? BasedOnSubmissionId { get; set; }
    public EducationCorrectionContent Content { get; set; } = new();
    public SubmitEducationCorrectionDto? Pending { get; set; }
    public string? PendingFingerprint { get; set; }
    public Guid? ImageOperationId { get; set; }
    public string? ImageHash { get; set; }
    public string? ImageName { get; set; }
    public void Validate(Guid? account, Guid? tenant, Guid delivery)
    {
        if (account == null || AccountId != account || TenantId != tenant || DeliveryId != delivery || delivery == Guid.Empty || Content == null) throw new InvalidOperationException();
        Content.Validate();
        if (Pending != null) {
            Pending.Content.Validate(submitted: true);
            if (Pending.OperationId == Guid.Empty || Pending.ExpectedSubmissionId != BasedOnSubmissionId || !Pending.ConfirmSharingWithTeacher ||
                Pending.PreviewFingerprint.Length != 64 || PendingFingerprint != EducationCorrectionFingerprint.Submit(delivery, Pending) ||
                System.Text.Json.JsonSerializer.Serialize(Pending.Content) != System.Text.Json.JsonSerializer.Serialize(Content)) throw new InvalidOperationException();
        } else if (PendingFingerprint != null) throw new InvalidOperationException();
        if (ImageOperationId != null && (ImageOperationId == Guid.Empty || ImageHash?.Length != 64 || string.IsNullOrWhiteSpace(ImageName) || Pending != null) || ImageOperationId == null && (ImageHash != null || ImageName != null)) throw new InvalidOperationException();
    }
    public bool ConflictsWith(Guid? serverHead) => BasedOnSubmissionId != serverHead;
    public bool MatchesPreview(EducationCorrectionSubmissionPreviewDto? preview) => preview != null && preview.DeliveryId == DeliveryId && preview.ExpectedSubmissionId == BasedOnSubmissionId &&
        System.Text.Json.JsonSerializer.Serialize(preview.Content) == System.Text.Json.JsonSerializer.Serialize(Content);
}
