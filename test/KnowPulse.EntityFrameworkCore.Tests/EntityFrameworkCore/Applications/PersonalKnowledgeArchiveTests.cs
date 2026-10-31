using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using KnowPulse.AiQuestioning;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class PersonalKnowledgeArchiveTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IKnowledgeItemAppService _items;
    private readonly IKnowledgeSchoolSharingAppService _sharing;
    private readonly IQuestionWorkflowAppService _workflow;
    private readonly IExamAppService _exams;
    private readonly TestKnowledgeVectorStore _vectors;
    private readonly TestRagChatClient _chat;

    public PersonalKnowledgeArchiveTests()
    {
        _items = GetRequiredService<IKnowledgeItemAppService>();
        _sharing = GetRequiredService<IKnowledgeSchoolSharingAppService>();
        _workflow = GetRequiredService<IQuestionWorkflowAppService>();
        _exams = GetRequiredService<IExamAppService>();
        _vectors = GetRequiredService<TestKnowledgeVectorStore>();
        _chat = GetRequiredService<TestRagChatClient>();
        _vectors.Reset();
        _chat.Reset();
        GetRequiredService<TestKnowledgeEmbeddingClient>().Reset();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Archive_Should_Preserve_Private_History_And_Frozen_Papers_While_Stopping_New_Shared_Use(bool publishPrivateRevision)
    {
        var fixture = await CreateFixtureAsync();
        QuestionDto question;
        ExamPlanDto plan;
        ExamAttemptDto attempt;
        using (ChangeUser(fixture.Reader))
        {
            question = await _workflow.CreateManualDraftAsync(Draft(fixture));
            await _workflow.SubmitAsync(question.Id);
            await GetRequiredService<IQuestionAppService>().ApproveAsync(question.Id, new ReviewQuestionDto());
            plan = await _exams.CreatePlanAsync(Plan(question.Id));
            attempt = await _exams.StartAttemptAsync(plan.Id);
            await _exams.SaveAnswersAsync(attempt.Id, new SaveExamAnswersDto
            {
                Answers = { new SaveExamAnswerItemDto { PaperQuestionId = attempt.Paper!.Questions.Single().Id, Values = { "true" } } }
            });
            (await _exams.SubmitAttemptAsync(attempt.Id, new SubmitExamAttemptDto())).Status.ShouldBe(ExamAttemptStatus.Scored);
        }

        Guid? privateRevisionId = null;
        using (ChangeUser(fixture.Owner))
        {
            if (publishPrivateRevision)
            {
                var revision = await _items.CreateVersionAsync(fixture.ItemId, new CreateKnowledgeVersionDto
                {
                    Title = "私人新版", ContentText = "未共享的后续教学反思。"
                });
                await _items.PublishVersionAsync(fixture.ItemId, revision.Id);
                (await GetRequiredService<IKnowledgeIngestionPipeline>().IngestAsync(revision.Id)).Succeeded.ShouldBeTrue();
                privateRevisionId = revision.Id;
            }

            // 归档只结束选定版本的索引事实，包括尚未完成的任务。
            await WithUnitOfWorkAsync(async () =>
            {
                foreach (var processing in new[] { false, true })
                {
                    var job = new KnowledgeIndexJob(Guid.NewGuid(), null, fixture.ItemId, fixture.VersionId,
                        KnowledgeIndexJobTriggerType.ManualRebuild, requestedByUserId: fixture.Owner);
                    if (processing) job.Start(DateTime.UtcNow);
                    await Repo<KnowledgeIndexJob>().InsertAsync(job, autoSave: true);
                }
            });
            var archived = await _items.ArchiveVersionAsync(fixture.ItemId, fixture.VersionId);
            archived.Status.ShouldBe(KnowledgeVersionStatus.Archived);
            archived.IsCurrentPublished.ShouldBeFalse();
            archived.ArchivedTime.ShouldNotBeNull();
            (await _items.ArchiveVersionAsync(fixture.ItemId, fixture.VersionId)).ArchivedTime.ShouldBe(archived.ArchivedTime);
            (await _items.GetAsync(fixture.ItemId)).CurrentPublishedVersionId.ShouldBe(privateRevisionId);

            var retained = (await _items.GetVersionsAsync(fixture.ItemId)).Items.Single(x => x.Id == fixture.VersionId);
            retained.ContentText.ShouldBe("教学积累原文。");
            (await _items.GetAttachmentsAsync(fixture.ItemId, fixture.VersionId)).Items.Single().Id.ShouldBe(fixture.AttachmentId);
            using (var content = await _items.DownloadAttachmentAsync(fixture.ItemId, fixture.VersionId, fixture.AttachmentId))
            using (var reader = new StreamReader(content.GetStream()))
                (await reader.ReadToEndAsync()).ShouldBe("原始教学附件。");
            (await Should.ThrowAsync<BusinessException>(() => _items.PublishVersionAsync(fixture.ItemId, fixture.VersionId)))
                .Code.ShouldBe(KnowPulseDomainErrorCodes.KnowledgeVersionAlreadyArchived);
            (await Should.ThrowAsync<BusinessException>(() => UploadAsync(fixture.ItemId, fixture.VersionId)))
                .Code.ShouldBe(KnowPulseDomainErrorCodes.PersonalKnowledgeAttachmentRequiresDraft);
            (await Should.ThrowAsync<BusinessException>(() => _items.DeleteAttachmentAsync(fixture.ItemId, fixture.VersionId, fixture.AttachmentId)))
                .Code.ShouldBe(KnowPulseDomainErrorCodes.PersonalKnowledgeAttachmentRequiresDraft);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.CreateManualDraftAsync(Draft(fixture)));
            (await _sharing.GetMySharesAsync(new GetSchoolKnowledgeShareListInput())).Items.Single().Id.ShouldBe(fixture.ShareId);
        }

        await WithUnitOfWorkAsync(async () =>
        {
            var archivedJobs = await Repo<KnowledgeIndexJob>().GetListAsync(x => x.KnowledgeVersionId == fixture.VersionId);
            archivedJobs.ShouldNotBeEmpty();
            archivedJobs.ShouldAllBe(x => x.Status == KnowledgeIndexJobStatus.Outdated);
            var archivedChunks = await Repo<KnowledgeChunk>().GetListAsync(x => x.KnowledgeVersionId == fixture.VersionId);
            archivedChunks.ShouldNotBeEmpty();
            archivedChunks.ShouldAllBe(x => !x.IsActive && !x.IsPublishedSnapshot);
            (await Repo<KnowledgeAttachment>().GetAsync(fixture.AttachmentId)).IsDeleted.ShouldBeFalse();
            (await Repo<Question>().GetAsync(question.Id)).IsDeleted.ShouldBeFalse();
            (await Repo<ExamAttempt>().GetAsync(attempt.Id)).Status.ShouldBe(ExamAttemptStatus.Scored);
            (await Repo<ExamPaper>().GetAsync(plan.PaperId)).PersonalOwnerUserId.ShouldBe(fixture.Reader);
            if (privateRevisionId.HasValue)
            {
                var revision = await Repo<KnowledgeVersion>().GetAsync(privateRevisionId.Value);
                revision.Status.ShouldBe(KnowledgeVersionStatus.Published);
                revision.IsCurrentPublished.ShouldBeTrue();
                (await Repo<KnowledgeIndexJob>().GetListAsync(x => x.KnowledgeVersionId == privateRevisionId.Value))
                    .ShouldContain(x => x.Status == KnowledgeIndexJobStatus.Indexed);
                (await Repo<KnowledgeChunk>().GetListAsync(x => x.KnowledgeVersionId == privateRevisionId.Value))
                    .ShouldContain(x => x.IsActive && x.IsPublishedSnapshot);
            }
        });

        // 即使外部向量库仍返回归档前的命中，也不能送给模型或用于新的题目。
        _vectors.SearchResults.Add(new VectorSearchHit
        {
            ChunkId = fixture.Chunk.Id, KnowledgeItemId = fixture.ItemId, KnowledgeVersionId = fixture.VersionId,
            Content = "stale archive content", Score = 0.95,
            Payload = new KnowledgeVectorPayload
            {
                ChunkId = fixture.Chunk.Id, KnowledgeItemId = fixture.ItemId, KnowledgeVersionId = fixture.VersionId,
                IsPublished = true, Visibility = "All"
            }
        });
        using (ChangeUser(fixture.Reader))
        {
            (await _sharing.GetSchoolSharesAsync(fixture.SchoolId, new GetSchoolKnowledgeShareListInput())).Items.ShouldBeEmpty();
            await Should.ThrowAsync<AbpAuthorizationException>(() => _sharing.GetDetailAsync(fixture.ShareId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _sharing.DownloadAttachmentAsync(fixture.ShareId, fixture.AttachmentId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.CreateManualDraftAsync(Draft(fixture)));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.CreatePlanAsync(Plan(question.Id)));
            (await GetRequiredService<IKnowledgeRetriever>().SearchAsync(new RagSearchRequest
            {
                Query = "教学积累", Scope = KnowledgeSearchScope.SchoolShared, SchoolId = fixture.SchoolId
            })).ShouldBeEmpty();
            var generation = await GetRequiredService<IAiQuestionGenerationAppService>().CreateAsync(new CreateAiQuestionGenerationTaskDto
            {
                KnowledgeScope = "教学积累", Scope = KnowledgeSearchScope.SchoolShared, SchoolId = fixture.SchoolId
            });
            generation.Status.ShouldBe(AiQuestionGenerationTaskStatus.Pending);
            await GetRequiredService<AiQuestionGenerationProcessor>().ProcessAsync(generation.Id);
            generation = await GetRequiredService<IAiQuestionGenerationAppService>().GetAsync(generation.Id);
            generation.Status.ShouldBe(AiQuestionGenerationTaskStatus.Failed);
            _chat.CallCount.ShouldBe(0);
            (await _exams.GetPlanAsync(plan.Id)).Paper!.Questions.Single().SourceSnapshot.ShouldBe("历史教学资料");
            (await _exams.GradeAttemptAsync(attempt.Id)).ObtainedScore.ShouldBe(10);
            (await _exams.GetScoreListAsync(new GetExamScoreListInput { PlanId = plan.Id })).Items.Single().ObtainedScore.ShouldBe(10);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _items.GetVersionsAsync(fixture.ItemId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _items.DownloadAttachmentAsync(fixture.ItemId, fixture.VersionId, fixture.AttachmentId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _items.ArchiveVersionAsync(fixture.ItemId, fixture.VersionId));
        }
        using (ChangeUser(fixture.Owner))
        {
            (await GetRequiredService<IKnowledgeRetriever>().SearchAsync(new RagSearchRequest
            {
                Query = "教学积累", Scope = KnowledgeSearchScope.PersonalLibrary
            })).ShouldBeEmpty();
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetPlanAsync(plan.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GradeAttemptAsync(attempt.Id));
        }
    }

    private async Task<Fixture> CreateFixtureAsync()
    {
        var owner = Guid.NewGuid();
        var reader = Guid.NewGuid();
        var schoolId = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            foreach (var id in new[] { owner, reader })
                await Repo<IdentityUser>().InsertAsync(new IdentityUser(id, $"archive-{id:N}", $"{id:N}@example.invalid"), autoSave: true);
            await Repo<School>().InsertAsync(new School(schoolId, null, $"S{Guid.NewGuid():N}", "归档验收初中"), autoSave: true);
            foreach (var id in new[] { owner, reader })
                await Repo<SchoolMembership>().InsertAsync(new SchoolMembership(Guid.NewGuid(), null, schoolId, id,
                    SchoolMemberRole.Teacher, DateTime.UtcNow.AddDays(-1)), autoSave: true);
            await Repo<KnowledgeLibrary>().InsertAsync(new KnowledgeLibrary(owner, null), autoSave: true);
        });
        using var user = ChangeUser(owner);
        var item = await _items.CreateAsync(new CreateKnowledgeItemDto { Title = "历史教学资料", LibraryId = owner });
        var version = await _items.CreateVersionAsync(item.Id, new CreateKnowledgeVersionDto
        {
            Title = "历史教学资料", ContentText = "教学积累原文。"
        });
        var attachment = await UploadAsync(item.Id, version.Id);
        await _items.PublishVersionAsync(item.Id, version.Id);
        (await GetRequiredService<IKnowledgeIngestionPipeline>().IngestAsync(version.Id)).Succeeded.ShouldBeTrue();
        var share = await _sharing.ShareVersionAsync(new CreateSchoolKnowledgeShareDto
        {
            IdempotencyKey = Guid.NewGuid(), KnowledgeItemId = item.Id, KnowledgeVersionId = version.Id, SchoolId = schoolId,
            Uses = SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve |
                SchoolKnowledgeShareUses.GenerateQuestions | SchoolKnowledgeShareUses.Download
        });
        var chunk = await WithUnitOfWorkAsync(async () => (await Repo<KnowledgeChunk>()
            .GetListAsync(x => x.KnowledgeVersionId == version.Id && x.IsActive)).First());
        return new Fixture(owner, reader, schoolId, item.Id, version.Id, attachment.Id, share.Id, chunk);
    }

    private Task<KnowledgeAttachmentDto> UploadAsync(Guid itemId, Guid versionId)
        => _items.UploadAttachmentAsync(itemId, versionId, new RemoteStreamContent(
            new MemoryStream(Encoding.UTF8.GetBytes("原始教学附件。")), "教学反思.txt", "text/plain"));

    private static SaveQuestionDraftDto Draft(Fixture fixture) => new()
    {
        Type = QuestionType.TrueFalse, Difficulty = QuestionDifficulty.Normal, Title = "教学练习", Stem = "知识积累可用于教学。",
        Answers = { new CreateUpdateQuestionAnswerDto { Value = "true", DisplayOrder = 1 } },
        Sources = { new CreateUpdateQuestionSourceDto
        {
            KnowledgeItemId = fixture.ItemId, KnowledgeVersionId = fixture.VersionId, ChunkId = fixture.Chunk.Id
        } }
    };

    private static CreateExamPlanDto Plan(Guid questionId) => new()
    {
        Name = "归档前教学考试", StartTime = DateTime.UtcNow.AddMinutes(-5), EndTime = DateTime.UtcNow.AddHours(1),
        DurationMinutes = 30, PassingScore = 5, ResultVisibility = ExamResultVisibility.FullReview,
        Paper = new GenerateExamPaperDto
        {
            Title = "历史冻结试卷", BuildMode = ExamPaperBuildMode.Fixed, FixedQuestionIds = { questionId },
            Rules = { new ExamPaperRuleDto { ScorePerQuestion = 10 } }
        },
        Assignments = { new ExamAssignmentInputDto { ScopeType = ExamAssignmentScopeType.All } }
    };

    private IRepository<T, Guid> Repo<T>() where T : class, Volo.Abp.Domain.Entities.IEntity<Guid>
        => GetRequiredService<IRepository<T, Guid>>();

    private IDisposable ChangeUser(Guid id) => GetRequiredService<ICurrentPrincipalAccessor>().Change(
        new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, id.ToString()), new Claim(AbpClaimTypes.UserName, $"archive-{id:N}"),
            new Claim(AbpClaimTypes.Role, "admin")
        }, "Test")));

    private sealed record Fixture(Guid Owner, Guid Reader, Guid SchoolId, Guid ItemId, Guid VersionId,
        Guid AttachmentId, Guid ShareId, KnowledgeChunk Chunk);
}
