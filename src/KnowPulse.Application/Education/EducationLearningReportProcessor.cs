using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public sealed record EducationLearningReportWorkItem(Guid Id, Guid? TenantId);

[RemoteService(false)]
public class EducationLearningReportProcessor : KnowPulseAppService
{
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private IUnitOfWorkManager Uow => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();
    private EducationLearningReportSourceReader Reader => LazyServiceProvider.LazyGetRequiredService<EducationLearningReportSourceReader>();
    private async Task<T?> RefAsync<T>(Guid id) where T : class, IEntity<Guid>
    { using var tracking = Repo<T>().DisableTracking(); return await Repo<T>().FindAsync(id); }

    public virtual async Task<List<EducationLearningReportWorkItem>> GetPendingAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        using var jobTracking = Repo<EducationLearningReportJob>().DisableTracking(); using var rootTracking = Repo<EducationLearningReport>().DisableTracking();
        var jobs = await Repo<EducationLearningReportJob>().GetQueryableAsync(); var roots = await Repo<EducationLearningReport>().GetQueryableAsync();
        return await AsyncExecuter.ToListAsync((from job in jobs join root in roots on job.Id equals root.CurrentJobId
            where job.ReportId == root.Id && job.TenantId == root.TenantId && job.OwnerUserId == root.OwnerUserId &&
                (job.Status == EducationLearningReportJobStatus.Queued || job.Status == EducationLearningReportJobStatus.Failed && !job.CapacityExceeded && job.AttemptCount < EducationLearningReportJob.AutoAttempts && job.NextAttemptAtUtc <= now ||
                 job.Status == EducationLearningReportJobStatus.Running && job.LeaseExpiresAtUtc <= now)
            orderby job.RequestedAtUtc, job.Id select new EducationLearningReportWorkItem(job.Id, job.TenantId)).Take(1), ct);
    }
    public virtual async Task<List<EducationLearningReportWorkItem>> GetSourceChecksAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        using var jobTracking = Repo<EducationLearningReportJob>().DisableTracking(); using var rootTracking = Repo<EducationLearningReport>().DisableTracking();
        var jobs = await Repo<EducationLearningReportJob>().GetQueryableAsync(); var roots = await Repo<EducationLearningReport>().GetQueryableAsync();
        return await AsyncExecuter.ToListAsync((from job in jobs join root in roots on job.Id equals root.CurrentJobId
            where job.ReportId == root.Id && job.TenantId == root.TenantId && job.OwnerUserId == root.OwnerUserId &&
                (job.Status == EducationLearningReportJobStatus.Completed || job.Status == EducationLearningReportJobStatus.SourceChanged ||
                 job.Status == EducationLearningReportJobStatus.Failed && (job.CapacityExceeded || job.AttemptCount >= EducationLearningReportJob.AutoAttempts)) &&
                job.NextSourceCheckAtUtc <= now
            orderby job.NextSourceCheckAtUtc, job.Id
            select new EducationLearningReportWorkItem(root.Id, root.TenantId)).Take(20), ct);
    }
    private static void Facts(EducationLearningReportJob job, EducationLearningReport root, Guid? tenant)
    {
        if (job.TenantId != tenant || root.TenantId != tenant || job.ReportId != root.Id || job.OwnerUserId != root.OwnerUserId)
            throw new BusinessException(EducationLearningReportErrors.Invalid);
    }

    [UnitOfWork(IsDisabled = true)]
    public virtual async Task ProcessAsync(Guid jobId, CancellationToken ct = default)
    {
        var runId = GuidGenerator.Create(); EducationLearningReportSource source;
        using (var claim = Uow.Begin(requiresNew: true, isTransactional: true))
        {
            using var tracking = Repo<EducationLearningReportJob>().EnableTracking();
            var job = await Repo<EducationLearningReportJob>().FindAsync(jobId, cancellationToken: ct); if (job == null) return;
            var root = await RefAsync<EducationLearningReport>(job.ReportId); if (root == null) return; Facts(job, root, CurrentTenant.Id);
            if (root.CurrentJobId != job.Id || job.Status is EducationLearningReportJobStatus.Completed or EducationLearningReportJobStatus.SourceChanged or EducationLearningReportJobStatus.Superseded ||
                job.Status == EducationLearningReportJobStatus.Running && job.LeaseExpiresAtUtc > DateTime.UtcNow ||
                job.Status == EducationLearningReportJobStatus.Failed && (job.AttemptCount >= EducationLearningReportJob.AutoAttempts || job.CapacityExceeded || job.NextAttemptAtUtc > DateTime.UtcNow)) return;
            if (job.Status == EducationLearningReportJobStatus.Running && job.AttemptCount >= EducationLearningReportJob.AutoAttempts)
            { job.Expire(DateTime.UtcNow); await Repo<EducationLearningReportJob>().UpdateManyAsync([job], autoSave: true, cancellationToken: ct); await claim.CompleteAsync(ct); return; }
            job.Claim(runId, DateTime.UtcNow); await Repo<EducationLearningReportJob>().UpdateManyAsync([job], autoSave: true, cancellationToken: ct);
            source = job.Source(); await claim.CompleteAsync(ct);
        }
        var stage = EducationLearningReportFailureStage.Source;
        try
        {
            await LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAccessGuard>().EnsureRecordedTeacherRelationAsync(source.Curriculum, source.AcademicTermId, source.SchoolClassId);
            var calculatedAt = DateTime.UtcNow; var results = new List<EducationLearningReportStudentResult>(); long bytes = 0;
            stage = EducationLearningReportFailureStage.Calculate;
            foreach (var student in source.Students)
            {
                ct.ThrowIfCancellationRequested();
                var result = EducationLearningReportCalculation.Student(source, student, calculatedAt);
                bytes += Encoding.UTF8.GetByteCount(EducationLearningReportJson.Write(result, EducationLearningReportJson.MaxStudentBytes));
                if (bytes > EducationLearningReportJson.MaxSnapshotBytes) throw new BusinessException(EducationLearningReportErrors.Capacity);
                results.Add(result);
                if (!await HeartbeatAsync(jobId, runId, results.Count, ct)) return;
            }
            var summary = EducationLearningReportCalculation.Summary(source, results, calculatedAt);
            stage = EducationLearningReportFailureStage.Source;
            using var finish = Uow.Begin(requiresNew: true, isTransactional: true);
            var baseline = await RefAsync<EducationLearningReportJob>(jobId) ?? throw new BusinessException(EducationLearningReportErrors.Invalid);
            var reportBaseline = await RefAsync<EducationLearningReport>(baseline.ReportId) ?? throw new BusinessException(EducationLearningReportErrors.Invalid);
            var current = await Reader.CaptureAsync(source.Curriculum, source.WindowStartsAtUtc, source.WindowEndsAtUtc,
                EducationLearningReportJson.Read<Guid[]>(reportBaseline.SelectedTaskIdsJson, 32000), includeScores: false, ct: ct);
            using var jobTracking = Repo<EducationLearningReportJob>().EnableTracking(); using var rootTracking = Repo<EducationLearningReport>().EnableTracking();
            var job = await Repo<EducationLearningReportJob>().GetAsync(jobId, cancellationToken: ct); var root = await Repo<EducationLearningReport>().GetAsync(job.ReportId, cancellationToken: ct);
            Facts(job, root, CurrentTenant.Id);
            if (job.RunId != runId || job.Status != EducationLearningReportJobStatus.Running || job.LeaseExpiresAtUtc <= DateTime.UtcNow) return;
            if (root.CurrentJobId != jobId || current.Fingerprint != source.Fingerprint)
            {
                job.Stop(runId, root.CurrentJobId == jobId, DateTime.UtcNow); await Repo<EducationLearningReportJob>().UpdateManyAsync([job], autoSave: true, cancellationToken: ct);
                await finish.CompleteAsync(ct); return;
            }
            stage = EducationLearningReportFailureStage.Persistence;
            var snapshot = new EducationLearningReportSnapshot(job, root, summary);
            await Repo<EducationLearningReportSnapshot>().InsertAsync(snapshot, autoSave: true, cancellationToken: ct);
            var students = results.Select(x => new EducationLearningReportStudentSnapshot(snapshot, x)).ToList();
            foreach (var batch in students.Chunk(100)) await Repo<EducationLearningReportStudentSnapshot>().InsertManyAsync(batch, autoSave: true, cancellationToken: ct);
            job.Complete(runId, snapshot.Id, DateTime.UtcNow); job.ScheduleSourceCheck(DateTime.UtcNow); root.Publish(job, snapshot);
            await Repo<EducationLearningReportJob>().UpdateManyAsync([job], autoSave: true, cancellationToken: ct); await Repo<EducationLearningReport>().UpdateManyAsync([root], autoSave: true, cancellationToken: ct);
            await finish.CompleteAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            using var failure = Uow.Begin(requiresNew: true, isTransactional: true); using var tracking = Repo<EducationLearningReportJob>().EnableTracking();
            var job = await Repo<EducationLearningReportJob>().FindAsync(jobId, cancellationToken: ct);
            if (job == null || job.TenantId != CurrentTenant.Id || job.RunId != runId || job.Status != EducationLearningReportJobStatus.Running || job.LeaseExpiresAtUtc <= DateTime.UtcNow) return;
            job.Fail(runId, stage, error is BusinessException business && business.Code == EducationLearningReportErrors.Capacity, DateTime.UtcNow);
            await Repo<EducationLearningReportJob>().UpdateManyAsync([job], autoSave: true, cancellationToken: ct); await failure.CompleteAsync(ct);
        }
    }

    private async Task<bool> HeartbeatAsync(Guid id, Guid runId, int completed, CancellationToken ct)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); using var tracking = Repo<EducationLearningReportJob>().EnableTracking();
        var job = await Repo<EducationLearningReportJob>().GetAsync(id, cancellationToken: ct); var root = await RefAsync<EducationLearningReport>(job.ReportId);
        if (root == null || job.RunId != runId || job.Status != EducationLearningReportJobStatus.Running || job.LeaseExpiresAtUtc <= DateTime.UtcNow) return false;
        Facts(job, root, CurrentTenant.Id);
        if (root.CurrentJobId != job.Id) job.Stop(runId, sourceChanged: false, DateTime.UtcNow); else job.Heartbeat(runId, completed, DateTime.UtcNow);
        await Repo<EducationLearningReportJob>().UpdateManyAsync([job], autoSave: true, cancellationToken: ct); await uow.CompleteAsync(ct); return root.CurrentJobId == job.Id;
    }

    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<bool> RefreshChangedAsync(Guid reportId, CancellationToken ct = default)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var baseline = await RefAsync<EducationLearningReport>(reportId); if (baseline?.CurrentJobId is not Guid jobId) return false;
        var previous = await RefAsync<EducationLearningReportJob>(jobId) ?? throw new BusinessException(EducationLearningReportErrors.Invalid); Facts(previous, baseline, CurrentTenant.Id);
        if (previous.Status is EducationLearningReportJobStatus.Queued or EducationLearningReportJobStatus.Running) return false;
        var scope = EducationLearningReportJson.Read<EducationCurriculumSnapshot>(previous.CurriculumJson, EducationLearningReportJson.MaxSourceBytes);
        try
        {
            var tasks = EducationLearningReportJson.Read<Guid[]>(baseline.SelectedTaskIdsJson, 32000);
            var current = await Reader.CaptureAsync(scope, baseline.WindowStartsAtUtc, baseline.WindowEndsAtUtc, tasks, includeScores: false, ct: ct);
            using var rootTracking = Repo<EducationLearningReport>().EnableTracking(); using var jobTracking = Repo<EducationLearningReportJob>().EnableTracking();
            var root = await Repo<EducationLearningReport>().GetAsync(reportId, cancellationToken: ct);
            if (root.CurrentJobId != jobId) return false;
            previous = await Repo<EducationLearningReportJob>().GetAsync(jobId, cancellationToken: ct);
            if (current.Fingerprint == previous.SourceFingerprint)
            { previous.ScheduleSourceCheck(DateTime.UtcNow); await Repo<EducationLearningReportJob>().UpdateManyAsync([previous], autoSave: true, cancellationToken: ct); await uow.CompleteAsync(ct); return false; }
            var source = await Reader.CaptureAsync(scope, baseline.WindowStartsAtUtc, baseline.WindowEndsAtUtc, tasks, ct: ct);
            var operationId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"education-report-recompute-v1|{root.Id:D}|{jobId:D}|{source.Fingerprint}")).AsSpan(0, 16));
            var fingerprint = EducationAssessmentGradeRevision.Hash($"recompute|{root.Id:D}|{jobId:D}|{source.Fingerprint}");
            var job = new EducationLearningReportJob(operationId, root, source, fingerprint, root.ConcurrencyStamp, DateTime.UtcNow);
            root.Queue(job); await Repo<EducationLearningReportJob>().InsertAsync(job, autoSave: true, cancellationToken: ct); await Repo<EducationLearningReport>().UpdateManyAsync([root], autoSave: true, cancellationToken: ct);
            await uow.CompleteAsync(ct); return true;
        }
        catch (Exception error) when (error is AbpAuthorizationException || error is BusinessException business && business.Code is EducationLearningReportErrors.SourceChanged or EducationLearningErrorCodes.InvalidRule or EducationLearningReportErrors.Capacity)
        {
            using var tracking = Repo<EducationLearningReportJob>().EnableTracking(); previous = await Repo<EducationLearningReportJob>().GetAsync(jobId, cancellationToken: ct);
            previous.ScheduleSourceCheck(DateTime.UtcNow); await Repo<EducationLearningReportJob>().UpdateManyAsync([previous], autoSave: true, cancellationToken: ct); await uow.CompleteAsync(ct); return false;
        }
    }
}
