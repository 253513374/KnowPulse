using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace KnowPulse.Education;

public partial class EducationAssessmentAppService
{
    private static BusinessException CorrectionInvalid() => new(EducationCorrectionErrors.Invalid);
    private async Task CorrectionTeacherAsync() { await CheckMvpPermissionAsync(EducationPermissions.LearningManage); await TeacherAsync(); }
    private async Task CorrectionStudentAsync() { await CheckMvpPermissionAsync(EducationPermissions.LearningParticipate); await StudentAsync(); }
    private async Task CorrectionStudentRelationAsync(EducationCorrectionTask root, bool write)
    {
        using var tracking = write ? Repo<IdentityUser>().EnableTracking() : null; var baseline = write ? await Repo<IdentityUser>().FindAsync(root.StudentUserId) : null;
        async Task ValidateAsync() {
            var account = await ReferenceAsync<IdentityUser>(root.StudentUserId); var member = await ReferenceAsync<SchoolMembership>(root.SchoolMembershipId); var enrollment = await ReferenceAsync<StudentEnrollment>(root.StudentEnrollmentId); var now = DateTime.UtcNow;
            if (account == null || account.TenantId != root.TenantId || !account.IsActive || member == null || member.TenantId != root.TenantId || member.SchoolId != root.SchoolId ||
                member.UserId != root.StudentUserId || member.Role != SchoolMemberRole.Student || !member.IsEffectiveAt(now) || enrollment == null || enrollment.TenantId != root.TenantId ||
                enrollment.SchoolId != root.SchoolId || enrollment.SchoolClassId != root.SchoolClassId || enrollment.StudentUserId != root.StudentUserId || !enrollment.IsEffectiveAt(now)) throw new AbpAuthorizationException();
        }
        await ValidateAsync(); if (write) { if (baseline == null) throw new AbpAuthorizationException(); await Repo<IdentityUser>().UpdateManyAsync([baseline], autoSave: true); await ValidateAsync(); }
    }
    private async Task<EducationCorrectionTask> CorrectionAccessAsync(Guid id, bool student, bool write = true)
    {
        if (student) await CorrectionStudentAsync(); else await CorrectionTeacherAsync();
        var root = await ReferenceAsync<EducationCorrectionTask>(id) ?? throw new AbpAuthorizationException();
        if (root.TenantId != CurrentTenant.Id || (student ? root.StudentUserId : root.OwnerUserId) != CurrentUser.Id) throw new AbpAuthorizationException();
        if (student) await Access.EnsureStudentAsync(root.SchoolId, root.AcademicTermId, root.SchoolClassId, root.SchoolMembershipId, root.StudentEnrollmentId, root.StudentUserId, write);
        else { await Access.EnsureTeacherRelationAsync(root.Authority(), root.AcademicTermId, root.SchoolClassId, write); await CorrectionStudentRelationAsync(root, write); }
        return await ReferenceAsync<EducationCorrectionTask>(id) ?? throw CorrectionInvalid();
    }
    private async Task<(EducationCorrectionDelivery Delivery, EducationCorrectionTask Task, EducationCorrectionTaskRevision Publication)> CorrectionDeliveryAsync(Guid id, bool student)
    {
        if (student) await CorrectionStudentAsync(); else await CorrectionTeacherAsync();
        var row = await ReferenceAsync<EducationCorrectionDelivery>(id) ?? throw new AbpAuthorizationException(); if (row.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        var root = await CorrectionAccessAsync(row.TaskId, student); row = await ReferenceAsync<EducationCorrectionDelivery>(id) ?? throw CorrectionInvalid();
        var publication = await ReferenceAsync<EducationCorrectionTaskRevision>(row.PublicationId) ?? throw CorrectionInvalid();
        if (row.OwnerUserId != root.OwnerUserId || row.StudentUserId != root.StudentUserId || publication.TaskId != root.Id || publication.TenantId != root.TenantId ||
            publication.OwnerUserId != root.OwnerUserId || publication.StudentUserId != root.StudentUserId || publication.Kind != EducationCorrectionRevisionKind.Published) throw CorrectionInvalid();
        return (row, root, publication);
    }
    private sealed record CorrectionGate(EducationAssessmentQuestionGrade Grade, EducationAssessmentGradeRevision Confirmed, EducationAssessmentSubmission Submission,
        EducationAssessmentRecipient Recipient, EducationAssessmentResultRelease? Release, EducationAssessmentReleasedGradeReference? Reference, bool FullReview);
    private async Task<CorrectionGate> CorrectionGateAsync(EducationCorrectionTask root, bool barrier = true)
    {
        using var tracking = barrier ? Repo<EducationAssessmentTask>().EnableTracking() : null;
        var task = barrier ? await Repo<EducationAssessmentTask>().GetAsync(root.SourceTaskId) : await ReferenceAsync<EducationAssessmentTask>(root.SourceTaskId) ?? throw CorrectionInvalid();
        if (task.TenantId != root.TenantId || task.OwnerUserId != root.OwnerUserId) throw CorrectionInvalid();
        if (barrier) await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true);
        var grade = await ReferenceAsync<EducationAssessmentQuestionGrade>(root.QuestionGradeId) ?? throw CorrectionInvalid();
        var confirmed = grade.CurrentConfirmedRevisionId is Guid gid ? await ReferenceAsync<EducationAssessmentGradeRevision>(gid) ?? throw CorrectionInvalid() : throw CorrectionInvalid();
        var submission = await ReferenceAsync<EducationAssessmentSubmission>(root.SubmissionId) ?? throw CorrectionInvalid(); var recipient = await ReferenceAsync<EducationAssessmentRecipient>(root.AssessmentRecipientId) ?? throw CorrectionInvalid();
        if (grade.TenantId != root.TenantId || confirmed.TenantId != root.TenantId || submission.TenantId != root.TenantId || recipient.TenantId != root.TenantId || grade.OwnerUserId != root.OwnerUserId ||
            grade.StudentUserId != root.StudentUserId || grade.TaskId != root.SourceTaskId || grade.TaskRevisionId != root.SourceTaskRevisionId || grade.SubmissionId != root.SubmissionId || grade.PaperQuestionId != root.PaperQuestionId ||
            confirmed.QuestionGradeId != grade.Id || confirmed.Kind != EducationAssessmentGradeRevisionKind.Confirmed || confirmed.SubmissionId != submission.Id || confirmed.PaperQuestionId != root.PaperQuestionId ||
            submission.StudentUserId != root.StudentUserId || recipient.StudentUserId != root.StudentUserId || recipient.TaskId != root.SourceTaskId || recipient.TaskRevisionId != root.SourceTaskRevisionId) throw CorrectionInvalid();
        var release = task.CurrentResultReleaseId is Guid rid ? await ReferenceAsync<EducationAssessmentResultRelease>(rid) : null;
        if (release == null) return new(grade, confirmed, submission, recipient, null, null, false);
        if (release.TaskId != task.Id || release.TenantId != root.TenantId) throw CorrectionInvalid();
        var item = (await ReferencesAsync<EducationAssessmentResultReleaseItem>(x => x.ResultReleaseId == release.Id && x.RecipientId == recipient.Id && x.StudentUserId == root.StudentUserId && x.TenantId == root.TenantId)).SingleOrDefault();
        var reference = item?.Grades.Grades.SingleOrDefault(x => x.QuestionGradeId == grade.Id && x.PaperQuestionId == root.PaperQuestionId);
        var full = task.PublishedRevisionId == root.SourceTaskRevisionId && release.ConfirmedGradeSetVersion == task.ConfirmedGradeSetVersion && reference?.GradeRevisionId == confirmed.Id &&
            item?.SubmissionId == root.SubmissionId && release.VisibilityAt(DateTime.UtcNow) == ExamResultVisibility.FullReview;
        return new(grade, confirmed, submission, recipient, release, reference, full);
    }
    private async Task<EducationCorrectionAvailability> CorrectionAvailabilityAsync(EducationCorrectionTask root, EducationCorrectionTaskRevision publication, CorrectionGate gate)
    {
        if (!gate.FullReview) return EducationCorrectionAvailability.ResultUnavailable;
        if (gate.Confirmed.Id != publication.GradeRevisionId) return EducationCorrectionAvailability.SourceChanged;
        if (root.PublishedRevisionId == null) return EducationCorrectionAvailability.Withdrawn;
        if (root.PublishedRevisionId != publication.Id) return EducationCorrectionAvailability.Superseded;
        try { await Access.EnsureRecordedTeacherRelationAsync(root.Authority(), root.AcademicTermId, root.SchoolClassId); } catch (AbpAuthorizationException) { return EducationCorrectionAvailability.Unavailable; }
        var content = publication.Content()!; var now = DateTime.UtcNow;
        return now < content.StartsAtUtc ? EducationCorrectionAvailability.Waiting : now >= content.DueAtUtc ? EducationCorrectionAvailability.Overdue : EducationCorrectionAvailability.Available;
    }
}
