using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Claims;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class TeachingKnowledgeMaintenanceAccessTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IKnowledgeIndexJobAppService _jobs;
    private readonly IKnowledgeFeedbackAppService _feedback;
    private readonly ICurrentPrincipalAccessor _principal;
    private readonly Guid _teacher = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();

    public TeachingKnowledgeMaintenanceAccessTests()
    {
        _jobs = GetRequiredService<IKnowledgeIndexJobAppService>();
        _feedback = GetRequiredService<IKnowledgeFeedbackAppService>();
        _principal = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    [Fact]
    public async Task Should_Protect_All_Job_Entrypoints_And_Run_Actual_Pipeline_For_Legacy_Completion()
    {
        var own = await SeedAsync(_teacher);
        var foreign = await SeedAsync(_other);
        var vectorStore = GetRequiredService<TestKnowledgeVectorStore>();
        vectorStore.Reset();
        using (As(_teacher))
        {
            (await _jobs.GetListAsync(new())).Items.Single().Id.ShouldBe(own.Job.Id);
            (await _jobs.GetOperationalSummaryAsync()).PendingCount.ShouldBe(1);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _jobs.GetAsync(foreign.Job.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _jobs.CreateAsync(new()
            {
                KnowledgeItemId = foreign.Item.Id, KnowledgeVersionId = foreign.Current.Id
            }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _jobs.ReindexVersionAsync(foreign.Current.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _jobs.RetryAsync(foreign.Job.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _jobs.ExecuteAsync(foreign.Job.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _jobs.RecoverAsync(foreign.Job.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _jobs.MarkIndexedAsync(foreign.Job.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _jobs.StartAsync(own.Job.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _jobs.MarkFailedAsync(own.Job.Id, new() { FailureStage = "test", FailureReason = "fake" }));
            var indexed = await _jobs.MarkIndexedAsync(own.Job.Id);
            indexed.Status.ShouldBe(KnowledgeIndexJobStatus.Indexed);
            indexed.ProgressPercent.ShouldBe(100);
            vectorStore.UpsertRequests.Count.ShouldBe(1);
            vectorStore.UpsertRequests.Single().ShouldNotBeEmpty();
        }
        using (As(Guid.NewGuid(), "admin"))
            await Should.ThrowAsync<AbpAuthorizationException>(() => _jobs.GetAsync(foreign.Job.Id));
    }

    [Fact]
    public async Task Shared_Feedback_Should_Only_Use_Current_Version_And_Never_Allow_Managing_The_Owner_Record()
    {
        var shared = await SeedAsync(_other, _teacher);
        KnowledgeFeedbackDto original;
        using (As(_other)) original = await _feedback.SubmitAsync(Input(shared.Item.Id, shared.Current.Id));
        using (As(_teacher))
        {
            (await _feedback.GetListAsync(new())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedback.GetAsync(original.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedback.ProcessAsync(original.Id, new()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedback.CreateRevisionAsync(original.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedback.ResolveAsync(original.Id, new() { Resolution = "cross owner" }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedback.CloseAsync(original.Id, new()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedback.LinkVersionAsync(original.Id, new() { LinkedKnowledgeVersionId = shared.Draft.Id }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedback.SubmitAsync(Input(shared.Item.Id, shared.Historical.Id)));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedback.SubmitAsync(Input(shared.Item.Id, shared.Draft.Id)));
            var submitted = await _feedback.SubmitAsync(Input(shared.Item.Id, null));
            submitted.KnowledgeVersionId.ShouldBe(shared.Current.Id);
            submitted.KnowledgeVersionTitle.ShouldBe(shared.Current.Title);
        }
        using (As(Guid.NewGuid(), "admin"))
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedback.GetAsync(original.Id));
        await WithUnitOfWorkAsync(async () =>
        {
            var repository = GetRequiredService<IRepository<KnowledgePermissionScope, Guid>>();
            var scope = await repository.GetAsync(shared.ScopeId!.Value);
            scope.SetEnabled(false);
            await repository.UpdateAsync(scope, autoSave: true);
        });
        using (As(_teacher))
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedback.SubmitAsync(Input(shared.Item.Id, shared.Current.Id)));
        using (As(_other))
        {
            var processing = await _feedback.ProcessAsync(original.Id, new());
            processing.Status.ShouldBe(KnowledgeFeedbackStatus.Processing);
            (await _feedback.CreateRevisionAsync(original.Id)).LinkedKnowledgeVersionId.ShouldNotBeNull();
        }
    }

    private IDisposable As(Guid id, string role = "teacher") => _principal.Change(new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(AbpClaimTypes.UserId, id.ToString()), new Claim(AbpClaimTypes.UserName, role), new Claim(AbpClaimTypes.Role, role)
    }, "Test")));

    private Task<Fixture> SeedAsync(Guid owner, Guid? shareTo = null) => WithUnitOfWorkAsync(async () =>
    {
        var item = new KnowledgeItem(Guid.NewGuid(), null, "teacher private source");
        item.AssignTeacher(owner);
        await GetRequiredService<IRepository<KnowledgeItem, Guid>>().InsertAsync(item, autoSave: true);
        var historical = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1, "historical private version", "old content");
        historical.Publish(DateTime.UtcNow.AddDays(-1));
        historical.ClearCurrentPublished();
        var current = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 2, "current shared version", "Current teacher-owned indexable content.");
        current.Publish(DateTime.UtcNow);
        var draft = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 3, "private draft", "private draft content");
        var versions = GetRequiredService<IRepository<KnowledgeVersion, Guid>>();
        await versions.InsertAsync(historical, autoSave: true);
        await versions.InsertAsync(current, autoSave: true);
        await versions.InsertAsync(draft, autoSave: true);
        item.SetCurrentPublishedVersion(current.Id);
        await GetRequiredService<IRepository<KnowledgeItem, Guid>>().UpdateAsync(item, autoSave: true);
        Guid? scopeId = null;
        if (shareTo.HasValue)
        {
            scopeId = Guid.NewGuid();
            await GetRequiredService<IRepository<KnowledgePermissionScope, Guid>>().InsertAsync(new KnowledgePermissionScope(
                scopeId.Value, null, item.Id, KnowledgePermissionScopeType.User, shareTo), autoSave: true);
        }
        var job = new KnowledgeIndexJob(Guid.NewGuid(), null, item.Id, current.Id, KnowledgeIndexJobTriggerType.Publish);
        await GetRequiredService<IRepository<KnowledgeIndexJob, Guid>>().InsertAsync(job, autoSave: true);
        return new Fixture(item, current, historical, draft, job, scopeId);
    });

    private static SubmitKnowledgeFeedbackDto Input(Guid itemId, Guid? versionId) => new()
    {
        KnowledgeItemId = itemId, KnowledgeVersionId = versionId,
        Type = KnowledgeFeedbackType.Error, Title = "错误反馈", Description = "请核对当前版本的说明。"
    };

    private sealed record Fixture(KnowledgeItem Item, KnowledgeVersion Current, KnowledgeVersion Historical,
        KnowledgeVersion Draft, KnowledgeIndexJob Job, Guid? ScopeId);
}
