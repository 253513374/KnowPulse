using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.Permissions;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationStudyHintReviewAppService StudyHints => GetRequiredService<IEducationStudyHintReviewAppService>();
    private static SaveEducationHintReviewDto StudyHint(EducationStudyHintContextDto value, EducationLearningExposureUse use = EducationLearningExposureUse.ConfirmedNoHints) => new() {
        OperationId = Guid.NewGuid(), ExpectedRevisionId = value.Current?.Id, ConcurrencyStamp = value.ConcurrencyStamp, Use = use, ConfirmedExactContext = true, Reason = "已核对实际下发版本、附件及冻结题" };
    private async Task<(GradingFixture Graded, StudyFixture Study, Guid RecipientId, Guid QuestionId)> StudyHintFixtureAsync(bool shared = false, bool open = true)
    {
        var graded = await GradingFixtureAsync(false); var study = await StudyAsync(shared, existingSchool: graded.Source.School, existingStudent: graded.Source.Student);
        var recipient = StudyRecipient(study);
        if (open) using (User(study.Student.UserId)) await Study.OpenMyAsync(recipient);
        var question = await WithUnitOfWorkAsync(async () => (await Repo<ExamPaperQuestion>().GetListAsync(x => x.PaperId == graded.Source.Revision.ExamPaperId)).OrderBy(x => x.DisplayOrder).First().Id);
        return (graded, study, recipient, question);
    }
    [Fact]
    public async Task Study_Hint_Should_Review_Actual_Publication_And_Frozen_Question_Without_Rewriting_Private_Drafts()
    {
        var f = await StudyHintFixtureAsync(); using var actor = User(f.Study.School.Reader);
        var choices = await StudyHints.GetHintCandidatesAsync(f.RecipientId, new()); choices.Items.ShouldContain(x => x.Id == f.QuestionId);
        var root = await Study.GetAsync(f.Study.TaskId); await Study.SaveDraftAsync(root.Id, new() { OperationId = Guid.NewGuid(), ExpectedRevisionId = root.Current.Id, ConcurrencyStamp = root.ConcurrencyStamp,
            Scope = f.Study.Draft.Scope, Content = f.Study.Draft.Content with { Title = "私有新标题", Instructions = "尚未下发的提示" } });
        var context = await StudyHints.GetHintContextAsync(f.RecipientId, f.QuestionId); context.TaskTitle.ShouldBe(f.Study.Publication.Content!.Title); context.Instructions.ShouldBe(f.Study.Draft.Content.Instructions);
        context.Question.PaperQuestionId.ShouldBe(f.QuestionId); context.Question.Stem.ShouldNotBeNullOrWhiteSpace(); context.DefinitionRevisionId.ShouldNotBe(Guid.Empty); context.Current.ShouldBeNull();
        JsonSerializer.Serialize(context).ShouldNotContain("私有新标题"); context.Materials.Single().Id.ShouldBe(f.Study.Material.Id);
        var saved = await StudyHints.SaveHintReviewAsync(f.RecipientId, f.QuestionId, StudyHint(context));
        var current = await StudyHints.GetHintContextAsync(f.RecipientId, f.QuestionId); current.Current!.Id.ShouldBe(saved.Id); current.ExposedAtUtc.ShouldBe(context.ExposedAtUtc);
        (await WithUnitOfWorkAsync(() => Repo<EducationStudyEvent>().GetCountAsync())).ShouldBe(0);
    }
    [Fact]
    public async Task Study_Hint_Should_Recover_Exact_Operations_And_Reject_Changed_Commands_And_Stale_Heads()
    {
        var f = await StudyHintFixtureAsync(); using var actor = User(f.Study.School.Reader);
        var initial = StudyHint(await StudyHints.GetHintContextAsync(f.RecipientId, f.QuestionId)); var first = await StudyHints.SaveHintReviewAsync(f.RecipientId, f.QuestionId, initial);
        var next = await StudyHints.SaveHintReviewAsync(f.RecipientId, f.QuestionId, StudyHint(await StudyHints.GetHintContextAsync(f.RecipientId, f.QuestionId), EducationLearningExposureUse.Confirmed));
        (await StudyHints.SaveHintReviewAsync(f.RecipientId, f.QuestionId, initial)).Id.ShouldBe(first.Id);
        (await StudyHints.GetHintReviewOperationAsync(f.RecipientId, f.QuestionId, first.Id))!.Id.ShouldBe(first.Id);
        initial.OperationId = Guid.NewGuid(); await Should.ThrowAsync<AbpDbConcurrencyException>(() => StudyHints.SaveHintReviewAsync(f.RecipientId, f.QuestionId, initial));
        initial.OperationId = first.Id; initial.Reason = "同编号换内容"; await Should.ThrowAsync<BusinessException>(() => StudyHints.SaveHintReviewAsync(f.RecipientId, f.QuestionId, initial));
        (await StudyHints.GetHintContextAsync(f.RecipientId, f.QuestionId)).Current!.Id.ShouldBe(next.Id);
        var unconfirmed = StudyHint(await StudyHints.GetHintContextAsync(f.RecipientId, f.QuestionId)); unconfirmed.ConfirmedExactContext = false;
        await Should.ThrowAsync<BusinessException>(() => StudyHints.SaveHintReviewAsync(f.RecipientId, f.QuestionId, unconfirmed));
    }
    [Fact]
    public async Task Study_Hint_Should_Not_Infer_Actual_Provision_From_Teacher_Views_Or_Lists()
    {
        var f = await StudyHintFixtureAsync(open: false); using var actor = User(f.Study.School.Reader);
        await Study.GetProgressAsync(f.Study.TaskId, null, new());
        await Should.ThrowAsync<AbpAuthorizationException>(() => StudyHints.GetHintCandidatesAsync(f.RecipientId, new()));
        await Should.ThrowAsync<AbpAuthorizationException>(() => StudyHints.GetHintContextAsync(f.RecipientId, f.QuestionId));
        (await WithUnitOfWorkAsync(() => Repo<EducationStudyHintReview>().GetCountAsync())).ShouldBe(0);
        (await WithUnitOfWorkAsync(async () => (await Repo<EducationStudyRecipient>().GetAsync(f.RecipientId)).FirstProvidedAtUtc)).ShouldBeNull();
    }
    [Fact]
    public async Task Study_Hint_Should_Block_New_No_Hints_After_Shared_Material_Withdrawal_But_Recover_The_Old_Receipt()
    {
        var f = await StudyHintFixtureAsync(shared: true); SaveEducationHintReviewDto input;
        using (User(f.Study.School.Reader)) { input = StudyHint(await StudyHints.GetHintContextAsync(f.RecipientId, f.QuestionId)); await StudyHints.SaveHintReviewAsync(f.RecipientId, f.QuestionId, input); }
        using (User(f.Study.School.Owner)) await _sharing.RevokeAsync(f.Study.ShareId!.Value);
        using (User(f.Study.School.Reader)) {
            (await StudyHints.SaveHintReviewAsync(f.RecipientId, f.QuestionId, input)).Id.ShouldBe(input.OperationId);
            var context = await StudyHints.GetHintContextAsync(f.RecipientId, f.QuestionId); context.MaterialsAvailable.ShouldBeFalse();
            await Should.ThrowAsync<BusinessException>(() => StudyHints.SaveHintReviewAsync(f.RecipientId, f.QuestionId, StudyHint(context)));
            (await StudyHints.SaveHintReviewAsync(f.RecipientId, f.QuestionId, StudyHint(context, EducationLearningExposureUse.Unverified))).Use.ShouldBe(EducationLearningExposureUse.Unverified);
        }
        using (User(f.Study.Student.UserId)) await Should.ThrowAsync<AbpAuthorizationException>(() => Study.GetMyMaterialAsync(f.RecipientId, f.Study.Material.Id));
    }
    [Fact]
    public async Task Study_Hint_Should_Reauthorize_Student_Other_Teacher_And_Tenant_Even_For_Receipts()
    {
        var f = await StudyHintFixtureAsync(); Guid revision;
        using (User(f.Study.School.Reader)) revision = (await StudyHints.SaveHintReviewAsync(f.RecipientId, f.QuestionId, StudyHint(await StudyHints.GetHintContextAsync(f.RecipientId, f.QuestionId)))).Id;
        foreach (var actor in new[] { f.Study.Student.UserId, f.Study.School.Owner }) using (User(actor)) {
            await Should.ThrowAsync<AbpAuthorizationException>(() => StudyHints.GetHintContextAsync(f.RecipientId, f.QuestionId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => StudyHints.GetHintReviewOperationAsync(f.RecipientId, f.QuestionId, revision));
        }
        using (User(f.Study.School.Reader)) using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid())) await Should.ThrowAsync<AbpAuthorizationException>(() => StudyHints.GetHintContextAsync(f.RecipientId, f.QuestionId));
        using (User(f.Study.Student.UserId)) JsonSerializer.Serialize(await Study.OpenMyAsync(f.RecipientId)).ShouldNotContain("已核对实际下发版本");
    }
    [Theory]
    [InlineData(EducationPermissions.LearningManage)]
    [InlineData(EducationPermissions.AssessmentManage)]
    [InlineData(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    public async Task Study_Hint_Should_Explicitly_Reject_Denied_Function_Permissions(string permission)
    {
        var f = await FixtureAsync(); using var user = User(f.Reader); var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(Arg.Any<string>()).Returns(true); checker.IsGrantedAsync(permission).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker); lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>());
        lazy.LazyGetRequiredService<IUnitOfWorkManager>().Returns(GetRequiredService<IUnitOfWorkManager>()); lazy.LazyGetRequiredService<EducationAssessmentAccessGuard>().Returns(GetRequiredService<EducationAssessmentAccessGuard>());
        var service = new EducationStudyTaskAppService { LazyServiceProvider = lazy }; var id = Guid.NewGuid();
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetHintContextAsync(id, id)); await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetHintCandidatesAsync(id, new()));
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.SaveHintReviewAsync(id, id, new() { OperationId = id }));
    }
    [Fact]
    public async Task Study_Hint_Exact_Reviews_Should_Unblock_New_Formal_Start_And_Not_Clear_Another_Material_Context()
    {
        var formal = await FormalFixtureAsync(bind: false); var school = formal.Source.Source.School; var student = formal.Source.Source.Student;
        var f = await StudyAsync(false, existingSchool: school, existingStudent: student); var recipient = StudyRecipient(f);
        using (User(student.UserId)) await Study.OpenMyAsync(recipient);
        var question = await WithUnitOfWorkAsync(async () => (await Repo<ExamPaperQuestion>().GetListAsync(x => x.PaperId == formal.Task.ExamPaperId)).Single().Id);
        using (User(school.Reader)) {
            await Should.ThrowAsync<BusinessException>(() => Formal.PreviewFormalAssessmentAsync(formal.ActivityId, formal.Task.TaskId));
            await StudyHints.SaveHintReviewAsync(recipient, question, StudyHint(await StudyHints.GetHintContextAsync(recipient, question)));
            var preview = await Formal.PreviewFormalAssessmentAsync(formal.ActivityId, formal.Task.TaskId); await Formal.BindFormalAssessmentAsync(formal.ActivityId, FormalBind(preview));
        }
        using (User(student.UserId)) (await Assessments.GetMyStudentTaskAsync(formal.RecipientId)).CanStart.ShouldBeTrue();
        var second = await StudyAsync(false, existingSchool: school, existingStudent: student); var secondRecipient = StudyRecipient(second);
        using (User(student.UserId)) { await Study.OpenMyAsync(secondRecipient); (await Assessments.GetMyStudentTaskAsync(formal.RecipientId)).CanStart.ShouldBeFalse(); }
        using (User(school.Reader)) await StudyHints.SaveHintReviewAsync(secondRecipient, question, StudyHint(await StudyHints.GetHintContextAsync(secondRecipient, question)));
        using (User(student.UserId)) { (await Assessments.GetMyStudentTaskAsync(formal.RecipientId)).CanStart.ShouldBeTrue(); (await Assessments.StartOrResumeAsync(formal.RecipientId, new() { OperationId = Guid.NewGuid() })).Id.ShouldNotBe(Guid.Empty); }
    }
    [Fact]
    public async Task Study_Hint_Should_Version_Analysis_Without_Erasing_Real_Full_Review_Exposure_And_Page_All_Contexts()
    {
        var f = await StudyHintFixtureAsync(); var reader = GetRequiredService<EducationLearningReportSourceReader>();
        using var teacher = User(f.Study.School.Reader); var contexts = await Evidence.GetExposureContextsAsync(await EvidenceGradeAsync(f.Graded), new() { MaxResultCount = 1 });
        contexts.TotalCount.ShouldBe(1); contexts.Items.Single().Source.ShouldBe(EducationQuestionExposureSource.StudyTask); contexts.Items.Single().CanReview.ShouldBeTrue();
        var end = DateTime.UtcNow; var before = await WithUnitOfWorkAsync(() => reader.CaptureAsync(f.Study.Publication.Scope, end.AddDays(-1), end, [f.Graded.TaskId]));
        var context = await StudyHints.GetHintContextAsync(f.RecipientId, f.QuestionId); var saved = await StudyHints.SaveHintReviewAsync(f.RecipientId, f.QuestionId, StudyHint(context));
        var after = await WithUnitOfWorkAsync(() => reader.CaptureAsync(f.Study.Publication.Scope, end.AddDays(-1), end, [f.Graded.TaskId])); after.Fingerprint.ShouldNotBe(before.Fingerprint);
        after.Students.Single(x => x.Student.StudentUserId == f.Study.Student.UserId).Exposures.Single(x => x.PaperQuestionId == f.QuestionId).HintReviewRevisionId.ShouldBe(saved.Id);
        JsonSerializer.Serialize(after).ShouldNotContain(saved.Reason); JsonSerializer.Serialize(after).ShouldNotContain(f.Study.Draft.Content.Instructions);
        await ExposureReleaseAsync(f.Graded, ExamResultVisibility.FullReview);
        using (User(f.Study.Student.UserId)) await Grades.GetMyResultAsync(f.Graded.RecipientId);
        var grade = await EvidenceGradeAsync(f.Graded); var first = await Evidence.GetExposureContextsAsync(grade, new() { MaxResultCount = 1 }); var second = await Evidence.GetExposureContextsAsync(grade, new() { SkipCount = 1, MaxResultCount = 1 });
        first.TotalCount.ShouldBe(2); second.TotalCount.ShouldBe(2); first.Items.Concat(second.Items).Select(x => x.Id).Distinct().Count().ShouldBe(2);
        first.Items.Concat(second.Items).Single(x => x.Source == EducationQuestionExposureSource.AssessmentReview).Use.ShouldBe(EducationLearningExposureUse.Confirmed);
    }
    [Fact]
    public async Task Study_Hint_Should_Not_Copy_A_Decision_To_Another_Frozen_Copy_Of_The_Same_Original_Question()
    {
        var f = await StudyHintFixtureAsync(); using var teacher = User(f.Study.School.Reader);
        await StudyHints.SaveHintReviewAsync(f.RecipientId, f.QuestionId, StudyHint(await StudyHints.GetHintContextAsync(f.RecipientId, f.QuestionId)));
        var original = await WithUnitOfWorkAsync(async () => (await Repo<ExamPaperQuestion>().GetAsync(f.QuestionId)).OriginalQuestionId);
        var exam = GetRequiredService<IExamAppService>(); var paper = await exam.SavePaperAsync(Paper(f.Study.School, original));
        var draft = await Assessments.CreateDraftAsync(AssessmentInput(f.Study.School, paper.Id));
        await Assessments.PublishAsync(draft.Id, AssessmentPublish(await Assessments.PreviewRecipientsAsync(draft.Id)));
        var another = (await exam.GetSavedPaperAsync(paper.Id)).Questions.Single().Id; another.ShouldNotBe(f.QuestionId);
        var context = await StudyHints.GetHintContextAsync(f.RecipientId, another); context.Current.ShouldBeNull();
        (await StudyHints.GetHintContextAsync(f.RecipientId, f.QuestionId)).Current!.Use.ShouldBe(EducationLearningExposureUse.ConfirmedNoHints);
        await StudyHints.SaveHintReviewAsync(f.RecipientId, another, StudyHint(context));
        var confirmed = await StudyHints.SaveHintReviewAsync(f.RecipientId, f.QuestionId, StudyHint(await StudyHints.GetHintContextAsync(f.RecipientId, f.QuestionId), EducationLearningExposureUse.Confirmed));
        var end = DateTime.UtcNow; var reader = GetRequiredService<EducationLearningReportSourceReader>();
        var captured = await WithUnitOfWorkAsync(() => reader.CaptureAsync(f.Study.Publication.Scope, end.AddDays(-1), end, [draft.Id]));
        var contacts = captured.Students.Single(x => x.Student.StudentUserId == f.Study.Student.UserId).Exposures;
        contacts.Single(x => x.PaperQuestionId == another).Use.ShouldBe(EducationLearningExposureUse.ConfirmedNoHints);
        contacts.ShouldContain(x => x.PaperQuestionId == null && x.Use == EducationLearningExposureUse.Confirmed && x.HintReviewRevisionId == confirmed.Id);
    }
}
