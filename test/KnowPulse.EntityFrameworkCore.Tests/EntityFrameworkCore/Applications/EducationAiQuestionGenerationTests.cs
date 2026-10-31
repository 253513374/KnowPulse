using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.AiQuestioning;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.Permissions;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class EducationAiQuestionGenerationTests : KnowPulseTestBase<EducationAiQuestionGenerationTestModule>
{
    private readonly IAiQuestionGenerationAppService _generation;
    private readonly AiQuestionGenerationProcessor _processor;
    private readonly TestKnowledgeVectorStore _vectors;
    private readonly EducationGenerationTestChatClient _chat;

    public EducationAiQuestionGenerationTests()
    {
        _generation = GetRequiredService<IAiQuestionGenerationAppService>();
        _processor = GetRequiredService<AiQuestionGenerationProcessor>();
        _vectors = GetRequiredService<TestKnowledgeVectorStore>();
        _chat = GetRequiredService<EducationGenerationTestChatClient>();
        GetRequiredService<IOptions<AiQuestionGenerationWorkerOptions>>().Value.MaxAttempts = 1;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Curriculum_Generation_Should_Require_A_Nonempty_Recovery_Operation(bool emptyKey)
    {
        var f = await FixtureAsync();
        using var owner = User(f.Owner);
        var input = Input(f, [f.MappingA]);
        input.IdempotencyKey = emptyKey ? Guid.Empty : null;
        await Should.ThrowAsync<ArgumentException>(() => _generation.CreateAsync(input));
        await WithUnitOfWorkAsync(async () =>
            (await GetRequiredService<IRepository<AiQuestionGenerationTask, Guid>>().GetListAsync()).ShouldBeEmpty());
        _chat.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Generation_Should_Freeze_Full_Scope_And_Keep_Declared_Subset_With_Exact_Source_Pairs()
    {
        var f = await FixtureAsync();
        using var owner = User(f.Owner);
        var source = await SourceAsync(f.Owner);
        var bindingA = await BindAsync(source, f.MappingA);
        var bindingB = await BindAsync(source, f.MappingB);
        _vectors.SearchResults.Add(Hit(source));
        _chat.Content = Response(source.Id, [f.MappingA]);
        var pending = await _generation.CreateAsync(Input(f, [f.MappingA, f.MappingB]));
        pending.Status.ShouldBe(AiQuestionGenerationTaskStatus.Pending);
        pending.CurriculumSnapshot!.Mappings.Count.ShouldBe(2);
        pending.Score.ShouldBe(5);
        pending.Purpose.ShouldBe("ChapterPractice");
        _chat.Calls.ShouldBe(0);
        await _processor.ProcessAsync(pending.Id);
        var generated = await _generation.GetAsync(pending.Id);
        generated.Status.ShouldBe(AiQuestionGenerationTaskStatus.Generated);
        var candidate = generated.Candidates.Single();
        candidate.Status.ShouldBe(AiQuestionCandidateStatus.Generated);
        candidate.HasValidatedCurriculumMapping.ShouldBeTrue();
        candidate.TextbookKnowledgePointMappingIds.ShouldBe(new[] { f.MappingA });
        candidate.CurriculumSnapshot!.Mappings.Select(x => x.MappingId).OrderBy(x => x)
            .ShouldBe(new[] { f.MappingA, f.MappingB }.OrderBy(x => x));
        candidate.CurriculumSnapshot.TeacherTextbookSelectionId.ShouldBe(f.SelectionId);
        candidate.Score.ShouldBe(5);
        candidate.Purpose.ShouldBe("ChapterPractice");
        var citation = candidate.Citations.Single();
        citation.CurriculumBindingIds.OrderBy(x => x).ShouldBe(new[] { bindingA, bindingB }.OrderBy(x => x));
        citation.TextbookKnowledgePointMappingIds.OrderBy(x => x).ShouldBe(new[] { f.MappingA, f.MappingB }.OrderBy(x => x));
        await GetRequiredService<EducationMaterialScope>().ValidateSourceAsync(candidate.CurriculumSnapshot,
            citation.KnowledgeItemId, citation.KnowledgeVersionId, citation.ChunkId, citation.CurriculumBindingIds,
            citation.TextbookKnowledgePointMappingIds, citation.SchoolKnowledgeShareId);
        _chat.LastPrompt.ShouldContain(f.MappingA.ToString());
        _chat.LastPrompt.ShouldContain(f.MappingB.ToString());
        _chat.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("outside-scope")]
    [InlineData("uncited")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("name")]
    public async Task Unconfirmed_Model_Mapping_Should_Remain_Visible_And_Unadoptable(string violation)
    {
        var f = await FixtureAsync();
        using var owner = User(f.Owner);
        var source = await SourceAsync(f.Owner);
        await BindAsync(source, f.MappingA);
        _vectors.SearchResults.Add(Hit(source));
        List<Guid>? declared = violation switch
        {
            "unknown" => [Guid.NewGuid()],
            "outside-scope" or "uncited" => [f.MappingB],
            "duplicate" => [f.MappingA, f.MappingA],
            "missing" => [],
            "null" => null,
            _ => [f.MappingA]
        };
        _chat.Content = Response(source.Id, declared, violation == "name" ? "模型自创知识点" : "同名知识点");
        var scope = violation == "uncited" ? new List<Guid> { f.MappingA, f.MappingB } : [f.MappingA];
        var pending = await _generation.CreateAsync(Input(f, scope));
        await _processor.ProcessAsync(pending.Id);
        var result = await _generation.GetAsync(pending.Id);
        result.Status.ShouldBe(AiQuestionGenerationTaskStatus.Generated);
        result.GeneratedCount.ShouldBe(0);
        var candidate = result.Candidates.Single();
        candidate.Status.ShouldBe(AiQuestionCandidateStatus.ValidationFailed);
        candidate.HasValidatedCurriculumMapping.ShouldBeFalse();
        candidate.TextbookKnowledgePointMappingIds.ShouldBe(declared ?? []);
        candidate.ValidationErrors.ShouldNotBeNull();
        candidate.ValidationErrors.ShouldContain("候选知识点必须来自已确认教材映射");
        candidate.SubmittedCitationChunkIds.ShouldBe(new[] { source.Id });
        await Should.ThrowAsync<Volo.Abp.BusinessException>(() => _generation.AdoptCandidateAsDraftAsync(candidate.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Malformed_Scoring_Rule_Should_Preserve_Both_Candidates_And_Allow_Explicit_Repair(bool envelope)
    {
        var f = await FixtureAsync();
        using var owner = User(f.Owner);
        var source = await SourceAsync(f.Owner);
        await BindAsync(source, f.MappingA);
        _vectors.SearchResults.Add(Hit(source));
        var rules = new QuestionScoringDefinition
        {
            Blanks = [new()
            {
                Id = Guid.NewGuid(), DisplayOrder = 1, StemMarker = "【1】", Score = 5,
                AllowedAnswers = ["概念"], TextRule = QuestionBlankTextRule.Exact,
                TextbookKnowledgePointMappingId = f.MappingA
            }]
        };
        var failedOutput = JsonNode.Parse(Response(source.Id, [f.MappingA]))!["questions"]![0]!.DeepClone();
        failedOutput["type"] = "FillBlank";
        failedOutput["title"] = "待修订的填空题";
        failedOutput["stem"] = "先明确数学【1】。";
        failedOutput["options"] = new JsonArray();
        failedOutput["answers"] = new JsonArray();
        failedOutput["scoringDefinition"] = JsonNode.Parse(rules.ToCanonicalJson());
        var validOutput = failedOutput.DeepClone();
        validOutput["title"] = "有效的填空题";
        const string privateSentinel = "not-guid-PRIVATE-RULE-SENTINEL";
        failedOutput["scoringDefinition"]!["blanks"]![0]!["id"] = privateSentinel;
        var outputs = new JsonArray(failedOutput, validOutput);
        _chat.Content = envelope ? new JsonObject { ["questions"] = outputs }.ToJsonString() : outputs.ToJsonString();
        var input = Input(f, [f.MappingA]);
        input.Type = QuestionType.FillBlank;
        input.RequestedCount = 2;
        var task = await _generation.CreateAsync(input);
        await _processor.ProcessAsync(task.Id);
        var generated = await _generation.GetAsync(task.Id);
        generated.Status.ShouldBe(AiQuestionGenerationTaskStatus.Generated);
        generated.GeneratedCount.ShouldBe(1);
        generated.Candidates.Count.ShouldBe(2);
        generated.Candidates.Select(x => x.OutputOrdinal).Order().ShouldBe(new[] { 1, 2 });
        var failed = generated.Candidates.Single(x => x.OutputOrdinal == 1);
        failed.Status.ShouldBe(AiQuestionCandidateStatus.ValidationFailed);
        failed.Title.ShouldBe("待修订的填空题");
        failed.Stem.ShouldBe("先明确数学【1】。");
        failed.Analysis.ShouldBe("先明确同名知识点的定义。");
        failed.SubmittedCitationChunkIds.ShouldBe(new[] { source.Id });
        failed.Citations.Single().ChunkId.ShouldBe(source.Id);
        failed.ScoringDefinition.ShouldBeNull();
        failed.ValidationErrors.ShouldNotBeNullOrWhiteSpace();
        failed.ValidationErrors.ShouldNotContain(privateSentinel);
        failed.ValidationErrors.ShouldNotContain("BytePositionInLine");
        JsonSerializer.Serialize(generated.CallLogs).ShouldNotContain(privateSentinel);
        await Should.ThrowAsync<Volo.Abp.BusinessException>(() => _generation.AdoptCandidateAsDraftAsync(failed.Id));
        var valid = generated.Candidates.Single(x => x.OutputOrdinal == 2);
        valid.Status.ShouldBe(AiQuestionCandidateStatus.Generated);
        valid.ScoringDefinition!.ToCanonicalJson().ShouldBe(rules.ToCanonicalJson());
        var validDraft = await _generation.AdoptCandidateAsDraftAsync(valid.Id);
        validDraft.Status.ShouldBe(QuestionStatus.Draft);
        validDraft.CurriculumConfirmed.ShouldBeFalse();
        var repair = new UpdateAiQuestionCandidateDto
        {
            ContentRevision = failed.ContentRevision, Title = failed.Title, Stem = failed.Stem,
            Analysis = failed.Analysis, ScoringDefinition = rules
        };
        var repaired = await _generation.UpdateCandidateAsync(failed.Id, repair);
        repaired.Status.ShouldBe(AiQuestionCandidateStatus.Generated);
        repaired.ValidationErrors.ShouldBeNull();
        repaired.ContentRevision.ShouldBe(failed.ContentRevision + 1);
        repaired.ScoringDefinition!.ToCanonicalJson().ShouldBe(rules.ToCanonicalJson());
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _generation.UpdateCandidateAsync(failed.Id, repair));
        var repairedDraft = await _generation.AdoptCandidateAsDraftAsync(repaired.Id);
        repairedDraft.Status.ShouldBe(QuestionStatus.Draft);
        repairedDraft.CurriculumConfirmed.ShouldBeFalse();
        repairedDraft.ScoringDefinition!.ToCanonicalJson().ShouldBe(rules.ToCanonicalJson());
        var candidateIds = generated.Candidates.Select(x => x.Id).Order().ToArray();
        await _processor.ProcessAsync(task.Id);
        var replayed = await _generation.GetAsync(task.Id);
        replayed.Candidates.Select(x => x.Id).Order().ShouldBe(candidateIds);
        var replayedRepair = replayed.Candidates.Single(x => x.Id == repaired.Id);
        replayedRepair.LinkedQuestionId.ShouldBe(repairedDraft.Id);
        replayedRepair.ContentRevision.ShouldBe(repaired.ContentRevision);
        replayedRepair.ScoringDefinition!.ToCanonicalJson().ShouldBe(rules.ToCanonicalJson());
        replayed.CallLogs.Count.ShouldBe(1);
        _chat.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Malformed_Optional_Single_Choice_Rule_Should_Not_Fall_Back_To_An_Absent_Rule()
    {
        var f = await FixtureAsync();
        using var owner = User(f.Owner);
        var source = await SourceAsync(f.Owner);
        await BindAsync(source, f.MappingA);
        _vectors.SearchResults.Add(Hit(source));
        var response = JsonNode.Parse(Response(source.Id, [f.MappingA]))!;
        const string privateSentinel = "PRIVATE-OPTIONAL-RULE-SENTINEL";
        response["questions"]![0]!["scoringDefinition"] = privateSentinel;
        _chat.Content = response.ToJsonString();
        var task = await _generation.CreateAsync(Input(f, [f.MappingA]));
        await _processor.ProcessAsync(task.Id);
        var generated = await _generation.GetAsync(task.Id);
        generated.Status.ShouldBe(AiQuestionGenerationTaskStatus.Generated);
        generated.GeneratedCount.ShouldBe(0);
        var candidate = generated.Candidates.Single();
        candidate.Status.ShouldBe(AiQuestionCandidateStatus.ValidationFailed);
        candidate.Title.ShouldBe("数学教学问题");
        candidate.Stem.ShouldBe("学习数学的第一步是什么？");
        candidate.Citations.Single().ChunkId.ShouldBe(source.Id);
        candidate.ScoringDefinition.ShouldBeNull();
        candidate.ValidationErrors.ShouldNotBeNullOrWhiteSpace();
        candidate.ValidationErrors.ShouldNotContain(privateSentinel);
        JsonSerializer.Serialize(generated.CallLogs).ShouldNotContain(privateSentinel);
        await Should.ThrowAsync<Volo.Abp.BusinessException>(() => _generation.AdoptCandidateAsDraftAsync(candidate.Id));
    }

    [Fact]
    public async Task No_Confirmed_Material_Should_Fail_Without_Model_Fallback()
    {
        var f = await FixtureAsync();
        using var owner = User(f.Owner);
        var source = await SourceAsync(f.Owner);
        _vectors.SearchResults.Add(Hit(source));
        var task = await _generation.CreateAsync(Input(f, [f.MappingA]));
        await _processor.ProcessAsync(task.Id);
        var result = await _generation.GetAsync(task.Id);
        result.Status.ShouldBe(AiQuestionGenerationTaskStatus.Failed);
        result.Candidates.ShouldBeEmpty();
        result.CallLogs.Single().Status.ShouldBe(RagCallStatus.NoContext);
        _chat.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Ending_Selection_Before_Execution_Should_Stop_The_Persisted_Task()
    {
        var f = await FixtureAsync();
        using var owner = User(f.Owner);
        var pending = await _generation.CreateAsync(Input(f, [f.MappingA]));
        await EndSelectionAsync(f.SelectionId);
        await _processor.ProcessAsync(pending.Id);
        var result = await _generation.GetAsync(pending.Id);
        result.Status.ShouldBe(AiQuestionGenerationTaskStatus.Failed);
        result.FailureStage.ShouldBe("authorization");
        result.Candidates.ShouldBeEmpty();
        _chat.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("binding")]
    [InlineData("account")]
    [InlineData("assignment")]
    public async Task Permission_Changes_During_Model_Call_Should_Prevent_Stale_Output_Persistence(string change)
    {
        var f = await FixtureAsync();
        using var owner = User(f.Owner);
        var source = await SourceAsync(f.Owner);
        var bindingId = await BindAsync(source, f.MappingA);
        _vectors.SearchResults.Add(Hit(source));
        _chat.Content = Response(source.Id, [f.MappingA]);
        var pending = await _generation.CreateAsync(Input(f, [f.MappingA]));
        _chat.AfterModel = async () =>
        {
            if (change == "selection") await EndSelectionAsync(f.SelectionId);
            else await WithUnitOfWorkAsync(async () =>
            {
                if (change == "binding")
                {
                    var binding = await Repo<KnowledgeMaterialCurriculumBinding>().GetAsync(bindingId);
                    binding.End(DateTime.UtcNow);
                    await Repo<KnowledgeMaterialCurriculumBinding>().UpdateAsync(binding, autoSave: true);
                }
                else if (change == "account")
                {
                    var account = await Repo<IdentityUser>().GetAsync(f.Owner);
                    account.SetIsActive(false);
                    await Repo<IdentityUser>().UpdateAsync(account, autoSave: true);
                }
                else
                {
                    var assignment = await Repo<TeachingAssignment>().GetAsync(f.AssignmentId);
                    assignment.EndAt(DateTime.UtcNow);
                    await Repo<TeachingAssignment>().UpdateAsync(assignment, autoSave: true);
                }
            });
        };
        await _processor.ProcessAsync(pending.Id);
        var result = await _generation.GetAsync(pending.Id);
        result.Status.ShouldBe(AiQuestionGenerationTaskStatus.Failed);
        result.Candidates.ShouldBeEmpty();
        result.GeneratedCount.ShouldBe(0);
        _chat.Calls.ShouldBe(1);
        (await WithUnitOfWorkAsync(() => Repo<AiQuestionCandidate>().GetListAsync(x => x.TaskId == pending.Id))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_Teacher_Should_Not_Edit_Regenerate_Or_Read_Owned_Tasks()
    {
        var f = await FixtureAsync();
        AiQuestionGenerationTaskDto pending;
        using (User(f.Owner))
        {
            var source = await SourceAsync(f.Owner);
            await BindAsync(source, f.MappingA);
            _vectors.SearchResults.Add(Hit(source));
            _chat.Content = Response(source.Id, [f.MappingA]);
            pending = await _generation.CreateAsync(Input(f, [f.MappingA]));
            await _processor.ProcessAsync(pending.Id);
            pending = await _generation.GetAsync(pending.Id);
        }
        var otherId = Guid.NewGuid();
        await WithUnitOfWorkAsync(() => Repo<IdentityUser>().InsertAsync(new IdentityUser(otherId, $"other-{otherId:N}", $"{otherId:N}@example.invalid"), autoSave: true));
        using var other = User(otherId);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _generation.GetAsync(pending.Id));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _generation.UpdateCandidateAsync(pending.Candidates.Single().Id,
            new UpdateAiQuestionCandidateDto { Title = "他人修订", Stem = "他人题干" }));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _generation.RegenerateAsync(pending.Id,
            new RegenerateAiQuestionGenerationTaskDto { IdempotencyKey = Guid.NewGuid() }));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _generation.CreateAsync(Input(f, [f.MappingA])));
    }

    [Fact]
    public async Task Worker_Should_Check_Actual_Owner_Principal_Without_Inheriting_Administrator_Role()
    {
        var f = await FixtureAsync();
        using var owner = User(f.Owner);
        var pending = await _generation.CreateAsync(Input(f, [f.MappingA]));
        var checker = Substitute.For<IPermissionChecker>();
        ClaimsPrincipal? checkedPrincipal = null;
        checker.IsGrantedAsync(Arg.Any<ClaimsPrincipal>(), KnowPulsePermissions.QuestionBank.AiGeneration)
            .Returns(call =>
            {
                checkedPrincipal = call.ArgAt<ClaimsPrincipal>(0);
                return Task.FromResult(false);
            });
        var services = Substitute.For<IAbpLazyServiceProvider>();
        services.LazyGetRequiredService<IRepository<IdentityUser, Guid>>().Returns(Repo<IdentityUser>());
        services.LazyGetRequiredService<IdentityUserManager>().Returns(GetRequiredService<IdentityUserManager>());
        services.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        var processor = new AiQuestionGenerationProcessor(Repo<AiQuestionGenerationTask>(), GetRequiredService<IUnitOfWorkManager>(),
            services, GetRequiredService<IServiceScopeFactory>(), GetRequiredService<IOptions<AiQuestionGenerationWorkerOptions>>(),
            GetRequiredService<ILogger<AiQuestionGenerationProcessor>>());
        using (GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AbpClaimTypes.UserId, f.Owner.ToString()), new Claim(AbpClaimTypes.Role, "admin")], "Test"))))
            await processor.ProcessAsync(pending.Id);
        checkedPrincipal.ShouldNotBeNull();
        checkedPrincipal!.FindFirst(AbpClaimTypes.UserId)!.Value.ShouldBe(f.Owner.ToString());
        checkedPrincipal.IsInRole("admin").ShouldBeFalse();
        _chat.Calls.ShouldBe(0);
        var result = await _generation.GetAsync(pending.Id);
        result.Status.ShouldBe(AiQuestionGenerationTaskStatus.Failed);
        result.FailureReason.ShouldBe("KnowPulse:AiGeneration:AuthorizationChanged");
    }

    private Task<Fixture> FixtureAsync() => WithUnitOfWorkAsync(async () =>
    {
        var owner = Guid.NewGuid();
        await Repo<IdentityUser>().InsertAsync(new IdentityUser(owner, $"genedu-{owner:N}", $"{owner:N}@example.invalid"), autoSave: true);
        await Repo<KnowledgeLibrary>().InsertAsync(new KnowledgeLibrary(owner, null), autoSave: true);
        var now = DateTime.UtcNow;
        var school = new School(Guid.NewGuid(), null, $"S{Guid.NewGuid():N}", "异步出题初中");
        var subject = new EducationSubject(Guid.NewGuid(), null, $"M{Guid.NewGuid():N}", "数学");
        var term = new AcademicTerm(Guid.NewGuid(), null, school.Id, "T1", "当前学期", now.AddDays(-2), now.AddDays(60));
        var schoolClass = new SchoolClass(Guid.NewGuid(), null, school.Id, term.Id, "C1", "七年一班", 7);
        await Repo<School>().InsertAsync(school, autoSave: true);
        await Repo<EducationSubject>().InsertAsync(subject, autoSave: true);
        await Repo<AcademicTerm>().InsertAsync(term, autoSave: true);
        await Repo<SchoolClass>().InsertAsync(schoolClass, autoSave: true);
        var book = new TextbookVersion(Guid.NewGuid(), null, school.Id, subject.Id, "MATH", 1, 7, "数学教材", "测试出版社", "测试版", "上册");
        await Repo<TextbookVersion>().InsertAsync(book, autoSave: true);
        var mappings = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var chapter = new TextbookChapter(Guid.NewGuid(), null, book.Id, null, $"CH{i}", $"第{i + 1}章", i);
            var point = new CourseKnowledgePoint(Guid.NewGuid(), null, school.Id, subject.Id, $"KP{i}");
            var revision = new CourseKnowledgePointRevision(Guid.NewGuid(), null, point.Id, point.AdvanceRevision(), "同名知识点", null);
            var mapping = new TextbookKnowledgePointMapping(Guid.NewGuid(), null, book.Id, chapter.Id, point.Id, revision.Id, owner, now);
            await Repo<TextbookChapter>().InsertAsync(chapter, autoSave: true);
            await Repo<CourseKnowledgePoint>().InsertAsync(point, autoSave: true);
            await Repo<CourseKnowledgePointRevision>().InsertAsync(revision, autoSave: true);
            await Repo<TextbookKnowledgePointMapping>().InsertAsync(mapping, autoSave: true);
            mappings.Add(mapping.Id);
        }
        book.Publish(now);
        await Repo<TextbookVersion>().UpdateManyAsync([book], autoSave: true);
        await Repo<SchoolMembership>().InsertAsync(new SchoolMembership(Guid.NewGuid(), null, school.Id, owner, SchoolMemberRole.Teacher, term.StartsAtUtc), autoSave: true);
        var assignment = new TeachingAssignment(Guid.NewGuid(), null, school.Id, term.Id, schoolClass.Id, subject.Id, owner, term.StartsAtUtc, term.EndsAtUtc);
        await Repo<TeachingAssignment>().InsertAsync(assignment, autoSave: true);
        var selection = new TeacherTextbookSelection(Guid.NewGuid(), null, school.Id, owner, assignment.Id, book.Id, now);
        await Repo<TeacherTextbookSelection>().InsertAsync(selection, autoSave: true);
        return new Fixture(owner, selection.Id, assignment.Id, mappings[0], mappings[1]);
    });

    private Task<KnowledgeChunk> SourceAsync(Guid owner) => WithUnitOfWorkAsync(async () =>
    {
        // The fixture starts with already indexed relational material; real worker processing is exercised below.
        var item = new KnowledgeItem(Guid.NewGuid(), null, "教师积累", libraryId: owner, ownerUserId: owner);
        await Repo<KnowledgeItem>().InsertAsync(item, autoSave: true);
        var version = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1, item.Title, "学习数学应先明确同名知识点的定义。");
        version.Publish(DateTime.UtcNow);
        await Repo<KnowledgeVersion>().InsertAsync(version, autoSave: true);
        item.SetCurrentPublishedVersion(version.Id);
        await Repo<KnowledgeItem>().UpdateManyAsync([item], autoSave: true);
        var job = new KnowledgeIndexJob(Guid.NewGuid(), null, item.Id, version.Id, KnowledgeIndexJobTriggerType.Publish);
        job.MarkIndexed(DateTime.UtcNow);
        await Repo<KnowledgeIndexJob>().InsertAsync(job, autoSave: true);
        var chunk = new KnowledgeChunk(Guid.NewGuid(), null, item.Id, version.Id, job.Id, 0, version.ContentText!,
            Guid.NewGuid().ToString("N"), item.Title, isPublishedSnapshot: true);
        await Repo<KnowledgeChunk>().InsertAsync(chunk, autoSave: true);
        return chunk;
    });

    private async Task<Guid> BindAsync(KnowledgeChunk chunk, Guid mappingId) => (await GetRequiredService<IEducationPreparationAppService>()
        .ConfirmBindingAsync(new ConfirmKnowledgeMaterialCurriculumBindingDto
        {
            IdempotencyKey = Guid.NewGuid(), KnowledgeVersionId = chunk.KnowledgeVersionId,
            KnowledgeChunkId = chunk.Id, TextbookKnowledgePointMappingId = mappingId
        })).Id;

    private Task EndSelectionAsync(Guid id) => WithUnitOfWorkAsync(async () =>
    {
        var selection = await Repo<TeacherTextbookSelection>().GetAsync(id);
        selection.End(DateTime.UtcNow);
        await Repo<TeacherTextbookSelection>().UpdateAsync(selection, autoSave: true);
    });

    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => GetRequiredService<IRepository<T, Guid>>();
    private IDisposable User(Guid id) => GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(AbpClaimTypes.UserId, id.ToString()), new Claim(AbpClaimTypes.UserName, $"genedu-{id:N}")], "Test")));
    private static CreateAiQuestionGenerationTaskDto Input(Fixture f, List<Guid> mappings) => new()
    {
        IdempotencyKey = Guid.NewGuid(), KnowledgeScope = "教材关联教学经验", Score = 5, Purpose = "ChapterPractice",
        CurriculumScope = new EducationCurriculumScopeDto { TeacherTextbookSelectionId = f.SelectionId, TextbookKnowledgePointMappingIds = mappings }
    };
    private static VectorSearchHit Hit(KnowledgeChunk chunk) => new()
    { ChunkId = chunk.Id, KnowledgeItemId = chunk.KnowledgeItemId, KnowledgeVersionId = chunk.KnowledgeVersionId, Content = "untrusted", Score = 0.9 };
    private static string Response(Guid chunkId, List<Guid>? mappingIds, string pointName = "同名知识点") => JsonSerializer.Serialize(new
    {
        questions = new[] { new
        {
            type = "SingleChoice", difficulty = "Normal", title = "数学教学问题", stem = "学习数学的第一步是什么？",
            options = new[] { new { optionKey = "A", content = "明确概念", displayOrder = 1 }, new { optionKey = "B", content = "随机猜测", displayOrder = 2 } },
            answers = new[] { "A" }, analysis = "先明确同名知识点的定义。", knowledgePoints = new[] { pointName },
            textbookKnowledgePointMappingIds = mappingIds, citations = new[] { new { chunkId } }
        } }
    });
    private sealed record Fixture(Guid Owner, Guid SelectionId, Guid AssignmentId, Guid MappingA, Guid MappingB);
}

[DependsOn(typeof(KnowPulseEntityFrameworkCoreTestModule))]
public class EducationAiQuestionGenerationTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton<EducationGenerationTestChatClient>();
        context.Services.Replace(ServiceDescriptor.Singleton<IRagChatClient>(services => services.GetRequiredService<EducationGenerationTestChatClient>()));
    }
}

public class EducationGenerationTestChatClient : IRagChatClient
{
    public string Content { get; set; } = "{}";
    public int Calls { get; private set; }
    public string LastPrompt { get; private set; } = string.Empty;
    public Func<Task>? AfterModel { get; set; }

    public async Task<RagChatClientResult> GenerateAsync(RagChatClientRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastPrompt = request.UserPrompt;
        if (AfterModel != null) await AfterModel();
        cancellationToken.ThrowIfCancellationRequested();
        return RagChatClientResult.Success(Content, 120, 30, 150);
    }
}
