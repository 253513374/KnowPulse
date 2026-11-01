using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.KnowledgeBase;
using KnowPulse.Permissions;
using KnowPulse.QuestionBank;
using KnowPulse.Teaching;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Users;
using Volo.Abp.Uow;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class PersonalKnowledgeOrganizationTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IKnowledgeLibraryAppService _library;
    private readonly IKnowledgeItemAppService _items;

    public PersonalKnowledgeOrganizationTests()
    {
        _library = GetRequiredService<IKnowledgeLibraryAppService>();
        _items = GetRequiredService<IKnowledgeItemAppService>();
    }

    [Fact]
    public async Task Existing_Items_Should_Default_To_Uncategorized_And_Both_Personal_Create_Paths_Should_Keep_Organization()
    {
        var user = await CreateUserAsync();
        using var owner = ChangeUser(user);
        var legacy = await _library.CreateItemAsync(new CreateKnowledgeItemDto { Title = "旧资料" });
        legacy.MaterialType.ShouldBe(PersonalMaterialType.Unspecified);
        legacy.MaterialYear.ShouldBeNull();
        legacy.SubjectName.ShouldBeNull();
        legacy.CustomTags.ShouldBeEmpty();
        var created = await _library.CreateItemAsync(new CreateKnowledgeItemDto
        {
            Title = "教案", Organization = Organization(PersonalMaterialType.LessonPlan, 2018, " 数学 ", " 重点 ", "重点", "重点,典型")
        });
        created.CustomTags.ShouldBe(new[] { "重点", "重点,典型" });
        created.SubjectName.ShouldBe("数学");
        var read = await _items.GetAsync(created.Id);
        read.MaterialYear.ShouldBe(2018);
        read.MaterialType.ShouldBe(PersonalMaterialType.LessonPlan);
        var updated = await _items.UpdateAsync(created.Id, new UpdateKnowledgeItemDto { Title = "改标题", Summary = "只修改摘要" });
        updated.MaterialYear.ShouldBe(2018);
        updated.CustomTags.ShouldBe(created.CustomTags);
        var sibling = await _items.CreateAsync(new CreateKnowledgeItemDto
        {
            LibraryId = created.LibraryId, Title = "另一入口", Organization = Organization(PersonalMaterialType.Note, 2019, "语文", "观察")
        });
        sibling.MaterialType.ShouldBe(PersonalMaterialType.Note);
        sibling.SubjectName.ShouldBe("语文");
        sibling.CustomTags.ShouldBe(new[] { "观察" });
    }

    [Fact]
    public async Task Filters_Should_Compose_In_Owner_Scope_With_Exact_Tags_And_Private_Version_Keywords()
    {
        var ownerId = await CreateUserAsync();
        var otherId = await CreateUserAsync();
        using (ChangeUser(otherId))
        {
            var hidden = await _library.CreateItemAsync(new CreateKnowledgeItemDto
            {
                Title = "不公开标题", Organization = Organization(PersonalMaterialType.LessonPlan, 2018, "数学", "重点")
            });
            await _items.CreateVersionAsync(hidden.Id, new CreateKnowledgeVersionDto { Title = "私人正文", ContentText = "FOREIGN-SECRET" });
        }
        using var owner = ChangeUser(ownerId);
        var one = await _library.CreateItemAsync(new CreateKnowledgeItemDto
        {
            Title = "第一份", Organization = Organization(PersonalMaterialType.LessonPlan, 2018, "数学", "重点", "%_literal")
        });
        await _items.CreateVersionAsync(one.Id, new CreateKnowledgeVersionDto { Title = "历史正文", ContentText = "BODY-ONLY-MATCH" });
        await _library.CreateItemAsync(new CreateKnowledgeItemDto
        {
            Title = "第二份", Organization = Organization(PersonalMaterialType.LessonPlan, 2018, "数学", "重点")
        });
        await _library.CreateItemAsync(new CreateKnowledgeItemDto
        {
            Title = "仅子串", Organization = Organization(PersonalMaterialType.LessonPlan, 2018, "数学", "重点扩展", "other-literal")
        });
        await _library.CreateItemAsync(new CreateKnowledgeItemDto
        {
            Title = "另一学科", Organization = Organization(PersonalMaterialType.LessonPlan, 2018, "英语", "重点")
        });
        var filtered = await _library.GetMyItemsAsync(new GetPersonalKnowledgeItemListInput
        {
            MaterialType = PersonalMaterialType.LessonPlan, MaterialYear = 2018, SubjectName = "数学", Tag = "重点", MaxResultCount = 1
        });
        filtered.TotalCount.ShouldBe(2);
        filtered.Items.Count.ShouldBe(1);
        var secondPage = await _library.GetMyItemsAsync(new GetPersonalKnowledgeItemListInput
        {
            MaterialType = PersonalMaterialType.LessonPlan, MaterialYear = 2018, SubjectName = "数学", Tag = "重点", SkipCount = 1, MaxResultCount = 1
        });
        secondPage.TotalCount.ShouldBe(2);
        secondPage.Items.Single().Id.ShouldNotBe(filtered.Items.Single().Id);
        (await _library.GetMyItemsAsync(new GetPersonalKnowledgeItemListInput { Keyword = "BODY-ONLY-MATCH" })).Items.Single().Id.ShouldBe(one.Id);
        (await _library.GetMyItemsAsync(new GetPersonalKnowledgeItemListInput { Keyword = "FOREIGN-SECRET" })).TotalCount.ShouldBe(0);
        (await _library.GetMyItemsAsync(new GetPersonalKnowledgeItemListInput { Tag = "%_literal" })).Items.Single().Id.ShouldBe(one.Id);
        (await _library.GetMyItemsAsync(new GetPersonalKnowledgeItemListInput { MaterialYear = 2017 })).TotalCount.ShouldBe(0);
        (await _library.GetMyItemsAsync(new GetPersonalKnowledgeItemListInput { MaterialType = PersonalMaterialType.Note })).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Subject_And_Tag_Suggestions_Should_Only_Include_The_Owners_Personal_Material()
    {
        var ownerId = await CreateUserAsync();
        var otherId = await CreateUserAsync();
        await WithUnitOfWorkAsync(async () =>
        {
            await Repo<EducationSubject>().InsertAsync(new EducationSubject(Guid.NewGuid(), null, $"ORG-{Guid.NewGuid():N}", "数学"), autoSave: true);
            await Repo<EducationSubject>().InsertAsync(new EducationSubject(Guid.NewGuid(), Guid.NewGuid(), $"OTHER-{Guid.NewGuid():N}", "FOREIGN-TENANT-SUBJECT"), autoSave: true);
        });
        using (ChangeUser(otherId)) await _library.CreateItemAsync(new CreateKnowledgeItemDto
        {
            Title = "私人标签", Organization = Organization(PersonalMaterialType.Note, null, "PRIVATE-SUBJECT", "PRIVATE-TAG")
        });
        using var owner = ChangeUser(ownerId);
        await _library.CreateItemAsync(new CreateKnowledgeItemDto
        {
            Title = "未入校随记", Organization = Organization(PersonalMaterialType.Note, 2020, "自定学科", "本人标签")
        });
        var suggestions = await _library.GetOrganizationOptionsAsync();
        suggestions.Subjects.ShouldBe(new[] { "自定学科" });
        suggestions.Subjects.ShouldNotContain("PRIVATE-SUBJECT");
        suggestions.Subjects.ShouldNotContain("FOREIGN-TENANT-SUBJECT");
        suggestions.Tags.ShouldBe(new[] { "本人标签" });
        (await WithUnitOfWorkAsync(() => Repo<TeachingAssignment>().CountAsync(x => x.TeacherUserId == ownerId))).ShouldBe(0);
        (await WithUnitOfWorkAsync(() => Repo<SchoolMembership>().CountAsync(x => x.UserId == ownerId))).ShouldBe(0);
    }

    [Fact]
    public async Task Admin_Claims_Another_Tenant_And_Missing_Feature_Permission_Should_Not_Allow_Private_Organization()
    {
        var ownerId = await CreateUserAsync();
        var otherId = await CreateUserAsync();
        KnowledgeItemDto item;
        using (ChangeUser(ownerId)) item = await _library.CreateItemAsync(new CreateKnowledgeItemDto { Title = "仅本人整理" });
        using (ChangeUser(otherId))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _library.UpdateOrganizationAsync(item.Id, Update(item, "禁止")));
            (await _library.GetMyItemsAsync(new GetPersonalKnowledgeItemListInput { Keyword = item.Title })).Items.ShouldBeEmpty();
        }
        using var owner = ChangeUser(ownerId);
        using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
            await Should.ThrowAsync<AbpAuthorizationException>(() => _library.GetMyItemsAsync(new GetPersonalKnowledgeItemListInput()));

        var permissions = Substitute.For<IPermissionChecker>();
        permissions.IsGrantedAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>();
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>());
        lazy.LazyGetRequiredService<IPermissionChecker>().Returns(permissions);
        var denied = new KnowledgeLibraryAppService(Repo<KnowledgeLibrary>(), Repo<KnowledgeItem>(), Repo<KnowledgeCategory>(),
            GetRequiredService<IUnitOfWorkManager>(), GetRequiredService<IDataFilter>()) { LazyServiceProvider = lazy };
        await Should.ThrowAsync<AbpAuthorizationException>(() => denied.UpdateOrganizationAsync(item.Id, Update(item, "禁止")));
        await Should.ThrowAsync<AbpAuthorizationException>(() => denied.GetOrganizationOptionsAsync());
    }

    [Fact]
    public async Task Stale_Organization_Edit_Should_Not_Overwrite_Newer_Fields()
    {
        var user = await CreateUserAsync();
        using var owner = ChangeUser(user);
        var old = await _library.CreateItemAsync(new CreateKnowledgeItemDto { Title = "并发整理" });
        var current = await _library.UpdateOrganizationAsync(old.Id, Update(old, "新标签"));
        current.ConcurrencyStamp.ShouldNotBe(old.ConcurrencyStamp);
        (await Should.ThrowAsync<BusinessException>(() => _library.UpdateOrganizationAsync(old.Id, Update(old, "旧页面标签"))))
            .Code.ShouldBe(PersonalKnowledgeOrganizationErrorCodes.OrganizationChanged);
        (await _items.GetAsync(old.Id)).CustomTags.ShouldBe(new[] { "新标签" });
        var invalid = Update(current, "bad\ntag");
        (await Should.ThrowAsync<BusinessException>(() => _library.UpdateOrganizationAsync(old.Id, invalid)))
            .Code.ShouldBe(PersonalKnowledgeOrganizationErrorCodes.InvalidOrganization);
        (await _items.GetAsync(old.Id)).CustomTags.ShouldBe(new[] { "新标签" });
    }

    [Fact]
    public async Task Organizing_Personal_Material_Should_Not_Change_Shared_Version_Or_Frozen_Paper()
    {
        var ownerId = await CreateUserAsync();
        var readerId = await CreateUserAsync();
        var studentId = await CreateUserAsync();
        var schoolId = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            await Repo<School>().InsertAsync(new School(schoolId, null, $"S{schoolId:N}", "共享测试学校"), autoSave: true);
            foreach (var userId in new[] { ownerId, readerId })
                await Repo<SchoolMembership>().InsertAsync(new SchoolMembership(Guid.NewGuid(), null, schoolId, userId,
                    SchoolMemberRole.Teacher, DateTime.UtcNow.AddDays(-1)), autoSave: true);
            var relationship = new TeachingRelationship(Guid.NewGuid(), null, ownerId, studentId, "资料整理测试学生");
            relationship.Confirm();
            await Repo<TeachingRelationship>().InsertAsync(relationship, autoSave: true);
        });
        Guid shareId;
        using (ChangeUser(ownerId))
        {
            var item = await _library.CreateItemAsync(new CreateKnowledgeItemDto { Title = "原资料", Summary = "本人摘要" });
            var version = await _items.CreateVersionAsync(item.Id, new CreateKnowledgeVersionDto { Title = "原版本标题", ContentText = "原正文" });
            await _items.PublishVersionAsync(item.Id, version.Id);
            shareId = (await SeedHistoricalSchoolShareAsync(item.Id, version.Id, schoolId,
                SchoolKnowledgeShareUses.Read, "确认共享摘要")).Id;
            var workflow = GetRequiredService<IQuestionWorkflowAppService>();
            var question = await workflow.CreateManualDraftAsync(new SaveQuestionDraftDto
            {
                Type = QuestionType.TrueFalse, Difficulty = QuestionDifficulty.Normal, Title = "原题", Stem = "原判断题",
                Answers = { new CreateUpdateQuestionAnswerDto { Value = "true", DisplayOrder = 1 } },
                Sources = { new CreateUpdateQuestionSourceDto { KnowledgeItemId = item.Id, KnowledgeVersionId = version.Id } }
            });
            question = await workflow.SubmitAsync(question.Id);
            await GetRequiredService<IQuestionAppService>().ApproveAsync(question.Id, new ReviewQuestionDto { Comment = "确认题目" });
            var exams = GetRequiredService<IExamAppService>();
            var plan = await exams.CreatePlanAsync(new CreateExamPlanDto
            {
                Name = "原试卷", StartTime = DateTime.UtcNow.AddHours(1), EndTime = DateTime.UtcNow.AddHours(2), DurationMinutes = 30, PassingScore = 5,
                Paper = new GenerateExamPaperDto { Title = "原试卷", BuildMode = ExamPaperBuildMode.Fixed,
                    FixedQuestionIds = { question.Id }, Rules = { new ExamPaperRuleDto { ScorePerQuestion = 10 } } },
                Assignments = { new ExamAssignmentInputDto { ScopeType = ExamAssignmentScopeType.User, TargetId = studentId } }
            });
            // Both snapshots come from fresh database reads; the creation DTO still carries pre-roundtrip decimal/time formatting.
            var originalSnapshot = await WithUnitOfWorkAsync(async () => JsonSerializer.Serialize((await exams.GetPlanAsync(plan.Id)).Paper));
            var editable = await _items.GetAsync(item.Id);
            await _library.UpdateOrganizationAsync(item.Id, new UpdatePersonalKnowledgeOrganizationInput
            {
                ConcurrencyStamp = editable.ConcurrencyStamp, MaterialType = PersonalMaterialType.Reflection, MaterialYear = 2014,
                SubjectName = "PRIVATE-UPDATED-SUBJECT", CustomTags = { "PRIVATE-UPDATED-TAG" }
            });
            var retainedSnapshot = await WithUnitOfWorkAsync(async () => JsonSerializer.Serialize((await exams.GetPlanAsync(plan.Id)).Paper));
            retainedSnapshot.ShouldBe(originalSnapshot);
            var retained = (await _items.GetVersionsAsync(item.Id)).Items.Single();
            retained.Id.ShouldBe(version.Id);
            retained.Title.ShouldBe("原版本标题");
            retained.ContentText.ShouldBe("原正文");
            retained.IsCurrentPublished.ShouldBeTrue();
        }
        using (ChangeUser(readerId))
        {
            var share = await WithUnitOfWorkAsync(() => Repo<SchoolKnowledgeShare>().GetAsync(shareId));
            share.Title.ShouldBe("原版本标题");
            share.Summary.ShouldBe("确认共享摘要");
            JsonSerializer.Serialize(share).ShouldNotContain("PRIVATE-UPDATED");
            (await _library.GetOrganizationOptionsAsync()).Tags.ShouldNotContain("PRIVATE-UPDATED-TAG");
            (await _library.GetOrganizationOptionsAsync()).Subjects.ShouldNotContain("PRIVATE-UPDATED-SUBJECT");
        }
    }

    private async Task<Guid> CreateUserAsync()
    {
        var id = Guid.NewGuid();
        await WithUnitOfWorkAsync(() => Repo<IdentityUser>().InsertAsync(new IdentityUser(id, $"organize-{id:N}", $"{id:N}@example.invalid"), autoSave: true));
        return id;
    }

    private static PersonalKnowledgeOrganizationInput Organization(PersonalMaterialType type, int? year, string? subject, params string[] tags)
        => new() { MaterialType = type, MaterialYear = year, SubjectName = subject, CustomTags = tags.ToList() };

    private static UpdatePersonalKnowledgeOrganizationInput Update(KnowledgeItemDto item, string tag) => new()
    {
        ConcurrencyStamp = item.ConcurrencyStamp, MaterialType = PersonalMaterialType.Note, MaterialYear = 2020,
        SubjectName = "自定学科", CustomTags = { tag }
    };

    private IRepository<T, Guid> Repo<T>() where T : class, Volo.Abp.Domain.Entities.IEntity<Guid> => GetRequiredService<IRepository<T, Guid>>();

    private IDisposable ChangeUser(Guid user) => GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(AbpClaimTypes.UserId, user.ToString()), new Claim(AbpClaimTypes.UserName, $"organize-{user:N}"), new Claim(AbpClaimTypes.Role, "admin")
    }, "Test")));
}
