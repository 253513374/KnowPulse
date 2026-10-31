using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Exam;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

/// <summary>Derives immutable baseline grades from a sealed submission, without granting reading authority.</summary>
[RemoteService(false)]
public class EducationAssessmentGradingProcessor : ITransientDependency
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IAbpLazyServiceProvider _services;
    private readonly IUnitOfWorkManager _uow;
    private readonly EducationAssessmentResponseOptions _options;
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => _services.LazyGetRequiredService<IRepository<T, Guid>>();
    private IAsyncQueryableExecuter Queries => _services.LazyGetRequiredService<IAsyncQueryableExecuter>();
    private Guid? TenantId => _services.LazyGetRequiredService<ICurrentTenant>().Id;

    public EducationAssessmentGradingProcessor(IAbpLazyServiceProvider services, IUnitOfWorkManager uow, IOptions<EducationAssessmentResponseOptions> options)
    {
        _services = services;
        _uow = uow;
        _options = options.Value;
        _options.EnsureValid();
    }

    public virtual async Task<List<EducationAssessmentSubmission>> GetPendingSubmissionsAsync(CancellationToken ct = default)
    {
        using var submissionTracking = Repo<EducationAssessmentSubmission>().DisableTracking();
        using var paperTracking = Repo<ExamPaper>().DisableTracking();
        using var gradeTracking = Repo<EducationAssessmentQuestionGrade>().DisableTracking();
        var submissions = await Repo<EducationAssessmentSubmission>().GetQueryableAsync();
        var papers = await Repo<ExamPaper>().GetQueryableAsync();
        var grades = await Repo<EducationAssessmentQuestionGrade>().GetQueryableAsync();
        var pending = from submission in submissions join paper in papers on submission.ExamPaperId equals paper.Id
            where submission.TenantId == paper.TenantId && paper.QuestionCount > 0 &&
                grades.Count(x => x.SubmissionId == submission.Id && x.TenantId == submission.TenantId && x.CurrentRevisionId != null) < paper.QuestionCount
            orderby submission.SealedAtUtc, submission.Id
            select submission;
        return await Queries.ToListAsync(pending.Take(_options.DeadlineBatchSize), ct);
    }

    [UnitOfWork(IsDisabled = true)]
    public virtual async Task InitializeAsync(Guid submissionId, CancellationToken ct = default)
    {
        using var uow = _uow.Begin(requiresNew: true, isTransactional: true);
        await InitializeInCurrentUnitOfWorkAsync(submissionId, ct);
        await uow.CompleteAsync(ct);
    }

    // A teacher caller holds School -> TeacherAccount -> Task in the same UoW.
    // Background derivation uses the fixed tenant/publication facts and Task -> Grade;
    // neither path uses a current textbook selection or authorizes student data reads.
    public virtual async Task InitializeInCurrentUnitOfWorkAsync(Guid submissionId, CancellationToken ct = default)
    {
        if (_uow.Current == null) throw new InvalidOperationException("Grading requires a business unit of work.");
        var submission = await ReferenceAsync<EducationAssessmentSubmission>(submissionId, ct) ?? throw Invalid();
        var attempt = await ReferenceAsync<EducationAssessmentAttempt>(submission.AttemptId, ct) ?? throw Invalid();
        var revision = await ReferenceAsync<EducationAssessmentTaskRevision>(attempt.TaskRevisionId, ct) ?? throw Invalid();
        var recipient = await ReferenceAsync<EducationAssessmentRecipient>(attempt.RecipientId, ct) ?? throw Invalid();
        var paper = await ReferenceAsync<ExamPaper>(attempt.ExamPaperId, ct) ?? throw Invalid();
        using var taskTracking = Repo<EducationAssessmentTask>().EnableTracking();
        var task = await Repo<EducationAssessmentTask>().FindAsync(attempt.TaskId, cancellationToken: ct) ?? throw Invalid();
        if (submission.TenantId != TenantId || attempt.TenantId != TenantId || revision.TenantId != TenantId || recipient.TenantId != TenantId ||
            paper.TenantId != TenantId || task.TenantId != TenantId || attempt.SubmissionId != submission.Id ||
            submission.StudentUserId != attempt.StudentUserId || submission.ExamPaperId != attempt.ExamPaperId ||
            submission.ResponseSetVersion != attempt.ResponseSetVersion || submission.PaperSnapshotVersion != paper.SnapshotVersion ||
            revision.TaskId != task.Id || task.PublishedRevisionId != revision.Id || !revision.PublishedAtUtc.HasValue ||
            revision.OwnerUserId != task.OwnerUserId || revision.ExamPaperId != paper.Id || recipient.TaskId != task.Id ||
            recipient.TaskRevisionId != revision.Id || recipient.StudentUserId != attempt.StudentUserId || recipient.CurrentAttemptId != attempt.Id ||
            recipient.StartedAttemptCount != attempt.AttemptNumber) throw Invalid();
        await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true, cancellationToken: ct);
        var questions = await ReferencesAsync<ExamPaperQuestion>(x => x.PaperId == paper.Id && x.TenantId == TenantId, ct);
        var manifest = Read<List<EducationAssessmentSubmissionManifestEntry>>(submission.ManifestJson);
        if (questions.Count != paper.QuestionCount || questions.Count == 0 || manifest.Count != questions.Count ||
            manifest.Select(x => x.PaperQuestionId).Distinct().Count() != manifest.Count ||
            manifest.Any(x => !questions.Any(q => q.Id == x.PaperQuestionId) || x.ResponseRevisionId.HasValue != (x.ResponseHash != null))) throw Invalid();
        foreach (var question in questions.OrderBy(x => x.Id))
        {
            var entry = manifest.Single(x => x.PaperQuestionId == question.Id);
            var response = entry.ResponseRevisionId.HasValue ? await ReferenceAsync<EducationAssessmentResponseRevision>(entry.ResponseRevisionId.Value, ct) : null;
            if (entry.ResponseRevisionId.HasValue && (response == null || response.TenantId != TenantId || response.AttemptId != attempt.Id ||
                response.ExamPaperId != paper.Id || response.PaperQuestionId != question.Id || response.StudentUserId != attempt.StudentUserId ||
                response.PaperSnapshotVersion != paper.SnapshotVersion || response.ResultResponseSetVersion > submission.ResponseSetVersion ||
                response.ResponseHash != entry.ResponseHash || response.ResponseHash != Hash(response.ResponseJson))) throw Invalid();
            var existing = (await ReferencesAsync<EducationAssessmentQuestionGrade>(x => x.SubmissionId == submission.Id && x.PaperQuestionId == question.Id && x.TenantId == TenantId, ct)).SingleOrDefault();
            if (existing != null)
            {
                if (existing.TaskId != task.Id || existing.OwnerUserId != task.OwnerUserId || existing.AttemptId != attempt.Id ||
                    existing.StudentUserId != attempt.StudentUserId || !existing.CurrentRevisionId.HasValue) throw Invalid();
                continue; // Never replace a teacher's draft or confirmed revision during automatic recovery.
            }
            var calculated = EducationAssessmentScoring.Calculate(question, response == null ? null : EducationAssessmentResponseValue.Parse(response.ResponseJson));
            var grade = new EducationAssessmentQuestionGrade(Operation("grade", submission.Id, question.Id), revision, attempt, submission, question);
            await Repo<EducationAssessmentQuestionGrade>().InsertAsync(grade, autoSave: true, cancellationToken: ct);
            var kind = calculated.ObtainedScore.HasValue ? EducationAssessmentGradeRevisionKind.Confirmed : EducationAssessmentGradeRevisionKind.Draft;
            var fingerprint = Hash($"automatic-v1:{submission.Id:N}:{question.Id:N}:{response?.ResponseHash}:{Hash(question.ScoringDefinitionJson ?? "legacy-objective")}");
            var baseline = new EducationAssessmentGradeRevision(Operation("baseline", submission.Id, question.Id), grade, submission, response, question,
                kind, EducationAssessmentGradingMethod.Automatic, calculated.Parts.Select(x => new EducationAssessmentPartMark { PartId = x.PartId, ObtainedScore = x.ObtainedScore }).ToList(),
                null, null, null, grade.ConcurrencyStamp, fingerprint, DateTime.UtcNow,
                kind == EducationAssessmentGradeRevisionKind.Confirmed ? task.ConfirmedGradeSetVersion : null);
            grade.Advance(baseline);
            await Repo<EducationAssessmentGradeRevision>().InsertAsync(baseline, autoSave: true, cancellationToken: ct);
            await Repo<EducationAssessmentQuestionGrade>().UpdateManyAsync([grade], autoSave: true, cancellationToken: ct);
            if (kind == EducationAssessmentGradeRevisionKind.Confirmed)
            {
                task.RecordConfirmedGrade(grade, baseline);
                await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true, cancellationToken: ct);
            }
        }
    }

    private async Task<T?> ReferenceAsync<T>(Guid id, CancellationToken ct) where T : class, IEntity<Guid>
    { using var tracking = Repo<T>().DisableTracking(); return await Repo<T>().FindAsync(id, cancellationToken: ct); }
    private async Task<List<T>> ReferencesAsync<T>(System.Linq.Expressions.Expression<Func<T, bool>> predicate, CancellationToken ct) where T : class, IEntity<Guid>
    { using var tracking = Repo<T>().DisableTracking(); return await Repo<T>().GetListAsync(predicate, cancellationToken: ct); }
    private static T Read<T>(string json) where T : class
    { try { return JsonSerializer.Deserialize<T>(json, Json) ?? throw Invalid(); } catch (JsonException) { throw Invalid(); } }
    private static Guid Operation(string prefix, Guid submission, Guid question) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"education-{prefix}:{submission:N}:{question:N}")).AsSpan(0, 16));
    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static BusinessException Invalid() => new(EducationAssessmentErrorCodes.InvalidRevision);
}
