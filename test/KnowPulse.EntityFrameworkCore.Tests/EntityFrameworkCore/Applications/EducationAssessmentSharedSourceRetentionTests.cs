using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Shouldly;
using Volo.Abp.Authorization;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Shared_Source_Withdrawal_Should_Preserve_Frozen_Grading_And_Released_Results_Without_Reopening_The_Material(bool withdrawBeforeGrading)
    {
        var source = await ResponseFixtureAsync(subjective: true);
        var school = source.School;
        const string privateBody = "WITHDRAWN-PRIVATE-SOURCE-BODY";
        var material = await SourceAsync(school.Owner, privateBody);
        Guid bindingId;
        Guid shareId;
        using (User(school.Owner))
        {
            bindingId = await BindAsync(material, school.MappingA);
            shareId = (await _sharing.ShareVersionAsync(Share(school, material))).Id;
        }
        using (User(school.Reader))
        {
            var frozen = await WithUnitOfWorkAsync(() => Repo<ExamPaperQuestion>().GetAsync(source.QuestionId));
            var input = Manual(school);
            input.Type = QuestionType.Subjective;
            input.Options = [];
            input.Answers = [];
            input.ScoringDefinition = QuestionScoringDefinition.Parse(frozen.ScoringDefinitionJson!);
            input.Sources = [new()
            {
                KnowledgeItemId = material.KnowledgeItemId, KnowledgeVersionId = material.KnowledgeVersionId,
                ChunkId = material.Id, SchoolKnowledgeShareId = shareId, SourceExcerpt = "已核验的出题依据",
                CurriculumBindingIds = [bindingId], TextbookKnowledgePointMappingIds = [school.MappingA]
            }];
            var questions = GetRequiredService<IQuestionAppService>();
            var question = await questions.CreateAsync(input);
            question = await questions.ConfirmCurriculumAsync(question.Id, Confirm(school, question));
            question = await questions.SubmitReviewAsync(question.Id);
            question = await questions.ApproveAsync(question.Id, Review(question));
            var paper = await GetRequiredService<IExamAppService>().SavePaperAsync(Paper(school, question.Id));
            var task = await Assessments.CreateDraftAsync(AssessmentInput(school, paper.Id));
            var publication = await Assessments.PublishAsync(task.Id, AssessmentPublish(await Assessments.PreviewRecipientsAsync(task.Id)));
            source = source with { Revision = publication, QuestionId = paper.Questions.Single().Id, PaperSnapshotVersion = paper.SnapshotVersion };
        }
        var f = await GradingFixtureAsync(subjective: true, sourceOverride: source);
        var originalManifest = await WithUnitOfWorkAsync(async () => (await Repo<EducationAssessmentSubmission>().GetAsync(f.SubmissionId)).ManifestJson);
        if (withdrawBeforeGrading)
        {
            using (User(school.Owner)) await _sharing.RevokeAsync(shareId);
        }
        EducationAssessmentResultReleaseDto release;
        using (User(school.Reader))
        {
            await ConfirmManualGradeAsync(f.SubmissionId, 1, null);
            var sheet = await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
            sheet.Questions.Single().Response!.Text.ShouldBe("保留原始推理");
            sheet.Questions.Single().Grade.Confirmed!.ObtainedScore.ShouldBe(8);
            var closure = await Grades.CloseTaskAsync(f.TaskId, CloseInput(await Grades.PreviewFinalizationAsync(f.TaskId)));
            release = await Grades.PublishResultsAsync(f.TaskId,
                ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), closure.Id, ExamResultVisibility.FullReview));
        }
        string beforeWithdrawal;
        using (User(source.Student.UserId)) beforeWithdrawal = JsonSerializer.Serialize(await Grades.GetMyResultAsync(f.RecipientId));
        if (!withdrawBeforeGrading)
        {
            using (User(school.Owner)) await _sharing.RevokeAsync(shareId);
        }
        using (User(source.Student.UserId))
        {
            var result = await Grades.GetMyResultAsync(f.RecipientId);
            JsonSerializer.Serialize(result).ShouldBe(beforeWithdrawal);
            result.ResultReleaseId.ShouldBe(release.Id);
            result.TotalScore.ShouldBe(8);
            result.Questions.Single().Response!.Text.ShouldBe("保留原始推理");
            var json = JsonSerializer.Serialize(result);
            foreach (var hidden in new[] { privateBody, material.KnowledgeVersionId.ToString(), shareId.ToString(), "SchoolKnowledgeShareId", "CurriculumSource" })
                json.ShouldNotContain(hidden);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _sharing.GetDetailAsync(shareId));
        }
        using (User(school.Reader))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _sharing.GetDetailAsync(shareId));
            (await Grades.GetReleaseOperationAsync(f.TaskId, release.Id))!.Items.Single().TotalScore.ShouldBe(8);
            (await Grades.GetSubmissionForGradingAsync(f.SubmissionId)).Questions.Single().Grade.Confirmed!.ObtainedScore.ShouldBe(8);
            await ConfirmManualGradeAsync(f.SubmissionId, 0, "复核冻结答卷后更正评分");
            (await Grades.GetReleaseOperationAsync(f.TaskId, release.Id))!.Items.Single().TotalScore.ShouldBe(8);
        }
        using (User(source.Student.UserId))
        {
            var corrected = await Grades.GetMyResultAsync(f.RecipientId);
            corrected.TotalScore.ShouldBe(10);
            corrected.ResultReleaseId.ShouldNotBe(release.Id);
            corrected.Questions.Single().Response!.Text.ShouldBe("保留原始推理");
            await Should.ThrowAsync<AbpAuthorizationException>(() => _sharing.GetDetailAsync(shareId));
        }
        using (User(f.Absent.UserId))
        {
            var absent = await Grades.GetMyResultAsync(f.AbsentRecipientId);
            absent.Outcome.ShouldBe(EducationAssessmentRecipientOutcome.Absent);
            absent.TotalScore.ShouldBeNull();
            await Should.ThrowAsync<AbpAuthorizationException>(() => Grades.GetMyResultAsync(f.RecipientId));
        }
        await WithUnitOfWorkAsync(async () =>
            (await Repo<EducationAssessmentSubmission>().GetAsync(f.SubmissionId)).ManifestJson.ShouldBe(originalManifest));
    }
}
