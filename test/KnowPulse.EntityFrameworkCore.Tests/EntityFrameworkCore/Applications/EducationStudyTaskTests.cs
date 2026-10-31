using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using NSubstitute;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationStudyTaskAppService Study => GetRequiredService<IEducationStudyTaskAppService>();
    private sealed record StudyFixture(Fixture School, AssessmentStudent Student, Guid TaskId, EducationStudyRevisionDto Publication, EducationStudyMaterial Material,
        Guid SelectedFile, Guid UnselectedFile, Guid? ShareId, SaveEducationStudyTaskDto Draft, PublishEducationStudyTaskDto Publish);
    private async Task<StudyFixture> StudyAsync(bool shared = true, bool future = false, bool publish = true, Fixture? existingSchool = null, AssessmentStudent? existingStudent = null)
    {
        var f = existingSchool ?? await FixtureAsync(); var student = existingStudent ?? await AssessmentStudentAsync(f); var source = await SourceAsync(shared ? f.Owner : f.Reader, "独立任务原始讲义");
        var selected = await MaterialFileAsync(source.KnowledgeVersionId, "指定材料.txt"); var other = await MaterialFileAsync(source.KnowledgeVersionId, "教师其他附件.txt");
        Guid? shareId = null;
        if (shared) using (User(f.Owner)) { var share = Share(f, source); share.Uses = SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.AssignToStudents | SchoolKnowledgeShareUses.Download;
            shareId = (await _sharing.ShareVersionAsync(share)).Id; }
        var material = new EducationStudyMaterial { Id = Guid.NewGuid(), Title = "学生讲义", KnowledgeItemId = source.KnowledgeItemId, KnowledgeVersionId = source.KnowledgeVersionId,
            SchoolKnowledgeShareId = shareId, AttachmentIds = [selected] };
        var taskId = Guid.NewGuid(); var now = DateTime.UtcNow;
        var draft = new SaveEducationStudyTaskDto { OperationId = Guid.NewGuid(), Scope = new() { TeacherTextbookSelectionId = f.ReaderSelection, TextbookKnowledgePointMappingIds = [f.MappingA] },
            Content = new() { Title = "课后巩固", Instructions = "阅读指定材料后总结自己的疑问", StartsAtUtc = future ? now.AddHours(1) : now.AddMinutes(-1), EndsAtUtc = now.AddDays(1),
                EstimatedMinutes = 20, Materials = [material], RecipientScope = EducationAssessmentRecipientScope.FullClass } };
        using var actor = User(f.Reader); var revision = await Study.SaveDraftAsync(taskId, draft); var root = await Study.GetAsync(taskId); var preview = await Study.PreviewRecipientsAsync(taskId);
        var command = new PublishEducationStudyTaskDto { OperationId = Guid.NewGuid(), ExpectedRevisionId = revision.Id, ConcurrencyStamp = root.ConcurrencyStamp,
            RosterFingerprint = preview.RosterFingerprint, ConfirmRecipientsMaterialsAndSafeText = true };
        if (publish) revision = await Study.PublishAsync(taskId, command);
        return new(f, student, taskId, revision, material, selected, other, shareId, draft, command);
    }
    private static Guid StudyRecipient(StudyFixture f) => EducationStudentGrowthSnapshot.StableId($"education-study-recipient-v1|{f.Publication.Id:D}|{f.Student.UserId:D}");
    [Fact]
    public async Task Study_Student_List_Should_Filter_School_Before_Count_And_Page_Without_Providing_Materials()
    {
        var first = await StudyAsync(false); var secondSchool = await FixtureAsync();
        var secondStudent = await WithUnitOfWorkAsync(async () => {
            var selection = await Repo<TeacherTextbookSelection>().GetAsync(secondSchool.ReaderSelection);
            var assignment = await Repo<TeachingAssignment>().GetAsync(selection.TeachingAssignmentId);
            var term = await Repo<AcademicTerm>().GetAsync(assignment.AcademicTermId);
            var member = new SchoolMembership(Guid.NewGuid(), null, secondSchool.SchoolId, first.Student.UserId, SchoolMemberRole.Student, term.StartsAtUtc, term.EndsAtUtc);
            var enrollment = new StudentEnrollment(Guid.NewGuid(), null, secondSchool.SchoolId, assignment.SchoolClassId, first.Student.UserId, term.StartsAtUtc, term.EndsAtUtc);
            await Repo<SchoolMembership>().InsertAsync(member, autoSave: true); await Repo<StudentEnrollment>().InsertAsync(enrollment, autoSave: true);
            return new AssessmentStudent(first.Student.UserId, member.Id, enrollment.Id);
        });
        var second = await StudyAsync(false, existingSchool: secondSchool, existingStudent: secondStudent);
        using var user = User(first.Student.UserId);
        (await Study.GetMyListAsync(new())).TotalCount.ShouldBe(2);
        var page = await Study.GetMyListAsync(new() { MaxResultCount = 1 }, first.School.SchoolId);
        page.TotalCount.ShouldBe(1); page.Items.Single().Id.ShouldBe(StudyRecipient(first)); page.Items.Single().SchoolId.ShouldBe(first.School.SchoolId);
        page.Items.Single().FirstProvidedAtUtc.ShouldBeNull(); page.Items.Single().Materials.ShouldBeEmpty(); page.Items.Single().Instructions.ShouldBeNull();
        (await Study.GetMyListAsync(new() { SkipCount = 1, MaxResultCount = 1 }, first.School.SchoolId)).Items.ShouldBeEmpty();
        (await Study.GetMyListAsync(new(), secondSchool.SchoolId)).Items.Single().Id.ShouldBe(StudyRecipient(second));
        (await Study.GetMyListAsync(new(), Guid.NewGuid())).TotalCount.ShouldBe(0);
        await Should.ThrowAsync<AbpAuthorizationException>(() => Study.OpenMyForSchoolAsync(StudyRecipient(second), first.School.SchoolId));
        (await WithUnitOfWorkAsync(() => Repo<EducationStudyRecipient>().GetListAsync())).All(x => x.FirstProvidedAtUtc == null).ShouldBeTrue();
        (await WithUnitOfWorkAsync(() => Repo<EducationStudyEvent>().GetCountAsync())).ShouldBe(0);
    }
    [Theory]
    [InlineData(EducationPermissions.LearningManage)]
    [InlineData(EducationPermissions.LearningParticipate)]
    [InlineData(KnowPulse.Permissions.KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    public async Task Study_Task_Should_Enforce_Function_Permissions_In_The_Service(string permission)
    {
        var f = await FixtureAsync(); using var user = User(f.Reader);
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(Arg.Any<string>()).Returns(true); checker.IsGrantedAsync(permission).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>()); lazy.LazyGetRequiredService<IUnitOfWorkManager>().Returns(GetRequiredService<IUnitOfWorkManager>());
        lazy.LazyGetRequiredService<EducationAssessmentAccessGuard>().Returns(GetRequiredService<EducationAssessmentAccessGuard>());
        var service = new EducationStudyTaskAppService { LazyServiceProvider = lazy }; var id = Guid.NewGuid();
        if (permission == EducationPermissions.LearningParticipate) {
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.OpenMyAsync(id)); await Should.ThrowAsync<AbpAuthorizationException>(() => service.SaveMyProgressAsync(id, new() { OperationId = id }));
        } else {
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetMaterialSourcesAsync(id, new())); await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetTeacherMaterialAsync(id, id, id));
        }
    }
    [Fact]
    public async Task Study_Task_Should_Recheck_Owner_Tenant_And_Current_Account_Even_For_Operation_Recovery()
    {
        var f = await StudyAsync(false); var started = await StudyStartedAsync(f); var id = StudyRecipient(f);
        using (User(f.School.Owner)) { await Should.ThrowAsync<AbpAuthorizationException>(() => Study.GetAsync(f.TaskId)); await Should.ThrowAsync<AbpAuthorizationException>(() => Study.GetOperationAsync(f.TaskId, f.Publish.OperationId)); }
        using (User(f.Student.UserId)) using (GetRequiredService<Volo.Abp.MultiTenancy.ICurrentTenant>().Change(Guid.NewGuid())) await Should.ThrowAsync<AbpAuthorizationException>(() => Study.GetMyOperationAsync(id, started.Id));
        await WithUnitOfWorkAsync(async () => { var account = await Repo<IdentityUser>().GetAsync(f.Student.UserId); account.SetIsActive(false); await Repo<IdentityUser>().UpdateAsync(account, autoSave: true); });
        using (User(f.Student.UserId)) await Should.ThrowAsync<AbpAuthorizationException>(() => Study.GetMyOperationAsync(id, started.Id));
        using (User(f.School.Reader)) { var progress = await Study.GetProgressAsync(f.TaskId, null, new()); progress.AssignedCount.ShouldBe(1); progress.VisibleCount.ShouldBe(0); }
        (await WithUnitOfWorkAsync(() => Repo<EducationStudyEvent>().GetCountAsync())).ShouldBe(1);
    }
    [Fact]
    public async Task Study_Task_Actual_Provision_Should_Version_Analysis_Context_Without_Inventing_Scoring_Or_Mastery()
    {
        var graded = await GradingFixtureAsync(false); await ExposureReleaseAsync(graded, KnowPulse.Exam.ExamResultVisibility.FullReview);
        var f = await StudyAsync(false, existingSchool: graded.Source.School, existingStudent: graded.Source.Student);
        var reader = GetRequiredService<EducationLearningReportSourceReader>(); var end = DateTime.UtcNow;
        using (User(f.School.Reader)) {
            var before = await WithUnitOfWorkAsync(() => reader.CaptureAsync(f.Publication.Scope, end.AddDays(-1), end, [graded.TaskId]));
            before.Students.Single(x => x.Student.StudentUserId == f.Student.UserId).Exposures.ShouldBeEmpty();
        }
        using (User(f.Student.UserId)) { await Study.GetMyListAsync(new()); await Study.OpenMyAsync(StudyRecipient(f)); }
        end = DateTime.UtcNow;
        using (User(f.School.Reader)) {
            var after = await WithUnitOfWorkAsync(() => reader.CaptureAsync(f.Publication.Scope, end.AddDays(-1), end, [graded.TaskId]));
            var student = after.Students.Single(x => x.Student.StudentUserId == f.Student.UserId);
            student.Exposures.ShouldNotBeEmpty(); student.Exposures.All(x => x.Use == EducationLearningExposureUse.Unverified).ShouldBeTrue();
            JsonSerializer.Serialize(after).ShouldNotContain("独立任务原始讲义"); JsonSerializer.Serialize(after).ShouldNotContain("阅读指定材料后");
            var original = student.Exposures.First().OriginalQuestionId;
            await Should.ThrowAsync<BusinessException>(() => WithUnitOfWorkAsync(() => reader.CheckFormalNoveltyAsync(f.Publication.Scope, f.Student.UserId, [original], [])));
        }
        (await WithUnitOfWorkAsync(() => Repo<EducationStudyEvent>().GetCountAsync())).ShouldBe(0);
    }
    private async Task<EducationStudentStudyEventDto> StudyStartedAsync(StudyFixture f)
    {
        using var actor = User(f.Student.UserId); var row = await Study.OpenMyAsync(StudyRecipient(f));
        return await Study.SaveMyProgressAsync(row.Id, new() { OperationId = Guid.NewGuid(), ExpectedStudentRevisionId = row.CurrentStudentRevisionId, Action = EducationTeachingActivityAction.Started });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Study_Task_Should_Work_Without_Report_Or_Exam_And_Grant_Only_Fixed_Materials(bool shared)
    {
        var f = await StudyAsync(shared); var recipient = StudyRecipient(f);
        (await WithUnitOfWorkAsync(() => Repo<EducationLearningReport>().GetCountAsync())).ShouldBe(0);
        (await WithUnitOfWorkAsync(() => Repo<EducationAssessmentTask>().GetCountAsync())).ShouldBe(0);
        using var actor = User(f.Student.UserId);
        var metadata = (await Study.GetMyListAsync(new() { MaxResultCount = 10 })).Items.Single(); metadata.Instructions.ShouldBeNull(); metadata.FirstProvidedAtUtc.ShouldBeNull();
        await Should.ThrowAsync<AbpAuthorizationException>(() => Study.GetMyMaterialAsync(recipient, f.Material.Id));
        var view = await Study.OpenMyAsync(recipient); view.FirstProvidedAtUtc.ShouldNotBeNull(); view.CurrentStudent.ShouldBeNull(); view.State.ShouldBe(EducationTeachingActivityState.NotStarted);
        view.Materials.Single().Title.ShouldBe("学生讲义"); view.Instructions.ShouldBe(f.Draft.Content.Instructions);
        (await Study.GetMyMaterialAsync(recipient, f.Material.Id)).ContentText!.ShouldContain("独立任务原始讲义");
        using var file = await Study.DownloadMyMaterialAsync(recipient, f.Material.Id, f.SelectedFile); file.FileName.ShouldBe("指定材料.txt");
        await Should.ThrowAsync<AbpAuthorizationException>(() => Study.DownloadMyMaterialAsync(recipient, f.Material.Id, f.UnselectedFile));
        JsonSerializer.Serialize(view).ShouldNotContain("KnowledgeVersionId"); JsonSerializer.Serialize(view).ShouldNotContain("SchoolKnowledgeShareId");
        var outsider = await AssessmentStudentAsync(f.School);
        using (User(outsider.UserId)) { (await Study.GetMyListAsync(new())).Items.ShouldBeEmpty(); await Should.ThrowAsync<AbpAuthorizationException>(() => Study.OpenMyAsync(recipient)); }
    }
    [Fact]
    public async Task Study_Task_Should_Stop_Shared_Reads_And_New_Completion_But_Keep_Submission_History_And_Recovery()
    {
        var f = await StudyAsync(); var started = await StudyStartedAsync(f); var recipient = StudyRecipient(f);
        var complete = new SaveEducationMyActivityDto { OperationId = Guid.NewGuid(), ExpectedStudentRevisionId = started.Id, Action = EducationTeachingActivityAction.Completed,
            SharedText = "已完成总结", ConfirmSharingWithTeacher = true, ConfirmActivityOnly = true, ReportedMinutes = 18 };
        using (User(f.Student.UserId)) await Study.SaveMyProgressAsync(recipient, complete);
        using (User(f.School.Owner)) await _sharing.RevokeAsync(f.ShareId!.Value);
        using (User(f.Student.UserId)) {
            var view = await Study.OpenMyAsync(recipient); view.Availability.ShouldBe(EducationStudyTaskAvailability.Unavailable); view.Instructions.ShouldBeNull();
            view.State.ShouldBe(EducationTeachingActivityState.ReportedComplete); view.CurrentStudent!.SharedText.ShouldBe("已完成总结");
            await Should.ThrowAsync<AbpAuthorizationException>(() => Study.GetMyMaterialAsync(recipient, f.Material.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Study.DownloadMyMaterialAsync(recipient, f.Material.Id, f.SelectedFile));
            (await Study.SaveMyProgressAsync(recipient, complete)).Id.ShouldBe(complete.OperationId);
            (await Study.GetMyHistoryAsync(recipient, complete.OperationId, new())).TotalCount.ShouldBe(2);
            await Should.ThrowAsync<BusinessException>(() => Study.SaveMyProgressAsync(recipient, new() { OperationId = Guid.NewGuid(), ExpectedStudentRevisionId = complete.OperationId, Action = EducationTeachingActivityAction.Started }));
            var difficulty = await Study.SaveMyProgressAsync(recipient, new() { OperationId = Guid.NewGuid(), ExpectedStudentRevisionId = complete.OperationId,
                Action = EducationTeachingActivityAction.Difficulty, Difficulty = EducationTeachingActivityDifficulty.MaterialUnavailable, SharedText = "材料已不可读", ConfirmSharingWithTeacher = true });
            difficulty.Action.ShouldBe(EducationTeachingActivityAction.Difficulty);
        }
        using (User(f.School.Reader)) (await Study.PublishAsync(f.TaskId, f.Publish)).Id.ShouldBe(f.Publication.Id);
    }
    [Fact]
    public async Task Study_Task_Should_Keep_Private_Follow_Up_Separate_And_Reject_Stale_Student_Head()
    {
        var f = await StudyAsync(false); await StudyStartedAsync(f); var id = StudyRecipient(f); EducationStudentStudyEventDto difficulty;
        using (User(f.Student.UserId)) { var view = await Study.OpenMyAsync(id); difficulty = await Study.SaveMyProgressAsync(id, new() { OperationId = Guid.NewGuid(), ExpectedStudentRevisionId = view.CurrentStudentRevisionId,
            Action = EducationTeachingActivityAction.Difficulty, Difficulty = EducationTeachingActivityDifficulty.Unclear, SharedText = "第二步不理解", ConfirmSharingWithTeacher = true }); }
        SaveEducationStudyFollowUpDto note;
        using (User(f.School.Reader)) {
            var progress = await Study.GetProgressAsync(f.TaskId, null, new()); var student = progress.Students.Single();
            note = new() { OperationId = Guid.NewGuid(), ExpectedRevisionId = student.CurrentRevisionId, ExpectedStudentRevisionId = difficulty.Id,
                ConcurrencyStamp = student.ConcurrencyStamp, InternalTeacherNote = "内部：下节课单独示范", ConfirmPrivateHandling = true };
            await Study.SaveFollowUpAsync(id, note); (await Study.GetFollowUpOperationAsync(id, note.OperationId))!.InternalTeacherNote.ShouldBe(note.InternalTeacherNote);
        }
        using (User(f.Student.UserId)) {
            var view = await Study.OpenMyAsync(id); view.CurrentStudentRevisionId.ShouldBe(difficulty.Id); view.TeacherHandledAtUtc.ShouldNotBeNull();
            JsonSerializer.Serialize(view).ShouldNotContain("下节课单独示范"); JsonSerializer.Serialize(view).ShouldNotContain("InternalTeacherNote");
            (await Study.GetMyHistoryAsync(id, difficulty.Id, new())).Items.ShouldNotContain(x => x.Action == EducationTeachingActivityAction.TeacherFollowUp);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Study.GetMyOperationAsync(id, note.OperationId));
            await Study.SaveMyProgressAsync(id, new() { OperationId = Guid.NewGuid(), ExpectedStudentRevisionId = difficulty.Id, Action = EducationTeachingActivityAction.Started });
        }
        using (User(f.School.Reader)) {
            (await Study.SaveFollowUpAsync(id, note)).Id.ShouldBe(note.OperationId);
            note.OperationId = Guid.NewGuid(); await Should.ThrowAsync<AbpDbConcurrencyException>(() => Study.SaveFollowUpAsync(id, note));
        }
    }
    [Fact]
    public async Task Study_Task_Should_Require_Exact_Roster_Then_Freeze_It()
    {
        var f = await StudyAsync(false, publish: false); var added = await AssessmentStudentAsync(f.School);
        using var actor = User(f.School.Reader);
        await Should.ThrowAsync<BusinessException>(() => Study.PublishAsync(f.TaskId, f.Publish));
        var preview = await Study.PreviewRecipientsAsync(f.TaskId); preview.Students.Count.ShouldBe(2); f.Publish.RosterFingerprint = preview.RosterFingerprint;
        await Study.PublishAsync(f.TaskId, f.Publish); await AssessmentStudentAsync(f.School);
        var progress = await Study.GetProgressAsync(f.TaskId, null, new()); progress.AssignedCount.ShouldBe(2); progress.Students.Count.ShouldBe(2);
        (await Study.SaveDraftAsync(f.TaskId, f.Draft)).Id.ShouldBe(f.Draft.OperationId);
    }
    [Fact]
    public async Task Study_Task_Should_Hide_Body_Before_Start_And_Reject_Unprovided_Progress()
    {
        var f = await StudyAsync(false, future: true); using var actor = User(f.Student.UserId); var id = StudyRecipient(f);
        var row = await Study.OpenMyAsync(id); row.Availability.ShouldBe(EducationStudyTaskAvailability.Waiting); row.FirstProvidedAtUtc.ShouldBeNull(); row.Instructions.ShouldBeNull();
        await Should.ThrowAsync<BusinessException>(() => Study.SaveMyProgressAsync(id, new() { OperationId = Guid.NewGuid(), Action = EducationTeachingActivityAction.Started }));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Study.GetMyMaterialAsync(id, f.Material.Id));
    }
    [Fact]
    public async Task Study_Task_Should_Stop_Old_Reads_After_Replacement_And_Preserve_Withdrawn_Reason_Across_Private_Drafts()
    {
        var f = await StudyAsync(false); var started = await StudyStartedAsync(f); var id = StudyRecipient(f); EducationStudyRevisionDto replacement;
        using (User(f.School.Reader)) {
            var root = await Study.GetAsync(f.TaskId); var next = new SaveEducationStudyTaskDto { OperationId = Guid.NewGuid(), ExpectedRevisionId = root.Current.Id, ConcurrencyStamp = root.ConcurrencyStamp,
                Scope = f.Draft.Scope, Content = f.Draft.Content with { Title = "修订版讲义任务" } }; await Study.SaveDraftAsync(f.TaskId, next);
            root = await Study.GetAsync(f.TaskId); var roster = await Study.PreviewRecipientsAsync(f.TaskId);
            replacement = await Study.PublishAsync(f.TaskId, new() { OperationId = Guid.NewGuid(), ExpectedRevisionId = root.Current.Id, ConcurrencyStamp = root.ConcurrencyStamp,
                RosterFingerprint = roster.RosterFingerprint, PublicReason = "已调整任务，请查看新版本", ConfirmRecipientsMaterialsAndSafeText = true });
        }
        using (User(f.Student.UserId)) {
            var old = await Study.OpenMyAsync(id); old.Availability.ShouldBe(EducationStudyTaskAvailability.Superseded); old.Title.ShouldBe(f.Draft.Content.Title); old.PublicReason.ShouldBe("已调整任务，请查看新版本");
            old.Instructions.ShouldBeNull(); (await Study.GetMyHistoryAsync(id, started.Id, new())).TotalCount.ShouldBe(1);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Study.GetMyMaterialAsync(id, f.Material.Id));
        }
        using (User(f.School.Reader)) {
            var root = await Study.GetAsync(f.TaskId); var withdraw = new WithdrawEducationStudyTaskDto { OperationId = Guid.NewGuid(), ExpectedRevisionId = root.Current.Id, ConcurrencyStamp = root.ConcurrencyStamp,
                PublicReason = "老师撤回，稍后重新安排", ConfirmStudentVisibleReason = true }; await Study.WithdrawAsync(f.TaskId, withdraw);
            root = await Study.GetAsync(f.TaskId); await Study.SaveDraftAsync(f.TaskId, new() { OperationId = Guid.NewGuid(), ExpectedRevisionId = root.Current.Id, ConcurrencyStamp = root.ConcurrencyStamp,
                Scope = f.Draft.Scope, Content = f.Draft.Content with { Title = "尚未公开的私有草稿" } });
            (await Study.WithdrawAsync(f.TaskId, withdraw)).Id.ShouldBe(withdraw.OperationId);
        }
        using (User(f.Student.UserId)) { var view = await Study.OpenMyAsync(id); view.Availability.ShouldBe(EducationStudyTaskAvailability.Withdrawn); view.PublicReason.ShouldBe("老师撤回，稍后重新安排");
            JsonSerializer.Serialize(view).ShouldNotContain("尚未公开的私有草稿"); }
    }
}
