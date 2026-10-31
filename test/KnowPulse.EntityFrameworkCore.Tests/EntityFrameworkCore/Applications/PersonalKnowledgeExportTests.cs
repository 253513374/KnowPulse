using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class PersonalKnowledgeExportTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly IKnowledgePersonalExportAppService _service;
    private readonly PersonalKnowledgeExportProcessor _processor;
    private readonly IBlobContainer<KnowledgeAttachmentBlobContainer> _blobs;
    private readonly PersonalKnowledgeExportOptions _limits;

    public PersonalKnowledgeExportTests()
    {
        _service = GetRequiredService<IKnowledgePersonalExportAppService>();
        _processor = GetRequiredService<PersonalKnowledgeExportProcessor>();
        _blobs = GetRequiredService<IBlobContainer<KnowledgeAttachmentBlobContainer>>();
        _limits = GetRequiredService<IOptions<PersonalKnowledgeExportOptions>>().Value;
    }

    [Fact]
    public async Task Async_Idempotent_Export_Should_Contain_All_Own_Versions_And_Originals_Only()
    {
        var own = await CreateSourceAsync(_owner, "自己的教案", "自己的正文");
        var foreign = await CreateSourceAsync(Guid.NewGuid(), "他人秘密", "他人正文");
        await WithUnitOfWorkAsync(async () =>
        {
            var material = await Repo<KnowledgeItem>().GetAsync(own.Item.Id);
            material.OrganizePersonalMaterial(PersonalMaterialType.LessonPlan, 2025, "数学", ["函数", "复盘"]);
            await Repo<KnowledgeItem>().UpdateAsync(material, autoSave: true);
            var archived = new KnowledgeVersion(Guid.NewGuid(), null, own.Item.Id, 2, "归档反思", "归档正文");
            archived.Archive(DateTime.UtcNow);
            await Repo<KnowledgeVersion>().InsertAsync(archived, autoSave: true);
            var org = new KnowledgeItem(Guid.NewGuid(), null, "组织受限资料");
            await Repo<KnowledgeItem>().InsertAsync(org, autoSave: true);
            await Repo<KnowledgeVersion>().InsertAsync(new KnowledgeVersion(Guid.NewGuid(), null, org.Id, 1, "组织正文", "受限答案"), autoSave: true);
            var school = new School(Guid.NewGuid(), null, $"X{Guid.NewGuid():N}", "共享学校");
            await Repo<School>().InsertAsync(school, autoSave: true);
            await Repo<SchoolKnowledgeShare>().InsertAsync(new SchoolKnowledgeShare(Guid.NewGuid(), null,
                foreign.Item.OwnerUserId!.Value, foreign.Item.LibraryId!.Value, foreign.Item.Id, foreign.Version.Id,
                school.Id, "学校共享", null, SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Download,
                DateTime.UtcNow), autoSave: true);
        });
        using var user = ChangeUser(_owner);
        var input = new CreatePersonalKnowledgeExportDto { IdempotencyKey = Guid.NewGuid() };
        var first = await _service.CreateAsync(input);
        first.Status.ShouldBe(PersonalKnowledgeExportStatus.Pending);
        first.CanDownload.ShouldBeFalse();
        (await _service.CreateAsync(input)).Id.ShouldBe(first.Id);
        (await _service.GetListAsync(new())).Items.Count.ShouldBe(1);
        await Should.ThrowAsync<BusinessException>(() => _service.DownloadAsync(first.Id));

        await _processor.ProcessAsync(first.Id);
        await _processor.ProcessAsync(first.Id); // Repeated delivery does not rebuild the completed object.
        var complete = await _service.GetAsync(first.Id);
        complete.Status.ShouldBe(PersonalKnowledgeExportStatus.Completed);
        complete.CanDownload.ShouldBeTrue();
        complete.ExpiresAtUtc.ShouldNotBeNull();
        using var download = await _service.DownloadAsync(first.Id);
        using var bytes = new MemoryStream();
        await download.GetStream().CopyToAsync(bytes);
        bytes.Position = 0;
        using var zip = new ZipArchive(bytes);
        var manifest = await ReadTextAsync(zip.GetEntry("manifest.json")!);
        using var metadata = JsonDocument.Parse(manifest);
        var itemMetadata = metadata.RootElement.GetProperty("Items").EnumerateArray().Single();
        itemMetadata.GetProperty("MaterialType").GetString().ShouldBe("LessonPlan");
        itemMetadata.GetProperty("MaterialYear").GetInt32().ShouldBe(2025);
        itemMetadata.GetProperty("SubjectName").GetString().ShouldBe("数学");
        itemMetadata.GetProperty("CustomTags").EnumerateArray().Select(x => x.GetString()).ShouldBe(["函数", "复盘"]);
        metadata.RootElement.GetProperty("Versions").EnumerateArray().Select(x => x.GetProperty("Title").GetString()).ShouldContain("归档反思");
        manifest.ShouldNotContain(foreign.Item.Id.ToString());
        manifest.ShouldNotContain("BlobName");
        manifest.ShouldNotContain("ConcurrencyStamp");
        zip.Entries.ShouldAllBe(x => !x.FullName.Contains("..") && !x.FullName.StartsWith('/'));
        (await ReadTextAsync(zip.GetEntry($"items/{own.Item.Id:N}/versions/{own.Version.Id:N}/content.txt")!)).ShouldBe("自己的正文");
        using var original = zip.GetEntry($"attachments/{own.Attachment.Id:N}.txt")!.Open();
        using var originalBytes = new MemoryStream();
        await original.CopyToAsync(originalBytes);
        originalBytes.ToArray().ShouldBe(new byte[] { 1, 2, 3, 4 });
        var stored = await LoadAsync(first.Id);
        stored.TemporaryFilePending.ShouldBeFalse();
    }

    [Fact]
    public async Task Administrative_Claims_And_Cross_Tenant_Cannot_Access_Anothers_Export()
    {
        await CreateSourceAsync(_owner);
        PersonalKnowledgeExportDto job;
        using (ChangeUser(_owner)) job = await _service.CreateAsync(new() { IdempotencyKey = Guid.NewGuid() });
        using (ChangeUser(Guid.NewGuid()))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetAsync(job.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.DownloadAsync(job.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.RetryAsync(job.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CancelAsync(job.Id));
        }
        using (ChangeUser(_owner))
        using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetAsync(job.Id));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("item")]
    [InlineData("account")]
    public async Task Each_Download_And_Completed_Job_Poll_Should_Recheck_Current_Facts(string change)
    {
        var source = await CreateSourceAsync(_owner);
        using var user = ChangeUser(_owner);
        var job = await _service.CreateAsync(new() { IdempotencyKey = Guid.NewGuid() });
        await _processor.ProcessAsync(job.Id);
        var stored = await LoadAsync(job.Id);
        await WithUnitOfWorkAsync(async () =>
        {
            if (change == "version") await Repo<KnowledgeVersion>().DeleteAsync(source.Version.Id, autoSave: true);
            else if (change == "item") await Repo<KnowledgeItem>().DeleteAsync(source.Item.Id, autoSave: true);
            else
            {
                var account = await Repo<IdentityUser>().GetAsync(_owner);
                account.SetIsActive(false);
                await Repo<IdentityUser>().UpdateAsync(account, autoSave: true);
            }
        });
        await Should.ThrowAsync<BusinessException>(() => _service.DownloadAsync(job.Id));
        await _processor.ProcessAsync(job.Id);
        var failed = await LoadAsync(job.Id);
        failed.Status.ShouldBe(PersonalKnowledgeExportStatus.Failed);
        failed.BlobName.ShouldBeNull();
        (await _blobs.ExistsAsync(stored.BlobName!)).ShouldBeFalse();
    }

    [Fact]
    public async Task Concurrent_Draft_Edit_Should_Fail_Then_Explicit_Retry_Captures_A_New_Snapshot()
    {
        var source = await CreateSourceAsync(_owner);
        using var user = ChangeUser(_owner);
        var job = await _service.CreateAsync(new() { IdempotencyKey = Guid.NewGuid() });
        var proxy = ProxyBlobs(async () =>
        {
            using var editUow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false);
            var version = await Repo<KnowledgeVersion>().GetAsync(source.Version.Id);
            version.UpdateDraft("修订标题", "修订正文");
            await Repo<KnowledgeVersion>().UpdateAsync(version, autoSave: true);
            await editUow.CompleteAsync();
        });
        await CreateProcessor(proxy).ProcessAsync(job.Id);
        var failed = await _service.GetAsync(job.Id);
        failed.Status.ShouldBe(PersonalKnowledgeExportStatus.Failed);
        failed.FailureCode.ShouldBe(PersonalKnowledgeExportErrorCodes.SourceChanged);
        (await _service.RetryAsync(job.Id)).Status.ShouldBe(PersonalKnowledgeExportStatus.Pending);
        await _processor.ProcessAsync(job.Id);
        var done = await _service.GetAsync(job.Id);
        done.Status.ShouldBe(PersonalKnowledgeExportStatus.Completed);
        done.RetryCount.ShouldBe(1);
    }

    [Fact]
    public async Task Actual_Original_Length_And_Uncompressed_Limits_Must_Be_Checked()
    {
        var source = await CreateSourceAsync(_owner, declaredSize: 1);
        using var user = ChangeUser(_owner);
        var corrupt = await _service.CreateAsync(new() { IdempotencyKey = Guid.NewGuid() });
        await _processor.ProcessAsync(corrupt.Id);
        (await _service.GetAsync(corrupt.Id)).FailureCode.ShouldBe(PersonalKnowledgeExportErrorCodes.SourceChanged);
        (await LoadAsync(corrupt.Id)).BlobName.ShouldBeNull();
        _limits.MaxSourceBytes = 1; // Even a highly compressible manifest/body counts before compression.
        var bounded = await _service.CreateAsync(new() { IdempotencyKey = Guid.NewGuid() });
        await _processor.ProcessAsync(bounded.Id);
        (await _service.GetAsync(bounded.Id)).FailureCode.ShouldBe(PersonalKnowledgeExportErrorCodes.LimitExceeded);
        (await WithUnitOfWorkAsync(() => Repo<KnowledgeVersion>().CountAsync())).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Cancellation_During_Copy_Should_Prevent_Completion_And_Clean_Artifact()
    {
        await CreateSourceAsync(_owner);
        using var user = ChangeUser(_owner);
        var job = await _service.CreateAsync(new() { IdempotencyKey = Guid.NewGuid() });
        await CreateProcessor(ProxyBlobs(async () => { await _service.CancelAsync(job.Id); })).ProcessAsync(job.Id);
        var cancelled = await _service.GetAsync(job.Id);
        cancelled.Status.ShouldBe(PersonalKnowledgeExportStatus.Cancelled);
        cancelled.CanDownload.ShouldBeFalse();
        var stored = await LoadAsync(job.Id);
        stored.BlobName.ShouldBeNull();
        stored.TemporaryFilePending.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cleanup_During_Save_Must_Retain_Old_Target_And_Old_Finally_Must_Not_Delete_New_Run(bool recoverAndRetry)
    {
        await CreateSourceAsync(_owner);
        using var user = ChangeUser(_owner);
        var job = await _service.CreateAsync(new() { IdempotencyKey = Guid.NewGuid() });
        string? oldBlob = null;
        string? newBlob = null;
        Guid oldRun = Guid.Empty;
        var proxy = ProxyBlobs(() => Task.CompletedTask);
        proxy.SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            oldBlob = call.ArgAt<string>(0);
            var before = await LoadAsync(job.Id);
            oldRun = before.RunId!.Value;
            if (recoverAndRetry)
            {
                await WithUnitOfWorkAsync(async () =>
                {
                    var stale = await Repo<PersonalKnowledgeExport>().GetAsync(job.Id);
                    stale.Heartbeat(DateTime.UtcNow.AddDays(-1));
                    await Repo<PersonalKnowledgeExport>().UpdateAsync(stale, autoSave: true);
                });
            }
            else await _service.CancelAsync(job.Id);

            // The real object does not exist yet. Deleting it must not discard a still-writable target.
            await _processor.ProcessAsync(job.Id);
            var cleaned = await LoadAsync(job.Id);
            cleaned.BlobName.ShouldBe(oldBlob);
            cleaned.RunStopped.ShouldBeFalse();
            if (recoverAndRetry)
            {
                await _service.RetryAsync(job.Id);
                await _processor.ProcessAsync(job.Id);
                var newer = await LoadAsync(job.Id);
                newer.Status.ShouldBe(PersonalKnowledgeExportStatus.Completed);
                newer.RunId.ShouldNotBe(oldRun);
                newBlob = newer.BlobName;
                var retired = newer.GetRetiredArtifacts().Single();
                retired.RunId.ShouldBe(oldRun);
                retired.BlobName.ShouldBe(oldBlob);
                retired.RunStopped.ShouldBeFalse();
            }
            await _blobs.SaveAsync(oldBlob!, call.ArgAt<Stream>(1), call.ArgAt<bool>(2), call.ArgAt<CancellationToken>(3));
            (await _blobs.ExistsAsync(oldBlob!)).ShouldBeTrue();
        });

        await CreateProcessor(proxy).ProcessAsync(job.Id);
        (await _blobs.ExistsAsync(oldBlob!)).ShouldBeFalse();
        var after = await LoadAsync(job.Id);
        if (recoverAndRetry)
        {
            after.Status.ShouldBe(PersonalKnowledgeExportStatus.Completed);
            after.BlobName.ShouldBe(newBlob);
            after.HasRetiredArtifacts.ShouldBeFalse();
            (await _blobs.ExistsAsync(newBlob!)).ShouldBeTrue();
            using var download = await _service.DownloadAsync(job.Id);
            download.GetStream().Length.ShouldBeGreaterThan(0);
        }
        else
        {
            after.Status.ShouldBe(PersonalKnowledgeExportStatus.Cancelled);
            after.BlobName.ShouldBeNull();
            after.TemporaryFilePending.ShouldBeFalse();
            after.RunStopped.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Storage_And_Cleanup_Failures_Should_Retain_Recovery_State_Then_Retry()
    {
        await CreateSourceAsync(_owner);
        using var user = ChangeUser(_owner);
        var job = await _service.CreateAsync(new() { IdempotencyKey = Guid.NewGuid() });
        var failing = ProxyBlobs(() => Task.CompletedTask);
        failing.SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("must not appear in status")));
        failing.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<bool>(new IOException("cleanup unavailable")));
        await CreateProcessor(failing).ProcessAsync(job.Id);
        var failed = await _service.GetAsync(job.Id);
        failed.FailureCode.ShouldBe(PersonalKnowledgeExportErrorCodes.StorageFailed);
        (await LoadAsync(job.Id)).BlobName.ShouldNotBeNull();
        await _processor.ProcessAsync(job.Id);
        (await LoadAsync(job.Id)).BlobName.ShouldBeNull();
        await _service.RetryAsync(job.Id);
        await _processor.ProcessAsync(job.Id);
        (await _service.GetAsync(job.Id)).Status.ShouldBe(PersonalKnowledgeExportStatus.Completed);
    }

    [Fact]
    public async Task Expiry_And_Stale_Run_Should_Be_Persisted_And_Cleaned_Without_Changing_Sources()
    {
        var source = await CreateSourceAsync(_owner);
        using var user = ChangeUser(_owner);
        var expired = new PersonalKnowledgeExport(Guid.NewGuid(), null, _owner);
        expired.Start(Guid.NewGuid(), DateTime.UtcNow.AddDays(-2), "personal-exports/test-expired.zip");
        expired.SetSnapshot("[]", DateTime.UtcNow.AddDays(-2));
        expired.Complete(3, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1));
        var stale = new PersonalKnowledgeExport(Guid.NewGuid(), null, _owner);
        stale.Start(Guid.NewGuid(), DateTime.UtcNow.AddDays(-2), "personal-exports/test-stale.zip");
        await WithUnitOfWorkAsync(async () =>
        {
            await Repo<PersonalKnowledgeExport>().InsertAsync(expired, autoSave: true);
            await Repo<PersonalKnowledgeExport>().InsertAsync(stale, autoSave: true);
        });
        await _blobs.SaveAsync(expired.BlobName!, new byte[] { 1, 2, 3 });
        await Should.ThrowAsync<BusinessException>(() => _service.DownloadAsync(expired.Id));
        await _processor.ProcessAsync(expired.Id);
        (await LoadAsync(expired.Id)).Status.ShouldBe(PersonalKnowledgeExportStatus.Expired);
        (await _blobs.ExistsAsync(expired.BlobName!)).ShouldBeFalse();
        await _processor.ProcessAsync(stale.Id);
        (await _service.GetAsync(stale.Id)).CanRetry.ShouldBeTrue();
        (await WithUnitOfWorkAsync(() => Repo<KnowledgeVersion>().GetAsync(source.Version.Id))).ContentText.ShouldBe("自己的正文");
    }

    [Fact]
    public async Task Full_Batch_Of_Old_Completed_Jobs_Must_Not_Starve_New_Pending_Job()
    {
        await CreateSourceAsync(_owner);
        await WithUnitOfWorkAsync(async () =>
        {
            for (var i = 0; i < _limits.BatchSize; i++)
            {
                var old = new PersonalKnowledgeExport(Guid.NewGuid(), null, _owner);
                old.Start(Guid.NewGuid(), DateTime.UtcNow.AddDays(-1), $"old/{i}.zip");
                old.SetSnapshot("[]", DateTime.UtcNow.AddDays(-1));
                old.Complete(1, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
                await Repo<PersonalKnowledgeExport>().InsertAsync(old, autoSave: true);
            }
        });
        using var user = ChangeUser(_owner);
        var pending = await _service.CreateAsync(new() { IdempotencyKey = Guid.NewGuid() });
        var due = await WithUnitOfWorkAsync(() => _processor.GetDueJobsAsync());
        due.Count.ShouldBe(_limits.BatchSize);
        due[0].Id.ShouldBe(pending.Id);
        await _processor.ProcessAsync(pending.Id);
        var before = (await LoadAsync(pending.Id)).LastHeartbeatAtUtc;
        await _processor.ProcessAsync(pending.Id);
        (await LoadAsync(pending.Id)).LastHeartbeatAtUtc!.Value.ShouldBeGreaterThanOrEqualTo(before!.Value);
    }

    [Fact]
    public async Task Retry_Must_Not_Overwrite_Old_Run_While_Its_Private_Temporary_File_Is_Locked()
    {
        await CreateSourceAsync(_owner);
        using var user = ChangeUser(_owner);
        var job = new PersonalKnowledgeExport(Guid.NewGuid(), null, _owner);
        var oldRun = Guid.NewGuid();
        job.Start(oldRun, DateTime.UtcNow.AddDays(-1), $"personal-exports/locked-{job.Id:N}.zip");
        job.MarkRunStopped(oldRun); // A known stopped writer can still leave a file held by another reader.
        var path = Path.Combine(Path.GetTempPath(), "knowpulse-personal-exports", $"{job.Id:N}-{oldRun:N}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await WithUnitOfWorkAsync(() => Repo<PersonalKnowledgeExport>().InsertAsync(job, autoSave: true));
        try
        {
            using (var locked = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                await locked.WriteAsync(Encoding.UTF8.GetBytes("private original temporary bytes"));
                await _processor.ProcessAsync(job.Id); // Lease recovery cannot remove an open Windows file.
                var failed = await LoadAsync(job.Id);
                failed.Status.ShouldBe(PersonalKnowledgeExportStatus.Failed);
                failed.TemporaryFilePending.ShouldBeTrue();
                await _service.RetryAsync(job.Id);
                await Should.ThrowAsync<BusinessException>(() => _processor.ProcessAsync(job.Id));
                var pending = await LoadAsync(job.Id);
                pending.Status.ShouldBe(PersonalKnowledgeExportStatus.Pending);
                pending.RunId.ShouldBe(oldRun);
                pending.TemporaryFilePending.ShouldBeTrue();
            }
            await _processor.ProcessAsync(job.Id);
            (await _service.GetAsync(job.Id)).Status.ShouldBe(PersonalKnowledgeExportStatus.Completed);
            File.Exists(path).ShouldBeFalse();
        }
        finally { File.Delete(path); }
    }

    private async Task<(KnowledgeItem Item, KnowledgeVersion Version, KnowledgeAttachment Attachment)> CreateSourceAsync(
        Guid owner, string title = "自己的教案", string body = "自己的正文", long declaredSize = 4)
    {
        var item = new KnowledgeItem(Guid.NewGuid(), null, title, libraryId: owner, ownerUserId: owner);
        var version = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1, title, body);
        var attachment = new KnowledgeAttachment(Guid.NewGuid(), null, version.Id, "../../dangerous.txt", $"original/{Guid.NewGuid():N}", "text/plain", declaredSize);
        await WithUnitOfWorkAsync(async () =>
        {
            await Repo<IdentityUser>().InsertAsync(new IdentityUser(owner, $"exp-{owner:N}", $"exp-{owner:N}@test.invalid"), autoSave: true);
            await Repo<KnowledgeLibrary>().InsertAsync(new KnowledgeLibrary(owner, null), autoSave: true);
            await Repo<KnowledgeItem>().InsertAsync(item, autoSave: true);
            await Repo<KnowledgeVersion>().InsertAsync(version, autoSave: true);
            await Repo<KnowledgeAttachment>().InsertAsync(attachment, autoSave: true);
        });
        await _blobs.SaveAsync(attachment.BlobName, new byte[] { 1, 2, 3, 4 });
        return (item, version, attachment);
    }

    private IBlobContainer<KnowledgeAttachmentBlobContainer> ProxyBlobs(Func<Task> onRead)
    {
        var proxy = Substitute.For<IBlobContainer<KnowledgeAttachmentBlobContainer>>();
        proxy.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => ReadInterleavedAsync(call.ArgAt<string>(0), call.ArgAt<CancellationToken>(1), onRead));
        proxy.SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => _blobs.SaveAsync(call.ArgAt<string>(0), call.ArgAt<Stream>(1), call.ArgAt<bool>(2), call.ArgAt<CancellationToken>(3)));
        proxy.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _blobs.DeleteAsync(call.ArgAt<string>(0), call.ArgAt<CancellationToken>(1)));
        return proxy;
    }

    private async Task<Stream> ReadInterleavedAsync(string name, CancellationToken ct, Func<Task> onRead)
    {
        using var original = await _blobs.GetAsync(name, ct);
        using var copy = new MemoryStream();
        await original.CopyToAsync(copy);
        return new InterleavedReadStream(copy.ToArray(), onRead);
    }

    private PersonalKnowledgeExportProcessor CreateProcessor(IBlobContainer<KnowledgeAttachmentBlobContainer> blobs) => new(
        Repo<PersonalKnowledgeExport>(), blobs, GetRequiredService<IUnitOfWorkManager>(), GetRequiredService<IAbpLazyServiceProvider>(),
        GetRequiredService<IOptions<PersonalKnowledgeExportOptions>>(), GetRequiredService<ILogger<PersonalKnowledgeExportProcessor>>());

    private Task<PersonalKnowledgeExport> LoadAsync(Guid id) => WithUnitOfWorkAsync(() => Repo<PersonalKnowledgeExport>().GetAsync(id));
    private IRepository<T, Guid> Repo<T>() where T : class, Volo.Abp.Domain.Entities.IEntity<Guid> => GetRequiredService<IRepository<T, Guid>>();
    private IDisposable ChangeUser(Guid id) => GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity(
        new[] { new Claim(AbpClaimTypes.UserId, id.ToString()), new Claim(AbpClaimTypes.UserName, "export-test"), new Claim(AbpClaimTypes.Role, "admin") }, "Test")));
    private static async Task<string> ReadTextAsync(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
    private sealed class InterleavedReadStream(byte[] bytes, Func<Task> action) : MemoryStream(bytes)
    {
        private bool _called;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_called) { _called = true; await action(); }
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
