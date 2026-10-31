using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace KnowPulse.Education;

public partial class EducationTeachingPlanAppService
{
    private Task TargetAsync(EducationTeachingPlan plan, bool write) => TargetAsync(plan.Authority(), plan.AcademicTermId, plan.SchoolClassId, plan.StudentUserId, plan.StudentEnrollmentId, plan.SchoolMembershipId, write);
    private async Task TargetAsync(EducationCurriculumSnapshot scope, Guid termId, Guid classId, Guid studentId, Guid enrollmentId, Guid membershipId, bool write)
    {
        await Access.EnsureTeacherRelationAsync(scope, termId, classId, write);
        var enrollment = await RefAsync<StudentEnrollment>(enrollmentId); var member = await RefAsync<SchoolMembership>(membershipId);
        var student = await RefAsync<IdentityUser>(studentId); var now = DateTime.UtcNow;
        if (enrollment == null || member == null || student == null || enrollment.TenantId != CurrentTenant.Id || member.TenantId != CurrentTenant.Id || student.TenantId != CurrentTenant.Id ||
            !student.IsActive || !enrollment.IsEffectiveAt(now) || enrollment.StudentUserId != studentId || enrollment.SchoolId != scope.SchoolId || enrollment.SchoolClassId != classId ||
            member.UserId != studentId || member.SchoolId != scope.SchoolId || member.Role != SchoolMemberRole.Student || !member.IsEffectiveAt(now)) throw new AbpAuthorizationException();
    }
    private async Task<EducationTeachingPlan> OwnAsync(Guid id, bool write = false)
    {
        await TeacherAsync(); var plan = await RefAsync<EducationTeachingPlan>(id) ?? throw new AbpAuthorizationException();
        if (plan.TenantId != CurrentTenant.Id || plan.OwnerUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        await TargetAsync(plan, write);
        if (write) plan = await RefAsync<EducationTeachingPlan>(id) ?? throw new AbpAuthorizationException();
        if (plan.TenantId != CurrentTenant.Id || plan.OwnerUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        return plan;
    }
    private async Task<EducationTeachingPlanBasis> BasisAsync(Guid reportId, Guid snapshotId, Guid studentId)
    {
        var root = await RefAsync<EducationLearningReport>(reportId) ?? throw new AbpAuthorizationException();
        if (root.TenantId != CurrentTenant.Id || root.OwnerUserId != CurrentUser.Id || root.CurrentSnapshotId != snapshotId) throw Changed();
        await Access.EnsureTeacherRelationAsync(root.Authority(), root.AcademicTermId, root.SchoolClassId, forUpdate: true);
        root = await RefAsync<EducationLearningReport>(reportId) ?? throw Changed();
        var snapshot = await RefAsync<EducationLearningReportSnapshot>(snapshotId) ?? throw Changed();
        if (root.CurrentSnapshotId != snapshotId || snapshot.ReportId != reportId || snapshot.OwnerUserId != CurrentUser.Id || snapshot.TenantId != CurrentTenant.Id) throw Changed();
        var job = await RefAsync<EducationLearningReportJob>(snapshot.JobId) ?? throw Changed(); var old = job.Source();
        var current = await Reader.CaptureAsync(old.Curriculum, root.WindowStartsAtUtc, root.WindowEndsAtUtc, EducationLearningReportJson.Read<Guid[]>(root.SelectedTaskIdsJson, 32000));
        if (current.Fingerprint != snapshot.SourceFingerprint) throw Changed();
        return await ProjectBasisAsync(current, studentId, reportId, snapshotId, null);
    }
    private async Task<EducationTeachingPlanBasis> ProjectBasisAsync(EducationLearningReportSource source, Guid studentId, Guid reportId, Guid snapshotId, List<Guid>? taskFilter)
    {
        var student = source.Students.SingleOrDefault(x => x.Student.StudentUserId == studentId) ?? throw new AbpAuthorizationException();
        var tasks = taskFilter ?? student.Assessments.Select(x => x.TaskId).Order().ToList();
        var evidence = student.Evidence.Where(x => tasks.Contains(x.TaskId)).ToList(); var assessments = student.Assessments.Where(x => tasks.Contains(x.TaskId)).ToList();
        if (assessments.Count != tasks.Count) throw Changed();
        var analysis = EducationLearningAnalysis.Calculate(source.StudentScope(studentId), evidence, student.Attempts, source.Relationships, student.Exposures, DateTime.UtcNow, source.Rule);
        var questions = new List<EducationTeachingPlanQuestion>();
        foreach (var header in source.Assessments.Where(x => tasks.Contains(x.TaskId)).OrderBy(x => x.TaskId))
        {
            using var tracking = Repo<EducationAssessmentRecipient>().DisableTracking();
            var recipient = await AsyncExecuter.SingleOrDefaultAsync((await Repo<EducationAssessmentRecipient>().GetQueryableAsync()).Where(x => x.TaskId == header.TaskId &&
                x.TaskRevisionId == header.TaskRevisionId && x.StudentUserId == studentId && x.TenantId == CurrentTenant.Id));
            if (recipient == null || recipient.StudentEnrollmentId != student.Student.StudentEnrollmentId || recipient.SchoolMembershipId != student.Student.SchoolMembershipId) throw Changed();
            var paperQuestions = await RowsAsync<ExamPaperQuestion>(x => x.PaperId == header.ExamPaperId && x.TenantId == CurrentTenant.Id);
            questions.AddRange(paperQuestions.Select(x => new EducationTeachingPlanQuestion(header.TaskId, header.TaskRevisionId, recipient.Id, header.ExamPaperId, x.Id, x.OriginalQuestionId, x.QuestionDefinitionRevisionId ?? throw Invalid())));
        }
        var fingerprint = Hash(new { Scope = source.Curriculum, source.WindowStartsAtUtc, source.WindowEndsAtUtc, Student = student.Student,
            Rule = source.Rule, source.RuleDecisionRevisionId, Points = analysis.Points, Evidence = evidence.OrderBy(x => x.GradeRevisionId),
            Decisions = analysis.Evidence.OrderBy(x => x.GradeRevisionId).Select(x => new { x.GradeRevisionId, x.ExclusionReason }),
            Assessments = assessments.OrderBy(x => x.TaskId).Select(x => new { x.TaskId, x.RecipientId, x.AttemptId, x.SubmissionId, x.State, x.QuestionCount, x.ConfirmedQuestionCount, x.TotalScore, x.MaxScore, x.EffectiveAtUtc }),
            Questions = questions.OrderBy(x => x.PaperQuestionId).ThenBy(x => x.RecipientId) });
        return new() { ReportId = reportId, ReportSnapshotId = snapshotId, Curriculum = source.Curriculum, Student = student.Student,
            WindowStartsAtUtc = source.WindowStartsAtUtc, WindowEndsAtUtc = source.WindowEndsAtUtc, Fingerprint = fingerprint,
            TaskIds = tasks, Points = analysis.Points.ToList(), Evidence = evidence, Questions = questions };
    }
    private async Task<bool> FreshAsync(EducationTeachingPlanBasis basis, bool student = false)
    {
        var source = await Reader.CaptureAsync(basis.Curriculum, basis.WindowStartsAtUtc, basis.WindowEndsAtUtc, basis.TaskIds, acquireAuthorityBarrier: !student);
        var current = await ProjectBasisAsync(source, basis.Student.StudentUserId, basis.ReportId, basis.ReportSnapshotId, basis.TaskIds);
        return current.Fingerprint == basis.Fingerprint;
    }
    private async Task<EducationTeachingPlanAvailability> AvailabilityAsync(EducationTeachingPlan plan, EducationTeachingPlanRevision? revision, bool student = false)
    {
        if (plan.PublishedRevisionId == null) return plan.LastPublishedRevisionId == null ? EducationTeachingPlanAvailability.Waiting : EducationTeachingPlanAvailability.Withdrawn;
        if (revision?.Id != plan.PublishedRevisionId || revision.Kind != EducationTeachingPlanRevisionKind.Published) throw Invalid();
        var basis = revision.Basis()!;
        foreach (var taskId in basis.TaskIds)
        {
            var task = await RefAsync<EducationAssessmentTask>(taskId);
            var release = task?.CurrentResultReleaseId is Guid id ? await RefAsync<EducationAssessmentResultRelease>(id) : null;
            if (task == null || release == null || task.TenantId != plan.TenantId || release.TenantId != plan.TenantId || release.TaskId != taskId ||
                release.TaskRevisionId != task.PublishedRevisionId || release.ClosureId != task.ClosureId || release.VisibilityAt(DateTime.UtcNow) != ExamResultVisibility.FullReview)
                return EducationTeachingPlanAvailability.Waiting;
        }
        try { return await FreshAsync(basis, student) ? EducationTeachingPlanAvailability.Available : EducationTeachingPlanAvailability.SourceChanged; }
        catch (AbpAuthorizationException) { return EducationTeachingPlanAvailability.Unavailable; }
        catch (BusinessException error) when (error.Code is EducationLearningReportErrors.SourceChanged or EducationTeachingPlanErrors.SourceChanged or EducationLearningErrorCodes.InvalidRule or EducationLearningReportErrors.Capacity)
        { return EducationTeachingPlanAvailability.SourceChanged; }
    }
}
