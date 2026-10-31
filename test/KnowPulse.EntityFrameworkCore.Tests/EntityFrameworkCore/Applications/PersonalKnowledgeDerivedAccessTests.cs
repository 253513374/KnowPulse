using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.AiQuestioning;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class PersonalKnowledgeDerivedAccessTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private readonly TestKnowledgeVectorStore _vectors;
    private readonly TestRagChatClient _chat;
    private readonly IAiQuestionGenerationAppService _generation;
    private readonly IQuestionAppService _questions;
    private readonly IQuestionWorkflowAppService _workflow;
    private readonly IRagEvaluationAppService _evaluations;
    private readonly IRagCallLogAppService _logs;

    public PersonalKnowledgeDerivedAccessTests()
    {
        _vectors = GetRequiredService<TestKnowledgeVectorStore>();
        _chat = GetRequiredService<TestRagChatClient>();
        _generation = GetRequiredService<IAiQuestionGenerationAppService>();
        _questions = GetRequiredService<IQuestionAppService>();
        _workflow = GetRequiredService<IQuestionWorkflowAppService>();
        _evaluations = GetRequiredService<IRagEvaluationAppService>();
        _logs = GetRequiredService<IRagCallLogAppService>();
        _vectors.Reset();
        _chat.Reset();
        GetRequiredService<TestKnowledgeEmbeddingClient>().Reset();
    }

    [Fact]
    public async Task Private_Vector_Should_Be_Owner_Only_And_Database_Check_Should_Reject_Stale_All_Payload()
    {
        var ownerChunk = await CreateChunkAsync(_owner);
        var otherChunk = await CreateChunkAsync(_other);
        using (ChangeUser(_owner))
        {
            var embedding = await GetRequiredService<IKnowledgeEmbeddingPipeline>()
                .GenerateForVersionAsync(ownerChunk.KnowledgeVersionId);
            embedding.Succeeded.ShouldBeTrue();
            var payloads = _vectors.UpsertRequests.SelectMany(x => x)
                .Where(x => x.ChunkId == ownerChunk.Id).Select(x => x.Payload).ToList();
            payloads.ShouldNotBeEmpty();
            foreach (var payload in payloads)
            {
                payload.Visibility.ShouldBe("Restricted");
                payload.UserIds.ShouldBe(new[] { _owner });
                payload.DepartmentIds.ShouldBeEmpty();
                payload.PositionIds.ShouldBeEmpty();
                payload.RoleIds.ShouldBeEmpty();
            }
        }

        // Simulate a stale or faulty vector store returning another owner's material as All.
        _vectors.SearchResults.AddRange(new[] { ToHit(ownerChunk), ToHit(otherChunk) });
        using (ChangeUser(_owner))
        {
            var found = await GetRequiredService<IKnowledgeRetriever>().SearchAsync(new RagSearchRequest { Query = "教学经验" });
            found.Select(x => x.ChunkId).ShouldBe(new[] { ownerChunk.Id });
            found.Single().Content.ShouldBe(ownerChunk.Content);
        }
        using (ChangeUser(_other))
        {
            var found = await GetRequiredService<IKnowledgeRetriever>().SearchAsync(new RagSearchRequest { Query = "教学经验" });
            found.Select(x => x.ChunkId).ShouldBe(new[] { otherChunk.Id });
        }
    }

    [Fact]
    public async Task Generated_Content_Should_Stay_Private_And_Retain_Original_Source_After_Edit()
    {
        var chunk = await CreateChunkAsync(_owner);
        _vectors.SearchResults.Add(ToHit(chunk));
        _chat.ResponseContent = QuestionResponse(chunk.Id);
        AiQuestionGenerationTaskDto task;
        QuestionDto question;
        using (ChangeUser(_owner))
        {
            task = await _generation.CreateAsync(new CreateAiQuestionGenerationTaskDto { KnowledgeScope = "个人教学经验" });
            task.Status.ShouldBe(AiQuestionGenerationTaskStatus.Pending);
            await EnsureAccountAsync(_owner);
            await GetRequiredService<AiQuestionGenerationProcessor>().ProcessAsync(task.Id);
            task = await _generation.GetAsync(task.Id);
            task.Status.ShouldBe(AiQuestionGenerationTaskStatus.Generated);
            question = await _workflow.AdoptAiCandidateAsync(task.Candidates.Single().Id);
            question = await _workflow.SaveDraftAsync(question.Id, Draft());
            question.Sources.ShouldContain(source => source.KnowledgeItemId == chunk.KnowledgeItemId &&
                source.KnowledgeVersionId == chunk.KnowledgeVersionId && source.ChunkId == chunk.Id);
            question.Sources.ShouldContain(source => !source.KnowledgeItemId.HasValue &&
                !source.KnowledgeVersionId.HasValue && !source.ChunkId.HasValue);
            await WithUnitOfWorkAsync(async () =>
            {
                (await Repository<Question>().GetAsync(question.Id)).PersonalOwnerUserId.ShouldBe(_owner);
                (await Repository<AiQuestionGenerationTask>().GetAsync(task.Id)).PersonalOwnerUserId.ShouldBe(_owner);
            });
            (await _generation.GetAsync(task.Id)).Candidates.Count.ShouldBe(1);
            (await _questions.GetAsync(question.Id)).Id.ShouldBe(question.Id);
            (await _logs.GetListAsync(new GetRagCallLogListInput { UserId = _owner })).Items.ShouldNotBeEmpty();
        }

        using (ChangeUser(_other))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _generation.GetAsync(task.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _generation.GetCandidateAsync(task.Candidates.Single().Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _generation.RetryAsync(task.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.AdoptAiCandidateAsync(task.Candidates.Single().Id));
            (await _generation.GetListAsync(new GetAiQuestionGenerationTaskListInput())).Items.ShouldNotContain(x => x.Id == task.Id);
            (await _generation.GetCallLogsAsync(new GetAiQuestionModelCallLogListInput { TaskId = task.Id })).TotalCount.ShouldBe(0);
            (await _logs.GetListAsync(new GetRagCallLogListInput { UserId = _owner })).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _questions.GetAsync(question.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _questions.GetSourcesAsync(question.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _questions.GetReviewsAsync(question.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _questions.DeleteAsync(question.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.SaveDraftAsync(question.Id, Draft()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.SubmitAsync(question.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.DecideReviewAsync(question.Id,
                new DecideQuestionReviewDto { Decision = QuestionReviewDecision.Approve }));
            (await _questions.GetListAsync(new GetQuestionListInput { Keyword = question.Title })).Items.ShouldNotContain(x => x.Id == question.Id);
            var similar = await WithUnitOfWorkAsync(() => GetRequiredService<IQuestionSimilarityService>()
                .FindMostSimilarAsync(question.Title, question.Stem));
            similar.ShouldBeNull();
        }

        using (ChangeUser(_owner))
        {
            (await _workflow.SubmitAsync(question.Id)).Status.ShouldBe(QuestionStatus.PendingReview);
            (await _workflow.DecideReviewAsync(question.Id,
                new DecideQuestionReviewDto { Decision = QuestionReviewDecision.Approve })).IsUsableForExam.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Failed_Task_And_No_Citation_Log_Should_Be_Private_Before_Library_Is_Created()
    {
        AiQuestionGenerationTaskDto task;
        using (ChangeUser(_owner))
        {
            task = await _generation.CreateAsync(new CreateAiQuestionGenerationTaskDto { KnowledgeScope = "尚未入库的个人课堂记录" });
            await EnsureAccountAsync(_owner);
            await GetRequiredService<AiQuestionGenerationProcessor>().ProcessAsync(task.Id);
            task = await _generation.GetAsync(task.Id);
            task.Candidates.ShouldBeEmpty();
            (await _generation.GetAsync(task.Id)).KnowledgeScope.ShouldBe("尚未入库的个人课堂记录");
            (await _generation.GetCallLogsAsync(new GetAiQuestionModelCallLogListInput { TaskId = task.Id })).TotalCount.ShouldBe(1);
            (await _logs.GetListAsync(new GetRagCallLogListInput { UserId = _owner })).Items.ShouldNotBeEmpty();
            await WithUnitOfWorkAsync(async () =>
            {
                (await Repository<KnowledgeLibrary>().FindAsync(_owner)).ShouldBeNull();
                (await Repository<AiQuestionGenerationTask>().GetAsync(task.Id)).PersonalOwnerUserId.ShouldBe(_owner);
            });
        }
        using (ChangeUser(_other))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _generation.GetAsync(task.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _generation.RetryAsync(task.Id));
            (await _generation.GetCallLogsAsync(new GetAiQuestionModelCallLogListInput { TaskId = task.Id })).Items.ShouldBeEmpty();
            (await _logs.GetListAsync(new GetRagCallLogListInput { UserId = _owner })).Items.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Manual_Source_Copy_Should_Check_Private_Owner_And_Reference_Relationships()
    {
        await EnsureAccountAsync(_owner);
        await EnsureAccountAsync(_other);
        var chunk = await CreateChunkAsync(_owner);
        var unrelated = await CreateChunkAsync(_owner);
        QuestionDto owned;
        using (ChangeUser(_other))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.CreateManualDraftAsync(Draft(chunk)));
        }
        using (ChangeUser(_owner))
        {
            var inconsistent = Draft(chunk);
            inconsistent.Sources[0].KnowledgeItemId = unrelated.KnowledgeItemId;
            await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.CreateManualDraftAsync(inconsistent));
            owned = await _workflow.CreateManualDraftAsync(Draft(chunk));
            await _workflow.SaveDraftAsync(owned.Id, Draft());
            (await _questions.GetAsync(owned.Id)).Id.ShouldBe(owned.Id);
        }
        using (ChangeUser(_other))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _questions.GetAsync(owned.Id));
        }
    }

    [Fact]
    public async Task Evaluation_Inputs_And_Runs_Should_Stay_Private_Without_Citations()
    {
        RagEvaluationCaseDto evaluationCase;
        RagEvaluationRunDto run;
        using (ChangeUser(_owner))
        {
            evaluationCase = await _evaluations.CreateCaseAsync(new CreateRagEvaluationCaseDto
            {
                Name = "私人教学评测", Question = "个人课堂记录中的问题", ExpectedRefusal = true
            });
            run = await _evaluations.RunAsync(new RunRagEvaluationDto { Name = "私人评测运行", CaseIds = { evaluationCase.Id } });
            (await _evaluations.GetRunAsync(run.Id)).Results.Count.ShouldBe(1);
            await WithUnitOfWorkAsync(async () =>
            {
                (await Repository<RagEvaluationCase>().GetAsync(evaluationCase.Id)).PersonalOwnerUserId.ShouldBe(_owner);
                (await Repository<RagEvaluationRun>().GetAsync(run.Id)).PersonalOwnerUserId.ShouldBe(_owner);
            });
        }
        using (ChangeUser(_other))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _evaluations.GetCaseAsync(evaluationCase.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _evaluations.UpdateCaseAsync(evaluationCase.Id,
                new UpdateRagEvaluationCaseDto { Name = "被覆盖", Question = "被覆盖" }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _evaluations.RunAsync(new RunRagEvaluationDto { CaseIds = { evaluationCase.Id } }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _evaluations.GetRunAsync(run.Id));
            (await _evaluations.GetCaseListAsync(new GetRagEvaluationCaseListInput())).Items.ShouldNotContain(x => x.Id == evaluationCase.Id);
            (await _evaluations.GetRunListAsync(new GetRagEvaluationRunListInput())).Items.ShouldNotContain(x => x.Id == run.Id);
        }
        using (ChangeUser(_owner))
        {
            (await _evaluations.GetCaseAsync(evaluationCase.Id)).Id.ShouldBe(evaluationCase.Id);
            (await _evaluations.GetRunAsync(run.Id)).Id.ShouldBe(run.Id);
        }
    }

    [Fact]
    public async Task Legacy_Organization_Questions_Should_Remain_Visible_But_Revoked_Source_Must_Be_Rechecked()
    {
        var chunk = await CreateChunkAsync(null);
        var (sourceQuestion, manualQuestion) = await WithUnitOfWorkAsync(async () =>
        {
            var source = new Question(Guid.NewGuid(), null, QuestionType.SingleChoice, QuestionDifficulty.Normal,
                QuestionCreationSource.Manual, "历史企业来源题", "课堂提问的第一步是什么？");
            var manual = new Question(Guid.NewGuid(), null, QuestionType.SingleChoice, QuestionDifficulty.Normal,
                QuestionCreationSource.Manual, "历史企业手工题", "课堂提问的第一步是什么？");
            await Repository<Question>().InsertAsync(source, autoSave: true);
            await Repository<Question>().InsertAsync(manual, autoSave: true);
            await Repository<QuestionSource>().InsertAsync(new QuestionSource(Guid.NewGuid(), null, source.Id,
                chunk.KnowledgeItemId, chunk.KnowledgeVersionId, chunk.Id, chunk.SourceTitle, null, null, null), autoSave: true);
            await Repository<QuestionSource>().InsertAsync(new QuestionSource(Guid.NewGuid(), null, manual.Id,
                null, null, null, null, null, null, "历史企业人工来源"), autoSave: true);
            return (source, manual);
        });
        using (ChangeUser(_other))
        {
            (await _questions.GetAsync(sourceQuestion.Id)).Id.ShouldBe(sourceQuestion.Id);
            (await _questions.GetAsync(manualQuestion.Id)).Id.ShouldBe(manualQuestion.Id);
        }
        await WithUnitOfWorkAsync(async () =>
        {
            await Repository<KnowledgePermissionScope>().DeleteAsync(x => x.KnowledgeItemId == chunk.KnowledgeItemId, autoSave: true);
            await Repository<KnowledgePermissionScope>().InsertAsync(new KnowledgePermissionScope(
                Guid.NewGuid(), null, chunk.KnowledgeItemId, KnowledgePermissionScopeType.User, _owner), autoSave: true);
        });
        using (ChangeUser(_other))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _questions.GetAsync(sourceQuestion.Id));
            (await _questions.GetAsync(manualQuestion.Id)).Id.ShouldBe(manualQuestion.Id);
        }
        using (ChangeUser(_owner))
        {
            (await _questions.GetAsync(sourceQuestion.Id)).Id.ShouldBe(sourceQuestion.Id);
        }
    }

    private IRepository<T, Guid> Repository<T>() where T : class, Volo.Abp.Domain.Entities.IEntity<Guid>
        => GetRequiredService<IRepository<T, Guid>>();

    private IDisposable ChangeUser(Guid userId)
        => GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
            new Claim(AbpClaimTypes.UserName, $"teacher-{userId:N}"),
            new Claim(AbpClaimTypes.Role, "admin")
        ], "Test")));

    private Task EnsureAccountAsync(Guid id) => WithUnitOfWorkAsync(async () =>
    {
        if (await Repository<IdentityUser>().FindAsync(id) == null)
            await Repository<IdentityUser>().InsertAsync(new IdentityUser(id, $"personal-{id:N}", $"{id:N}@example.invalid"), autoSave: true);
    });

    private async Task<KnowledgeChunk> CreateChunkAsync(Guid? owner)
    {
        var versionId = await WithUnitOfWorkAsync(async () =>
        {
            if (owner.HasValue && await Repository<KnowledgeLibrary>().FindAsync(owner.Value) == null)
            {
                await Repository<KnowledgeLibrary>().InsertAsync(new KnowledgeLibrary(owner.Value, null), autoSave: true);
            }
            var item = new KnowledgeItem(Guid.NewGuid(), null, $"教学经验-{Guid.NewGuid():N}", null, null, owner, owner);
            var version = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1, item.Title, "课堂提问应先明确知识点，然后结合教材设计层次递进的问题。");
            version.Publish(DateTime.UtcNow);
            item.SetCurrentPublishedVersion(version.Id);
            await Repository<KnowledgeItem>().InsertAsync(item, autoSave: true);
            await Repository<KnowledgeVersion>().InsertAsync(version, autoSave: true);
            // Existing All scopes must never widen a private library.
            await Repository<KnowledgePermissionScope>().InsertAsync(new KnowledgePermissionScope(
                Guid.NewGuid(), null, item.Id, KnowledgePermissionScopeType.All), autoSave: true);
            return version.Id;
        });
        (await GetRequiredService<IKnowledgeIngestionPipeline>().IngestAsync(versionId)).Succeeded.ShouldBeTrue();
        return await WithUnitOfWorkAsync(async () => (await Repository<KnowledgeChunk>().GetQueryableAsync())
            .Single(x => x.KnowledgeVersionId == versionId && x.IsActive));
    }

    private static SaveQuestionDraftDto Draft(KnowledgeChunk? chunk = null)
        => new()
        {
            Type = QuestionType.SingleChoice,
            Difficulty = QuestionDifficulty.Normal,
            Title = "个人教学问题",
            Stem = "课堂提问的第一步是什么？",
            Analysis = "先明确知识点。",
            Options =
            {
                new CreateUpdateQuestionOptionDto { OptionKey = "A", Content = "明确知识点", DisplayOrder = 1 },
                new CreateUpdateQuestionOptionDto { OptionKey = "B", Content = "随机提问", DisplayOrder = 2 }
            },
            Answers = { new CreateUpdateQuestionAnswerDto { Value = "A", DisplayOrder = 1 } },
            Sources =
            {
                chunk == null
                    ? new CreateUpdateQuestionSourceDto { ManualSourceDescription = "本人课堂经验整理" }
                    : new CreateUpdateQuestionSourceDto { KnowledgeItemId = chunk.KnowledgeItemId, KnowledgeVersionId = chunk.KnowledgeVersionId, ChunkId = chunk.Id }
            }
        };

    private static string QuestionResponse(Guid chunkId)
        => JsonSerializer.Serialize(new
        {
            questions = new[] { new
            {
                type = "SingleChoice", difficulty = "Normal", title = "个人教学问题", stem = "课堂提问的第一步是什么？",
                options = new[]
                {
                    new { optionKey = "A", content = "明确知识点", displayOrder = 1 },
                    new { optionKey = "B", content = "随机提问", displayOrder = 2 }
                },
                answers = new[] { "A" }, analysis = "依据教学经验，先明确知识点。", knowledgePoints = new[] { "课堂提问" },
                citations = new[] { new { chunkId } }
            } }
        });

    private static VectorSearchHit ToHit(KnowledgeChunk chunk)
        => new()
        {
            ChunkId = chunk.Id, KnowledgeItemId = chunk.KnowledgeItemId, KnowledgeVersionId = chunk.KnowledgeVersionId,
            Content = "Untrusted vector payload", Score = 0.95,
            Payload = new KnowledgeVectorPayload
            {
                TenantId = chunk.TenantId, KnowledgeItemId = chunk.KnowledgeItemId, KnowledgeVersionId = chunk.KnowledgeVersionId,
                ChunkId = chunk.Id, IsPublished = true, Visibility = "All", SourceTitle = chunk.SourceTitle
            }
        };
}
