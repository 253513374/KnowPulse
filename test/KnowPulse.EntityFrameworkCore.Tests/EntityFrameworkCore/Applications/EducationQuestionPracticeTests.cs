using System;
using System.IO;
using System.Linq;
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
using Volo.Abp.Content;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationQuestionPracticeAppService Practices => GetRequiredService<IEducationQuestionPracticeAppService>();
    private static StartEducationPracticeDto PracticeStart(GradingFixture f, EducationAssessmentResultReleaseDto release) => new()
    { OperationId = Guid.NewGuid(), PaperQuestionId = f.Source.QuestionId, ExpectedResultReleaseId = release.Id };
    private static EducationAssessmentResponseValue PracticeBlanks(EducationPracticeDto row) => new() { Type = QuestionType.FillBlank,
        Blanks = row.Question!.ResponseFields.Select(x => new EducationAssessmentBlankResponse { BlankId = x.Id, Text = x.StemMarker == "【1】" ? "正确甲" : "新解法乙" }).ToList() };
    private async Task<SubmitEducationPracticeDto> PracticeSubmitAsync(Guid id, EducationAssessmentResponseValue response) => new()
    { OperationId = Guid.NewGuid(), Response = response, PreparationFingerprint = (await Practices.PreviewMyPracticeAsync(id, new() { Response = response })).Fingerprint,
        ConfirmPracticeOnly = true, ConfirmSharingWithTeacher = true };

    [Theory]
    [InlineData(ExamResultVisibility.Hidden)]
    [InlineData(ExamResultVisibility.ScoreOnly)]
    [InlineData(ExamResultVisibility.FullReview)]
    public async Task Practice_Start_List_And_Operation_Lookup_Should_Not_Provide_Answers_Or_Review_Exposure(ExamResultVisibility visibility)
    {
        var f = await GradingFixtureAsync(false); var release = await ExposureReleaseAsync(f, visibility);
        using var student = NotebookActor(f.Source.Student.UserId); var input = PracticeStart(f, release);
        if (visibility != ExamResultVisibility.FullReview) {
            (await Should.ThrowAsync<BusinessException>(() => Practices.StartMyPracticeAsync(f.RecipientId, input))).Code.ShouldBe(EducationPracticeErrors.SourceUnavailable);
            (await WithUnitOfWorkAsync(() => Repo<EducationQuestionPractice>().GetCountAsync())).ShouldBe(0); return;
        }
        var receipt = await Practices.StartMyPracticeAsync(f.RecipientId, input); receipt.PracticeId.ShouldBe(input.OperationId); receipt.IsSubmission.ShouldBeFalse();
        receipt.RequestFingerprint.ShouldBe(EducationPracticeFingerprint.Start(f.RecipientId, input));
        (await Practices.StartMyPracticeAsync(f.RecipientId, input)).PracticeId.ShouldBe(receipt.PracticeId);
        var schoolStamp = await WithUnitOfWorkAsync(async () => (await Repo<School>().GetAsync(f.Source.School.SchoolId)).ConcurrencyStamp);
        var row = await Practices.GetMyPracticeAsync(receipt.PracticeId); row.Question!.Id.ShouldBe(f.Source.QuestionId); row.Feedback.ShouldBeNull(); row.SubmittedResponse.ShouldBeNull();
        var json = JsonSerializer.Serialize(row); json.ShouldNotContain("正确甲"); json.ShouldNotContain("AllowedAnswers"); json.ShouldNotContain("ScoringDefinition");
        (await Practices.GetMyPracticeListAsync(new())).Items.Single().Id.ShouldBe(row.Id);
        (await Practices.GetMyPracticeOperationAsync(row.Id, input.OperationId))!.RequestFingerprint.ShouldBe(receipt.RequestFingerprint);
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(0);
        (await WithUnitOfWorkAsync(async () => (await Repo<School>().GetAsync(f.Source.School.SchoolId)).ConcurrencyStamp)).ShouldBe(schoolStamp);
        using (NotebookActor(f.Source.School.Reader, "teacher")) {
            (await Practices.GetPracticeSubmissionsAsync(f.SubmissionId, null, new())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Practices.GetPracticeSubmissionAsync(row.Id));
        }
    }

    [Fact]
    public async Task Practice_Should_Seal_Separate_Repeats_And_Show_Automatic_Feedback_Only_After_Explicit_Submission_Without_Changing_Official_Results()
    {
        var f = await GradingFixtureAsync(false); var release = await ExposureReleaseAsync(f, ExamResultVisibility.FullReview);
        using var student = NotebookActor(f.Source.Student.UserId);
        var before = JsonSerializer.Serialize(await Grades.GetMyResultAsync(f.RecipientId));
        var scope = await WithUnitOfWorkAsync(async () => JsonSerializer.Deserialize<EducationCurriculumSnapshot>(
            (await Repo<EducationAssessmentTaskRevision>().GetAsync((await Repo<EducationAssessmentRecipient>().GetAsync(f.RecipientId)).TaskRevisionId)).CurriculumSnapshotJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        var windowStart = DateTime.UtcNow.AddDays(-1); EducationLearningReportSource evidenceBefore;
        var sourceReader = GetRequiredService<EducationLearningReportSourceReader>();
        using (NotebookActor(f.Source.School.Reader, "teacher")) evidenceBefore = await WithUnitOfWorkAsync(() => sourceReader.CaptureAsync(scope, windowStart, DateTime.UtcNow, [f.TaskId]));
        var responseCount = await WithUnitOfWorkAsync(() => Repo<EducationAssessmentResponseRevision>().GetCountAsync());
        var gradeCount = await WithUnitOfWorkAsync(() => Repo<EducationAssessmentGradeRevision>().GetCountAsync());
        for (var round = 0; round < 2; round++) {
            var start = await Practices.StartMyPracticeAsync(f.RecipientId, PracticeStart(f, release)); var row = await Practices.GetMyPracticeAsync(start.PracticeId);
            var input = await PracticeSubmitAsync(row.Id, PracticeBlanks(row)); input.ConfirmSharingWithTeacher = false;
            await Should.ThrowAsync<BusinessException>(() => Practices.SubmitMyPracticeAsync(row.Id, input));
            (await Practices.GetMyPracticeAsync(row.Id)).SubmittedAtUtc.ShouldBeNull();
            input.ConfirmSharingWithTeacher = true; var receipt = await Practices.SubmitMyPracticeAsync(row.Id, input);
            receipt.IsSubmission.ShouldBeTrue(); receipt.RequestFingerprint.ShouldBe(EducationPracticeFingerprint.Submit(row.Id, input));
            (await Practices.SubmitMyPracticeAsync(row.Id, input)).OperationId.ShouldBe(receipt.OperationId);
            (await Practices.GetMyPracticeOperationAsync(row.Id, receipt.OperationId))!.IsSubmission.ShouldBeTrue();
            var submitted = await Practices.GetMyPracticeAsync(row.Id); submitted.SubmittedResponse!.Blanks.Last().Text.ShouldBe("新解法乙");
            submitted.Feedback!.Evaluation.ObtainedScore.ShouldBe(6); submitted.Feedback.Evaluation.Parts.Count.ShouldBe(2);
            submitted.Feedback.ScoringDefinition!.Blanks.All(x => !x.TextbookKnowledgePointMappingId.HasValue).ShouldBeTrue();
            JsonSerializer.Serialize(submitted.Feedback).ShouldNotContain(f.Source.School.MappingA.ToString());
            JsonSerializer.Serialize(submitted.Feedback).ShouldNotContain(f.Source.School.MappingB.ToString());
            var copy = JsonSerializer.Deserialize<SubmitEducationPracticeDto>(JsonSerializer.Serialize(input))!; copy.Response.Blanks[0].Text = "改写";
            (await Should.ThrowAsync<BusinessException>(() => Practices.SubmitMyPracticeAsync(row.Id, copy))).Code.ShouldBe(EducationPracticeErrors.Conflict);
            using (NotebookActor(f.Source.School.Reader, "teacher")) {
                (await Practices.GetPracticeSubmissionAsync(row.Id)).SubmittedResponse!.Blanks.Last().Text.ShouldBe("新解法乙");
                (await Practices.GetPracticeSubmissionsAsync(f.SubmissionId, f.Source.QuestionId, new())).TotalCount.ShouldBe(round + 1);
            }
        }
        (await Practices.GetMyPracticeListAsync(new())).TotalCount.ShouldBe(2);
        (await WithUnitOfWorkAsync(() => Repo<EducationAssessmentResponseRevision>().GetCountAsync())).ShouldBe(responseCount);
        (await WithUnitOfWorkAsync(() => Repo<EducationAssessmentGradeRevision>().GetCountAsync())).ShouldBe(gradeCount);
        (await WithUnitOfWorkAsync(() => Repo<EducationAssessmentAttempt>().GetCountAsync())).ShouldBe(1);
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(1);
        JsonSerializer.Serialize(await Grades.GetMyResultAsync(f.RecipientId)).ShouldBe(before);
        using (NotebookActor(f.Source.School.Reader, "teacher")) {
            var evidenceAfter = await WithUnitOfWorkAsync(() => sourceReader.CaptureAsync(scope, windowStart, DateTime.UtcNow, [f.TaskId]));
            JsonSerializer.Serialize(evidenceAfter.Students).ShouldBe(JsonSerializer.Serialize(evidenceBefore.Students));
        }
    }

    [Fact]
    public async Task Practice_Should_Close_New_Execution_And_Hide_Feedback_When_The_Exact_Release_Changes_But_Retain_The_Submitted_Copy()
    {
        var f = await GradingFixtureAsync(false); var release = await ExposureReleaseAsync(f, ExamResultVisibility.FullReview);
        using var student = NotebookActor(f.Source.Student.UserId); var startInput = PracticeStart(f, release);
        var start = await Practices.StartMyPracticeAsync(f.RecipientId, startInput); var row = await Practices.GetMyPracticeAsync(start.PracticeId);
        var input = await PracticeSubmitAsync(row.Id, PracticeBlanks(row)); var saved = await Practices.SubmitMyPracticeAsync(row.Id, input);
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(0);
        using (NotebookActor(f.Source.School.Reader, "teacher")) {
            (await Practices.GetPracticeSubmissionAsync(row.Id)).Feedback.ShouldNotBeNull();
            (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(0);
            var close = ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), release.ClosureId, ExamResultVisibility.Hidden);
            close.Reason = "关闭讲评以验证后续复练读取"; await Grades.PublishResultsAsync(f.TaskId, close);
        }
        var hidden = await Practices.GetMyPracticeAsync(row.Id); hidden.SourceAvailable.ShouldBeFalse(); hidden.Question.ShouldBeNull(); hidden.Feedback.ShouldBeNull();
        hidden.SubmittedResponse!.Blanks.Last().Text.ShouldBe("新解法乙");
        (await Practices.SubmitMyPracticeAsync(row.Id, input)).OperationId.ShouldBe(saved.OperationId);
        (await Practices.StartMyPracticeAsync(f.RecipientId, startInput)).PracticeId.ShouldBe(row.Id);
        await Should.ThrowAsync<BusinessException>(() => Practices.StartMyPracticeAsync(f.RecipientId, PracticeStart(f, release)));
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Practice_Should_Protect_Private_Image_Reservations_And_Share_Only_Selected_Originals_After_Submission_With_No_Invented_Subjective_Score()
    {
        var f = await GradingFixtureAsync(true);
        using (NotebookActor(f.Source.School.Reader, "teacher")) await ConfirmManualGradeAsync(f.SubmissionId, 1, null);
        var release = await ExposureReleaseAsync(f, ExamResultVisibility.FullReview);
        using var student = NotebookActor(f.Source.Student.UserId);
        var start = await Practices.StartMyPracticeAsync(f.RecipientId, PracticeStart(f, release));
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6l6sAAAAASUVORK5CYII=");
        async Task<EducationPracticeImageDto> Upload(Guid operation) => await Practices.UploadMyPracticeImageAsync(start.PracticeId, operation,
            new RemoteStreamContent(new MemoryStream(bytes), "自己的草图.png", "image/png"));
        var image = await Upload(Guid.NewGuid()); var unused = await Upload(Guid.NewGuid()); (await Upload(image.Id)).ContentHash.ShouldBe(image.ContentHash);
        (await Practices.GetMyPracticeImageOperationAsync(start.PracticeId, image.Id))!.IsUploaded.ShouldBeTrue();
        using (NotebookActor(f.Source.School.Reader, "teacher")) await Should.ThrowAsync<AbpAuthorizationException>(() => Practices.DownloadPracticeImageAsync(start.PracticeId, image.Id));
        var input = await PracticeSubmitAsync(start.PracticeId, new() { Type = QuestionType.Subjective, Text = "独立复练的新解法",
            Images = [new() { AttachmentId = image.Id, RotationDegrees = 270 }] });
        await Practices.SubmitMyPracticeAsync(start.PracticeId, input);
        using (NotebookActor(f.Source.School.Reader, "teacher")) {
            var copy = await Practices.GetPracticeSubmissionAsync(start.PracticeId); copy.OwnImages.Single().Id.ShouldBe(image.Id);
            copy.Feedback!.Evaluation.ObtainedScore.ShouldBeNull(); copy.Feedback.Evaluation.Parts.All(x => !x.ObtainedScore.HasValue).ShouldBeTrue();
            await Should.ThrowAsync<AbpAuthorizationException>(() => Practices.DownloadPracticeImageAsync(start.PracticeId, unused.Id));
            using var download = await Practices.DownloadPracticeImageAsync(start.PracticeId, image.Id); using var buffer = new MemoryStream(); await download.GetStream().CopyToAsync(buffer); buffer.ToArray().ShouldBe(bytes);
        }
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(0);
        var viewed = await Practices.GetMyPracticeAsync(start.PracticeId); viewed.SubmittedResponse!.Images.Single().RotationDegrees.ShouldBe(270);
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Practice_Should_Deny_Foreign_Accounts_Tenants_And_Ended_Student_Relations_Even_For_Recovery()
    {
        var f = await GradingFixtureAsync(false); var release = await ExposureReleaseAsync(f, ExamResultVisibility.FullReview); EducationPracticeOperationDto start;
        using (NotebookActor(f.Source.Student.UserId)) start = await Practices.StartMyPracticeAsync(f.RecipientId, PracticeStart(f, release));
        foreach (var actor in new[] { (f.Source.School.Owner, "admin"), (f.Absent.UserId, "student"), (f.Source.School.Reader, "teacher") }) {
            using var other = NotebookActor(actor.Item1, actor.Item2);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Practices.GetMyPracticeAsync(start.PracticeId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Practices.GetMyPracticeOperationAsync(start.PracticeId, start.OperationId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Practices.GetPracticeSubmissionAsync(start.PracticeId));
        }
        using (NotebookActor(f.Source.Student.UserId)) using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid())) await Should.ThrowAsync<AbpAuthorizationException>(() => Practices.GetMyPracticeAsync(start.PracticeId));
        await WithUnitOfWorkAsync(async () => { var member = await Repo<SchoolMembership>().GetAsync(f.Source.Student.MembershipId); member.EndAt(DateTime.UtcNow.AddSeconds(-1)); await Repo<SchoolMembership>().UpdateAsync(member, autoSave: true); });
        using (NotebookActor(f.Source.Student.UserId)) {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Practices.GetMyPracticeOperationAsync(start.PracticeId, start.OperationId));
            (await Practices.GetMyPracticeListAsync(new())).Items.ShouldBeEmpty();
        }
    }

    [Theory]
    [InlineData(EducationPermissions.AssessmentParticipate)]
    [InlineData(EducationPermissions.LearningParticipate)]
    [InlineData(EducationPermissions.AssessmentManage)]
    [InlineData(EducationPermissions.LearningManage)]
    public async Task Practice_Should_Explicitly_Enforce_Both_Function_Permissions(string denied)
    {
        var f = await GradingFixtureAsync(false); using var actor = NotebookActor(f.Source.Student.UserId);
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(Arg.Any<string>()).Returns(true); checker.IsGrantedAsync(denied).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>()); lazy.LazyGetRequiredService<IUnitOfWorkManager>().Returns(GetRequiredService<IUnitOfWorkManager>());
        lazy.LazyGetRequiredService<EducationAssessmentAccessGuard>().Returns(GetRequiredService<EducationAssessmentAccessGuard>());
        var service = new EducationAssessmentAppService { LazyServiceProvider = lazy };
        if (denied.EndsWith("Participate", StringComparison.Ordinal)) await Should.ThrowAsync<AbpAuthorizationException>(() => service.StartMyPracticeAsync(f.RecipientId, new()));
        else await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetPracticeSubmissionsAsync(f.SubmissionId, null, new()));
    }
}
