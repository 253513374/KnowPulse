using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.KnowledgeBase;
using KnowPulse.Learning;
using KnowPulse.Organization;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class Mvp3LearningPathAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private static readonly Guid CurrentUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private readonly ILearningPathAppService _learningPathAppService;
    private readonly IRepository<KnowledgeItem, Guid> _knowledgeItemRepository;
    private readonly IRepository<KnowledgeVersion, Guid> _knowledgeVersionRepository;
    private readonly IRepository<KnowledgePermissionScope, Guid> _knowledgePermissionScopeRepository;
    private readonly IRepository<ExamPaper, Guid> _examPaperRepository;
    private readonly IRepository<ExamPlan, Guid> _examPlanRepository;
    private readonly IRepository<UserOrganizationAssignment, Guid> _userOrganizationAssignmentRepository;

    public Mvp3LearningPathAppServiceTests()
    {
        _learningPathAppService = GetRequiredService<ILearningPathAppService>();
        _knowledgeItemRepository = GetRequiredService<IRepository<KnowledgeItem, Guid>>();
        _knowledgeVersionRepository = GetRequiredService<IRepository<KnowledgeVersion, Guid>>();
        _knowledgePermissionScopeRepository = GetRequiredService<IRepository<KnowledgePermissionScope, Guid>>();
        _examPaperRepository = GetRequiredService<IRepository<ExamPaper, Guid>>();
        _examPlanRepository = GetRequiredService<IRepository<ExamPlan, Guid>>();
        _userOrganizationAssignmentRepository = GetRequiredService<IRepository<UserOrganizationAssignment, Guid>>();
    }

    [Fact]
    public async Task Should_Create_Update_Query_And_Deactivate_Learning_Path()
    {
        var prefix = UniquePrefix();
        var departmentId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var requiredKnowledge = await CreatePublishedKnowledgeAsync($"{prefix} required");
        var recommendedKnowledge = await CreatePublishedKnowledgeAsync($"{prefix} recommended");
        var examPlanId = await CreatePublishedExamPlanAsync($"{prefix} exam");

        var created = await _learningPathAppService.CreateAsync(new CreateLearningPathDto
        {
            Name = $"{prefix} onboarding",
            Description = "first version",
            Assignments =
            {
                new LearningPathAssignmentInputDto
                {
                    ScopeType = LearningPathAssignmentScopeType.Department,
                    TargetId = departmentId
                }
            },
            RequiredKnowledge =
            {
                new LearningPathKnowledgeInputDto { KnowledgeItemId = requiredKnowledge.ItemId, Note = "must read" }
            },
            RecommendedKnowledge =
            {
                new LearningPathKnowledgeInputDto { KnowledgeItemId = recommendedKnowledge.ItemId, Reason = "nice to know" }
            },
            RequiredExams =
            {
                new LearningPathRequiredExamInputDto { ExamPlanId = examPlanId, PassingRequired = true }
            }
        });

        created.ConfigVersion.ShouldBe(1);
        created.Assignments.Single().ScopeType.ShouldBe(LearningPathAssignmentScopeType.Department);
        created.RequiredKnowledge.Single().KnowledgeVersionId.ShouldBe(requiredKnowledge.VersionId);
        created.RecommendedKnowledge.Single().KnowledgeVersionId.ShouldBe(recommendedKnowledge.VersionId);
        created.RequiredExams.Single().ExamPlanId.ShouldBe(examPlanId);

        var updated = await _learningPathAppService.UpdateAsync(created.Id, new UpdateLearningPathDto
        {
            Name = $"{prefix} onboarding v2",
            Description = "second version",
            Assignments =
            {
                new LearningPathAssignmentInputDto
                {
                    ScopeType = LearningPathAssignmentScopeType.Position,
                    TargetId = positionId
                }
            },
            RecommendedKnowledge =
            {
                new LearningPathKnowledgeInputDto { KnowledgeItemId = requiredKnowledge.ItemId, Reason = "review later" }
            }
        });

        updated.ConfigVersion.ShouldBe(2);
        updated.Assignments.Single().ScopeType.ShouldBe(LearningPathAssignmentScopeType.Position);
        updated.RequiredKnowledge.ShouldBeEmpty();
        updated.RecommendedKnowledge.Single().KnowledgeItemId.ShouldBe(requiredKnowledge.ItemId);
        updated.RequiredExams.ShouldBeEmpty();

        var deactivated = await _learningPathAppService.DeactivateAsync(created.Id);
        deactivated.IsActive.ShouldBeFalse();
        deactivated.ConfigVersion.ShouldBe(3);

        var list = await _learningPathAppService.GetListAsync(new GetLearningPathListInput
        {
            Keyword = prefix,
            MaxResultCount = 10
        });
        list.Items.Single().IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task My_List_Should_Only_Return_Active_Paths_Assigned_By_Department_Position_Or_JobLevel()
    {
        var prefix = UniquePrefix();
        var departmentId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var jobLevelId = Guid.NewGuid();
        var otherDepartmentId = Guid.NewGuid();
        var knowledge = await CreatePublishedKnowledgeAsync($"{prefix} knowledge");

        await AssignCurrentUserAsync(departmentId, positionId, jobLevelId);
        await CreatePathAsync($"{prefix} department path", LearningPathAssignmentScopeType.Department, departmentId, knowledge.ItemId);
        await CreatePathAsync($"{prefix} position path", LearningPathAssignmentScopeType.Position, positionId, knowledge.ItemId);
        await CreatePathAsync($"{prefix} job level path", LearningPathAssignmentScopeType.JobLevel, jobLevelId, knowledge.ItemId);
        await CreatePathAsync($"{prefix} hidden path", LearningPathAssignmentScopeType.Department, otherDepartmentId, knowledge.ItemId);
        var inactive = await CreatePathAsync($"{prefix} inactive path", LearningPathAssignmentScopeType.Department, departmentId, knowledge.ItemId);
        await _learningPathAppService.DeactivateAsync(inactive.Id);

        var mine = await _learningPathAppService.GetMyListAsync(new GetLearningPathListInput
        {
            Keyword = prefix,
            MaxResultCount = 20
        });

        mine.TotalCount.ShouldBe(3);
        var names = mine.Items.Select(x => x.Name).ToList();
        names.ShouldContain($"{prefix} department path");
        names.ShouldContain($"{prefix} position path");
        names.ShouldContain($"{prefix} job level path");
        names.ShouldNotContain($"{prefix} hidden path");
        names.ShouldNotContain($"{prefix} inactive path");

        await Should.ThrowAsync<AbpAuthorizationException>(() => _learningPathAppService.GetMyAsync(inactive.Id));
    }

    [Fact]
    public async Task My_List_Should_Filter_Knowledge_Content_By_Knowledge_Permission_Scopes()
    {
        var prefix = UniquePrefix();
        var departmentId = Guid.NewGuid();
        var hiddenDepartmentId = Guid.NewGuid();
        var visibleKnowledge = await CreatePublishedKnowledgeAsync($"{prefix} visible");
        var hiddenKnowledge = await CreatePublishedKnowledgeAsync($"{prefix} hidden", hiddenDepartmentId);

        await AssignCurrentUserAsync(departmentId, null, null);
        await _learningPathAppService.CreateAsync(new CreateLearningPathDto
        {
            Name = $"{prefix} permission path",
            Assignments =
            {
                new LearningPathAssignmentInputDto
                {
                    ScopeType = LearningPathAssignmentScopeType.Department,
                    TargetId = departmentId
                }
            },
            RequiredKnowledge =
            {
                new LearningPathKnowledgeInputDto { KnowledgeItemId = hiddenKnowledge.ItemId }
            },
            RecommendedKnowledge =
            {
                new LearningPathKnowledgeInputDto { KnowledgeItemId = visibleKnowledge.ItemId }
            }
        });

        var mine = await _learningPathAppService.GetMyListAsync(new GetLearningPathListInput
        {
            Keyword = prefix,
            MaxResultCount = 10
        });

        var path = mine.Items.Single();
        path.RequiredKnowledge.ShouldBeEmpty();
        path.RecommendedKnowledge.Single().KnowledgeItemId.ShouldBe(visibleKnowledge.ItemId);
    }

    [Fact]
    public async Task My_Learning_Should_Record_Read_And_Completion_Status()
    {
        var prefix = UniquePrefix();
        var departmentId = Guid.NewGuid();
        var knowledge = await CreatePublishedKnowledgeAsync($"{prefix} read status");

        await AssignCurrentUserAsync(departmentId, null, null);
        var path = await CreatePathAsync($"{prefix} personal center", LearningPathAssignmentScopeType.Department, departmentId, knowledge.ItemId);

        var mine = await _learningPathAppService.GetMyListAsync(new GetLearningPathListInput
        {
            Keyword = prefix,
            MaxResultCount = 10
        });
        var initial = mine.Items.Single().RequiredKnowledge.Single();
        initial.CompletionStatus.ShouldBe(LearningContentCompletionStatus.NotStarted);
        initial.LastReadTime.ShouldBeNull();
        initial.ReadCount.ShouldBe(0);

        var reading = await _learningPathAppService.MarkKnowledgeReadAsync(new MarkLearningKnowledgeReadDto
        {
            LearningPathId = path.Id,
            KnowledgeItemId = knowledge.ItemId,
            KnowledgeVersionId = knowledge.VersionId,
            MarkCompleted = false
        });
        var inProgress = reading.RequiredKnowledge.Single();
        inProgress.CompletionStatus.ShouldBe(LearningContentCompletionStatus.InProgress);
        inProgress.LastReadTime.ShouldNotBeNull();
        inProgress.CompletedTime.ShouldBeNull();
        inProgress.ReadCount.ShouldBe(1);

        var completed = await _learningPathAppService.MarkKnowledgeReadAsync(new MarkLearningKnowledgeReadDto
        {
            LearningPathId = path.Id,
            KnowledgeItemId = knowledge.ItemId,
            KnowledgeVersionId = knowledge.VersionId,
            MarkCompleted = true
        });
        var completedKnowledge = completed.RequiredKnowledge.Single();
        completedKnowledge.CompletionStatus.ShouldBe(LearningContentCompletionStatus.Completed);
        completedKnowledge.CompletedTime.ShouldNotBeNull();
        completedKnowledge.ReadCount.ShouldBe(2);
    }

    [Theory]
    [InlineData(ExamResultVisibility.Hidden, false, false)]
    [InlineData(ExamResultVisibility.ScoreOnly, false, false)]
    [InlineData(ExamResultVisibility.FullReview, false, false)]
    [InlineData(ExamResultVisibility.Hidden, true, false)]
    [InlineData(ExamResultVisibility.ScoreOnly, true, true)]
    [InlineData(ExamResultVisibility.FullReview, true, true)]
    public async Task My_Learning_Should_Respect_Exam_Result_Visibility_And_Window(
        ExamResultVisibility visibility,
        bool ended,
        bool canViewResults)
    {
        var prefix = UniquePrefix();
        var planId = await CreatePublishedExamPlanAsync($"{prefix} results", visibility,
            ended ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(1), passingScore: 60);
        await CreateExamActivityAsync(planId, scored: true);
        var path = await CreateExamPathAsync(prefix, planId, passingRequired: true);

        var mine = await _learningPathAppService.GetMyAsync(path.Id);
        AssertExamResultVisibility(mine.RequiredExams.Single(), canViewResults);
        var list = await _learningPathAppService.GetMyListAsync(new GetLearningPathListInput { Keyword = prefix });
        AssertExamResultVisibility(list.Items.Single().RequiredExams.Single(), canViewResults);

        var managed = await _learningPathAppService.GetAsync(path.Id);
        managed.Assignments.Single().ScopeType.ShouldBe(LearningPathAssignmentScopeType.All);
        managed.RequiredExams.Single().ExamPlanId.ShouldBe(planId);
        managed.RequiredExams.Single().PassingRequired.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, LearningContentCompletionStatus.InProgress)]
    [InlineData(true, LearningContentCompletionStatus.Completed)]
    public async Task Non_Passing_Requirement_Should_Preserve_Activity_Without_Revealing_Hidden_Result(
        bool scored,
        LearningContentCompletionStatus expectedStatus)
    {
        var prefix = UniquePrefix();
        var planId = await CreatePublishedExamPlanAsync($"{prefix} hidden", ExamResultVisibility.Hidden,
            DateTime.UtcNow.AddHours(-1), passingScore: 60);
        await CreateExamActivityAsync(planId, scored);
        var path = await CreateExamPathAsync(prefix, planId, passingRequired: false);

        var exam = (await _learningPathAppService.GetMyAsync(path.Id)).RequiredExams.Single();
        exam.CompletionStatus.ShouldBe(expectedStatus);
        exam.LastAttemptTime.ShouldNotBeNull();
        exam.BestScore.ShouldBeNull();
        exam.PassedTime.ShouldBeNull();
    }

    [Fact]
    public async Task My_Learning_Should_Not_Reveal_Results_After_Exam_Assignment_Is_Removed()
    {
        var prefix = UniquePrefix();
        var planId = await CreatePublishedExamPlanAsync($"{prefix} no longer assigned", ExamResultVisibility.ScoreOnly,
            DateTime.UtcNow.AddHours(-1), passingScore: 60);
        await CreateExamActivityAsync(planId, scored: true);
        var path = await CreateExamPathAsync(prefix, planId, passingRequired: true);
        await GetRequiredService<IRepository<ExamAssignment, Guid>>().InsertAsync(
            new ExamAssignment(Guid.NewGuid(), null, planId, ExamAssignmentScopeType.User, Guid.NewGuid()),
            autoSave: true);

        var exam = (await _learningPathAppService.GetMyAsync(path.Id)).RequiredExams.Single();
        exam.IsAssignedToCurrentUser.ShouldBeFalse();
        exam.CompletionStatus.ShouldBe(LearningContentCompletionStatus.Unavailable);
        exam.BestScore.ShouldBeNull();
        exam.PassedTime.ShouldBeNull();
    }

    [Fact]
    public async Task My_Learning_Should_Not_Publish_A_Score_Before_The_Attempt_Is_Scored()
    {
        var prefix = UniquePrefix();
        var planId = await CreatePublishedExamPlanAsync($"{prefix} pending scoring", ExamResultVisibility.ScoreOnly,
            DateTime.UtcNow.AddHours(-1), passingScore: 60);
        var attempt = await CreateExamActivityAsync(planId, scored: false);
        await GetRequiredService<IRepository<ExamScore, Guid>>().InsertAsync(
            new ExamScore(Guid.NewGuid(), null, planId, attempt.PaperId, attempt.Id, CurrentUserId,
                100, 100, 60, DateTime.UtcNow, "pending-scoring"), autoSave: true);
        var path = await CreateExamPathAsync(prefix, planId, passingRequired: true);

        var exam = (await _learningPathAppService.GetMyAsync(path.Id)).RequiredExams.Single();
        exam.CompletionStatus.ShouldBe(LearningContentCompletionStatus.InProgress);
        exam.BestScore.ShouldBeNull();
        exam.PassedTime.ShouldBeNull();
    }

    private async Task<LearningPathDto> CreateExamPathAsync(string name, Guid planId, bool passingRequired)
    {
        return await _learningPathAppService.CreateAsync(new CreateLearningPathDto
        {
            Name = name,
            Assignments = { new LearningPathAssignmentInputDto { ScopeType = LearningPathAssignmentScopeType.All } },
            RequiredExams = { new LearningPathRequiredExamInputDto { ExamPlanId = planId, PassingRequired = passingRequired } }
        });
    }

    private async Task<ExamAttempt> CreateExamActivityAsync(Guid planId, bool scored)
    {
        var plan = await _examPlanRepository.GetAsync(planId);
        var attempt = new ExamAttempt(Guid.NewGuid(), null, plan.Id, plan.PaperId, CurrentUserId, 1,
            plan.StartTime, plan.EndTime);
        attempt.Submit(plan.StartTime.AddMinutes(1), $"learning-submit-{attempt.Id:N}");
        var scoredTime = plan.StartTime.AddMinutes(2);
        if (scored)
        {
            attempt.MarkScored(scoredTime);
        }

        await GetRequiredService<IRepository<ExamAttempt, Guid>>().InsertAsync(attempt, autoSave: true);
        if (scored)
        {
            await GetRequiredService<IRepository<ExamScore, Guid>>().InsertAsync(
                new ExamScore(Guid.NewGuid(), null, plan.Id, plan.PaperId, attempt.Id, CurrentUserId,
                    100, 100, plan.PassingScore, scoredTime, "visibility-test"),
                autoSave: true);
        }

        return attempt;
    }

    private static void AssertExamResultVisibility(LearningPathRequiredExamDto exam, bool canViewResults)
    {
        exam.IsAssignedToCurrentUser.ShouldBeTrue();
        exam.LastAttemptTime.ShouldNotBeNull();
        if (canViewResults)
        {
            exam.BestScore.ShouldBe(100m);
            exam.PassedTime.ShouldNotBeNull();
            exam.CompletionStatus.ShouldBe(LearningContentCompletionStatus.Completed);
        }
        else
        {
            exam.BestScore.ShouldBeNull();
            exam.PassedTime.ShouldBeNull();
            exam.CompletionStatus.ShouldBe(LearningContentCompletionStatus.InProgress);
        }
    }

    private async Task<LearningPathDto> CreatePathAsync(
        string name,
        LearningPathAssignmentScopeType scopeType,
        Guid targetId,
        Guid knowledgeItemId)
    {
        return await _learningPathAppService.CreateAsync(new CreateLearningPathDto
        {
            Name = name,
            Assignments =
            {
                new LearningPathAssignmentInputDto
                {
                    ScopeType = scopeType,
                    TargetId = targetId
                }
            },
            RequiredKnowledge =
            {
                new LearningPathKnowledgeInputDto { KnowledgeItemId = knowledgeItemId }
            }
        });
    }

    private async Task AssignCurrentUserAsync(Guid departmentId, Guid? positionId, Guid? jobLevelId)
    {
        await _userOrganizationAssignmentRepository.InsertAsync(
            new UserOrganizationAssignment(
                Guid.NewGuid(),
                null,
                CurrentUserId,
                departmentId,
                positionId,
                jobLevelId,
                securityLevel: 1,
                isPrimary: false),
            autoSave: true);
    }

    private async Task<PublishedKnowledge> CreatePublishedKnowledgeAsync(string title, Guid? restrictedDepartmentId = null)
    {
        var item = await _knowledgeItemRepository.InsertAsync(
            new KnowledgeItem(Guid.NewGuid(), null, title, summary: $"{title} summary"),
            autoSave: true);

        var version = new KnowledgeVersion(
            Guid.NewGuid(),
            null,
            item.Id,
            1,
            $"{title} v1",
            $"{title} content");
        version.Publish(DateTime.UtcNow);
        version = await _knowledgeVersionRepository.InsertAsync(version, autoSave: true);

        item.SetCurrentPublishedVersion(version.Id);
        await _knowledgeItemRepository.UpdateAsync(item, autoSave: true);

        if (restrictedDepartmentId.HasValue)
        {
            await _knowledgePermissionScopeRepository.InsertAsync(
                new KnowledgePermissionScope(
                    Guid.NewGuid(),
                    null,
                    item.Id,
                    KnowledgePermissionScopeType.Department,
                    restrictedDepartmentId.Value),
                autoSave: true);
        }

        return new PublishedKnowledge(item.Id, version.Id);
    }

    private async Task<Guid> CreatePublishedExamPlanAsync(
        string name,
        ExamResultVisibility resultVisibility = ExamResultVisibility.ScoreOnly,
        DateTime? endTime = null,
        decimal passingScore = 0)
    {
        var now = DateTime.UtcNow;
        var end = endTime ?? now.AddDays(1);
        var paper = await _examPaperRepository.InsertAsync(
            new ExamPaper(
                Guid.NewGuid(),
                null,
                $"{name} paper",
                ExamPaperBuildMode.Fixed,
                applicablePositionId: null,
                questionCount: 0,
                totalScore: 0),
            autoSave: true);

        var plan = await _examPlanRepository.InsertAsync(
            new ExamPlan(
                Guid.NewGuid(),
                null,
                name,
                paper.Id,
                end < now ? end.AddHours(-1) : now.AddMinutes(-5),
                end,
                durationMinutes: 30,
                passingScore: passingScore,
                maxAttempts: 1,
                resultVisibility: resultVisibility),
            autoSave: true);

        return plan.Id;
    }

    private static string UniquePrefix()
    {
        return $"mvp3-learning-{Guid.NewGuid():N}";
    }

    private sealed record PublishedKnowledge(Guid ItemId, Guid VersionId);
}
