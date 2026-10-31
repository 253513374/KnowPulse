using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Data;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationCorrectionAppService Corrections => GetRequiredService<IEducationCorrectionAppService>();
    [Fact]
    public async Task Native_Correction_Should_Filter_School_And_Check_It_Before_Providing_The_Question()
    {
        var f = await CorrectionFixtureAsync();
        var school = await WithUnitOfWorkAsync(async () => (await Repo<EducationCorrectionTask>().GetAsync(f.TaskId)).SchoolId);
        var exposures = await WithUnitOfWorkAsync(async () => await Repo<EducationQuestionExposure>().GetCountAsync());
        using var student = User(f.Source.Source.Student.UserId);
        var list = await Corrections.GetMyCorrectionListAsync(new(), school);
        list.Items.Single().SchoolId.ShouldBe(school); list.Items.Single().OriginalQuestion.ShouldBeNull(); list.Items.Single().Instructions.ShouldBeNull();
        (await Corrections.GetMyCorrectionListAsync(new(), Guid.NewGuid())).TotalCount.ShouldBe(0);
        await Should.ThrowAsync<AbpAuthorizationException>(() => Corrections.GetMyCorrectionForSchoolAsync(f.DeliveryId, Guid.NewGuid()));
        await WithUnitOfWorkAsync(async () => {
            (await Repo<EducationCorrectionDelivery>().GetAsync(f.DeliveryId)).FirstProvidedAtUtc.ShouldBeNull();
            (await Repo<EducationQuestionExposure>().GetCountAsync()).ShouldBe(exposures);
        });
        (await Corrections.GetMyCorrectionForSchoolAsync(f.DeliveryId, school)).OriginalQuestion.ShouldNotBeNull();
        (await Corrections.GetMyCorrectionAsync(f.DeliveryId)).SchoolId.ShouldBe(school);
        (await Corrections.GetMyCorrectionListAsync(new())).TotalCount.ShouldBe(1);
    }
    [Fact]
    public async Task Correction_Should_Reject_Invalid_And_Changed_Image_Bytes_Before_Sharing()
    {
        var f = await CorrectionFixtureAsync(); using var student = User(f.Source.Source.Student.UserId); await Corrections.GetMyCorrectionAsync(f.DeliveryId);
        using var invalid = new RemoteStreamContent(new MemoryStream([1, 2, 3]), "fake.png", "image/png");
        await Should.ThrowAsync<BusinessException>(() => Corrections.UploadMyCorrectionImageAsync(f.DeliveryId, Guid.NewGuid(), invalid));
        var image = await Corrections.UploadMyCorrectionImageAsync(f.DeliveryId, Guid.NewGuid(), Png());
        var blobName = await WithUnitOfWorkAsync(async () => (await Repo<EducationCorrectionImage>().GetAsync(image.Id)).BlobName);
        using var changed = new MemoryStream([1, 2, 3]); await GetRequiredService<IBlobContainer<EducationCorrectionImageBlobContainer>>().SaveAsync(blobName, changed, overrideExisting: true);
        var content = CorrectionContent() with { Images = [new(image.Id)] };
        await Should.ThrowAsync<BusinessException>(() => CorrectionSubmitInputAsync(f.DeliveryId, content: content));
        await Should.ThrowAsync<BusinessException>(() => Corrections.DownloadMyCorrectionImageAsync(f.DeliveryId, image.Id));
        await WithUnitOfWorkAsync(async () => (await Repo<EducationCorrectionSubmission>().GetCountAsync()).ShouldBe(0));
    }
    [Fact]
    public async Task Correction_Should_Record_Only_The_Actually_Provided_Frozen_Question_In_A_Multi_Question_Paper()
    {
        var source = await ResponseFixtureAsync(true);
        using (User(source.School.Reader)) {
            var original = await WithUnitOfWorkAsync(async () => (await Repo<ExamPaperQuestion>().GetAsync(source.QuestionId)).OriginalQuestionId);
            var other = await ApprovedManualAsync(source.School); var input = Paper(source.School, original); input.FixedQuestionIds.Add(other.Id);
            var paper = await GetRequiredService<IExamAppService>().SavePaperAsync(input); source.Revision.ExamPaperId = paper.Id;
            source = source with { QuestionId = paper.Questions.Single(x => x.OriginalQuestionId == original).Id, PaperSnapshotVersion = paper.SnapshotVersion };
        }
        var f = await GradingFixtureAsync(true, sourceOverride: source); Guid task = Guid.NewGuid(); Guid delivery;
        using (User(source.School.Reader)) {
            var grading = await Grades.GetSubmissionForGradingAsync(f.SubmissionId); var q = grading.Questions.Single(x => x.Id == source.QuestionId);
            var draft = await Grades.SaveGradeDraftAsync(f.SubmissionId, new() { OperationId = Guid.NewGuid(), PaperQuestionId = q.Id, ExpectedGradeRevisionId = q.Grade.CurrentRevisionId, ConcurrencyStamp = q.Grade.ConcurrencyStamp,
                PartMarks = q.Grade.Draft!.Content.Parts.Select(x => new EducationAssessmentPartMark { PartId = x.PartId, ObtainedScore = x.MaxScore - 1 }).ToList() });
            var next = await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
            await Grades.ConfirmGradeAsync(q.Grade.Id, new() { OperationId = Guid.NewGuid(), ExpectedDraftRevisionId = draft.Id, ConcurrencyStamp = next.Questions.Single(x => x.Id == q.Id).Grade.ConcurrencyStamp, ExpectedGradeSetVersion = next.ConfirmedGradeSetVersion });
            await ExposureReleaseAsync(f, ExamResultVisibility.FullReview);
            await Corrections.SaveCorrectionDraftAsync(task, new() { OperationId = Guid.NewGuid(), QuestionGradeId = q.Grade.Id, Content = CorrectionInstructions() });
            await PublishCorrectionAsync(task); delivery = (await Corrections.GetCorrectionProgressAsync(task, null)).Delivery.Id;
        }
        using (User(source.Student.UserId)) {
            await Corrections.GetMyCorrectionListAsync(new());
            await WithUnitOfWorkAsync(async () => (await Repo<EducationQuestionExposure>().GetCountAsync()).ShouldBe(0));
            var result = await Corrections.GetMyCorrectionAsync(delivery); result.OriginalQuestion!.Id.ShouldBe(source.QuestionId);
            await CorrectionSubmitInputAsync(delivery);
            await WithUnitOfWorkAsync(async () => { var exposures = await Repo<EducationQuestionExposure>().GetListAsync(); exposures.Count.ShouldBe(1); exposures.Single().PaperQuestionId.ShouldBe(source.QuestionId); });
            (await Grades.GetMyResultAsync(f.RecipientId)).Questions.Count.ShouldBe(2);
            await WithUnitOfWorkAsync(async () => (await Repo<EducationQuestionExposure>().GetCountAsync()).ShouldBe(2));
        }
    }
    [Fact]
    public async Task Correction_Should_Require_A_New_Task_Publication_After_Original_Grade_Changes()
    {
        var f = await CorrectionFixtureAsync(); EducationCorrectionSubmissionDto submitted;
        using (User(f.Source.Source.Student.UserId)) { await Corrections.GetMyCorrectionAsync(f.DeliveryId); submitted = await Corrections.SubmitMyCorrectionAsync(f.DeliveryId, await CorrectionSubmitInputAsync(f.DeliveryId)); }
        using (User(f.Source.Source.School.Reader)) {
            await ConfirmManualGradeAsync(f.Source.SubmissionId, 1, "重新核定原始评分"); var overview = await Grades.GetGradingOverviewAsync(f.Source.TaskId);
            var release = ReleaseInput(overview, overview.ClosureId!.Value, ExamResultVisibility.FullReview); release.Reason = "发布修订后的原始评分"; await Grades.PublishResultsAsync(f.Source.TaskId, release);
            var progress = await Corrections.GetCorrectionProgressAsync(f.TaskId, null);
            await Should.ThrowAsync<BusinessException>(() => Corrections.PublishCorrectionReviewAsync(f.DeliveryId, CorrectionReviewInput(progress)));
        }
        using (User(f.Source.Source.Student.UserId)) {
            var body = await Corrections.GetMyCorrectionAsync(f.DeliveryId); body.Availability.ShouldBe(EducationCorrectionAvailability.SourceChanged); body.OriginalQuestion.ShouldBeNull(); body.CurrentSubmission!.Id.ShouldBe(submitted.Id);
            await Should.ThrowAsync<BusinessException>(() => CorrectionSubmitInputAsync(f.DeliveryId, submitted.Id));
        }
        Guid replacement;
        using (User(f.Source.Source.School.Reader)) {
            var root = await Corrections.GetCorrectionAsync(f.TaskId); await Corrections.SaveCorrectionDraftAsync(f.TaskId, new() { OperationId = Guid.NewGuid(), QuestionGradeId = f.GradeId, ExpectedRevisionId = root.Current.Id, ConcurrencyStamp = root.ConcurrencyStamp, Content = CorrectionInstructions() });
            await PublishCorrectionAsync(f.TaskId, "按新评分更新订正"); replacement = (await Corrections.GetCorrectionProgressAsync(f.TaskId, null)).Delivery.Id;
        }
        using (User(f.Source.Source.Student.UserId)) { var fresh = await Corrections.GetMyCorrectionAsync(replacement); fresh.Availability.ShouldBe(EducationCorrectionAvailability.Available); fresh.CurrentSubmission.ShouldBeNull(); fresh.OriginalQuestion!.ObtainedScore.ShouldBe(8); }
    }
    [Theory]
    [InlineData("tenant")]
    [InlineData("ended-enrollment")]
    [InlineData("other-student")]
    public async Task Correction_Should_Recheck_Current_Authority_For_History_And_Original_Operation_Recovery(string denied)
    {
        var f = await CorrectionFixtureAsync(); EducationCorrectionSubmissionDto submitted;
        using (User(f.Source.Source.Student.UserId)) { await Corrections.GetMyCorrectionAsync(f.DeliveryId); submitted = await Corrections.SubmitMyCorrectionAsync(f.DeliveryId, await CorrectionSubmitInputAsync(f.DeliveryId)); }
        if (denied == "ended-enrollment") await WithUnitOfWorkAsync(async () => { var enrollment = await Repo<StudentEnrollment>().GetAsync(f.Source.Source.Student.EnrollmentId); enrollment.EndAt(DateTime.UtcNow.AddSeconds(-1)); await Repo<StudentEnrollment>().UpdateAsync(enrollment, autoSave: true); });
        using var user = User(denied == "other-student" ? f.Source.Absent.UserId : f.Source.Source.Student.UserId);
        using var tenant = GetRequiredService<Volo.Abp.MultiTenancy.ICurrentTenant>().Change(denied == "tenant" ? Guid.NewGuid() : null);
        await Should.ThrowAsync<AbpAuthorizationException>(() => Corrections.GetMyCorrectionAsync(f.DeliveryId));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Corrections.GetMyCorrectionHistoryAsync(f.DeliveryId, submitted.Id, new()));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Corrections.GetMyCorrectionOperationAsync(f.DeliveryId, submitted.Id));
    }
    [Theory]
    [InlineData(EducationPermissions.AssessmentParticipate)]
    [InlineData(EducationPermissions.LearningParticipate)]
    [InlineData(EducationPermissions.AssessmentManage)]
    [InlineData(EducationPermissions.LearningManage)]
    public async Task Correction_Should_Check_Separate_Function_Permissions_Before_Reading_Any_Source(string denied)
    {
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(Arg.Any<string>()).Returns(true); checker.IsGrantedAsync(denied).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker); var raw = new EducationAssessmentAppService { LazyServiceProvider = lazy };
        if (denied is EducationPermissions.AssessmentParticipate or EducationPermissions.LearningParticipate) await Should.ThrowAsync<AbpAuthorizationException>(() => raw.SubmitMyCorrectionAsync(Guid.NewGuid(), new()));
        else await Should.ThrowAsync<AbpAuthorizationException>(() => raw.SaveCorrectionDraftAsync(Guid.NewGuid(), new()));
    }
    private sealed record CorrectionFixture(GradingFixture Source, Guid TaskId, Guid GradeId, Guid DeliveryId);
    private async Task<CorrectionFixture> CorrectionFixtureAsync()
    {
        var f = await GradingFixtureAsync(false); await ExposureReleaseAsync(f, ExamResultVisibility.FullReview);
        using var teacher = User(f.Source.School.Reader); var grade = await EvidenceGradeAsync(f); var task = Guid.NewGuid();
        await Corrections.SaveCorrectionDraftAsync(task, new() { OperationId = Guid.NewGuid(), QuestionGradeId = grade, Content = CorrectionInstructions() });
        await PublishCorrectionAsync(task);
        return new(f, task, grade, (await Corrections.GetCorrectionProgressAsync(task, null)).Delivery.Id);
    }
    private static EducationCorrectionInstructions CorrectionInstructions() => new() { Title = "错题订正", Instructions = "重新说明解题过程与检查方法", StartsAtUtc = DateTime.UtcNow.AddMinutes(-1), DueAtUtc = DateTime.UtcNow.AddDays(1), EstimatedMinutes = 15 };
    private async Task<EducationCorrectionRevisionDto> PublishCorrectionAsync(Guid task, string? reason = null)
    {
        var preview = await Corrections.PreviewCorrectionPublishAsync(task);
        return await Corrections.PublishCorrectionAsync(task, new() { OperationId = Guid.NewGuid(), ExpectedRevisionId = preview.DraftRevisionId, ConcurrencyStamp = preview.ConcurrencyStamp,
            PreviewFingerprint = preview.Fingerprint, ConfirmRecipientSourceAndSafeText = true, PublicReason = reason });
    }
    private static EducationCorrectionContent CorrectionContent(string answer = "先核对条件，再按步骤计算") => new() { OriginalThinking = "原先遗漏了一个条件", CurrentSolution = answer, CheckingStrategy = "将结果代回原条件验证" };
    private async Task<SubmitEducationCorrectionDto> CorrectionSubmitInputAsync(Guid delivery, Guid? previous = null, EducationCorrectionContent? content = null)
    {
        var value = content ?? CorrectionContent(); var p = await Corrections.PreviewMyCorrectionAsync(delivery, new() { Content = value, ExpectedSubmissionId = previous });
        return new() { OperationId = Guid.NewGuid(), ExpectedSubmissionId = previous, Content = value, PreviewFingerprint = p.Fingerprint, ConfirmSharingWithTeacher = true };
    }
    private static PublishEducationCorrectionReviewDto CorrectionReviewInput(EducationCorrectionProgressDto p) => new() { OperationId = Guid.NewGuid(), ExpectedSubmissionId = p.Delivery.CurrentSubmissionId!.Value,
        ExpectedReviewId = p.ReviewHeadId, ConcurrencyStamp = p.ConcurrencyStamp, Decision = EducationCorrectionReviewDecision.NeedsRevision, StudentFeedback = "请补充验算步骤", ConfirmStudentVisibleFeedback = true };

    [Fact]
    public async Task Correction_Should_Require_Actual_Provision_And_Explicit_Sharing_And_Keep_Submission_Review_History()
    {
        var f = await CorrectionFixtureAsync(); var studentId = f.Source.Source.Student.UserId; EducationCorrectionSubmissionDto first; SubmitEducationCorrectionDto command;
        using (User(studentId)) {
            var list = await Corrections.GetMyCorrectionListAsync(new()); list.Items.Single().OriginalQuestion.ShouldBeNull(); list.Items.Single().Instructions.ShouldBeNull();
            await Should.ThrowAsync<BusinessException>(() => CorrectionSubmitInputAsync(f.DeliveryId));
            var body = await Corrections.GetMyCorrectionAsync(f.DeliveryId); body.OriginalQuestion.ShouldNotBeNull(); body.Instructions.ShouldNotBeNull();
            command = await CorrectionSubmitInputAsync(f.DeliveryId); command.ConfirmSharingWithTeacher = false;
            await Should.ThrowAsync<BusinessException>(() => Corrections.SubmitMyCorrectionAsync(f.DeliveryId, command));
            command.ConfirmSharingWithTeacher = true; first = await Corrections.SubmitMyCorrectionAsync(f.DeliveryId, command);
            (await Corrections.SubmitMyCorrectionAsync(f.DeliveryId, command)).Id.ShouldBe(first.Id);
        }
        PublishEducationCorrectionReviewDto reviewCommand; EducationCorrectionReviewDto review;
        using (User(f.Source.Source.School.Reader)) {
            var p = await Corrections.GetCorrectionProgressAsync(f.TaskId, null); reviewCommand = CorrectionReviewInput(p); review = await Corrections.PublishCorrectionReviewAsync(f.DeliveryId, reviewCommand);
            (await Corrections.GetCorrectionSubmissionsAsync(f.DeliveryId, new())).Items.Single().Submission.Content.CurrentSolution.ShouldBe(command.Content.CurrentSolution);
        }
        using (User(studentId)) {
            (await Corrections.GetMyCorrectionAsync(f.DeliveryId)).CurrentReview!.Id.ShouldBe(review.Id);
            var second = await Corrections.SubmitMyCorrectionAsync(f.DeliveryId, await CorrectionSubmitInputAsync(f.DeliveryId, first.Id, CorrectionContent("补充验算后的订正")));
            second.BasedOnSubmissionId.ShouldBe(first.Id); second.SubmissionNumber.ShouldBe(2);
            (await Corrections.GetMyCorrectionAsync(f.DeliveryId)).CurrentReview.ShouldBeNull();
            var history = await Corrections.GetMyCorrectionHistoryAsync(f.DeliveryId, second.Id, new()); history.TotalCount.ShouldBe(2);
            history.Items.Single(x => x.Submission.Id == first.Id).Reviews.Single().Id.ShouldBe(review.Id);
            (await Corrections.GetMyCorrectionOperationAsync(f.DeliveryId, command.OperationId))!.Id.ShouldBe(first.Id);
        }
        using (User(f.Source.Source.School.Reader)) {
            (await Corrections.PublishCorrectionReviewAsync(f.DeliveryId, reviewCommand)).Id.ShouldBe(review.Id);
            reviewCommand.OperationId = Guid.NewGuid(); await Should.ThrowAsync<AbpDbConcurrencyException>(() => Corrections.PublishCorrectionReviewAsync(f.DeliveryId, reviewCommand));
            (await Grades.GetSubmissionForGradingAsync(f.Source.SubmissionId)).Questions.Single().Grade.Confirmed!.ObtainedScore.ShouldBe(10);
        }
        await WithUnitOfWorkAsync(async () => { (await Repo<EducationCorrectionSubmission>().GetCountAsync()).ShouldBe(2); (await Repo<EducationCorrectionReview>().GetCountAsync()).ShouldBe(1); });
    }

    [Fact]
    public async Task Correction_Preview_Should_Reject_Changed_Content_And_Another_Device_Head()
    {
        var f = await CorrectionFixtureAsync(); using var student = User(f.Source.Source.Student.UserId); await Corrections.GetMyCorrectionAsync(f.DeliveryId);
        var stale = await CorrectionSubmitInputAsync(f.DeliveryId); var edited = JsonSerializer.Deserialize<SubmitEducationCorrectionDto>(JsonSerializer.Serialize(stale))!;
        edited.Content = CorrectionContent("预览后改写的内容"); await Should.ThrowAsync<BusinessException>(() => Corrections.SubmitMyCorrectionAsync(f.DeliveryId, edited));
        var first = await Corrections.SubmitMyCorrectionAsync(f.DeliveryId, stale); stale.OperationId = Guid.NewGuid();
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Corrections.SubmitMyCorrectionAsync(f.DeliveryId, stale));
        (await Corrections.GetMyCorrectionAsync(f.DeliveryId)).CurrentSubmission!.Id.ShouldBe(first.Id);
    }

    [Theory]
    [InlineData(ExamResultVisibility.Hidden)]
    [InlineData(ExamResultVisibility.ScoreOnly)]
    public async Task Correction_Should_Stop_Source_And_Teacher_Feedback_Reads_After_Result_Gate_Closes_But_Keep_Own_Submitted_Copy(ExamResultVisibility visibility)
    {
        var f = await CorrectionFixtureAsync(); SubmitEducationCorrectionDto command; EducationCorrectionSubmissionDto submitted;
        using (User(f.Source.Source.Student.UserId)) { await Corrections.GetMyCorrectionAsync(f.DeliveryId); command = await CorrectionSubmitInputAsync(f.DeliveryId); submitted = await Corrections.SubmitMyCorrectionAsync(f.DeliveryId, command); }
        using (User(f.Source.Source.School.Reader)) {
            var p = await Corrections.GetCorrectionProgressAsync(f.TaskId, null); await Corrections.PublishCorrectionReviewAsync(f.DeliveryId, CorrectionReviewInput(p));
            var overview = await Grades.GetGradingOverviewAsync(f.Source.TaskId); var release = ReleaseInput(overview, overview.ClosureId!.Value, visibility); release.Reason = "调整讲评开放范围";
            await Grades.PublishResultsAsync(f.Source.TaskId, release);
        }
        using (User(f.Source.Source.Student.UserId)) {
            var body = await Corrections.GetMyCorrectionAsync(f.DeliveryId); body.Availability.ShouldBe(EducationCorrectionAvailability.ResultUnavailable);
            body.Title.ShouldBeNull(); body.Instructions.ShouldBeNull(); body.OriginalQuestion.ShouldBeNull(); body.CurrentReview.ShouldBeNull(); JsonSerializer.Serialize(body.CurrentSubmission!.Content).ShouldBe(JsonSerializer.Serialize(submitted.Content));
            var history = await Corrections.GetMyCorrectionHistoryAsync(f.DeliveryId, submitted.Id, new()); history.Items.Single().Reviews.ShouldBeEmpty();
            (await Corrections.SubmitMyCorrectionAsync(f.DeliveryId, command)).Id.ShouldBe(submitted.Id);
            await Should.ThrowAsync<BusinessException>(() => CorrectionSubmitInputAsync(f.DeliveryId, submitted.Id));
        }
    }

    [Fact]
    public async Task Correction_Publication_Replacement_And_Withdrawal_Should_Preserve_Old_Handed_In_Records_And_Not_Expose_Private_Drafts()
    {
        var f = await CorrectionFixtureAsync(); EducationCorrectionSubmissionDto submitted;
        using (User(f.Source.Source.Student.UserId)) { await Corrections.GetMyCorrectionAsync(f.DeliveryId); submitted = await Corrections.SubmitMyCorrectionAsync(f.DeliveryId, await CorrectionSubmitInputAsync(f.DeliveryId)); }
        using (User(f.Source.Source.School.Reader)) {
            var root = await Corrections.GetCorrectionAsync(f.TaskId); await Corrections.SaveCorrectionDraftAsync(f.TaskId, new() { OperationId = Guid.NewGuid(), QuestionGradeId = f.GradeId, ExpectedRevisionId = root.Current.Id,
                ConcurrencyStamp = root.ConcurrencyStamp, Content = CorrectionInstructions() with { Instructions = "PRIVATE-DRAFT-SENTINEL" } });
        }
        using (User(f.Source.Source.Student.UserId)) { var body = await Corrections.GetMyCorrectionAsync(f.DeliveryId); body.Availability.ShouldBe(EducationCorrectionAvailability.Available); body.Instructions!.ShouldNotContain("PRIVATE"); }
        using (User(f.Source.Source.School.Reader)) {
            await PublishCorrectionAsync(f.TaskId, "已更新任务要求"); var root = await Corrections.GetCorrectionAsync(f.TaskId);
            await Corrections.WithdrawCorrectionAsync(f.TaskId, new() { OperationId = Guid.NewGuid(), ExpectedRevisionId = root.Current.Id, ConcurrencyStamp = root.ConcurrencyStamp, PublicReason = "请暂缓订正", ConfirmStudentVisibleWithdrawal = true });
        }
        using (User(f.Source.Source.Student.UserId)) { var body = await Corrections.GetMyCorrectionAsync(f.DeliveryId); body.Availability.ShouldBe(EducationCorrectionAvailability.Withdrawn); body.PublicReason.ShouldBe("请暂缓订正"); body.CurrentSubmission!.Id.ShouldBe(submitted.Id); body.OriginalQuestion.ShouldBeNull(); }
    }

    [Fact]
    public async Task Correction_Images_Should_Be_Private_Until_Explicitly_Included_In_Submitted_Copy_And_Stay_Immutable()
    {
        var f = await CorrectionFixtureAsync(); EducationCorrectionImageDto selected, unused; EducationCorrectionSubmissionDto submitted;
        using (User(f.Source.Source.Student.UserId)) {
            await Corrections.GetMyCorrectionAsync(f.DeliveryId); var op = Guid.NewGuid();
            selected = await Corrections.UploadMyCorrectionImageAsync(f.DeliveryId, op, Png()); unused = await Corrections.UploadMyCorrectionImageAsync(f.DeliveryId, Guid.NewGuid(), Png());
            (await Corrections.UploadMyCorrectionImageAsync(f.DeliveryId, op, Png())).Id.ShouldBe(selected.Id);
            (await Corrections.GetMyCorrectionImageOperationAsync(f.DeliveryId, op))!.IsUploaded.ShouldBeTrue();
            var content = CorrectionContent() with { Images = [new(selected.Id, 90)] };
            submitted = await Corrections.SubmitMyCorrectionAsync(f.DeliveryId, await CorrectionSubmitInputAsync(f.DeliveryId, content: content));
        }
        using (User(f.Source.Source.School.Reader)) {
            using var downloaded = await Corrections.DownloadCorrectionImageAsync(submitted.Id, selected.Id); downloaded.ContentType.ShouldBe("image/png");
            await Should.ThrowAsync<AbpAuthorizationException>(() => Corrections.DownloadCorrectionImageAsync(submitted.Id, unused.Id));
        }
        using (User(f.Source.Absent.UserId)) {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Corrections.GetMyCorrectionAsync(f.DeliveryId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Corrections.DownloadMyCorrectionImageAsync(f.DeliveryId, selected.Id));
        }
        using (User(f.Source.Source.School.Owner)) await Should.ThrowAsync<AbpAuthorizationException>(() => Corrections.GetCorrectionAsync(f.TaskId));
        await WithUnitOfWorkAsync(async () => { var row = await Repo<EducationCorrectionSubmission>().GetAsync(submitted.Id); row.Content().Images.Single().RotationDegrees.ShouldBe(90); });
    }
}
