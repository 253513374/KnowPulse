using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

[Authorize]
public class EducationCurriculumAppService : KnowPulseAppService, IEducationCurriculumAppService
{
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private IUnitOfWorkManager Uow => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();

    // Reference reads do not join another aggregate's change graph: ABP otherwise updates its audit fields/stamp on FK changes.
    private async Task<T?> FindReferenceAsync<T>(Guid id) where T : class, IEntity<Guid>
    {
        using var tracking = Repo<T>().DisableTracking();
        return await Repo<T>().FindAsync(id);
    }
    private async Task<List<T>> ReferencesAsync<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity<Guid>
    {
        using var tracking = Repo<T>().DisableTracking();
        return await Repo<T>().GetListAsync(predicate);
    }
    private static async Task<T> CompleteCommandAsync<T>(IUnitOfWork transaction, Func<Task<T>> map)
    {
        await transaction.SaveChangesAsync();
        var result = await map();
        await transaction.CompleteAsync();
        return result;
    }

    public virtual async Task<CurriculumContextDto> GetContextAsync(Guid schoolId)
    {
        var access = await GetSchoolAccessAsync(schoolId);
        var subjects = await AsyncExecuter.ToListAsync((await Repo<EducationSubject>().GetQueryableAsync())
            .Where(x => x.TenantId == access.School.TenantId).OrderBy(x => x.Code));
        var result = new CurriculumContextDto
        {
            SchoolId = schoolId, CanManage = access.CanManage,
            Subjects = subjects.Select(x => new EducationSubjectDto { Id = x.Id, Code = x.Code, Name = x.Name }).ToList()
        };
        if (!access.IsTeacher) return result;
        var now = DateTime.UtcNow;
        var assignments = await Repo<TeachingAssignment>().GetQueryableAsync();
        var terms = await Repo<AcademicTerm>().GetQueryableAsync();
        var classes = await Repo<SchoolClass>().GetQueryableAsync();
        result.TeachingContexts = await AsyncExecuter.ToListAsync(
            from assignment in assignments
            join term in terms on assignment.AcademicTermId equals term.Id
            join schoolClass in classes on assignment.SchoolClassId equals schoolClass.Id
            join subject in (await Repo<EducationSubject>().GetQueryableAsync()) on assignment.EducationSubjectId equals subject.Id
            where assignment.TenantId == access.School.TenantId && assignment.SchoolId == schoolId && assignment.TeacherUserId == CurrentUser.Id &&
                assignment.EffectiveFromUtc <= now && (!assignment.EffectiveToUtc.HasValue || now < assignment.EffectiveToUtc) &&
                term.TenantId == access.School.TenantId && term.SchoolId == schoolId && term.StartsAtUtc <= now && now < term.EndsAtUtc &&
                schoolClass.TenantId == access.School.TenantId && schoolClass.SchoolId == schoolId && schoolClass.AcademicTermId == term.Id &&
                subject.TenantId == access.School.TenantId
            orderby term.Name, schoolClass.Name, subject.Name
            select new MyTeachingContextDto
            {
                Id = assignment.Id, AcademicTermId = term.Id, TermName = term.Name, SchoolClassId = schoolClass.Id,
                ClassName = schoolClass.Name, Grade = schoolClass.Grade, EducationSubjectId = subject.Id, SubjectName = subject.Name
            });
        return result;
    }

    public virtual async Task<PagedResultDto<TextbookVersionDto>> GetTextbooksAsync(Guid schoolId, GetTextbookListInput input)
    {
        var access = await GetSchoolAccessAsync(schoolId);
        var query = (await Repo<TextbookVersion>().GetQueryableAsync()).Where(x => x.SchoolId == schoolId && x.TenantId == access.School.TenantId);
        if (!access.CanManage || !input.IncludeUnpublished) query = query.Where(x => x.Status == TextbookVersionStatus.Published);
        if (input.EducationSubjectId.HasValue) query = query.Where(x => x.EducationSubjectId == input.EducationSubjectId);
        if (input.Grade.HasValue) query = query.Where(x => x.Grade == input.Grade);
        var count = await AsyncExecuter.CountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.CatalogCode).ThenByDescending(x => x.CatalogRevision)
            .Skip(input.SkipCount).Take(Math.Min(input.MaxResultCount, 100)));
        return new PagedResultDto<TextbookVersionDto>(count, rows.Select(Map).ToList());
    }

    public virtual async Task<TextbookVersionDto> GetTextbookAsync(Guid id)
    {
        var book = await FindBookAsync(id);
        var access = await GetSchoolAccessAsync(book.SchoolId);
        if (book.Status == TextbookVersionStatus.Draft && !access.CanManage) throw new AbpAuthorizationException();
        return await MapBookAsync(book);
    }

    [Authorize(EducationPermissions.CurriculumManage)]
    public virtual async Task<TextbookVersionDto> CreateDraftAsync(CreateTextbookDraftDto input)
    {
        using var transaction = Uow.Begin(requiresNew: true, isTransactional: true);
        var access = await GetSchoolAccessAsync(input.SchoolId, manage: true);
        EducationRules.RequireId(input.IdempotencyKey);
        await EnsureSubjectAsync(access.School, input.EducationSubjectId);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            input.SchoolId, input.EducationSubjectId, Code = EducationRules.NormalizeCode(input.CatalogCode, 64),
            input.Grade, Title = Check.NotNullOrWhiteSpace(input.Title, nameof(input.Title), 256).Trim(),
            Publisher = Check.NotNullOrWhiteSpace(input.Publisher, nameof(input.Publisher), 128).Trim(),
            Edition = Check.NotNullOrWhiteSpace(input.Edition, nameof(input.Edition), 128).Trim(),
            Volume = Check.NotNullOrWhiteSpace(input.Volume, nameof(input.Volume), 64).Trim()
        }))));
        var existing = await Repo<TextbookVersion>().FindAsync(input.IdempotencyKey);
        if (existing != null)
        {
            EnsureBookScope(existing, access.School);
            if (existing.GetProperty<string>("CreateFingerprint") != fingerprint) throw Conflict();
            return await CompleteCommandAsync(transaction, () => MapBookAsync(existing));
        }
        var book = new TextbookVersion(input.IdempotencyKey, access.School.TenantId, access.School.Id, input.EducationSubjectId,
            input.CatalogCode, 1, input.Grade, input.Title, input.Publisher, input.Edition, input.Volume);
        if (await AsyncExecuter.AnyAsync((await Repo<TextbookVersion>().GetQueryableAsync()).Where(x =>
            x.SchoolId == book.SchoolId && x.TenantId == book.TenantId && x.CatalogCode == book.CatalogCode))) throw Conflict();
        book.SetProperty("CreateFingerprint", fingerprint);
        await AdvanceSchoolAsync(access.School);
        await Repo<TextbookVersion>().InsertAsync(book, autoSave: true);
        await transaction.CompleteAsync();
        return Map(book);
    }

    [Authorize(EducationPermissions.CurriculumManage)]
    public virtual async Task<TextbookVersionDto> ReviseAsync(Guid id, ReviseTextbookDto input)
    {
        using var transaction = Uow.Begin(requiresNew: true, isTransactional: true);
        var source = await FindBookAsync(id);
        var access = await GetSchoolAccessAsync(source.SchoolId, manage: true);
        EducationRules.RequireId(input.IdempotencyKey);
        var existing = await Repo<TextbookVersion>().FindAsync(input.IdempotencyKey);
        if (existing != null)
        {
            EnsureBookScope(existing, access.School);
            if (existing.PreviousVersionId != source.Id) throw Conflict();
            return await CompleteCommandAsync(transaction, () => MapBookAsync(existing));
        }
        if (source.Status == TextbookVersionStatus.Draft) throw new BusinessException(EducationCurriculumErrorCodes.PublishedVersionRequired);
        var revisions = await AsyncExecuter.ToListAsync((await Repo<TextbookVersion>().GetQueryableAsync()).Where(x =>
            x.SchoolId == source.SchoolId && x.TenantId == source.TenantId && x.CatalogCode == source.CatalogCode).Select(x => x.CatalogRevision));
        var book = new TextbookVersion(input.IdempotencyKey, source.TenantId, source.SchoolId, source.EducationSubjectId,
            source.CatalogCode, checked(revisions.Max() + 1), source.Grade, source.Title, source.Publisher, source.Edition, source.Volume, source.Id);
        await AdvanceSchoolAsync(access.School);
        await Repo<TextbookVersion>().InsertAsync(book, autoSave: true);
        var chapters = await ChaptersAsync(source.Id);
        var chapterIds = chapters.ToDictionary(x => x.Id, _ => GuidGenerator.Create());
        foreach (var chapter in chapters)
            await Repo<TextbookChapter>().InsertAsync(new TextbookChapter(chapterIds[chapter.Id], book.TenantId, book.Id,
                chapter.ParentChapterId.HasValue ? chapterIds[chapter.ParentChapterId.Value] : null,
                chapter.Code, chapter.Title, chapter.DisplayOrder), autoSave: false);
        foreach (var mapping in await MappingsAsync(source.Id))
            await Repo<TextbookKnowledgePointMapping>().InsertAsync(new TextbookKnowledgePointMapping(GuidGenerator.Create(), book.TenantId,
                book.Id, chapterIds[mapping.ChapterId], mapping.KnowledgePointId, mapping.KnowledgePointRevisionId,
                CurrentUser.Id!.Value, DateTime.UtcNow), autoSave: false);
        return await CompleteCommandAsync(transaction, () => MapBookAsync(book));
    }

    [Authorize(EducationPermissions.CurriculumManage)]
    public virtual async Task<TextbookVersionDto> SaveDraftAsync(Guid id, SaveTextbookDraftDto input)
    {
        using var transaction = Uow.Begin(requiresNew: true, isTransactional: true);
        var book = await FindBookAsync(id, forUpdate: true);
        var access = await GetSchoolAccessAsync(book.SchoolId, manage: true);
        book.EnsureDraft();
        EnsureStamp(book.ConcurrencyStamp, input.ConcurrencyStamp);
        if (input.Chapters == null || input.Mappings == null || input.Chapters.Count > 300 || input.Mappings.Count > 3000)
            throw new BusinessException(EducationCurriculumErrorCodes.CapacityExceeded);
        var chapters = input.Chapters.Select(x => new TextbookChapter(x.Id, book.TenantId, book.Id,
            x.ParentChapterId, x.Code, x.Title, x.DisplayOrder)).ToList();
        ValidateChapters(chapters);
        var revisionIds = input.Mappings.Select(x => x.KnowledgePointRevisionId).Distinct().ToList();
        var revisions = await ValidatePointReferencesAsync(book, revisionIds);
        var chapterIds = chapters.Select(x => x.Id).ToHashSet();
        var mappingPairs = new HashSet<(Guid, Guid)>();
        foreach (var mapping in input.Mappings)
        {
            if (!chapterIds.Contains(mapping.ChapterId) || !revisions.TryGetValue(mapping.KnowledgePointRevisionId, out var revision) ||
                !mappingPairs.Add((mapping.ChapterId, revision.KnowledgePointId))) throw new BusinessException(EducationCurriculumErrorCodes.InvalidStructure);
        }
        var oldChapters = await ChaptersAsync(id);
        foreach (var chapter in chapters.Where(x => oldChapters.All(old => old.Id != x.Id)))
            if (await Repo<TextbookChapter>().FindAsync(chapter.Id) != null) throw new BusinessException(EducationCurriculumErrorCodes.ReferenceMismatch);
        book.UpdateDraft(input.Grade, input.Title, input.Publisher, input.Edition, input.Volume);
        await AdvanceSchoolAsync(access.School);
        // Children and publication share this root concurrency barrier even when metadata itself did not change.
        await Repo<TextbookVersion>().UpdateManyAsync([book], autoSave: true);
        foreach (var mapping in await MappingsAsync(id)) await Repo<TextbookKnowledgePointMapping>().DeleteAsync(mapping, autoSave: false);
        foreach (var old in oldChapters)
        {
            var replacement = chapters.FirstOrDefault(x => x.Id == old.Id);
            if (replacement == null) await Repo<TextbookChapter>().DeleteAsync(old, autoSave: false);
            else
            {
                old.UpdateDraft(book, replacement.ParentChapterId, replacement.Code, replacement.Title, replacement.DisplayOrder);
                await Repo<TextbookChapter>().UpdateAsync(old, autoSave: false);
            }
        }
        foreach (var chapter in chapters.Where(x => oldChapters.All(old => old.Id != x.Id)))
            await Repo<TextbookChapter>().InsertAsync(chapter, autoSave: false);
        // Release old active mapping keys before inserting replacement confirmations in the same transaction.
        await transaction.SaveChangesAsync();
        foreach (var mapping in input.Mappings)
            await Repo<TextbookKnowledgePointMapping>().InsertAsync(new TextbookKnowledgePointMapping(GuidGenerator.Create(), book.TenantId,
                id, mapping.ChapterId, revisions[mapping.KnowledgePointRevisionId].KnowledgePointId,
                mapping.KnowledgePointRevisionId, CurrentUser.Id!.Value, DateTime.UtcNow), autoSave: false);
        return await CompleteCommandAsync(transaction, () => MapBookAsync(book));
    }

    [Authorize(EducationPermissions.CurriculumManage)]
    public virtual async Task<TextbookVersionDto> PublishAsync(Guid id, string concurrencyStamp)
    {
        using var transaction = Uow.Begin(requiresNew: true, isTransactional: true);
        var book = await FindBookAsync(id, forUpdate: true);
        var access = await GetSchoolAccessAsync(book.SchoolId, manage: true);
        if (book.Status == TextbookVersionStatus.Published) return await MapBookAsync(book);
        EnsureStamp(book.ConcurrencyStamp, concurrencyStamp);
        book.EnsureDraft();
        var chapters = await ChaptersAsync(id);
        if (chapters.Count == 0) throw new BusinessException(EducationCurriculumErrorCodes.InvalidStructure);
        ValidateChapters(chapters);
        var mappings = await MappingsAsync(id);
        await ValidatePointReferencesAsync(book, mappings.Select(x => x.KnowledgePointRevisionId).ToList());
        if (mappings.Any(x => chapters.All(chapter => chapter.Id != x.ChapterId))) throw new BusinessException(EducationCurriculumErrorCodes.ReferenceMismatch);
        book.Publish(DateTime.UtcNow);
        await AdvanceSchoolAsync(access.School);
        await Repo<TextbookVersion>().UpdateAsync(book, autoSave: true);
        return await CompleteCommandAsync(transaction, () => MapBookAsync(book));
    }

    [Authorize(EducationPermissions.CurriculumManage)]
    public virtual async Task<TextbookVersionDto> RetireAsync(Guid id, string concurrencyStamp)
    {
        using var transaction = Uow.Begin(requiresNew: true, isTransactional: true);
        var book = await FindBookAsync(id, forUpdate: true);
        var access = await GetSchoolAccessAsync(book.SchoolId, manage: true);
        if (book.Status == TextbookVersionStatus.Retired) return await MapBookAsync(book);
        EnsureStamp(book.ConcurrencyStamp, concurrencyStamp);
        book.Retire(DateTime.UtcNow);
        await AdvanceSchoolAsync(access.School);
        await Repo<TextbookVersion>().UpdateAsync(book, autoSave: true);
        return await CompleteCommandAsync(transaction, () => MapBookAsync(book));
    }

    public virtual async Task<PagedResultDto<CourseKnowledgePointDto>> GetKnowledgePointsAsync(Guid schoolId, Guid subjectId, PagedResultRequestDto input)
    {
        var access = await GetSchoolAccessAsync(schoolId);
        await EnsureSubjectAsync(access.School, subjectId);
        var query = (await Repo<CourseKnowledgePoint>().GetQueryableAsync()).Where(x =>
            x.SchoolId == schoolId && x.TenantId == access.School.TenantId && x.EducationSubjectId == subjectId);
        var count = await AsyncExecuter.CountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.Code).Skip(input.SkipCount).Take(Math.Min(input.MaxResultCount, 100)));
        var result = new List<CourseKnowledgePointDto>();
        foreach (var point in rows) result.Add(await MapPointAsync(point));
        return new PagedResultDto<CourseKnowledgePointDto>(count, result);
    }

    [Authorize(EducationPermissions.CurriculumManage)]
    public virtual async Task<CourseKnowledgePointDto> CreateKnowledgePointAsync(CreateCourseKnowledgePointDto input)
    {
        using var transaction = Uow.Begin(requiresNew: true, isTransactional: true);
        var access = await GetSchoolAccessAsync(input.SchoolId, manage: true);
        await EnsureSubjectAsync(access.School, input.EducationSubjectId);
        EducationRules.RequireId(input.IdempotencyKey);
        var existing = await Repo<CourseKnowledgePoint>().FindAsync(input.IdempotencyKey);
        if (existing != null)
        {
            if (existing.TenantId != access.School.TenantId || existing.SchoolId != input.SchoolId || existing.EducationSubjectId != input.EducationSubjectId ||
                existing.Code != EducationRules.NormalizeCode(input.Code, 64)) throw Conflict();
            var first = await Repo<CourseKnowledgePointRevision>().GetAsync(x => x.KnowledgePointId == existing.Id && x.RevisionNumber == 1);
            if (first.Name != input.Name.Trim() || first.ParentRevisionId != input.ParentRevisionId) throw Conflict();
            return await CompleteCommandAsync(transaction, () => MapPointAsync(existing));
        }
        var point = new CourseKnowledgePoint(input.IdempotencyKey, access.School.TenantId, input.SchoolId, input.EducationSubjectId, input.Code);
        if (await AsyncExecuter.AnyAsync((await Repo<CourseKnowledgePoint>().GetQueryableAsync()).Where(x => x.TenantId == point.TenantId &&
            x.SchoolId == point.SchoolId && x.EducationSubjectId == point.EducationSubjectId && x.Code == point.Code))) throw Conflict();
        await ValidateParentAsync(point, input.ParentRevisionId);
        var revision = new CourseKnowledgePointRevision(GuidGenerator.Create(), point.TenantId, point.Id,
            point.AdvanceRevision(), input.Name, input.ParentRevisionId);
        await AdvanceSchoolAsync(access.School);
        await Repo<CourseKnowledgePoint>().InsertAsync(point, autoSave: false);
        await Repo<CourseKnowledgePointRevision>().InsertAsync(revision, autoSave: true);
        return await CompleteCommandAsync(transaction, () => MapPointAsync(point));
    }

    [Authorize(EducationPermissions.CurriculumManage)]
    public virtual async Task<CourseKnowledgePointDto> CreateKnowledgePointRevisionAsync(Guid id, CreateKnowledgePointRevisionDto input)
    {
        using var transaction = Uow.Begin(requiresNew: true, isTransactional: true);
        var point = await Repo<CourseKnowledgePoint>().GetAsync(id);
        if (point.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        var access = await GetSchoolAccessAsync(point.SchoolId, manage: true);
        EducationRules.RequireId(input.IdempotencyKey);
        var existing = await Repo<CourseKnowledgePointRevision>().FindAsync(input.IdempotencyKey);
        if (existing != null)
        {
            if (existing.TenantId != point.TenantId || existing.KnowledgePointId != id || existing.Name != input.Name.Trim() || existing.ParentRevisionId != input.ParentRevisionId)
                throw Conflict();
            return await CompleteCommandAsync(transaction, () => MapPointAsync(point));
        }
        EnsureStamp(point.ConcurrencyStamp, input.ConcurrencyStamp);
        await ValidateParentAsync(point, input.ParentRevisionId);
        var revision = new CourseKnowledgePointRevision(input.IdempotencyKey, point.TenantId, id, point.AdvanceRevision(), input.Name, input.ParentRevisionId);
        await AdvanceSchoolAsync(access.School);
        await Repo<CourseKnowledgePoint>().UpdateAsync(point, autoSave: false);
        await Repo<CourseKnowledgePointRevision>().InsertAsync(revision, autoSave: true);
        return await CompleteCommandAsync(transaction, () => MapPointAsync(point));
    }

    public virtual async Task<ListResultDto<TeacherTextbookSelectionDto>> GetMySelectionsAsync(Guid schoolId)
    {
        var access = await GetSchoolAccessAsync(schoolId);
        if (!access.IsTeacher) throw new AbpAuthorizationException();
        var selections = await AsyncExecuter.ToListAsync((await Repo<TeacherTextbookSelection>().GetQueryableAsync()).Where(x =>
            x.TenantId == access.School.TenantId && x.SchoolId == schoolId && x.TeacherUserId == CurrentUser.Id).OrderByDescending(x => x.SelectedAtUtc));
        var result = new List<TeacherTextbookSelectionDto>();
        foreach (var selection in selections) result.Add(await MapSelectionAsync(selection));
        return new ListResultDto<TeacherTextbookSelectionDto>(result);
    }

    public virtual async Task<TeacherTextbookSelectionDto> SelectTextbookAsync(Guid assignmentId, SelectTeacherTextbookDto input)
    {
        using var transaction = Uow.Begin(requiresNew: true, isTransactional: true);
        var assignment = await FindReferenceAsync<TeachingAssignment>(assignmentId);
        if (assignment == null || assignment.TenantId != CurrentTenant.Id || assignment.TeacherUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        var access = await GetSchoolAccessAsync(assignment.SchoolId);
        if (!access.IsTeacher || !assignment.IsEffectiveAt(DateTime.UtcNow)) throw new AbpAuthorizationException();
        var term = await FindReferenceAsync<AcademicTerm>(assignment.AcademicTermId);
        var schoolClass = await FindReferenceAsync<SchoolClass>(assignment.SchoolClassId);
        var now = DateTime.UtcNow;
        if (term == null || schoolClass == null || term.TenantId != access.School.TenantId || term.SchoolId != access.School.Id || now < term.StartsAtUtc || now >= term.EndsAtUtc ||
            schoolClass.TenantId != access.School.TenantId || schoolClass.SchoolId != access.School.Id || schoolClass.AcademicTermId != term.Id)
            throw new BusinessException(EducationCurriculumErrorCodes.ReferenceMismatch);
        EducationRules.RequireId(input.IdempotencyKey);
        var book = await FindBookAsync(input.TextbookVersionId);
        EnsureBookScope(book, access.School);
        if (book.EducationSubjectId != assignment.EducationSubjectId || book.Grade != schoolClass.Grade)
            throw new BusinessException(EducationCurriculumErrorCodes.ReferenceMismatch);
        var existing = await Repo<TeacherTextbookSelection>().FindAsync(input.IdempotencyKey);
        if (existing != null)
        {
            if (existing.TenantId != access.School.TenantId || existing.TeacherUserId != CurrentUser.Id || existing.TeachingAssignmentId != assignmentId || existing.TextbookVersionId != book.Id)
                throw Conflict();
            return await MapSelectionAsync(existing);
        }
        if (book.Status != TextbookVersionStatus.Published) throw new BusinessException(EducationCurriculumErrorCodes.PublishedVersionRequired);
        var current = await Repo<TeacherTextbookSelection>().FindAsync(x => x.TenantId == access.School.TenantId &&
            x.TeachingAssignmentId == assignmentId && !x.EndedAtUtc.HasValue);
        if (current != null)
        {
            current.End(now);
            await Repo<TeacherTextbookSelection>().UpdateAsync(current, autoSave: true);
        }
        // Share the existing school barrier with relationship changes so a concurrent end cannot authorize a new choice.
        await AdvanceSchoolAsync(access.School);
        var selection = new TeacherTextbookSelection(input.IdempotencyKey, access.School.TenantId, access.School.Id,
            CurrentUser.Id!.Value, assignmentId, book.Id, now);
        await Repo<TeacherTextbookSelection>().InsertAsync(selection, autoSave: true);
        return await CompleteCommandAsync(transaction, () => MapSelectionAsync(selection));
    }

    private async Task<(School School, bool IsTeacher, bool CanManage)> GetSchoolAccessAsync(Guid schoolId, bool manage = false)
    {
        if (!CurrentUser.IsAuthenticated || !CurrentUser.Id.HasValue) throw new AbpAuthorizationException();
        var account = await FindReferenceAsync<IdentityUser>(CurrentUser.Id.Value);
        var school = await Repo<School>().FindAsync(schoolId);
        if (account == null || !account.IsActive || account.TenantId != CurrentTenant.Id || school == null || school.TenantId != CurrentTenant.Id)
            throw new AbpAuthorizationException();
        var now = DateTime.UtcNow;
        var roles = await AsyncExecuter.ToListAsync((await Repo<SchoolMembership>().GetQueryableAsync()).Where(x =>
            x.TenantId == school.TenantId && x.SchoolId == school.Id && x.UserId == account.Id && x.EffectiveFromUtc <= now &&
            (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc)).Select(x => x.Role));
        var teacher = roles.Contains(SchoolMemberRole.Teacher);
        var manager = roles.Contains(SchoolMemberRole.Manager);
        if (!teacher && !manager) throw new AbpAuthorizationException();
        var canManage = manager && await IsMvpPermissionGrantedAsync(EducationPermissions.CurriculumManage);
        if (manage)
        {
            await CheckMvpPermissionAsync(EducationPermissions.CurriculumManage);
            if (!manager) throw new AbpAuthorizationException();
        }
        return (school, teacher, canManage);
    }

    private async Task EnsureSubjectAsync(School school, Guid subjectId)
    {
        var subject = await FindReferenceAsync<EducationSubject>(subjectId);
        if (subject == null || subject.TenantId != school.TenantId) throw new BusinessException(EducationCurriculumErrorCodes.ReferenceMismatch);
    }
    private async Task<TextbookVersion> FindBookAsync(Guid id, bool forUpdate = false)
    {
        var book = forUpdate ? await Repo<TextbookVersion>().FindAsync(id) : await FindReferenceAsync<TextbookVersion>(id);
        if (book == null || book.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        return book;
    }
    private static void EnsureBookScope(TextbookVersion book, School school)
    {
        if (book.TenantId != school.TenantId || book.SchoolId != school.Id) throw new AbpAuthorizationException();
    }
    private static void EnsureStamp(string actual, string expected)
    {
        if (string.IsNullOrWhiteSpace(expected) || actual != expected) throw new AbpDbConcurrencyException();
    }
    private async Task AdvanceSchoolAsync(School school)
    {
        school.AdvanceConfigurationRevision();
        await Repo<School>().UpdateAsync(school, autoSave: true);
    }
    private static BusinessException Conflict() => new(EducationCurriculumErrorCodes.ConflictingOperation);
    private Task<List<TextbookChapter>> ChaptersAsync(Guid id) => Repo<TextbookChapter>().GetListAsync(x => x.TextbookVersionId == id);
    private Task<List<TextbookKnowledgePointMapping>> MappingsAsync(Guid id) => Repo<TextbookKnowledgePointMapping>().GetListAsync(x => x.TextbookVersionId == id);

    private static void ValidateChapters(List<TextbookChapter> chapters)
    {
        if (chapters.Select(x => x.Id).Distinct().Count() != chapters.Count || chapters.Select(x => x.Code).Distinct().Count() != chapters.Count)
            throw new BusinessException(EducationCurriculumErrorCodes.InvalidStructure);
        var byId = chapters.ToDictionary(x => x.Id);
        foreach (var chapter in chapters)
        {
            var visited = new HashSet<Guid> { chapter.Id };
            var parent = chapter.ParentChapterId;
            while (parent.HasValue)
            {
                if (!byId.TryGetValue(parent.Value, out var row) || !visited.Add(parent.Value)) throw new BusinessException(EducationCurriculumErrorCodes.InvalidStructure);
                parent = row.ParentChapterId;
            }
        }
    }
    private async Task<Dictionary<Guid, CourseKnowledgePointRevision>> ValidatePointReferencesAsync(TextbookVersion book, List<Guid> ids)
    {
        var revisions = await ReferencesAsync<CourseKnowledgePointRevision>(x => ids.Contains(x.Id) && x.TenantId == book.TenantId);
        if (revisions.Count != ids.Distinct().Count()) throw new BusinessException(EducationCurriculumErrorCodes.ReferenceMismatch);
        var pointIds = revisions.Select(x => x.KnowledgePointId).Distinct().ToList();
        var points = await ReferencesAsync<CourseKnowledgePoint>(x =>
            pointIds.Contains(x.Id) && x.TenantId == book.TenantId && x.SchoolId == book.SchoolId && x.EducationSubjectId == book.EducationSubjectId);
        if (points.Count != pointIds.Count) throw new BusinessException(EducationCurriculumErrorCodes.ReferenceMismatch);
        return revisions.ToDictionary(x => x.Id);
    }
    private async Task ValidateParentAsync(CourseKnowledgePoint point, Guid? parentId)
    {
        var visited = new HashSet<Guid> { point.Id };
        while (parentId.HasValue)
        {
            var revision = await FindReferenceAsync<CourseKnowledgePointRevision>(parentId.Value);
            var parent = revision == null ? null : await FindReferenceAsync<CourseKnowledgePoint>(revision.KnowledgePointId);
            if (parent == null || revision!.TenantId != point.TenantId || parent.TenantId != point.TenantId || parent.SchoolId != point.SchoolId ||
                parent.EducationSubjectId != point.EducationSubjectId || !visited.Add(parent.Id))
                throw new BusinessException(EducationCurriculumErrorCodes.ReferenceMismatch);
            parentId = revision.ParentRevisionId;
        }
    }
    private static TextbookVersionDto Map(TextbookVersion x) => new()
    {
        Id = x.Id, SchoolId = x.SchoolId, EducationSubjectId = x.EducationSubjectId, CatalogCode = x.CatalogCode, CatalogRevision = x.CatalogRevision,
        PreviousVersionId = x.PreviousVersionId, Stage = x.Stage, Grade = x.Grade, Title = x.Title, Publisher = x.Publisher, Edition = x.Edition,
        Volume = x.Volume, Status = x.Status, PublishedAtUtc = x.PublishedAtUtc, ConcurrencyStamp = x.ConcurrencyStamp
    };
    private async Task<TextbookVersionDto> MapBookAsync(TextbookVersion book)
    {
        var result = Map(book);
        var mappings = await MappingsAsync(book.Id);
        var revisions = await ValidatePointReferencesAsync(book, mappings.Select(x => x.KnowledgePointRevisionId).ToList());
        var pointIds = revisions.Values.Select(x => x.KnowledgePointId).ToList();
        var points = await ReferencesAsync<CourseKnowledgePoint>(x =>
            pointIds.Contains(x.Id) && x.TenantId == book.TenantId && x.SchoolId == book.SchoolId && x.EducationSubjectId == book.EducationSubjectId);
        var codes = points.ToDictionary(x => x.Id, x => x.Code);
        result.Chapters = (await ChaptersAsync(book.Id)).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Code).Select(x => new TextbookChapterDto
        {
            Id = x.Id, ParentChapterId = x.ParentChapterId, Code = x.Code, Title = x.Title, DisplayOrder = x.DisplayOrder,
            HasConfirmedMapping = mappings.Any(mapping => mapping.ChapterId == x.Id)
        }).ToList();
        result.Mappings = mappings.Select(x => new TextbookKnowledgePointMappingDto
        {
            Id = x.Id, ChapterId = x.ChapterId, KnowledgePointId = x.KnowledgePointId, KnowledgePointRevisionId = x.KnowledgePointRevisionId,
            KnowledgePointCode = codes[x.KnowledgePointId], KnowledgePointName = revisions[x.KnowledgePointRevisionId].Name,
            RevisionNumber = revisions[x.KnowledgePointRevisionId].RevisionNumber, ConfirmedByUserId = x.ConfirmedByUserId, ConfirmedAtUtc = x.ConfirmedAtUtc
        }).ToList();
        return result;
    }
    private async Task<CourseKnowledgePointDto> MapPointAsync(CourseKnowledgePoint point)
    {
        var revisions = await ReferencesAsync<CourseKnowledgePointRevision>(x => x.KnowledgePointId == point.Id && x.TenantId == point.TenantId);
        return new CourseKnowledgePointDto
        {
            Id = point.Id, SchoolId = point.SchoolId, EducationSubjectId = point.EducationSubjectId, Code = point.Code,
            CurrentRevisionNumber = point.CurrentRevisionNumber, ConcurrencyStamp = point.ConcurrencyStamp,
            Revisions = revisions.OrderByDescending(x => x.RevisionNumber).Select(x => new CourseKnowledgePointRevisionDto
            {
                Id = x.Id, KnowledgePointId = x.KnowledgePointId, RevisionNumber = x.RevisionNumber, Name = x.Name, ParentRevisionId = x.ParentRevisionId
            }).ToList()
        };
    }
    private async Task<TeacherTextbookSelectionDto> MapSelectionAsync(TeacherTextbookSelection selection)
    {
        var book = await FindBookAsync(selection.TextbookVersionId);
        if (book.SchoolId != selection.SchoolId || book.TenantId != selection.TenantId) throw new AbpAuthorizationException();
        return new TeacherTextbookSelectionDto
        {
            Id = selection.Id, TeachingAssignmentId = selection.TeachingAssignmentId, TextbookVersionId = book.Id, TextbookTitle = book.Title,
            CatalogRevision = book.CatalogRevision, TextbookStatus = book.Status, SelectedAtUtc = selection.SelectedAtUtc, EndedAtUtc = selection.EndedAtUtc
        };
    }
}
