using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.KnowledgeBase;
using KnowPulse.Permissions;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Fact]
    public async Task Native_Notebook_Source_Expected_Version_Should_Block_Body_Before_Exposure_Or_Provision()
    {
        var f = await GradingFixtureAsync(false); await ExposureReleaseAsync(f, ExamResultVisibility.FullReview);
        using (NotebookActor(f.Source.Student.UserId))
        {
            (await Should.ThrowAsync<BusinessException>(() => GetRequiredService<IEducationAssessmentGradingAppService>()
                .GetMyResultForSchoolAsync(f.RecipientId, f.Source.School.SchoolId, Guid.NewGuid()))).Code.ShouldBe(EducationNotebookSourceErrors.Unavailable);
            (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(0);
        }
        var study = await StudyAsync(); var recipient = StudyRecipient(study);
        using (NotebookActor(study.Student.UserId))
        {
            (await Should.ThrowAsync<BusinessException>(() => Study.OpenMyForSchoolAsync(recipient, study.School.SchoolId, Guid.NewGuid())))
                .Code.ShouldBe(EducationNotebookSourceErrors.Unavailable);
            (await WithUnitOfWorkAsync(() => Repo<EducationStudyRecipient>().GetAsync(recipient))).FirstProvidedAtUtc.ShouldBeNull();
        }
    }
    private IEducationNotebookSourceAppService NotebookSources => GetRequiredService<IEducationNotebookSourceAppService>();
    private async Task<(KnowledgeItemDto Note, KnowledgeVersionDto Version)> SourceNoteAsync()
    {
        var note = await GetRequiredService<IKnowledgeLibraryAppService>().CreateItemAsync(new()
            { Title = "来源测试笔记", Organization = new() { MaterialType = PersonalMaterialType.Note } });
        var version = await GetRequiredService<IKnowledgeItemAppService>().CreateVersionAsync(note.Id, new() { Title = "自己写的笔记", ContentText = "自己的思考，不是源资料副本" });
        return (note, version);
    }
    private async Task<AddEducationNotebookSourceDto> NotebookAddAsync(Guid note, Guid version, EducationNotebookSourcePointer source) => new()
    {
        OperationId = Guid.NewGuid(), NoteVersionId = version, Source = source,
        ConcurrencyStamp = (await NotebookSources.GetSourcesAsync(note, version)).ConcurrencyStamp
    };

    [Theory]
    [InlineData(ExamResultVisibility.Hidden)]
    [InlineData(ExamResultVisibility.ScoreOnly)]
    [InlineData(ExamResultVisibility.FullReview)]
    public async Task Notebook_Question_Reference_Should_Require_Current_Full_Review_Without_Recording_Exposure(ExamResultVisibility visibility)
    {
        var f = await GradingFixtureAsync(false); await ExposureReleaseAsync(f, visibility);
        var schoolStamp = await WithUnitOfWorkAsync(async () => (await Repo<School>().GetAsync(f.Source.School.SchoolId)).ConcurrencyStamp);
        var accountStamp = await WithUnitOfWorkAsync(async () => (await Repo<Volo.Abp.Identity.IdentityUser>().GetAsync(f.Source.Student.UserId)).ConcurrencyStamp);
        using var student = NotebookActor(f.Source.Student.UserId);
        var preview = await NotebookSources.PreviewSourceAsync(new() { Kind = EducationNotebookSourceKind.AssessmentQuestion,
            RecipientId = f.RecipientId, ComponentId = f.Source.QuestionId });
        preview.Available.ShouldBe(visibility == ExamResultVisibility.FullReview);
        if (preview.Available)
        {
            preview.Source!.SourceVersionId.ShouldBe((await WithUnitOfWorkAsync(() => Repo<EducationAssessmentRecipient>().GetAsync(f.RecipientId))).TaskRevisionId);
            var (note, version) = await SourceNoteAsync(); var add = await NotebookAddAsync(note.Id, version.Id, preview.Source);
            var saved = await NotebookSources.AddSourceAsync(note.Id, add);
            saved.Items.Single().Source.ShouldBe(preview.Source);
            (await NotebookSources.AddSourceAsync(note.Id, add)).Items.Single().Id.ShouldBe(add.OperationId);
            (await NotebookSources.GetSourceOperationAsync(note.Id, add.OperationId))!.Id.ShouldBe(add.OperationId);
            (await NotebookSources.GetSourcesAsync(note.Id, version.Id)).Items.Single().Available.ShouldBeTrue();
            var stale = await NotebookAddAsync(note.Id, version.Id, preview.Source with { SourceVersionId = Guid.NewGuid() });
            await Should.ThrowAsync<BusinessException>(() => NotebookSources.AddSourceAsync(note.Id, stale));
            var old = add.OperationId; add.OperationId = Guid.NewGuid();
            await Should.ThrowAsync<AbpDbConcurrencyException>(() => NotebookSources.AddSourceAsync(note.Id, add));
            add.OperationId = old; add.Source = add.Source with { ComponentId = Guid.NewGuid() };
            (await Should.ThrowAsync<BusinessException>(() => NotebookSources.AddSourceAsync(note.Id, add))).Code.ShouldBe(EducationNotebookSourceErrors.Conflict);
            var other = await SourceNoteAsync();
            await Should.ThrowAsync<AbpAuthorizationException>(() => NotebookSources.GetSourcesAsync(note.Id, other.Version.Id));
            var remove = new RemoveEducationNotebookSourceDto { OperationId = Guid.NewGuid(), NoteVersionId = version.Id, ConcurrencyStamp = saved.ConcurrencyStamp };
            (await NotebookSources.RemoveSourceAsync(note.Id, old, remove)).Items.ShouldBeEmpty();
            (await NotebookSources.RemoveSourceAsync(note.Id, old, remove)).Items.ShouldBeEmpty();
            (await NotebookSources.GetSourceOperationAsync(note.Id, remove.OperationId))!.Removed.ShouldBeTrue();
        }
        else { preview.Title.ShouldBeNull(); preview.Source.ShouldBeNull(); }
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(0);
        (await WithUnitOfWorkAsync(async () => (await Repo<School>().GetAsync(f.Source.School.SchoolId)).ConcurrencyStamp)).ShouldBe(schoolStamp);
        (await WithUnitOfWorkAsync(async () => (await Repo<Volo.Abp.Identity.IdentityUser>().GetAsync(f.Source.Student.UserId)).ConcurrencyStamp)).ShouldBe(accountStamp);
        await Grades.GetMyResultAsync(f.RecipientId);
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(visibility == ExamResultVisibility.FullReview ? 1 : 0);
    }

    [Fact]
    public async Task Notebook_Reference_Should_Deny_Other_Accounts_And_Recheck_Source_Membership_Without_Losing_Private_Text()
    {
        var f = await GradingFixtureAsync(false); await ExposureReleaseAsync(f, ExamResultVisibility.FullReview);
        KnowledgeItemDto note; KnowledgeVersionDto version; AddEducationNotebookSourceDto add;
        using (NotebookActor(f.Source.Student.UserId))
        {
            (note, version) = await SourceNoteAsync();
            var preview = await NotebookSources.PreviewSourceAsync(new() { RecipientId = f.RecipientId, ComponentId = f.Source.QuestionId });
            add = await NotebookAddAsync(note.Id, version.Id, preview.Source!); await NotebookSources.AddSourceAsync(note.Id, add);
        }
        foreach (var actor in new[] { (f.Source.School.Reader, "teacher"), (f.Source.School.Owner, "admin"), (f.Absent.UserId, "student") })
        {
            using var foreign = NotebookActor(actor.Item1, actor.Item2);
            await Should.ThrowAsync<AbpAuthorizationException>(() => NotebookSources.GetSourcesAsync(note.Id, version.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => NotebookSources.AddSourceAsync(note.Id, add));
            await Should.ThrowAsync<AbpAuthorizationException>(() => NotebookSources.RemoveSourceAsync(note.Id, add.OperationId, new() { OperationId = Guid.NewGuid(), NoteVersionId = version.Id, ConcurrencyStamp = add.ConcurrencyStamp }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => NotebookSources.GetSourceOperationAsync(note.Id, add.OperationId));
            (await NotebookSources.PreviewSourceAsync(new() { RecipientId = f.RecipientId, ComponentId = f.Source.QuestionId })).Available.ShouldBeFalse();
        }
        using (NotebookActor(f.Source.Student.UserId)) using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
            await Should.ThrowAsync<AbpAuthorizationException>(() => NotebookSources.GetSourceOperationAsync(note.Id, add.OperationId));
        await WithUnitOfWorkAsync(async () => { var membership = await Repo<SchoolMembership>().GetAsync(f.Source.Student.MembershipId);
            membership.EndAt(DateTime.UtcNow.AddSeconds(-1)); await Repo<SchoolMembership>().UpdateAsync(membership, autoSave: true); });
        using (NotebookActor(f.Source.Student.UserId))
        {
            var state = await NotebookSources.GetSourcesAsync(note.Id, version.Id); var source = state.Items.Single();
            source.Available.ShouldBeFalse(); source.Title.ShouldBeNull(); source.OpenUrl.ShouldBeNull();
            (await GetRequiredService<IKnowledgeItemAppService>().GetVersionsAsync(note.Id)).Items.Single().ContentText.ShouldBe("自己的思考，不是源资料副本");
            (await NotebookSources.RemoveSourceAsync(note.Id, add.OperationId, new() { OperationId = Guid.NewGuid(), NoteVersionId = version.Id, ConcurrencyStamp = state.ConcurrencyStamp })).Items.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Notebook_Material_Reference_Should_Not_Provide_Materials_And_Should_Stop_Reads_After_Withdrawal_While_Exporting_Only_Pointers()
    {
        var f = await StudyAsync(); var recipient = StudyRecipient(f);
        using var student = NotebookActor(f.Student.UserId);
        var input = new PreviewEducationNotebookSourceDto { Kind = EducationNotebookSourceKind.StudyMaterial, RecipientId = recipient, ComponentId = f.Material.Id };
        (await NotebookSources.PreviewSourceAsync(input)).Available.ShouldBeFalse();
        (await WithUnitOfWorkAsync(() => Repo<EducationStudyRecipient>().GetAsync(recipient))).FirstProvidedAtUtc.ShouldBeNull();
        (await WithUnitOfWorkAsync(() => Repo<EducationStudyEvent>().GetCountAsync())).ShouldBe(0);
        var opened = await Study.OpenMyAsync(recipient);
        var started = await Study.SaveMyProgressAsync(recipient, new() { OperationId = Guid.NewGuid(), ExpectedStudentRevisionId = opened.CurrentStudentRevisionId, Action = EducationTeachingActivityAction.Started });
        var completed = await Study.SaveMyProgressAsync(recipient, new() { OperationId = Guid.NewGuid(), ExpectedStudentRevisionId = started.Id,
            Action = EducationTeachingActivityAction.Completed, SharedText = "我明确上交的总结", ConfirmActivityOnly = true, ConfirmSharingWithTeacher = true });
        var provided = (await WithUnitOfWorkAsync(() => Repo<EducationStudyRecipient>().GetAsync(recipient))).FirstProvidedAtUtc;
        var schoolStamp = await WithUnitOfWorkAsync(async () => (await Repo<School>().GetAsync(f.School.SchoolId)).ConcurrencyStamp);
        var preview = await NotebookSources.PreviewSourceAsync(input); preview.Available.ShouldBeTrue();
        preview.Source!.SourceVersionId.ShouldBe(f.Publication.Id);
        var (note, version) = await SourceNoteAsync(); var add = await NotebookAddAsync(note.Id, version.Id, preview.Source);
        await NotebookSources.AddSourceAsync(note.Id, add);
        (await WithUnitOfWorkAsync(() => Repo<EducationStudyRecipient>().GetAsync(recipient))).FirstProvidedAtUtc.ShouldBe(provided);
        (await WithUnitOfWorkAsync(async () => (await Repo<School>().GetAsync(f.School.SchoolId)).ConcurrencyStamp)).ShouldBe(schoolStamp);
        (await WithUnitOfWorkAsync(() => Repo<EducationStudyEvent>().GetCountAsync())).ShouldBe(2);
        var stored = await WithUnitOfWorkAsync(async () => JsonSerializer.Serialize((await Repo<KnowledgeItem>().GetAsync(note.Id)).ExtraProperties));
        stored.ShouldNotContain(f.Material.KnowledgeItemId.ToString()); stored.ShouldNotContain(f.Material.KnowledgeVersionId.ToString());
        stored.ShouldNotContain("独立任务原始讲义"); stored.ShouldNotContain(f.Material.Title);
        using (User(f.School.Owner)) await _sharing.RevokeAsync(f.ShareId!.Value);
        (await NotebookSources.GetSourcesAsync(note.Id, version.Id)).Items.Single().Available.ShouldBeFalse();
        (await NotebookSources.PreviewSourceAsync(input)).Available.ShouldBeFalse();
        await Should.ThrowAsync<AbpAuthorizationException>(() => Study.GetMyMaterialAsync(recipient, f.Material.Id));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Study.DownloadMyMaterialAsync(recipient, f.Material.Id, f.SelectedFile));
        (await Study.GetMyHistoryAsync(recipient, completed.Id, new())).Items.Single(x => x.Action == EducationTeachingActivityAction.Completed).SharedText.ShouldBe("我明确上交的总结");
        var exports = GetRequiredService<IKnowledgePersonalExportAppService>(); var job = await exports.CreateAsync(new() { IdempotencyKey = Guid.NewGuid() });
        await GetRequiredService<PersonalKnowledgeExportProcessor>().ProcessAsync(job.Id);
        (await exports.GetAsync(job.Id)).Status.ShouldBe(PersonalKnowledgeExportStatus.Completed);
        using var file = await exports.DownloadAsync(job.Id); using var bytes = new MemoryStream(); await file.GetStream().CopyToAsync(bytes); bytes.Position = 0;
        using var zip = new ZipArchive(bytes); var manifest = await NotebookZipTextAsync(zip, "manifest.json"); using var json = JsonDocument.Parse(manifest);
        var reference = json.RootElement.GetProperty("Versions").EnumerateArray().Single().GetProperty("NotebookSources").EnumerateArray().Single();
        reference.GetProperty("SourceVersionId").GetGuid().ShouldBe(f.Publication.Id); reference.GetProperty("RequiresCurrentAuthorization").GetBoolean().ShouldBeTrue();
        manifest.ShouldNotContain("独立任务原始讲义"); manifest.ShouldNotContain("我明确上交的总结"); manifest.ShouldNotContain("RequestFingerprint");
        manifest.ShouldNotContain(f.Material.KnowledgeItemId.ToString()); manifest.ShouldNotContain(f.Material.KnowledgeVersionId.ToString());
        manifest.ShouldNotContain("ConcurrencyStamp"); manifest.ShouldNotContain("Available");
        (await NotebookZipTextAsync(zip, $"items/{note.Id:N}/versions/{version.Id:N}/content.txt")).ShouldBe("自己的思考，不是源资料副本");
    }

    [Fact]
    public async Task Notebook_Source_Should_Explicitly_Enforce_Personal_Library_Permission()
    {
        using var student = NotebookActor(Guid.NewGuid());
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        lazy.LazyGetRequiredService<Volo.Abp.Users.ICurrentUser>().Returns(GetRequiredService<Volo.Abp.Users.ICurrentUser>());
        var service = new EducationNotebookSourceAppService { LazyServiceProvider = lazy };
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.PreviewSourceAsync(new()));
    }
}
