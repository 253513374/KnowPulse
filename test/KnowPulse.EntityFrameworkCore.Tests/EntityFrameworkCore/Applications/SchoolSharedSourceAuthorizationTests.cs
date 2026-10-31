using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.AiQuestioning;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using KnowPulse.Teaching;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class SchoolSharedSourceAuthorizationTests : KnowPulseEntityFrameworkCoreTestBase
{
    private const SchoolKnowledgeShareUses GenerationUses = SchoolKnowledgeShareUses.Read |
        SchoolKnowledgeShareUses.Retrieve | SchoolKnowledgeShareUses.GenerateQuestions;

    private readonly IQuestionAppService _questions;
    private readonly IQuestionWorkflowAppService _workflow;
    private readonly IExamAppService _exams;
    private readonly IKnowledgeSchoolSharingAppService _sharing;
    private readonly KnowledgeSourceAccessGuard _guard;

    public SchoolSharedSourceAuthorizationTests()
    {
        _questions = GetRequiredService<IQuestionAppService>();
        _workflow = GetRequiredService<IQuestionWorkflowAppService>();
        _exams = GetRequiredService<IExamAppService>();
        _sharing = GetRequiredService<IKnowledgeSchoolSharingAppService>();
        _guard = GetRequiredService<KnowledgeSourceAccessGuard>();
        GetRequiredService<TestKnowledgeEmbeddingClient>().Reset();
        GetRequiredService<TestKnowledgeVectorStore>().Reset();
        GetRequiredService<TestRagChatClient>().Reset();
    }

    [Fact]
    public async Task Shared_Old_Version_Should_Create_Borrowers_Question_And_Paper_Without_Private_Revision_Metadata()
    {
        var fixture = await CreateFixtureAsync();
        await PublishPrivateRevisionAsync(fixture);
        QuestionDto question;
        ExamPlanDto plan;
        using (ChangeUser(fixture.Reader))
        {
            // 客户端即使提交条目当前私有标题，也只能冻结实际获授权的共享版本标题。
            question = await _workflow.CreateManualDraftAsync(Draft(fixture));
            question.Sources.Single().SourceTitle.ShouldBe("共享V1标题");
            await _workflow.SubmitAsync(question.Id);
            question = await _questions.ApproveAsync(question.Id, new ReviewQuestionDto { Comment = "确认共享来源" });
            plan = await CreatePlanAsync(question.Id);
            plan.Paper!.Questions.Single().SourceSnapshot.ShouldBe("共享V1标题");
            JsonSerializer.Serialize(question).ShouldNotContain("PRIVATE-V2");
            JsonSerializer.Serialize(plan).ShouldNotContain("PRIVATE-V2");
        }
        await WithUnitOfWorkAsync(async () =>
        {
            (await Repository<Question>().GetAsync(question.Id)).PersonalOwnerUserId.ShouldBe(fixture.Reader);
            (await Repository<ExamPaper>().GetAsync(plan.PaperId)).PersonalOwnerUserId.ShouldBe(fixture.Reader);
            (await Repository<KnowledgeVersion>().GetAsync(fixture.VersionId)).IsCurrentPublished.ShouldBeFalse();
        });
        using (ChangeUser(fixture.Owner))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _questions.GetAsync(question.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetPlanAsync(plan.Id));
        }
        using (ChangeUser(fixture.Outsider))
            await Should.ThrowAsync<AbpAuthorizationException>(() => _questions.GetAsync(question.Id));
    }

    [Fact]
    public async Task Shared_Generation_And_Adoption_Should_Retain_Current_Teacher_As_Personal_Owner()
    {
        var fixture = await CreateFixtureAsync();
        await PublishPrivateRevisionAsync(fixture);
        ConfigureSharedGeneration(fixture);
        using (ChangeUser(fixture.Reader))
        {
            var task = await GetRequiredService<IAiQuestionGenerationAppService>().CreateAsync(
                new CreateAiQuestionGenerationTaskDto
                {
                    KnowledgeScope = "教材知识点", Type = QuestionType.TrueFalse, Scope = KnowledgeSearchScope.SchoolShared
                });
            task.Status.ShouldBe(AiQuestionGenerationTaskStatus.Pending);
            await GetRequiredService<AiQuestionGenerationProcessor>().ProcessAsync(task.Id);
            task = await GetRequiredService<IAiQuestionGenerationAppService>().GetAsync(task.Id);
            task.Status.ShouldBe(AiQuestionGenerationTaskStatus.Generated);
            var question = await _workflow.AdoptAiCandidateAsync(task.Candidates.Single().Id);
            question.CreationSource.ShouldBe(QuestionCreationSource.AiGenerated);
            question.Sources.Single().SourceTitle.ShouldBe("共享V1标题");
            await WithUnitOfWorkAsync(async () =>
            {
                (await Repository<AiQuestionGenerationTask>().GetAsync(task.Id)).PersonalOwnerUserId.ShouldBe(fixture.Reader);
                (await Repository<Question>().GetAsync(question.Id)).PersonalOwnerUserId.ShouldBe(fixture.Reader);
            });
            JsonSerializer.Serialize(task).ShouldNotContain("PRIVATE-V2");
            using (ChangeUser(fixture.Owner))
                await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.AdoptAiCandidateAsync(task.Candidates.Single().Id));
        }
    }

    private void ConfigureSharedGeneration(Fixture fixture)
    {
        var vectors = GetRequiredService<TestKnowledgeVectorStore>();
        vectors.SearchResults.Add(new VectorSearchHit
        {
            ChunkId = fixture.Chunk.Id, KnowledgeItemId = fixture.ItemId, KnowledgeVersionId = fixture.VersionId,
            Score = 0.95, Content = "Untrusted vector content",
            Payload = new KnowledgeVectorPayload
            {
                ChunkId = fixture.Chunk.Id, KnowledgeItemId = fixture.ItemId, KnowledgeVersionId = fixture.VersionId,
                IsPublished = true, Visibility = "All", SourceTitle = "PRIVATE-V2-VECTOR-TITLE"
            }
        });
        GetRequiredService<TestRagChatClient>().ResponseContent = JsonSerializer.Serialize(new
        {
            questions = new[] { new
            {
                type = "TrueFalse", difficulty = "Normal", title = "教材知识点", stem = "先明确知识点再设计练习。",
                options = Array.Empty<object>(), answers = new[] { "true" }, analysis = "共享V1正文提供依据。",
                knowledgePoints = new[] { "教学目标" }, citations = new[] { new { chunkId = fixture.Chunk.Id } }
            } }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clearing_Or_Replacing_Shared_Question_Sources_Should_Not_Remove_Authorization_Or_Duplicate_References(bool adoptAi)
    {
        var fixture = await CreateFixtureAsync();
        await PublishPrivateRevisionAsync(fixture);
        if (adoptAi) ConfigureSharedGeneration(fixture);
        var questions = new List<QuestionDto>();
        using (ChangeUser(fixture.Reader))
        {
            for (var stage = 0; stage < 3; stage++)
            {
                QuestionDto question;
                if (adoptAi)
                {
                    var task = await GetRequiredService<IAiQuestionGenerationAppService>().CreateAsync(
                        new CreateAiQuestionGenerationTaskDto
                        {
                            KnowledgeScope = "教材知识点", Type = QuestionType.TrueFalse, Scope = KnowledgeSearchScope.SchoolShared
                        });
                    task.Status.ShouldBe(AiQuestionGenerationTaskStatus.Pending);
                    await GetRequiredService<AiQuestionGenerationProcessor>().ProcessAsync(task.Id);
                    task = await GetRequiredService<IAiQuestionGenerationAppService>().GetAsync(task.Id);
                    task.Status.ShouldBe(AiQuestionGenerationTaskStatus.Generated);
                    question = await _workflow.AdoptAiCandidateAsync(task.Candidates.Single().Id);
                }
                else
                {
                    question = await _workflow.CreateManualDraftAsync(Draft(fixture));
                }

                var originalSourceId = question.Sources.Single().Id;
                for (var repeat = 0; repeat < 2; repeat++)
                {
                    question = await _workflow.SaveDraftAsync(question.Id, Draft(fixture));
                    question.Sources.Single().Id.ShouldBe(originalSourceId);
                    question.Sources.Single().SourceTitle.ShouldBe("共享V1标题");
                }

                var input = Draft(fixture);
                input.Sources.Clear();
                input.Sources.Add(new CreateUpdateQuestionSourceDto { ManualSourceDescription = "可编辑的来源说明" });
                question = await _workflow.SaveDraftAsync(question.Id, input);
                question.Sources.Count.ShouldBe(2);
                question.Sources.Single(x => x.KnowledgeVersionId.HasValue).Id.ShouldBe(originalSourceId);
                question.Sources.Single(x => !x.KnowledgeVersionId.HasValue).ManualSourceDescription.ShouldBe("可编辑的来源说明");

                input.Sources.Clear();
                question = await _workflow.SaveDraftAsync(question.Id, input);
                var retained = question.Sources.Single();
                retained.Id.ShouldBe(originalSourceId);
                retained.KnowledgeItemId.ShouldBe(fixture.ItemId);
                retained.KnowledgeVersionId.ShouldBe(fixture.VersionId);
                retained.ChunkId.ShouldBe(fixture.Chunk.Id);
                retained.SourceTitle.ShouldBe("共享V1标题");
                if (stage >= 1) question = await _workflow.SubmitAsync(question.Id);
                if (stage == 2) question = await _questions.ApproveAsync(question.Id, new ReviewQuestionDto { Comment = "授权有效时审核通过" });
                questions.Add(question);
            }
        }
        using (ChangeUser(fixture.Owner)) await _sharing.RevokeAsync(fixture.ShareId);
        using (ChangeUser(fixture.Reader))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.SubmitAsync(questions[0].Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _questions.ApproveAsync(questions[1].Id, new ReviewQuestionDto()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => CreatePlanAsync(questions[2].Id));
        }
        await WithUnitOfWorkAsync(async () =>
        {
            foreach (var question in questions)
            {
                var source = await Repository<QuestionSource>().GetAsync(x => x.QuestionId == question.Id);
                source.KnowledgeItemId.ShouldBe(fixture.ItemId);
                source.KnowledgeVersionId.ShouldBe(fixture.VersionId);
                source.ChunkId.ShouldBe(fixture.Chunk.Id);
            }
        });
    }

    [Fact]
    public async Task Missing_Original_Source_Should_Not_Let_Save_Remove_Personal_Authorization()
    {
        var fixture = await CreateFixtureAsync();
        QuestionDto question;
        using (ChangeUser(fixture.Reader)) question = await _workflow.CreateManualDraftAsync(Draft(fixture));
        await WithUnitOfWorkAsync(() => Repository<KnowledgeItem>().DeleteAsync(fixture.ItemId, autoSave: true));
        using (ChangeUser(fixture.Reader))
        {
            var input = Draft(fixture);
            input.Sources.Clear();
            await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.SaveDraftAsync(question.Id, input));
        }
        await WithUnitOfWorkAsync(async () =>
        {
            (await Repository<QuestionSource>().GetAsync(x => x.QuestionId == question.Id)).KnowledgeVersionId.ShouldBe(fixture.VersionId);
            (await Repository<Question>().GetAsync(question.Id)).PersonalOwnerUserId.ShouldBe(fixture.Reader);
        });
    }

    [Fact]
    public async Task Legacy_Unmarked_Question_Should_Retain_Existing_Personal_Source_And_Record_Its_Current_Owner()
    {
        var fixture = await CreateFixtureAsync();
        var questionId = await WithUnitOfWorkAsync(async () =>
        {
            var question = new Question(Guid.NewGuid(), null, QuestionType.TrueFalse, QuestionDifficulty.Normal,
                QuestionCreationSource.Manual, "旧未标记题", "教材练习");
            await Repository<Question>().InsertAsync(question, autoSave: true);
            await Repository<QuestionSource>().InsertAsync(new QuestionSource(Guid.NewGuid(), null, question.Id,
                fixture.ItemId, fixture.VersionId, fixture.Chunk.Id, "共享V1标题", null, null, null), autoSave: true);
            return question.Id;
        });
        using (ChangeUser(fixture.Reader))
        {
            var input = Draft(fixture);
            input.Sources.Clear();
            var saved = await _workflow.SaveDraftAsync(questionId, input);
            saved.Sources.Single().KnowledgeVersionId.ShouldBe(fixture.VersionId);
            (await WithUnitOfWorkAsync(() => Repository<Question>().GetAsync(questionId))).PersonalOwnerUserId.ShouldBe(fixture.Reader);
        }
        using (ChangeUser(fixture.Owner)) await _sharing.RevokeAsync(fixture.ShareId);
        using (ChangeUser(fixture.Reader))
            await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.SubmitAsync(questionId));
    }

    [Theory]
    [InlineData(SchoolKnowledgeShareUses.Read)]
    [InlineData(SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve)]
    public async Task Reading_Does_Not_Grant_New_Question_Use(SchoolKnowledgeShareUses uses)
    {
        var fixture = await CreateFixtureAsync(uses);
        using var user = ChangeUser(fixture.Reader);
        (await ReadSourceAsync(fixture)).Id.ShouldBe(fixture.ItemId);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.CreateManualDraftAsync(Draft(fixture)));
        (await WithUnitOfWorkAsync(() => Repository<Question>().CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Generation_Without_Retrieval_Should_Be_Rejected_When_Creating_Share()
    {
        var fixture = await CreateFixtureAsync(SchoolKnowledgeShareUses.Read);
        using var user = ChangeUser(fixture.Owner);
        var share = await _sharing.GetAsync(fixture.ShareId);
        (await Should.ThrowAsync<BusinessException>(() => _sharing.ShareVersionAsync(new CreateSchoolKnowledgeShareDto
        {
            IdempotencyKey = Guid.NewGuid(), KnowledgeItemId = fixture.ItemId, KnowledgeVersionId = fixture.VersionId,
            SchoolId = share.SchoolId, Uses = SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.GenerateQuestions
        }))).Code.ShouldBe(SchoolKnowledgeSharingErrorCodes.InvalidUses);
    }

    [Fact]
    public async Task Author_Should_Use_Their_Shared_Old_Version_After_A_Private_Revision_Without_Allowing_Unshared_Old_Versions()
    {
        var fixture = await CreateFixtureAsync();
        await PublishPrivateRevisionAsync(fixture);
        using var user = ChangeUser(fixture.Owner);
        // 分块单独引用也必须走实际共享版本的安全元数据出口。
        var input = Draft(fixture);
        input.Sources.Single().KnowledgeVersionId = null;
        var question = await _workflow.CreateManualDraftAsync(input);
        question.Sources.Single().SourceTitle.ShouldBe("共享V1标题");
        await _workflow.SubmitAsync(question.Id);
        await _questions.ApproveAsync(question.Id, new ReviewQuestionDto { Comment = "作者使用有效共享旧版本" });
        (await CreatePlanAsync(question.Id)).Paper!.Questions.Single().SourceSnapshot.ShouldBe("共享V1标题");
        (await WithUnitOfWorkAsync(() => Repository<Question>().GetAsync(question.Id))).PersonalOwnerUserId.ShouldBe(fixture.Owner);
        await _sharing.RevokeAsync(fixture.ShareId);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.CreateManualDraftAsync(Draft(fixture)));
        // 旧版本的私人历史查看依然可用，撤回共享并不收回作者的历史资料。
        (await ReadSourceAsync(fixture)).Id.ShouldBe(fixture.ItemId);
    }

    [Fact]
    public async Task Item_Only_Private_Version_Mismatched_References_And_Outsider_Should_Not_Use_Shared_Authorization()
    {
        var fixture = await CreateFixtureAsync();
        var privateVersion = await PublishPrivateRevisionAsync(fixture);
        using (ChangeUser(fixture.Reader))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => WithUnitOfWorkAsync(() =>
                _guard.EnsureSourceAsync(fixture.ItemId, null, null)));
            await Should.ThrowAsync<AbpAuthorizationException>(() => WithUnitOfWorkAsync(() =>
                _guard.EnsureSourceAsync(fixture.ItemId, privateVersion, null)));
            await Should.ThrowAsync<AbpAuthorizationException>(() => WithUnitOfWorkAsync(() =>
                _guard.EnsureSourceAsync(fixture.ItemId, privateVersion, fixture.Chunk.Id)));
            await Should.ThrowAsync<AbpAuthorizationException>(() => WithUnitOfWorkAsync(() =>
                _guard.EnsureSourceAsync(Guid.NewGuid(), fixture.VersionId, fixture.Chunk.Id)));
            // 分块引用可确定被共享的精确版本。
            (await WithUnitOfWorkAsync(() => _guard.EnsureSourceAsync(null, null, fixture.Chunk.Id, true)))!
                .Id.ShouldBe(fixture.ItemId);
        }
        using (ChangeUser(fixture.Outsider))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => ReadSourceAsync(fixture));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.CreateManualDraftAsync(Draft(fixture)));
        }
    }

    [Fact]
    public async Task New_Use_Should_Require_Indexed_Job_For_The_Exact_Active_Chunk()
    {
        var fixture = await CreateFixtureAsync();
        await WithUnitOfWorkAsync(async () =>
        {
            var job = await Repository<KnowledgeIndexJob>().GetAsync(fixture.Chunk.KnowledgeIndexJobId);
            job.MarkOutdated();
            await Repository<KnowledgeIndexJob>().UpdateAsync(job, autoSave: true);
        });
        using var user = ChangeUser(fixture.Reader);
        (await ReadSourceAsync(fixture)).Id.ShouldBe(fixture.ItemId);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.CreateManualDraftAsync(Draft(fixture)));
    }

    [Fact]
    public async Task Revocation_Should_Stop_New_Use_While_Borrower_Can_Read_Their_Frozen_Historical_Paper()
    {
        var fixture = await CreateFixtureAsync();
        QuestionDto question;
        ExamPlanDto plan;
        using (ChangeUser(fixture.Reader))
        {
            question = await _workflow.CreateManualDraftAsync(Draft(fixture));
            await _workflow.SubmitAsync(question.Id);
            await _questions.ApproveAsync(question.Id, new ReviewQuestionDto { Comment = "共享授权有效" });
            plan = await CreatePlanAsync(question.Id);
        }
        using (ChangeUser(fixture.Owner)) await _sharing.RevokeAsync(fixture.ShareId);
        using (ChangeUser(fixture.Reader))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => ReadSourceAsync(fixture));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _questions.GetAsync(question.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.CreateManualDraftAsync(Draft(fixture)));
            await Should.ThrowAsync<AbpAuthorizationException>(() => CreatePlanAsync(question.Id));
            var historical = await _exams.GetPlanAsync(plan.Id);
            historical.Paper!.Questions.Single().SourceSnapshot.ShouldBe("共享V1标题");
        }
        using (ChangeUser(fixture.Owner))
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetPlanAsync(plan.Id));
    }

    private async Task<Fixture> CreateFixtureAsync(SchoolKnowledgeShareUses uses = GenerationUses)
    {
        var seed = await WithUnitOfWorkAsync(async () =>
        {
            var owner = Guid.NewGuid();
            var reader = Guid.NewGuid();
            var outsider = Guid.NewGuid();
            foreach (var id in new[] { owner, reader, outsider })
                await Repository<IdentityUser>().InsertAsync(new IdentityUser(id, $"shared-source-{id:N}", $"{id:N}@example.invalid"), autoSave: true);
            var school = new School(Guid.NewGuid(), null, $"S{Guid.NewGuid():N}", "共享来源测试初中");
            await Repository<School>().InsertAsync(school, autoSave: true);
            foreach (var id in new[] { owner, reader })
                await Repository<SchoolMembership>().InsertAsync(new SchoolMembership(Guid.NewGuid(), null,
                    school.Id, id, SchoolMemberRole.Teacher, DateTime.UtcNow.AddDays(-1)), autoSave: true);
            await Repository<KnowledgeLibrary>().InsertAsync(new KnowledgeLibrary(owner, null), autoSave: true);
            var item = new KnowledgeItem(Guid.NewGuid(), null, "共享V1标题", summary: "旧条目摘要", libraryId: owner, ownerUserId: owner);
            var version = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1, "共享V1标题", "先明确教材知识点，再设计练习。共享V1正文提供教学依据。");
            version.Publish(DateTime.UtcNow);
            item.SetCurrentPublishedVersion(version.Id);
            await Repository<KnowledgeItem>().InsertAsync(item, autoSave: true);
            await Repository<KnowledgeVersion>().InsertAsync(version, autoSave: true);
            return (owner, reader, outsider, itemId: item.Id, versionId: version.Id, schoolId: school.Id);
        });
        SchoolKnowledgeShareDto share;
        using (ChangeUser(seed.owner))
        {
            (await GetRequiredService<IKnowledgeIngestionPipeline>().IngestAsync(seed.versionId)).Succeeded.ShouldBeTrue();
            share = await _sharing.ShareVersionAsync(new CreateSchoolKnowledgeShareDto
            {
                IdempotencyKey = Guid.NewGuid(), KnowledgeItemId = seed.itemId, KnowledgeVersionId = seed.versionId,
                SchoolId = seed.schoolId, Uses = uses, SharedSummary = "作者确认的共享摘要"
            });
        }
        var chunk = await WithUnitOfWorkAsync(async () => await Repository<KnowledgeChunk>().GetAsync(x =>
            x.KnowledgeVersionId == seed.versionId && x.IsActive));
        return new Fixture(seed.owner, seed.reader, seed.outsider, seed.itemId, seed.versionId, share.Id, chunk);
    }

    private async Task<Guid> PublishPrivateRevisionAsync(Fixture fixture)
    {
        using var user = ChangeUser(fixture.Owner);
        var service = GetRequiredService<IKnowledgeItemAppService>();
        await service.UpdateAsync(fixture.ItemId, new UpdateKnowledgeItemDto { Title = "PRIVATE-V2-TITLE", Summary = "PRIVATE-V2-SUMMARY" });
        var version = await service.CreateVersionAsync(fixture.ItemId, new CreateKnowledgeVersionDto
        {
            Title = "PRIVATE-V2-TITLE", ContentText = "PRIVATE-V2-CONTENT"
        });
        await service.PublishVersionAsync(fixture.ItemId, version.Id);
        return version.Id;
    }

    private async Task<KnowledgeItem> ReadSourceAsync(Fixture fixture)
        => (await WithUnitOfWorkAsync(() => _guard.EnsureSourceAsync(fixture.ItemId, fixture.VersionId, fixture.Chunk.Id)))!;

    private async Task<ExamPlanDto> CreatePlanAsync(Guid questionId)
    {
        var studentId = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            await Repository<IdentityUser>().InsertAsync(new IdentityUser(studentId, $"student-{studentId:N}",
                $"{studentId:N}@example.invalid"), autoSave: true);
            var relationship = new TeachingRelationship(Guid.NewGuid(), null,
                GetRequiredService<ICurrentUser>().GetId(), studentId, "共享来源测试学生");
            relationship.Confirm();
            await Repository<TeachingRelationship>().InsertAsync(relationship, autoSave: true);
        });
        return await _exams.CreatePlanAsync(new CreateExamPlanDto
        {
            Name = "共享来源历史试卷", StartTime = DateTime.UtcNow.AddMinutes(-5), EndTime = DateTime.UtcNow.AddHours(1),
            DurationMinutes = 30, PassingScore = 5, ResultVisibility = ExamResultVisibility.FullReview,
            Paper = new GenerateExamPaperDto
            {
                Title = "共享来源试卷", BuildMode = ExamPaperBuildMode.Fixed, FixedQuestionIds = { questionId },
                Rules = { new ExamPaperRuleDto { ScorePerQuestion = 10 } }
            },
            Assignments = { new ExamAssignmentInputDto { ScopeType = ExamAssignmentScopeType.User, TargetId = studentId } }
        });
    }

    private static SaveQuestionDraftDto Draft(Fixture fixture) => new()
    {
        Type = QuestionType.TrueFalse, Difficulty = QuestionDifficulty.Normal,
        Title = "教材知识点", Stem = "先明确知识点再设计练习。", Analysis = "共享V1正文提供依据。",
        Answers = { new CreateUpdateQuestionAnswerDto { Value = "true", DisplayOrder = 1 } },
        Sources = { new CreateUpdateQuestionSourceDto
        {
            KnowledgeItemId = fixture.ItemId, KnowledgeVersionId = fixture.VersionId, ChunkId = fixture.Chunk.Id,
            SourceTitle = "PRIVATE-V2-TITLE"
        } }
    };

    private IRepository<T, Guid> Repository<T>() where T : class, Volo.Abp.Domain.Entities.IEntity<Guid>
        => GetRequiredService<IRepository<T, Guid>>();

    private IDisposable ChangeUser(Guid userId) => GetRequiredService<ICurrentPrincipalAccessor>().Change(
        new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, userId.ToString()), new Claim(AbpClaimTypes.UserName, $"shared-source-{userId:N}"),
            // 管理角色声明也不替代真实学校身份或个人衍生内容所有者。
            new Claim(AbpClaimTypes.Role, "admin")
        }, "Test")));

    private sealed record Fixture(Guid Owner, Guid Reader, Guid Outsider, Guid ItemId, Guid VersionId,
        Guid ShareId, KnowledgeChunk Chunk);
}
