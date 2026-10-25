using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using KnowPulse.AiQuestioning;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class TeachingKnowledgeAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly ICurrentPrincipalAccessor _principal;
    private readonly IKnowledgeItemAppService _items;
    private readonly IQuestionWorkflowAppService _workflow;
    private readonly IQuestionAppService _questions;
    private readonly IAiQuestionGenerationAppService _generation;

    public TeachingKnowledgeAppServiceTests()
    {
        _principal = GetRequiredService<ICurrentPrincipalAccessor>();
        _items = GetRequiredService<IKnowledgeItemAppService>();
        _workflow = GetRequiredService<IQuestionWorkflowAppService>();
        _questions = GetRequiredService<IQuestionAppService>();
        _generation = GetRequiredService<IAiQuestionGenerationAppService>();
    }

    [Fact]
    public async Task Should_Reject_Authenticated_Principal_Without_A_Stable_User_Id()
    {
        using (_principal.Change(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(AbpClaimTypes.UserName, "no-stable-id"), new Claim(AbpClaimTypes.Role, "teacher")
        ], "Test"))))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _items.GetListAsync(new()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _questions.GetListAsync(new()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _generation.GetListAsync(new()));
        }
    }

    [Fact]
    public async Task Should_Share_Only_Current_Published_Version_And_Revoke_Future_Access()
    {
        var owner = Guid.NewGuid();
        var users = GetRequiredService<IdentityUserManager>();
        var sharedTeacher = await WithUnitOfWorkAsync(async () =>
        {
            var id = Guid.NewGuid();
            var user = new IdentityUser(id, "share_" + id.ToString("N")[..8], id.ToString("N") + "@example.invalid");
            (await users.CreateAsync(user)).Succeeded.ShouldBeTrue();
            (await users.AddToRoleAsync(user, "teacher")).Succeeded.ShouldBeTrue();
            return user.Id;
        });
        KnowledgeItemDto item;
        KnowledgeVersionDto published;
        KnowledgeVersionDto draft;
        using (As(owner))
        {
            item = await _items.CreateAsync(new() { Title = "可共享的讲义" });
            published = await _items.CreateVersionAsync(item.Id, new() { Title = item.Title, ContentText = "允许共享的正文" });
            await _items.PublishVersionAsync(item.Id, published.Id);
            draft = await _items.CreateVersionAsync(item.Id, new() { Title = "私人草稿", ContentText = "尚未共享的内容" });
            await _items.SetSharedTeachersAsync(item.Id, new() { TeacherIds = [sharedTeacher] });
            var scopes = await _items.GetPermissionScopesAsync(item.Id);
            scopes.Items.Select(x => x.TargetId).ShouldContain(owner);
            scopes.Items.Select(x => x.TargetId).ShouldContain(sharedTeacher);
            await Should.ThrowAsync<UserFriendlyException>(() => _items.DeletePermissionScopeAsync(item.Id,
                scopes.Items.Single(x => x.TargetId == owner).Id));
        }
        using (As(sharedTeacher))
        {
            (await _items.GetVersionsAsync(item.Id)).Items.ShouldHaveSingleItem().Id.ShouldBe(published.Id);
            var detail = await GetRequiredService<IKnowledgeManagementAppService>().GetAsync(item.Id);
            detail.Versions.ShouldHaveSingleItem().Id.ShouldBe(published.Id);
            detail.VersionCount.ShouldBe(1);
            detail.LatestIndexJobId.ShouldBeNull();
            detail.LatestIndexFailureReason.ShouldBeNull();
            detail.PermissionScopes.ShouldBeEmpty();
            detail.IndexJobs.ShouldBeEmpty();
            var shared = await GetRequiredService<IKnowledgeManagementAppService>().GetListAsync(new() { SharedOnly = true });
            shared.Items.Single(x => x.Id == item.Id).VersionCount.ShouldBe(1);
            shared.Items.Single(x => x.Id == item.Id).LatestIndexJobId.ShouldBeNull();
            await Should.ThrowAsync<AbpAuthorizationException>(() => _items.GetAttachmentsAsync(item.Id, draft.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _items.UpdateAsync(item.Id, new() { Title = "不能改原件" }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _items.SetSharedTeachersAsync(item.Id, new()));
        }
        using (As(owner)) await _items.SetSharedTeachersAsync(item.Id, new());
        using (As(sharedTeacher))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _items.GetAsync(item.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _generation.CreateAsync(new()
            {
                KnowledgeScope = "讲义", KnowledgeItemIds = [item.Id]
            }));
        }
    }

    [Fact]
    public async Task Should_Default_To_Private_And_Reject_Other_Teacher_Reads_And_Writes()
    {
        var teacher = Guid.NewGuid();
        KnowledgeItemDto item;
        KnowledgeVersionDto version;
        using (As(teacher))
        {
            item = await _items.CreateAsync(new() { Title = "私有讲义" });
            item.OwnerTeacherId.ShouldBe(teacher);
            version = await _items.CreateVersionAsync(item.Id, new() { Title = item.Title, ContentText = "平均分是分数的基础。" });
            await _items.PublishVersionAsync(item.Id, version.Id);
            var scopes = await _items.GetPermissionScopesAsync(item.Id);
            scopes.Items.ShouldHaveSingleItem().TargetId.ShouldBe(teacher);
            await Should.ThrowAsync<UserFriendlyException>(() => _items.UpdateDraftVersionAsync(item.Id, version.Id,
                new() { Title = "不能修改已发布版本", ContentText = "不能改写" }));
        }
        using (As(Guid.NewGuid()))
        {
            (await _items.GetListAsync(new() { MaxResultCount = 100 })).Items.ShouldNotContain(x => x.Id == item.Id);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _items.GetAsync(item.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _items.GetVersionsAsync(item.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _items.CreateVersionAsync(item.Id, new() { Title = "越权" }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _items.DeleteAsync(item.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _items.GetPermissionScopesAsync(item.Id));
        }
    }

    [Fact]
    public async Task Should_Allow_Only_Owner_To_Confirm_And_Reuse_A_Question()
    {
        var teacher = Guid.NewGuid();
        QuestionDto question;
        using (As(teacher))
        {
            question = await _workflow.CreateManualDraftAsync(new()
            {
                Title = "分数判断", Stem = "分数必须建立在平均分的基础上。", Type = QuestionType.TrueFalse,
                Answers = [new() { Value = "true" }]
            });
            question.OwnerTeacherId.ShouldBe(teacher);
            (await _workflow.ConfirmAsync(question.Id)).Status.ShouldBe(QuestionStatus.Approved);
            (await _workflow.ConfirmAsync(question.Id)).Status.ShouldBe(QuestionStatus.Approved);
        }
        using (As(Guid.NewGuid()))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _questions.GetAsync(question.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _workflow.ConfirmAsync(question.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _questions.DeleteAsync(question.Id));
            (await _questions.GetListAsync(new() { MaxResultCount = 100 })).Items.ShouldNotContain(x => x.Id == question.Id);
        }
    }

    [Fact]
    public async Task Should_Require_Explicit_Ready_Sources_Before_Generating()
    {
        using (As(Guid.NewGuid()))
        {
            await Should.ThrowAsync<UserFriendlyException>(() => _generation.CreateAsync(new() { KnowledgeScope = "分数" }));
            var item = await _items.CreateAsync(new() { Title = "未处理资料" });
            await Should.ThrowAsync<UserFriendlyException>(() => _generation.CreateAsync(new()
            {
                KnowledgeScope = "分数", KnowledgeItemIds = [item.Id]
            }));
        }
    }

    [Fact]
    public async Task Should_Apply_Selected_Source_Ids_Before_Model_Context()
    {
        var vector = GetRequiredService<TestKnowledgeVectorStore>();
        vector.Reset();
        var pipeline = GetRequiredService<IKnowledgeIngestionPipeline>();
        var chunks = GetRequiredService<IRepository<KnowledgeChunk, Guid>>();
        var teacher = Guid.NewGuid();
        using (As(teacher))
        {
            var first = await _items.CreateAsync(new() { Title = "选中的分数讲义" });
            var second = await _items.CreateAsync(new() { Title = "未选中的讲义" });
            foreach (var item in new[] { first, second })
            {
                var version = await _items.CreateVersionAsync(item.Id, new() { Title = item.Title, ContentText = "分数表示把单位一平均分成若干份，取其中的一份或几份。" });
                await _items.PublishVersionAsync(item.Id, version.Id);
                await pipeline.IngestAsync(version.Id);
            }
            await WithUnitOfWorkAsync(async () =>
            {
                var rows = (await chunks.GetQueryableAsync()).Where(x => x.IsActive &&
                    (x.KnowledgeItemId == first.Id || x.KnowledgeItemId == second.Id)).ToList();
                vector.SearchResults.AddRange(rows.Select(x => new VectorSearchHit
                {
                    ChunkId = x.Id, KnowledgeItemId = x.KnowledgeItemId,
                    KnowledgeVersionId = x.KnowledgeVersionId, Score = 0.9
                }));
            });
            var found = await GetRequiredService<IKnowledgeRetriever>().SearchAsync(new()
            {
                Query = "分数", KnowledgeItemIds = [first.Id], TopK = 10
            });
            found.ShouldNotBeEmpty();
            found.ShouldAllBe(x => x.KnowledgeItemId == first.Id);
            vector.SearchRequests.Last().PermissionFilter.KnowledgeItemIds.ShouldBe([first.Id]);
        }
    }

    private IDisposable As(Guid id) => _principal.Change(new ClaimsPrincipal(new ClaimsIdentity([
        new Claim(AbpClaimTypes.UserId, id.ToString()), new Claim(AbpClaimTypes.UserName, "教师"),
        new Claim(AbpClaimTypes.Role, "teacher")
    ], "Test")));
}
