using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using KnowPulse.Permissions;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IPersonalNotebookAppService Notebooks => GetRequiredService<IPersonalNotebookAppService>();
    private static CreatePersonalNotebookDto NewNote() => new() { OperationId = Guid.NewGuid(), Title = "本人云端反思", ContentText = "PRIVATE-NOTE-FIRST-BODY",
        SubjectName = "数学", MaterialYear = 2026, CustomTags = ["有理数", "第一章"] };
    private static SavePersonalNotebookDto NoteEdit(PersonalNotebookDto note, string text = "PRIVATE-NOTE-NEXT-BODY") => new() {
        OperationId = Guid.NewGuid(), ExpectedVersionId = note.LatestVersionId, ConcurrencyStamp = note.ConcurrencyStamp, Title = "反思的新标题", ContentText = text,
        Summary = note.Summary, SubjectName = note.SubjectName, MaterialYear = note.MaterialYear, CustomTags = note.CustomTags.ToList() };

    [Fact]
    public async Task Personal_Notebook_Should_Atomically_Version_And_Recover_Exact_Old_Receipts_Without_Rollback_Or_Reusing_Deleted_Numbers()
    {
        var f = await GradingFixtureAsync(false); using var owner = NotebookActor(f.Source.Student.UserId); var create = NewNote();
        (await Notebooks.GetOperationAsync(create.OperationId)).ShouldBeNull();
        var first = await Notebooks.CreateAsync(create); var original = await Notebooks.GetAsync(first.NoteId);
        first.VersionNumber.ShouldBe(1); (await Notebooks.CreateAsync(create)).VersionId.ShouldBe(first.VersionId);
        (await Notebooks.GetVersionAsync(first.NoteId, first.VersionId!.Value)).ContentText.ShouldBe(create.ContentText);
        JsonSerializer.Serialize(await Notebooks.GetVersionsAsync(first.NoteId, new())).ShouldNotContain(create.ContentText!);
        var save = NoteEdit(original); var second = await Notebooks.SaveAsync(first.NoteId, save); second.VersionNumber.ShouldBe(2);
        (await Notebooks.CreateAsync(create)).VersionId.ShouldBe(first.VersionId); (await Notebooks.GetAsync(first.NoteId)).LatestVersionId.ShouldBe(second.VersionId);
        (await Notebooks.GetOperationAsync(save.OperationId))!.VersionId.ShouldBe(second.VersionId);
        var altered = JsonSerializer.Deserialize<SavePersonalNotebookDto>(JsonSerializer.Serialize(save))!; altered.ContentText = "改写原操作";
        (await Should.ThrowAsync<BusinessException>(() => Notebooks.SaveAsync(first.NoteId, altered))).Code.ShouldBe(PersonalNotebookErrors.OperationConflict);
        (await Should.ThrowAsync<BusinessException>(() => Notebooks.SaveAsync(first.NoteId, NoteEdit(original)))).Code.ShouldBe(PersonalNotebookErrors.Changed);
        var beforeWeb = await Notebooks.GetAsync(first.NoteId);
        var legacy = await GetRequiredService<IKnowledgeItemAppService>().CreateVersionAsync(first.NoteId, new() { Title = "Web追加", ContentText = "PRIVATE-WEB-VERSION" });
        (await Should.ThrowAsync<BusinessException>(() => Notebooks.SaveAsync(first.NoteId, NoteEdit(beforeWeb)))).Code.ShouldBe(PersonalNotebookErrors.Changed);
        await WithUnitOfWorkAsync(() => Repo<KnowledgeVersion>().DeleteAsync(legacy.Id, autoSave: true));
        var fourth = await Notebooks.SaveAsync(first.NoteId, NoteEdit(await Notebooks.GetAsync(first.NoteId))); fourth.VersionNumber.ShouldBe(4);
        var versions = await Notebooks.GetVersionsAsync(first.NoteId, new() { MaxResultCount = 1 }); versions.TotalCount.ShouldBe(3); versions.Items.Single().Id.ShouldBe(fourth.VersionId!.Value);
        var deletion = new DeletePersonalNotebookDto { OperationId = Guid.NewGuid(), ConcurrencyStamp = (await Notebooks.GetAsync(first.NoteId)).ConcurrencyStamp };
        (await Notebooks.DeleteAsync(first.NoteId, deletion)).Kind.ShouldBe(PersonalNotebookCommandKind.Deleted);
        (await Notebooks.DeleteAsync(first.NoteId, deletion)).OperationId.ShouldBe(deletion.OperationId);
        await Should.ThrowAsync<AbpAuthorizationException>(() => Notebooks.GetAsync(first.NoteId));
        (await Notebooks.GetOperationAsync(create.OperationId))!.VersionId.ShouldBe(first.VersionId);
        (await WithUnitOfWorkAsync(() => Repo<EducationAssessmentAttempt>().GetCountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Personal_Notebook_Should_Stay_Owner_Only_Across_Roles_Tenants_And_Account_Status_Without_A_School_Gate()
    {
        var f = await GradingFixtureAsync(false); PersonalNotebookReceiptDto receipt;
        using (NotebookActor(f.Source.Student.UserId)) receipt = await Notebooks.CreateAsync(NewNote());
        foreach (var actor in new[] { (f.Source.School.Reader, "teacher"), (f.Source.School.Owner, "admin"), (f.Absent.UserId, "student") }) {
            using var foreign = NotebookActor(actor.Item1, actor.Item2);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Notebooks.GetAsync(receipt.NoteId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Notebooks.GetOperationAsync(receipt.OperationId));
            (await Notebooks.GetListAsync(new())).Items.ShouldNotContain(x => x.Id == receipt.NoteId);
        }
        using var owner = NotebookActor(f.Source.Student.UserId);
        using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid())) await Should.ThrowAsync<AbpAuthorizationException>(() => Notebooks.GetAsync(receipt.NoteId));
        await WithUnitOfWorkAsync(async () => { foreach (var membership in await Repo<SchoolMembership>().GetListAsync(x => x.UserId == f.Source.Student.UserId)) await Repo<SchoolMembership>().DeleteAsync(membership, autoSave: true); });
        (await Notebooks.GetAsync(receipt.NoteId)).OwnerUserId.ShouldBe(f.Source.Student.UserId);
        (await Notebooks.SaveAsync(receipt.NoteId, NoteEdit(await Notebooks.GetAsync(receipt.NoteId)))).VersionNumber.ShouldBe(2);
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        await Should.ThrowAsync<AbpAuthorizationException>(() => new PersonalNotebookAppService { LazyServiceProvider = lazy }.GetAsync(receipt.NoteId));
        await WithUnitOfWorkAsync(async () => { var account = await Repo<IdentityUser>().GetAsync(f.Source.Student.UserId); account.SetIsActive(false); await Repo<IdentityUser>().UpdateAsync(account, autoSave: true); });
        await Should.ThrowAsync<AbpAuthorizationException>(() => Notebooks.GetOperationAsync(receipt.OperationId));
    }

    [Fact]
    public async Task Personal_Notebook_Upload_Should_Commit_One_Exact_Original_With_Its_Receipt_And_Reject_Changed_Bytes()
    {
        var f = await GradingFixtureAsync(false); using var owner = NotebookActor(f.Source.Student.UserId);
        var note = await Notebooks.CreateAsync(NewNote()); var op = Guid.NewGuid(); var bytes = new byte[] { 1, 2, 3, 4 };
        async Task<PersonalNotebookReceiptDto> Upload(byte[] data) { using var file = new RemoteStreamContent(new MemoryStream(data), "own.txt", "text/plain"); return await Notebooks.UploadAttachmentAsync(note.NoteId, note.VersionId!.Value, op, file); }
        var first = await Upload(bytes); first.AttachmentId.ShouldBe(op); first.FileSize.ShouldBe(4); first.ContentHash!.Length.ShouldBe(64);
        (await Upload(bytes)).OperationId.ShouldBe(first.OperationId);
        (await Should.ThrowAsync<BusinessException>(() => Upload([9]))).Code.ShouldBe(PersonalNotebookErrors.OperationConflict);
        (await Notebooks.GetAttachmentsAsync(note.NoteId, note.VersionId!.Value, new())).TotalCount.ShouldBe(1);
        using var download = await Notebooks.DownloadAttachmentAsync(note.NoteId, note.VersionId.Value, op); using var buffer = new MemoryStream(); await download.GetStream().CopyToAsync(buffer); buffer.ToArray().ShouldBe(bytes);
        using (NotebookActor(f.Absent.UserId)) await Should.ThrowAsync<AbpAuthorizationException>(() => Notebooks.DownloadAttachmentAsync(note.NoteId, note.VersionId.Value, op));
        (await Notebooks.GetOperationAsync(op))!.ContentHash.ShouldBe(first.ContentHash);
    }

    [Fact]
    public async Task Personal_Notebook_Upload_Should_Repair_Only_An_Identical_Uncommitted_Blob_And_Keep_A_Conflicting_Original()
    {
        var f = await GradingFixtureAsync(false); using var owner = NotebookActor(f.Source.Student.UserId); var note = await Notebooks.CreateAsync(NewNote());
        var blobs = GetRequiredService<IBlobContainer<KnowledgeAttachmentBlobContainer>>();
        foreach (var same in new[] { true, false }) {
            var op = Guid.NewGuid(); var blob = $"tenants/host/knowledge-items/{note.NoteId:N}/versions/{note.VersionId:N}/attachments/{op:N}/recovered.txt";
            using (var original = new MemoryStream(same ? new byte[] { 1, 2 } : [8, 9])) await blobs.SaveAsync(blob, original, overrideExisting: false);
            using var file = new RemoteStreamContent(new MemoryStream([1, 2]), "recovered.txt", "text/plain");
            if (same) (await Notebooks.UploadAttachmentAsync(note.NoteId, note.VersionId!.Value, op, file)).AttachmentId.ShouldBe(op);
            else { (await Should.ThrowAsync<BusinessException>(() => Notebooks.UploadAttachmentAsync(note.NoteId, note.VersionId!.Value, op, file))).Code.ShouldBe(PersonalNotebookErrors.OperationConflict);
                (await Notebooks.GetOperationAsync(op)).ShouldBeNull(); using var original = await blobs.GetAsync(blob); original.ReadByte().ShouldBe(8); }
        }
    }
}
