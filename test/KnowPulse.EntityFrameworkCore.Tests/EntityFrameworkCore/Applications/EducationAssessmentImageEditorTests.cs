using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Blazor.Client.Pages.Education;
using KnowPulse.Education;
using KnowPulse.QuestionBank;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.BlobStoring;
using Volo.Abp.Data;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Fact]
    public async Task Image_Editor_Should_Keep_Upload_And_Link_Separate_Then_Preserve_Replacements_Rotation_And_Original_Evidence()
    {
        var f = await ResponseFixtureAsync(true); EducationAssessmentSubmissionDto submission; Guid replacementId;
        using (User(f.Student.UserId)) {
            var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
            var question = attempt.Paper.Questions.Single(); var local = new EducationAssessmentLocalDrafts();
            local.Edit(question, attempt).Text = "  保留文字步骤\n以及原始空白  ";
            var a = await Assessments.UploadAnswerImageAsync(attempt.Id, ImageInput(f), Png());
            var b = await Assessments.UploadAnswerImageAsync(attempt.Id, ImageInput(f), Png());
            local.AttachUploaded(question, attempt, a); local.AttachUploaded(question, attempt, b);
            (await Assessments.GetAttemptAsync(attempt.Id)).Responses.ShouldBeEmpty(); a.CurrentResponseRevisionId.ShouldBeNull();
            var first = await Assessments.SaveResponseAsync(attempt.Id, ResponseSave(f, 0, local.Value(question, attempt)));
            local.Drop(question.Id); attempt = await Assessments.GetAttemptAsync(attempt.Id);
            var value = local.Edit(question, attempt); value.Images.Reverse(); value.Images[0].RotationDegrees = 270;
            var c = await Assessments.UploadAnswerImageAsync(attempt.Id, ImageInput(f), Png()); replacementId = c.Id;
            local.AttachUploaded(question, attempt, c, a.Id); local.AttachUploaded(question, attempt, c, a.Id);
            value.Images.Select(x => x.AttachmentId).ShouldBe(new[] { b.Id, c.Id }); value.Images[0].RotationDegrees.ShouldBe(270);
            attempt.Responses.Single().Response.Images.Select(x => x.AttachmentId).ShouldBe(new[] { a.Id, b.Id });
            (await Assessments.GetResponseRevisionAsync(attempt.Id, first.Id))!.Response.Images.Select(x => x.AttachmentId).ShouldBe(new[] { a.Id, b.Id });
            local.HasConflict(question.Id, attempt).ShouldBeFalse();
            var saved = await Assessments.SaveResponseAsync(attempt.Id, ResponseSave(f, attempt.ResponseSetVersion, value));
            saved.Response.Text.ShouldBe("  保留文字步骤\n以及原始空白  ");
            (await Assessments.GetAnswerImageAsync(attempt.Id, c.Id))!.CurrentResponseRevisionId.ShouldBe(saved.Id);
            (await Assessments.GetAnswerImageAsync(attempt.Id, a.Id))!.CurrentResponseRevisionId.ShouldBeNull();
            using var oldImage = await Assessments.DownloadAnswerImageAsync(attempt.Id, a.Id);
            using var originalBytes = new MemoryStream(); await oldImage.GetStream().CopyToAsync(originalBytes); originalBytes.ToArray().ShouldBe(AnswerPng);
            var submit = Submit(attempt, saved.ResultResponseSetVersion); submit.ConfirmSavedOnly = true;
            submission = await SubmitReviewedAsync(attempt.Id, submit);
        }
        await GetRequiredService<EducationAssessmentGradingProcessor>().InitializeAsync(submission.Id);
        using (User(f.School.Reader)) {
            var grading = await Grades.GetSubmissionForGradingAsync(submission.Id); var answer = grading.Questions.Single();
            answer.Response!.Images.Last().AttachmentId.ShouldBe(replacementId); answer.Response.Images.First().RotationDegrees.ShouldBe(270);
            answer.Response.Text.ShouldBe("  保留文字步骤\n以及原始空白  "); answer.Images.Count.ShouldBe(2);
            using var file = await Grades.DownloadSubmissionImageAsync(submission.Id, replacementId);
            using var bytes = new MemoryStream(); await file.GetStream().CopyToAsync(bytes); bytes.ToArray().ShouldBe(AnswerPng);
        }
    }

    [Fact]
    public async Task Image_Editor_Should_Retain_The_Original_Draft_Base_Across_Refresh_And_Require_A_Choice_After_Another_Device_Saves()
    {
        var f = await ResponseFixtureAsync(true); using var actor = User(f.Student.UserId);
        var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
        var first = await Assessments.SaveResponseAsync(attempt.Id, ResponseSave(f, 0, new() { Type = QuestionType.Subjective, Text = "服务器原稿" }));
        attempt = await Assessments.GetAttemptAsync(attempt.Id); var question = attempt.Paper.Questions.Single(); var local = new EducationAssessmentLocalDrafts();
        local.Edit(question, attempt).Text = "旧设备尚未同步的步骤";
        var other = await Assessments.SaveResponseAsync(attempt.Id, ResponseSave(f, attempt.ResponseSetVersion, new() { Type = QuestionType.Subjective, Text = "另一设备较新的答案" }));
        var conflict = await Should.ThrowAsync<BusinessException>(() => Assessments.SaveResponseAsync(attempt.Id, ResponseSave(f, attempt.ResponseSetVersion, local.Value(question, attempt))));
        conflict.Code.ShouldBe(EducationAssessmentErrorCodes.ResponseVersionConflict);
        var restored = JsonSerializer.Deserialize<EducationAssessmentLocalDrafts>(JsonSerializer.Serialize(local))!;
        var refreshed = await Assessments.GetAttemptAsync(attempt.Id); restored.HasConflict(question.Id, refreshed).ShouldBeTrue();
        restored.Edit(question, refreshed).Text.ShouldBe("旧设备尚未同步的步骤");
        restored.BaseRevisionIds[question.Id].ShouldBe(first.Id); restored.HasConflict(question.Id, refreshed).ShouldBeTrue();
        (await Assessments.GetResponseRevisionAsync(attempt.Id, first.Id))!.Response.Text.ShouldBe("服务器原稿");
        (await Assessments.GetAttemptAsync(attempt.Id)).Responses.Single().Id.ShouldBe(other.Id);
        var image = await Assessments.UploadAnswerImageAsync(attempt.Id, ImageInput(f), Png()); restored.AttachUploaded(question, refreshed, image);
        restored.HasConflict(question.Id, refreshed).ShouldBeTrue();
        restored.ConfirmLocal(question.Id, refreshed); restored.HasConflict(question.Id, refreshed).ShouldBeFalse();
        var saved = await Assessments.SaveResponseAsync(attempt.Id, ResponseSave(f, refreshed.ResponseSetVersion, restored.Value(question, refreshed)));
        saved.Response.Text.ShouldBe("旧设备尚未同步的步骤"); saved.Response.Images.Single().AttachmentId.ShouldBe(image.Id);
        (await Assessments.GetResponseRevisionAsync(attempt.Id, other.Id))!.Response.Text.ShouldBe("另一设备较新的答案");
    }

    [Fact]
    public async Task Image_Editor_Should_Not_Attach_An_Unconfirmed_Upload_And_Should_Recover_One_Original_Operation_Without_Losing_Text()
    {
        var f = await ResponseFixtureAsync(true); using var actor = User(f.Student.UserId);
        var attempt = await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() });
        var question = attempt.Paper.Questions.Single(); var local = new EducationAssessmentLocalDrafts(); local.Edit(question, attempt).Text = "单图失败仍保留全题文字";
        var actual = GetRequiredService<IBlobContainer<EducationAssessmentImageBlobContainer>>(); var blobs = Substitute.For<IBlobContainer<EducationAssessmentImageBlobContainer>>();
        blobs.GetOrNullAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => actual.GetOrNullAsync(call.ArgAt<string>(0), call.ArgAt<CancellationToken>(1)));
        var mayWrite = false;
        blobs.SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(async call => {
            if (!mayWrite) throw new IOException("Test image write unavailable");
            await actual.SaveAsync(call.ArgAt<string>(0), call.ArgAt<Stream>(1), false, call.ArgAt<CancellationToken>(3));
            throw new IOException("Test successful write acknowledgement lost");
        });
        var service = ResponseImageService(blobs); var input = ImageInput(f);
        await Should.ThrowAsync<BusinessException>(() => service.UploadAnswerImageAsync(attempt.Id, input, Png()));
        var pending = (await Assessments.GetAnswerImageAsync(attempt.Id, input.OperationId))!; pending.IsUploaded.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => local.AttachUploaded(question, attempt, pending));
        local.Value(question, attempt).Text.ShouldBe("单图失败仍保留全题文字"); local.Value(question, attempt).Images.ShouldBeEmpty();
        local = JsonSerializer.Deserialize<EducationAssessmentLocalDrafts>(JsonSerializer.Serialize(local))!; mayWrite = true;
        var confirmed = await service.UploadAnswerImageAsync(attempt.Id, input, Png()); confirmed.Id.ShouldBe(pending.Id);
        local.AttachUploaded(question, attempt, confirmed); local.AttachUploaded(question, attempt, confirmed);
        local.Value(question, attempt).Images.Single().AttachmentId.ShouldBe(input.OperationId); local.Value(question, attempt).Text.ShouldBe("单图失败仍保留全题文字");
        (await Assessments.GetAttemptAsync(attempt.Id)).Responses.ShouldBeEmpty(); confirmed.CurrentResponseRevisionId.ShouldBeNull();
        await Assessments.SaveResponseAsync(attempt.Id, ResponseSave(f, 0, local.Value(question, attempt)));
        (await Assessments.GetAnswerImageAsync(attempt.Id, confirmed.Id))!.CurrentResponseRevisionId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Image_Editor_Policy_Should_Use_Actual_Configuration_And_Keep_Other_Accounts_And_Tenants_Out()
    {
        var f = await ResponseFixtureAsync(true); Guid attempt;
        var options = GetRequiredService<IOptions<EducationAssessmentResponseOptions>>().Value; var before = options.MaxImageBytes;
        try {
            options.MaxImageBytes = 2048;
            using (User(f.Student.UserId)) {
                attempt = (await Assessments.StartOrResumeAsync(f.Revision.Recipients.Single().Id, new() { OperationId = Guid.NewGuid() })).Id;
                var policy = await Assessments.GetAnswerImagePolicyAsync(attempt); policy.MaxFileBytes.ShouldBe(2048); policy.MaxImagesPerResponse.ShouldBe(20);
                policy.AllowedContentTypes.Order().ShouldBe(new[] { "image/jpeg", "image/png", "image/webp" }.Order());
            }
            using (User(f.School.Reader)) await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetAnswerImagePolicyAsync(attempt));
            using (User(f.Student.UserId)) using (GetRequiredService<Volo.Abp.MultiTenancy.ICurrentTenant>().Change(Guid.NewGuid()))
                await Should.ThrowAsync<AbpAuthorizationException>(() => Assessments.GetAnswerImagePolicyAsync(attempt));
        } finally { options.MaxImageBytes = before; }
    }

    [Fact]
    public void Image_Editor_Should_Preserve_Legacy_Cached_Answers_But_Never_Assume_Their_Unknown_Server_Base()
    {
        var question = new EducationStudentAssessmentQuestionDto { Id = Guid.NewGuid(), Type = QuestionType.Subjective };
        var server = new EducationStudentAssessmentAttemptDto { Id = Guid.NewGuid(), Paper = new() { Questions = [question] }, Responses = [new() { Id = Guid.NewGuid(), PaperQuestionId = question.Id, Response = new() { Type = QuestionType.Subjective, Text = "服务器较新答案" } }] };
        var local = new EducationAssessmentLocalDrafts { Values = { [question.Id] = new() { Type = QuestionType.Subjective, Text = "旧版标签页草稿" } } };
        local.HasConflict(question.Id, server).ShouldBeTrue(); local.Edit(question, server).Text.ShouldBe("旧版标签页草稿"); local.HasConflict(question.Id, server).ShouldBeTrue();
        local.ConfirmLocal(question.Id, server); local.HasConflict(question.Id, server).ShouldBeFalse(); server.Responses.Single().Response.Text.ShouldBe("服务器较新答案");
        local.Drop(question.Id); local.Values.ShouldBeEmpty(); local.BaseRevisionIds.ShouldBeEmpty(); local.Value(question, server).Text.ShouldBe("服务器较新答案");
    }
}
