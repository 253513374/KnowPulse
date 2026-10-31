using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

[Authorize]
public partial class EducationLearningReportAppService : KnowPulseAppService, IEducationLearningReportAppService
{
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private IUnitOfWorkManager Uow => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();
    private EducationAssessmentAccessGuard Access => LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAccessGuard>();
    private EducationLearningReportSourceReader Reader => LazyServiceProvider.LazyGetRequiredService<EducationLearningReportSourceReader>();
    private async Task<T?> RefAsync<T>(Guid id) where T : class, IEntity<Guid>
    { using var tracking = Repo<T>().DisableTracking(); return await Repo<T>().FindAsync(id); }
    private async Task TeacherAsync() { await CheckMvpPermissionAsync(EducationPermissions.AnalysisManage); await Access.CurrentAccountAsync(); }
    private static string Hash(object input) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));
    private static BusinessException Changed() => new(EducationLearningReportErrors.SourceChanged);
    private static int Page(PagedResultRequestDto input)
    { if (input.SkipCount < 0 || input.MaxResultCount is < 1 or > 100) throw new BusinessException(EducationLearningReportErrors.Invalid); return input.MaxResultCount; }
    private static bool Recoverable(Exception error) => error is not BusinessException && error is not AbpAuthorizationException && error is not OperationCanceledException;

    private async Task<EducationLearningReport> OwnAsync(Guid id, bool write = false)
    {
        await TeacherAsync(); var report = await RefAsync<EducationLearningReport>(id) ?? throw new AbpAuthorizationException();
        if (report.TenantId != CurrentTenant.Id || report.OwnerUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        await Access.EnsureTeacherRelationAsync(report.Authority(), report.AcademicTermId, report.SchoolClassId, write);
        return write ? await RefAsync<EducationLearningReport>(id) ?? throw new AbpAuthorizationException() : report;
    }
    private static void Stamp(EducationLearningReport report, string stamp, Guid expectedJob)
    { if (report.ConcurrencyStamp != stamp || report.CurrentJobId != expectedJob) throw new AbpDbConcurrencyException(); }
    private sealed record JobMetadata(EducationLearningReportJobDto Dto, string CurriculumJson);
    private async Task<JobMetadata?> MetadataAsync(Guid reportId, Guid id)
    {
        using var tracking = Repo<EducationLearningReportJob>().DisableTracking();
        return await AsyncExecuter.SingleOrDefaultAsync((await Repo<EducationLearningReportJob>().GetQueryableAsync())
            .Where(x => x.Id == id && x.ReportId == reportId && x.OwnerUserId == CurrentUser.Id && x.TenantId == CurrentTenant.Id)
            .Select(x => new JobMetadata(new EducationLearningReportJobDto { Id = x.Id, ReportId = x.ReportId, Generation = x.Generation, RetryOfJobId = x.RetryOfJobId,
                Status = x.Status, AttemptCount = x.AttemptCount, CompletedStudentCount = x.CompletedStudentCount, StudentCount = x.StudentCount, RequestedAtUtc = x.RequestedAtUtc,
                HeartbeatAtUtc = x.HeartbeatAtUtc, CompletedAtUtc = x.CompletedAtUtc, FailureStage = x.FailureStage, CapacityExceeded = x.CapacityExceeded,
                SourceFingerprint = x.SourceFingerprint, RequestFingerprint = x.RequestFingerprint }, x.CurriculumJson)));
    }
    private async Task<EducationLearningReportJobDto?> ReceiptAsync(Guid reportId, Guid operationId, string? fingerprint = null)
    {
        await TeacherAsync(); var report = await RefAsync<EducationLearningReport>(reportId); if (report == null) return null;
        await OwnAsync(reportId);
        var receipt = await MetadataAsync(reportId, operationId); if (receipt == null) return null;
        if (fingerprint != null && receipt.Dto.RequestFingerprint != fingerprint) throw new BusinessException(EducationLearningReportErrors.OperationConflict);
        if (report.CurrentJobId != operationId && receipt.Dto.Status != EducationLearningReportJobStatus.Completed) receipt.Dto.Status = EducationLearningReportJobStatus.Superseded;
        return receipt.Dto;
    }
    private async Task<EducationLearningReportJobDto> CompleteAsync(IUnitOfWork uow, Guid reportId, Guid operationId)
    { await uow.SaveChangesAsync(); var result = (await MetadataAsync(reportId, operationId))!.Dto; await uow.CompleteAsync(); return result; }

    [Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationLearningReportJobDto> RequestAsync(RequestEducationLearningReportDto input)
    {
        await TeacherAsync(); EducationRules.RequireId(input.OperationId); var fp = Hash(new { Command = "report-request", input });
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var prior = await ReceiptAsync(input.OperationId, input.OperationId, fp); if (prior != null) { await uow.CompleteAsync(); return prior; }
            var scope = await LazyServiceProvider.LazyGetRequiredService<EducationCurriculumAccessGuard>().ResolveAsync(input.Scope);
            var source = await Reader.CaptureAsync(scope, input.WindowStartsAtUtc, input.WindowEndsAtUtc, input.TaskIds);
            var report = new EducationLearningReport(input.OperationId, source, input.TaskIds, fp);
            var job = new EducationLearningReportJob(input.OperationId, report, source, fp, report.ConcurrencyStamp, DateTime.UtcNow);
            await Repo<EducationLearningReport>().InsertAsync(report, autoSave: true);
            report.Queue(job); await Repo<EducationLearningReportJob>().InsertAsync(job, autoSave: true); await Repo<EducationLearningReport>().UpdateManyAsync([report], autoSave: true);
            return await CompleteAsync(uow, report.Id, job.Id);
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            var receipt = await ReceiptAsync(input.OperationId, input.OperationId, fp); if (receipt == null) throw;
            await recovery.CompleteAsync(); return receipt;
        }
    }

    [Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationLearningReportJobDto> RefreshAsync(Guid reportId, RefreshEducationLearningReportDto input)
    {
        await TeacherAsync(); EducationRules.RequireId(input.OperationId); var fp = Hash(new { Command = "report-refresh", reportId, input });
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var prior = await ReceiptAsync(reportId, input.OperationId, fp); if (prior != null) { await uow.CompleteAsync(); return prior; }
            await OwnAsync(reportId, write: true);
            using var tracking = Repo<EducationLearningReport>().EnableTracking(); var report = await Repo<EducationLearningReport>().GetAsync(reportId);
            Stamp(report, input.ConcurrencyStamp, input.ExpectedCurrentJobId);
            var scope = await LazyServiceProvider.LazyGetRequiredService<EducationCurriculumAccessGuard>().ResolveAsync(input.Scope);
            var source = await Reader.CaptureAsync(scope, report.WindowStartsAtUtc, report.WindowEndsAtUtc, EducationLearningReportJson.Read<Guid[]>(report.SelectedTaskIdsJson, 32000));
            var job = new EducationLearningReportJob(input.OperationId, report, source, fp, input.ConcurrencyStamp, DateTime.UtcNow);
            report.Queue(job); await Repo<EducationLearningReportJob>().InsertAsync(job, autoSave: true); await Repo<EducationLearningReport>().UpdateManyAsync([report], autoSave: true);
            return await CompleteAsync(uow, report.Id, job.Id);
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false); var receipt = await ReceiptAsync(reportId, input.OperationId, fp); if (receipt == null) throw;
            await recovery.CompleteAsync(); return receipt;
        }
    }

    [Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationLearningReportJobDto> RecoverAsync(Guid reportId, RecoverEducationLearningReportDto input)
    {
        await TeacherAsync(); EducationRules.RequireId(input.OperationId); var fp = Hash(new { Command = "report-recover", reportId, input });
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var prior = await ReceiptAsync(reportId, input.OperationId, fp); if (prior != null) { await uow.CompleteAsync(); return prior; }
            await OwnAsync(reportId, write: true); using var tracking = Repo<EducationLearningReport>().EnableTracking();
            var report = await Repo<EducationLearningReport>().GetAsync(reportId); Stamp(report, input.ConcurrencyStamp, input.ExpectedCurrentJobId);
            var previous = await RefAsync<EducationLearningReportJob>(input.ExpectedCurrentJobId) ?? throw Changed();
            if (previous.CapacityExceeded) throw new BusinessException(EducationLearningReportErrors.Capacity);
            if (previous.ReportId != reportId || previous.OwnerUserId != CurrentUser.Id || previous.TenantId != CurrentTenant.Id ||
                !(previous.Status == EducationLearningReportJobStatus.Failed || previous.Status == EducationLearningReportJobStatus.Running && previous.LeaseExpiresAtUtc <= DateTime.UtcNow)) throw Changed();
            var source = previous.Source();
            var current = await Reader.CaptureAsync(source.Curriculum, report.WindowStartsAtUtc, report.WindowEndsAtUtc, EducationLearningReportJson.Read<Guid[]>(report.SelectedTaskIdsJson, 32000), includeScores: false);
            if (current.Fingerprint != source.Fingerprint) throw Changed();
            var job = new EducationLearningReportJob(input.OperationId, report, source, fp, input.ConcurrencyStamp, DateTime.UtcNow, previous.Id);
            report.Queue(job); await Repo<EducationLearningReportJob>().InsertAsync(job, autoSave: true); await Repo<EducationLearningReport>().UpdateManyAsync([report], autoSave: true);
            return await CompleteAsync(uow, reportId, job.Id);
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false); var receipt = await ReceiptAsync(reportId, input.OperationId, fp); if (receipt == null) throw;
            await recovery.CompleteAsync(); return receipt;
        }
    }

    [Authorize(EducationPermissions.AnalysisManage)]
    public virtual Task<EducationLearningReportJobDto?> GetOperationAsync(Guid reportId, Guid operationId) => ReceiptAsync(reportId, operationId);

    [Authorize(EducationPermissions.AnalysisManage)]
    public virtual async Task<PagedResultDto<EducationLearningReportListDto>> GetListAsync(Guid teachingAssignmentId, PagedResultRequestDto input, Guid? schoolId = null)
    {
        await TeacherAsync(); var take = Page(input); var teaching = await Access.EnsureTeachingAssignmentAsync(teachingAssignmentId);
        if (schoolId.HasValue && teaching.SchoolId != schoolId.Value) throw new AbpAuthorizationException();
        using var tracking = Repo<EducationLearningReport>().DisableTracking();
        var query = (await Repo<EducationLearningReport>().GetQueryableAsync()).Where(x => x.TeachingAssignmentId == teachingAssignmentId && x.OwnerUserId == CurrentUser.Id && x.TenantId == CurrentTenant.Id);
        return new(await AsyncExecuter.CountAsync(query), await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.CreationTime).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take)
            .Select(x => new EducationLearningReportListDto { Id = x.Id, SchoolId = x.SchoolId, TeachingAssignmentId = x.TeachingAssignmentId, TextbookVersionId = x.TextbookVersionId, WindowStartsAtUtc = x.WindowStartsAtUtc, WindowEndsAtUtc = x.WindowEndsAtUtc, Generation = x.Generation })));
    }

    private sealed record CurrentReport(EducationLearningReport Root, JobMetadata Job, EducationLearningReportSnapshot? Snapshot, bool SourceCurrent, bool ScopeChanged);
    private async Task<CurrentReport> CurrentAsync(Guid reportId, Guid? schoolId = null, Guid? teachingAssignmentId = null)
    {
        var report = await OwnAsync(reportId, write: true);
        if (schoolId.HasValue && report.SchoolId != schoolId.Value || teachingAssignmentId.HasValue && report.TeachingAssignmentId != teachingAssignmentId.Value) throw new AbpAuthorizationException();
        var job = report.CurrentJobId is Guid jobId ? await MetadataAsync(reportId, jobId) ?? throw Changed() : throw Changed();
        var scope = EducationLearningReportJson.Read<EducationCurriculumSnapshot>(job.CurriculumJson, EducationLearningReportJson.MaxSourceBytes);
        var snapshot = report.CurrentSnapshotId is Guid snapshotId ? await RefAsync<EducationLearningReportSnapshot>(snapshotId) ?? throw Changed() : null;
        if (snapshot != null && (snapshot.ReportId != reportId || snapshot.OwnerUserId != CurrentUser.Id || snapshot.TenantId != CurrentTenant.Id)) throw new AbpAuthorizationException();
        try
        {
            var current = await Reader.CaptureAsync(scope, report.WindowStartsAtUtc, report.WindowEndsAtUtc, EducationLearningReportJson.Read<Guid[]>(report.SelectedTaskIdsJson, 32000), includeScores: false);
            return new(report, job, snapshot, snapshot != null && snapshot.SourceFingerprint == current.Fingerprint, false);
        }
        catch (BusinessException error) when (error.Code is EducationLearningReportErrors.SourceChanged or EducationLearningErrorCodes.InvalidRule)
        { return new(report, job, snapshot, false, true); }
    }

    [Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationLearningReportDto> GetAsync(Guid id) => GetCoreAsync(id, null, null);

    [Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationLearningReportDto> GetForSchoolAsync(Guid reportId, Guid schoolId, Guid teachingAssignmentId)
        => GetCoreAsync(reportId, EducationRules.RequireId(schoolId), EducationRules.RequireId(teachingAssignmentId));

    private async Task<EducationLearningReportDto> GetCoreAsync(Guid id, Guid? schoolId, Guid? teachingAssignmentId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var current = await CurrentAsync(id, schoolId, teachingAssignmentId);
        var root = current.Root; var scope = EducationLearningReportJson.Read<EducationCurriculumSnapshot>(current.Job.CurriculumJson, EducationLearningReportJson.MaxSourceBytes);
        var summary = current.SourceCurrent ? current.Snapshot!.Summary() : null;
        var rule = summary?.RuleRevisionId is Guid ruleId ? (await RefAsync<EducationMasteryRuleRevision>(ruleId))?.CalculationRule() : null;
        var result = new EducationLearningReportDto { Id = root.Id, SchoolId = root.SchoolId, TeachingAssignmentId = root.TeachingAssignmentId, TextbookVersionId = root.TextbookVersionId, WindowStartsAtUtc = root.WindowStartsAtUtc, WindowEndsAtUtc = root.WindowEndsAtUtc,
            Generation = root.Generation, ConcurrencyStamp = root.ConcurrencyStamp, CurrentJob = current.Job.Dto, CurrentSnapshotId = current.SourceCurrent ? current.Snapshot?.Id : null,
            SnapshotGeneration = current.SourceCurrent ? current.Snapshot?.Generation : null, SourceIsCurrent = current.SourceCurrent, ScopeNeedsUpdate = current.ScopeChanged,
            CalculatedAtUtc = current.SourceCurrent ? current.Snapshot?.CalculatedAtUtc : null, Summary = summary == null ? null : summary with { Students = [] }, Rule = rule,
            Coverage = summary == null ? null : new() { StudentCount = summary.Students.Count, ScoredStudentCount = summary.Students.Count(x => x.ScoredCount > 0),
                AssignedCount = summary.Students.Sum(x => x.AssignedCount), ScoredCount = summary.Students.Sum(x => x.ScoredCount), PendingGradingCount = summary.Students.Sum(x => x.PendingGradingCount),
                AbsentCount = summary.Students.Sum(x => x.AbsentCount), NotStartedCount = summary.Students.Sum(x => x.NotStartedCount), InProgressCount = summary.Students.Sum(x => x.InProgressCount),
                AwaitingSealCount = summary.Students.Sum(x => x.AwaitingSealCount), OutsideWindowCount = summary.Students.Sum(x => x.OutsideWindowCount) },
            Scope = new() { TeacherTextbookSelectionId = scope.TeacherTextbookSelectionId, TextbookKnowledgePointMappingIds = scope.Mappings.Select(x => x.MappingId).ToList() } };
        await uow.CompleteAsync(); return result;
    }

    private static EducationLearningReportSnapshot Expected(CurrentReport current, Guid snapshotId)
    { if (!current.SourceCurrent || current.Snapshot?.Id != snapshotId) throw Changed(); return current.Snapshot; }

    [Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationLearningReportStudentSummary>> GetStudentsAsync(Guid reportId, Guid snapshotId, PagedResultRequestDto input)
    {
        var take = Page(input); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var snapshot = Expected(await CurrentAsync(reportId), snapshotId); var students = snapshot.Summary().Students;
        var result = new PagedResultDto<EducationLearningReportStudentSummary>(students.Count, students.Skip(input.SkipCount).Take(take).ToList()); await uow.CompleteAsync(); return result;
    }
    private async Task<EducationLearningReportStudentSnapshot> StudentRowAsync(Guid reportId, Guid snapshotId, Guid studentId)
    {
        using var tracking = Repo<EducationLearningReportStudentSnapshot>().DisableTracking();
        return await Repo<EducationLearningReportStudentSnapshot>().FindAsync(x => x.ReportId == reportId && x.SnapshotId == snapshotId && x.StudentUserId == studentId &&
            x.OwnerUserId == CurrentUser.Id && x.TenantId == CurrentTenant.Id) ?? throw new AbpAuthorizationException();
    }
    [Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationLearningReportStudentResult> GetStudentAsync(Guid reportId, Guid snapshotId, Guid studentUserId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); Expected(await CurrentAsync(reportId), snapshotId);
        var result = (await StudentRowAsync(reportId, snapshotId, studentUserId)).Result(); await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationLearningReportBasisDto>> GetPointEvidenceAsync(Guid reportId, Guid snapshotId, Guid studentUserId, Guid pointRevisionId, PagedResultRequestDto input, Guid? taskId = null)
    {
        var take = Page(input); using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var current = await CurrentAsync(reportId); var snapshot = Expected(current, snapshotId);
        var student = (await StudentRowAsync(reportId, snapshotId, studentUserId)).Result();
        if (!student.Analysis.Points.Any(x => x.Point.KnowledgePointRevisionId == pointRevisionId)) throw new AbpAuthorizationException();
        var job = await RefAsync<EducationLearningReportJob>(snapshot.JobId) ?? throw Changed();
        var source = job.Source().Students.Single(x => x.Student.StudentUserId == studentUserId);
        if (taskId.HasValue && !student.Assessments.Any(x => x.TaskId == taskId)) throw new AbpAuthorizationException();
        var rows = source.Evidence.Where(x => (!taskId.HasValue || x.TaskId == taskId) && x.Points.Any(p => p.KnowledgePointRevisionId == pointRevisionId)).OrderBy(x => x.EffectiveAtUtc).ThenBy(x => x.GradeRevisionId).ToList();
        var result = new PagedResultDto<EducationLearningReportBasisDto>(rows.Count, rows.Skip(input.SkipCount).Take(take).Select(x => new EducationLearningReportBasisDto
        { QuestionGradeId = x.QuestionGradeId, GradeRevisionId = x.GradeRevisionId, TaskId = x.TaskId, SubmissionId = x.SubmissionId, PaperQuestionId = x.PaperQuestionId,
            QualificationRevisionId = x.QualificationRevisionId, EffectiveAtUtc = x.EffectiveAtUtc, MaxScore = x.Points.Where(p => p.KnowledgePointRevisionId == pointRevisionId).Sum(p => p.MaxScore),
            ObtainedScore = x.Points.Where(p => p.KnowledgePointRevisionId == pointRevisionId).Sum(p => p.ObtainedScore), ExclusionReason = student.Analysis.Evidence.Single(d => d.GradeRevisionId == x.GradeRevisionId).ExclusionReason }).ToList());
        await uow.CompleteAsync(); return result;
    }
}
