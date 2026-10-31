using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Claims;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class PersonalKnowledgeAttachmentTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly IKnowledgeItemAppService _items;
    private readonly PersonalKnowledgeAttachmentOptions _limits;

    public PersonalKnowledgeAttachmentTests()
    {
        _items = GetRequiredService<IKnowledgeItemAppService>();
        _limits = GetRequiredService<IOptions<PersonalKnowledgeAttachmentOptions>>().Value;
        _limits.MaxFileBytes = 4;
        _limits.MaxFilesPerVersion = 2;
    }

    [Fact]
    public async Task Policy_And_Upload_Should_Use_Configured_Limits_And_Actual_Size()
    {
        using var user = ChangeUser(_owner);
        var policy = await GetRequiredService<IKnowledgeLibraryAppService>().GetUploadPolicyAsync();
        policy.MaxFileBytes.ShouldBe(4);
        policy.MaxFilesPerVersion.ShouldBe(2);
        var (item, version) = await CreateDraftAsync();
        using var file = File(new byte[] { 1, 2, 3, 4 }, declaredLength: 1);
        var uploaded = await _items.UploadAttachmentAsync(item.Id, version.Id, file);
        uploaded.Size.ShouldBe(4);
        await AssertContentAsync(item.Id, version.Id, uploaded.Id, new byte[] { 1, 2, 3, 4 });
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1L)]
    [InlineData(4L)]
    public async Task Unknown_Or_Forged_Length_Must_Not_Allow_Actual_Oversize(long? declaredLength)
    {
        using var user = ChangeUser(_owner);
        var (item, version) = await CreateDraftAsync();
        using var file = File(new byte[] { 1, 2, 3, 4, 5, 6 }, declaredLength);
        (await Should.ThrowAsync<BusinessException>(() => _items.UploadAttachmentAsync(item.Id, version.Id, file)))
            .Code.ShouldBe(KnowPulseDomainErrorCodes.PersonalKnowledgeAttachmentTooLarge);
        (await _items.GetAttachmentsAsync(item.Id, version.Id)).Items.ShouldBeEmpty();
        await AssertDraftAsync(version.Id);
    }

    [Fact]
    public async Task Size_Count_And_Read_Failures_Should_Preserve_Previous_Attachments_And_Draft()
    {
        using var user = ChangeUser(_owner);
        var (item, version) = await CreateDraftAsync();
        var first = await UploadAsync(item.Id, version.Id, new byte[] { 1, 2 }, "first.txt");
        using (var tooLarge = File(new byte[] { 1, 2, 3, 4, 5 }, null))
        {
            (await Should.ThrowAsync<BusinessException>(() => _items.UploadAttachmentAsync(item.Id, version.Id, tooLarge)))
                .Code.ShouldBe(KnowPulseDomainErrorCodes.PersonalKnowledgeAttachmentTooLarge);
        }
        using (var broken = RemoteFile(new InterleavedReadStream(new byte[] { 9 }, () => throw new IOException("Upload interrupted.")), null, "broken.txt"))
        {
            await Should.ThrowAsync<IOException>(() => _items.UploadAttachmentAsync(item.Id, version.Id, broken));
        }
        var second = await UploadAsync(item.Id, version.Id, new byte[] { 3 }, "second.txt");
        using (var overCount = File(new byte[] { 4 }, 1))
        {
            (await Should.ThrowAsync<BusinessException>(() => _items.UploadAttachmentAsync(item.Id, version.Id, overCount)))
                .Code.ShouldBe(KnowPulseDomainErrorCodes.PersonalKnowledgeAttachmentLimitReached);
        }
        (await _items.GetAttachmentsAsync(item.Id, version.Id)).Items.Select(x => x.Id)
            .OrderBy(x => x).ShouldBe(new[] { first.Id, second.Id }.OrderBy(x => x));
        await AssertContentAsync(item.Id, version.Id, first.Id, new byte[] { 1, 2 });
        await AssertContentAsync(item.Id, version.Id, second.Id, new byte[] { 3 });
        await AssertDraftAsync(version.Id);
    }

    [Fact]
    public async Task Only_Owner_Can_Add_And_Delete_Personal_Draft_Attachments()
    {
        var (item, version) = await CreateDraftAsync();
        KnowledgeAttachmentDto attachment;
        using (ChangeUser(_owner))
        {
            attachment = await UploadAsync(item.Id, version.Id, new byte[] { 1 });
        }
        using (ChangeUser(Guid.NewGuid()))
        {
            using var file = File(new byte[] { 2 }, 1);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _items.UploadAttachmentAsync(item.Id, version.Id, file));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _items.DeleteAttachmentAsync(item.Id, version.Id, attachment.Id));
        }
        using (ChangeUser(_owner))
        {
            await AssertContentAsync(item.Id, version.Id, attachment.Id, new byte[] { 1 });
            var beforeDelete = await WithUnitOfWorkAsync(() => Repository<KnowledgeVersion>().GetAsync(version.Id));
            await _items.DeleteAttachmentAsync(item.Id, version.Id, attachment.Id);
            (await _items.GetAttachmentsAsync(item.Id, version.Id)).Items.ShouldBeEmpty();
            var afterDelete = await WithUnitOfWorkAsync(() => Repository<KnowledgeVersion>().GetAsync(version.Id));
            afterDelete.ConcurrencyStamp.ShouldNotBe(beforeDelete.ConcurrencyStamp);
        }
    }

    [Fact]
    public async Task Prepared_Version_Attachments_Should_Be_Frozen_But_Remain_Downloadable()
    {
        using var user = ChangeUser(_owner);
        var (item, version) = await CreateDraftAsync();
        var attachment = await UploadAsync(item.Id, version.Id, new byte[] { 1 });
        await _items.PublishVersionAsync(item.Id, version.Id);
        using var file = File(new byte[] { 2 }, 1);
        (await Should.ThrowAsync<BusinessException>(() => _items.UploadAttachmentAsync(item.Id, version.Id, file)))
            .Code.ShouldBe(KnowPulseDomainErrorCodes.PersonalKnowledgeAttachmentRequiresDraft);
        (await Should.ThrowAsync<BusinessException>(() => _items.DeleteAttachmentAsync(item.Id, version.Id, attachment.Id)))
            .Code.ShouldBe(KnowPulseDomainErrorCodes.PersonalKnowledgeAttachmentRequiresDraft);
        await AssertContentAsync(item.Id, version.Id, attachment.Id, new byte[] { 1 });
    }

    [Fact]
    public async Task Interleaved_Uploads_Must_Conflict_On_Version_Before_Exceeding_Count()
    {
        using var user = ChangeUser(_owner);
        _limits.MaxFilesPerVersion = 1;
        var (item, version) = await CreateDraftAsync();
        KnowledgeAttachmentDto? winner = null;
        // The first request has counted zero files. Complete a second request before its version write.
        using var first = RemoteFile(new InterleavedReadStream(new byte[] { 1 }, async () =>
        {
            winner = await UploadAsync(item.Id, version.Id, new byte[] { 2 }, "winner.txt");
        }), 1, "loser.txt");
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _items.UploadAttachmentAsync(item.Id, version.Id, first));
        winner.ShouldNotBeNull();
        var attachments = (await _items.GetAttachmentsAsync(item.Id, version.Id)).Items;
        attachments.Count.ShouldBe(1);
        attachments.Single().Id.ShouldBe(winner.Id);
        await AssertContentAsync(item.Id, version.Id, winner.Id, new byte[] { 2 });
        var savedVersion = await WithUnitOfWorkAsync(() => Repository<KnowledgeVersion>().GetAsync(version.Id));
        savedVersion.ConcurrencyStamp.ShouldNotBe(version.ConcurrencyStamp);
        await AssertDraftAsync(version.Id);
    }

    [Fact]
    public async Task Preparation_During_Upload_Should_Freeze_The_Version_Before_Attachment_Is_Saved()
    {
        using var user = ChangeUser(_owner);
        var (item, version) = await CreateDraftAsync();
        using var file = RemoteFile(new InterleavedReadStream(new byte[] { 1 }, async () =>
        {
            // A separate request prepares the version after the upload has observed Draft.
            using var prepareUow = GetRequiredService<Volo.Abp.Uow.IUnitOfWorkManager>()
                .Begin(new Volo.Abp.Uow.AbpUnitOfWorkOptions(), requiresNew: true);
            await _items.PublishVersionAsync(item.Id, version.Id);
            await prepareUow.CompleteAsync();
        }), 1, "too-late.txt");

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _items.UploadAttachmentAsync(item.Id, version.Id, file));

        (await _items.GetAttachmentsAsync(item.Id, version.Id)).Items.ShouldBeEmpty();
        var prepared = await WithUnitOfWorkAsync(() => Repository<KnowledgeVersion>().GetAsync(version.Id));
        prepared.Status.ShouldBe(KnowledgeVersionStatus.Published);
    }

    [Fact]
    public async Task Legacy_Organization_Attachments_Should_Keep_Previous_Size_Count_And_State_Behavior()
    {
        var (item, version) = await CreateDraftAsync(personal: false);
        _limits.MaxFileBytes = 1;
        _limits.MaxFilesPerVersion = 1;
        await _items.PublishVersionAsync(item.Id, version.Id);
        var first = await UploadAsync(item.Id, version.Id, new byte[] { 1, 2 });
        var second = await UploadAsync(item.Id, version.Id, new byte[] { 3, 4 });
        (await _items.GetAttachmentsAsync(item.Id, version.Id)).Items.Count.ShouldBe(2);
        await _items.DeleteAttachmentAsync(item.Id, version.Id, first.Id);
        (await _items.GetAttachmentsAsync(item.Id, version.Id)).Items.Single().Id.ShouldBe(second.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failed_Metadata_Delete_Must_Keep_Original_Attachment_Downloadable(bool personal)
    {
        using var user = ChangeUser(_owner);
        var (item, version) = await CreateDraftAsync(personal);
        var uploaded = await UploadAsync(item.Id, version.Id, new byte[] { 1, 2, 3 });
        var metadata = await WithUnitOfWorkAsync(() => Repository<KnowledgeAttachment>().GetAsync(uploaded.Id));
        var failingRepository = Substitute.For<IRepository<KnowledgeAttachment, Guid>>();
        failingRepository.GetAsync(uploaded.Id).Returns(metadata);
        failingRepository.DeleteAsync(Arg.Any<KnowledgeAttachment>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("Simulated metadata write failure.")));
        var service = new KnowledgeItemAppService(
            Repository<KnowledgeItem>(), Repository<KnowledgeVersion>(), failingRepository,
            Repository<KnowledgePermissionScope>(), Repository<KnowledgeIndexJob>(), Repository<KnowledgeChunk>(),
            GetRequiredService<IKnowledgeAccessContextProvider>(),
            GetRequiredService<IBlobContainer<KnowledgeAttachmentBlobContainer>>())
        {
            LazyServiceProvider = GetRequiredService<IAbpLazyServiceProvider>()
        };

        await Should.ThrowAsync<IOException>(() => WithUnitOfWorkAsync(() =>
            service.DeleteAttachmentAsync(item.Id, version.Id, uploaded.Id)));

        (await _items.GetAttachmentsAsync(item.Id, version.Id)).Items.Single().Id.ShouldBe(uploaded.Id);
        await AssertContentAsync(item.Id, version.Id, uploaded.Id, new byte[] { 1, 2, 3 });
    }

    private async Task<(KnowledgeItem Item, KnowledgeVersion Version)> CreateDraftAsync(bool personal = true)
        => await WithUnitOfWorkAsync(async () =>
        {
            if (personal && await Repository<KnowledgeLibrary>().FindAsync(_owner) == null)
            {
                await Repository<KnowledgeLibrary>().InsertAsync(new KnowledgeLibrary(_owner, null), autoSave: true);
            }
            var item = new KnowledgeItem(Guid.NewGuid(), null, "个人教学附件", libraryId: personal ? _owner : null, ownerUserId: personal ? _owner : null);
            var version = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1, "教材草稿", "教学正文");
            await Repository<KnowledgeItem>().InsertAsync(item, autoSave: true);
            await Repository<KnowledgeVersion>().InsertAsync(version, autoSave: true);
            return (item, version);
        });

    private async Task<KnowledgeAttachmentDto> UploadAsync(Guid itemId, Guid versionId, byte[] content, string name = "note.txt")
    {
        using var file = File(content, content.Length, name);
        return await _items.UploadAttachmentAsync(itemId, versionId, file);
    }

    private async Task AssertContentAsync(Guid itemId, Guid versionId, Guid attachmentId, byte[] expected)
    {
        using var download = await _items.DownloadAttachmentAsync(itemId, versionId, attachmentId);
        using var content = new MemoryStream();
        await download.GetStream().CopyToAsync(content);
        content.ToArray().ShouldBe(expected);
    }

    private async Task AssertDraftAsync(Guid versionId)
        => (await WithUnitOfWorkAsync(() => Repository<KnowledgeVersion>().GetAsync(versionId)))
            .Status.ShouldBe(KnowledgeVersionStatus.Draft);

    private static IRemoteStreamContent File(byte[] content, long? declaredLength, string name = "note.txt")
        => RemoteFile(new MemoryStream(content), declaredLength, name);

    private static IRemoteStreamContent RemoteFile(Stream stream, long? declaredLength, string name)
    {
        var file = Substitute.For<IRemoteStreamContent>();
        file.FileName.Returns(name);
        file.ContentType.Returns("text/plain");
        file.ContentLength.Returns(declaredLength);
        file.GetStream().Returns(stream);
        return file;
    }

    private IRepository<T, Guid> Repository<T>() where T : class, Volo.Abp.Domain.Entities.IEntity<Guid>
        => GetRequiredService<IRepository<T, Guid>>();

    private IDisposable ChangeUser(Guid id)
        => GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(AbpClaimTypes.UserId, id.ToString()), new Claim(AbpClaimTypes.UserName, "teacher"), new Claim(AbpClaimTypes.Role, "admin")], "Test")));

    private sealed class InterleavedReadStream(byte[] content, Func<Task> onFirstRead) : MemoryStream(content)
    {
        private bool _called;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_called)
            {
                _called = true;
                await onFirstRead();
            }
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
