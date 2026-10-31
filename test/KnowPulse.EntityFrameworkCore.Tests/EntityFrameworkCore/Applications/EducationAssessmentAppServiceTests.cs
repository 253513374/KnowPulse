using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

// Reuse the real school, curriculum, sharing and formal-question fixtures from this test class.
public partial class EducationCurriculumRetrievalTests
{
    private IEducationAssessmentAppService Assessments => GetRequiredService<IEducationAssessmentAppService>();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assessment_Function_Permission_Should_Be_Required_For_An_Otherwise_Valid_Teacher_Or_Student(bool teacherMode)
    {
        var f = await FixtureAsync();
        var student = await AssessmentStudentAsync(f);
        CreateEducationAssessmentDraftDto input;
        EducationAssessmentTaskDto task;
        PublishEducationAssessmentDto publish;
        EducationAssessmentTaskRevisionDto published;
        using (User(f.Reader))
        {
            input = await AssessmentInputAsync(f);
            task = await Assessments.CreateDraftAsync(input);
            publish = AssessmentPublish(await Assessments.PreviewRecipientsAsync(task.Id));
            published = await Assessments.PublishAsync(task.Id, publish);
        }
        var permission = teacherMode ? EducationPermissions.AssessmentManage : EducationPermissions.AssessmentParticipate;
        var checker = Substitute.For<IPermissionChecker>();
        checker.IsGrantedAsync(permission).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>();
        lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>());
        var denied = new EducationAssessmentAppService { LazyServiceProvider = lazy };
        using (User(teacherMode ? f.Reader : student.UserId))
        {
            if (teacherMode)
            {
                input.OperationId = Guid.NewGuid();
                await Should.ThrowAsync<AbpAuthorizationException>(() => denied.CreateDraftAsync(input));
                await Should.ThrowAsync<AbpAuthorizationException>(() => denied.GetMyListAsync(new()));
                await Should.ThrowAsync<AbpAuthorizationException>(() => denied.PublishAsync(task.Id, publish));
            }
            else
            {
                var recipientId = published.Recipients.Single().Id;
                await Should.ThrowAsync<AbpAuthorizationException>(() => denied.GetMyStudentListAsync(new()));
                await Should.ThrowAsync<AbpAuthorizationException>(() => denied.GetMyStudentTaskAsync(recipientId));
                await Should.ThrowAsync<AbpAuthorizationException>(() => denied.StartOrResumeAsync(recipientId, new() { OperationId = Guid.NewGuid() }));
                await Should.ThrowAsync<AbpAuthorizationException>(() => denied.PreviewSubmissionAsync(Guid.NewGuid()));
            }
        }
        await checker.Received(teacherMode ? 3 : 4).IsGrantedAsync(permission);
        await WithUnitOfWorkAsync(async () =>
        {
            (await Repo<EducationAssessmentTask>().GetListAsync()).Count.ShouldBe(1);
            (await Repo<EducationAssessmentAttempt>().GetListAsync()).ShouldBeEmpty();
            (await Repo<EducationAssessmentRecipient>().GetAsync(published.Recipients.Single().Id)).StartedAttemptCount.ShouldBe(0);
        });
    }

    [Fact]
    public async Task Assessment_Publication_Should_Freeze_The_Confirmed_Roster_And_Recover_The_Original_Operation()
    {
        var f = await FixtureAsync();
        var studentA = await AssessmentStudentAsync(f);
        var studentB = await AssessmentStudentAsync(f);
        using var teacher = User(f.Reader);
        var input = await AssessmentInputAsync(f);
        var task = await Assessments.CreateDraftAsync(input);
        (await Assessments.CreateDraftAsync(input)).Id.ShouldBe(task.Id);
        (await Assessments.GetAsync(task.Id)).ConcurrencyStamp.ShouldBe(task.ConcurrencyStamp);
        var preview = await Assessments.PreviewRecipientsAsync(task.Id);
        preview.Students.Select(x => x.StudentUserId).Order().ShouldBe(new[] { studentA.UserId, studentB.UserId }.Order());
        var publish = AssessmentPublish(preview);
        var revision = await Assessments.PublishAsync(task.Id, publish);
        revision.Id.ShouldBe(publish.OperationId);
        revision.RevisionNumber.ShouldBe(2);
        revision.BasedOnRevisionId.ShouldBe(task.CurrentRevision.Id);
        revision.Recipients.Select(x => x.StudentUserId).Order().ShouldBe(new[] { studentA.UserId, studentB.UserId }.Order());
        revision.RosterFingerprint.ShouldBe(preview.RosterFingerprint);
        revision.PublishedAtUtc.ShouldNotBeNull();
        var schoolStamp = (await WithUnitOfWorkAsync(() => Repo<School>().GetAsync(f.SchoolId))).ConcurrencyStamp;
        await AssessmentStudentAsync(f);
        var repeated = await Assessments.PublishAsync(task.Id, publish);
        repeated.Id.ShouldBe(revision.Id);
        repeated.Recipients.Select(x => x.Id).Order().ShouldBe(revision.Recipients.Select(x => x.Id).Order());
        repeated.RequestFingerprint.ShouldBe(revision.RequestFingerprint);
        (await WithUnitOfWorkAsync(() => Repo<School>().GetAsync(f.SchoolId))).ConcurrencyStamp.ShouldBe(schoolStamp);
        var receipt = await Assessments.GetOperationAsync(task.Id, publish.OperationId);
        receipt!.PublishedAtUtc.ShouldBe(revision.PublishedAtUtc);
        (await Assessments.GetOperationAsync(task.Id, input.OperationId))!.PublishedAtUtc.ShouldBeNull();
        (await Assessments.GetOperationAsync(task.Id, Guid.NewGuid())).ShouldBeNull();
        publish.RosterFingerprint = new string('a', 64);
        (await Should.ThrowAsync<BusinessException>(() => Assessments.PublishAsync(task.Id, publish)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.ConflictingOperation);
        await WithUnitOfWorkAsync(async () =>
        {
            (await Repo<EducationAssessmentTaskRevision>().GetListAsync(x => x.TaskId == task.Id)).Count.ShouldBe(2);
            (await Repo<EducationAssessmentRecipient>().GetListAsync(x => x.TaskId == task.Id)).Count.ShouldBe(2);
            (await Repo<ExamPlan>().GetListAsync()).ShouldBeEmpty();
            (await Repo<ExamAssignment>().GetListAsync()).ShouldBeEmpty();
        });
    }

    [Fact]
    public async Task Assessment_Save_Should_Append_Immutable_Drafts_And_Reject_An_Old_Seen_Revision()
    {
        var f = await FixtureAsync();
        using var teacher = User(f.Reader);
        var task = await Assessments.CreateDraftAsync(await AssessmentInputAsync(f));
        var input = AssessmentSave(task);
        input.Title = "教师重新确认的测评标题";
        var saved = await Assessments.SaveDraftAsync(task.Id, input);
        saved.CurrentRevision.Id.ShouldBe(input.OperationId);
        saved.CurrentRevision.RevisionNumber.ShouldBe(2);
        saved.CurrentRevision.BasedOnRevisionId.ShouldBe(task.CurrentRevision.Id);
        (await Assessments.SaveDraftAsync(task.Id, input)).CurrentRevision.Id.ShouldBe(saved.CurrentRevision.Id);
        var original = await Assessments.GetOperationAsync(task.Id, task.CurrentRevision.Id);
        original!.Title.ShouldBe(task.CurrentRevision.Title);
        var nextInput = AssessmentSave(saved);
        nextInput.Title = "后一次保存的当前稿";
        var next = await Assessments.SaveDraftAsync(task.Id, nextInput);
        var replay = await Assessments.SaveDraftAsync(task.Id, input);
        replay.CurrentRevision.Id.ShouldBe(next.CurrentRevision.Id);
        replay.CurrentRevision.Title.ShouldBe(nextInput.Title);
        var savedReceipt = await Assessments.GetOperationAsync(task.Id, input.OperationId);
        savedReceipt!.Id.ShouldBe(saved.CurrentRevision.Id);
        savedReceipt.Title.ShouldBe("教师重新确认的测评标题");
        var stale = AssessmentSave(task);
        stale.Title = "旧页面未见当前稿";
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Assessments.SaveDraftAsync(task.Id, stale));
        input.Title = "复用操作号却改了事实";
        (await Should.ThrowAsync<BusinessException>(() => Assessments.SaveDraftAsync(task.Id, input)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.ConflictingOperation);
    }

    [Fact]
    public async Task Assessment_Roster_Changes_Should_Require_A_New_Preview_Before_Publication()
    {
        var f = await FixtureAsync();
        await AssessmentStudentAsync(f);
        using var teacher = User(f.Reader);
        var task = await Assessments.CreateDraftAsync(await AssessmentInputAsync(f));
        var oldPreview = await Assessments.PreviewRecipientsAsync(task.Id);
        await AssessmentStudentAsync(f);
        (await Should.ThrowAsync<BusinessException>(() => Assessments.PublishAsync(task.Id, AssessmentPublish(oldPreview))))
            .Code.ShouldBe(EducationAssessmentErrorCodes.RosterChanged);
        (await Assessments.GetAsync(task.Id)).PublishedRevisionId.ShouldBeNull();
        await WithUnitOfWorkAsync(async () =>
            (await Repo<EducationAssessmentRecipient>().GetListAsync(x => x.TaskId == task.Id)).ShouldBeEmpty());
        var fresh = await Assessments.PreviewRecipientsAsync(task.Id);
        fresh.RosterFingerprint.ShouldNotBe(oldPreview.RosterFingerprint);
        (await Assessments.PublishAsync(task.Id, AssessmentPublish(fresh))).Recipients.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Assessment_Selection_Should_Stay_In_The_Taught_Class_And_Student_Access_Should_Stay_In_The_Frozen_List()
    {
        var f = await FixtureAsync();
        var selected = await AssessmentStudentAsync(f);
        var unselected = await AssessmentStudentAsync(f);
        var otherClass = await WithUnitOfWorkAsync(async () =>
        {
            var selection = await Repo<TeacherTextbookSelection>().GetAsync(f.ReaderSelection);
            var assignment = await Repo<TeachingAssignment>().GetAsync(selection.TeachingAssignmentId);
            var schoolClass = new SchoolClass(Guid.NewGuid(), null, f.SchoolId, assignment.AcademicTermId, "OTHER", "其他班级", 7);
            await Repo<SchoolClass>().InsertAsync(schoolClass, autoSave: true);
            return schoolClass.Id;
        });
        var outsider = await AssessmentStudentAsync(f, otherClass);
        EducationAssessmentTaskDto task;
        EducationAssessmentTaskRevisionDto published;
        using (User(f.Reader))
        {
            var input = await AssessmentInputAsync(f);
            input.RecipientScope = EducationAssessmentRecipientScope.SelectedStudents;
            input.StudentUserIds = [outsider.UserId];
            var invalid = await Assessments.CreateDraftAsync(input);
            await Should.ThrowAsync<BusinessException>(() => Assessments.PreviewRecipientsAsync(invalid.Id));
            input.OperationId = Guid.NewGuid();
            input.StudentUserIds = [selected.UserId];
            task = await Assessments.CreateDraftAsync(input);
            var preview = await Assessments.PreviewRecipientsAsync(task.Id);
            preview.Students.Single().StudentUserId.ShouldBe(selected.UserId);
            published = await Assessments.PublishAsync(task.Id, AssessmentPublish(preview));
        }
        var recipientId = published.Recipients.Single().Id;
        using (User(unselected.UserId))
        {
            (await Assessments.GetMyStudentListAsync(new())).Items.ShouldBeEmpty();
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetMyStudentTaskAsync(recipientId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.StartOrResumeAsync(recipientId, new() { OperationId = Guid.NewGuid() }));
        }
        using (User(f.Owner))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetAsync(task.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.PreviewRecipientsAsync(task.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetOperationAsync(task.Id, published.Id));
        }
        using (User(selected.UserId))
        {
            (await Assessments.GetMyStudentListAsync(new())).Items.Single().Id.ShouldBe(recipientId);
            using (GetRequiredService<Volo.Abp.MultiTenancy.ICurrentTenant>().Change(Guid.NewGuid()))
                await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetMyStudentTaskAsync(recipientId));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Assessment_Detail_Should_Not_Start_And_Resume_Should_Preserve_One_Deadline_And_Safe_Paper(bool windowLimits)
    {
        var f = await FixtureAsync();
        var student = await AssessmentStudentAsync(f);
        EducationAssessmentTaskRevisionDto published;
        using (User(f.Reader))
        {
            var input = await AssessmentInputAsync(f, fill: true);
            input.DurationMinutes = windowLimits ? 10 : 1;
            input.EndsAtUtc = DateTime.UtcNow.AddMinutes(windowLimits ? 2 : 60);
            var task = await Assessments.CreateDraftAsync(input);
            published = await Assessments.PublishAsync(task.Id, AssessmentPublish(await Assessments.PreviewRecipientsAsync(task.Id)));
        }
        var recipientId = published.Recipients.Single().Id;
        using var participant = User(student.UserId);
        var detail = await Assessments.GetMyStudentTaskAsync(recipientId);
        detail.SchoolId.ShouldBe(published.SchoolId);
        (await Assessments.GetMyStudentListAsync(new())).Items.Single().SchoolId.ShouldBe(published.SchoolId);
        detail.CurrentAttemptId.ShouldBeNull();
        detail.CanStart.ShouldBeTrue();
        detail.ConfiguredResultVisibility.ShouldBe(ExamResultVisibility.FullReview);
        detail.CurrentResultVisibility.ShouldBe(ExamResultVisibility.Hidden);
        await Assessments.GetMyStudentTaskAsync(recipientId);
        await WithUnitOfWorkAsync(async () =>
        {
            (await Repo<EducationAssessmentRecipient>().GetAsync(recipientId)).StartedAttemptCount.ShouldBe(0);
            (await Repo<EducationAssessmentAttempt>().GetListAsync()).ShouldBeEmpty();
        });
        var start = new StartEducationAssessmentDto { OperationId = Guid.NewGuid() };
        var attempt = await Assessments.StartOrResumeAsync(recipientId, start);
        attempt.Id.ShouldBe(start.OperationId);
        attempt.AttemptNumber.ShouldBe(1);
        attempt.ExpiresAtUtc.ShouldBe(windowLimits ? published.EndsAtUtc : attempt.StartedAtUtc.AddMinutes(1));
        var repeated = await Assessments.StartOrResumeAsync(recipientId, start);
        repeated.Id.ShouldBe(attempt.Id);
        repeated.StartedAtUtc.ShouldBe(attempt.StartedAtUtc);
        repeated.ExpiresAtUtc.ShouldBe(attempt.ExpiresAtUtc);
        var resumed = await Assessments.StartOrResumeAsync(recipientId, new() { OperationId = Guid.NewGuid() });
        resumed.Id.ShouldBe(attempt.Id);
        resumed.ExpiresAtUtc.ShouldBe(attempt.ExpiresAtUtc);
        resumed.Paper.Questions.Single().ResponseFields.Select(x => x.StemMarker).ShouldBe(new[] { "【1】", "【2】" });
        var serialized = JsonSerializer.Serialize(resumed);
        foreach (var restricted in new[] { "PRIVATE-ANSWER-SENTINEL", "PRIVATE-REFERENCE-SENTINEL", "PRIVATE-ANALYSIS-SENTINEL",
                     "AllowedAnswers", "ScoringDefinition", "ScoringPoints", "CorrectAnswers", "CurriculumSources", "QuestionDefinitionRevisionId" })
            serialized.ShouldNotContain(restricted);
        await WithUnitOfWorkAsync(async () =>
        {
            (await Repo<EducationAssessmentRecipient>().GetAsync(recipientId)).StartedAttemptCount.ShouldBe(1);
            (await Repo<EducationAssessmentAttempt>().GetListAsync(x => x.RecipientId == recipientId)).Count.ShouldBe(1);
            (await Repo<ExamPlan>().GetListAsync()).ShouldBeEmpty();
            (await Repo<ExamAssignment>().GetListAsync()).ShouldBeEmpty();
        });
    }

    [Theory]
    [InlineData("membership")]
    [InlineData("enrollment")]
    [InlineData("account")]
    public async Task Assessment_Student_Relation_Or_Account_Withdrawal_Should_Block_Read_And_Start(string change)
    {
        var f = await FixtureAsync();
        var student = await AssessmentStudentAsync(f);
        EducationAssessmentTaskRevisionDto published;
        using (User(f.Reader))
        {
            var task = await Assessments.CreateDraftAsync(await AssessmentInputAsync(f));
            published = await Assessments.PublishAsync(task.Id, AssessmentPublish(await Assessments.PreviewRecipientsAsync(task.Id)));
        }
        await WithUnitOfWorkAsync(async () =>
        {
            if (change == "membership")
            {
                var membership = await Repo<SchoolMembership>().GetAsync(student.MembershipId);
                membership.EndAt(DateTime.UtcNow);
                await Repo<SchoolMembership>().UpdateManyAsync([membership], autoSave: true);
            }
            else if (change == "enrollment")
            {
                var enrollment = await Repo<StudentEnrollment>().GetAsync(student.EnrollmentId);
                enrollment.EndAt(DateTime.UtcNow);
                await Repo<StudentEnrollment>().UpdateManyAsync([enrollment], autoSave: true);
            }
            else
            {
                var account = await Repo<IdentityUser>().GetAsync(student.UserId);
                account.SetIsActive(false);
                await Repo<IdentityUser>().UpdateManyAsync([account], autoSave: true);
            }
            var school = await Repo<School>().GetAsync(f.SchoolId);
            school.AdvanceConfigurationRevision();
            await Repo<School>().UpdateManyAsync([school], autoSave: true);
        });
        using var participant = User(student.UserId);
        var recipient = published.Recipients.Single().Id;
        await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetMyStudentTaskAsync(recipient));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.StartOrResumeAsync(recipient, new() { OperationId = Guid.NewGuid() }));
        if (change != "account") (await Assessments.GetMyStudentListAsync(new())).Items.ShouldBeEmpty();
        await WithUnitOfWorkAsync(async () => (await Repo<EducationAssessmentAttempt>().GetListAsync()).ShouldBeEmpty());
    }

    [Theory]
    [InlineData("assignment")]
    [InlineData("membership")]
    public async Task Assessment_Former_Teacher_Should_Not_Read_Frozen_Recipients_Through_Own_Task_History(string change)
    {
        var f = await FixtureAsync();
        var student = await AssessmentStudentAsync(f);
        using var teacher = User(f.Reader);
        var input = await AssessmentInputAsync(f);
        var task = await Assessments.CreateDraftAsync(input);
        var publish = AssessmentPublish(await Assessments.PreviewRecipientsAsync(task.Id));
        var published = await Assessments.PublishAsync(task.Id, publish);
        input.OperationId = Guid.NewGuid();
        input.RecipientScope = EducationAssessmentRecipientScope.SelectedStudents;
        input.StudentUserIds = [student.UserId];
        var selectedDraft = await Assessments.CreateDraftAsync(input);
        (await Assessments.GetMyListAsync(new())).TotalCount.ShouldBe(2);
        await WithUnitOfWorkAsync(async () =>
        {
            if (change == "assignment")
            {
                var assignment = await Repo<TeachingAssignment>().GetAsync(published.TeachingAssignmentId);
                assignment.EndAt(DateTime.UtcNow);
                await Repo<TeachingAssignment>().UpdateManyAsync([assignment], autoSave: true);
            }
            else
            {
                var member = (await Repo<SchoolMembership>().GetListAsync(x => x.SchoolId == f.SchoolId &&
                    x.UserId == f.Reader && x.Role == SchoolMemberRole.Teacher)).Single();
                member.EndAt(DateTime.UtcNow);
                await Repo<SchoolMembership>().UpdateManyAsync([member], autoSave: true);
            }
            var school = await Repo<School>().GetAsync(f.SchoolId);
            school.AdvanceConfigurationRevision();
            await Repo<School>().UpdateManyAsync([school], autoSave: true);
        });
        await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetAsync(task.Id));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetOperationAsync(task.Id, published.Id));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.PublishAsync(task.Id, publish));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetAsync(selectedDraft.Id));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetOperationAsync(selectedDraft.Id, selectedDraft.CurrentRevision.Id));
        var list = await Assessments.GetMyListAsync(new());
        list.TotalCount.ShouldBe(0);
        list.Items.ShouldBeEmpty();
        // A full-class draft receipt contains no student identities; personal paper history remains readable.
        (await Assessments.GetOperationAsync(task.Id, task.CurrentRevision.Id))!.Recipients.ShouldBeEmpty();
        (await GetRequiredService<IExamAppService>().GetSavedPaperAsync(input.ExamPaperId)).Id.ShouldBe(input.ExamPaperId);
        await WithUnitOfWorkAsync(async () =>
            (await Repo<EducationAssessmentRecipient>().GetListAsync(x => x.TaskId == task.Id)).Count.ShouldBe(1));
    }

    [Fact]
    public async Task Assessment_Original_Formal_Paper_Should_Remain_Publishable_After_Edit_But_Not_After_Disable()
    {
        var f = await FixtureAsync();
        var student = await AssessmentStudentAsync(f);
        using var teacher = User(f.Reader);
        var input = await AssessmentInputAsync(f);
        var task = await Assessments.CreateDraftAsync(input);
        var originalPaper = await GetRequiredService<IExamAppService>().GetSavedPaperAsync(input.ExamPaperId);
        var sourceQuestion = await GetRequiredService<IQuestionAppService>().GetAsync(originalPaper.Questions.Single().OriginalQuestionId);
        var draft = Draft(sourceQuestion);
        draft.Stem = "教师后来编辑的新题干";
        (await GetRequiredService<IQuestionWorkflowAppService>().SaveDraftAsync(sourceQuestion.Id, draft)).Status.ShouldBe(QuestionStatus.Draft);
        var published = await Assessments.PublishAsync(task.Id, AssessmentPublish(await Assessments.PreviewRecipientsAsync(task.Id)));
        using (User(student.UserId))
        {
            var attempt = await Assessments.StartOrResumeAsync(published.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
            attempt.Paper.Questions.Single().Stem.ShouldBe(originalPaper.Questions.Single().Stem);
            attempt.Paper.Questions.Single().Stem.ShouldNotBe(draft.Stem);
        }
        input.OperationId = Guid.NewGuid();
        var next = await Assessments.CreateDraftAsync(input);
        var preview = await Assessments.PreviewRecipientsAsync(next.Id);
        await GetRequiredService<IQuestionWorkflowAppService>().ChangeAvailabilityAsync(sourceQuestion.Id, new() { Action = QuestionAvailabilityAction.Disable });
        (await Should.ThrowAsync<BusinessException>(() => Assessments.PublishAsync(next.Id, AssessmentPublish(preview))))
            .Code.ShouldBe("KnowPulse:Education:FormalDefinitionRequired");
        (await Assessments.GetAsync(next.Id)).PublishedRevisionId.ShouldBeNull();
    }

    [Fact]
    public async Task Assessment_Shared_Source_Withdrawal_Should_Block_First_Publish_But_Keep_Own_Operation_History()
    {
        var f = await FixtureAsync();
        await AssessmentStudentAsync(f);
        var source = await SourceAsync(f.Owner, "学校共享教材资料");
        Guid bindingId;
        Guid shareId;
        using (User(f.Owner))
        {
            bindingId = await BindAsync(source, f.MappingA);
            shareId = (await _sharing.ShareVersionAsync(Share(f, source))).Id;
        }
        using var teacher = User(f.Reader);
        var questionInput = Manual(f);
        questionInput.Sources = [new()
        {
            KnowledgeItemId = source.KnowledgeItemId, KnowledgeVersionId = source.KnowledgeVersionId, ChunkId = source.Id,
            SchoolKnowledgeShareId = shareId, SourceExcerpt = "本次使用的实际依据",
            CurriculumBindingIds = [bindingId], TextbookKnowledgePointMappingIds = [f.MappingA]
        }];
        var questions = GetRequiredService<IQuestionAppService>();
        var question = await questions.CreateAsync(questionInput);
        question = await questions.ConfirmCurriculumAsync(question.Id, Confirm(f, question));
        question = await questions.SubmitReviewAsync(question.Id);
        question = await questions.ApproveAsync(question.Id, Review(question));
        var paper = await GetRequiredService<IExamAppService>().SavePaperAsync(Paper(f, question.Id));
        var input = AssessmentInput(f, paper.Id);
        var task = await Assessments.CreateDraftAsync(input);
        var preview = await Assessments.PreviewRecipientsAsync(task.Id);
        using (User(f.Owner)) await _sharing.RevokeAsync(shareId);
        await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.PublishAsync(task.Id, AssessmentPublish(preview)));
        (await Assessments.GetOperationAsync(task.Id, input.OperationId))!.ExamPaperId.ShouldBe(paper.Id);
        (await Assessments.GetAsync(task.Id)).PublishedRevisionId.ShouldBeNull();
    }

    [Fact]
    public async Task Assessment_Publish_And_Start_Should_Advance_School_And_Account_Concurrency_Barriers()
    {
        var f = await FixtureAsync();
        var student = await AssessmentStudentAsync(f);
        EducationAssessmentTaskRevisionDto published;
        using (User(f.Reader))
        {
            var task = await Assessments.CreateDraftAsync(await AssessmentInputAsync(f));
            var school = await WithUnitOfWorkAsync(() => Repo<School>().GetAsync(f.SchoolId));
            var account = await WithUnitOfWorkAsync(() => Repo<IdentityUser>().GetAsync(student.UserId));
            published = await Assessments.PublishAsync(task.Id, AssessmentPublish(await Assessments.PreviewRecipientsAsync(task.Id)));
            await Should.ThrowAsync<AbpDbConcurrencyException>(() => WithUnitOfWorkAsync(async () =>
                await Repo<School>().UpdateManyAsync([school], autoSave: true)));
            await Should.ThrowAsync<AbpDbConcurrencyException>(() => WithUnitOfWorkAsync(async () =>
                await Repo<IdentityUser>().UpdateManyAsync([account], autoSave: true)));
        }
        using var participant = User(student.UserId);
        var schoolBeforeStart = await WithUnitOfWorkAsync(() => Repo<School>().GetAsync(f.SchoolId));
        var accountBeforeStart = await WithUnitOfWorkAsync(() => Repo<IdentityUser>().GetAsync(student.UserId));
        var recipientBeforeStart = await WithUnitOfWorkAsync(() => Repo<EducationAssessmentRecipient>().GetAsync(published.Recipients.Single().Id));
        await Assessments.StartOrResumeAsync(recipientBeforeStart.Id, new() { OperationId = Guid.NewGuid() });
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => WithUnitOfWorkAsync(async () =>
            await Repo<School>().UpdateManyAsync([schoolBeforeStart], autoSave: true)));
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => WithUnitOfWorkAsync(async () =>
            await Repo<IdentityUser>().UpdateManyAsync([accountBeforeStart], autoSave: true)));
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => WithUnitOfWorkAsync(async () =>
            await Repo<EducationAssessmentRecipient>().UpdateManyAsync([recipientBeforeStart], autoSave: true)));
    }

    [Fact]
    public async Task Assessment_Before_The_Window_Should_Reject_Start_Without_Consuming_An_Attempt()
    {
        var f = await FixtureAsync();
        var student = await AssessmentStudentAsync(f);
        EducationAssessmentTaskRevisionDto published;
        using (User(f.Reader))
        {
            var input = await AssessmentInputAsync(f);
            input.StartsAtUtc = DateTime.UtcNow.AddHours(1);
            input.EndsAtUtc = input.StartsAtUtc.AddHours(1);
            var task = await Assessments.CreateDraftAsync(input);
            published = await Assessments.PublishAsync(task.Id, AssessmentPublish(await Assessments.PreviewRecipientsAsync(task.Id)));
        }
        using var participant = User(student.UserId);
        var recipientId = published.Recipients.Single().Id;
        (await Assessments.GetMyStudentTaskAsync(recipientId)).CanStart.ShouldBeFalse();
        (await Should.ThrowAsync<BusinessException>(() => Assessments.StartOrResumeAsync(recipientId, new() { OperationId = Guid.NewGuid() })))
            .Code.ShouldBe(EducationAssessmentErrorCodes.OutsideWindow);
        await WithUnitOfWorkAsync(async () =>
        {
            (await Repo<EducationAssessmentRecipient>().GetAsync(recipientId)).StartedAttemptCount.ShouldBe(0);
            (await Repo<EducationAssessmentAttempt>().GetListAsync()).ShouldBeEmpty();
        });
    }

    [Fact]
    public async Task Assessment_Should_Accept_Equivalent_Decimal_Weights_From_A_Confirmed_Formal_Paper()
    {
        var f = await FixtureAsync();
        await AssessmentStudentAsync(f);
        using var teacher = User(f.Reader);
        var questions = GetRequiredService<IQuestionAppService>();
        var question = await questions.CreateAsync(Manual(f));
        var confirmation = Confirm(f, question);
        confirmation.Score = 10.00m;
        confirmation.PointWeights[0].Weight = 0.60m;
        confirmation.PointWeights[0].Score = 6.00m;
        confirmation.PointWeights[1].Weight = 0.40m;
        confirmation.PointWeights[1].Score = 4.00m;
        question = await questions.ConfirmCurriculumAsync(question.Id, confirmation);
        question = await questions.SubmitReviewAsync(question.Id);
        question = await questions.ApproveAsync(question.Id, Review(question));
        var paper = await GetRequiredService<IExamAppService>().SavePaperAsync(Paper(f, question.Id));
        var frozen = await WithUnitOfWorkAsync(() => Repo<ExamPaperQuestion>().GetAsync(paper.Questions.Single().Id));
        frozen.CurriculumPointWeightsJson.ShouldNotBeNull();
        frozen.CurriculumPointWeightsJson.ShouldContain("0.60");
        var task = await Assessments.CreateDraftAsync(AssessmentInput(f, paper.Id));
        var revision = await Assessments.PublishAsync(task.Id, AssessmentPublish(await Assessments.PreviewRecipientsAsync(task.Id)));
        revision.ExamPaperId.ShouldBe(paper.Id);
        revision.Recipients.Count.ShouldBe(1);
        (await GetRequiredService<IExamAppService>().GetSavedPaperAsync(paper.Id)).Questions.Single().CurriculumPointWeights
            .Select(x => (x.Weight, x.Score)).ShouldBe(new[] { (0.6m, 6m), (0.4m, 4m) });
    }

    [Fact]
    public async Task Assessment_Should_Not_Apply_A_Mathematics_Paper_To_Another_Real_Subject_Selection()
    {
        var f = await FixtureAsync();
        using var teacher = User(f.Reader);
        var input = await AssessmentInputAsync(f);
        var otherScope = await WithUnitOfWorkAsync(async () =>
        {
            var oldSelection = await Repo<TeacherTextbookSelection>().GetAsync(f.ReaderSelection);
            var oldAssignment = await Repo<TeachingAssignment>().GetAsync(oldSelection.TeachingAssignmentId);
            var term = await Repo<AcademicTerm>().GetAsync(oldAssignment.AcademicTermId);
            var now = DateTime.UtcNow;
            var subject = new EducationSubject(Guid.NewGuid(), null, $"PHY{Guid.NewGuid():N}", "物理");
            await Repo<EducationSubject>().InsertAsync(subject, autoSave: true);
            var book = new TextbookVersion(Guid.NewGuid(), null, f.SchoolId, subject.Id, "PHYSICS", 1, 7, "物理教材", "测试出版社", "测试版", "上册");
            await Repo<TextbookVersion>().InsertAsync(book, autoSave: true);
            var chapter = new TextbookChapter(Guid.NewGuid(), null, book.Id, null, "CH1", "第一章", 1);
            var point = new CourseKnowledgePoint(Guid.NewGuid(), null, f.SchoolId, subject.Id, "KP1");
            var pointRevision = new CourseKnowledgePointRevision(Guid.NewGuid(), null, point.Id, point.AdvanceRevision(), "同名知识点", null);
            var mapping = new TextbookKnowledgePointMapping(Guid.NewGuid(), null, book.Id, chapter.Id, point.Id, pointRevision.Id, f.Reader, now);
            await Repo<TextbookChapter>().InsertAsync(chapter, autoSave: true);
            await Repo<CourseKnowledgePoint>().InsertAsync(point, autoSave: true);
            await Repo<CourseKnowledgePointRevision>().InsertAsync(pointRevision, autoSave: true);
            await Repo<TextbookKnowledgePointMapping>().InsertAsync(mapping, autoSave: true);
            book.Publish(now);
            await Repo<TextbookVersion>().UpdateManyAsync([book], autoSave: true);
            var assignment = new TeachingAssignment(Guid.NewGuid(), null, f.SchoolId, term.Id, oldAssignment.SchoolClassId,
                subject.Id, f.Reader, term.StartsAtUtc, term.EndsAtUtc);
            await Repo<TeachingAssignment>().InsertAsync(assignment, autoSave: true);
            var selection = new TeacherTextbookSelection(Guid.NewGuid(), null, f.SchoolId, f.Reader, assignment.Id, book.Id, now);
            await Repo<TeacherTextbookSelection>().InsertAsync(selection, autoSave: true);
            return new EducationCurriculumScopeDto { TeacherTextbookSelectionId = selection.Id, TextbookKnowledgePointMappingIds = [mapping.Id] };
        });
        input.Scope = otherScope;
        (await Should.ThrowAsync<BusinessException>(() => Assessments.CreateDraftAsync(input))).Code.ShouldBe("KnowPulse:Education:FormalDefinitionRequired");
        await WithUnitOfWorkAsync(async () => (await Repo<EducationAssessmentTask>().GetListAsync()).ShouldBeEmpty());
    }

    private async Task<CreateEducationAssessmentDraftDto> AssessmentInputAsync(Fixture f, bool fill = false)
    {
        var questions = GetRequiredService<IQuestionAppService>();
        var input = Manual(f);
        if (fill)
        {
            input.Type = QuestionType.FillBlank;
            input.Stem = "甲【1】，乙【2】";
            input.Options = [];
            input.Answers = [];
            input.Analysis = "PRIVATE-ANALYSIS-SENTINEL";
            input.ScoringDefinition = new()
            {
                ReferenceAnswer = "PRIVATE-REFERENCE-SENTINEL",
                Blanks = [new() { Id = Guid.NewGuid(), DisplayOrder = 1, StemMarker = "【1】", Score = 6,
                    TextRule = QuestionBlankTextRule.Exact, AllowedAnswers = ["PRIVATE-ANSWER-SENTINEL"], TextbookKnowledgePointMappingId = f.MappingA },
                    new() { Id = Guid.NewGuid(), DisplayOrder = 2, StemMarker = "【2】", Score = 4,
                        TextRule = QuestionBlankTextRule.Manual, AllowedAnswers = ["PRIVATE-ANSWER-SENTINEL"], TextbookKnowledgePointMappingId = f.MappingB }]
            };
        }
        var question = await questions.CreateAsync(input);
        question = await questions.ConfirmCurriculumAsync(question.Id, Confirm(f, question));
        question = await questions.SubmitReviewAsync(question.Id);
        question = await questions.ApproveAsync(question.Id, Review(question));
        var paper = await GetRequiredService<IExamAppService>().SavePaperAsync(Paper(f, question.Id));
        return AssessmentInput(f, paper.Id);
    }
    private static CreateEducationAssessmentDraftDto AssessmentInput(Fixture f, Guid paperId) => new()
    {
        OperationId = Guid.NewGuid(), Title = "本班正式测评", Instructions = "请阅读题面；当前仅支持开始与恢复。",
        Scope = Both(f), ExamPaperId = paperId, StartsAtUtc = DateTime.UtcNow.AddMinutes(-1), EndsAtUtc = DateTime.UtcNow.AddHours(1),
        DurationMinutes = 10, MaxAttempts = 2, ResultVisibility = ExamResultVisibility.FullReview
    };
    private static SaveEducationAssessmentDraftDto AssessmentSave(EducationAssessmentTaskDto task)
    {
        var revision = task.CurrentRevision;
        return new SaveEducationAssessmentDraftDto
        {
            OperationId = Guid.NewGuid(), ConcurrencyStamp = task.ConcurrencyStamp, ExpectedDraftRevisionId = revision.Id,
            Title = revision.Title, Instructions = revision.Instructions, ExamPaperId = revision.ExamPaperId,
            Scope = new() { TeacherTextbookSelectionId = revision.Curriculum.TeacherTextbookSelectionId,
                TextbookKnowledgePointMappingIds = revision.Curriculum.Mappings.Select(x => x.MappingId).ToList() },
            RecipientScope = revision.RecipientScope, StudentUserIds = revision.StudentUserIds,
            StartsAtUtc = revision.StartsAtUtc, EndsAtUtc = revision.EndsAtUtc, DurationMinutes = revision.DurationMinutes,
            MaxAttempts = revision.MaxAttempts, ResultVisibility = revision.ResultVisibility, AllowWrongQuestionCollection = revision.AllowWrongQuestionCollection
        };
    }
    private static PublishEducationAssessmentDto AssessmentPublish(EducationAssessmentRosterPreviewDto preview) => new()
    {
        OperationId = Guid.NewGuid(), ConcurrencyStamp = preview.ConcurrencyStamp, ExpectedDraftRevisionId = preview.DraftRevisionId,
        RosterFingerprint = preview.RosterFingerprint
    };
    private Task<AssessmentStudent> AssessmentStudentAsync(Fixture f, Guid? classId = null) => WithUnitOfWorkAsync(async () =>
    {
        var selection = await Repo<TeacherTextbookSelection>().GetAsync(f.ReaderSelection);
        var assignment = await Repo<TeachingAssignment>().GetAsync(selection.TeachingAssignmentId);
        var term = await Repo<AcademicTerm>().GetAsync(assignment.AcademicTermId);
        var userId = Guid.NewGuid();
        await Repo<IdentityUser>().InsertAsync(new IdentityUser(userId, $"student-{userId:N}", $"{userId:N}@example.invalid"), autoSave: true);
        var member = new SchoolMembership(Guid.NewGuid(), null, f.SchoolId, userId, SchoolMemberRole.Student, term.StartsAtUtc, term.EndsAtUtc);
        var enrollment = new StudentEnrollment(Guid.NewGuid(), null, f.SchoolId, classId ?? assignment.SchoolClassId, userId, term.StartsAtUtc, term.EndsAtUtc);
        await Repo<SchoolMembership>().InsertAsync(member, autoSave: true);
        await Repo<StudentEnrollment>().InsertAsync(enrollment, autoSave: true);
        return new AssessmentStudent(userId, member.Id, enrollment.Id);
    });
    private sealed record AssessmentStudent(Guid UserId, Guid MembershipId, Guid EnrollmentId);
}
