using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Organization;
using KnowPulse.Permissions;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using KnowPulse.Teaching;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.Exam;

[Authorize(KnowPulsePermissions.Exam.Default)]
public class ExamAppService : KnowPulseAppService, IExamAppService
{
    private const string ScoringVersion = "objective-v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRepository<ExamPlan, Guid> _planRepository;
    private readonly IRepository<ExamPaper, Guid> _paperRepository;
    private readonly IRepository<ExamPaperQuestion, Guid> _paperQuestionRepository;
    private readonly IRepository<ExamAssignment, Guid> _assignmentRepository;
    private readonly IExamAttemptRepository _attemptRepository;
    private readonly IRepository<ExamAnswer, Guid> _answerRepository;
    private readonly IRepository<ExamScore, Guid> _scoreRepository;
    private readonly IRepository<WrongQuestionRecord, Guid> _wrongQuestionRepository;
    private readonly IRepository<Question, Guid> _questionRepository;
    private readonly IRepository<QuestionOption, Guid> _questionOptionRepository;
    private readonly IRepository<QuestionAnswer, Guid> _questionAnswerRepository;
    private readonly IRepository<QuestionAnalysis, Guid> _questionAnalysisRepository;
    private readonly IRepository<QuestionKnowledgePoint, Guid> _questionKnowledgePointRepository;
    private readonly IRepository<QuestionSource, Guid> _questionSourceRepository;
    private readonly IRepository<TeachingRelationship, Guid> _teachingRelationships;
    private readonly IRepository<IdentityUser, Guid> _users;
    private readonly IRepository<UserOrganizationAssignment, Guid> _userOrganizationAssignmentRepository;
    private readonly KnowledgeSourceAccessGuard _sourceAccessGuard;
    private IUnitOfWorkManager Uow => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();

    public ExamAppService(
        IRepository<ExamPlan, Guid> planRepository,
        IRepository<ExamPaper, Guid> paperRepository,
        IRepository<ExamPaperQuestion, Guid> paperQuestionRepository,
        IRepository<ExamAssignment, Guid> assignmentRepository,
        IExamAttemptRepository attemptRepository,
        IRepository<ExamAnswer, Guid> answerRepository,
        IRepository<ExamScore, Guid> scoreRepository,
        IRepository<WrongQuestionRecord, Guid> wrongQuestionRepository,
        IRepository<Question, Guid> questionRepository,
        IRepository<QuestionOption, Guid> questionOptionRepository,
        IRepository<QuestionAnswer, Guid> questionAnswerRepository,
        IRepository<QuestionAnalysis, Guid> questionAnalysisRepository,
        IRepository<QuestionKnowledgePoint, Guid> questionKnowledgePointRepository,
        IRepository<QuestionSource, Guid> questionSourceRepository,
        IRepository<TeachingRelationship, Guid> teachingRelationships,
        IRepository<IdentityUser, Guid> users,
        IRepository<UserOrganizationAssignment, Guid> userOrganizationAssignmentRepository,
        KnowledgeSourceAccessGuard sourceAccessGuard)
    {
        _planRepository = planRepository;
        _paperRepository = paperRepository;
        _paperQuestionRepository = paperQuestionRepository;
        _assignmentRepository = assignmentRepository;
        _attemptRepository = attemptRepository;
        _answerRepository = answerRepository;
        _scoreRepository = scoreRepository;
        _wrongQuestionRepository = wrongQuestionRepository;
        _questionRepository = questionRepository;
        _questionOptionRepository = questionOptionRepository;
        _questionAnswerRepository = questionAnswerRepository;
        _questionAnalysisRepository = questionAnalysisRepository;
        _questionKnowledgePointRepository = questionKnowledgePointRepository;
        _questionSourceRepository = questionSourceRepository;
        _teachingRelationships = teachingRelationships;
        _users = users;
        _userOrganizationAssignmentRepository = userOrganizationAssignmentRepository;
        _sourceAccessGuard = sourceAccessGuard;
    }

    public virtual async Task<PagedResultDto<ExamPlanDto>> GetPlanListAsync(GetExamPlanListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);

        var queryable = await _planRepository.GetQueryableAsync();
        queryable = queryable.Where(x => x.OwnerId == CurrentUser.Id || (x.OwnerId == null && IsLegacyAdministrator));
        queryable = ApplyPlanFilters(queryable, input);

        var candidates = await AsyncExecuter.ToListAsync(queryable.OrderByDescending(x => x.CreationTime));
        var visiblePlans = new List<ExamPlan>();
        foreach (var plan in candidates)
        {
            if (await CanManagePaperAsync(plan.PaperId))
            {
                visiblePlans.Add(plan);
            }
        }

        var plans = visiblePlans.Skip(input.SkipCount).Take(input.MaxResultCount).ToList();

        return new PagedResultDto<ExamPlanDto>(visiblePlans.Count, await MapPlansAsync(plans));
    }

    public virtual async Task<ExamPlanDto> GetPlanAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);
        return await MapPlanAsync(await GetOwnedPlanAsync(id));
    }

    public virtual async Task<ExamPaperDto> PreviewPaperAsync(GenerateExamPaperDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);
        var build = await BuildPaperAsync(input);
        return await MapPaperAsync(build.Paper, build.Questions);
    }

    [UnitOfWork]
    public virtual async Task<ExamPlanDto> CreatePlanAsync(CreateExamPlanDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);

        var studentIds = await ValidateRecipientsAsync(input);
        if (input.Paper == null) throw new UserFriendlyException("请选择已确认的题目。");
        var build = await BuildPaperAsync(input.Paper, forUpdate: true);
        await ValidateRecipientsAsync(input);
        if (build.Paper.CurriculumSnapshotJson != null || build.Questions.Any(x => x.ScoringDefinitionJson != null || x.Type == QuestionType.Subjective))
            throw new BusinessException("KnowPulse:Education:EducationTaskNotImplemented");
        var plan = new ExamPlan(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.Name.Trim(),
            build.Paper.Id,
            input.StartTime,
            input.EndTime,
            input.DurationMinutes,
            input.PassingScore,
            input.MaxAttempts,
            input.ResultVisibility,
            TrimToNull(input.Description),
            GetCurrentUserId());

        await _paperRepository.InsertAsync(build.Paper, autoSave: true);
        foreach (var question in build.Questions)
        {
            await _paperQuestionRepository.InsertAsync(question, autoSave: true);
        }
        await _planRepository.InsertAsync(plan, autoSave: true);

        foreach (var studentId in studentIds)
        {
            await _assignmentRepository.InsertAsync(
                new ExamAssignment(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    plan.Id,
                    ExamAssignmentScopeType.User,
                    studentId),
                autoSave: true);
        }

        await uow.SaveChangesAsync();
        var result = await MapPlanAsync(plan);
        await uow.CompleteAsync();
        return result;
    }

    public virtual async Task<ExamPaperDto> SavePaperAsync(GenerateExamPaperDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);
        if (!input.OperationId.HasValue || input.OperationId == Guid.Empty)
            throw new BusinessException(EducationErrorCodes.InvalidIdentifier);
        var operationId = input.OperationId.Value;
        var requestHash = GetSaveRequestHash(input);
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var previous = await _paperRepository.FindAsync(operationId);
            if (previous != null)
            {
                EnsureSameSaveOperation(previous, requestHash);
                var saved = await MapPaperAsync(previous);
                await uow.CompleteAsync();
                return saved;
            }
            var build = await BuildPaperAsync(input, forUpdate: true, operationId: operationId);
            build.Paper.RecordSaveOperation(requestHash);
            await _paperRepository.InsertAsync(build.Paper, autoSave: true);
            await _paperQuestionRepository.InsertManyAsync(build.Questions, autoSave: true);
            await uow.SaveChangesAsync();
            var result = await MapPaperAsync(build.Paper, build.Questions);
            await uow.CompleteAsync();
            return result;
        }
        catch (Exception error) when (error is not AbpAuthorizationException && error is not BusinessException && error is not OperationCanceledException)
        {
            // A failed/unknown commit can be another request's completed operation. Recover only
            // the same owner's exact input and complete frozen rows; all other storage errors remain errors.
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            var winner = await _paperRepository.FindAsync(operationId);
            if (winner == null) throw;
            EnsureSameSaveOperation(winner, requestHash);
            var result = await MapPaperAsync(winner);
            await recovery.CompleteAsync();
            return result;
        }
    }

    private static string GetSaveRequestHash(GenerateExamPaperDto input)
    {
        var normalized = new
        {
            Title = input.Title.Trim(), input.BuildMode, input.ApplicablePositionId, input.Randomize,
            FixedQuestionIds = input.FixedQuestionIds.Distinct().ToArray(),
            FixedQuestions = input.FixedQuestions.Select(x => new { x.QuestionId, x.Score, x.MultipleChoiceScoringMode }).ToArray(),
            Rules = input.Rules.Select(x => new { x.Type, x.Difficulty, KnowledgePoint = TrimToNull(x.KnowledgePoint), x.Count,
                ScorePerQuestion = x.ScorePerQuestion.ToString("G29", CultureInfo.InvariantCulture), x.MultipleChoiceScoringMode }).ToArray(),
            Curriculum = input.CurriculumScope == null ? null : new
            {
                input.CurriculumScope.TeacherTextbookSelectionId,
                MappingIds = input.CurriculumScope.TextbookKnowledgePointMappingIds.OrderBy(x => x).ToArray()
            }
        };
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized, JsonOptions))));
    }

    private void EnsureSameSaveOperation(ExamPaper paper, string requestHash)
    {
        if (paper.TenantId != CurrentTenant.Id || paper.PersonalOwnerUserId != CurrentUser.Id || !CurrentUser.IsAuthenticated)
            throw new AbpAuthorizationException();
        if (paper.SaveOperationRequestHash != requestHash)
            throw new BusinessException("KnowPulse:Education:PaperSaveOperationConflict");
    }

    public virtual async Task<ExamPaperDto> GetSavedPaperAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);
        return await MapPaperAsync(await _paperRepository.GetAsync(id));
    }

    public virtual async Task<PagedResultDto<ExamPlanDto>> GetMyPlanListAsync(GetExamPlanListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Participate);
        var userId = GetCurrentUserId();
        var assignments = await _assignmentRepository.GetQueryableAsync();
        var query = ApplyPlanFilters(await _planRepository.GetQueryableAsync(), input)
            .Where(x => x.Status == ExamPlanStatus.Published &&
                (x.OwnerId != null || IsLegacyAdministrator) && assignments.Any(a => a.PlanId == x.Id &&
                    a.IsActive && a.ScopeType == ExamAssignmentScopeType.User && a.TargetId == userId));
        var count = await AsyncExecuter.CountAsync(query);
        var plans = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.CreationTime)
            .Skip(input.SkipCount).Take(input.MaxResultCount));
        var result = new List<ExamPlanDto>();
        foreach (var plan in plans) result.Add(await MapStudentPlanAsync(plan));
        return new PagedResultDto<ExamPlanDto>(count, result);
    }

    public virtual async Task<ExamPlanDto> GetMyPlanAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Participate);
        return await MapStudentPlanAsync(await GetAssignedPlanAsync(id));
    }

    public virtual async Task<ExamPlanResultsDto> GetPlanResultsAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);
        var plan = await GetOwnedPlanAsync(id);
        var assignments = (await GetAssignmentsAsync(id)).Where(x => x.IsActive &&
            x.ScopeType == ExamAssignmentScopeType.User && x.TargetId.HasValue).ToList();
        var studentIds = assignments.Select(x => x.TargetId!.Value).Distinct().ToList();
        var relationships = await AsyncExecuter.ToListAsync((await _teachingRelationships.GetQueryableAsync())
            .Where(x => x.TeacherId == plan.OwnerId && studentIds.Contains(x.StudentId)));
        var attempts = await AsyncExecuter.ToListAsync((await _attemptRepository.GetQueryableAsync()).Where(x => x.PlanId == id));
        var scores = await AsyncExecuter.ToListAsync((await _scoreRepository.GetQueryableAsync()).Where(x => x.PlanId == id));
        return new ExamPlanResultsDto
        {
            Plan = await MapPlanAsync(plan),
            Recipients = studentIds.Select(studentId =>
            {
                var attempt = attempts.Where(x => x.UserId == studentId).OrderByDescending(x => x.AttemptNumber).FirstOrDefault();
                return new ExamRecipientResultDto
                {
                    StudentId = studentId,
                    DisplayName = relationships.FirstOrDefault(x => x.StudentId == studentId)?.DisplayName ?? "学生",
                    AttemptId = attempt?.Id, Status = attempt?.Status, SubmittedTime = attempt?.SubmittedTime,
                    ObtainedScore = scores.FirstOrDefault(x => x.AttemptId == attempt?.Id)?.ObtainedScore
                };
            }).ToList()
        };
    }

    public virtual async Task<ExamAttemptDto> GetStudentAttemptAsync(Guid attemptId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);
        var attempt = await _attemptRepository.GetAsync(attemptId);
        await GetOwnedPlanAsync(attempt.PlanId);
        return await MapAttemptAsync(attempt, forTeacher: true);
    }

    public virtual async Task<ExamAttemptDto> StartAttemptAsync(Guid planId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Participate);

        var userId = GetCurrentUserId();
        var now = DateTime.UtcNow;
        var plan = await GetAssignedPlanAsync(planId);
        var attempts = await GetAttemptsAsync(plan.Id, userId);
        foreach (var expired in attempts.Where(x => x.Status == ExamAttemptStatus.InProgress))
            if (expired.ExpireIfNeeded(now)) await _attemptRepository.UpdateAsync(expired, autoSave: true);
        var inProgress = attempts
            .Where(x => x.Status == ExamAttemptStatus.InProgress)
            .OrderByDescending(x => x.StartedTime)
            .FirstOrDefault();
        if (inProgress != null)
        {
            return await MapAttemptAsync(inProgress);
        }

        if (attempts.Count(x => x.Status != ExamAttemptStatus.Cancelled) >= plan.MaxAttempts)
        {
            var completed = attempts.Where(x => x.Status is ExamAttemptStatus.Submitted or ExamAttemptStatus.Scored)
                .OrderByDescending(x => x.AttemptNumber).FirstOrDefault();
            if (completed != null) return await MapAttemptAsync(completed);
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamAttemptLimitReached);
        }
        plan.EnsureCanStart(now);
        DateTime? expiresTime = plan.DurationMinutes.HasValue ? now.AddMinutes(plan.DurationMinutes.Value) : null;
        if (plan.EndTime.HasValue && (!expiresTime.HasValue || expiresTime > plan.EndTime)) expiresTime = plan.EndTime;

        var attempt = await _attemptRepository.InsertAsync(
            new ExamAttempt(
                GuidGenerator.Create(),
                CurrentTenant.Id,
                plan.Id,
                plan.PaperId,
                userId,
                attempts.Count + 1,
                now,
                expiresTime),
            autoSave: true);

        return await MapAttemptAsync(attempt);
    }

    public virtual async Task<ExamAttemptDto> GetAttemptAsync(Guid attemptId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Participate);
        var attempt = await _attemptRepository.GetAsync(attemptId);
        EnsureAttemptOwner(attempt);
        return await MapAttemptAsync(attempt);
    }

    public virtual async Task<ExamAttemptDto> SaveAnswersAsync(Guid attemptId, SaveExamAnswersDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Participate);

        var attempt = await _attemptRepository.GetForUpdateAsync(attemptId);
        EnsureAttemptOwner(attempt);

        var now = DateTime.UtcNow;
        attempt.EnsureCanSave(now);

        var paperQuestions = await GetPaperQuestionsAsync(attempt.PaperId);
        EnsureLegacyScoring(paperQuestions);
        var validQuestionIds = paperQuestions.Select(x => x.Id).ToHashSet();
        var existingAnswers = await GetAnswersAsync(attempt.Id);

        foreach (var item in input.Answers)
        {
            if (!validQuestionIds.Contains(item.PaperQuestionId))
            {
                throw new BusinessException(KnowPulseDomainErrorCodes.ExamPaperQuestionNotFound);
            }

            var answerJson = SerializeValues(NormalizeValues(item.Values));
            var answer = existingAnswers.FirstOrDefault(x => x.PaperQuestionId == item.PaperQuestionId);
            if (answer == null)
            {
                answer = new ExamAnswer(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    attempt.Id,
                    item.PaperQuestionId,
                    answerJson,
                    item.ClientVersion.GetValueOrDefault(1),
                    input.IdempotencyKey,
                    now);
                await _answerRepository.InsertAsync(answer, autoSave: true);
                existingAnswers.Add(answer);
            }
            else if (answer.Save(answerJson, item.ClientVersion, input.IdempotencyKey, now))
            {
                await _answerRepository.UpdateAsync(answer, autoSave: true);
            }
        }

        attempt.TouchSaved(now);
        await _attemptRepository.UpdateAsync(attempt, autoSave: true);
        return await MapAttemptAsync(attempt);
    }

    public virtual async Task<ExamAttemptDto> SubmitAttemptAsync(Guid attemptId, SubmitExamAttemptDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Participate);

        var attempt = await _attemptRepository.GetForUpdateAsync(attemptId);
        EnsureAttemptOwner(attempt);

        if (attempt.Status == ExamAttemptStatus.Scored) return await MapAttemptAsync(attempt);

        EnsureLegacyScoring(await GetPaperQuestionsAsync(attempt.PaperId));
        attempt.Submit(DateTime.UtcNow, input.IdempotencyKey);
        await _attemptRepository.UpdateAsync(attempt, autoSave: true);

        if (attempt.Status == ExamAttemptStatus.Submitted)
        {
            await GradeAttemptCoreAsync(attempt);
        }

        return await MapAttemptAsync(await _attemptRepository.GetAsync(attempt.Id));
    }

    public virtual async Task<ExamScoreDto> GradeAttemptAsync(Guid attemptId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);
        var attempt = await _attemptRepository.GetForUpdateAsync(attemptId);
        await GetOwnedPlanAsync(attempt.PlanId);
        if (!await CanManagePaperAsync(attempt.PaperId))
        {
            throw new AbpAuthorizationException("The current user cannot manage this exam paper.");
        }

        return await GradeAttemptCoreAsync(attempt);
    }

    public virtual async Task<PagedResultDto<ExamScoreDto>> GetScoreListAsync(GetExamScoreListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Results);

        var ownedPlans = (await _planRepository.GetQueryableAsync())
            .Where(x => x.OwnerId == CurrentUser.Id || (x.OwnerId == null && IsLegacyAdministrator));
        if (input.PlanId.HasValue) await GetOwnedPlanAsync(input.PlanId.Value);
        var queryable = (await _scoreRepository.GetQueryableAsync()).Where(x => ownedPlans.Any(p => p.Id == x.PlanId));
        if (input.PlanId.HasValue)
        {
            queryable = queryable.Where(x => x.PlanId == input.PlanId.Value);
        }

        if (input.UserId.HasValue)
        {
            queryable = queryable.Where(x => x.UserId == input.UserId.Value);
        }

        if (input.IsPassed.HasValue)
        {
            queryable = queryable.Where(x => x.IsPassed == input.IsPassed.Value);
        }

        var candidates = await AsyncExecuter.ToListAsync(queryable.OrderByDescending(x => x.ScoredTime));
        var paperAccess = new Dictionary<Guid, bool>();
        var visibleScores = new List<ExamScore>();
        foreach (var score in candidates)
        {
            if (!paperAccess.TryGetValue(score.PaperId, out var canManage))
            {
                canManage = await CanManagePaperAsync(score.PaperId);
                paperAccess[score.PaperId] = canManage;
            }

            if (canManage)
            {
                visibleScores.Add(score);
            }
        }

        var scores = visibleScores.Skip(input.SkipCount).Take(input.MaxResultCount).ToList();

        return new PagedResultDto<ExamScoreDto>(visibleScores.Count, await MapScoresAsync(scores));
    }

    private async Task<ExamScoreDto> GradeAttemptCoreAsync(ExamAttempt attempt)
    {
        if (attempt.Status is not ExamAttemptStatus.Submitted and not ExamAttemptStatus.Scored)
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamAttemptCannotScore);
        var plan = await _planRepository.GetAsync(attempt.PlanId);
        var paper = await _paperRepository.GetAsync(attempt.PaperId);
        var paperQuestions = await GetPaperQuestionsAsync(attempt.PaperId);
        EnsureLegacyScoring(paperQuestions);
        var answers = await GetAnswersAsync(attempt.Id);
        var now = DateTime.UtcNow;

        var scoreItems = paperQuestions
            .Select(question =>
            {
                var answer = answers.FirstOrDefault(x => x.PaperQuestionId == question.Id);
                var userValues = answer == null ? new List<string>() : DeserializeValues(answer.AnswerJson);
                var correctValues = DeserializeValues(question.CorrectAnswersJson);
                var obtained = ScoreQuestion(question, userValues, correctValues);
                return new QuestionScoreItem(question, userValues, correctValues, obtained);
            })
            .ToList();

        var obtainedScore = scoreItems.Sum(x => x.ObtainedScore);
        var existingScore = await GetScoreByAttemptAsync(attempt.Id);
        ExamScore score;
        if (existingScore == null)
        {
            score = await _scoreRepository.InsertAsync(
                new ExamScore(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    plan.Id,
                    paper.Id,
                    attempt.Id,
                    attempt.UserId,
                    paper.TotalScore,
                    obtainedScore,
                    plan.PassingScore ?? 0,
                    now,
                    ScoringVersion),
                autoSave: true);
        }
        else
        {
            existingScore.Refresh(paper.TotalScore, obtainedScore, plan.PassingScore ?? 0, now, ScoringVersion);
            score = await _scoreRepository.UpdateAsync(existingScore, autoSave: true);
            await DeleteWrongQuestionsAsync(score.Id);
        }

        foreach (var item in scoreItems.Where(x => x.ObtainedScore < x.Question.Score))
        {
            await _wrongQuestionRepository.InsertAsync(
                new WrongQuestionRecord(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    score.Id,
                    plan.Id,
                    paper.Id,
                    attempt.Id,
                    item.Question.Id,
                    item.Question.OriginalQuestionId,
                    attempt.UserId,
                    item.Question.Stem,
                    SerializeValues(item.UserValues),
                    SerializeValues(item.CorrectValues),
                    item.Question.Analysis,
                    item.Question.Score,
                    item.ObtainedScore),
                autoSave: true);
        }

        attempt.MarkScored(now);
        await _attemptRepository.UpdateAsync(attempt, autoSave: true);

        return await MapScoreAsync(score);
    }

    private async Task<PaperBuildResult> BuildPaperAsync(GenerateExamPaperDto input, bool forUpdate = false, Guid? operationId = null)
    {
        if (input.CurriculumScope != null)
            throw new BusinessException("KnowPulse:Education:SchoolWorkflowRetired");
        var selected = input.BuildMode == ExamPaperBuildMode.Fixed
            ? await SelectFixedQuestionsAsync(input)
            : await SelectByRulesAsync(input);

        if (selected.Count == 0)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamPaperQuestionInsufficient);
        }

        var paperId = operationId ?? GuidGenerator.Create();
        var totalScore = selected.Sum(x => x.Score);
        var questions = new List<ExamPaperQuestion>();
        var hasPersonalContent = false;
        for (var i = 0; i < selected.Count; i++)
        {
            var selectedQuestion = selected[i];
            if (selectedQuestion.Question.CurriculumSnapshotJson != null)
                throw new BusinessException("KnowPulse:Education:SchoolWorkflowRetired");
            var (snapshot, isPersonal) = await CreatePaperQuestionSnapshotAsync(
                paperId, selectedQuestion.Question, selectedQuestion.Score, selectedQuestion.MultipleChoiceScoringMode, i + 1, forUpdate);
            questions.Add(snapshot);
            hasPersonalContent |= isPersonal;
        }

        // Only a fully authorized new snapshot receives a durable owner. Historical reads do not reuse source access.
        var paper = new ExamPaper(
            paperId,
            CurrentTenant.Id,
            input.Title.Trim(),
            input.BuildMode,
            input.ApplicablePositionId,
            selected.Count,
            totalScore,
            hasPersonalContent || operationId.HasValue ? GetCurrentUserId() : null);

        return new PaperBuildResult(paper, questions);
    }

    private async Task<List<SelectedQuestion>> SelectFixedQuestionsAsync(GenerateExamPaperDto input)
    {
        var selected = input.FixedQuestions.Count > 0 ? input.FixedQuestions : input.FixedQuestionIds
            .Distinct().Select(id => new ExamFixedQuestionInputDto
            {
                QuestionId = id, Score = input.Rules.FirstOrDefault()?.ScorePerQuestion ?? 5m,
                MultipleChoiceScoringMode = input.Rules.FirstOrDefault()?.MultipleChoiceScoringMode ?? MultipleChoiceScoringMode.AllOrNothing
            }).ToList();
        var ids = selected.Select(x => x.QuestionId).ToList();
        if (ids.Count == 0 || ids.Count > 200 || ids.Distinct().Count() != ids.Count ||
            selected.Any(x => x.Score <= 0 || x.Score > 1000 || !Enum.IsDefined(x.MultipleChoiceScoringMode)))
            throw new UserFriendlyException("请选择有效且不重复的题目，并检查分值。");
        var query = FilterQuestionOwnership(await _questionRepository.GetQueryableAsync());
        var questions = await AsyncExecuter.ToListAsync(query.Where(x => ids.Contains(x.Id) && x.Status == QuestionStatus.Approved));
        if (questions.Count != ids.Count) throw new BusinessException(KnowPulseDomainErrorCodes.ExamPaperQuestionInsufficient);
        foreach (var question in questions)
            await _sourceAccessGuard.EnsureQuestionAsync(question, requireUsableSources: true);
        return selected.Select(item => new SelectedQuestion(questions.Single(x => x.Id == item.QuestionId),
            item.Score, item.MultipleChoiceScoringMode)).ToList();
    }

    private async Task<List<SelectedQuestion>> SelectByRulesAsync(GenerateExamPaperDto input)
    {
        if (input.Rules.Count == 0)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamPaperQuestionInsufficient);
        }

        var result = new List<SelectedQuestion>();
        var usedIds = new HashSet<Guid>();
        foreach (var rule in input.Rules)
        {
            var candidates = await GetCandidatesForRuleAsync(input, rule, usedIds);
            if (candidates.Count < rule.Count)
            {
                throw new BusinessException(KnowPulseDomainErrorCodes.ExamPaperQuestionInsufficient);
            }

            if (input.Randomize)
            {
                candidates = candidates.OrderBy(_ => Guid.NewGuid()).ToList();
            }

            foreach (var question in candidates.Take(rule.Count))
            {
                usedIds.Add(question.Id);
                result.Add(new SelectedQuestion(question, rule.ScorePerQuestion, rule.MultipleChoiceScoringMode));
            }
        }

        return result;
    }

    private async Task<List<Question>> GetCandidatesForRuleAsync(
        GenerateExamPaperDto input,
        ExamPaperRuleDto rule,
        HashSet<Guid> usedIds)
    {
        var queryable = FilterQuestionOwnership(await _questionRepository.GetQueryableAsync());
        queryable = queryable.Where(x => x.Status == QuestionStatus.Approved && !usedIds.Contains(x.Id) &&
            (!x.PersonalOwnerUserId.HasValue || x.PersonalOwnerUserId == CurrentUser.Id));

        if (rule.Type.HasValue)
        {
            queryable = queryable.Where(x => x.Type == rule.Type.Value);
        }

        if (rule.Difficulty.HasValue)
        {
            queryable = queryable.Where(x => x.Difficulty == rule.Difficulty.Value);
        }

        if (input.ApplicablePositionId.HasValue)
        {
            queryable = queryable.Where(x => x.ApplicablePositionId == null || x.ApplicablePositionId == input.ApplicablePositionId.Value);
        }

        var candidates = await AsyncExecuter.ToListAsync(queryable.OrderBy(x => x.CreationTime));
        if (!string.IsNullOrWhiteSpace(rule.KnowledgePoint))
        {
            var pointQueryable = await _questionKnowledgePointRepository.GetQueryableAsync();
            var keyword = rule.KnowledgePoint.Trim();
            var questionIds = await AsyncExecuter.ToListAsync(
                pointQueryable
                    .Where(x => x.Name.Contains(keyword))
                    .Select(x => x.QuestionId));
            var idSet = questionIds.ToHashSet();
            candidates = candidates.Where(x => idSet.Contains(x.Id)).ToList();
        }

        var usableCandidates = new List<Question>();
        foreach (var candidate in candidates)
        {
            if (candidate.CurriculumSnapshotJson != null) continue;
            try
            {
                await _sourceAccessGuard.EnsureQuestionAsync(candidate, requireUsableSources: true);
                usableCandidates.Add(candidate);
            }
            catch (Exception error) when (error is AbpAuthorizationException || error is BusinessException && candidate.CurriculumSnapshotJson != null)
            {
                // A rule can select other available questions without exposing inaccessible candidates.
            }
        }

        return usableCandidates;
    }

    private async Task<(ExamPaperQuestion Snapshot, bool IsPersonal)> CreatePaperQuestionSnapshotAsync(
        Guid paperId,
        Question question,
        decimal score,
        MultipleChoiceScoringMode scoringMode,
        int displayOrder, bool forUpdate = false)
    {
        // The preliminary rule selection stays read-only. Only selected questions contend with
        // content/source edits, in the same transaction that freezes the paper.
        if (question.CurriculumSnapshotJson != null) throw new BusinessException("KnowPulse:Education:SchoolWorkflowRetired");
        await _sourceAccessGuard.EnsureQuestionAsync(question, requireUsableSources: true, forUpdate: forUpdate);
        var options = await GetQuestionOptionsAsync(question.Id);
        var answers = await GetQuestionAnswersAsync(question.Id);
        var analyses = await GetQuestionAnalysesAsync(question.Id);
        var points = await GetQuestionKnowledgePointsAsync(question.Id);
        var sources = await GetQuestionSourcesAsync(question.Id);
        var isPersonal = question.PersonalOwnerUserId.HasValue;
        var sourceLabels = new List<string>();
        foreach (var source in sources)
        {
            // Older questions may not carry an owner yet; use the same validated sources included in this snapshot.
            var metadata = await _sourceAccessGuard.EnsureSourceMetadataAsync(
                source.KnowledgeItemId, source.KnowledgeVersionId, source.ChunkId, requireUsable: true, schoolKnowledgeShareId: source.SchoolKnowledgeShareId);
            if (metadata.IsShared) throw new BusinessException("KnowPulse:Education:SchoolWorkflowRetired");
            var item = metadata.Item;
            isPersonal |= item?.OwnerUserId.HasValue == true;
            var title = source.SourceTitle;
            var label = TrimToNull(title) ?? TrimToNull(source.ManualSourceDescription) ??
                TrimToNull(source.SourceHash) ?? source.KnowledgeItemId?.ToString() ??
                source.KnowledgeVersionId?.ToString() ?? source.ChunkId?.ToString();
            if (label != null) sourceLabels.Add(label);
        }

        var snapshot = new ExamPaperQuestion(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            paperId,
            question.Id,
            question.Type,
            question.Difficulty,
            question.CreationSource,
            question.Title,
            question.Stem,
            JsonSerializer.Serialize(options.Select(x => new ExamPaperQuestionOptionDto
            {
                OptionKey = x.OptionKey,
                Content = x.Content,
                DisplayOrder = x.DisplayOrder
            }).ToList(), JsonOptions),
            SerializeValues(answers.OrderBy(x => x.DisplayOrder).Select(x => x.Value)),
            analyses.FirstOrDefault()?.Content,
            points.Count == 0 ? null : string.Join(", ", points.Select(x => x.Name)),
            sourceLabels.Count == 0 ? null : string.Join("; ", sourceLabels),
            score,
            scoringMode,
            displayOrder);
        if (question.ScoringDefinitionJson != null || question.ApprovedDefinitionRevisionId.HasValue)
        {
            await LazyServiceProvider.LazyGetRequiredService<QuestionAppService>().EnsureApprovedDefinitionAsync(question);
            snapshot.FreezeDefinition(question);
        }
        return (snapshot, isPersonal);
    }

    private async Task<bool> IsAssignedAsync(Guid planId, Guid userId)
    {
        return await _assignmentRepository.AnyAsync(x => x.PlanId == planId && x.IsActive &&
            x.ScopeType == ExamAssignmentScopeType.User && x.TargetId == userId);
    }

    private async Task<List<ExamAttempt>> GetAttemptsAsync(Guid planId, Guid userId)
    {
        var queryable = await _attemptRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.PlanId == planId && x.UserId == userId));
    }

    private async Task<ExamScore?> GetScoreByAttemptAsync(Guid attemptId)
    {
        var queryable = await _scoreRepository.GetQueryableAsync();
        return await AsyncExecuter.FirstOrDefaultAsync(queryable.Where(x => x.AttemptId == attemptId));
    }

    private async Task DeleteWrongQuestionsAsync(Guid scoreId)
    {
        var queryable = await _wrongQuestionRepository.GetQueryableAsync();
        var wrongs = await AsyncExecuter.ToListAsync(queryable.Where(x => x.ScoreId == scoreId));
        foreach (var wrong in wrongs)
        {
            await _wrongQuestionRepository.DeleteAsync(wrong, autoSave: true);
        }
    }

    private async Task<List<ExamPaperQuestion>> GetPaperQuestionsAsync(Guid paperId)
    {
        var queryable = await _paperQuestionRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.PaperId == paperId).OrderBy(x => x.DisplayOrder));
    }

    private async Task<List<ExamAssignment>> GetAssignmentsAsync(Guid planId)
    {
        var queryable = await _assignmentRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.PlanId == planId));
    }

    private async Task<List<ExamAnswer>> GetAnswersAsync(Guid attemptId)
    {
        var queryable = await _answerRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.AttemptId == attemptId));
    }

    private async Task<List<QuestionOption>> GetQuestionOptionsAsync(Guid questionId)
    {
        var queryable = await _questionOptionRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.QuestionId == questionId).OrderBy(x => x.DisplayOrder));
    }

    private async Task<List<QuestionAnswer>> GetQuestionAnswersAsync(Guid questionId)
    {
        var queryable = await _questionAnswerRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.QuestionId == questionId).OrderBy(x => x.DisplayOrder));
    }

    private async Task<List<QuestionAnalysis>> GetQuestionAnalysesAsync(Guid questionId)
    {
        var queryable = await _questionAnalysisRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.QuestionId == questionId));
    }

    private async Task<List<QuestionKnowledgePoint>> GetQuestionKnowledgePointsAsync(Guid questionId)
    {
        var queryable = await _questionKnowledgePointRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.QuestionId == questionId).OrderBy(x => x.Name));
    }

    private async Task<List<QuestionSource>> GetQuestionSourcesAsync(Guid questionId)
    {
        var queryable = await _questionSourceRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.QuestionId == questionId).OrderBy(x => x.CreationTime));
    }

    private async Task<List<WrongQuestionRecord>> GetWrongQuestionsAsync(Guid scoreId)
    {
        var queryable = await _wrongQuestionRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.ScoreId == scoreId).OrderBy(x => x.CreationTime));
    }

    private IQueryable<ExamPlan> ApplyPlanFilters(IQueryable<ExamPlan> queryable, GetExamPlanListInput input)
    {
        if (input.Status.HasValue)
        {
            queryable = queryable.Where(x => x.Status == input.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            var keyword = input.Keyword.Trim();
            queryable = queryable.Where(x => x.Name.Contains(keyword) || (x.Description != null && x.Description.Contains(keyword)));
        }

        return queryable;
    }

    private async Task<List<ExamPlanDto>> MapPlansAsync(IReadOnlyCollection<ExamPlan> plans, bool forParticipant = false)
    {
        var result = new List<ExamPlanDto>();
        foreach (var plan in plans)
        {
            result.Add(await MapPlanAsync(plan, forParticipant));
        }

        return result;
    }

    private async Task<ExamPlanDto> MapPlanAsync(ExamPlan plan, bool forParticipant = false)
    {
        var assignments = forParticipant ? new List<ExamAssignment>() : await GetAssignmentsAsync(plan.Id);
        var paper = await _paperRepository.GetAsync(plan.PaperId);
        return new ExamPlanDto
        {
            Id = plan.Id,
            OwnerId = plan.OwnerId,
            TeacherId = plan.OwnerId,
            TeacherName = await GetTeacherNameAsync(plan.OwnerId),
            CreationTime = plan.CreationTime,
            CreatorId = plan.CreatorId,
            LastModificationTime = plan.LastModificationTime,
            LastModifierId = plan.LastModifierId,
            IsDeleted = plan.IsDeleted,
            DeleterId = plan.DeleterId,
            DeletionTime = plan.DeletionTime,
            Name = plan.Name,
            Description = plan.Description,
            PaperId = plan.PaperId,
            StartTime = plan.StartTime,
            EndTime = plan.EndTime,
            DurationMinutes = plan.DurationMinutes,
            PassingScore = plan.PassingScore,
            MaxAttempts = plan.MaxAttempts,
            ResultVisibility = plan.ResultVisibility,
            Status = plan.Status,
            Paper = forParticipant
                ? await MapPaperAsync(paper, Array.Empty<ExamPaperQuestion>(), ExamPaperView.Participant)
                : await MapPaperAsync(paper),
            Assignments = assignments.Select(MapAssignment).ToList()
        };
    }

    private async Task<ExamPaperDto> MapPaperAsync(ExamPaper paper, ExamPaperView view = ExamPaperView.Management)
    {
        return await MapPaperAsync(paper, await GetPaperQuestionsAsync(paper.Id), view);
    }

    private async Task<ExamPaperDto> MapPaperAsync(
        ExamPaper paper,
        IReadOnlyCollection<ExamPaperQuestion> questions,
        ExamPaperView view = ExamPaperView.Management)
    {
        if (view == ExamPaperView.Management && !await CanManagePaperAsync(paper, questions))
        {
            throw new AbpAuthorizationException("The current user cannot read this exam paper or its knowledge sources.");
        }

        return new ExamPaperDto
        {
            Id = paper.Id,
            OperationId = view == ExamPaperView.Management && paper.SaveOperationRequestHash != null ? paper.Id : null,
            Curriculum = view == ExamPaperView.Management && paper.CurriculumSnapshotJson != null
                ? JsonSerializer.Deserialize<EducationCurriculumSnapshot>(paper.CurriculumSnapshotJson, JsonOptions) : null,
            CreationTime = paper.CreationTime,
            CreatorId = paper.CreatorId,
            LastModificationTime = paper.LastModificationTime,
            LastModifierId = paper.LastModifierId,
            IsDeleted = paper.IsDeleted,
            DeleterId = paper.DeleterId,
            DeletionTime = paper.DeletionTime,
            Title = paper.Title,
            BuildMode = paper.BuildMode,
            ApplicablePositionId = paper.ApplicablePositionId,
            QuestionCount = paper.QuestionCount,
            TotalScore = paper.TotalScore,
            SnapshotVersion = paper.SnapshotVersion,
            Questions = questions.OrderBy(x => x.DisplayOrder).Select(question => MapPaperQuestion(question, view)).ToList()
        };
    }

    private async Task<bool> CanManagePaperAsync(Guid paperId)
    {
        var paper = await _paperRepository.FindAsync(paperId);
        if (paper == null || paper.TenantId != CurrentTenant.Id)
        {
            return false;
        }

        var questions = await GetPaperQuestionsAsync(paper.Id);
        return await CanManagePaperAsync(paper, questions);
    }

    private async Task<bool> CanManagePaperAsync(ExamPaper paper, IReadOnlyCollection<ExamPaperQuestion> questions)
    {
        if (!CurrentUser.IsAuthenticated || paper.TenantId != CurrentTenant.Id || questions.Count != paper.QuestionCount)
        {
            return false;
        }

        if (paper.PersonalOwnerUserId.HasValue)
        {
            return paper.PersonalOwnerUserId != Guid.Empty && paper.PersonalOwnerUserId == CurrentUser.Id;
        }

        // Legacy papers have no durable ownership fact; missing or inaccessible sources must still fail closed.
        return await CanManageQuestionsAsync(questions);
    }

    private async Task<bool> CanManageQuestionsAsync(IReadOnlyCollection<ExamPaperQuestion> questions)
    {
        foreach (var questionId in questions.Select(x => x.OriginalQuestionId).Distinct())
        {
            if (!await _sourceAccessGuard.CanReadQuestionAsync(questionId))
            {
                return false;
            }
        }

        return true;
    }

    private static ExamPaperQuestionDto MapPaperQuestion(ExamPaperQuestion question, ExamPaperView view)
    {
        var definition = question.ScoringDefinitionJson == null ? null : QuestionScoringDefinition.Parse(question.ScoringDefinitionJson);
        return new ExamPaperQuestionDto
        {
            Id = question.Id,
            ScoringDefinition = view == ExamPaperView.Management ? definition : null,
            QuestionDefinitionRevisionId = view == ExamPaperView.Management ? question.QuestionDefinitionRevisionId : null,
            ResponseFields = definition?.Blanks.OrderBy(x => x.DisplayOrder).Select(x => new ExamResponseFieldDto
                { Id = x.Id, DisplayOrder = x.DisplayOrder, StemMarker = x.StemMarker }).ToList() ?? [],
            Curriculum = view == ExamPaperView.Management && question.CurriculumSnapshotJson != null
                ? JsonSerializer.Deserialize<EducationCurriculumSnapshot>(question.CurriculumSnapshotJson, JsonOptions) : null,
            CurriculumPointWeights = view == ExamPaperView.Management && question.CurriculumPointWeightsJson != null
                ? JsonSerializer.Deserialize<List<QuestionCurriculumPointWeight>>(question.CurriculumPointWeightsJson, JsonOptions) ?? [] : [],
            CurriculumScoringPoints = view == ExamPaperView.Management
                ? QuestionCurriculumAccessGuard.GetScoringPoints(question.CurriculumSnapshotJson, question.CurriculumPointWeightsJson) : [],
            CurriculumSources = view == ExamPaperView.Management && question.CurriculumSourceReferencesJson != null
                ? JsonSerializer.Deserialize<List<EducationCurriculumSourceReference>>(question.CurriculumSourceReferencesJson, JsonOptions) ?? [] : [],
            CurriculumConfirmedByUserId = view == ExamPaperView.Management ? question.CurriculumConfirmedByUserId : null,
            CurriculumConfirmedAtUtc = view == ExamPaperView.Management ? question.CurriculumConfirmedAtUtc : null,
            PaperId = question.PaperId,
            OriginalQuestionId = view != ExamPaperView.Management && question.CurriculumSnapshotJson != null ? Guid.Empty : question.OriginalQuestionId,
            Type = question.Type,
            Difficulty = question.Difficulty,
            OriginalQuestionCreationSource = question.OriginalQuestionCreationSource,
            Title = question.Title,
            Stem = question.Stem,
            Options = DeserializeOptions(question.OptionsJson),
            CorrectAnswers = view == ExamPaperView.Participant ? new List<string>() : DeserializeValues(question.CorrectAnswersJson),
            Analysis = view == ExamPaperView.Participant ? null : question.Analysis,
            KnowledgePointsSnapshot = question.KnowledgePointsSnapshot,
            SourceSnapshot = view == ExamPaperView.Management ? question.SourceSnapshot : null,
            Score = question.Score,
            MultipleChoiceScoringMode = question.MultipleChoiceScoringMode,
            DisplayOrder = question.DisplayOrder
        };
    }

    private static ExamAssignmentDto MapAssignment(ExamAssignment assignment)
    {
        return new ExamAssignmentDto
        {
            Id = assignment.Id,
            PlanId = assignment.PlanId,
            ScopeType = assignment.ScopeType,
            TargetId = assignment.TargetId,
            IsActive = assignment.IsActive
        };
    }

    private async Task<ExamAttemptDto> MapAttemptAsync(ExamAttempt attempt, bool forTeacher = false)
    {
        var plan = await _planRepository.GetAsync(attempt.PlanId);
        var paper = await _paperRepository.GetAsync(attempt.PaperId);
        var answers = await GetAnswersAsync(attempt.Id);
        var score = await GetScoreByAttemptAsync(attempt.Id);
        if (!forTeacher) await GetAssignedPlanAsync(attempt.PlanId);
        var resultsAvailable = attempt.Status == ExamAttemptStatus.Scored &&
            (!plan.EndTime.HasValue || DateTime.UtcNow >= plan.EndTime.Value);
        var revealAnswers = forTeacher || (resultsAvailable && plan.ResultVisibility == ExamResultVisibility.FullReview);
        var paperDto = await MapPaperAsync(paper, forTeacher ? ExamPaperView.Management :
            revealAnswers ? ExamPaperView.ParticipantReview : ExamPaperView.Participant);
        if (!forTeacher) TrimStudentPaper(paperDto, revealAnswers);
        var scoreDto = score == null || (!forTeacher && (!resultsAvailable || plan.ResultVisibility == ExamResultVisibility.Hidden))
            ? null : await MapScoreAsync(score);
        if (!forTeacher && !revealAnswers && scoreDto != null) scoreDto.WrongQuestions.Clear();
        if (!forTeacher && scoreDto != null)
            foreach (var wrong in scoreDto.WrongQuestions) wrong.OriginalQuestionId = Guid.Empty;
        return new ExamAttemptDto
        {
            Id = attempt.Id,
            CreationTime = attempt.CreationTime,
            CreatorId = attempt.CreatorId,
            LastModificationTime = attempt.LastModificationTime,
            LastModifierId = attempt.LastModifierId,
            IsDeleted = attempt.IsDeleted,
            DeleterId = attempt.DeleterId,
            DeletionTime = attempt.DeletionTime,
            PlanId = attempt.PlanId,
            PaperId = attempt.PaperId,
            UserId = attempt.UserId,
            AttemptNumber = attempt.AttemptNumber,
            Status = attempt.Status,
            StartedTime = attempt.StartedTime,
            ExpiresTime = attempt.ExpiresTime,
            LastSavedTime = attempt.LastSavedTime,
            SubmittedTime = attempt.SubmittedTime,
            ScoredTime = attempt.ScoredTime,
            Plan = forTeacher ? await MapPlanAsync(plan) : await MapStudentPlanAsync(plan, revealAnswers),
            Paper = paperDto,
            Answers = answers.Select(MapAnswer).ToList(),
            Score = scoreDto
        };
    }

    private static ExamAnswerDto MapAnswer(ExamAnswer answer)
    {
        return new ExamAnswerDto
        {
            Id = answer.Id,
            AttemptId = answer.AttemptId,
            PaperQuestionId = answer.PaperQuestionId,
            Values = DeserializeValues(answer.AnswerJson),
            SaveVersion = answer.SaveVersion,
            SavedTime = answer.SavedTime
        };
    }

    private async Task<List<ExamScoreDto>> MapScoresAsync(IReadOnlyCollection<ExamScore> scores)
    {
        var result = new List<ExamScoreDto>();
        foreach (var score in scores)
        {
            result.Add(await MapScoreAsync(score));
        }

        return result;
    }

    private async Task<ExamScoreDto> MapScoreAsync(ExamScore score, bool includeWrongQuestions = true)
    {
        var wrongs = includeWrongQuestions ? await GetWrongQuestionsAsync(score.Id) : new List<WrongQuestionRecord>();
        return new ExamScoreDto
        {
            Id = score.Id,
            CreationTime = score.CreationTime,
            CreatorId = score.CreatorId,
            LastModificationTime = score.LastModificationTime,
            LastModifierId = score.LastModifierId,
            IsDeleted = score.IsDeleted,
            DeleterId = score.DeleterId,
            DeletionTime = score.DeletionTime,
            PlanId = score.PlanId,
            PaperId = score.PaperId,
            AttemptId = score.AttemptId,
            UserId = score.UserId,
            TotalScore = score.TotalScore,
            ObtainedScore = score.ObtainedScore,
            PassingScore = score.PassingScore,
            IsPassed = score.IsPassed,
            ScoredTime = score.ScoredTime,
            ScoringVersion = score.ScoringVersion,
            WrongQuestions = wrongs.Select(MapWrongQuestion).ToList()
        };
    }

    private static WrongQuestionRecordDto MapWrongQuestion(WrongQuestionRecord record)
    {
        return new WrongQuestionRecordDto
        {
            Id = record.Id,
            ScoreId = record.ScoreId,
            PlanId = record.PlanId,
            PaperId = record.PaperId,
            AttemptId = record.AttemptId,
            PaperQuestionId = record.PaperQuestionId,
            OriginalQuestionId = record.OriginalQuestionId,
            UserId = record.UserId,
            QuestionSnapshot = record.QuestionSnapshot,
            UserAnswers = DeserializeValues(record.UserAnswerJson),
            CorrectAnswers = DeserializeValues(record.CorrectAnswerJson),
            AnalysisSnapshot = record.AnalysisSnapshot,
            MaxScore = record.MaxScore,
            ObtainedScore = record.ObtainedScore
        };
    }

    private async Task<ExamPlan> GetOwnedPlanAsync(Guid id)
    {
        var plan = await _planRepository.GetAsync(id);
        EnsureTeachingOwner(plan.OwnerId);
        return plan;
    }

    private async Task<ExamPlan> GetAssignedPlanAsync(Guid id)
    {
        var plan = await _planRepository.GetAsync(id);
        if (plan.OwnerId == null) EnsureTeachingOwner(null);
        if (!await IsAssignedAsync(id, GetCurrentUserId()))
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamNotAssigned);
        return plan;
    }

    private async Task<List<Guid>> ValidateRecipientsAsync(CreateExamPlanDto input)
    {
        if (input.Assignments.Any(x => x.ScopeType != ExamAssignmentScopeType.User || !x.TargetId.HasValue) ||
            (input.StudentIds.Count > 0 && input.Assignments.Count > 0))
            throw new UserFriendlyException("请使用具体学生名单，不能向全员或组织发布。");
        var ids = (input.StudentIds.Count > 0 ? input.StudentIds : input.Assignments.Select(x => x.TargetId!.Value))
            .Distinct().ToList();
        if (ids.Count == 0 || ids.Any(x => x == Guid.Empty)) throw new UserFriendlyException("至少选择一位接收学生。");
        var activeIds = await AsyncExecuter.ToListAsync((await _teachingRelationships.GetQueryableAsync())
            .Where(x => x.TeacherId == CurrentUser.Id && x.Status == TeachingRelationshipStatus.Active && ids.Contains(x.StudentId))
            .Select(x => x.StudentId));
        if (activeIds.Distinct().Count() != ids.Count) throw new UserFriendlyException("部分学生不在你的有效名单中，请重新选择。");
        return ids;
    }

    private IQueryable<Question> FilterQuestionOwnership(IQueryable<Question> query) =>
        query.Where(x => x.OwnerTeacherId == CurrentUser.Id ||
            (x.OwnerTeacherId == null && (IsLegacyAdministrator || x.PersonalOwnerUserId == CurrentUser.Id ||
                x.CurriculumSnapshotJson != null)));

    private async Task<string> GetTeacherNameAsync(Guid? teacherId)
    {
        if (!teacherId.HasValue) return "历史任务";
        var user = await _users.FindAsync(teacherId.Value);
        return user?.Name ?? user?.UserName ?? "教师";
    }

    private async Task<ExamPlanDto> MapStudentPlanAsync(ExamPlan plan, bool revealAnswers = false)
    {
        var dto = await MapPlanAsync(plan, forParticipant: true);
        if (!plan.EndTime.HasValue)
            dto.Paper = await MapPaperAsync(await _paperRepository.GetAsync(plan.PaperId),
                revealAnswers ? ExamPaperView.ParticipantReview : ExamPaperView.Participant);
        dto.Assignments.Clear();
        if (dto.Paper != null) TrimStudentPaper(dto.Paper, revealAnswers);
        var attempts = await GetAttemptsAsync(plan.Id, GetCurrentUserId());
        var latest = attempts.OrderByDescending(x => x.AttemptNumber).FirstOrDefault();
        dto.MyAttemptId = latest?.Id;
        dto.MyStatus = latest?.Status;
        if (latest?.Status == ExamAttemptStatus.Scored && plan.ResultVisibility != ExamResultVisibility.Hidden &&
            (!plan.EndTime.HasValue || DateTime.UtcNow >= plan.EndTime.Value))
            dto.MyScore = (await GetScoreByAttemptAsync(latest.Id))?.ObtainedScore;
        return dto;
    }

    private static void TrimStudentPaper(ExamPaperDto paper, bool revealAnswers)
    {
        foreach (var question in paper.Questions)
        {
            question.OriginalQuestionId = Guid.Empty;
            question.SourceSnapshot = null;
            question.KnowledgePointsSnapshot = null;
            if (!revealAnswers)
            {
                question.CorrectAnswers.Clear();
                question.Analysis = null;
            }
        }
    }

    private void EnsureAttemptOwner(ExamAttempt attempt)
    {
        if (attempt.UserId != GetCurrentUserId())
        {
            throw new AbpAuthorizationException("The exam attempt does not belong to the current user.");
        }
    }

    private Guid GetCurrentUserId()
    {
        return CurrentUser.Id ?? throw new AbpAuthorizationException("Authentication is required for this operation.");
    }

    private static void EnsureLegacyScoring(IReadOnlyCollection<ExamPaperQuestion> questions)
    {
        if (questions.Any(x => x.ScoringDefinitionJson != null || x.Type == QuestionType.Subjective))
            throw new BusinessException("KnowPulse:Education:EducationTaskNotImplemented");
    }

    private static decimal ScoreQuestion(ExamPaperQuestion question, List<string> userValues, List<string> correctValues)
    {
        var normalizedUser = NormalizeValues(userValues);
        var normalizedCorrect = NormalizeValues(correctValues);

        if (question.Type == QuestionType.TrueFalse)
        {
            normalizedUser = normalizedUser.Select(NormalizeTrueFalse).ToList();
            normalizedCorrect = normalizedCorrect.Select(NormalizeTrueFalse).ToList();
        }

        if (question.Type == QuestionType.MultipleChoice)
        {
            if (question.MultipleChoiceScoringMode == MultipleChoiceScoringMode.PartialWithoutWrong)
            {
                var correctSet = normalizedCorrect.ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (normalizedUser.Any(x => !correctSet.Contains(x)) || normalizedCorrect.Count == 0)
                {
                    return 0;
                }

                return Math.Round(question.Score * normalizedUser.Count / normalizedCorrect.Count, 2, MidpointRounding.AwayFromZero);
            }

            return SameSet(normalizedUser, normalizedCorrect) ? question.Score : 0;
        }

        return SameSet(normalizedUser, normalizedCorrect) ? question.Score : 0;
    }

    private static bool SameSet(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right)
    {
        return left.Count == right.Count &&
               left.All(x => right.Contains(x, StringComparer.OrdinalIgnoreCase));
    }

    private static string NormalizeTrueFalse(string value) => value switch
    {
        "对" => "true",
        "错" => "false",
        _ => value
    };

    private static List<string> NormalizeValues(IEnumerable<string> values)
    {
        return values
            .Select(TrimToNull)
            .Where(x => x != null)
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string SerializeValues(IEnumerable<string> values)
    {
        return JsonSerializer.Serialize(NormalizeValues(values), JsonOptions);
    }

    private static List<string> DeserializeValues(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();
    }

    private static List<ExamPaperQuestionOptionDto> DeserializeOptions(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ExamPaperQuestionOptionDto>();
        }

        return JsonSerializer.Deserialize<List<ExamPaperQuestionOptionDto>>(json, JsonOptions) ?? new List<ExamPaperQuestionOptionDto>();
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private enum ExamPaperView
    {
        Management,
        Participant,
        ParticipantReview
    }

    private sealed record PaperBuildResult(ExamPaper Paper, List<ExamPaperQuestion> Questions);

    private sealed record SelectedQuestion(Question Question, decimal Score, MultipleChoiceScoringMode MultipleChoiceScoringMode);

    private sealed record QuestionScoreItem(
        ExamPaperQuestion Question,
        List<string> UserValues,
        List<string> CorrectValues,
        decimal ObtainedScore);
}
