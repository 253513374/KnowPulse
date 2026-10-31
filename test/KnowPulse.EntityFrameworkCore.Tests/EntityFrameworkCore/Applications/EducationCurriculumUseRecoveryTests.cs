using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Identity;
using Volo.Abp.Uow;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Fact]
    public async Task Saved_Paper_Operation_Should_Recover_The_Same_Complete_Snapshot_Without_Reselecting_Current_Curriculum()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var question = await ApprovedManualAsync(f);
        var exams = GetRequiredService<IExamAppService>();
        var input = Paper(f, question.Id);
        var saved = await exams.SavePaperAsync(input);
        saved.Id.ShouldBe(input.OperationId!.Value);
        saved.OperationId.ShouldBe(input.OperationId);
        var rowId = saved.Questions.Single().Id;
        var sourceStamp = (await WithUnitOfWorkAsync(() => Repo<Question>().GetAsync(question.Id))).ConcurrencyStamp;
        await WithUnitOfWorkAsync(async () =>
        {
            var selection = await Repo<TeacherTextbookSelection>().GetAsync(f.ReaderSelection);
            selection.End(DateTime.UtcNow);
            await Repo<TeacherTextbookSelection>().UpdateManyAsync([selection], autoSave: true);
        });
        // The client can query an unknown result by the operation ID, then retry that same fact.
        var recovered = await exams.GetSavedPaperAsync(input.OperationId.Value);
        var repeated = await exams.SavePaperAsync(input);
        repeated.Id.ShouldBe(recovered.Id);
        repeated.Questions.Single().Id.ShouldBe(rowId);
        repeated.Questions.Single().Stem.ShouldBe(question.Stem);
        repeated.Questions.Single().CorrectAnswers.ShouldBe(saved.Questions.Single().CorrectAnswers);
        repeated.Questions.Single().CurriculumSources.ShouldBeEmpty();
        repeated.Questions.Single().CurriculumPointWeights.Select(x => (x.TextbookKnowledgePointMappingId, x.Weight, x.Score))
            .ShouldBe(saved.Questions.Single().CurriculumPointWeights.Select(x => (x.TextbookKnowledgePointMappingId, x.Weight, x.Score)));
        repeated.Curriculum!.TeacherTextbookSelectionId.ShouldBe(f.ReaderSelection);
        (await WithUnitOfWorkAsync(() => Repo<ExamPaper>().GetCountAsync())).ShouldBe(1);
        (await WithUnitOfWorkAsync(() => Repo<ExamPaperQuestion>().GetCountAsync())).ShouldBe(1);
        (await WithUnitOfWorkAsync(() => Repo<Question>().GetAsync(question.Id))).ConcurrencyStamp.ShouldBe(sourceStamp);
        input.OperationId = Guid.NewGuid();
        await Should.ThrowAsync<AbpAuthorizationException>(() => exams.SavePaperAsync(input));
    }

    [Fact]
    public async Task Paper_Save_Should_Reject_Missing_Operation_Parameter_Conflicts_And_Other_Owners()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var question = await ApprovedManualAsync(f);
        var exams = GetRequiredService<IExamAppService>();
        var input = Paper(f, question.Id);
        var operation = input.OperationId;
        input.OperationId = null;
        await Should.ThrowAsync<BusinessException>(() => exams.SavePaperAsync(input));
        input.OperationId = Guid.Empty;
        await Should.ThrowAsync<BusinessException>(() => exams.SavePaperAsync(input));
        input.OperationId = operation;
        var saved = await exams.SavePaperAsync(input);
        input.Rules[0].ScorePerQuestion = 10.00m; // equivalent decimal representation is the same request
        (await exams.SavePaperAsync(input)).Id.ShouldBe(saved.Id);
        input.Title = "相同编号的另一份试卷";
        (await Should.ThrowAsync<BusinessException>(() => exams.SavePaperAsync(input))).Code.ShouldBe("KnowPulse:Education:PaperSaveOperationConflict");
        input.Title = saved.Title;
        input.CurriculumScope!.TextbookKnowledgePointMappingIds = [f.MappingA];
        (await Should.ThrowAsync<BusinessException>(() => exams.SavePaperAsync(input))).Code.ShouldBe("KnowPulse:Education:PaperSaveOperationConflict");
        input.CurriculumScope = Both(f);
        using (User(f.Owner)) await Should.ThrowAsync<AbpAuthorizationException>(() => exams.SavePaperAsync(input));
        (await WithUnitOfWorkAsync(() => Repo<ExamPaper>().GetCountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Manual_Curriculum_Create_Operation_Should_Recover_Its_Own_Draft_And_Reject_Different_Facts()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var service = GetRequiredService<IQuestionAppService>();
        var input = Manual(f);
        var operation = input.OperationId;
        input.OperationId = null;
        await Should.ThrowAsync<BusinessException>(() => service.CreateAsync(input));
        input.OperationId = operation;
        var question = await service.CreateAsync(input);
        question.Id.ShouldBe(operation!.Value);
        input.CurriculumScore = 10.00m;
        (await service.CreateAsync(input)).Id.ShouldBe(question.Id);
        input.Stem = "另一道题";
        (await Should.ThrowAsync<BusinessException>(() => service.CreateAsync(input))).Code.ShouldBe("KnowPulse:Education:QuestionCreateOperationConflict");
        input.Stem = question.Stem;
        using (User(f.Owner)) await Should.ThrowAsync<AbpAuthorizationException>(() => service.CreateAsync(input));
        await WithUnitOfWorkAsync(async () =>
        {
            var selection = await Repo<TeacherTextbookSelection>().GetAsync(f.ReaderSelection);
            selection.End(DateTime.UtcNow);
            await Repo<TeacherTextbookSelection>().UpdateManyAsync([selection], autoSave: true);
        });
        (await service.CreateAsync(input)).Curriculum!.TeacherTextbookSelectionId.ShouldBe(f.ReaderSelection);
        (await service.GetAsync(question.Id)).Stem.ShouldBe(question.Stem);
        input.OperationId = Guid.NewGuid();
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.CreateAsync(input));
        (await WithUnitOfWorkAsync(() => Repo<Question>().GetCountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Seven_Marks_Three_And_Four_Should_Confirm_And_Mixed_Marks_Should_Freeze_Per_Question()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var questions = GetRequiredService<IQuestionAppService>();
        var input = Manual(f);
        input.CurriculumScore = 7;
        var seven = await questions.CreateAsync(input);
        var confirmation = Confirm(f, seven);
        confirmation.Score = 7;
        var firstWeight = 3m / 7m;
        confirmation.PointWeights = [new() { TextbookKnowledgePointMappingId = f.MappingA, Weight = firstWeight, Score = 3 },
            new() { TextbookKnowledgePointMappingId = f.MappingB, Weight = 1 - firstWeight, Score = 4 }];
        seven = await questions.ConfirmCurriculumAsync(seven.Id, confirmation);
        seven.CurriculumPointWeights.Sum(x => x.Weight).ShouldBe(1);
        seven.CurriculumScoringPoints.Single(x => x.TextbookKnowledgePointMappingId == f.MappingA).Score.ShouldBe(3);
        seven.CurriculumScoringPoints.Single(x => x.TextbookKnowledgePointMappingId == f.MappingB).Score.ShouldBe(4);
        seven.CurriculumScoringPoints.Select(x => x.KnowledgePointId).Distinct().Count().ShouldBe(2);
        seven.CurriculumScoringPoints.All(x => !string.IsNullOrWhiteSpace(x.KnowledgePointName) && !string.IsNullOrWhiteSpace(x.ChapterTitle)).ShouldBeTrue();
        seven = await questions.SubmitReviewAsync(seven.Id);
        seven = await questions.ApproveAsync(seven.Id, Review(seven));
        var fiveInput = Manual(f);
        fiveInput.CurriculumScore = 5;
        var five = await questions.CreateAsync(fiveInput);
        var fiveConfirm = Confirm(f, five);
        fiveConfirm.Score = 5;
        fiveConfirm.PointWeights = [new() { TextbookKnowledgePointMappingId = f.MappingA, Weight = 1, Score = 5 }];
        five = await questions.ConfirmCurriculumAsync(five.Id, fiveConfirm);
        five = await questions.SubmitReviewAsync(five.Id);
        five = await questions.ApproveAsync(five.Id, Review(five));
        var paper = Paper(f, seven.Id);
        paper.FixedQuestionIds.Add(five.Id);
        paper.Rules = [];
        var saved = await GetRequiredService<IExamAppService>().SavePaperAsync(paper);
        saved.TotalScore.ShouldBe(12);
        saved.Questions.Select(x => x.Score).ShouldBe(new[] { 7m, 5m });
        foreach (var row in saved.Questions)
        {
            row.CurriculumPointWeights.Sum(x => x.Score).ShouldBe(row.Score);
            row.CurriculumScoringPoints.Sum(x => x.Score).ShouldBe(row.Score);
            row.Curriculum!.TextbookVersionId.ShouldBe(f.BookId);
        }
        var invalid = Confirm(f, seven);
        invalid.Score = 7;
        invalid.PointWeights = [new() { TextbookKnowledgePointMappingId = f.MappingA, Weight = firstWeight, Score = 3.01m },
            new() { TextbookKnowledgePointMappingId = f.MappingB, Weight = 1 - firstWeight, Score = 3.99m }];
        var current = await questions.GetAsync(seven.Id);
        invalid.ConcurrencyStamp = current.ConcurrencyStamp;
        await Should.ThrowAsync<BusinessException>(() => questions.ConfirmCurriculumAsync(seven.Id, invalid));
        invalid.Score = 7.001m;
        await Should.ThrowAsync<BusinessException>(() => questions.ConfirmCurriculumAsync(seven.Id, invalid));
    }

    [Fact]
    public async Task Formal_Curriculum_Query_Should_Page_Only_Confirmed_Exact_Scoring_Mappings()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var service = GetRequiredService<IQuestionAppService>();
        Guid expected = Guid.Empty;
        foreach (var mapping in new[] { f.MappingA, f.MappingB })
        {
            var question = await service.CreateAsync(Manual(f));
            var confirmation = Confirm(f, question);
            confirmation.PointWeights = [new() { TextbookKnowledgePointMappingId = mapping, Weight = 1, Score = 10 }];
            await service.ConfirmCurriculumAsync(question.Id, confirmation);
            question = await service.SubmitReviewAsync(question.Id);
            await service.ApproveAsync(question.Id, Review(question));
            if (mapping == f.MappingA) expected = question.Id;
        }
        var legacyInput = Manual(f);
        legacyInput.CurriculumScope = null;
        var legacy = await service.CreateAsync(legacyInput);
        await service.SubmitReviewAsync(legacy.Id);
        await service.ApproveAsync(legacy.Id, new ReviewQuestionDto());
        await service.CreateAsync(Manual(f)); // unconfirmed curriculum draft does not enter the formal selector
        var result = await service.GetFormalListAsync(new GetQuestionListInput
        {
            CurriculumScope = new() { TeacherTextbookSelectionId = f.ReaderSelection, TextbookKnowledgePointMappingIds = [f.MappingA] },
            MaxResultCount = 1
        });
        result.TotalCount.ShouldBe(1);
        result.Items.Single().Id.ShouldBe(expected);
    }

    [Fact]
    public async Task An_Older_Editor_Stamp_Should_Not_Overwrite_Content_Saved_From_Another_View()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var questions = GetRequiredService<IQuestionAppService>();
        var question = await questions.CreateAsync(Manual(f));
        var firstView = Draft(question);
        var olderView = Draft(question);
        firstView.Analysis = "另一视图已经保存的解析";
        var saved = await GetRequiredService<IQuestionWorkflowAppService>().SaveDraftAsync(question.Id, firstView);
        saved.ConcurrencyStamp.ShouldNotBe(question.ConcurrencyStamp);
        olderView.Analysis = "旧视图的覆盖";
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => GetRequiredService<IQuestionWorkflowAppService>().SaveDraftAsync(question.Id, olderView));
        (await questions.GetAsync(question.Id)).Analysis.ShouldBe(firstView.Analysis);
    }

    [Fact]
    public async Task Reference_Reads_Should_Not_Rotate_Curriculum_Source_Or_Owner_Root_Stamps()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Reader, "只读引用");
        using var reader = User(f.Reader);
        var binding = await BindAsync(source, f.MappingA);
        var question = await ApprovedManualAsync(f);
        var before = await SourceStampsAsync(f, source, binding, question.Id);
        var snapshot = await GetRequiredService<EducationCurriculumAccessGuard>().ResolveAsync(Both(f));
        await GetRequiredService<EducationMaterialScope>().ValidateSourceAsync(snapshot, source.KnowledgeItemId, source.KnowledgeVersionId, source.Id, [binding], [f.MappingA]);
        await WithUnitOfWorkAsync(() => GetRequiredService<KnowledgeSourceAccessGuard>().EnsureSourceAsync(source.KnowledgeItemId, source.KnowledgeVersionId, source.Id, requireUsable: true));
        await WithUnitOfWorkAsync(async () => await GetRequiredService<QuestionCurriculumAccessGuard>().EnsureAsync(await Repo<Question>().GetAsync(question.Id)));
        _vectors.SearchResults.Add(Hit(source));
        (await _retriever.SearchAsync(Search(f, KnowledgeSearchScope.PersonalLibrary))).Count.ShouldBe(1);
        var after = await SourceStampsAsync(f, source, binding, question.Id);
        after.OrderBy(x => x.Key).ShouldBe(before.OrderBy(x => x.Key));
    }

    [Fact]
    public async Task Ending_Original_Binding_Should_Contend_With_The_Captured_Source_Version_Before_New_Use()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Reader, "原版本屏障");
        using var reader = User(f.Reader);
        var binding = await BindAsync(source, f.MappingA);
        var snapshot = await GetRequiredService<EducationCurriculumAccessGuard>().ResolveAsync(Both(f));
        using var use = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
        var captured = await Repo<KnowledgeVersion>().GetAsync(source.KnowledgeVersionId);
        var originalStamp = captured.ConcurrencyStamp;
        await EndBindingAsync(binding); // actual application service uses its own transaction and the same version barrier
        using (var read = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false))
        {
            (await Repo<KnowledgeVersion>().GetAsync(source.KnowledgeVersionId)).ConcurrencyStamp.ShouldNotBe(originalStamp);
            await read.CompleteAsync();
        }
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => GetRequiredService<EducationMaterialScope>().ValidateSourceAsync(snapshot,
            source.KnowledgeItemId, source.KnowledgeVersionId, source.Id, [binding], [f.MappingA], forUpdate: true));
        // Do not complete this failed use. This SQLite test verifies the token conflict; production transactions keep its writes atomic.
    }

    [Fact]
    public async Task Exact_Sharing_Provenance_Should_Not_Borrow_Another_Schools_Live_Authorization_After_Revocation()
    {
        var f = await FixtureAsync();
        var second = await FixtureAsync();
        await WithUnitOfWorkAsync(async () =>
        {
            foreach (var user in new[] { f.Owner, f.Reader })
                await Repo<SchoolMembership>().InsertAsync(new SchoolMembership(Guid.NewGuid(), null, second.SchoolId, user,
                    SchoolMemberRole.Teacher, DateTime.UtcNow.AddDays(-1)), autoSave: true);
        });
        var source = await SourceAsync(f.Owner, "两校共享");
        SchoolKnowledgeShareDto first;
        using (User(f.Owner))
        {
            first = await _sharing.ShareVersionAsync(Share(f, source));
            var next = Share(f, source);
            next.SchoolId = second.SchoolId;
            await _sharing.ShareVersionAsync(next);
            await _sharing.RevokeAsync(first.Id);
        }
        using var reader = User(f.Reader);
        var guard = GetRequiredService<KnowledgeSourceAccessGuard>();
        (await WithUnitOfWorkAsync(() => guard.EnsureSourceAsync(source.KnowledgeItemId, source.KnowledgeVersionId, source.Id, requireUsable: true)))!.Id.ShouldBe(source.KnowledgeItemId);
        await Should.ThrowAsync<AbpAuthorizationException>(() => WithUnitOfWorkAsync(() => guard.EnsureSourceAsync(source.KnowledgeItemId,
            source.KnowledgeVersionId, source.Id, requireUsable: true, schoolKnowledgeShareId: first.Id)));
        using var use = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
        await Should.ThrowAsync<AbpAuthorizationException>(() => guard.EnsureSourceAsync(source.KnowledgeItemId,
            source.KnowledgeVersionId, source.Id, requireUsable: true, forUpdate: true, schoolKnowledgeShareId: first.Id));
    }

    private Task<Dictionary<string, string>> SourceStampsAsync(Fixture f, KnowledgeChunk source, Guid bindingId, Guid questionId) => WithUnitOfWorkAsync(async () => new Dictionary<string, string>
    {
        ["school"] = (await Repo<School>().GetAsync(f.SchoolId)).ConcurrencyStamp,
        ["selection"] = (await Repo<TeacherTextbookSelection>().GetAsync(f.ReaderSelection)).ConcurrencyStamp,
        ["book"] = (await Repo<TextbookVersion>().GetAsync(f.BookId)).ConcurrencyStamp,
        ["account"] = (await Repo<IdentityUser>().GetAsync(f.Reader)).ConcurrencyStamp,
        ["item"] = (await Repo<KnowledgeItem>().GetAsync(source.KnowledgeItemId)).ConcurrencyStamp,
        ["version"] = (await Repo<KnowledgeVersion>().GetAsync(source.KnowledgeVersionId)).ConcurrencyStamp,
        ["library"] = (await Repo<KnowledgeLibrary>().GetAsync(f.Reader)).ConcurrencyStamp,
        ["chunk"] = (await Repo<KnowledgeChunk>().GetAsync(source.Id)).ConcurrencyStamp,
        ["job"] = (await Repo<KnowledgeIndexJob>().GetAsync(source.KnowledgeIndexJobId)).ConcurrencyStamp,
        ["binding"] = (await Repo<KnowledgeMaterialCurriculumBinding>().GetAsync(bindingId)).ConcurrencyStamp,
        ["question"] = (await Repo<Question>().GetAsync(questionId)).ConcurrencyStamp
    });
}
