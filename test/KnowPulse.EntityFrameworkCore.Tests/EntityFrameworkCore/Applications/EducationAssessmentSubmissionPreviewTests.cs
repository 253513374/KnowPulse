using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Blazor.Client.Pages.Education;
using KnowPulse.Education;
using KnowPulse.QuestionBank;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.BlobStoring;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private async Task<EducationAssessmentSubmissionDto> SubmitReviewedAsync(Guid attemptId, SubmitEducationAssessmentDto input)
    {
        input.ExpectedPreparationFingerprint ??= (await Assessments.PreviewSubmissionAsync(attemptId)).Fingerprint;
        return await Assessments.SubmitAsync(attemptId, input);
    }

    [Fact]
    public async Task Submission_Preview_Should_Show_Empty_And_Partly_Answered_Blanks_Without_Sealing_Or_Revealing_Keys()
    {
        var f = await ResponseFixtureAsync(); using var actor = User(f.Student.UserId);
        var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
        var empty = await Assessments.PreviewSubmissionAsync(attempt.Id);
        empty.CanSubmit.ShouldBeTrue(); empty.Questions.Single().IsUnanswered.ShouldBeTrue();
        empty.Questions.Single().UnansweredBlankCount.ShouldBe(attempt.Paper.Questions.Single().ResponseFields.Count);
        empty.Fingerprint.ShouldBe((await Assessments.PreviewSubmissionAsync(attempt.Id)).Fingerprint);
        var saved = await Assessments.SaveResponseAsync(attempt.Id, ResponseSave(f, 0, new() { Type = QuestionType.FillBlank,
            Blanks = [new() { BlankId = attempt.Paper.Questions.Single().ResponseFields[0].Id, Text = "保留原文" }] }));
        var partial = await Assessments.PreviewSubmissionAsync(attempt.Id);
        partial.ResponseSetVersion.ShouldBe(1); partial.Fingerprint.ShouldNotBe(empty.Fingerprint);
        partial.Questions.Single().IsUnanswered.ShouldBeFalse(); partial.Questions.Single().UnansweredBlankCount.ShouldBe(1);
        partial.Questions.Single().SavedResponseRevisionId.ShouldBe(saved.Id);
        (await Assessments.GetSubmissionAsync(attempt.Id)).ShouldBeNull();
        var serialized = JsonSerializer.Serialize(partial);
        foreach (var secret in new[] { "保留原文", "PRIVATE-", "ScoringDefinition", "AllowedAnswers", "ResponseJson" }) serialized.ShouldNotContain(secret);
    }

    [Fact]
    public async Task Submission_Preview_Should_Detect_Upload_Changes_Without_A_Response_Save_And_Exclude_Replaced_Images()
    {
        var f = await ResponseFixtureAsync(true); using var actor = User(f.Student.UserId);
        var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
        var a = await Assessments.UploadAnswerImageAsync(attempt.Id, ImageInput(f), Png());
        var saved = await Assessments.SaveResponseAsync(attempt.Id, ResponseSave(f, 0, new() { Type = QuestionType.Subjective, Text = "原始文字", Images = [new() { AttachmentId = a.Id }] }));
        var before = await Assessments.PreviewSubmissionAsync(attempt.Id); before.UnlinkedImages.ShouldBeEmpty();
        before.Questions.Single().SavedImageCount.ShouldBe(1); before.Questions.Single().IsUnanswered.ShouldBeFalse();
        var b = await Assessments.UploadAnswerImageAsync(attempt.Id, ImageInput(f), Png());
        var changed = await Assessments.PreviewSubmissionAsync(attempt.Id);
        changed.ResponseSetVersion.ShouldBe(before.ResponseSetVersion); changed.Fingerprint.ShouldNotBe(before.Fingerprint);
        changed.UnlinkedImages.Single().Id.ShouldBe(b.Id); changed.UnlinkedImages.Single().IsUploaded.ShouldBeTrue();
        var stale = Submit(attempt, saved.ResultResponseSetVersion); stale.ExpectedPreparationFingerprint = before.Fingerprint; stale.ConfirmSavedOnly = true;
        (await Should.ThrowAsync<BusinessException>(() => Assessments.SubmitAsync(attempt.Id, stale))).Code.ShouldBe(EducationAssessmentErrorCodes.PreparationChanged);
        (await Assessments.GetSubmissionAsync(attempt.Id)).ShouldBeNull();
        var input = Submit(attempt, saved.ResultResponseSetVersion); input.ExpectedPreparationFingerprint = changed.Fingerprint;
        (await Should.ThrowAsync<BusinessException>(() => Assessments.SubmitAsync(attempt.Id, input))).Code.ShouldBe(EducationAssessmentErrorCodes.IncompleteSubmission);
        await Assessments.SaveResponseAsync(attempt.Id, ResponseSave(f, 1, new() { Type = QuestionType.Subjective, Images = [new() { AttachmentId = b.Id }] }));
        var replaced = await Assessments.PreviewSubmissionAsync(attempt.Id); replaced.UnlinkedImages.Single().Id.ShouldBe(a.Id);
        replaced.Questions.Single().SavedImageCount.ShouldBe(1); replaced.Questions.Single().IsUnanswered.ShouldBeFalse();
        (await Assessments.GetResponseRevisionAsync(attempt.Id, saved.Id))!.Response.Images.Single().AttachmentId.ShouldBe(a.Id);
        var command = Submit(attempt, 2); command.ExpectedPreparationFingerprint = replaced.Fingerprint; command.ConfirmSavedOnly = true;
        var receipt = await Assessments.SubmitAsync(attempt.Id, command);
        (await Assessments.SubmitAsync(attempt.Id, command)).Id.ShouldBe(receipt.Id);
        var closed = await Assessments.PreviewSubmissionAsync(attempt.Id); closed.CanSubmit.ShouldBeFalse(); closed.SubmissionId.ShouldBe(receipt.Id);
    }

    [Fact]
    public async Task Submission_Preview_Should_List_Reserved_Unconfirmed_Images_And_Invalidate_After_Confirmation()
    {
        var f = await ResponseFixtureAsync(true); using var actor = User(f.Student.UserId);
        var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
        var before = await Assessments.PreviewSubmissionAsync(attempt.Id);
        var blobs = Substitute.For<IBlobContainer<EducationAssessmentImageBlobContainer>>();
        blobs.SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), false, Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("Injected upload failure")));
        var operation = ImageInput(f);
        await Should.ThrowAsync<BusinessException>(() => ResponseImageService(blobs).UploadAnswerImageAsync(attempt.Id, operation, Png()));
        var pending = await Assessments.PreviewSubmissionAsync(attempt.Id);
        pending.ResponseSetVersion.ShouldBe(0); pending.Fingerprint.ShouldNotBe(before.Fingerprint);
        pending.UnlinkedImages.Single().Id.ShouldBe(operation.OperationId); pending.UnlinkedImages.Single().IsUploaded.ShouldBeFalse();
        await Assessments.UploadAnswerImageAsync(attempt.Id, operation, Png());
        var confirmed = await Assessments.PreviewSubmissionAsync(attempt.Id);
        confirmed.ResponseSetVersion.ShouldBe(0); confirmed.Fingerprint.ShouldNotBe(pending.Fingerprint); confirmed.UnlinkedImages.Single().IsUploaded.ShouldBeTrue();
    }

    [Fact]
    public async Task New_Manual_Submit_Should_Require_Preparation_But_Recover_An_Existing_Legacy_Command_Exactly()
    {
        var f = await ResponseFixtureAsync(); using var actor = User(f.Student.UserId);
        var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
        var input = Submit(attempt, 0); input.ConfirmSavedOnly = true;
        (await Should.ThrowAsync<BusinessException>(() => Assessments.SubmitAsync(attempt.Id, input))).Code.ShouldBe(EducationAssessmentErrorCodes.PreparationRequired);
        var attemptId = attempt.Id;
        var legacyFingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { attemptId, input.PaperSnapshotVersion, input.ExpectedResponseSetVersion, input.ConfirmSavedOnly }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var legacy = await WithUnitOfWorkAsync(() => GetRequiredService<EducationAssessmentSubmissionProcessor>().SealAsync(attempt.Id, input.OperationId, legacyFingerprint, 0, true));
        (await Assessments.SubmitAsync(attempt.Id, input)).Id.ShouldBe(legacy.Id);
        input.ExpectedPreparationFingerprint = (await Assessments.PreviewSubmissionAsync(attempt.Id)).Fingerprint;
        (await Should.ThrowAsync<BusinessException>(() => Assessments.SubmitAsync(attempt.Id, input))).Code.ShouldBe(EducationAssessmentErrorCodes.ConflictingOperation);
    }

    [Fact]
    public async Task Submission_Review_Should_Require_Explicit_Local_Exclusions_Keep_Drafts_And_Reset_Confirmation_On_Invalidation()
    {
        var f = await ResponseFixtureAsync(true); using var actor = User(f.Student.UserId);
        var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
        await Assessments.SaveResponseAsync(attempt.Id, ResponseSave(f, 0, new() { Type = QuestionType.Subjective, Text = "服务器原稿" }));
        attempt = await Assessments.GetAttemptAsync(attempt.Id); var question = attempt.Paper.Questions.Single();
        var local = new EducationAssessmentLocalDrafts(); local.Edit(question, attempt).Text = "本机未同步改动";
        var review = new EducationAssessmentSubmissionReview(); review.Prepare(await Assessments.PreviewSubmissionAsync(attempt.Id), attempt);
        review.HasExclusions(0).ShouldBeFalse(); review.HasExclusions(local.Values.Count).ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => review.Submit(Guid.NewGuid(), local.Values.Count));
        review.ConfirmSavedOnly = true; var input = review.Submit(Guid.NewGuid(), local.Values.Count);
        input.ExpectedPreparationFingerprint.ShouldBe(review.Preview!.Fingerprint);
        var receipt = await Assessments.SubmitAsync(attempt.Id, input);
        var saved = await Assessments.GetResponseRevisionAsync(attempt.Id, receipt.Manifest.Single().ResponseRevisionId!.Value);
        saved!.Response.Text.ShouldBe("服务器原稿"); local.Values[question.Id].Text.ShouldBe("本机未同步改动");
        review.Invalidate(); review.Preview.ShouldBeNull(); review.ConfirmSavedOnly.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => review.Submit(Guid.NewGuid(), 0));
    }

    [Fact]
    public async Task Submission_Review_Should_Reject_A_Checklist_For_A_Different_Server_Response_Set()
    {
        var f = await ResponseFixtureAsync(true); using var actor = User(f.Student.UserId);
        var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
        await Assessments.SaveResponseAsync(attempt.Id, ResponseSave(f, 0, new() { Type = QuestionType.Subjective, Text = "另一设备" }));
        var preview = await Assessments.PreviewSubmissionAsync(attempt.Id); var review = new EducationAssessmentSubmissionReview();
        Should.Throw<InvalidOperationException>(() => review.Prepare(preview, attempt)); review.Preview.ShouldBeNull();
        review.Prepare(preview, await Assessments.GetAttemptAsync(attempt.Id)); review.Preview.ShouldNotBeNull();
        var input = review.Submit(Guid.NewGuid(), 0); input.ConfirmSavedOnly.ShouldBeFalse();
        (await Assessments.SubmitAsync(attempt.Id, input)).ConfirmedSavedOnly.ShouldBeFalse();
    }

    [Fact]
    public async Task Submission_Preview_Crossing_The_Deadline_Should_Not_Require_A_New_Confirmation_To_Seal()
    {
        var f = await ExpiredResponseFixtureAsync(expiresInMilliseconds: 3000); using var actor = User(f.Source.Student.UserId);
        var expires = (await Assessments.GetAttemptAsync(f.AttemptId)).ExpiresAtUtc;
        var readCount = 0;
        var reads = CurrentResponseReads(async () => { readCount++; DateTime.UtcNow.ShouldBeLessThan(expires); var wait = expires.AddMilliseconds(50) - DateTime.UtcNow; if (wait > TimeSpan.Zero) await Task.Delay(wait); });
        var service = ResponseImageService(GetRequiredService<IBlobContainer<EducationAssessmentImageBlobContainer>>(), reads);
        var receipt = await service.SubmitAsync(f.AttemptId, new() { OperationId = Guid.NewGuid(), PaperSnapshotVersion = f.Source.PaperSnapshotVersion, ExpectedResponseSetVersion = 0 });
        receipt.Cause.ShouldBe(EducationAssessmentSubmissionCause.Deadline); receipt.ConfirmedSavedOnly.ShouldBeFalse();
        readCount.ShouldBe(1);
        receipt.Manifest.Single().ResponseRevisionId.ShouldBeNull();
        (await Assessments.GetSubmissionAsync(f.AttemptId))!.Id.ShouldBe(receipt.Id);
    }

    [Fact]
    public async Task Submission_Preview_Should_Require_The_Current_Own_Student_And_School_Relation()
    {
        var f = await ResponseFixtureAsync(); Guid id;
        using (User(f.Student.UserId)) id = (await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() })).Id;
        using (User(f.School.Reader)) await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.PreviewSubmissionAsync(id));
        using (User(Guid.NewGuid())) await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.PreviewSubmissionAsync(id));
        var otherStudent = await AssessmentStudentAsync(f.School);
        using (User(otherStudent.UserId)) await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.PreviewSubmissionAsync(id));
        using (User(f.Student.UserId)) {
            using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid())) await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.PreviewSubmissionAsync(id));
            await WithUnitOfWorkAsync(async () => { var member = await Repo<SchoolMembership>().GetAsync(f.Student.MembershipId); member.EndAt(DateTime.UtcNow); await Repo<SchoolMembership>().UpdateManyAsync([member], autoSave: true); });
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.PreviewSubmissionAsync(id));
        }
    }
}
