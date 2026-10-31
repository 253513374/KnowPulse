using System;
using System.Linq;
using KnowPulse.Education;

namespace KnowPulse.Blazor.Client.Pages.Education;

internal sealed class EducationAssessmentSubmissionReview
{
    public EducationAssessmentSubmissionPreviewDto? Preview { get; private set; }
    public bool ConfirmSavedOnly { get; set; }

    public void Invalidate() { Preview = null; ConfirmSavedOnly = false; }

    public void Prepare(EducationAssessmentSubmissionPreviewDto preview, EducationStudentAssessmentAttemptDto attempt)
    {
        Invalidate();
        if (preview.AttemptId != attempt.Id || preview.PaperSnapshotVersion != attempt.PaperSnapshotVersion ||
            preview.ResponseSetVersion != attempt.ResponseSetVersion || preview.Fingerprint?.Length != 64 ||
            !preview.CanSubmit || preview.SubmissionId != null ||
            !preview.Questions.Select(x => x.Id).OrderBy(x => x).SequenceEqual(attempt.Paper.Questions.Select(x => x.Id).OrderBy(x => x)))
            throw new InvalidOperationException(EducationAssessmentErrorCodes.PreparationChanged);
        Preview = preview;
    }

    public bool HasExclusions(int localItemCount) => localItemCount > 0 || Preview is { } preview &&
        (preview.Questions.Any(x => x.IsUnanswered || x.UnansweredBlankCount > 0) || preview.UnlinkedImages.Count != 0);

    public SubmitEducationAssessmentDto Submit(Guid operationId, int localItemCount)
    {
        var preview = Preview ?? throw new InvalidOperationException(EducationAssessmentErrorCodes.PreparationRequired);
        if (HasExclusions(localItemCount) && !ConfirmSavedOnly)
            throw new InvalidOperationException(EducationAssessmentErrorCodes.IncompleteSubmission);
        return new() { OperationId = operationId, PaperSnapshotVersion = preview.PaperSnapshotVersion,
            ExpectedResponseSetVersion = preview.ResponseSetVersion, ExpectedPreparationFingerprint = preview.Fingerprint,
            ConfirmSavedOnly = ConfirmSavedOnly };
    }
}
