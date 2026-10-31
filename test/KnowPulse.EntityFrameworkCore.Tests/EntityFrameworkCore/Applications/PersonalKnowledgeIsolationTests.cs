using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.Permissions;
using KnowPulse.RagAI;
using NSubstitute;
using Shouldly;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Content;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

/// <summary>
/// 全部功能权限放行时仍验证个人归属，覆盖旧知识、管理、索引和检索入口。
/// 使用真实 claims 切换当前用户，避免把测试基类的管理员身份当成所有者权限。
/// </summary>
[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class PersonalKnowledgeIsolationTests : KnowPulseEntityFrameworkCoreTestBase
{
    private static readonly Guid OwnerUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");
    private static readonly Guid OtherUserId = Guid.Parse("461f0c0a-5c83-4d50-ace5-6b90f570f3b0");

    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private readonly IKnowledgeItemAppService _itemAppService;
    private readonly IKnowledgeManagementAppService _managementAppService;
    private readonly IKnowledgeIndexJobAppService _indexJobAppService;
    private readonly IKnowledgeFeedbackAppService _feedbackAppService;
    private readonly IKnowledgeRetriever _retriever;
    private readonly IRepository<KnowledgeLibrary, Guid> _libraryRepository;
    private readonly IRepository<KnowledgeItem, Guid> _itemRepository;
    private readonly IRepository<KnowledgeVersion, Guid> _versionRepository;
    private readonly IRepository<KnowledgePermissionScope, Guid> _scopeRepository;
    private readonly IRepository<KnowledgeIndexJob, Guid> _jobRepository;
    private readonly IRepository<KnowledgeChunk, Guid> _chunkRepository;
    private readonly IRepository<KnowledgeFeedback, Guid> _feedbackRepository;
    private readonly TestKnowledgeVectorStore _vectorStore;

    public PersonalKnowledgeIsolationTests()
    {
        _principalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
        _itemAppService = GetRequiredService<IKnowledgeItemAppService>();
        _managementAppService = GetRequiredService<IKnowledgeManagementAppService>();
        _indexJobAppService = GetRequiredService<IKnowledgeIndexJobAppService>();
        _feedbackAppService = GetRequiredService<IKnowledgeFeedbackAppService>();
        _retriever = GetRequiredService<IKnowledgeRetriever>();
        _libraryRepository = GetRequiredService<IRepository<KnowledgeLibrary, Guid>>();
        _itemRepository = GetRequiredService<IRepository<KnowledgeItem, Guid>>();
        _versionRepository = GetRequiredService<IRepository<KnowledgeVersion, Guid>>();
        _scopeRepository = GetRequiredService<IRepository<KnowledgePermissionScope, Guid>>();
        _jobRepository = GetRequiredService<IRepository<KnowledgeIndexJob, Guid>>();
        _chunkRepository = GetRequiredService<IRepository<KnowledgeChunk, Guid>>();
        _feedbackRepository = GetRequiredService<IRepository<KnowledgeFeedback, Guid>>();
        _vectorStore = GetRequiredService<TestKnowledgeVectorStore>();
        _vectorStore.Reset();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Other_User_Should_Not_Read_Private_Item_Even_With_Admin_And_Legacy_All_Scope(bool legacyAllScope)
    {
        var knowledge = await InsertKnowledgeAsync(personal: true, scopeType: legacyAllScope ? KnowledgePermissionScopeType.All : null);

        using (ChangeUser(OtherUserId))
        {
            var items = await _itemAppService.GetListAsync(new PagedAndSortedResultRequestDto { MaxResultCount = 100 });
            items.Items.Select(x => x.Id).ShouldNotContain(knowledge.Item.Id);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.GetAsync(knowledge.Item.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.GetVersionsAsync(knowledge.Item.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.GetAttachmentsAsync(knowledge.Item.Id, knowledge.Version.Id));

            var management = await _managementAppService.GetListAsync(new GetKnowledgeManagementListInput
            {
                Keyword = knowledge.Item.Title,
                MaxResultCount = 100
            });
            management.Items.ShouldBeEmpty();
            management.TotalCount.ShouldBe(0);
            management.Overview.TotalCount.ShouldBe(0);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _managementAppService.GetAsync(knowledge.Item.Id));
        }

        using (ChangeUser(OwnerUserId))
        {
            (await _itemAppService.GetAsync(knowledge.Item.Id)).Id.ShouldBe(knowledge.Item.Id);
            (await _itemAppService.GetVersionsAsync(knowledge.Item.Id)).Items.Single().ContentText.ShouldBe(knowledge.Version.ContentText);
        }
    }

    [Fact]
    public async Task Explicit_Owner_Should_Control_Access_Instead_Of_Audit_Creator()
    {
        KnowledgeFixture knowledge;
        using (ChangeUser(OtherUserId))
        {
            knowledge = await InsertKnowledgeAsync(personal: true);
            knowledge.Item.CreatorId.ShouldBe(OtherUserId);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.GetAsync(knowledge.Item.Id));
        }

        using (ChangeUser(OwnerUserId))
        {
            var item = await _itemAppService.GetAsync(knowledge.Item.Id);
            item.OwnerUserId.ShouldBe(OwnerUserId);
            var updated = await _itemAppService.UpdateAsync(knowledge.Item.Id, new UpdateKnowledgeItemDto
            {
                Title = "Owner keeps control of the teaching material",
                IsActive = true
            });
            updated.Title.ShouldBe("Owner keeps control of the teaching material");
            updated.OwnerUserId.ShouldBe(OwnerUserId);
        }
    }

    [Fact]
    public async Task Other_User_Should_Not_Modify_Metadata_Delete_Or_Create_Versions()
    {
        var knowledge = await InsertKnowledgeAsync(personal: true, published: true);

        using (ChangeUser(OtherUserId))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.UpdateAsync(knowledge.Item.Id, new UpdateKnowledgeItemDto
            {
                Title = "Unauthorized replacement",
                IsActive = false
            }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.DeleteAsync(knowledge.Item.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.CreateVersionAsync(knowledge.Item.Id, new CreateKnowledgeVersionDto
            {
                Title = "Unauthorized version",
                ContentText = "Unauthorized contents"
            }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.PublishVersionAsync(knowledge.Item.Id, knowledge.Version.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.WithdrawVersionAsync(knowledge.Item.Id, knowledge.Version.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.ArchiveVersionAsync(knowledge.Item.Id, knowledge.Version.Id));
        }

        await WithUnitOfWorkAsync(async () =>
        {
            var item = await _itemRepository.GetAsync(knowledge.Item.Id);
            item.Title.ShouldBe(knowledge.Item.Title);
            item.IsActive.ShouldBeTrue();
            (await _versionRepository.GetAsync(knowledge.Version.Id)).Status.ShouldBe(KnowledgeVersionStatus.Published);
            (await _versionRepository.CountAsync(x => x.KnowledgeItemId == item.Id)).ShouldBe(1);
        });
    }

    [Fact]
    public async Task Other_User_Should_Not_Read_Upload_Or_Delete_Private_Attachments()
    {
        var knowledge = await InsertKnowledgeAsync(personal: true);
        KnowledgeAttachmentDto attachment;
        using (ChangeUser(OwnerUserId))
        {
            attachment = await UploadTextAsync(knowledge, "private-note.txt", "Only the owner may read this attachment.");
        }

        using (ChangeUser(OtherUserId))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.GetAttachmentsAsync(knowledge.Item.Id, knowledge.Version.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.DownloadAttachmentAsync(knowledge.Item.Id, knowledge.Version.Id, attachment.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => UploadTextAsync(knowledge, "foreign.txt", "Unauthorized upload"));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.DeleteAttachmentAsync(knowledge.Item.Id, knowledge.Version.Id, attachment.Id));
        }

        using (ChangeUser(OwnerUserId))
        {
            (await _itemAppService.GetAttachmentsAsync(knowledge.Item.Id, knowledge.Version.Id)).Items.Single().Id.ShouldBe(attachment.Id);
            using var download = await _itemAppService.DownloadAttachmentAsync(knowledge.Item.Id, knowledge.Version.Id, attachment.Id);
            using var reader = new StreamReader(download.GetStream(), Encoding.UTF8);
            (await reader.ReadToEndAsync()).ShouldBe("Only the owner may read this attachment.");
        }
    }

    [Fact]
    public async Task Other_User_Should_Not_Read_Change_Or_Republish_Private_Access_Policy()
    {
        var knowledge = await InsertKnowledgeAsync(personal: true, scopeType: KnowledgePermissionScopeType.User);

        using (ChangeUser(OtherUserId))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.GetPermissionScopesAsync(knowledge.Item.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.CreatePermissionScopeAsync(knowledge.Item.Id, new CreateKnowledgePermissionScopeDto
            {
                ScopeType = KnowledgePermissionScopeType.All
            }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.DeletePermissionScopeAsync(knowledge.Item.Id, knowledge.Scope!.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.PublishVersionWithAccessPolicyAsync(knowledge.Item.Id, new PublishKnowledgeVersionInput
            {
                KnowledgeVersionId = knowledge.Version.Id,
                PermissionScopes = [new CreateKnowledgePermissionScopeDto { ScopeType = KnowledgePermissionScopeType.All }]
            }));
        }

        await WithUnitOfWorkAsync(async () =>
        {
            var scope = await _scopeRepository.GetAsync(knowledge.Scope!.Id);
            scope.ScopeType.ShouldBe(KnowledgePermissionScopeType.User);
            scope.TargetId.ShouldBe(OwnerUserId);
            (await _versionRepository.GetAsync(knowledge.Version.Id)).Status.ShouldBe(KnowledgeVersionStatus.Draft);
            (await _jobRepository.CountAsync(x => x.KnowledgeItemId == knowledge.Item.Id)).ShouldBe(0);
        });
    }

    [Fact]
    public async Task Other_User_Should_Not_List_Read_Or_Manipulate_Private_Index_Jobs()
    {
        var knowledge = await InsertKnowledgeAsync(personal: true, published: true);
        var jobId = knowledge.Job!.Id;

        using (ChangeUser(OtherUserId))
        {
            var jobs = await _indexJobAppService.GetListAsync(new GetKnowledgeIndexJobListInput { MaxResultCount = 100 });
            jobs.Items.Select(x => x.Id).ShouldNotContain(jobId);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _indexJobAppService.GetAsync(jobId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _indexJobAppService.CreateAsync(new CreateKnowledgeIndexJobDto
            {
                KnowledgeItemId = knowledge.Item.Id,
                KnowledgeVersionId = knowledge.Version.Id
            }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _indexJobAppService.ReindexVersionAsync(knowledge.Version.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _indexJobAppService.StartAsync(jobId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _indexJobAppService.ExecuteAsync(jobId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _indexJobAppService.RetryAsync(jobId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _indexJobAppService.RecoverAsync(jobId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _indexJobAppService.MarkIndexedAsync(jobId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _indexJobAppService.MarkFailedAsync(jobId, new MarkKnowledgeIndexJobFailedDto
            {
                FailureStage = "foreign-update",
                FailureReason = "Must not overwrite the owner's job."
            }));

            var summary = await _indexJobAppService.GetOperationalSummaryAsync();
            summary.LastIndexedTime.ShouldBeNull();
            (summary.PendingCount + summary.ProcessingCount + summary.FailedCount).ShouldBe(0);
        }

        using (ChangeUser(OwnerUserId))
        {
            (await _indexJobAppService.GetAsync(jobId)).KnowledgeItemId.ShouldBe(knowledge.Item.Id);
            var ownJobs = await _indexJobAppService.GetListAsync(new GetKnowledgeIndexJobListInput
            {
                KnowledgeItemId = knowledge.Item.Id,
                MaxResultCount = 100
            });
            ownJobs.Items.Single().Id.ShouldBe(jobId);
            (await _indexJobAppService.GetOperationalSummaryAsync()).LastIndexedTime.ShouldNotBeNull();
        }

        (await WithUnitOfWorkAsync(() => _jobRepository.GetAsync(jobId))).Status.ShouldBe(KnowledgeIndexJobStatus.Indexed);
    }

    [Fact]
    public async Task Other_User_Should_Not_Read_Submit_Or_Process_Private_Feedback()
    {
        var knowledge = await InsertKnowledgeAsync(personal: true, published: true);
        KnowledgeFeedbackDto submitted;
        using (ChangeUser(OwnerUserId))
        {
            submitted = await _feedbackAppService.SubmitAsync(new SubmitKnowledgeFeedbackDto
            {
                KnowledgeItemId = knowledge.Item.Id,
                KnowledgeVersionId = knowledge.Version.Id,
                Type = KnowledgeFeedbackType.Error,
                Title = "Private teaching correction",
                Description = "This description is private to the teacher."
            });
        }

        using (ChangeUser(OtherUserId))
        {
            var result = await _feedbackAppService.GetListAsync(new GetKnowledgeFeedbackListInput
            {
                KnowledgeItemId = knowledge.Item.Id,
                Keyword = "Private teaching correction"
            });
            result.TotalCount.ShouldBe(0);
            result.Items.ShouldBeEmpty();
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedbackAppService.GetAsync(submitted.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedbackAppService.SubmitAsync(new SubmitKnowledgeFeedbackDto
            {
                KnowledgeItemId = knowledge.Item.Id,
                Type = KnowledgeFeedbackType.Error,
                Title = "Unauthorized feedback",
                Description = "Must not be created."
            }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedbackAppService.ProcessAsync(submitted.Id, new ProcessKnowledgeFeedbackDto()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedbackAppService.CreateRevisionAsync(submitted.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedbackAppService.ResolveAsync(submitted.Id, new ResolveKnowledgeFeedbackDto { Resolution = "Unauthorized" }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedbackAppService.CloseAsync(submitted.Id, new CloseKnowledgeFeedbackDto { CloseReason = "Unauthorized" }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _feedbackAppService.LinkVersionAsync(submitted.Id, new LinkKnowledgeFeedbackVersionDto
            {
                LinkedKnowledgeVersionId = knowledge.Version.Id
            }));
        }

        await WithUnitOfWorkAsync(async () =>
        {
            var feedback = await _feedbackRepository.GetAsync(submitted.Id);
            feedback.Status.ShouldBe(KnowledgeFeedbackStatus.Submitted);
            feedback.LinkedKnowledgeVersionId.ShouldBeNull();
            (await _feedbackRepository.CountAsync(x => x.KnowledgeItemId == knowledge.Item.Id)).ShouldBe(1);
            (await _versionRepository.CountAsync(x => x.KnowledgeItemId == knowledge.Item.Id)).ShouldBe(1);
        });

        using (ChangeUser(OwnerUserId))
        {
            (await _feedbackAppService.GetAsync(submitted.Id)).Description.ShouldBe(submitted.Description);
            await _feedbackAppService.ProcessAsync(submitted.Id, new ProcessKnowledgeFeedbackDto());
            (await _feedbackAppService.CreateRevisionAsync(submitted.Id)).LinkedKnowledgeVersionId.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Personal_Permission_Alone_Should_Allow_Owner_Management_Recovery_And_Revision()
    {
        var personal = await InsertKnowledgeAsync(personal: true, published: true);
        var organization = await InsertKnowledgeAsync(personal: false, published: true);
        await _feedbackAppService.SubmitAsync(new SubmitKnowledgeFeedbackDto
        {
            KnowledgeItemId = organization.Item.Id,
            Type = KnowledgeFeedbackType.Error,
            Title = "Organizational correction",
            Description = "Must be excluded when organizational feedback permission is absent."
        });
        var services = CreateServicesWithPermissions(personalGranted: true, organizationGranted: false);

        using var owner = ChangeUser(OwnerUserId);
        await WithUnitOfWorkAsync(async () =>
        {
            var management = await services.Management.GetListAsync(new GetKnowledgeManagementListInput { MaxResultCount = 100 });
            management.Items.Select(x => x.Id).ShouldContain(personal.Item.Id);
            management.Items.Select(x => x.Id).ShouldNotContain(organization.Item.Id);
            management.Overview.TotalCount.ShouldBe(1);
            (await services.Management.GetAsync(personal.Item.Id)).Id.ShouldBe(personal.Item.Id);
            await Should.ThrowAsync<AbpAuthorizationException>(() => services.Management.GetAsync(organization.Item.Id));

            var jobs = await services.IndexJobs.GetListAsync(new GetKnowledgeIndexJobListInput { MaxResultCount = 100 });
            jobs.Items.Single().Id.ShouldBe(personal.Job!.Id);
            await Should.ThrowAsync<AbpAuthorizationException>(() => services.IndexJobs.GetAsync(organization.Job!.Id));
            var job = await services.IndexJobs.CreateAsync(new CreateKnowledgeIndexJobDto
            {
                KnowledgeItemId = personal.Item.Id,
                KnowledgeVersionId = personal.Version.Id
            });
            await services.IndexJobs.StartAsync(job.Id);
            var recovered = await services.IndexJobs.RecoverAsync(job.Id);
            recovered.RetryOfJobId.ShouldBe(job.Id);
            recovered.Status.ShouldBe(KnowledgeIndexJobStatus.Pending);
            (await services.IndexJobs.GetAsync(job.Id)).Status.ShouldBe(KnowledgeIndexJobStatus.Failed);
            (await services.IndexJobs.GetOperationalSummaryAsync()).PendingCount.ShouldBe(1);

            var feedback = await services.Feedback.SubmitAsync(new SubmitKnowledgeFeedbackDto
            {
                KnowledgeItemId = personal.Item.Id,
                KnowledgeVersionId = personal.Version.Id,
                Type = KnowledgeFeedbackType.Error,
                Title = "Owner-only revision",
                Description = "The owner can maintain personal materials without organizational permissions."
            });
            await services.Feedback.ProcessAsync(feedback.Id, new ProcessKnowledgeFeedbackDto());
            var revision = await services.Feedback.CreateRevisionAsync(feedback.Id);
            revision.LinkedKnowledgeVersionStatus.ShouldBe(KnowledgeVersionStatus.Draft);
            (await services.Feedback.GetListAsync(new GetKnowledgeFeedbackListInput())).Items.Single().Id.ShouldBe(feedback.Id);
        });
    }

    [Fact]
    public async Task Deleted_Personal_Library_Should_Be_Excluded_From_Management_Jobs_And_Feedback()
    {
        var knowledge = await InsertKnowledgeAsync(personal: true, published: true);
        using var owner = ChangeUser(OwnerUserId);
        var feedback = await _feedbackAppService.SubmitAsync(new SubmitKnowledgeFeedbackDto
        {
            KnowledgeItemId = knowledge.Item.Id,
            Type = KnowledgeFeedbackType.Error,
            Title = "Unavailable personal library",
            Description = "Soft-deleted libraries must not remain visible through older management endpoints."
        });
        await WithUnitOfWorkAsync(() => _libraryRepository.DeleteAsync(OwnerUserId, autoSave: true));

        (await _managementAppService.GetListAsync(new GetKnowledgeManagementListInput())).Items.ShouldBeEmpty();
        (await _indexJobAppService.GetListAsync(new GetKnowledgeIndexJobListInput())).Items.ShouldBeEmpty();
        (await _feedbackAppService.GetListAsync(new GetKnowledgeFeedbackListInput())).Items.ShouldBeEmpty();
        (await _indexJobAppService.GetOperationalSummaryAsync()).LastIndexedTime.ShouldBeNull();
        await Should.ThrowAsync<AbpAuthorizationException>(() => _managementAppService.GetAsync(knowledge.Item.Id));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _indexJobAppService.GetAsync(knowledge.Job!.Id));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _feedbackAppService.GetAsync(feedback.Id));
    }

    [Fact]
    public async Task Organizational_Permissions_Should_Not_Substitute_For_Personal_Permission_Even_For_Owner()
    {
        var knowledge = await InsertKnowledgeAsync(personal: true, published: true);
        KnowledgeFeedbackDto feedback;
        using (ChangeUser(OwnerUserId))
        {
            feedback = await _feedbackAppService.SubmitAsync(new SubmitKnowledgeFeedbackDto
            {
                KnowledgeItemId = knowledge.Item.Id,
                Type = KnowledgeFeedbackType.Error,
                Title = "Requires personal permission",
                Description = "Organizational administrator privileges are insufficient."
            });
        }

        var services = CreateServicesWithPermissions(personalGranted: false, organizationGranted: true);
        using var owner = ChangeUser(OwnerUserId);
        await WithUnitOfWorkAsync(async () =>
        {
            (await services.Management.GetListAsync(new GetKnowledgeManagementListInput())).Items.ShouldBeEmpty();
            (await services.IndexJobs.GetListAsync(new GetKnowledgeIndexJobListInput())).Items.ShouldBeEmpty();
            (await services.Feedback.GetListAsync(new GetKnowledgeFeedbackListInput())).Items.ShouldBeEmpty();
            (await services.IndexJobs.GetOperationalSummaryAsync()).LastIndexedTime.ShouldBeNull();
            await Should.ThrowAsync<AbpAuthorizationException>(() => services.Management.GetAsync(knowledge.Item.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => services.IndexJobs.RecoverAsync(knowledge.Job!.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => services.Feedback.CreateRevisionAsync(feedback.Id));
        });
    }

    [Fact]
    public async Task Stale_Vector_Hits_Should_Not_Leak_Private_Chunks_While_Legacy_Organization_Content_Remains_Visible()
    {
        var personal = await InsertKnowledgeAsync(personal: true, published: true);
        var legacy = await InsertKnowledgeAsync(personal: false, published: true);
        _vectorStore.SearchResults.Add(ToHit(personal.Chunk!));
        _vectorStore.SearchResults.Add(ToHit(legacy.Chunk!));

        using (ChangeUser(OtherUserId))
        {
            var result = await _retriever.SearchAsync(new RagSearchRequest
            {
                Query = "Teaching material",
                UserId = OtherUserId,
                TopK = 10
            });
            result.Select(x => x.KnowledgeItemId).ShouldContain(legacy.Item.Id);
            result.Select(x => x.KnowledgeItemId).ShouldNotContain(personal.Item.Id);
            result.Any(x => x.Content.Contains(personal.Version.ContentText!, StringComparison.Ordinal)).ShouldBeFalse();
            _vectorStore.SearchRequests.ShouldNotBeEmpty();

            var spoofedOwner = await _retriever.SearchAsync(new RagSearchRequest
            {
                Query = "Teaching material",
                UserId = OwnerUserId
            });
            spoofedOwner.ShouldBeEmpty();
        }

        using (ChangeUser(OwnerUserId))
        {
            var result = await _retriever.SearchAsync(new RagSearchRequest { Query = "Teaching material", UserId = OwnerUserId });
            result.Single(x => x.KnowledgeItemId == personal.Item.Id).Content.ShouldBe(personal.Version.ContentText);
        }
    }

    [Fact]
    public async Task Owner_Should_Prepare_Private_Version_Without_Granting_Others_Access()
    {
        var knowledge = await InsertKnowledgeAsync(personal: true);
        PublishKnowledgeVersionResultDto prepared;
        using (ChangeUser(OwnerUserId))
        {
            prepared = await _itemAppService.PublishVersionWithAccessPolicyAsync(knowledge.Item.Id, new PublishKnowledgeVersionInput
            {
                KnowledgeVersionId = knowledge.Version.Id,
                PermissionScopes =
                [
                    new CreateKnowledgePermissionScopeDto
                    {
                        ScopeType = KnowledgePermissionScopeType.User,
                        TargetId = OwnerUserId
                    }
                ]
            });
            prepared.Version.Status.ShouldBe(KnowledgeVersionStatus.Published);
            prepared.IndexJob.KnowledgeItemId.ShouldBe(knowledge.Item.Id);
            (await _itemAppService.GetAsync(knowledge.Item.Id)).CurrentPublishedVersionId.ShouldBe(knowledge.Version.Id);
        }

        using (ChangeUser(OtherUserId))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _itemAppService.GetAsync(knowledge.Item.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _indexJobAppService.GetAsync(prepared.IndexJob.Id));
        }
    }

    [Fact]
    public async Task Legacy_Organization_Item_Should_Keep_Existing_Empty_Scope_Read_And_Management_Semantics()
    {
        var knowledge = await InsertKnowledgeAsync(personal: false);

        using (ChangeUser(OtherUserId))
        {
            (await _itemAppService.GetAsync(knowledge.Item.Id)).OwnerUserId.ShouldBeNull();
            (await _managementAppService.GetAsync(knowledge.Item.Id)).Id.ShouldBe(knowledge.Item.Id);
            var version = await _itemAppService.CreateVersionAsync(knowledge.Item.Id, new CreateKnowledgeVersionDto
            {
                Title = "Legacy organization v2",
                ContentText = "Existing administrators keep managing organizational material."
            });
            var published = await _itemAppService.PublishVersionAsync(knowledge.Item.Id, version.Id);
            published.Status.ShouldBe(KnowledgeVersionStatus.Published);
            (await _itemAppService.GetVersionsAsync(knowledge.Item.Id)).Items.Count.ShouldBe(2);
        }
    }

    private IDisposable ChangeUser(Guid userId)
    {
        // 两个身份都带 admin，且公共测试模块放行功能授权；拒绝必须来自归属边界。
        return _principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
            new Claim(AbpClaimTypes.UserName, $"test-{userId:N}"),
            new Claim(AbpClaimTypes.Role, "admin")
        ], "Test")));
    }

    private (KnowledgeManagementAppService Management, KnowledgeIndexJobAppService IndexJobs, KnowledgeFeedbackAppService Feedback)
        CreateServicesWithPermissions(bool personalGranted, bool organizationGranted)
    {
        var permissionChecker = Substitute.For<IPermissionChecker>();
        permissionChecker.IsGrantedAsync(Arg.Any<string>()).Returns(call =>
            call.Arg<string>() == KnowPulsePermissions.KnowledgeBase.PersonalLibrary ? personalGranted : organizationGranted);
        var lazyProvider = Substitute.For<IAbpLazyServiceProvider>();
        lazyProvider.LazyGetRequiredService<IPermissionChecker>().Returns(permissionChecker);
        lazyProvider.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>());
        lazyProvider.LazyGetRequiredService<ICurrentTenant>().Returns(GetRequiredService<ICurrentTenant>());
        lazyProvider.LazyGetRequiredService<IAsyncQueryableExecuter>().Returns(GetRequiredService<IAsyncQueryableExecuter>());
        lazyProvider.LazyGetService<IGuidGenerator>(Arg.Any<IGuidGenerator>()).Returns(GetRequiredService<IGuidGenerator>());
        lazyProvider.LazyGetRequiredService<IRepository<KnowledgeLibrary, Guid>>().Returns(_libraryRepository);
        lazyProvider.LazyGetRequiredService<IRepository<KnowledgeItem, Guid>>().Returns(_itemRepository);
        lazyProvider.LazyGetRequiredService<IRepository<KnowledgeVersion, Guid>>().Returns(_versionRepository);
        lazyProvider.LazyGetRequiredService<IRepository<SchoolKnowledgeShare, Guid>>()
            .Returns(GetRequiredService<IRepository<SchoolKnowledgeShare, Guid>>());
        lazyProvider.LazyGetRequiredService<IRepository<KnowPulse.Education.School, Guid>>()
            .Returns(GetRequiredService<IRepository<KnowPulse.Education.School, Guid>>());
        lazyProvider.LazyGetRequiredService<IRepository<KnowPulse.Education.SchoolMembership, Guid>>()
            .Returns(GetRequiredService<IRepository<KnowPulse.Education.SchoolMembership, Guid>>());
        lazyProvider.LazyGetRequiredService<IRepository<Volo.Abp.Identity.IdentityUser, Guid>>()
            .Returns(GetRequiredService<IRepository<Volo.Abp.Identity.IdentityUser, Guid>>());

        var management = new KnowledgeManagementAppService(
            _itemRepository,
            _libraryRepository,
            GetRequiredService<IRepository<KnowledgeCategory, Guid>>(),
            _versionRepository,
            GetRequiredService<IRepository<KnowledgeAttachment, Guid>>(),
            _scopeRepository,
            _jobRepository,
            _feedbackRepository,
            GetRequiredService<IKnowledgeAccessContextProvider>()) { LazyServiceProvider = lazyProvider };
        var indexJobs = new KnowledgeIndexJobAppService(
            _jobRepository, _itemRepository, _libraryRepository, _versionRepository,
            GetRequiredService<IKnowledgeIndexJobProcessor>()) { LazyServiceProvider = lazyProvider };
        var feedback = new KnowledgeFeedbackAppService(
            _feedbackRepository, _itemRepository, _libraryRepository, _versionRepository, _jobRepository,
            _scopeRepository, GetRequiredService<IKnowledgeAccessContextProvider>()) { LazyServiceProvider = lazyProvider };
        return (management, indexJobs, feedback);
    }

    private Task<KnowledgeFixture> InsertKnowledgeAsync(
        bool personal,
        bool published = false,
        KnowledgePermissionScopeType? scopeType = null)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            KnowledgeLibrary? library = null;
            if (personal)
            {
                library = await _libraryRepository.FindAsync(OwnerUserId);
                library ??= await _libraryRepository.InsertAsync(new KnowledgeLibrary(OwnerUserId, null), autoSave: true);
            }

            var title = $"isolation-{Guid.NewGuid():N}";
            var item = new KnowledgeItem(Guid.NewGuid(), null, title, null, "Teaching material", library?.Id, personal ? OwnerUserId : null);
            var version = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1, title, $"{title} teaching contents.");
            if (published)
            {
                version.Publish(DateTime.UtcNow);
                item.SetCurrentPublishedVersion(version.Id);
            }

            await _itemRepository.InsertAsync(item, autoSave: true);
            await _versionRepository.InsertAsync(version, autoSave: true);

            KnowledgePermissionScope? scope = null;
            if (scopeType.HasValue)
            {
                scope = await _scopeRepository.InsertAsync(new KnowledgePermissionScope(
                    Guid.NewGuid(), null, item.Id, scopeType.Value,
                    scopeType == KnowledgePermissionScopeType.User ? OwnerUserId : null), autoSave: true);
            }

            KnowledgeIndexJob? job = null;
            KnowledgeChunk? chunk = null;
            if (published)
            {
                job = new KnowledgeIndexJob(Guid.NewGuid(), null, item.Id, version.Id, KnowledgeIndexJobTriggerType.Publish);
                job.MarkIndexed(DateTime.UtcNow);
                await _jobRepository.InsertAsync(job, autoSave: true);
                chunk = await _chunkRepository.InsertAsync(new KnowledgeChunk(
                    Guid.NewGuid(), null, item.Id, version.Id, job.Id, 0, version.ContentText!,
                    Guid.NewGuid().ToString("N"), title, tokenCount: 10), autoSave: true);
            }

            return new KnowledgeFixture(item, version, scope, job, chunk);
        });
    }

    private async Task<KnowledgeAttachmentDto> UploadTextAsync(KnowledgeFixture knowledge, string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        using var stream = new MemoryStream(bytes);
        using var file = new RemoteStreamContent(stream, fileName, "text/plain", bytes.Length);
        return await _itemAppService.UploadAttachmentAsync(knowledge.Item.Id, knowledge.Version.Id, file);
    }

    private static VectorSearchHit ToHit(KnowledgeChunk chunk)
    {
        // 模拟旧 Qdrant payload 没有新的归属信息；即使候选过滤失效，主库复核也必须拒绝。
        return new VectorSearchHit
        {
            ChunkId = chunk.Id,
            KnowledgeItemId = chunk.KnowledgeItemId,
            KnowledgeVersionId = chunk.KnowledgeVersionId,
            Content = "Vector payload is not authoritative.",
            Score = 0.9,
            Payload = new KnowledgeVectorPayload
            {
                TenantId = chunk.TenantId,
                KnowledgeItemId = chunk.KnowledgeItemId,
                KnowledgeVersionId = chunk.KnowledgeVersionId,
                ChunkId = chunk.Id,
                IsPublished = true,
                SecurityLevel = 0,
                SourceTitle = chunk.SourceTitle
            }
        };
    }

    private sealed record KnowledgeFixture(
        KnowledgeItem Item,
        KnowledgeVersion Version,
        KnowledgePermissionScope? Scope,
        KnowledgeIndexJob? Job,
        KnowledgeChunk? Chunk);
}
