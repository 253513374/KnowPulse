using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

/// <summary>One sealing path for explicit submission and server cutoff; never grants a reader permission.</summary>
[RemoteService(false)]
public class EducationAssessmentSubmissionProcessor : ITransientDependency
{
    private readonly IAbpLazyServiceProvider _services;
    private readonly IUnitOfWorkManager _uow;
    private readonly EducationAssessmentResponseOptions _options;
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => _services.LazyGetRequiredService<IRepository<T, Guid>>();
    private IAsyncQueryableExecuter Queries => _services.LazyGetRequiredService<IAsyncQueryableExecuter>();
    private Guid? TenantId => _services.LazyGetRequiredService<ICurrentTenant>().Id;

    public EducationAssessmentSubmissionProcessor(IAbpLazyServiceProvider services, IUnitOfWorkManager uow,
        IOptions<EducationAssessmentResponseOptions> options)
    {
        _services = services;
        _uow = uow;
        _options = options.Value;
        _options.EnsureValid();
    }

    public virtual async Task<List<EducationAssessmentAttempt>> GetDueAttemptsAsync(CancellationToken ct = default)
    {
        using var tracking = Repo<EducationAssessmentAttempt>().DisableTracking();
        var now = DateTime.UtcNow;
        return await Queries.ToListAsync((await Repo<EducationAssessmentAttempt>().GetQueryableAsync())
            .Where(x => x.SubmissionId == null && x.ExpiresAtUtc <= now).OrderBy(x => x.ExpiresAtUtc).ThenBy(x => x.Id)
            .Take(_options.DeadlineBatchSize), ct);
    }

    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentSubmission?> SealDueAsync(Guid attemptId, CancellationToken ct = default)
    {
        try
        {
            using var uow = _uow.Begin(requiresNew: true, isTransactional: true);
            var attempt = await ReferenceAsync<EducationAssessmentAttempt>(attemptId, ct);
            if (attempt == null || attempt.TenantId != TenantId || attempt.ExpiresAtUtc > DateTime.UtcNow) return null;
            var result = await SealAsync(attemptId, DeadlineOperation(attemptId), null, null, false, ct);
            await uow.CompleteAsync(ct);
            return result;
        }
        catch (Exception error) when (error is not BusinessException && error is not OperationCanceledException)
        {
            // A save/manual-submit CAS winner may already have sealed. Recover only a committed
            // matching fact; otherwise the next bounded poll retries without inventing a receipt.
            using var recovery = _uow.Begin(requiresNew: true, isTransactional: false);
            var attempt = await ReferenceAsync<EducationAssessmentAttempt>(attemptId, ct);
            if (attempt == null || attempt.TenantId != TenantId || !attempt.SubmissionId.HasValue) throw;
            var result = await ReferenceAsync<EducationAssessmentSubmission>(attempt.SubmissionId.Value, ct) ?? throw Invalid();
            EnsureSubmission(attempt, result);
            await recovery.CompleteAsync(ct);
            return result;
        }
    }

    // The caller owns this business UoW. Student authorization precedes a manual call;
    // server cutoff deliberately uses only persisted tenant and frozen recipient facts.
    public virtual async Task<EducationAssessmentSubmission> SealAsync(Guid attemptId, Guid operationId, string? requestFingerprint,
        int? expectedResponseSetVersion, bool confirmedSavedOnly, CancellationToken ct = default)
    {
        if (_uow.Current == null) throw new InvalidOperationException("Sealing requires a business unit of work.");
        using var tracking = Repo<EducationAssessmentAttempt>().EnableTracking();
        var attempt = await Repo<EducationAssessmentAttempt>().FindAsync(attemptId, cancellationToken: ct) ?? throw Invalid();
        if (attempt.TenantId != TenantId) throw Invalid();
        var existing = await ReferenceAsync<EducationAssessmentSubmission>(operationId, ct);
        if (existing != null)
        {
            EnsureSubmission(attempt, existing);
            if (requestFingerprint != null && existing.RequestFingerprint != requestFingerprint) throw Conflict();
            if (attempt.SubmissionId != existing.Id) throw Invalid();
            return existing;
        }
        if (attempt.SubmissionId.HasValue)
        {
            var winner = await ReferenceAsync<EducationAssessmentSubmission>(attempt.SubmissionId.Value, ct) ?? throw Invalid();
            EnsureSubmission(attempt, winner);
            return winner;
        }
        var now = DateTime.UtcNow;
        var deadline = now >= attempt.ExpiresAtUtc;
        if (!deadline)
        {
            if (!expectedResponseSetVersion.HasValue || requestFingerprint == null) throw Invalid();
            attempt.EnsureCanEdit(now, expectedResponseSetVersion.Value);
        }
        var recipient = await ReferenceAsync<EducationAssessmentRecipient>(attempt.RecipientId, ct);
        var revision = await ReferenceAsync<EducationAssessmentTaskRevision>(attempt.TaskRevisionId, ct);
        var task = await ReferenceAsync<EducationAssessmentTask>(attempt.TaskId, ct);
        var paper = await ReferenceAsync<ExamPaper>(attempt.ExamPaperId, ct);
        if (recipient == null || revision == null || task == null || paper == null ||
            recipient.TenantId != attempt.TenantId || recipient.TaskId != attempt.TaskId || recipient.TaskRevisionId != attempt.TaskRevisionId ||
            recipient.StudentUserId != attempt.StudentUserId || recipient.CurrentAttemptId != attempt.Id || recipient.StartedAttemptCount != attempt.AttemptNumber ||
            revision.TenantId != attempt.TenantId || revision.TaskId != task.Id || revision.ExamPaperId != paper.Id || !revision.PublishedAtUtc.HasValue ||
            task.TenantId != attempt.TenantId || task.PublishedRevisionId != revision.Id || task.OwnerUserId != revision.OwnerUserId ||
            paper.TenantId != attempt.TenantId) throw Invalid();
        using var questionsTracking = Repo<ExamPaperQuestion>().DisableTracking();
        using var responsesTracking = Repo<EducationAssessmentCurrentResponse>().DisableTracking();
        using var revisionsTracking = Repo<EducationAssessmentResponseRevision>().DisableTracking();
        var questions = await Repo<ExamPaperQuestion>().GetListAsync(x => x.PaperId == paper.Id && x.TenantId == attempt.TenantId, cancellationToken: ct);
        if (questions.Count != paper.QuestionCount || questions.Count == 0) throw Invalid();
        var responses = await Repo<EducationAssessmentCurrentResponse>().GetListAsync(x => x.AttemptId == attempt.Id && x.TenantId == attempt.TenantId, cancellationToken: ct);
        var ids = responses.Select(x => x.CurrentRevisionId).ToList();
        var values = await Repo<EducationAssessmentResponseRevision>().GetListAsync(x => ids.Contains(x.Id) && x.TenantId == attempt.TenantId, cancellationToken: ct);
        // All writers contest the attempt root. Acquire that barrier before choosing the
        // closing instant: reads or the CAS itself may have waited across the cutoff.
        // A changed response set fails CAS instead of sealing a mixed manifest.
        await Repo<EducationAssessmentAttempt>().UpdateManyAsync([attempt], autoSave: true, cancellationToken: ct);
        now = DateTime.UtcNow;
        deadline = now >= attempt.ExpiresAtUtc;
        if (!deadline) attempt.EnsureCanEdit(now, expectedResponseSetVersion!.Value);
        if (!deadline && !confirmedSavedOnly && questions.Any(question => HasUnansweredBlank(question, responses, values)))
            throw new BusinessException(EducationAssessmentErrorCodes.IncompleteSubmission);
        var id = deadline ? DeadlineOperation(attempt.Id) : operationId;
        var fingerprint = deadline ? DeadlineFingerprint(attempt) : requestFingerprint!;
        var submission = new EducationAssessmentSubmission(id, attempt, paper.SnapshotVersion,
            deadline ? EducationAssessmentSubmissionCause.Deadline : EducationAssessmentSubmissionCause.Manual,
            fingerprint, !deadline && confirmedSavedOnly, questions.OrderBy(x => x.DisplayOrder).Select(x => x.Id).ToList(), responses, values, now);
        await Repo<EducationAssessmentSubmission>().InsertAsync(submission, autoSave: true, cancellationToken: ct);
        attempt.Seal(submission);
        await Repo<EducationAssessmentAttempt>().UpdateManyAsync([attempt], autoSave: true, cancellationToken: ct);
        return submission;
    }

    private static bool HasUnansweredBlank(ExamPaperQuestion question, IReadOnlyCollection<EducationAssessmentCurrentResponse> current,
        IReadOnlyCollection<EducationAssessmentResponseRevision> revisions)
    {
        if (question.Type != QuestionType.FillBlank || question.ScoringDefinitionJson == null) return false;
        var pointer = current.FirstOrDefault(x => x.PaperQuestionId == question.Id);
        var revision = pointer == null ? null : revisions.FirstOrDefault(x => x.Id == pointer.CurrentRevisionId);
        var value = revision == null ? null : EducationAssessmentResponseValue.Parse(revision.ResponseJson);
        return QuestionScoringDefinition.Parse(question.ScoringDefinitionJson).Blanks.Any(blank =>
            string.IsNullOrWhiteSpace(value?.Blanks.FirstOrDefault(x => x.BlankId == blank.Id)?.Text));
    }

    private async Task<T?> ReferenceAsync<T>(Guid id, CancellationToken ct) where T : class, IEntity<Guid>
    {
        using var tracking = Repo<T>().DisableTracking();
        return await Repo<T>().FindAsync(id, cancellationToken: ct);
    }
    private static Guid DeadlineOperation(Guid id) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"education-cutoff:{id:N}")).AsSpan(0, 16));
    private static string DeadlineFingerprint(EducationAssessmentAttempt attempt) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes($"deadline:{attempt.Id:N}:{attempt.ExpiresAtUtc:O}:{attempt.ResponseSetVersion}")));
    private static void EnsureSubmission(EducationAssessmentAttempt attempt, EducationAssessmentSubmission submission)
    {
        if (submission.AttemptId != attempt.Id || submission.TenantId != attempt.TenantId || submission.StudentUserId != attempt.StudentUserId ||
            submission.ExamPaperId != attempt.ExamPaperId || submission.ResponseSetVersion != attempt.ResponseSetVersion)
            throw Invalid();
    }
    private static BusinessException Invalid() => new(EducationAssessmentErrorCodes.InvalidRevision);
    private static BusinessException Conflict() => new(EducationAssessmentErrorCodes.ConflictingOperation);
}
