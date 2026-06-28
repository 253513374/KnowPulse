using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Organization;
using KnowPulse.Permissions;
using KnowPulse.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;

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
    private readonly IRepository<ExamAttempt, Guid> _attemptRepository;
    private readonly IRepository<ExamAnswer, Guid> _answerRepository;
    private readonly IRepository<ExamScore, Guid> _scoreRepository;
    private readonly IRepository<WrongQuestionRecord, Guid> _wrongQuestionRepository;
    private readonly IRepository<Question, Guid> _questionRepository;
    private readonly IRepository<QuestionOption, Guid> _questionOptionRepository;
    private readonly IRepository<QuestionAnswer, Guid> _questionAnswerRepository;
    private readonly IRepository<QuestionAnalysis, Guid> _questionAnalysisRepository;
    private readonly IRepository<QuestionKnowledgePoint, Guid> _questionKnowledgePointRepository;
    private readonly IRepository<QuestionSource, Guid> _questionSourceRepository;
    private readonly IRepository<UserOrganizationAssignment, Guid> _userOrganizationAssignmentRepository;

    public ExamAppService(
        IRepository<ExamPlan, Guid> planRepository,
        IRepository<ExamPaper, Guid> paperRepository,
        IRepository<ExamPaperQuestion, Guid> paperQuestionRepository,
        IRepository<ExamAssignment, Guid> assignmentRepository,
        IRepository<ExamAttempt, Guid> attemptRepository,
        IRepository<ExamAnswer, Guid> answerRepository,
        IRepository<ExamScore, Guid> scoreRepository,
        IRepository<WrongQuestionRecord, Guid> wrongQuestionRepository,
        IRepository<Question, Guid> questionRepository,
        IRepository<QuestionOption, Guid> questionOptionRepository,
        IRepository<QuestionAnswer, Guid> questionAnswerRepository,
        IRepository<QuestionAnalysis, Guid> questionAnalysisRepository,
        IRepository<QuestionKnowledgePoint, Guid> questionKnowledgePointRepository,
        IRepository<QuestionSource, Guid> questionSourceRepository,
        IRepository<UserOrganizationAssignment, Guid> userOrganizationAssignmentRepository)
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
        _userOrganizationAssignmentRepository = userOrganizationAssignmentRepository;
    }

    public virtual async Task<PagedResultDto<ExamPlanDto>> GetPlanListAsync(GetExamPlanListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);

        var queryable = await _planRepository.GetQueryableAsync();
        queryable = ApplyPlanFilters(queryable, input);

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var plans = await AsyncExecuter.ToListAsync(queryable
            .OrderByDescending(x => x.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<ExamPlanDto>(totalCount, await MapPlansAsync(plans));
    }

    public virtual async Task<ExamPlanDto> GetPlanAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);
        return await MapPlanAsync(await _planRepository.GetAsync(id));
    }

    public virtual async Task<ExamPaperDto> PreviewPaperAsync(GenerateExamPaperDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);
        var build = await BuildPaperAsync(input);
        return await MapPaperAsync(build.Paper, build.Questions);
    }

    public virtual async Task<ExamPlanDto> CreatePlanAsync(CreateExamPlanDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage);

        var build = await BuildPaperAsync(input.Paper);
        var paper = await _paperRepository.InsertAsync(build.Paper, autoSave: true);
        foreach (var question in build.Questions)
        {
            await _paperQuestionRepository.InsertAsync(question, autoSave: true);
        }

        var plan = new ExamPlan(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.Name.Trim(),
            paper.Id,
            input.StartTime,
            input.EndTime,
            input.DurationMinutes,
            input.PassingScore,
            input.MaxAttempts,
            input.ResultVisibility,
            TrimToNull(input.Description));

        await _planRepository.InsertAsync(plan, autoSave: true);

        var assignments = input.Assignments.Count == 0
            ? new List<ExamAssignmentInputDto> { new() { ScopeType = ExamAssignmentScopeType.All } }
            : input.Assignments;

        foreach (var assignment in assignments)
        {
            await _assignmentRepository.InsertAsync(
                new ExamAssignment(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    plan.Id,
                    assignment.ScopeType,
                    assignment.TargetId),
                autoSave: true);
        }

        return await MapPlanAsync(plan);
    }

    public virtual async Task<PagedResultDto<ExamPlanDto>> GetMyPlanListAsync(GetExamPlanListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Participate);

        var userId = GetCurrentUserId();
        var queryable = await _planRepository.GetQueryableAsync();
        queryable = ApplyPlanFilters(queryable, input).Where(x => x.Status == ExamPlanStatus.Published);

        var allPlans = await AsyncExecuter.ToListAsync(queryable.OrderByDescending(x => x.StartTime));
        var assignedPlans = new List<ExamPlan>();
        foreach (var plan in allPlans)
        {
            if (await IsAssignedAsync(plan.Id, userId))
            {
                assignedPlans.Add(plan);
            }
        }

        var paged = assignedPlans.Skip(input.SkipCount).Take(input.MaxResultCount).ToList();
        return new PagedResultDto<ExamPlanDto>(assignedPlans.Count, await MapPlansAsync(paged));
    }

    public virtual async Task<ExamAttemptDto> StartAttemptAsync(Guid planId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Participate);

        var userId = GetCurrentUserId();
        var now = DateTime.UtcNow;
        var plan = await _planRepository.GetAsync(planId);
        plan.EnsureCanStart(now);

        if (!await IsAssignedAsync(plan.Id, userId))
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamNotAssigned);
        }

        var attempts = await GetAttemptsAsync(plan.Id, userId);
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
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamAttemptLimitReached);
        }

        var expiresTime = now.AddMinutes(plan.DurationMinutes);
        if (expiresTime > plan.EndTime)
        {
            expiresTime = plan.EndTime;
        }

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

        var attempt = await _attemptRepository.GetAsync(attemptId);
        EnsureAttemptOwner(attempt);

        var now = DateTime.UtcNow;
        attempt.EnsureCanSave(now);

        var paperQuestions = await GetPaperQuestionsAsync(attempt.PaperId);
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

        var attempt = await _attemptRepository.GetAsync(attemptId);
        EnsureAttemptOwner(attempt);

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
        return await GradeAttemptCoreAsync(await _attemptRepository.GetAsync(attemptId));
    }

    public virtual async Task<PagedResultDto<ExamScoreDto>> GetScoreListAsync(GetExamScoreListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Results);

        var queryable = await _scoreRepository.GetQueryableAsync();
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

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var scores = await AsyncExecuter.ToListAsync(queryable
            .OrderByDescending(x => x.ScoredTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<ExamScoreDto>(totalCount, await MapScoresAsync(scores));
    }

    private async Task<ExamScoreDto> GradeAttemptCoreAsync(ExamAttempt attempt)
    {
        var plan = await _planRepository.GetAsync(attempt.PlanId);
        var paper = await _paperRepository.GetAsync(attempt.PaperId);
        var paperQuestions = await GetPaperQuestionsAsync(attempt.PaperId);
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
                    plan.PassingScore,
                    now,
                    ScoringVersion),
                autoSave: true);
        }
        else
        {
            existingScore.Refresh(paper.TotalScore, obtainedScore, plan.PassingScore, now, ScoringVersion);
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

    private async Task<PaperBuildResult> BuildPaperAsync(GenerateExamPaperDto input)
    {
        var selected = input.BuildMode == ExamPaperBuildMode.Fixed
            ? await SelectFixedQuestionsAsync(input)
            : await SelectByRulesAsync(input);

        if (selected.Count == 0)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamPaperQuestionInsufficient);
        }

        var paperId = GuidGenerator.Create();
        var totalScore = selected.Sum(x => x.Score);
        var paper = new ExamPaper(
            paperId,
            CurrentTenant.Id,
            input.Title.Trim(),
            input.BuildMode,
            input.ApplicablePositionId,
            selected.Count,
            totalScore);

        var questions = new List<ExamPaperQuestion>();
        for (var i = 0; i < selected.Count; i++)
        {
            var selectedQuestion = selected[i];
            questions.Add(await CreatePaperQuestionSnapshotAsync(paperId, selectedQuestion.Question, selectedQuestion.Score, selectedQuestion.MultipleChoiceScoringMode, i + 1));
        }

        return new PaperBuildResult(paper, questions);
    }

    private async Task<List<SelectedQuestion>> SelectFixedQuestionsAsync(GenerateExamPaperDto input)
    {
        var ids = input.FixedQuestionIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamPaperQuestionInsufficient);
        }

        var queryable = await _questionRepository.GetQueryableAsync();
        var questions = await AsyncExecuter.ToListAsync(queryable
            .Where(x => ids.Contains(x.Id) && x.Status == QuestionStatus.Approved));

        if (questions.Count != ids.Count)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamPaperQuestionInsufficient);
        }

        var defaultScore = input.Rules.FirstOrDefault()?.ScorePerQuestion ?? 5m;
        var scoringMode = input.Rules.FirstOrDefault()?.MultipleChoiceScoringMode ?? MultipleChoiceScoringMode.AllOrNothing;
        return ids.Select(id => new SelectedQuestion(
                questions.Single(x => x.Id == id),
                defaultScore,
                scoringMode))
            .ToList();
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
        var queryable = await _questionRepository.GetQueryableAsync();
        queryable = queryable.Where(x => x.Status == QuestionStatus.Approved && !usedIds.Contains(x.Id));

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

        return candidates;
    }

    private async Task<ExamPaperQuestion> CreatePaperQuestionSnapshotAsync(
        Guid paperId,
        Question question,
        decimal score,
        MultipleChoiceScoringMode scoringMode,
        int displayOrder)
    {
        var options = await GetQuestionOptionsAsync(question.Id);
        var answers = await GetQuestionAnswersAsync(question.Id);
        var analyses = await GetQuestionAnalysesAsync(question.Id);
        var points = await GetQuestionKnowledgePointsAsync(question.Id);
        var sources = await GetQuestionSourcesAsync(question.Id);

        return new ExamPaperQuestion(
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
            BuildSourceSnapshot(sources),
            score,
            scoringMode,
            displayOrder);
    }

    private async Task<bool> IsAssignedAsync(Guid planId, Guid userId)
    {
        var assignments = await GetAssignmentsAsync(planId);
        if (assignments.Count == 0 || assignments.Any(x => x.IsActive && x.ScopeType == ExamAssignmentScopeType.All))
        {
            return true;
        }

        if (assignments.Any(x => x.IsActive && x.ScopeType == ExamAssignmentScopeType.User && x.TargetId == userId))
        {
            return true;
        }

        var assignmentQueryable = await _userOrganizationAssignmentRepository.GetQueryableAsync();
        var userAssignments = await AsyncExecuter.ToListAsync(assignmentQueryable.Where(x => x.UserId == userId && x.IsActive));
        var departmentIds = userAssignments.Select(x => x.DepartmentId).ToHashSet();
        var positionIds = userAssignments.Where(x => x.PositionId.HasValue).Select(x => x.PositionId!.Value).ToHashSet();

        return assignments.Any(x =>
            x.IsActive &&
            ((x.ScopeType == ExamAssignmentScopeType.Department && x.TargetId.HasValue && departmentIds.Contains(x.TargetId.Value)) ||
             (x.ScopeType == ExamAssignmentScopeType.Position && x.TargetId.HasValue && positionIds.Contains(x.TargetId.Value))));
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

    private async Task<List<ExamPlanDto>> MapPlansAsync(IReadOnlyCollection<ExamPlan> plans)
    {
        var result = new List<ExamPlanDto>();
        foreach (var plan in plans)
        {
            result.Add(await MapPlanAsync(plan));
        }

        return result;
    }

    private async Task<ExamPlanDto> MapPlanAsync(ExamPlan plan)
    {
        var assignments = await GetAssignmentsAsync(plan.Id);
        var paper = await _paperRepository.GetAsync(plan.PaperId);
        return new ExamPlanDto
        {
            Id = plan.Id,
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
            Paper = await MapPaperAsync(paper),
            Assignments = assignments.Select(MapAssignment).ToList()
        };
    }

    private async Task<ExamPaperDto> MapPaperAsync(ExamPaper paper)
    {
        return await MapPaperAsync(paper, await GetPaperQuestionsAsync(paper.Id));
    }

    private Task<ExamPaperDto> MapPaperAsync(ExamPaper paper, IReadOnlyCollection<ExamPaperQuestion> questions)
    {
        return Task.FromResult(new ExamPaperDto
        {
            Id = paper.Id,
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
            Questions = questions.OrderBy(x => x.DisplayOrder).Select(MapPaperQuestion).ToList()
        });
    }

    private static ExamPaperQuestionDto MapPaperQuestion(ExamPaperQuestion question)
    {
        return new ExamPaperQuestionDto
        {
            Id = question.Id,
            PaperId = question.PaperId,
            OriginalQuestionId = question.OriginalQuestionId,
            Type = question.Type,
            Difficulty = question.Difficulty,
            OriginalQuestionCreationSource = question.OriginalQuestionCreationSource,
            Title = question.Title,
            Stem = question.Stem,
            Options = DeserializeOptions(question.OptionsJson),
            CorrectAnswers = DeserializeValues(question.CorrectAnswersJson),
            Analysis = question.Analysis,
            KnowledgePointsSnapshot = question.KnowledgePointsSnapshot,
            SourceSnapshot = question.SourceSnapshot,
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

    private async Task<ExamAttemptDto> MapAttemptAsync(ExamAttempt attempt)
    {
        var plan = await _planRepository.GetAsync(attempt.PlanId);
        var paper = await _paperRepository.GetAsync(attempt.PaperId);
        var answers = await GetAnswersAsync(attempt.Id);
        var score = await GetScoreByAttemptAsync(attempt.Id);

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
            Plan = await MapPlanAsync(plan),
            Paper = await MapPaperAsync(paper),
            Answers = answers.Select(MapAnswer).ToList(),
            Score = score == null ? null : await MapScoreAsync(score)
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

    private async Task<ExamScoreDto> MapScoreAsync(ExamScore score)
    {
        var wrongs = await GetWrongQuestionsAsync(score.Id);
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

    private static decimal ScoreQuestion(ExamPaperQuestion question, List<string> userValues, List<string> correctValues)
    {
        var normalizedUser = NormalizeValues(userValues);
        var normalizedCorrect = NormalizeValues(correctValues);

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

    private static string? BuildSourceSnapshot(IReadOnlyCollection<QuestionSource> sources)
    {
        if (sources.Count == 0)
        {
            return null;
        }

        return string.Join("; ", sources.Select(x =>
            TrimToNull(x.SourceTitle) ??
            TrimToNull(x.ManualSourceDescription) ??
            TrimToNull(x.SourceHash) ??
            x.KnowledgeItemId?.ToString() ??
            x.KnowledgeVersionId?.ToString() ??
            x.ChunkId?.ToString()).Where(x => x != null));
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record PaperBuildResult(ExamPaper Paper, List<ExamPaperQuestion> Questions);

    private sealed record SelectedQuestion(Question Question, decimal Score, MultipleChoiceScoringMode MultipleChoiceScoringMode);

    private sealed record QuestionScoreItem(
        ExamPaperQuestion Question,
        List<string> UserValues,
        List<string> CorrectValues,
        decimal ObtainedScore);
}
