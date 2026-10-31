using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private static readonly byte[] AnswerPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a/e0AAAAASUVORK5CYII=");

    [Fact]
    public async Task Assessment_Response_Save_Should_Preserve_Blank_Identity_Original_Text_And_Immutable_Replay()
    {
        var f = await ResponseFixtureAsync();
        using var student = User(f.Student.UserId);
        var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
        var fields = attempt.Paper.Questions.Single().ResponseFields;
        var first = ResponseSave(f, 0, new()
        {
            Type = QuestionType.FillBlank,
            Blanks = [new() { BlankId = fields[1].Id, Text = "  同一原文 \n" }, new() { BlankId = fields[0].Id, Text = "  同一原文 \n" }]
        });
        var a = await Assessments.SaveResponseAsync(attempt.Id, first);
        a.ResultResponseSetVersion.ShouldBe(1);
        a.Response.Blanks.Select(x => x.BlankId).ShouldBe(new[] { fields[1].Id, fields[0].Id });
        a.Response.Blanks.Select(x => x.Text).ShouldBe(new[] { "  同一原文 \n", "  同一原文 \n" });
        var next = ResponseSave(f, 1, new() { Type = QuestionType.FillBlank, Blanks = [new() { BlankId = fields[0].Id, Text = "后来修改" }] });
        var b = await Assessments.SaveResponseAsync(attempt.Id, next);
        b.ResultResponseSetVersion.ShouldBe(2);
        (await Assessments.SaveResponseAsync(attempt.Id, first)).Id.ShouldBe(a.Id);
        var receipt = await Assessments.GetResponseRevisionAsync(attempt.Id, first.OperationId);
        receipt!.Response.Blanks[0].Text.ShouldBe("  同一原文 \n");
        var current = await Assessments.GetAttemptAsync(attempt.Id);
        current.ResponseSetVersion.ShouldBe(2);
        current.Responses.Single().Id.ShouldBe(b.Id);
        current.Responses.Single().Response.Blanks.Single().Text.ShouldBe("后来修改");
        var stale = ResponseSave(f, 0, first.Response);
        (await Should.ThrowAsync<BusinessException>(() => Assessments.SaveResponseAsync(attempt.Id, stale))).Code
            .ShouldBe(EducationAssessmentErrorCodes.ResponseVersionConflict);
        first.Response.Blanks[0].Text = "同操作号不同事实";
        (await Should.ThrowAsync<BusinessException>(() => Assessments.SaveResponseAsync(attempt.Id, first))).Code
            .ShouldBe(EducationAssessmentErrorCodes.ConflictingOperation);
        await WithUnitOfWorkAsync(async () =>
        {
            (await Repo<EducationAssessmentResponseRevision>().GetListAsync(x => x.AttemptId == attempt.Id)).Count.ShouldBe(2);
            (await Repo<EducationAssessmentCurrentResponse>().GetListAsync(x => x.AttemptId == attempt.Id)).Count.ShouldBe(1);
            (await Repo<ExamAnswer>().GetListAsync()).ShouldBeEmpty();
        });
    }

    [Fact]
    public async Task Assessment_Manual_Submit_Should_Require_Seen_Response_Version_And_Freeze_One_Safe_Manifest()
    {
        var f = await ResponseFixtureAsync();
        using var student = User(f.Student.UserId);
        var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
        var incomplete = Submit(attempt, 0);
        (await Should.ThrowAsync<BusinessException>(() => SubmitReviewedAsync(attempt.Id, incomplete))).Code
            .ShouldBe(EducationAssessmentErrorCodes.IncompleteSubmission);
        (await Assessments.GetSubmissionAsync(attempt.Id)).ShouldBeNull();
        var response = ResponseSave(f, 0, new()
        {
            Type = QuestionType.FillBlank,
            Blanks = attempt.Paper.Questions.Single().ResponseFields.Select(x => new EducationAssessmentBlankResponse { BlankId = x.Id, Text = "原始答案" }).ToList()
        });
        var saved = await Assessments.SaveResponseAsync(attempt.Id, response);
        (await Should.ThrowAsync<BusinessException>(() => SubmitReviewedAsync(attempt.Id, Submit(attempt, 0)))).Code
            .ShouldBe(EducationAssessmentErrorCodes.ResponseVersionConflict);
        var submit = Submit(attempt, 1);
        var receipt = await SubmitReviewedAsync(attempt.Id, submit);
        receipt.Cause.ShouldBe(EducationAssessmentSubmissionCause.Manual);
        receipt.Manifest.Single().ResponseRevisionId.ShouldBe(saved.Id);
        receipt.Manifest.Single().ResponseHash.ShouldBe(saved.ResponseHash);
        (await SubmitReviewedAsync(attempt.Id, submit)).Id.ShouldBe(receipt.Id);
        var closed = await Assessments.GetAttemptAsync(attempt.Id);
        closed.Status.ShouldBe(EducationAssessmentAttemptStatus.Submitted);
        closed.SubmissionId.ShouldBe(receipt.Id);
        (await Assessments.GetMyStudentTaskAsync(f.Revision.Recipients.Single().Id)).CanStart.ShouldBeFalse();
        // A previously committed save remains recoverable; new late writes cannot change the submission.
        (await Assessments.SaveResponseAsync(attempt.Id, response)).Id.ShouldBe(saved.Id);
        (await Should.ThrowAsync<BusinessException>(() => Assessments.SaveResponseAsync(attempt.Id, ResponseSave(f, 1, response.Response)))).Code
            .ShouldBe(EducationAssessmentErrorCodes.AttemptClosed);
        var serialized = JsonSerializer.Serialize(closed);
        foreach (var secret in new[] { "PRIVATE-ANSWER-SENTINEL", "PRIVATE-REFERENCE-SENTINEL", "PRIVATE-ANALYSIS-SENTINEL", "ScoringDefinition", "AllowedAnswers" })
            serialized.ShouldNotContain(secret);
        await WithUnitOfWorkAsync(async () =>
            (await Repo<EducationAssessmentSubmission>().GetListAsync(x => x.AttemptId == attempt.Id)).Count.ShouldBe(1));
    }

    [Fact]
    public async Task Assessment_Explicit_Saved_Only_Submit_Should_Record_Unanswered_Questions_Without_Inventing_Answers()
    {
        var f = await ResponseFixtureAsync();
        using var student = User(f.Student.UserId);
        var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
        var input = Submit(attempt, 0);
        input.ConfirmSavedOnly = true;
        var receipt = await SubmitReviewedAsync(attempt.Id, input);
        receipt.ConfirmedSavedOnly.ShouldBeTrue();
        receipt.ResponseSetVersion.ShouldBe(0);
        receipt.Manifest.Single().ResponseRevisionId.ShouldBeNull();
        receipt.Manifest.Single().ResponseHash.ShouldBeNull();
        input.ConfirmSavedOnly = false;
        (await Should.ThrowAsync<BusinessException>(() => SubmitReviewedAsync(attempt.Id, input))).Code
            .ShouldBe(EducationAssessmentErrorCodes.ConflictingOperation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Assessment_Partly_Answered_Blanks_Should_Require_Saved_Only_Confirmation(bool whitespace)
    {
        var f = await ResponseFixtureAsync();
        using var student = User(f.Student.UserId);
        var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
        var blanks = attempt.Paper.Questions.Single().ResponseFields;
        var value = new EducationAssessmentResponseValue { Type = QuestionType.FillBlank,
            Blanks = [new() { BlankId = blanks[0].Id, Text = "已答的第一空" }] };
        if (whitespace) value.Blanks.Add(new() { BlankId = blanks[1].Id, Text = " \n\t " });
        var saved = await Assessments.SaveResponseAsync(attempt.Id, ResponseSave(f, 0, value));
        (await Should.ThrowAsync<BusinessException>(() => SubmitReviewedAsync(attempt.Id, Submit(attempt, 1)))).Code
            .ShouldBe(EducationAssessmentErrorCodes.IncompleteSubmission);
        (await Assessments.GetSubmissionAsync(attempt.Id)).ShouldBeNull();
        var input = Submit(attempt, 1);
        input.ConfirmSavedOnly = true;
        var receipt = await SubmitReviewedAsync(attempt.Id, input);
        receipt.ConfirmedSavedOnly.ShouldBeTrue();
        receipt.Manifest.Single().ResponseRevisionId.ShouldBe(saved.Id);
        (await Assessments.GetResponseRevisionAsync(attempt.Id, saved.Id))!.Response.Blanks.Select(x => (x.BlankId, x.Text))
            .ShouldBe(value.Blanks.Select(x => (x.BlankId, x.Text)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Assessment_Attempt_Recovery_Should_Reread_A_Save_Or_Submission_Committed_Between_Reads(bool seal)
    {
        var f = await ResponseFixtureAsync();
        using var student = User(f.Student.UserId);
        var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
        EducationAssessmentResponseRevisionDto? saved = null;
        EducationAssessmentSubmissionDto? submitted = null;
        var once = false;
        var reads = CurrentResponseReads(async () =>
        {
            if (once) return;
            once = true;
            if (seal)
            {
                var input = Submit(attempt, 0);
                input.ConfirmSavedOnly = true;
                submitted = await SubmitReviewedAsync(attempt.Id, input);
            }
            else saved = await Assessments.SaveResponseAsync(attempt.Id, ResponseSave(f, 0, new()
            { Type = QuestionType.FillBlank, Blanks = [new() { BlankId = attempt.Paper.Questions.Single().ResponseFields[0].Id, Text = "另一设备已保存" }] }));
        });
        var service = ResponseImageService(GetRequiredService<IBlobContainer<EducationAssessmentImageBlobContainer>>(), reads);
        var recovered = await WithUnitOfWorkAsync(() => service.GetAttemptAsync(attempt.Id));
        once.ShouldBeTrue();
        if (seal)
        {
            recovered.Status.ShouldBe(EducationAssessmentAttemptStatus.Submitted);
            recovered.SubmissionId.ShouldBe(submitted!.Id);
            recovered.ResponseSetVersion.ShouldBe(0);
            recovered.Responses.ShouldBeEmpty();
        }
        else
        {
            recovered.ResponseSetVersion.ShouldBe(1);
            recovered.LastSavedAtUtc.ShouldBe(saved!.SavedAtUtc);
            recovered.Responses.Single().Id.ShouldBe(saved.Id);
            recovered.Responses.Single().ResultResponseSetVersion.ShouldBe(recovered.ResponseSetVersion);
        }
        await reads.Received(2).GetListAsync(Arg.Any<Expression<Func<EducationAssessmentCurrentResponse, bool>>>(),
            Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Assessment_Manual_Submission_Waiting_Across_Cutoff_Should_Record_The_Real_Deadline_Time()
    {
        var f = await ExpiredResponseFixtureAsync(expiresInMilliseconds: 3000);
        var original = await WithUnitOfWorkAsync(() => Repo<EducationAssessmentAttempt>().GetAsync(f.AttemptId));
        var waited = false;
        var reads = CurrentResponseReads(async () =>
        {
            DateTime.UtcNow.ShouldBeLessThan(original.ExpiresAtUtc);
            waited = true;
            var wait = original.ExpiresAtUtc - DateTime.UtcNow + TimeSpan.FromMilliseconds(25);
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
        });
        var processor = SubmissionProcessor(reads);
        var service = ResponseImageService(GetRequiredService<IBlobContainer<EducationAssessmentImageBlobContainer>>(), processor: processor);
        using var student = User(f.Source.Student.UserId);
        var input = new SubmitEducationAssessmentDto { OperationId = Guid.NewGuid(), PaperSnapshotVersion = f.Source.PaperSnapshotVersion,
            ExpectedResponseSetVersion = 0, ConfirmSavedOnly = true };
        input.ExpectedPreparationFingerprint = (await Assessments.PreviewSubmissionAsync(f.AttemptId)).Fingerprint;
        var receipt = await service.SubmitAsync(f.AttemptId, input);
        waited.ShouldBeTrue();
        receipt.Cause.ShouldBe(EducationAssessmentSubmissionCause.Deadline);
        receipt.Id.ShouldNotBe(input.OperationId);
        receipt.ConfirmedSavedOnly.ShouldBeFalse();
        receipt.EffectiveAtUtc.ShouldBe(original.ExpiresAtUtc);
        receipt.SealedAtUtc.ShouldBeGreaterThanOrEqualTo(original.ExpiresAtUtc);
        (await Assessments.SubmitAsync(f.AttemptId, input)).Id.ShouldBe(receipt.Id);
        (await GetRequiredService<EducationAssessmentSubmissionProcessor>().SealDueAsync(f.AttemptId))!.Id.ShouldBe(receipt.Id);
        await WithUnitOfWorkAsync(async () =>
            (await Repo<EducationAssessmentSubmission>().GetListAsync(x => x.AttemptId == f.AttemptId)).Count.ShouldBe(1));
    }

    [Fact]
    public async Task Assessment_Images_Should_Store_Actual_Bytes_Separately_And_Preserve_Response_Order_And_Rotation()
    {
        var f = await ResponseFixtureAsync(subjective: true);
        using var student = User(f.Student.UserId);
        var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
        var firstInput = ImageInput(f);
        var imageA = await Assessments.UploadAnswerImageAsync(attempt.Id, firstInput, Png());
        var imageB = await Assessments.UploadAnswerImageAsync(attempt.Id, ImageInput(f), Png());
        imageA.IsUploaded.ShouldBeTrue();
        imageA.CurrentResponseRevisionId.ShouldBeNull();
        (await Assessments.GetAttemptAsync(attempt.Id)).ResponseSetVersion.ShouldBe(0);
        (await Assessments.UploadAnswerImageAsync(attempt.Id, firstInput, Png())).UploadedAtUtc.ShouldBe(imageA.UploadedAtUtc);
        var response = ResponseSave(f, 0, new()
        {
            Type = QuestionType.Subjective, Text = "  原始步骤\n保持空白  ",
            Images = [new() { AttachmentId = imageB.Id, RotationDegrees = 90 }, new() { AttachmentId = imageA.Id, RotationDegrees = 270 }]
        });
        var saved = await Assessments.SaveResponseAsync(attempt.Id, response);
        saved.Response.Text.ShouldBe(response.Response.Text);
        saved.Response.Images.Select(x => (x.AttachmentId, x.RotationDegrees)).ShouldBe(new[] { (imageB.Id, 90), (imageA.Id, 270) });
        (await Assessments.GetAnswerImageAsync(attempt.Id, imageA.Id))!.CurrentResponseRevisionId.ShouldBe(saved.Id);
        using var download = await Assessments.DownloadAnswerImageAsync(attempt.Id, imageA.Id);
        using var copied = new MemoryStream();
        await download.GetStream().CopyToAsync(copied);
        copied.ToArray().ShouldBe(AnswerPng);
        (await SubmitReviewedAsync(attempt.Id, Submit(attempt, 1))).Manifest.Single().ResponseRevisionId.ShouldBe(saved.Id);
        (await Assessments.GetAnswerImageAsync(attempt.Id, imageA.Id))!.IsUploaded.ShouldBeTrue();
        JsonSerializer.Serialize(await Assessments.GetAttemptAsync(attempt.Id)).ShouldNotContain("PRIVATE-SCORING-SENTINEL");
    }

    [Theory]
    [InlineData("mime")]
    [InlineData("hash")]
    [InlineData("bytes")]
    public async Task Assessment_Image_Validation_Should_Reject_A_False_Type_Hash_Or_Body_Before_Reservation(string change)
    {
        var f = await ResponseFixtureAsync(subjective: true);
        using var student = User(f.Student.UserId);
        var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
        var input = ImageInput(f);
        if (change == "hash") input.ContentHash = new string('a', 64);
        var bytes = change == "bytes" ? new byte[] { 1, 2, 3, 4 } : AnswerPng;
        using var file = new RemoteStreamContent(new MemoryStream(bytes), "answer.png", change == "mime" ? "image/jpeg" : "image/png", bytes.LongLength);
        await Should.ThrowAsync<BusinessException>(() => Assessments.UploadAnswerImageAsync(attempt.Id, input, file));
        (await Assessments.GetAnswerImageAsync(attempt.Id, input.OperationId)).ShouldBeNull();
        await WithUnitOfWorkAsync(async () =>
            (await Repo<EducationAssessmentAnswerAttachment>().GetListAsync(x => x.AttemptId == attempt.Id)).ShouldBeEmpty());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Assessment_Server_Cutoff_Should_Seal_Without_A_Login_Or_Current_Student_Relation(bool endedRelation)
    {
        var f = await ExpiredResponseFixtureAsync();
        var original = await WithUnitOfWorkAsync(() => Repo<EducationAssessmentAttempt>().GetAsync(f.AttemptId));
        using (User(f.Source.Student.UserId))
        {
            (await Assessments.GetAttemptAsync(f.AttemptId)).SubmissionId.ShouldBeNull();
            (await Should.ThrowAsync<BusinessException>(() => Assessments.SaveResponseAsync(f.AttemptId,
                ResponseSave(f.Source, 0, new() { Type = QuestionType.FillBlank })))).Code.ShouldBe(EducationAssessmentErrorCodes.AttemptExpired);
        }
        if (endedRelation) await WithUnitOfWorkAsync(async () =>
        {
            var member = await Repo<SchoolMembership>().GetAsync(f.Source.Student.MembershipId);
            member.EndAt(DateTime.UtcNow);
            await Repo<SchoolMembership>().UpdateManyAsync([member], autoSave: true);
        });
        EducationAssessmentSubmission receipt;
        // This principal has no actual account, teacher identity or student membership.
        using (User(Guid.NewGuid())) receipt = (await GetRequiredService<EducationAssessmentSubmissionProcessor>().SealDueAsync(f.AttemptId))!;
        receipt.ShouldNotBeNull();
        receipt.Cause.ShouldBe(EducationAssessmentSubmissionCause.Deadline);
        receipt.EffectiveAtUtc.ShouldBe(original.ExpiresAtUtc);
        (await GetRequiredService<EducationAssessmentSubmissionProcessor>().SealDueAsync(f.AttemptId))!.Id.ShouldBe(receipt.Id);
        using (User(f.Source.Student.UserId))
        {
            if (endedRelation)
            {
                await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetSubmissionAsync(f.AttemptId));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetAttemptAsync(f.AttemptId));
            }
            else
            {
                var lateManual = await Assessments.SubmitAsync(f.AttemptId, new() { OperationId = Guid.NewGuid(),
                    PaperSnapshotVersion = f.Source.PaperSnapshotVersion, ExpectedResponseSetVersion = 0 });
                lateManual.Id.ShouldBe(receipt.Id);
                lateManual.Cause.ShouldBe(EducationAssessmentSubmissionCause.Deadline);
                (await Assessments.GetSubmissionAsync(f.AttemptId))!.Manifest.Single().ResponseRevisionId.ShouldBeNull();
            }
        }
        await WithUnitOfWorkAsync(async () =>
        {
            (await Repo<EducationAssessmentSubmission>().GetListAsync(x => x.AttemptId == f.AttemptId)).Count.ShouldBe(1);
            (await Repo<EducationAssessmentResponseRevision>().GetListAsync(x => x.AttemptId == f.AttemptId)).ShouldBeEmpty();
        });
    }

    [Fact]
    public async Task Assessment_Answer_And_Image_Identifiers_Should_Not_Grant_Other_Users_Attempts_Questions_Or_Tenants()
    {
        var f = await ResponseFixtureAsync(subjective: true);
        EducationStudentAssessmentAttemptDto first;
        EducationAssessmentAnswerImageDto image;
        using (User(f.Student.UserId))
        {
            first = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
            image = await Assessments.UploadAnswerImageAsync(first.Id, ImageInput(f), Png());
        }
        EducationAssessmentTaskRevisionDto nextTask;
        using (User(f.School.Reader))
        {
            var task = await Assessments.CreateDraftAsync(AssessmentInput(f.School, f.Revision.ExamPaperId));
            nextTask = await Assessments.PublishAsync(task.Id, AssessmentPublish(await Assessments.PreviewRecipientsAsync(task.Id)));
        }
        using (User(f.Student.UserId))
        {
            var second = await Assessments.StartOrResumeAsync(nextTask.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
            var wrongImage = ResponseSave(f, 0, new() { Type = QuestionType.Subjective, Images = [new() { AttachmentId = image.Id }] });
            (await Should.ThrowAsync<BusinessException>(() => Assessments.SaveResponseAsync(second.Id, wrongImage))).Code
                .ShouldBe(EducationAssessmentErrorCodes.InvalidResponse);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetAnswerImageAsync(second.Id, image.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.DownloadAnswerImageAsync(second.Id, image.Id));
            var wrongQuestion = ImageInput(f);
            wrongQuestion.PaperQuestionId = Guid.NewGuid();
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.UploadAnswerImageAsync(first.Id, wrongQuestion, Png()));
            using (GetRequiredService<Volo.Abp.MultiTenancy.ICurrentTenant>().Change(Guid.NewGuid()))
                await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetAttemptAsync(first.Id));
        }
        using (User(f.School.Owner))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetAttemptAsync(first.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetSubmissionAsync(first.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetAnswerImageAsync(first.Id, image.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.DownloadAnswerImageAsync(first.Id, image.Id));
        }
        await WithUnitOfWorkAsync(async () => (await Repo<EducationAssessmentResponseRevision>().GetListAsync()).ShouldBeEmpty());
    }

    [Fact]
    public async Task Assessment_Image_Failure_Should_Preserve_Pending_Operation_And_An_Unknown_Successful_Blob_Write()
    {
        var f = await ResponseFixtureAsync(subjective: true);
        using var student = User(f.Student.UserId);
        var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
        var actual = GetRequiredService<IBlobContainer<EducationAssessmentImageBlobContainer>>();
        var blobs = Substitute.For<IBlobContainer<EducationAssessmentImageBlobContainer>>();
        blobs.GetOrNullAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            actual.GetOrNullAsync(call.ArgAt<string>(0), call.ArgAt<CancellationToken>(1)));
        var failBeforeWrite = true;
        blobs.SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), false, Arg.Any<CancellationToken>()).Returns(async call =>
        {
            if (failBeforeWrite) throw new IOException("Injected storage failure before writing");
            await actual.SaveAsync(call.ArgAt<string>(0), call.ArgAt<Stream>(1), false, call.ArgAt<CancellationToken>(3));
            throw new IOException("Injected loss of the successful storage acknowledgement");
        });
        var service = ResponseImageService(blobs);
        var input = ImageInput(f);
        (await Should.ThrowAsync<BusinessException>(() => service.UploadAnswerImageAsync(attempt.Id, input, Png()))).Code
            .ShouldBe("KnowPulse:EducationAssessment:ImageStorageFailed");
        var pending = await Assessments.GetAnswerImageAsync(attempt.Id, input.OperationId);
        pending!.IsUploaded.ShouldBeFalse();
        pending.UploadedAtUtc.ShouldBeNull();
        var unconfirmed = ResponseSave(f, 0, new() { Type = QuestionType.Subjective, Images = [new() { AttachmentId = input.OperationId }] });
        (await Should.ThrowAsync<BusinessException>(() => Assessments.SaveResponseAsync(attempt.Id, unconfirmed))).Code
            .ShouldBe(EducationAssessmentErrorCodes.InvalidResponse);
        failBeforeWrite = false;
        var completed = await service.UploadAnswerImageAsync(attempt.Id, input, Png());
        completed.IsUploaded.ShouldBeTrue();
        completed.Id.ShouldBe(pending.Id);
        completed.RequestedAtUtc.ShouldBe(pending.RequestedAtUtc);
        using var download = await Assessments.DownloadAnswerImageAsync(attempt.Id, completed.Id);
        using var copied = new MemoryStream();
        await download.GetStream().CopyToAsync(copied);
        copied.ToArray().ShouldBe(AnswerPng);
        await blobs.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await WithUnitOfWorkAsync(async () =>
            (await Repo<EducationAssessmentAnswerAttachment>().GetListAsync(x => x.AttemptId == attempt.Id)).Count.ShouldBe(1));
    }

    private EducationAssessmentAppService ResponseImageService(IBlobContainer<EducationAssessmentImageBlobContainer> blobs,
        IRepository<EducationAssessmentCurrentResponse, Guid>? current = null, EducationAssessmentSubmissionProcessor? processor = null)
    {
        var lazy = Substitute.For<IAbpLazyServiceProvider>();
        lazy.LazyGetRequiredService<IPermissionChecker>().Returns(GetRequiredService<IPermissionChecker>());
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>());
        lazy.LazyGetRequiredService<ICurrentTenant>().Returns(GetRequiredService<ICurrentTenant>());
        lazy.LazyGetRequiredService<IAsyncQueryableExecuter>().Returns(GetRequiredService<IAsyncQueryableExecuter>());
        lazy.LazyGetRequiredService<IUnitOfWorkManager>().Returns(GetRequiredService<IUnitOfWorkManager>());
        lazy.LazyGetRequiredService<Volo.Abp.Guids.IGuidGenerator>().Returns(GetRequiredService<Volo.Abp.Guids.IGuidGenerator>());
        lazy.LazyGetRequiredService<EducationAssessmentAccessGuard>().Returns(GetRequiredService<EducationAssessmentAccessGuard>());
        lazy.LazyGetRequiredService<EducationAssessmentSubmissionProcessor>().Returns(processor ?? GetRequiredService<EducationAssessmentSubmissionProcessor>());
        lazy.LazyGetRequiredService<IOptions<EducationAssessmentResponseOptions>>().Returns(GetRequiredService<IOptions<EducationAssessmentResponseOptions>>());
        lazy.LazyGetRequiredService<IBlobContainer<EducationAssessmentImageBlobContainer>>().Returns(blobs);
        lazy.LazyGetRequiredService<Volo.Abp.Domain.Repositories.IRepository<EducationAssessmentAttempt, Guid>>().Returns(Repo<EducationAssessmentAttempt>());
        lazy.LazyGetRequiredService<Volo.Abp.Domain.Repositories.IRepository<EducationAssessmentRecipient, Guid>>().Returns(Repo<EducationAssessmentRecipient>());
        lazy.LazyGetRequiredService<Volo.Abp.Domain.Repositories.IRepository<EducationAssessmentTaskRevision, Guid>>().Returns(Repo<EducationAssessmentTaskRevision>());
        lazy.LazyGetRequiredService<Volo.Abp.Domain.Repositories.IRepository<EducationAssessmentTask, Guid>>().Returns(Repo<EducationAssessmentTask>());
        lazy.LazyGetRequiredService<Volo.Abp.Domain.Repositories.IRepository<ExamPaperQuestion, Guid>>().Returns(Repo<ExamPaperQuestion>());
        lazy.LazyGetRequiredService<Volo.Abp.Domain.Repositories.IRepository<ExamPaper, Guid>>().Returns(Repo<ExamPaper>());
        lazy.LazyGetRequiredService<Volo.Abp.Domain.Repositories.IRepository<EducationAssessmentAnswerAttachment, Guid>>().Returns(Repo<EducationAssessmentAnswerAttachment>());
        lazy.LazyGetRequiredService<Volo.Abp.Domain.Repositories.IRepository<EducationAssessmentCurrentResponse, Guid>>().Returns(current ?? Repo<EducationAssessmentCurrentResponse>());
        lazy.LazyGetRequiredService<Volo.Abp.Domain.Repositories.IRepository<EducationAssessmentResponseRevision, Guid>>().Returns(Repo<EducationAssessmentResponseRevision>());
        lazy.LazyGetRequiredService<IRepository<EducationAssessmentSubmission, Guid>>().Returns(Repo<EducationAssessmentSubmission>());
        return new EducationAssessmentAppService { LazyServiceProvider = lazy };
    }

    private IRepository<EducationAssessmentCurrentResponse, Guid> CurrentResponseReads(Func<Task> beforeRead)
    {
        var actual = Repo<EducationAssessmentCurrentResponse>();
        var reads = Substitute.For<IRepository<EducationAssessmentCurrentResponse, Guid>>();
        reads.GetListAsync(Arg.Any<Expression<Func<EducationAssessmentCurrentResponse, bool>>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await beforeRead();
                using var tracking = actual.DisableTracking();
                return await actual.GetListAsync(call.ArgAt<Expression<Func<EducationAssessmentCurrentResponse, bool>>>(0),
                    call.ArgAt<bool>(1), call.ArgAt<CancellationToken>(2));
            });
        return reads;
    }

    private EducationAssessmentSubmissionProcessor SubmissionProcessor(IRepository<EducationAssessmentCurrentResponse, Guid> current)
    {
        var lazy = Substitute.For<IAbpLazyServiceProvider>();
        lazy.LazyGetRequiredService<ICurrentTenant>().Returns(GetRequiredService<ICurrentTenant>());
        lazy.LazyGetRequiredService<IAsyncQueryableExecuter>().Returns(GetRequiredService<IAsyncQueryableExecuter>());
        lazy.LazyGetRequiredService<IRepository<EducationAssessmentAttempt, Guid>>().Returns(Repo<EducationAssessmentAttempt>());
        lazy.LazyGetRequiredService<IRepository<EducationAssessmentRecipient, Guid>>().Returns(Repo<EducationAssessmentRecipient>());
        lazy.LazyGetRequiredService<IRepository<EducationAssessmentTaskRevision, Guid>>().Returns(Repo<EducationAssessmentTaskRevision>());
        lazy.LazyGetRequiredService<IRepository<EducationAssessmentTask, Guid>>().Returns(Repo<EducationAssessmentTask>());
        lazy.LazyGetRequiredService<IRepository<ExamPaper, Guid>>().Returns(Repo<ExamPaper>());
        lazy.LazyGetRequiredService<IRepository<ExamPaperQuestion, Guid>>().Returns(Repo<ExamPaperQuestion>());
        lazy.LazyGetRequiredService<IRepository<EducationAssessmentCurrentResponse, Guid>>().Returns(current);
        lazy.LazyGetRequiredService<IRepository<EducationAssessmentResponseRevision, Guid>>().Returns(Repo<EducationAssessmentResponseRevision>());
        lazy.LazyGetRequiredService<IRepository<EducationAssessmentSubmission, Guid>>().Returns(Repo<EducationAssessmentSubmission>());
        return new EducationAssessmentSubmissionProcessor(lazy, GetRequiredService<IUnitOfWorkManager>(),
            GetRequiredService<IOptions<EducationAssessmentResponseOptions>>());
    }

    private static SaveEducationAssessmentResponseDto ResponseSave(AssessmentResponseFixture f, int version, EducationAssessmentResponseValue value) => new()
    {
        OperationId = Guid.NewGuid(), PaperQuestionId = f.QuestionId, PaperSnapshotVersion = f.PaperSnapshotVersion,
        ExpectedResponseSetVersion = version, Response = value
    };
    private static SubmitEducationAssessmentDto Submit(EducationStudentAssessmentAttemptDto attempt, int version) => new()
    { OperationId = Guid.NewGuid(), PaperSnapshotVersion = attempt.PaperSnapshotVersion, ExpectedResponseSetVersion = version };
    private static UploadEducationAssessmentAnswerImageDto ImageInput(AssessmentResponseFixture f) => new()
    { OperationId = Guid.NewGuid(), PaperQuestionId = f.QuestionId, ContentHash = Convert.ToHexStringLower(SHA256.HashData(AnswerPng)) };
    private static RemoteStreamContent Png() => new(new MemoryStream(AnswerPng, writable: false), "answer.png", "image/png", AnswerPng.LongLength);

    private async Task<AssessmentResponseFixture> ResponseFixtureAsync(bool subjective = false)
    {
        var f = await FixtureAsync();
        var student = await AssessmentStudentAsync(f);
        using var teacher = User(f.Reader);
        CreateEducationAssessmentDraftDto input;
        if (!subjective) input = await AssessmentInputAsync(f, fill: true);
        else
        {
            var value = Manual(f);
            value.Type = QuestionType.Subjective;
            value.Stem = "请用文字及图片说明解题过程";
            value.Options = [];
            value.Answers = [];
            value.ScoringDefinition = new()
            {
                ReferenceAnswer = "PRIVATE-REFERENCE-SENTINEL",
                ScoringPoints = [new() { Id = Guid.NewGuid(), DisplayOrder = 1, Description = "PRIVATE-SCORING-SENTINEL-A", Score = 6,
                    TextbookKnowledgePointMappingId = f.MappingA }, new() { Id = Guid.NewGuid(), DisplayOrder = 2,
                    Description = "PRIVATE-SCORING-SENTINEL-B", Score = 4, TextbookKnowledgePointMappingId = f.MappingB }]
            };
            var questions = GetRequiredService<IQuestionAppService>();
            var question = await questions.CreateAsync(value);
            question = await questions.ConfirmCurriculumAsync(question.Id, Confirm(f, question));
            question = await questions.SubmitReviewAsync(question.Id);
            question = await questions.ApproveAsync(question.Id, Review(question));
            var paper = await GetRequiredService<IExamAppService>().SavePaperAsync(Paper(f, question.Id));
            input = AssessmentInput(f, paper.Id);
        }
        var task = await Assessments.CreateDraftAsync(input);
        var published = await Assessments.PublishAsync(task.Id, AssessmentPublish(await Assessments.PreviewRecipientsAsync(task.Id)));
        var saved = await GetRequiredService<IExamAppService>().GetSavedPaperAsync(input.ExamPaperId);
        return new AssessmentResponseFixture(f, student, published, saved.Questions.Single().Id, saved.SnapshotVersion);
    }

    private sealed record AssessmentResponseFixture(Fixture School, AssessmentStudent Student,
        EducationAssessmentTaskRevisionDto Revision, Guid QuestionId, int PaperSnapshotVersion);

    private async Task<ExpiredAssessmentFixture> ExpiredResponseFixtureAsync(int expiresInMilliseconds = -420000)
    {
        var f = await ResponseFixtureAsync();
        return await WithUnitOfWorkAsync(async () =>
        {
            var source = f.Revision;
            var now = DateTime.UtcNow;
            var recorded = now.AddMinutes(-10);
            var task = new EducationAssessmentTask(Guid.NewGuid(), null, f.School.Reader, new string('a', 64), recorded);
            await Repo<EducationAssessmentTask>().InsertAsync(task, autoSave: true);
            EducationAssessmentTaskRevision Revision(Guid id, int number, Guid? previous, DateTime time, bool published) => new(
                id, null, f.School.Reader, task.Id, number, previous, "已到个人时限的真实作答", source.Instructions,
                source.SchoolId, source.AcademicTermId, source.SchoolClassId, source.SchoolClassName, source.TeachingAssignmentId,
                source.ExamPaperId, JsonSerializer.Serialize(source.Curriculum, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                EducationAssessmentRecipientScope.FullClass, [], now.AddMinutes(-15), now.AddHours(1), 1, 1,
                ExamResultVisibility.Hidden, false, new string('b', 64), time, published ? time : null, published ? new string('c', 64) : null);
            var draft = Revision(Guid.NewGuid(), 1, null, recorded, false);
            await Repo<EducationAssessmentTaskRevision>().InsertAsync(draft, autoSave: true);
            task.AdvanceDraft(draft);
            await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true);
            var publication = Revision(Guid.NewGuid(), 2, draft.Id, now.AddMinutes(-9), true);
            await Repo<EducationAssessmentTaskRevision>().InsertAsync(publication, autoSave: true);
            var frozenStudent = source.Recipients.Single();
            var recipient = new EducationAssessmentRecipient(Guid.NewGuid(), publication, f.Student.UserId,
                f.Student.MembershipId, f.Student.EnrollmentId, frozenStudent.StudentName, frozenStudent.AccountName);
            await Repo<EducationAssessmentRecipient>().InsertAsync(recipient, autoSave: true);
            task.Publish(publication);
            await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true);
            var attempt = new EducationAssessmentAttempt(Guid.NewGuid(), publication, recipient, now.AddMilliseconds(expiresInMilliseconds).AddMinutes(-1));
            await Repo<EducationAssessmentAttempt>().InsertAsync(attempt, autoSave: true);
            recipient.RecordStartedAttempt(attempt);
            await Repo<EducationAssessmentRecipient>().UpdateManyAsync([recipient], autoSave: true);
            return new ExpiredAssessmentFixture(f, attempt.Id, recipient.Id);
        });
    }
    private sealed record ExpiredAssessmentFixture(AssessmentResponseFixture Source, Guid AttemptId, Guid RecipientId);
}
