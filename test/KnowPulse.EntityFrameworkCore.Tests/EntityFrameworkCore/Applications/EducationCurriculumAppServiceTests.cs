using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using KnowPulse.Education;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class EducationCurriculumAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IEducationCurriculumAppService _service;
    private readonly ICurrentPrincipalAccessor _principal;
    public EducationCurriculumAppServiceTests()
    {
        _service = GetRequiredService<IEducationCurriculumAppService>();
        _principal = GetRequiredService<ICurrentPrincipalAccessor>();
    }
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => GetRequiredService<IRepository<T, Guid>>();

    [Fact]
    public async Task Read_And_Related_Writes_Should_Not_Rotate_Unedited_Curriculum_Root_Stamps()
    {
        var f = await FixtureAsync();
        using var manager = User(f.Manager);
        var point = await PointAsync(f);
        (await WithUnitOfWorkAsync(async () => (await Repo<CourseKnowledgePoint>().GetAsync(point.Id)).ConcurrencyStamp))
            .ShouldBe(point.ConcurrencyStamp, "Point creation must return its persisted stamp.");
        await _service.GetKnowledgePointsAsync(f.School.Id, f.Subject.Id, new PagedResultRequestDto());
        await _service.GetKnowledgePointsAsync(f.School.Id, f.Subject.Id, new PagedResultRequestDto());
        (await WithUnitOfWorkAsync(async () => (await Repo<CourseKnowledgePoint>().GetAsync(point.Id)).ConcurrencyStamp))
            .ShouldBe(point.ConcurrencyStamp, "Reading points must not write a new stamp.");
        var book = await PublishedAsync(f, point.Revisions.Single().Id);
        (await WithUnitOfWorkAsync(async () => (await Repo<TextbookVersion>().GetAsync(book.Id)).ConcurrencyStamp))
            .ShouldBe(book.ConcurrencyStamp, "Publication must return its persisted stamp.");
        (await WithUnitOfWorkAsync(async () => (await Repo<CourseKnowledgePoint>().GetAsync(point.Id)).ConcurrencyStamp))
            .ShouldBe(point.ConcurrencyStamp, "Mapping another aggregate must not edit the point.");
        await _service.CreateKnowledgePointAsync(new CreateCourseKnowledgePointDto
        {
            IdempotencyKey = Guid.NewGuid(), SchoolId = f.School.Id, EducationSubjectId = f.Subject.Id,
            Code = "CHILD", Name = "子知识点", ParentRevisionId = point.Revisions.Single().Id
        });
        (await WithUnitOfWorkAsync(async () => (await Repo<CourseKnowledgePoint>().GetAsync(point.Id)).ConcurrencyStamp))
            .ShouldBe(point.ConcurrencyStamp, "Referencing a parent revision must not edit its stable point.");
        var revisedBook = await _service.ReviseAsync(book.Id, new ReviseTextbookDto { IdempotencyKey = Guid.NewGuid() });
        revisedBook.PreviousVersionId.ShouldBe(book.Id);
        (await WithUnitOfWorkAsync(async () => (await Repo<TextbookVersion>().GetAsync(book.Id)).ConcurrencyStamp))
            .ShouldBe(book.ConcurrencyStamp, "Creating another version must not edit the immutable source version.");
        await _service.GetTextbookAsync(book.Id);
        await _service.GetTextbookAsync(book.Id);
        (await WithUnitOfWorkAsync(async () => (await Repo<TextbookVersion>().GetAsync(book.Id)).ConcurrencyStamp))
            .ShouldBe(book.ConcurrencyStamp, "Reading a published book must not edit it.");
        using var teacher = User(f.Teacher);
        await _service.SelectTextbookAsync(f.Assignment.Id, new SelectTeacherTextbookDto
        {
            IdempotencyKey = Guid.NewGuid(), TextbookVersionId = book.Id
        });
        (await WithUnitOfWorkAsync(async () => (await Repo<TextbookVersion>().GetAsync(book.Id)).ConcurrencyStamp))
            .ShouldBe(book.ConcurrencyStamp, "Selecting a book must not edit its frozen catalog root.");
        using var managerAgain = User(f.Manager);
        var changedPoint = await _service.CreateKnowledgePointRevisionAsync(point.Id, new CreateKnowledgePointRevisionDto
        {
            IdempotencyKey = Guid.NewGuid(), ConcurrencyStamp = point.ConcurrencyStamp, Name = "真正修订的名称"
        });
        changedPoint.ConcurrencyStamp.ShouldNotBe(point.ConcurrencyStamp);
        (await WithUnitOfWorkAsync(async () => (await Repo<CourseKnowledgePoint>().GetAsync(point.Id)).ConcurrencyStamp))
            .ShouldBe(changedPoint.ConcurrencyStamp, "An actual revision must return its new persisted CAS stamp.");
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.CreateKnowledgePointRevisionAsync(point.Id,
            new CreateKnowledgePointRevisionDto { IdempotencyKey = Guid.NewGuid(), ConcurrencyStamp = point.ConcurrencyStamp, Name = "过期编辑" }));
    }

    [Fact]
    public async Task Published_Mappings_And_Teacher_Choice_History_Should_Survive_Point_And_Textbook_Revisions()
    {
        var f = await FixtureAsync();
        TextbookVersionDto original;
        CourseKnowledgePointDto point;
        using (User(f.Manager))
        {
            point = await PointAsync(f);
            original = await PublishedAsync(f, point.Revisions.Single().Id);
        }
        TeacherTextbookSelectionDto first;
        var operation = new SelectTeacherTextbookDto { IdempotencyKey = Guid.NewGuid(), TextbookVersionId = original.Id };
        using (User(f.Teacher))
        {
            var context = await _service.GetContextAsync(f.School.Id);
            context.CanManage.ShouldBeFalse();
            context.TeachingContexts.Single().Id.ShouldBe(f.Assignment.Id);
            first = await _service.SelectTextbookAsync(f.Assignment.Id, operation);
            (await _service.SelectTextbookAsync(f.Assignment.Id, operation)).Id.ShouldBe(first.Id);
        }
        TextbookVersionDto revised;
        using (User(f.Manager))
        {
            point = await _service.CreateKnowledgePointRevisionAsync(point.Id, new CreateKnowledgePointRevisionDto
            {
                IdempotencyKey = Guid.NewGuid(), ConcurrencyStamp = point.ConcurrencyStamp, Name = "知识点新名称"
            });
            var revisionOperation = new ReviseTextbookDto { IdempotencyKey = Guid.NewGuid() };
            revised = await _service.ReviseAsync(original.Id, revisionOperation);
            (await _service.ReviseAsync(original.Id, revisionOperation)).Id.ShouldBe(revised.Id);
            revised.CatalogRevision.ShouldBe(2);
            revised.PreviousVersionId.ShouldBe(original.Id);
            revised.Chapters.Single().Id.ShouldNotBe(original.Chapters.Single().Id);
            revised.Mappings.Single().KnowledgePointRevisionId.ShouldBe(original.Mappings.Single().KnowledgePointRevisionId);
            var save = Save(revised);
            save.Edition = "测试新物理版次";
            save.Mappings.Single().KnowledgePointRevisionId = point.Revisions.First().Id;
            revised = await _service.SaveDraftAsync(revised.Id, save);
            revised = await _service.PublishAsync(revised.Id, revised.ConcurrencyStamp);
            await _service.RetireAsync(original.Id, original.ConcurrencyStamp);
        }
        using (User(f.Teacher))
        {
            var second = await _service.SelectTextbookAsync(f.Assignment.Id, new SelectTeacherTextbookDto
            {
                IdempotencyKey = Guid.NewGuid(), TextbookVersionId = revised.Id
            });
            second.Id.ShouldNotBe(first.Id);
            var history = (await _service.GetMySelectionsAsync(f.School.Id)).Items;
            history.Count.ShouldBe(2);
            history.Single(x => x.Id == first.Id).EndedAtUtc.ShouldNotBeNull();
            history.Single(x => x.Id == first.Id).TextbookVersionId.ShouldBe(original.Id);
            history.Single(x => x.Id == second.Id).EndedAtUtc.ShouldBeNull();
            // A retry of the first confirmed operation must not reselect it or rewrite the current choice.
            (await _service.SelectTextbookAsync(f.Assignment.Id, operation)).Id.ShouldBe(first.Id);
            var frozen = await _service.GetTextbookAsync(original.Id);
            frozen.Edition.ShouldBe("测试物理版次");
            frozen.Mappings.Single().KnowledgePointName.ShouldBe("知识点原名称");
            frozen.Mappings.Single().RevisionNumber.ShouldBe(1);
            (await _service.GetTextbookAsync(revised.Id)).Mappings.Single().KnowledgePointName.ShouldBe("知识点新名称");
            (await _service.GetTextbooksAsync(f.School.Id, new GetTextbookListInput())).Items.Select(x => x.Id).ShouldNotContain(original.Id);
        }
    }

    [Fact]
    public async Task New_Confirmation_Of_Current_Book_Should_Persist_Its_Operation_And_Not_Reselect_On_Later_Retry()
    {
        var f = await FixtureAsync();
        TextbookVersionDto firstBook;
        TextbookVersionDto secondBook;
        using (User(f.Manager))
        {
            firstBook = await PublishedAsync(f);
            secondBook = await PublishedAsync(f);
        }
        using var teacher = User(f.Teacher);
        var initial = await _service.SelectTextbookAsync(f.Assignment.Id, new SelectTeacherTextbookDto
        {
            IdempotencyKey = Guid.NewGuid(), TextbookVersionId = firstBook.Id
        });
        var confirmation = new SelectTeacherTextbookDto { IdempotencyKey = Guid.NewGuid(), TextbookVersionId = firstBook.Id };
        var confirmed = await _service.SelectTextbookAsync(f.Assignment.Id, confirmation);
        confirmed.Id.ShouldBe(confirmation.IdempotencyKey);
        confirmed.Id.ShouldNotBe(initial.Id);
        var latest = await _service.SelectTextbookAsync(f.Assignment.Id, new SelectTeacherTextbookDto
        {
            IdempotencyKey = Guid.NewGuid(), TextbookVersionId = secondBook.Id
        });
        var replay = await _service.SelectTextbookAsync(f.Assignment.Id, confirmation);
        replay.Id.ShouldBe(confirmed.Id);
        replay.EndedAtUtc.ShouldNotBeNull();
        var history = (await _service.GetMySelectionsAsync(f.School.Id)).Items;
        history.Count.ShouldBe(3);
        history.Single(x => !x.EndedAtUtc.HasValue).Id.ShouldBe(latest.Id);
        history.Single(x => !x.EndedAtUtc.HasValue).TextbookVersionId.ShouldBe(secondBook.Id);
        confirmation.TextbookVersionId = secondBook.Id;
        (await Should.ThrowAsync<BusinessException>(() => _service.SelectTextbookAsync(f.Assignment.Id, confirmation)))
            .Code.ShouldBe(EducationCurriculumErrorCodes.ConflictingOperation);
        (await _service.GetMySelectionsAsync(f.School.Id)).Items.Single(x => !x.EndedAtUtc.HasValue).Id.ShouldBe(latest.Id);
    }

    [Fact]
    public async Task Admin_Role_Student_And_Another_School_Manager_Should_Not_Grant_Curriculum_Access()
    {
        var f = await FixtureAsync();
        var other = await FixtureAsync();
        var outsider = await UserAsync();
        TextbookVersionDto draft;
        using (User(f.Manager)) draft = await _service.CreateDraftAsync(Create(f));
        foreach (var user in new[] { f.Student, other.Manager, outsider })
        {
            using (User(user))
            {
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetContextAsync(f.School.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetTextbookAsync(draft.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetKnowledgePointsAsync(f.School.Id, f.Subject.Id, new PagedResultRequestDto()));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CreateDraftAsync(Create(f)));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetMySelectionsAsync(f.School.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.SelectTextbookAsync(f.Assignment.Id,
                    new SelectTeacherTextbookDto { IdempotencyKey = Guid.NewGuid(), TextbookVersionId = draft.Id }));
            }
        }
        using (User(f.Teacher))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CreateDraftAsync(Create(f)));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetTextbookAsync(draft.Id));
            (await _service.GetTextbooksAsync(f.School.Id, new GetTextbookListInput { IncludeUnpublished = true })).Items.ShouldBeEmpty();
        }
        using (User(f.Manager))
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.SelectTextbookAsync(f.Assignment.Id,
                new SelectTeacherTextbookDto { IdempotencyKey = Guid.NewGuid(), TextbookVersionId = draft.Id }));
    }

    [Fact]
    public async Task Current_Manager_Should_Also_Need_Curriculum_Function_Permission()
    {
        var f = await FixtureAsync();
        using var manager = User(f.Manager);
        var denied = DeniedService();
        (await WithUnitOfWorkAsync(() => denied.GetContextAsync(f.School.Id))).CanManage.ShouldBeFalse();
        await Should.ThrowAsync<AbpAuthorizationException>(() => denied.CreateDraftAsync(Create(f)));
        (await WithUnitOfWorkAsync(() => Repo<TextbookVersion>().CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Same_Name_And_Code_Across_Subjects_Should_Stay_Separate_And_Wrong_Subject_Mappings_Should_Fail()
    {
        var f = await FixtureAsync();
        using var manager = User(f.Manager);
        var math = await PointAsync(f);
        var english = await PointAsync(f, f.OtherSubject.Id);
        math.Id.ShouldNotBe(english.Id);
        math.Code.ShouldBe(english.Code);
        math.Revisions.Single().Name.ShouldBe(english.Revisions.Single().Name);
        var book = await _service.CreateDraftAsync(Create(f));
        var save = Save(book);
        save.Chapters.Add(new SaveTextbookChapterDto { Id = Guid.NewGuid(), Code = "CH1", Title = "测试章节" });
        save.Mappings.Add(new SaveTextbookMappingDto { ChapterId = save.Chapters.Single().Id, KnowledgePointRevisionId = english.Revisions.Single().Id });
        (await Should.ThrowAsync<BusinessException>(() => _service.SaveDraftAsync(book.Id, save)))
            .Code.ShouldBe(EducationCurriculumErrorCodes.ReferenceMismatch);
        (await _service.GetTextbookAsync(book.Id)).Chapters.ShouldBeEmpty();
        save.Mappings.Single().KnowledgePointRevisionId = math.Revisions.Single().Id;
        book = await _service.SaveDraftAsync(book.Id, save);
        book = await _service.PublishAsync(book.Id, book.ConcurrencyStamp);
        var englishBook = await PublishedAsync(f, english.Revisions.Single().Id, f.OtherSubject.Id);
        using var teacher = User(f.Teacher);
        (await Should.ThrowAsync<BusinessException>(() => _service.SelectTextbookAsync(f.Assignment.Id,
            new SelectTeacherTextbookDto { IdempotencyKey = Guid.NewGuid(), TextbookVersionId = englishBook.Id })))
            .Code.ShouldBe(EducationCurriculumErrorCodes.ReferenceMismatch);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.SelectTextbookAsync(f.OtherAssignment.Id,
            new SelectTeacherTextbookDto { IdempotencyKey = Guid.NewGuid(), TextbookVersionId = book.Id }));
    }

    [Fact]
    public async Task Knowledge_Point_Parents_Should_Reject_Other_Schools_And_Cycles_Through_Stable_Ids()
    {
        var f = await FixtureAsync();
        var other = await FixtureAsync();
        CourseKnowledgePointDto foreign;
        using (User(other.Manager)) foreign = await PointAsync(other);
        using var manager = User(f.Manager);
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateKnowledgePointAsync(new CreateCourseKnowledgePointDto
        {
            IdempotencyKey = Guid.NewGuid(), SchoolId = f.School.Id, EducationSubjectId = f.Subject.Id,
            Code = "BAD", Name = "跨校父节点", ParentRevisionId = foreign.Revisions.Single().Id
        }))).Code.ShouldBe(EducationCurriculumErrorCodes.ReferenceMismatch);
        var parent = await PointAsync(f);
        var child = await _service.CreateKnowledgePointAsync(new CreateCourseKnowledgePointDto
        {
            IdempotencyKey = Guid.NewGuid(), SchoolId = f.School.Id, EducationSubjectId = f.Subject.Id,
            Code = "CHILD", Name = "子知识点", ParentRevisionId = parent.Revisions.Single().Id
        });
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateKnowledgePointRevisionAsync(parent.Id, new CreateKnowledgePointRevisionDto
        {
            IdempotencyKey = Guid.NewGuid(), ConcurrencyStamp = parent.ConcurrencyStamp, Name = "循环关系", ParentRevisionId = child.Revisions.Single().Id
        }))).Code.ShouldBe(EducationCurriculumErrorCodes.ReferenceMismatch);
        (await _service.GetKnowledgePointsAsync(f.School.Id, f.Subject.Id, new PagedResultRequestDto())).Items.Single(x => x.Id == parent.Id)
            .CurrentRevisionNumber.ShouldBe(1);
    }

    [Fact]
    public async Task Draft_Save_Should_Reject_Stale_Child_Updates_And_Published_Edits_But_Allow_Explicit_Unmapped_Chapters()
    {
        var f = await FixtureAsync();
        using var manager = User(f.Manager);
        var book = await _service.CreateDraftAsync(Create(f));
        var save = Save(book);
        var parent = Guid.NewGuid();
        save.Chapters = [new() { Id = parent, Code = "CH1", Title = "父章节" }, new() { Id = Guid.NewGuid(), ParentChapterId = parent, Code = "CH2", Title = "子章节" }];
        book = await _service.SaveDraftAsync(book.Id, save);
        book.Chapters.ShouldAllBe(x => !x.HasConfirmedMapping);
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.SaveDraftAsync(book.Id, save));
        var swap = Save(book);
        swap.Chapters[0].Code = "CH2";
        swap.Chapters[1].Code = "CH1";
        book = await _service.SaveDraftAsync(book.Id, swap);
        book = await _service.PublishAsync(book.Id, book.ConcurrencyStamp);
        (await Should.ThrowAsync<BusinessException>(() => _service.SaveDraftAsync(book.Id, Save(book))))
            .Code.ShouldBe(EducationCurriculumErrorCodes.ImmutableVersion);
        var invalid = await _service.CreateDraftAsync(Create(f));
        var invalidSave = Save(invalid);
        invalidSave.Chapters = [new() { Id = Guid.NewGuid(), ParentChapterId = parent, Code = "BAD", Title = "跨版本父章节" }];
        (await Should.ThrowAsync<BusinessException>(() => _service.SaveDraftAsync(invalid.Id, invalidSave)))
            .Code.ShouldBe(EducationCurriculumErrorCodes.InvalidStructure);
        (await _service.GetTextbookAsync(invalid.Id)).Chapters.ShouldBeEmpty();
    }

    [Fact]
    public async Task Repeated_Draft_Save_Should_Replace_Confirmation_Without_Duplicate_Active_Mappings()
    {
        var f = await FixtureAsync();
        using var manager = User(f.Manager);
        var point = await PointAsync(f);
        var book = await _service.CreateDraftAsync(Create(f));
        var save = Save(book);
        var chapter = Guid.NewGuid();
        save.Chapters = [new() { Id = chapter, Code = "CH1", Title = "章节" }];
        save.Mappings = [new() { ChapterId = chapter, KnowledgePointRevisionId = point.Revisions.Single().Id }];
        book = await _service.SaveDraftAsync(book.Id, save);
        var initialMappingId = book.Mappings.Single().Id;
        book = await _service.SaveDraftAsync(book.Id, Save(book));
        book.Mappings.Count.ShouldBe(1);
        book.Mappings.Single().Id.ShouldNotBe(initialMappingId);
        (await WithUnitOfWorkAsync(() => Repo<TextbookKnowledgePointMapping>().CountAsync(x => x.TextbookVersionId == book.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task Creation_Operation_Should_Replay_After_Edits_And_Reject_Reuse_For_A_Different_Request()
    {
        var f = await FixtureAsync();
        using var manager = User(f.Manager);
        var input = Create(f);
        var book = await _service.CreateDraftAsync(input);
        var save = Save(book);
        save.Title = "后续修改的标题";
        book = await _service.SaveDraftAsync(book.Id, save);
        (await _service.CreateDraftAsync(input)).Title.ShouldBe(save.Title);
        input.Title = "复用旧操作创建另一本";
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateDraftAsync(input)))
            .Code.ShouldBe(EducationCurriculumErrorCodes.ConflictingOperation);
        (await WithUnitOfWorkAsync(() => Repo<TextbookVersion>().CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Retired_Book_And_Expired_Assignment_Should_Block_New_Choices_While_Own_History_Remains()
    {
        var f = await FixtureAsync();
        TextbookVersionDto book;
        using (User(f.Manager)) book = await PublishedAsync(f);
        using (User(f.Teacher))
            await _service.SelectTextbookAsync(f.Assignment.Id, new SelectTeacherTextbookDto { IdempotencyKey = Guid.NewGuid(), TextbookVersionId = book.Id });
        using (User(f.Manager)) await _service.RetireAsync(book.Id, book.ConcurrencyStamp);
        using (User(f.Teacher))
        {
            (await Should.ThrowAsync<BusinessException>(() => _service.SelectTextbookAsync(f.Assignment.Id,
                new SelectTeacherTextbookDto { IdempotencyKey = Guid.NewGuid(), TextbookVersionId = book.Id })))
                .Code.ShouldBe(EducationCurriculumErrorCodes.PublishedVersionRequired);
            (await _service.GetMySelectionsAsync(f.School.Id)).Items.Single().TextbookVersionId.ShouldBe(book.Id);
        }
        await WithUnitOfWorkAsync(async () =>
        {
            var assignment = await Repo<TeachingAssignment>().GetAsync(f.Assignment.Id);
            assignment.EndAt(DateTime.UtcNow.AddSeconds(-1));
            await Repo<TeachingAssignment>().UpdateAsync(assignment, autoSave: true);
        });
        using (User(f.Teacher))
        {
            (await _service.GetContextAsync(f.School.Id)).TeachingContexts.ShouldBeEmpty();
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.SelectTextbookAsync(f.Assignment.Id,
                new SelectTeacherTextbookDto { IdempotencyKey = Guid.NewGuid(), TextbookVersionId = book.Id }));
            (await _service.GetMySelectionsAsync(f.School.Id)).Items.Count.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Disabled_Tenant_Filter_Should_Not_Expose_Foreign_Catalogs_Or_Subjects()
    {
        var f = await FixtureAsync();
        var foreignTenant = Guid.NewGuid();
        var foreignManager = await UserAsync(foreignTenant);
        var foreignSchool = new School(Guid.NewGuid(), foreignTenant, "FOREIGN", "跨租户学校");
        var foreignSubject = new EducationSubject(Guid.NewGuid(), foreignTenant, "MATH", "数学");
        var foreignBook = new TextbookVersion(Guid.NewGuid(), foreignTenant, foreignSchool.Id, foreignSubject.Id,
            "BOOK", 1, 7, "跨租户教材", "测试出版社", "测试版次", "上册");
        using var filter = GetRequiredService<IDataFilter>().Disable<IMultiTenant>();
        await WithUnitOfWorkAsync(async () =>
        {
            await Repo<School>().InsertAsync(foreignSchool, autoSave: false);
            await Repo<EducationSubject>().InsertAsync(foreignSubject, autoSave: false);
            await Repo<TextbookVersion>().InsertAsync(foreignBook, autoSave: true);
        });
        using var manager = User(f.Manager);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetContextAsync(foreignSchool.Id));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetTextbookAsync(foreignBook.Id));
        (await _service.GetContextAsync(f.School.Id)).Subjects.Select(x => x.Id).ShouldNotContain(foreignSubject.Id);
        var create = Create(f);
        create.EducationSubjectId = foreignSubject.Id;
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateDraftAsync(create)))
            .Code.ShouldBe(EducationCurriculumErrorCodes.ReferenceMismatch);
        using var foreignAccount = User(foreignManager);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetContextAsync(f.School.Id));
    }

    private async Task<Fixture> FixtureAsync()
    {
        var manager = await UserAsync();
        var teacher = await UserAsync();
        var secondTeacher = await UserAsync();
        var student = await UserAsync();
        var now = DateTime.UtcNow;
        var school = new School(Guid.NewGuid(), null, $"S{Guid.NewGuid():N}", "测试学校");
        var term = new AcademicTerm(Guid.NewGuid(), null, school.Id, "T1", "测试学期", now.AddDays(-10), now.AddDays(90));
        var schoolClass = new SchoolClass(Guid.NewGuid(), null, school.Id, term.Id, "C1", "七年级一班", 7);
        var suffix = Guid.NewGuid().ToString("N");
        var subject = new EducationSubject(Guid.NewGuid(), null, "M" + suffix, "数学");
        var otherSubject = new EducationSubject(Guid.NewGuid(), null, "E" + suffix, "英语");
        var assignment = new TeachingAssignment(Guid.NewGuid(), null, school.Id, term.Id, schoolClass.Id, subject.Id, teacher, term.StartsAtUtc, term.EndsAtUtc);
        var otherAssignment = new TeachingAssignment(Guid.NewGuid(), null, school.Id, term.Id, schoolClass.Id, subject.Id, secondTeacher, term.StartsAtUtc, term.EndsAtUtc);
        await WithUnitOfWorkAsync(async () =>
        {
            await Repo<School>().InsertAsync(school, autoSave: false);
            await Repo<AcademicTerm>().InsertAsync(term, autoSave: false);
            await Repo<SchoolClass>().InsertAsync(schoolClass, autoSave: false);
            await Repo<EducationSubject>().InsertAsync(subject, autoSave: false);
            await Repo<EducationSubject>().InsertAsync(otherSubject, autoSave: false);
            foreach (var member in new[] { (manager, SchoolMemberRole.Manager), (teacher, SchoolMemberRole.Teacher),
                         (secondTeacher, SchoolMemberRole.Teacher), (student, SchoolMemberRole.Student) })
                await Repo<SchoolMembership>().InsertAsync(new SchoolMembership(Guid.NewGuid(), null, school.Id, member.Item1, member.Item2, term.StartsAtUtc), autoSave: false);
            await Repo<TeachingAssignment>().InsertAsync(assignment, autoSave: false);
            await Repo<TeachingAssignment>().InsertAsync(otherAssignment, autoSave: true);
        });
        return new Fixture(school, term, schoolClass, subject, otherSubject, assignment, otherAssignment, manager, teacher, student);
    }

    private Task<Guid> UserAsync(Guid? tenantId = null) => WithUnitOfWorkAsync(async () =>
    {
        var id = Guid.NewGuid();
        await Repo<IdentityUser>().InsertAsync(new IdentityUser(id, $"curriculum-{id:N}", $"curriculum-{id:N}@example.invalid", tenantId), autoSave: true);
        return id;
    });
    private IDisposable User(Guid id) => _principal.Change(new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(AbpClaimTypes.UserId, id.ToString()), new Claim(AbpClaimTypes.UserName, $"curriculum-{id:N}"), new Claim(AbpClaimTypes.Role, "admin")], "Test")));
    private static CreateTextbookDraftDto Create(Fixture f, Guid? subjectId = null) => new()
    {
        IdempotencyKey = Guid.NewGuid(), SchoolId = f.School.Id, EducationSubjectId = subjectId ?? f.Subject.Id,
        CatalogCode = "B" + Guid.NewGuid().ToString("N"), Grade = 7, Title = "测试目录", Publisher = "测试出版社", Edition = "测试物理版次", Volume = "上册"
    };
    private Task<CourseKnowledgePointDto> PointAsync(Fixture f, Guid? subjectId = null) => _service.CreateKnowledgePointAsync(new CreateCourseKnowledgePointDto
    {
        IdempotencyKey = Guid.NewGuid(), SchoolId = f.School.Id, EducationSubjectId = subjectId ?? f.Subject.Id, Code = "KP1", Name = "知识点原名称"
    });
    private async Task<TextbookVersionDto> PublishedAsync(Fixture f, Guid? pointRevision = null, Guid? subjectId = null)
    {
        var book = await _service.CreateDraftAsync(Create(f, subjectId));
        var save = Save(book);
        var chapter = Guid.NewGuid();
        save.Chapters = [new() { Id = chapter, Code = "CH1", Title = "测试章节" }];
        if (pointRevision.HasValue) save.Mappings = [new() { ChapterId = chapter, KnowledgePointRevisionId = pointRevision.Value }];
        book = await _service.SaveDraftAsync(book.Id, save);
        return await _service.PublishAsync(book.Id, book.ConcurrencyStamp);
    }
    private static SaveTextbookDraftDto Save(TextbookVersionDto x) => new()
    {
        ConcurrencyStamp = x.ConcurrencyStamp, Grade = x.Grade, Title = x.Title, Publisher = x.Publisher, Edition = x.Edition, Volume = x.Volume,
        Chapters = x.Chapters.Select(c => new SaveTextbookChapterDto { Id = c.Id, ParentChapterId = c.ParentChapterId, Code = c.Code, Title = c.Title, DisplayOrder = c.DisplayOrder }).ToList(),
        Mappings = x.Mappings.Select(m => new SaveTextbookMappingDto { ChapterId = m.ChapterId, KnowledgePointRevisionId = m.KnowledgePointRevisionId }).ToList()
    };
    private EducationCurriculumAppService DeniedService()
    {
        var checker = Substitute.For<IPermissionChecker>();
        checker.IsGrantedAsync(EducationPermissions.CurriculumManage).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>();
        lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>());
        lazy.LazyGetRequiredService<ICurrentTenant>().Returns(GetRequiredService<ICurrentTenant>());
        lazy.LazyGetRequiredService<IAsyncQueryableExecuter>().Returns(GetRequiredService<IAsyncQueryableExecuter>());
        lazy.LazyGetRequiredService<IUnitOfWorkManager>().Returns(GetRequiredService<IUnitOfWorkManager>());
        lazy.LazyGetRequiredService<IRepository<IdentityUser, Guid>>().Returns(Repo<IdentityUser>());
        lazy.LazyGetRequiredService<IRepository<School, Guid>>().Returns(Repo<School>());
        lazy.LazyGetRequiredService<IRepository<SchoolMembership, Guid>>().Returns(Repo<SchoolMembership>());
        lazy.LazyGetRequiredService<IRepository<EducationSubject, Guid>>().Returns(Repo<EducationSubject>());
        return new EducationCurriculumAppService { LazyServiceProvider = lazy };
    }
    private sealed record Fixture(School School, AcademicTerm Term, SchoolClass Class, EducationSubject Subject,
        EducationSubject OtherSubject, TeachingAssignment Assignment, TeachingAssignment OtherAssignment, Guid Manager, Guid Teacher, Guid Student);

}
