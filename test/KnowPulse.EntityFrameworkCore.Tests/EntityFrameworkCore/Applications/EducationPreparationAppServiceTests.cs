using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.QuestionBank;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class EducationPreparationAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IEducationPreparationAppService _service;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public EducationPreparationAppServiceTests() => _service = GetRequiredService<IEducationPreparationAppService>();
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => GetRequiredService<IRepository<T, Guid>>();

    [Fact]
    public async Task Organizing_An_Own_Material_Does_Not_Require_A_Current_Teaching_Assignment()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Owner);
        await WithUnitOfWorkAsync(async () =>
        {
            var assignment = await Repo<TeachingAssignment>().GetAsync(f.OwnerAssignment);
            assignment.EndAt(DateTime.UtcNow);
            await Repo<TeachingAssignment>().UpdateManyAsync([assignment], autoSave: true);
        });
        using var owner = User(f.Owner);
        var input = Binding(source, f.MappingA);
        var binding = await _service.ConfirmBindingAsync(input);
        binding.Curriculum.TeachingAssignmentId.ShouldBe(Guid.Empty);
        binding.Curriculum.TeacherTextbookSelectionId.ShouldBe(Guid.Empty);
        (await _service.ConfirmBindingAsync(input)).Id.ShouldBe(binding.Id);
        (await _service.GetMyBindingsAsync(new GetMaterialBindingListInput())).TotalCount.ShouldBe(1);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CreatePreparationAsync(Create(f, binding.Id)));
    }
    [Fact]
    public async Task Whole_Confirmation_And_Chunk_Version_Pair_Are_Actual_Validated_Choices()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Owner);
        var other = await SourceAsync(f.Owner);
        using var owner = User(f.Owner);
        var whole = Binding(source, f.MappingA); whole.KnowledgeChunkId = null;
        (await Should.ThrowAsync<BusinessException>(() => _service.ConfirmBindingAsync(whole))).Code.ShouldBe(EducationPreparationErrorCodes.WholeMaterialConfirmationRequired);
        whole.ConfirmWholeMaterialForThisMapping = true;
        (await _service.ConfirmBindingAsync(whole)).WholeMaterialAppliesToMapping.ShouldBeTrue();
        var forged = Binding(source, f.MappingA); forged.KnowledgeChunkId = other.Id;
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.ConfirmBindingAsync(forged));
        var options = await _service.GetMaterialChunksAsync(source.KnowledgeVersionId, new PagedResultRequestDto());
        options.Items.Single().Id.ShouldBe(source.Id);
    }
    [Fact]
    public async Task Confirm_And_End_Advance_The_Source_Barrier_While_End_Exact_Retry_Does_Not()
    {
        var f = await FixtureAsync(); var source = await SourceAsync(f.Owner);
        var original = await VersionStampAsync(source.KnowledgeVersionId);
        using var owner = User(f.Owner);
        var binding = await _service.ConfirmBindingAsync(Binding(source, f.MappingA));
        var confirmed = await VersionStampAsync(source.KnowledgeVersionId); confirmed.ShouldNotBe(original);
        var ended = await _service.EndBindingAsync(binding.Id, new EndMaterialBindingDto { ConcurrencyStamp = binding.ConcurrencyStamp });
        var endedStamp = await VersionStampAsync(source.KnowledgeVersionId); endedStamp.ShouldNotBe(confirmed);
        ended.EndedAtUtc.ShouldNotBeNull();
        (await _service.EndBindingAsync(binding.Id, new EndMaterialBindingDto { ConcurrencyStamp = binding.ConcurrencyStamp })).EndedAtUtc.ShouldBe(ended.EndedAtUtc);
        (await VersionStampAsync(source.KnowledgeVersionId)).ShouldBe(endedStamp);
    }
    [Fact]
    public async Task Ending_A_Binding_Rejects_An_Already_Captured_Source_Write_At_The_Database_Barrier()
    {
        var f = await FixtureAsync(); var source = await SourceAsync(f.Owner);
        using var owner = User(f.Owner);
        var binding = await _service.ConfirmBindingAsync(Binding(source, f.MappingA));
        var captured = await WithUnitOfWorkAsync(() => Repo<KnowledgeVersion>().GetAsync(source.KnowledgeVersionId));
        await _service.EndBindingAsync(binding.Id, new EndMaterialBindingDto { ConcurrencyStamp = binding.ConcurrencyStamp });
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => WithUnitOfWorkAsync(() => Repo<KnowledgeVersion>().UpdateManyAsync([captured], autoSave: true)));
        await Should.ThrowAsync<BusinessException>(() => _service.CreatePreparationAsync(Create(f, binding.Id)));
    }
    [Fact]
    public async Task School_Manager_Role_Does_Not_Grant_Access_To_Another_Teachers_Private_Binding()
    {
        var f = await FixtureAsync(); var source = await SourceAsync(f.Owner);
        await WithUnitOfWorkAsync(() => Repo<SchoolMembership>().InsertAsync(new SchoolMembership(Guid.NewGuid(), null,
            f.School, f.Reader, SchoolMemberRole.Manager, DateTime.UtcNow.AddDays(-1)), autoSave: true));
        KnowledgeMaterialCurriculumBindingDto binding;
        using (User(f.Owner)) binding = await _service.ConfirmBindingAsync(Binding(source, f.MappingA));
        using var manager = User(f.Reader);
        (await _service.GetMyBindingsAsync(new GetMaterialBindingListInput())).Items.ShouldBeEmpty();
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.EndBindingAsync(binding.Id, new EndMaterialBindingDto { ConcurrencyStamp = binding.ConcurrencyStamp }));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.ConfirmBindingAsync(Binding(source, f.MappingA)));
    }
    [Fact]
    public async Task Objectives_Material_And_Private_Reflection_Are_Immutable_And_Save_Is_Cas_And_Idempotent()
    {
        var f = await FixtureAsync(); var source = await SourceAsync(f.Owner);
        using var owner = User(f.Owner);
        var binding = await _service.ConfirmBindingAsync(Binding(source, f.MappingA));
        var create = Create(f, binding.Id);
        var first = await _service.CreatePreparationAsync(create);
        (await _service.CreatePreparationAsync(create)).Id.ShouldBe(first.Id);
        var save = Save(first, f, binding.Id); save.Objectives = "修订教学目标"; save.Reflection = "修订反思";
        var second = await _service.SavePreparationAsync(first.Id, save);
        second.CurrentRevision.RevisionNumber.ShouldBe(2);
        second.ConcurrencyStamp.ShouldNotBe(first.ConcurrencyStamp);
        (await _service.SavePreparationAsync(first.Id, save)).CurrentRevision.Id.ShouldBe(second.CurrentRevision.Id);
        var history = await _service.GetRevisionAsync(first.Id, first.CurrentRevision.Id);
        history.Objectives.ShouldBe(create.Objectives); history.Reflection.ShouldBe(create.Reflection);
        history.References.Single().KnowledgeVersionId.ShouldBe(source.KnowledgeVersionId);
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.SavePreparationAsync(first.Id, Save(first, f, binding.Id)));
        var latestSave = Save(second, f, binding.Id); latestSave.Objectives = "第三次教学目标";
        var third = await _service.SavePreparationAsync(first.Id, latestSave);
        (await _service.SavePreparationAsync(first.Id, save)).CurrentRevision.Id.ShouldBe(third.CurrentRevision.Id);
        (await _service.GetRevisionsAsync(first.Id, new PagedResultRequestDto())).TotalCount.ShouldBe(3);
    }
    [Fact]
    public async Task A_Different_Mapping_And_Foreign_Selection_Cannot_Use_An_Own_Binding()
    {
        var f = await FixtureAsync(); var source = await SourceAsync(f.Owner);
        using var owner = User(f.Owner);
        var binding = await _service.ConfirmBindingAsync(Binding(source, f.MappingA));
        var wrongMapping = Create(f, binding.Id); wrongMapping.Scope.TextbookKnowledgePointMappingIds = [f.MappingB];
        (await Should.ThrowAsync<BusinessException>(() => _service.CreatePreparationAsync(wrongMapping))).Code.ShouldBe(EducationPreparationErrorCodes.CurriculumMismatch);
        var wrongTeacher = Create(f, binding.Id); wrongTeacher.Scope.TeacherTextbookSelectionId = f.ReaderSelection;
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CreatePreparationAsync(wrongTeacher));
    }
    [Fact]
    public async Task Shared_Binding_Uses_Frozen_Allowed_Ids_And_End_Preserves_Only_Personal_History()
    {
        var f = await FixtureAsync(); var source = await SourceAsync(f.Owner);
        KnowledgeMaterialCurriculumBindingDto binding;
        SchoolKnowledgeShareDto share;
        using (User(f.Owner))
        {
            binding = await _service.ConfirmBindingAsync(Binding(source, f.MappingA));
            share = await GetRequiredService<IKnowledgeSchoolSharingAppService>().ShareVersionAsync(new CreateSchoolKnowledgeShareDto
            { IdempotencyKey = Guid.NewGuid(), KnowledgeItemId = source.KnowledgeItemId, KnowledgeVersionId = source.KnowledgeVersionId,
                SchoolId = f.School, Uses = SchoolKnowledgeShareUses.Read });
        }
        LessonPreparationDto preparation;
        using (User(f.Reader))
        {
            var options = await _service.GetReferenceOptionsAsync(new GetPreparationReferenceOptionsInput
            { Scope = Scope(f.ReaderSelection, f.MappingA), Kind = LessonPreparationReferenceKind.Material, IncludeSchoolShared = true });
            options.Items.Single().Id.ShouldBe(binding.Id);
            var input = Create(f, binding.Id); input.Scope = Scope(f.ReaderSelection, f.MappingA);
            preparation = await _service.CreatePreparationAsync(input);
        }
        using (User(f.Owner)) await GetRequiredService<IKnowledgeSchoolSharingAppService>().RevokeAsync(share.Id);
        using (User(f.Reader))
        {
            var retained = await _service.GetPreparationAsync(preparation.Id);
            retained.CurrentRevision.References.Single().Title.ShouldBe("本人资料");
            retained.CurrentRevision.References.Single().CanUseNow.ShouldBeFalse();
            var copy = new CopyLessonPreparationDto { IdempotencyKey = Guid.NewGuid(), Title = "共享失效复制", Scope = Scope(f.ReaderSelection, f.MappingA), BindingIds = [binding.Id] };
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CopyPreparationAsync(preparation.CurrentRevision.Id, copy));
        }
        using (User(f.Owner)) await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetPreparationAsync(preparation.Id));
    }
    [Fact]
    public async Task Historical_Revision_Is_Readable_After_Selection_Ends_And_Copy_Reconfirms_Current_Scope()
    {
        var f = await FixtureAsync();
        using var owner = User(f.Owner);
        var preparation = await _service.CreatePreparationAsync(Create(f));
        await WithUnitOfWorkAsync(async () =>
        {
            var selection = await Repo<TeacherTextbookSelection>().GetAsync(f.OwnerSelection); selection.End(DateTime.UtcNow);
            await Repo<TeacherTextbookSelection>().UpdateManyAsync([selection], autoSave: true);
        });
        var historical = await _service.GetPreparationAsync(preparation.Id);
        historical.CurrentRevision.Curriculum.Mappings.Single().KnowledgePointName.ShouldBe("原知识点0");
        historical.CurrentRevision.NeedsCurriculumConfirmation.ShouldBeTrue();
        var copy = new CopyLessonPreparationDto { IdempotencyKey = Guid.NewGuid(), Title = "复制内容", Scope = Scope(f.OwnerSelection, f.MappingA) };
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CopyPreparationAsync(preparation.CurrentRevision.Id, copy));
        var current = await GetRequiredService<IEducationCurriculumAppService>().SelectTextbookAsync(f.OwnerAssignment,
            new SelectTeacherTextbookDto { IdempotencyKey = Guid.NewGuid(), TextbookVersionId = f.Book });
        copy.Scope.TeacherTextbookSelectionId = current.Id;
        var newPreparation = await _service.CopyPreparationAsync(preparation.CurrentRevision.Id, copy);
        newPreparation.CopiedFromRevisionId.ShouldBe(preparation.CurrentRevision.Id);
        newPreparation.CurrentRevision.Objectives.ShouldBe(historical.CurrentRevision.Objectives);
        newPreparation.CurrentRevision.References.ShouldBeEmpty();
        (await _service.GetMyPreparationsAsync(new GetLessonPreparationListInput { ChapterId = f.ChapterA })).TotalCount.ShouldBe(2);
    }
    [Fact]
    public async Task An_Explicit_Reflection_Copy_Creates_Only_An_Own_Private_Draft_And_Is_Idempotent()
    {
        var f = await FixtureAsync();
        using var owner = User(f.Owner);
        var preparation = await _service.CreatePreparationAsync(Create(f));
        var input = new CopyReflectionToKnowledgeDto { IdempotencyKey = Guid.NewGuid(), Title = "课后反思" };
        var itemId = await _service.CopyReflectionToMyLibraryAsync(preparation.Id, preparation.CurrentRevision.Id, input);
        (await _service.CopyReflectionToMyLibraryAsync(preparation.Id, preparation.CurrentRevision.Id, input)).ShouldBe(itemId);
        await WithUnitOfWorkAsync(async () =>
        {
            var item = await Repo<KnowledgeItem>().GetAsync(itemId);
            item.OwnerUserId.ShouldBe(f.Owner); item.LibraryId.ShouldBe(f.Owner); item.MaterialType.ShouldBe(PersonalMaterialType.Reflection);
            item.CurrentPublishedVersionId.ShouldBeNull();
            var version = (await Repo<KnowledgeVersion>().GetListAsync(x => x.KnowledgeItemId == itemId)).Single();
            version.Status.ShouldBe(KnowledgeVersionStatus.Draft); version.ContentText.ShouldBe(preparation.CurrentRevision.Reflection);
            (await Repo<SchoolKnowledgeShare>().GetListAsync(x => x.KnowledgeItemId == itemId)).ShouldBeEmpty();
        });
        using var reader = User(f.Reader);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetPreparationAsync(preparation.Id));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CopyReflectionToMyLibraryAsync(preparation.Id, preparation.CurrentRevision.Id, input));
    }
    [Fact]
    public async Task Actual_Confirmed_Questions_And_Saved_Papers_Are_Referenced_Without_Creating_An_Exam_Plan()
    {
        var f = await FixtureAsync();
        using var owner = User(f.Owner);
        var snapshot = await GetRequiredService<EducationCurriculumAccessGuard>().ResolveAsync(Scope(f.OwnerSelection, f.MappingA));
        var questionId = await QuestionAsync(f.Owner, snapshot);
        var paper = await GetRequiredService<IExamAppService>().SavePaperAsync(new GenerateExamPaperDto
        { OperationId = Guid.NewGuid(), Title = "真实课程试卷", CurriculumScope = Scope(f.OwnerSelection, f.MappingA), BuildMode = ExamPaperBuildMode.Fixed, FixedQuestionIds = [questionId] });
        (await GetRequiredService<IExamAppService>().GetSavedPaperAsync(paper.Id)).Id.ShouldBe(paper.Id);
        var input = Create(f); input.QuestionIds = [questionId]; input.ExamPaperIds = [paper.Id];
        var preparation = await _service.CreatePreparationAsync(input);
        preparation.CurrentRevision.References.Select(x => x.Kind).Order().ShouldBe(new[] { LessonPreparationReferenceKind.Question, LessonPreparationReferenceKind.ExamPaper });
        (await _service.GetReferenceOptionsAsync(new GetPreparationReferenceOptionsInput { Scope = input.Scope, Kind = LessonPreparationReferenceKind.ExamPaper })).Items.Single().Id.ShouldBe(paper.Id);
        (await WithUnitOfWorkAsync(() => Repo<ExamPlan>().GetListAsync(x => x.PaperId == paper.Id))).ShouldBeEmpty();
        using var reader = User(f.Reader);
        var foreign = Create(f); foreign.Scope = Scope(f.ReaderSelection, f.MappingA); foreign.QuestionIds = [questionId];
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CreatePreparationAsync(foreign));
    }
    [Fact]
    public async Task Reading_Revisions_And_Options_Does_Not_Wash_Root_Cas_Stamps()
    {
        var f = await FixtureAsync(); var source = await SourceAsync(f.Owner);
        using var owner = User(f.Owner);
        var binding = await _service.ConfirmBindingAsync(Binding(source, f.MappingA));
        var preparation = await _service.CreatePreparationAsync(Create(f, binding.Id));
        var versionStamp = await VersionStampAsync(source.KnowledgeVersionId);
        var bookStamp = await WithUnitOfWorkAsync(async () => (await Repo<TextbookVersion>().GetAsync(f.Book)).ConcurrencyStamp);
        await _service.GetPreparationAsync(preparation.Id);
        await _service.GetRevisionsAsync(preparation.Id, new PagedResultRequestDto());
        await _service.GetMyBindingsAsync(new GetMaterialBindingListInput());
        await _service.GetReferenceOptionsAsync(new GetPreparationReferenceOptionsInput { Scope = Scope(f.OwnerSelection, f.MappingA), Kind = LessonPreparationReferenceKind.Material });
        (await _service.GetPreparationAsync(preparation.Id)).ConcurrencyStamp.ShouldBe(preparation.ConcurrencyStamp);
        (await VersionStampAsync(source.KnowledgeVersionId)).ShouldBe(versionStamp);
        (await WithUnitOfWorkAsync(async () => (await Repo<TextbookVersion>().GetAsync(f.Book)).ConcurrencyStamp)).ShouldBe(bookStamp);
    }

    private Task<string> VersionStampAsync(Guid id) => WithUnitOfWorkAsync(async () => (await Repo<KnowledgeVersion>().GetAsync(id)).ConcurrencyStamp);
    private IDisposable User(Guid id) => GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(AbpClaimTypes.UserId, id.ToString()), new Claim(AbpClaimTypes.UserName, $"prep-{id:N}"), new Claim(AbpClaimTypes.Role, "admin")], "Test")));
    private static EducationCurriculumScopeDto Scope(Guid selection, Guid mapping) => new() { TeacherTextbookSelectionId = selection, TextbookKnowledgePointMappingIds = [mapping] };
    private static ConfirmKnowledgeMaterialCurriculumBindingDto Binding(KnowledgeChunk source, Guid mapping) => new()
    { IdempotencyKey = Guid.NewGuid(), KnowledgeVersionId = source.KnowledgeVersionId, KnowledgeChunkId = source.Id, TextbookKnowledgePointMappingId = mapping };
    private static CreateLessonPreparationDto Create(Fixture f, params Guid[] bindings) => new()
    { IdempotencyKey = Guid.NewGuid(), Title = "章节备课", Scope = Scope(f.OwnerSelection, f.MappingA), Objectives = "能解释第一章知识点", Reflection = "仅本人反思，不含学生报告", BindingIds = bindings.ToList() };
    private static SaveLessonPreparationDto Save(LessonPreparationDto preparation, Fixture f, params Guid[] bindings) => new()
    { IdempotencyKey = Guid.NewGuid(), ConcurrencyStamp = preparation.ConcurrencyStamp, Title = preparation.CurrentRevision.Title,
        Scope = Scope(f.OwnerSelection, f.MappingA), Objectives = preparation.CurrentRevision.Objectives, Reflection = preparation.CurrentRevision.Reflection, BindingIds = bindings.ToList() };
    private Task<KnowledgeChunk> SourceAsync(Guid owner) => WithUnitOfWorkAsync(async () =>
    {
        var item = new KnowledgeItem(Guid.NewGuid(), null, "本人资料", libraryId: owner, ownerUserId: owner);
        await Repo<KnowledgeItem>().InsertAsync(item, autoSave: true);
        var version = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1, "本人资料", "课件分块正文"); version.Publish(DateTime.UtcNow);
        await Repo<KnowledgeVersion>().InsertAsync(version, autoSave: true);
        item.SetCurrentPublishedVersion(version.Id); await Repo<KnowledgeItem>().UpdateManyAsync([item], autoSave: true);
        var job = new KnowledgeIndexJob(Guid.NewGuid(), null, item.Id, version.Id, KnowledgeIndexJobTriggerType.Publish); job.MarkIndexed(DateTime.UtcNow);
        await Repo<KnowledgeIndexJob>().InsertAsync(job, autoSave: true);
        return await Repo<KnowledgeChunk>().InsertAsync(new KnowledgeChunk(Guid.NewGuid(), null, item.Id, version.Id, job.Id, 0,
            "课件分块正文", Guid.NewGuid().ToString("N"), "本人资料", isPublishedSnapshot: true), autoSave: true);
    });
    private async Task<Guid> QuestionAsync(Guid owner, EducationCurriculumSnapshot snapshot)
    {
        using var currentOwner = User(owner);
        var service = GetRequiredService<IQuestionAppService>();
        var mappingId = snapshot.Mappings.Single().MappingId;
        var scope = Scope(snapshot.TeacherTextbookSelectionId, mappingId);
        var question = await service.CreateAsync(new CreateQuestionDto
        {
            OperationId = Guid.NewGuid(), Type = QuestionType.TrueFalse, Difficulty = QuestionDifficulty.Easy,
            Title = "实际课程题", Stem = "一加一等于二？", CurriculumScope = scope, CurriculumScore = 5,
            Answers = [new() { Value = "true", DisplayOrder = 1 }]
        });
        question = await service.ConfirmCurriculumAsync(question.Id, new ConfirmQuestionCurriculumDto
        {
            Scope = scope, ConcurrencyStamp = question.ConcurrencyStamp, Score = 5,
            PointWeights = [new() { TextbookKnowledgePointMappingId = mappingId, Weight = 1, Score = 5 }]
        });
        question = await service.SubmitReviewAsync(question.Id);
        question = await service.ApproveAsync(question.Id, new ReviewQuestionDto
        { OperationId = Guid.NewGuid(), ConcurrencyStamp = question.ConcurrencyStamp });
        question.ApprovedDefinitionRevisionId.ShouldNotBeNull();
        return question.Id;
    }
    private Task<Fixture> FixtureAsync() => WithUnitOfWorkAsync(async () =>
    {
        var owner = Guid.NewGuid(); var reader = Guid.NewGuid(); var now = DateTime.UtcNow;
        foreach (var user in new[] { owner, reader })
        {
            await Repo<IdentityUser>().InsertAsync(new IdentityUser(user, $"prep-{user:N}", $"{user:N}@example.invalid"), autoSave: true);
            await Repo<KnowledgeLibrary>().InsertAsync(new KnowledgeLibrary(user, null), autoSave: true);
        }
        var school = new School(Guid.NewGuid(), null, $"S{Guid.NewGuid():N}", "备课测试初中");
        var subject = new EducationSubject(Guid.NewGuid(), null, $"M{Guid.NewGuid():N}", "数学");
        await Repo<School>().InsertAsync(school, autoSave: true); await Repo<EducationSubject>().InsertAsync(subject, autoSave: true);
        var term = new AcademicTerm(Guid.NewGuid(), null, school.Id, "T1", "当前学期", now.AddDays(-3), now.AddDays(50));
        await Repo<AcademicTerm>().InsertAsync(term, autoSave: true);
        var schoolClass = new SchoolClass(Guid.NewGuid(), null, school.Id, term.Id, "C1", "七年一班", 7);
        await Repo<SchoolClass>().InsertAsync(schoolClass, autoSave: true);
        var book = new TextbookVersion(Guid.NewGuid(), null, school.Id, subject.Id, "MATH", 1, 7, "数学教材", "测试出版社", "测试版", "上册");
        await Repo<TextbookVersion>().InsertAsync(book, autoSave: true);
        var mappings = new List<Guid>(); var chapters = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var chapter = new TextbookChapter(Guid.NewGuid(), null, book.Id, null, $"CH{i}", $"第{i + 1}章", i);
            var point = new CourseKnowledgePoint(Guid.NewGuid(), null, school.Id, subject.Id, $"KP{i}");
            var revision = new CourseKnowledgePointRevision(Guid.NewGuid(), null, point.Id, point.AdvanceRevision(), $"原知识点{i}", null);
            await Repo<TextbookChapter>().InsertAsync(chapter, autoSave: true); await Repo<CourseKnowledgePoint>().InsertAsync(point, autoSave: true);
            await Repo<CourseKnowledgePointRevision>().InsertAsync(revision, autoSave: true);
            var mapping = new TextbookKnowledgePointMapping(Guid.NewGuid(), null, book.Id, chapter.Id, point.Id, revision.Id, owner, now);
            await Repo<TextbookKnowledgePointMapping>().InsertAsync(mapping, autoSave: true); mappings.Add(mapping.Id); chapters.Add(chapter.Id);
        }
        book.Publish(now); await Repo<TextbookVersion>().UpdateManyAsync([book], autoSave: true);
        var assignments = new List<Guid>(); var selections = new List<Guid>();
        foreach (var user in new[] { owner, reader })
        {
            await Repo<SchoolMembership>().InsertAsync(new SchoolMembership(Guid.NewGuid(), null, school.Id, user, SchoolMemberRole.Teacher, term.StartsAtUtc), autoSave: true);
            var assignment = new TeachingAssignment(Guid.NewGuid(), null, school.Id, term.Id, schoolClass.Id, subject.Id, user, term.StartsAtUtc, term.EndsAtUtc);
            await Repo<TeachingAssignment>().InsertAsync(assignment, autoSave: true); assignments.Add(assignment.Id);
            var selection = new TeacherTextbookSelection(Guid.NewGuid(), null, school.Id, user, assignment.Id, book.Id, now);
            await Repo<TeacherTextbookSelection>().InsertAsync(selection, autoSave: true); selections.Add(selection.Id);
        }
        return new Fixture(owner, reader, school.Id, book.Id, assignments[0], selections[0], selections[1], mappings[0], mappings[1], chapters[0]);
    });
    private sealed record Fixture(Guid Owner, Guid Reader, Guid School, Guid Book, Guid OwnerAssignment, Guid OwnerSelection, Guid ReaderSelection, Guid MappingA, Guid MappingB, Guid ChapterA);
}
