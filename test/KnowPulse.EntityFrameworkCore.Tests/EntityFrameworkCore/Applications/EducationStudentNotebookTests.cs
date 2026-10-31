using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.KnowledgeBase;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Security.Claims;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IDisposable NotebookActor(Guid id, string role = "student") => GetRequiredService<ICurrentPrincipalAccessor>().Change(
        new ClaimsPrincipal(new ClaimsIdentity([new Claim(AbpClaimTypes.UserId, id.ToString()),
            new Claim(AbpClaimTypes.UserName, $"notebook-{id:N}"), new Claim(AbpClaimTypes.Role, role)], "Test")));

    [Fact]
    public async Task Student_Notebook_Cloud_Versions_And_Images_Should_Stay_Private_And_Not_Change_Assessed_Work()
    {
        var f = await GradingFixtureAsync(false);
        await ExposureReleaseAsync(f, ExamResultVisibility.FullReview);
        var library = GetRequiredService<IKnowledgeLibraryAppService>();
        var items = GetRequiredService<IKnowledgeItemAppService>();
        await WithUnitOfWorkAsync(async () => await Repo<SchoolMembership>().InsertAsync(
            new SchoolMembership(Guid.NewGuid(), null, f.Source.School.SchoolId, f.Source.School.Owner,
                SchoolMemberRole.Manager, DateTime.UtcNow.AddDays(-1)), autoSave: true));
        KnowledgeItemDto note; KnowledgeVersionDto first; KnowledgeAttachmentDto image;
        string resultBefore;
        using (NotebookActor(f.Source.Student.UserId))
        {
            GetRequiredService<ICurrentUser>().IsInRole("admin").ShouldBeFalse();
            var own = await library.GetMyLibraryAsync();
            own.OwnerUserId.ShouldBe(f.Source.Student.UserId);
            (await library.GetMyLibraryAsync()).Id.ShouldBe(own.Id);
            resultBefore = JsonSerializer.Serialize(await GetRequiredService<IEducationAssessmentGradingAppService>().GetMyResultAsync(f.RecipientId));
            note = await library.CreateItemAsync(new()
            {
                Title = "我的有理数反思", Organization = new() { MaterialType = PersonalMaterialType.Note,
                    SubjectName = "数学", CustomTags = ["七年上册", "有理数加法"] }
            });
            first = await items.CreateVersionAsync(note.Id, new() { Title = note.Title, ContentText = "只有本人可见的第一版推理" });
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6l6sAAAAASUVORK5CYII=");
            image = await items.UploadAttachmentAsync(note.Id, first.Id, new RemoteStreamContent(new MemoryStream(png), "解题草图.png", "image/png"));
            var second = await items.CreateVersionAsync(note.Id, new() { Title = "重写后的反思", ContentText = "第二版自己的检查策略" });
            second.VersionNumber.ShouldBe(2);
            var versions = (await items.GetVersionsAsync(note.Id)).Items;
            versions.Count.ShouldBe(2);
            versions.Single(x => x.Id == first.Id).ContentText.ShouldBe("只有本人可见的第一版推理");
            using var download = await items.DownloadAttachmentAsync(note.Id, first.Id, image.Id);
            using var copy = new MemoryStream(); await download.GetStream().CopyToAsync(copy);
            copy.ToArray().ShouldBe(png);
            (await library.GetMyItemsAsync(new() { MaterialType = PersonalMaterialType.Note, SubjectName = "数学", Tag = "有理数加法",
                Keyword = "第一版推理" })).Items.Single().Id.ShouldBe(note.Id);
            (await library.GetOrganizationOptionsAsync()).Tags.ShouldContain("七年上册");
            JsonSerializer.Serialize(await GetRequiredService<IEducationAssessmentGradingAppService>().GetMyResultAsync(f.RecipientId)).ShouldBe(resultBefore);
        }
        foreach (var actor in new[] { (f.Source.School.Reader, "teacher"), (f.Source.School.Owner, "admin"), (f.Absent.UserId, "student") })
        {
            using var foreign = NotebookActor(actor.Item1, actor.Item2);
            // Functional grants are deliberately open in this test module; ownership must still deny every reader.
            await Should.ThrowAsync<AbpAuthorizationException>(() => items.GetAsync(note.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => items.GetVersionsAsync(note.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => items.DownloadAttachmentAsync(note.Id, first.Id, image.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => items.UpdateAsync(note.Id, new() { Title = "试图改写" }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => items.DeleteAsync(note.Id));
            (await library.GetMyItemsAsync(new() { Keyword = "第一版推理" })).Items.ShouldNotContain(x => x.Id == note.Id);
            (await library.GetOrganizationOptionsAsync()).Tags.ShouldNotContain("七年上册");
        }
        using (NotebookActor(f.Source.Student.UserId))
        {
            await items.DeleteAsync(note.Id);
            (await library.GetMyItemsAsync(new())).Items.ShouldNotContain(x => x.Id == note.Id);
            JsonSerializer.Serialize(await GetRequiredService<IEducationAssessmentGradingAppService>().GetMyResultAsync(f.RecipientId)).ShouldBe(resultBefore);
        }
    }

    [Fact]
    public async Task Student_Notebook_Export_Should_Include_Own_Versions_And_Original_Attachment_Without_Teacher_Or_Other_Student_Data()
    {
        var f = await GradingFixtureAsync(false);
        var library = GetRequiredService<IKnowledgeLibraryAppService>();
        var items = GetRequiredService<IKnowledgeItemAppService>();
        KnowledgeItemDto foreign;
        using (NotebookActor(f.Absent.UserId))
        {
            foreign = await library.CreateItemAsync(new() { Title = "另一学生的私有笔记" });
            await items.CreateVersionAsync(foreign.Id, new() { Title = foreign.Title, ContentText = "他人笔记正文不得导出" });
        }
        using (NotebookActor(f.Source.Student.UserId))
        {
            var note = await library.CreateItemAsync(new() { Title = "个人云端笔记", Organization = new() { MaterialType = PersonalMaterialType.Note,
                SubjectName = "数学", CustomTags = ["第一章", "复盘"] } });
            var first = await items.CreateVersionAsync(note.Id, new() { Title = note.Title, ContentText = "我的首个笔记版本" });
            var second = await items.CreateVersionAsync(note.Id, new() { Title = "后续反思", ContentText = "我的后续笔记版本" });
            var bytes = System.Text.Encoding.UTF8.GetBytes("我自己上传的文本附件");
            var attachment = await items.UploadAttachmentAsync(note.Id, second.Id, new RemoteStreamContent(new MemoryStream(bytes), "notes.txt", "text/plain"));
            var exports = GetRequiredService<IKnowledgePersonalExportAppService>();
            var job = await exports.CreateAsync(new() { IdempotencyKey = Guid.NewGuid() });
            await GetRequiredService<PersonalKnowledgeExportProcessor>().ProcessAsync(job.Id);
            (await exports.GetAsync(job.Id)).Status.ShouldBe(PersonalKnowledgeExportStatus.Completed);
            using var download = await exports.DownloadAsync(job.Id);
            using var buffer = new MemoryStream(); await download.GetStream().CopyToAsync(buffer); buffer.Position = 0;
            using var zip = new ZipArchive(buffer);
            var manifest = await NotebookZipTextAsync(zip, "manifest.json");
            using var json = JsonDocument.Parse(manifest);
            var metadata = json.RootElement.GetProperty("Items").EnumerateArray().Single();
            metadata.GetProperty("MaterialType").GetString().ShouldBe("Note");
            metadata.GetProperty("SubjectName").GetString().ShouldBe("数学");
            manifest.ShouldNotContain(foreign.Id.ToString());
            manifest.ShouldNotContain(f.SubmissionId.ToString());
            (await NotebookZipTextAsync(zip, $"items/{note.Id:N}/versions/{first.Id:N}/content.txt")).ShouldBe("我的首个笔记版本");
            (await NotebookZipTextAsync(zip, $"items/{note.Id:N}/versions/{second.Id:N}/content.txt")).ShouldBe("我的后续笔记版本");
            (await NotebookZipTextAsync(zip, $"attachments/{attachment.Id:N}.txt")).ShouldBe("我自己上传的文本附件");
        }
    }

    private static async Task<string> NotebookZipTextAsync(ZipArchive zip, string path)
    {
        using var stream = zip.GetEntry(path)!.Open(); using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
