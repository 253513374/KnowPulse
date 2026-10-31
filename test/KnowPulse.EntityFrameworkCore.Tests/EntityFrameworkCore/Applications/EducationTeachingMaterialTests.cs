using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.Permissions;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationTeachingMaterialAppService Materials => GetRequiredService<IEducationTeachingMaterialAppService>();
    private sealed record MaterialDelivery(GradingFixture F, Guid PlanId, EducationTeachingPlanRevisionDto Published,
        EducationTeachingPlanMaterial Material, KnowledgeChunk Source, Guid SelectedFile, Guid UnselectedFile, Guid? ShareId);
    private async Task<MaterialDelivery> MaterialDeliveryAsync(bool shared = true, bool open = true)
    {
        var (f, report) = await PlanSourceAsync(); var school = f.Source.School; var source = await SourceAsync(shared ? school.Owner : school.Reader, "指定V1材料正文");
        var selected = await MaterialFileAsync(source.KnowledgeVersionId, "必要讲义.txt"); var other = await MaterialFileAsync(source.KnowledgeVersionId, "未授权附件.txt");
        Guid? shareId = null;
        if (shared) using (User(school.Owner)) { var share = Share(school, source); share.Uses = SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.AssignToStudents | SchoolKnowledgeShareUses.Download; shareId = (await _sharing.ShareVersionAsync(share)).Id; }
        var id = Guid.NewGuid(); EducationTeachingPlanRevisionDto publication; EducationTeachingPlanMaterial material;
        using (User(school.Reader)) {
            var input = await PlanDraftAsync(f, report);
            material = new() { Id = Guid.NewGuid(), StepId = input.Content.Steps[1].Id, Title = "学生可见指定讲义", KnowledgeItemId = source.KnowledgeItemId,
                KnowledgeVersionId = source.KnowledgeVersionId, SchoolKnowledgeShareId = shareId, AttachmentIds = [selected] };
            input.Content = input.Content with { Materials = [material] }; await Plans.SaveDraftAsync(id, input); publication = await ConfirmAndPublishPlanAsync(id);
        }
        if (open) using (User(f.Source.Student.UserId)) await Activities.GetMyActivitiesAsync(id, publication.Id);
        return new(f, id, publication, material, source, selected, other, shareId);
    }
    private async Task<Guid> MaterialFileAsync(Guid versionId, string name)
    {
        var id = Guid.NewGuid(); var blob = $"test-material/{id:D}"; var bytes = Encoding.UTF8.GetBytes(name);
        using var stream = new MemoryStream(bytes); await GetRequiredService<IBlobContainer<KnowledgeAttachmentBlobContainer>>().SaveAsync(blob, stream);
        await WithUnitOfWorkAsync(() => Repo<KnowledgeAttachment>().InsertAsync(new(id, null, versionId, name, blob, "text/plain", bytes.Length), autoSave: true)); return id;
    }
    private static Guid MaterialActivity(MaterialDelivery f) => EducationTeachingActivity.Identity(f.Published.Id, f.Material.StepId, f.F.Source.Student.UserId);

    [Fact]
    public async Task Teaching_Material_Empty_Lists_Should_Preserve_Legacy_Draft_Bytes_Fingerprint_And_Transition_Recovery()
    {
        var (f, report) = await PlanSourceAsync(); using var actor = User(f.Source.School.Reader); var planId = Guid.NewGuid(); var input = await PlanDraftAsync(f, report);
        var content = input.Content;
        var legacyContent = new { content.Title, content.StudentGoal, content.TeacherActions, content.SuccessCriteria, content.StartsAtUtc, content.EndsAtUtc, content.ReassessmentAtUtc, content.TargetPointRevisionIds, content.Steps };
        var legacyInput = new { input.OperationId, input.ReportId, input.ReportSnapshotId, input.StudentUserId, input.ExpectedRevisionId, input.ConcurrencyStamp, Content = legacyContent, input.InternalNotes };
        var expectedFingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Command = "plan-draft", planId, input = legacyInput }))));
        var draft = await Plans.SaveDraftAsync(planId, input); draft.RequestFingerprint.ShouldBe(expectedFingerprint);
        var original = await WithUnitOfWorkAsync(() => Repo<EducationTeachingPlanRevision>().GetAsync(draft.Id));
        original.ContentJson.ShouldBe(JsonSerializer.Serialize(legacyContent, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var publication = await ConfirmAndPublishPlanAsync(planId); (await Plans.SaveDraftAsync(planId, input)).Id.ShouldBe(draft.Id);
        (await Plans.GetOperationAsync(planId, publication.Id))!.Id.ShouldBe(publication.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Teaching_Material_Should_Grant_Only_Exact_Version_Selected_Files_And_Not_Private_Library_Access(bool shared)
    {
        var f = await MaterialDeliveryAsync(shared); var activity = MaterialActivity(f);
        using (User(f.F.Source.School.Reader)) {
            var candidate = (await Materials.GetMaterialSourcesAsync(f.PlanId, new() { SchoolShared = shared })).Items.Single(x => x.Id == f.Source.KnowledgeVersionId);
            candidate.Title = "不信任客户端的伪造标题"; var preview = await Materials.PreviewMaterialSourceAsync(f.PlanId, candidate); preview.Source.Title.ShouldBe("指定V1材料正文"); preview.Attachments.Count.ShouldBe(2);
            (await Materials.GetTeacherMaterialAsync(f.PlanId, f.Published.Id, f.Material.Id)).Attachments.Single().Id.ShouldBe(f.SelectedFile);
            using var original = await Materials.DownloadTeacherMaterialAsync(f.PlanId, f.Published.Id, f.Material.Id, f.SelectedFile); original.FileName.ShouldBe("必要讲义.txt");
        }
        await WithUnitOfWorkAsync(async () => {
            var old = await Repo<KnowledgeVersion>().GetAsync(f.Source.KnowledgeVersionId); old.ClearCurrentPublished(); await Repo<KnowledgeVersion>().UpdateManyAsync([old], autoSave: true);
            var next = new KnowledgeVersion(Guid.NewGuid(), null, f.Source.KnowledgeItemId, 2, "PRIVATE-V2-TITLE", "PRIVATE-V2-BODY"); next.Publish(DateTime.UtcNow); await Repo<KnowledgeVersion>().InsertAsync(next, autoSave: true);
            var item = await Repo<KnowledgeItem>().GetAsync(f.Source.KnowledgeItemId); item.SetCurrentPublishedVersion(next.Id); item.Rename("PRIVATE-V2-TITLE"); await Repo<KnowledgeItem>().UpdateManyAsync([item], autoSave: true);
        });
        using var user = User(f.F.Source.Student.UserId);
        var content = await Materials.GetMyMaterialAsync(activity, f.Material.Id);
        content.IsAvailable.ShouldBeTrue(); content.ContentText.ShouldBe("指定V1材料正文"); content.VersionNumber.ShouldBe(1); content.Attachments.Single().Id.ShouldBe(f.SelectedFile);
        JsonSerializer.Serialize(content).ShouldNotContain("PRIVATE-V2"); JsonSerializer.Serialize(content).ShouldNotContain("BlobName");
        var publicPlan = await Plans.OpenMyAsync(f.PlanId, f.Published.Id); publicPlan.Content!.Materials.Single().Id.ShouldBe(f.Material.Id);
        var publicJson = JsonSerializer.Serialize(publicPlan); foreach (var secret in new[] { "KnowledgeItemId", "KnowledgeVersionId", "LibraryId", "SchoolKnowledgeShareId", "PRIVATE-" }) publicJson.ShouldNotContain(secret);
        using var file = await Materials.DownloadMyMaterialAsync(activity, f.Material.Id, f.SelectedFile); using var copied = new MemoryStream(); await file.GetStream().CopyToAsync(copied); Encoding.UTF8.GetString(copied.ToArray()).ShouldBe("必要讲义.txt");
        await Should.ThrowAsync<AbpAuthorizationException>(() => Materials.DownloadMyMaterialAsync(activity, f.Material.Id, f.UnselectedFile));
        await Should.ThrowAsync<AbpAuthorizationException>(() => GetRequiredService<IKnowledgeItemAppService>().GetAsync(f.Source.KnowledgeItemId));
        if (f.ShareId is Guid share) await Should.ThrowAsync<AbpAuthorizationException>(() => _sharing.GetDetailAsync(share));
    }
    [Fact]
    public async Task Teaching_Material_Withdrawal_Should_Block_New_Reads_And_Completion_Keep_Handed_In_History_And_Allow_Difficulty()
    {
        var f = await MaterialDeliveryAsync(); var activity = MaterialActivity(f); EducationStudentActivityEventDto completion;
        using (User(f.F.Source.Student.UserId)) {
            var row = (await Activities.GetMyActivitiesAsync(f.PlanId, f.Published.Id)).Activities.Single(x => x.Id == activity);
            await Activities.SaveMyActivityAsync(activity, ActivityInput(row, EducationTeachingActivityAction.Started));
            row = (await Activities.GetMyActivitiesAsync(f.PlanId, f.Published.Id)).Activities.Single(x => x.Id == activity);
            completion = await Activities.SaveMyActivityAsync(activity, ActivityInput(row, EducationTeachingActivityAction.Completed, "明确上交的学习总结"));
            (await Materials.GetMyMaterialAsync(activity, f.Material.Id)).IsAvailable.ShouldBeTrue();
        }
        using (User(f.F.Source.School.Owner)) await _sharing.RevokeAsync(f.ShareId!.Value);
        using (User(f.F.Source.Student.UserId)) {
            var blocked = await Materials.GetMyMaterialAsync(activity, f.Material.Id); blocked.IsAvailable.ShouldBeFalse(); blocked.ContentText.ShouldBeNull(); blocked.Attachments.ShouldBeEmpty();
            (await Should.ThrowAsync<BusinessException>(() => Materials.DownloadMyMaterialAsync(activity, f.Material.Id, f.SelectedFile))).Code.ShouldBe(EducationTeachingPlanErrors.MaterialUnavailable);
            var row = (await Activities.GetMyActivitiesAsync(f.PlanId, f.Published.Id)).Activities.Single(x => x.Id == activity); row.State.ShouldBe(EducationTeachingActivityState.Blocked); row.CompletedAtUtc.ShouldNotBeNull();
            (await Activities.GetMyActivityHistoryAsync(activity, completion.Id, new())).Items.ShouldContain(x => x.Id == completion.Id && x.SharedText == "明确上交的学习总结");
            var restart = ActivityInput(row, EducationTeachingActivityAction.Started, "希望重新学习");
            (await Should.ThrowAsync<BusinessException>(() => Activities.SaveMyActivityAsync(activity, restart))).Code.ShouldBe(EducationTeachingPlanErrors.MaterialUnavailable);
            var difficulty = ActivityInput(row, EducationTeachingActivityAction.Difficulty, "材料打不开"); difficulty.Difficulty = EducationTeachingActivityDifficulty.MaterialUnavailable;
            (await Activities.SaveMyActivityAsync(activity, difficulty)).SharedText.ShouldBe("材料打不开");
            (await Grades.GetMyResultAsync(f.F.RecipientId)).TotalScore.ShouldBe(10);
        }
        using (User(f.F.Source.School.Reader)) {
            var teacher = await Activities.GetActivityProgressAsync(f.PlanId); teacher.Activities.Single(x => x.Id == activity).CurrentStudent!.Difficulty.ShouldBe(EducationTeachingActivityDifficulty.MaterialUnavailable);
            var exposure = await WithUnitOfWorkAsync(async () => (await Repo<EducationTeachingPlanQuestionExposure>().GetListAsync(x => x.PlanRevisionId == f.Published.Id)).First().Id);
            var hint = PlanHint(await Plans.GetHintContextAsync(exposure));
            (await Should.ThrowAsync<BusinessException>(() => Plans.SaveHintReviewAsync(exposure, hint))).Code.ShouldBe(EducationTeachingPlanErrors.MaterialUnavailable);
        }
    }
    [Fact]
    public async Task Teaching_Material_Should_Require_Explicit_Student_Assignment_And_Separate_Attachment_Permission()
    {
        var (f, report) = await PlanSourceAsync(); var source = await SourceAsync(f.Source.School.Owner, "共享仅阅读正文"); var file = await MaterialFileAsync(source.KnowledgeVersionId, "未授下载.txt");
        SchoolKnowledgeShareDto share;
        using (User(f.Source.School.Owner)) { var input = Share(f.Source.School, source); input.Uses = SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Download; share = await _sharing.ShareVersionAsync(input); }
        var plan = Guid.NewGuid();
        using (User(f.Source.School.Reader)) {
            var draft = await PlanDraftAsync(f, report); await Plans.SaveDraftAsync(plan, draft);
            (await Materials.GetMaterialSourcesAsync(plan, new() { SchoolShared = true })).Items.ShouldBeEmpty();
            await Should.ThrowAsync<AbpAuthorizationException>(() => Materials.PreviewMaterialSourceAsync(plan, new() { Id = source.KnowledgeVersionId, KnowledgeItemId = source.KnowledgeItemId, SchoolKnowledgeShareId = share.Id }));
        }
        using (User(f.Source.School.Owner)) { await _sharing.RevokeAsync(share.Id); var input = Share(f.Source.School, source); input.Uses = SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.AssignToStudents; share = await _sharing.ShareVersionAsync(input); }
        using (User(f.Source.School.Reader)) {
            var sourceDto = (await Materials.GetMaterialSourcesAsync(plan, new() { SchoolShared = true, Filter = "共享仅" })).Items.Single();
            var preview = await Materials.PreviewMaterialSourceAsync(plan, sourceDto); preview.ContentText.ShouldBe("共享仅阅读正文"); preview.Attachments.ShouldBeEmpty();
            var input = await PlanDraftAsync(f, report); var current = await Plans.GetAsync(plan); input.ExpectedRevisionId = current.Current.Id; input.ConcurrencyStamp = current.ConcurrencyStamp;
            input.Content = input.Content with { Materials = [new() { Id = Guid.NewGuid(), StepId = input.Content.Steps[1].Id, Title = "尝试绕过附件用途", KnowledgeItemId = source.KnowledgeItemId, KnowledgeVersionId = source.KnowledgeVersionId, SchoolKnowledgeShareId = share.Id, AttachmentIds = [file] }] };
            (await Should.ThrowAsync<BusinessException>(() => Plans.SaveDraftAsync(plan, input))).Code.ShouldBe(EducationTeachingPlanErrors.MaterialUnavailable);
        }
    }
    [Fact]
    public async Task Teaching_Material_Should_Reject_Unprovided_Activities_Other_People_Tenants_And_Ended_Relations()
    {
        var f = await MaterialDeliveryAsync(open: false); var activity = MaterialActivity(f);
        using (User(f.F.Source.Student.UserId)) await Should.ThrowAsync<AbpAuthorizationException>(() => Materials.GetMyMaterialAsync(activity, f.Material.Id));
        using (User(f.F.Source.Student.UserId)) await Activities.GetMyActivitiesAsync(f.PlanId, f.Published.Id);
        using (User(f.F.Source.School.Reader)) await Should.ThrowAsync<AbpAuthorizationException>(() => Materials.GetMyMaterialAsync(activity, f.Material.Id));
        var other = await AssessmentStudentAsync(f.F.Source.School);
        using (User(other.UserId)) await Should.ThrowAsync<AbpAuthorizationException>(() => Materials.GetMyMaterialAsync(activity, f.Material.Id));
        using (User(f.F.Source.Student.UserId)) {
            using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid())) await Should.ThrowAsync<AbpAuthorizationException>(() => Materials.GetMyMaterialAsync(activity, f.Material.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Materials.GetMyMaterialAsync(activity, Guid.NewGuid()));
            await WithUnitOfWorkAsync(async () => { var member = await Repo<SchoolMembership>().GetAsync(f.F.Source.Student.MembershipId); member.EndAt(DateTime.UtcNow); await Repo<SchoolMembership>().UpdateManyAsync([member], autoSave: true); });
            await Should.ThrowAsync<AbpAuthorizationException>(() => Materials.GetMyMaterialAsync(activity, f.Material.Id));
        }
    }
    [Fact]
    public async Task Teaching_Material_Missing_Blob_Should_Return_A_Controlled_Error_Without_Leaking_Private_Storage_Paths()
    {
        var f = await MaterialDeliveryAsync(false); var file = await WithUnitOfWorkAsync(() => Repo<KnowledgeAttachment>().GetAsync(f.SelectedFile));
        await GetRequiredService<IBlobContainer<KnowledgeAttachmentBlobContainer>>().DeleteAsync(file.BlobName);
        using var actor = User(f.F.Source.Student.UserId);
        var error = await Should.ThrowAsync<BusinessException>(() => Materials.DownloadMyMaterialAsync(MaterialActivity(f), f.Material.Id, f.SelectedFile));
        error.Code.ShouldBe(EducationTeachingPlanErrors.MaterialUnavailable); error.Message.ShouldNotContain(file.BlobName);
    }

    [Fact]
    public async Task Teaching_Material_Preview_Should_Bound_Source_Attachments_Without_Expanding_The_Student_Grant()
    {
        var f = await MaterialDeliveryAsync(false);
        await WithUnitOfWorkAsync(async () => {
            var rows = Enumerable.Range(0, 199).Select(i => new KnowledgeAttachment(Guid.NewGuid(), null, f.Source.KnowledgeVersionId, $"extra-{i}.txt", $"synthetic/{i}", "text/plain", 0)).ToList();
            await Repo<KnowledgeAttachment>().InsertManyAsync(rows, autoSave: true);
        });
        using (User(f.F.Source.School.Reader)) {
            var source = new EducationMaterialSourceDto { Id = f.Source.KnowledgeVersionId, KnowledgeItemId = f.Source.KnowledgeItemId };
            (await Should.ThrowAsync<BusinessException>(() => Materials.PreviewMaterialSourceAsync(f.PlanId, source))).Code.ShouldBe(EducationLearningReportErrors.Capacity);
        }
        using (User(f.F.Source.Student.UserId)) (await Materials.GetMyMaterialAsync(MaterialActivity(f), f.Material.Id)).Attachments.Single().Id.ShouldBe(f.SelectedFile);
    }

    [Fact]
    public async Task Teaching_Material_Plan_Withdrawal_Should_Stop_New_Reads_But_Keep_Original_Activity_Receipts()
    {
        var f = await MaterialDeliveryAsync(false); var activity = MaterialActivity(f); SaveEducationMyActivityDto command; Guid receipt;
        using (User(f.F.Source.Student.UserId)) { var row = (await Activities.GetMyActivitiesAsync(f.PlanId, f.Published.Id)).Activities.Single(x => x.Id == activity); command = ActivityInput(row, EducationTeachingActivityAction.Started); receipt = (await Activities.SaveMyActivityAsync(activity, command)).Id; }
        using (User(f.F.Source.School.Reader)) await Plans.WithdrawAsync(f.PlanId, PlanTransition(await Plans.GetAsync(f.PlanId), "撤回后调整"));
        using (User(f.F.Source.Student.UserId)) { await Should.ThrowAsync<AbpAuthorizationException>(() => Materials.GetMyMaterialAsync(activity, f.Material.Id)); (await Activities.SaveMyActivityAsync(activity, command)).Id.ShouldBe(receipt); (await Activities.GetMyActivityHistoryAsync(activity, receipt, new())).Items.Single().Id.ShouldBe(receipt); }
    }
    [Theory]
    [InlineData(EducationPermissions.TeachingPlanManage)]
    [InlineData(EducationPermissions.AnalysisManage)]
    [InlineData(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    [InlineData(EducationPermissions.TeachingPlanView)]
    [InlineData(EducationPermissions.LearningParticipate)]
    public async Task Teaching_Material_Should_Explicitly_Reject_Denied_Function_Permissions(string permission)
    {
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(Arg.Any<string>()).Returns(true); checker.IsGrantedAsync(permission).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker); lazy.LazyGetRequiredService<IUnitOfWorkManager>().Returns(GetRequiredService<IUnitOfWorkManager>()); lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>());
        var service = new EducationTeachingPlanAppService { LazyServiceProvider = lazy }; var id = Guid.NewGuid();
        if (permission is EducationPermissions.TeachingPlanView or EducationPermissions.LearningParticipate) {
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetMyMaterialAsync(id, id)); await Should.ThrowAsync<AbpAuthorizationException>(() => service.DownloadMyMaterialAsync(id, id, id));
        } else {
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetMaterialSourcesAsync(id, new())); await Should.ThrowAsync<AbpAuthorizationException>(() => service.PreviewMaterialSourceAsync(id, new()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetTeacherMaterialAsync(id, id, id)); await Should.ThrowAsync<AbpAuthorizationException>(() => service.DownloadTeacherMaterialAsync(id, id, id, id));
        }
    }
}
