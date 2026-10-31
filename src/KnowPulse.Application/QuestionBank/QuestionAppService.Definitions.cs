using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Permissions;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.QuestionBank;

public partial class QuestionAppService
{
    private static readonly JsonSerializerOptions DefinitionJsonOptions = CreateDefinitionJsonOptions();
    private IRepository<QuestionDefinitionRevision, Guid> DefinitionRevisions =>
        LazyServiceProvider.LazyGetRequiredService<IRepository<QuestionDefinitionRevision, Guid>>();

    public virtual async Task<QuestionDto> DecideReviewAsync(Guid id, DecideQuestionReviewDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Reviews);
        if (!Enum.IsDefined(input.Decision)) throw new BusinessException("KnowPulse:Education:InvalidReviewDecision");
        if (input.OperationId == Guid.Empty) throw new BusinessException(EducationErrorCodes.InvalidIdentifier);
        if (input.Decision is QuestionReviewDecision.ReturnForRevision or QuestionReviewDecision.Reject && string.IsNullOrWhiteSpace(input.Comment))
            throw new BusinessException(KnowPulseDomainErrorCodes.QuestionReviewCommentRequired);
        var requestHash = ReviewRequestHash(id, input);
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            if (input.OperationId.HasValue)
            {
                using var operationTracking = _reviewRepository.DisableTracking();
                var previous = await _reviewRepository.FindAsync(input.OperationId.Value);
                if (previous != null)
                {
                    await EnsureSameReviewOperationAsync(previous, id, requestHash);
                    var repeated = await CompleteQuestionAsync(uow, await _questionRepository.GetAsync(id));
                    repeated.ReviewOperation = MapReviewOperation(previous);
                    return repeated;
                }
            }
            var question = await GetAccessibleQuestionAsync(id);
            var structured = question.CurriculumSnapshotJson != null || question.ScoringDefinitionJson != null;
            if (structured && !input.OperationId.HasValue)
                throw new BusinessException(EducationErrorCodes.InvalidIdentifier);
            if ((structured || input.OperationId.HasValue) && string.IsNullOrWhiteSpace(input.ConcurrencyStamp))
                throw new BusinessException("KnowPulse:Education:ReviewConcurrencyRequired");
            if (!string.IsNullOrWhiteSpace(input.ConcurrencyStamp) && question.ConcurrencyStamp != input.ConcurrencyStamp)
                throw new AbpDbConcurrencyException();
            var fromStatus = question.Status;
            var now = DateTime.UtcNow;
            var operationId = input.OperationId ?? GuidGenerator.Create();
            QuestionReviewAction action;
            QuestionDefinitionRevision? revision = null;
            switch (input.Decision)
            {
                case QuestionReviewDecision.Approve:
                    if (structured && question.CurriculumSnapshotJson == null)
                        throw new BusinessException("KnowPulse:Education:CurriculumConfirmationRequired");
                    question.EnsureDefinitionIsValid(await GetOptionsAsync(id), await GetAnswersAsync(id));
                    await QuestionCurriculum.EnsureAsync(question, forUpdate: true);
                    await SourceAccess.EnsureQuestionAsync(question, requireUsableSources: true, forUpdate: true);
                    question.Approve(CurrentUser.Id, now);
                    if (structured || input.OperationId.HasValue || question.ApprovedDefinitionRevisionId.HasValue)
                    {
                        var definitionJson = await GetDefinitionJsonAsync(question);
                        var numbers = await AsyncExecuter.ToListAsync((await DefinitionRevisions.GetQueryableAsync())
                            .Where(x => x.QuestionId == id).Select(x => x.RevisionNumber));
                        revision = new QuestionDefinitionRevision(operationId, question.TenantId, id, question.PersonalOwnerUserId,
                            numbers.Count == 0 ? 1 : checked(numbers.Max() + 1), operationId,
                            CurrentUser.Id ?? throw new AbpAuthorizationException(), now, HashDefinition(definitionJson), definitionJson);
                    }
                    action = QuestionReviewAction.Approved;
                    break;
                case QuestionReviewDecision.ReturnForRevision:
                    question.ReturnForRevision(CurrentUser.Id, now);
                    action = QuestionReviewAction.ReturnedForRevision;
                    break;
                case QuestionReviewDecision.Reject:
                    question.Reject(CurrentUser.Id, now);
                    action = QuestionReviewAction.Rejected;
                    break;
                default:
                    throw new BusinessException("KnowPulse:Education:InvalidReviewDecision");
            }
            var review = new QuestionReview(operationId, question.TenantId, id, CurrentUser.Id, action, fromStatus,
                question.Status, TrimToNull(input.Comment), now);
            if (input.OperationId.HasValue)
                review.RecordOperation(requestHash, input.ConcurrencyStamp!, question.PersonalOwnerUserId, revision?.Id);
            await _reviewRepository.InsertAsync(review, autoSave: true);
            if (revision != null)
            {
                await DefinitionRevisions.InsertAsync(revision, autoSave: true);
                question.RecordApprovedDefinitionRevision(revision.Id);
            }
            // Force the original question token for every decision, including return/reject.
            await _questionRepository.UpdateManyAsync([question], autoSave: true);
            var result = await CompleteQuestionAsync(uow, question);
            result.ReviewOperation = MapReviewOperation(review);
            return result;
        }
        catch (Exception error) when (input.OperationId.HasValue && error is not BusinessException &&
            error is not AbpAuthorizationException && error is not OperationCanceledException)
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            using var operationTracking = _reviewRepository.DisableTracking();
            var winner = await _reviewRepository.FindAsync(input.OperationId.Value);
            if (winner == null) throw;
            await EnsureSameReviewOperationAsync(winner, id, requestHash);
            var result = await CompleteQuestionAsync(recovery, await _questionRepository.GetAsync(id));
            result.ReviewOperation = MapReviewOperation(winner);
            return result;
        }
    }

    public virtual async Task<QuestionReviewOperationDto?> GetReviewOperationAsync(Guid questionId, Guid operationId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        if (questionId == Guid.Empty || operationId == Guid.Empty) throw new BusinessException(EducationErrorCodes.InvalidIdentifier);
        using var tracking = _reviewRepository.DisableTracking();
        var operation = await _reviewRepository.FindAsync(operationId);
        if (operation == null)
        {
            await EnsureDefinitionHistoryAccessAsync(questionId, null, CurrentTenant.Id);
            return null;
        }
        if (operation.OperationRequestHash == null || operation.QuestionId != questionId) throw new AbpAuthorizationException();
        await EnsureDefinitionHistoryAccessAsync(questionId, operation.PersonalOwnerUserId, operation.TenantId);
        await EnsureCompletedReviewFactsAsync(operation);
        return MapReviewOperation(operation);
    }

    public virtual async Task<QuestionDefinitionRevisionDto> GetDefinitionRevisionAsync(Guid questionId, Guid revisionId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        using var tracking = DefinitionRevisions.DisableTracking();
        var revision = await DefinitionRevisions.FindAsync(revisionId);
        if (revision == null || revision.QuestionId != questionId) throw new AbpAuthorizationException();
        await EnsureDefinitionHistoryAccessAsync(questionId, revision.PersonalOwnerUserId, revision.TenantId);
        return MapDefinitionRevision(revision);
    }

    public virtual async Task<ListResultDto<QuestionDefinitionRevisionDto>> GetDefinitionRevisionsAsync(Guid questionId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        using var tracking = DefinitionRevisions.DisableTracking();
        var revisions = await AsyncExecuter.ToListAsync((await DefinitionRevisions.GetQueryableAsync())
            .Where(x => x.QuestionId == questionId && x.TenantId == CurrentTenant.Id).OrderByDescending(x => x.RevisionNumber));
        if (revisions.Count == 0) await EnsureDefinitionHistoryAccessAsync(questionId, null, CurrentTenant.Id);
        foreach (var revision in revisions)
            await EnsureDefinitionHistoryAccessAsync(questionId, revision.PersonalOwnerUserId, revision.TenantId);
        return new ListResultDto<QuestionDefinitionRevisionDto>(revisions.Select(MapDefinitionRevision).ToList());
    }

    // New use requires the current definition; history only checks the frozen owner and tenant.
    internal async Task<QuestionDefinitionRevision?> EnsureApprovedDefinitionAsync(Question question)
    {
        if (question.Status != QuestionStatus.Approved) throw new BusinessException("KnowPulse:Education:FormalDefinitionRequired");
        if (!question.ApprovedDefinitionRevisionId.HasValue)
        {
            if (question.CurriculumSnapshotJson == null && question.ScoringDefinitionJson == null) return null;
            throw new BusinessException("KnowPulse:Education:FormalDefinitionRequired");
        }
        using var tracking = DefinitionRevisions.DisableTracking();
        var revision = await DefinitionRevisions.FindAsync(question.ApprovedDefinitionRevisionId.Value);
        if (revision == null || revision.QuestionId != question.Id || revision.TenantId != CurrentTenant.Id ||
            revision.TenantId != question.TenantId || revision.PersonalOwnerUserId != question.PersonalOwnerUserId ||
            revision.DefinitionHash != HashDefinition(revision.DefinitionJson) ||
            revision.DefinitionHash != HashDefinition(await GetDefinitionJsonAsync(question)))
            throw new BusinessException("KnowPulse:Education:FormalDefinitionRequired");
        SourceAccess.EnsurePersonalOwner(revision.PersonalOwnerUserId, revision.TenantId);
        return revision;
    }

    private async Task EnsureSameReviewOperationAsync(QuestionReview operation, Guid questionId, string requestHash)
    {
        if (operation.QuestionId != questionId || operation.TenantId != CurrentTenant.Id || operation.ReviewerUserId != CurrentUser.Id)
            throw new AbpAuthorizationException();
        await EnsureDefinitionHistoryAccessAsync(questionId, operation.PersonalOwnerUserId, operation.TenantId);
        if (operation.OperationRequestHash != requestHash)
            throw new BusinessException("KnowPulse:Education:ReviewOperationConflict");
        await EnsureCompletedReviewFactsAsync(operation);
    }

    private async Task EnsureCompletedReviewFactsAsync(QuestionReview operation)
    {
        if (operation.Action != QuestionReviewAction.Approved) return;
        if (!operation.DefinitionRevisionId.HasValue) throw new BusinessException("KnowPulse:Education:FormalDefinitionRequired");
        using var tracking = DefinitionRevisions.DisableTracking();
        var revision = await DefinitionRevisions.FindAsync(operation.DefinitionRevisionId.Value);
        if (revision == null || revision.QuestionId != operation.QuestionId || revision.ReviewOperationId != operation.Id ||
            revision.TenantId != operation.TenantId || revision.PersonalOwnerUserId != operation.PersonalOwnerUserId ||
            revision.ReviewedByUserId != operation.ReviewerUserId || revision.DefinitionHash != HashDefinition(revision.DefinitionJson))
            throw new BusinessException("KnowPulse:Education:FormalDefinitionRequired");
    }

    private async Task EnsureDefinitionHistoryAccessAsync(Guid questionId, Guid? ownerId, Guid? tenantId)
    {
        SourceAccess.EnsurePersonalOwner(ownerId, tenantId);
        if (ownerId.HasValue) return;
        using var tracking = _questionRepository.DisableTracking();
        var question = await _questionRepository.FindAsync(questionId);
        if (question == null || question.TenantId != tenantId) throw new AbpAuthorizationException();
        await SourceAccess.EnsureQuestionAsync(question);
    }

    private async Task<string> GetDefinitionJsonAsync(Question question)
    {
        var options = await GetOptionsAsync(question.Id);
        var answers = await GetAnswersAsync(question.Id);
        var analyses = await GetAnalysesAsync(question.Id);
        var points = await GetKnowledgePointsAsync(question.Id);
        var sources = await GetQuestionSourcesAsync(question.Id);
        var snapshot = new QuestionDefinitionSnapshotDto
        {
            Type = question.Type, Difficulty = question.Difficulty, CreationSource = question.CreationSource,
            Title = question.Title, Stem = question.Stem, ApplicablePositionId = question.ApplicablePositionId,
            SecurityLevel = question.SecurityLevel, Tags = question.Tags, Analysis = analyses.FirstOrDefault()?.Content,
            Options = options.OrderBy(x => x.DisplayOrder).ThenBy(x => x.OptionKey, StringComparer.Ordinal).Select(x => new CreateUpdateQuestionOptionDto
                { OptionKey = x.OptionKey, Content = x.Content, DisplayOrder = x.DisplayOrder }).ToList(),
            Answers = answers.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Value, StringComparer.Ordinal).Select(x => new CreateUpdateQuestionAnswerDto
                { Value = x.Value, DisplayOrder = x.DisplayOrder }).ToList(),
            KnowledgePointNames = points.Select(x => x.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ThenBy(x => x, StringComparer.Ordinal).ToList(),
            Sources = sources.OrderBy(x => x.KnowledgeItemId).ThenBy(x => x.KnowledgeVersionId).ThenBy(x => x.ChunkId)
                .ThenBy(x => x.SchoolKnowledgeShareId).ThenBy(x => x.SourceTitle, StringComparer.Ordinal).ThenBy(x => x.SourceExcerpt, StringComparer.Ordinal)
                .ThenBy(x => x.ManualSourceDescription, StringComparer.Ordinal).ThenBy(x => x.SourceHash, StringComparer.Ordinal)
                .Select(x => new CreateUpdateQuestionSourceDto
                {
                    KnowledgeItemId = x.KnowledgeItemId, KnowledgeVersionId = x.KnowledgeVersionId, ChunkId = x.ChunkId,
                    SchoolKnowledgeShareId = x.SchoolKnowledgeShareId, SourceTitle = x.SourceTitle, SourceExcerpt = x.SourceExcerpt,
                    SourceHash = x.SourceHash, ManualSourceDescription = x.ManualSourceDescription,
                    CurriculumBindingIds = Deserialize<List<Guid>>(x.CurriculumBindingIdsJson).OrderBy(id => id).ToList(),
                    TextbookKnowledgePointMappingIds = Deserialize<List<Guid>>(x.TextbookKnowledgePointMappingIdsJson).OrderBy(id => id).ToList()
                }).ToList(),
            ScoringDefinition = question.ScoringDefinitionJson == null ? null : QuestionScoringDefinition.Parse(
                QuestionScoringDefinition.Parse(question.ScoringDefinitionJson).ToCanonicalJson()),
            Curriculum = question.CurriculumSnapshotJson == null ? null : Deserialize<EducationCurriculumSnapshot>(question.CurriculumSnapshotJson),
            CurriculumSuggestedMappingIds = Deserialize<List<Guid>>(question.CurriculumSuggestedMappingIdsJson).OrderBy(x => x).ToList(),
            CurriculumPointWeights = Deserialize<List<QuestionCurriculumPointWeight>>(question.CurriculumPointWeightsJson)
                .OrderBy(x => x.TextbookKnowledgePointMappingId).ToList(),
            CurriculumSources = Deserialize<List<EducationCurriculumSourceReference>>(question.CurriculumSourceReferencesJson)
                .OrderBy(x => x.KnowledgeItemId).ThenBy(x => x.KnowledgeVersionId).ThenBy(x => x.ChunkId).ThenBy(x => x.SchoolKnowledgeShareId)
                .Select(x => new EducationCurriculumSourceReference
                {
                    KnowledgeItemId = x.KnowledgeItemId, KnowledgeVersionId = x.KnowledgeVersionId, ChunkId = x.ChunkId,
                    SchoolKnowledgeShareId = x.SchoolKnowledgeShareId, CurriculumBindingIds = x.CurriculumBindingIds.OrderBy(id => id).ToList(),
                    TextbookKnowledgePointMappingIds = x.TextbookKnowledgePointMappingIds.OrderBy(id => id).ToList()
                }).ToList(),
            CurriculumScore = question.CurriculumScore, CurriculumConfirmed = question.CurriculumConfirmed,
            CurriculumConfirmedByUserId = question.CurriculumConfirmedByUserId, CurriculumConfirmedAtUtc = question.CurriculumConfirmedAtUtc
        };
        return JsonSerializer.Serialize(snapshot, DefinitionJsonOptions);
    }

    private static string ReviewRequestHash(Guid questionId, DecideQuestionReviewDto input) => HashDefinition(JsonSerializer.Serialize(
        new { QuestionId = questionId, input.Decision, Comment = TrimToNull(input.Comment), input.ConcurrencyStamp }, JsonOptions));

    private static string HashDefinition(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    private static QuestionReviewOperationDto MapReviewOperation(QuestionReview review) => new()
    {
        Id = review.Id, QuestionId = review.QuestionId, Action = review.Action, FromStatus = review.FromStatus, ToStatus = review.ToStatus,
        ReviewerUserId = review.ReviewerUserId, ReviewTime = review.ReviewTime, Comment = review.Comment,
        ExpectedConcurrencyStamp = review.ExpectedConcurrencyStamp, RequestHash = review.OperationRequestHash, DefinitionRevisionId = review.DefinitionRevisionId
    };

    private static QuestionDefinitionRevisionDto MapDefinitionRevision(QuestionDefinitionRevision revision) => new()
    {
        Id = revision.Id, CreationTime = revision.CreationTime, CreatorId = revision.CreatorId, QuestionId = revision.QuestionId,
        RevisionNumber = revision.RevisionNumber, ReviewOperationId = revision.ReviewOperationId, ReviewedByUserId = revision.ReviewedByUserId,
        ReviewedAtUtc = revision.ReviewedAtUtc, DefinitionHash = revision.DefinitionHash,
        Definition = JsonSerializer.Deserialize<QuestionDefinitionSnapshotDto>(revision.DefinitionJson, DefinitionJsonOptions)!
    };

    private static JsonSerializerOptions CreateDefinitionJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonOptions);
        options.Converters.Add(new DefinitionDecimalConverter());
        options.Converters.Add(new DefinitionDateTimeConverter());
        return options;
    }

    private sealed class DefinitionDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDecimal();
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.ToString("G29", CultureInfo.InvariantCulture));
    }

    private sealed class DefinitionDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDateTime();
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
