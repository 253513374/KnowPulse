using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace KnowPulse.Education;

[RemoteService(false)]
public class EducationCurriculumAccessGuard : KnowPulseAppService
{
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();

    private async Task<T?> FindAsync<T>(Guid id, CancellationToken ct) where T : class, IEntity<Guid>
    {
        using var tracking = Repo<T>().DisableTracking();
        return await Repo<T>().FindAsync(id, cancellationToken: ct);
    }

    public virtual async Task<EducationCurriculumSnapshot> ResolveAsync(EducationCurriculumScopeDto input, CancellationToken ct = default)
    {
        if (input == null || input.TeacherTextbookSelectionId == Guid.Empty || input.TextbookKnowledgePointMappingIds == null ||
            input.TextbookKnowledgePointMappingIds.Count is < 1 or > 3000 || input.TextbookKnowledgePointMappingIds.Contains(Guid.Empty) ||
            input.TextbookKnowledgePointMappingIds.Distinct().Count() != input.TextbookKnowledgePointMappingIds.Count)
            throw new BusinessException(EducationCurriculumErrorCodes.InvalidStructure);
        var account = await CurrentAccountAsync(ct);
        var selection = await FindAsync<TeacherTextbookSelection>(input.TeacherTextbookSelectionId, ct);
        if (selection == null || selection.TenantId != CurrentTenant.Id || selection.TeacherUserId != account.Id || selection.EndedAtUtc.HasValue)
            throw new AbpAuthorizationException();
        var school = await SchoolAsync(selection.SchoolId, account.Id, teacherOnly: true, ct);
        var assignment = await FindAsync<TeachingAssignment>(selection.TeachingAssignmentId, ct);
        var now = DateTime.UtcNow;
        if (assignment == null || assignment.TenantId != school.TenantId || assignment.SchoolId != school.Id ||
            assignment.TeacherUserId != account.Id || !assignment.IsEffectiveAt(now)) throw new AbpAuthorizationException();
        var term = await FindAsync<AcademicTerm>(assignment.AcademicTermId, ct);
        var schoolClass = await FindAsync<SchoolClass>(assignment.SchoolClassId, ct);
        if (term == null || schoolClass == null || term.TenantId != school.TenantId || term.SchoolId != school.Id ||
            now < term.StartsAtUtc || now >= term.EndsAtUtc || schoolClass.TenantId != school.TenantId ||
            schoolClass.SchoolId != school.Id || schoolClass.AcademicTermId != term.Id)
            throw new AbpAuthorizationException();
        var book = await PublishedBookAsync(selection.TextbookVersionId, ct);
        if (book.SchoolId != school.Id || book.EducationSubjectId != assignment.EducationSubjectId || book.Grade != schoolClass.Grade)
            throw new BusinessException(EducationCurriculumErrorCodes.ReferenceMismatch);
        var result = await SnapshotAsync(book, input.TextbookKnowledgePointMappingIds, account.Id, ct);
        result.TeachingAssignmentId = assignment.Id;
        result.TeacherTextbookSelectionId = selection.Id;
        return result;
    }

    public virtual async Task ValidateAsync(EducationCurriculumSnapshot snapshot, CancellationToken ct = default)
    {
        if (snapshot == null || snapshot.TenantId != CurrentTenant.Id || snapshot.TeacherUserId != CurrentUser.Id || snapshot.Mappings == null)
            throw new AbpAuthorizationException();
        var current = await ResolveAsync(new EducationCurriculumScopeDto
        {
            TeacherTextbookSelectionId = snapshot.TeacherTextbookSelectionId,
            TextbookKnowledgePointMappingIds = snapshot.Mappings.Select(x => x.MappingId).ToList()
        }, ct);
        if (current.SchoolId != snapshot.SchoolId || current.TeachingAssignmentId != snapshot.TeachingAssignmentId ||
            current.TextbookVersionId != snapshot.TextbookVersionId || current.EducationSubjectId != snapshot.EducationSubjectId ||
            current.Grade != snapshot.Grade || current.CatalogRevision != snapshot.CatalogRevision ||
            current.Mappings.Any(row => !snapshot.Mappings.Any(old => old.MappingId == row.MappingId && old.ChapterId == row.ChapterId &&
                old.KnowledgePointId == row.KnowledgePointId && old.KnowledgePointRevisionId == row.KnowledgePointRevisionId)))
            throw new BusinessException(EducationCurriculumErrorCodes.ReferenceMismatch);
    }

    public virtual async Task ValidateForUseAsync(EducationCurriculumSnapshot snapshot, CancellationToken ct = default)
    {
        if (snapshot == null || snapshot.TenantId != CurrentTenant.Id || snapshot.TeacherUserId != CurrentUser.Id)
            throw new AbpAuthorizationException();
        var account = await Repo<IdentityUser>().FindAsync(snapshot.TeacherUserId, cancellationToken: ct);
        if (account == null || !account.IsActive || account.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        var school = await Repo<School>().FindAsync(snapshot.SchoolId, cancellationToken: ct);
        if (school == null || school.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        await ValidateAsync(snapshot, ct);
        // Use the same school concurrency barrier as assignment, selection and catalog changes.
        await Repo<School>().UpdateManyAsync([school], autoSave: true, cancellationToken: ct);
        await Repo<IdentityUser>().UpdateManyAsync([account], autoSave: true, cancellationToken: ct);
        await ValidateAsync(snapshot, ct);
    }

    private async Task<IdentityUser> CurrentAccountAsync(CancellationToken ct)
    {
        if (!CurrentUser.IsAuthenticated || !CurrentUser.Id.HasValue) throw new AbpAuthorizationException();
        var account = await FindAsync<IdentityUser>(CurrentUser.Id.Value, ct);
        if (account == null || !account.IsActive || account.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        return account;
    }

    private async Task<School> SchoolAsync(Guid id, Guid userId, bool teacherOnly, CancellationToken ct)
    {
        var school = await FindAsync<School>(id, ct);
        if (school == null || school.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        var now = DateTime.UtcNow;
        var members = await Repo<SchoolMembership>().GetQueryableAsync();
        if (!await AsyncExecuter.AnyAsync(members.Where(x => x.TenantId == school.TenantId && x.SchoolId == id && x.UserId == userId &&
            x.EffectiveFromUtc <= now && (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc) &&
            (x.Role == SchoolMemberRole.Teacher || (!teacherOnly && x.Role == SchoolMemberRole.Manager))), ct))
            throw new AbpAuthorizationException();
        return school;
    }

    private async Task<TextbookVersion> PublishedBookAsync(Guid id, CancellationToken ct)
    {
        var book = await FindAsync<TextbookVersion>(id, ct);
        if (book == null || book.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        if (book.Status != TextbookVersionStatus.Published) throw new BusinessException(EducationCurriculumErrorCodes.PublishedVersionRequired);
        return book;
    }

    private async Task<EducationCurriculumSnapshot> SnapshotAsync(TextbookVersion book, List<Guid> ids, Guid userId, CancellationToken ct)
    {
        var subject = await FindAsync<EducationSubject>(book.EducationSubjectId, ct);
        if (subject == null || subject.TenantId != book.TenantId) throw new BusinessException(EducationCurriculumErrorCodes.ReferenceMismatch);
        using var mappingTracking = Repo<TextbookKnowledgePointMapping>().DisableTracking();
        using var chapterTracking = Repo<TextbookChapter>().DisableTracking();
        using var revisionTracking = Repo<CourseKnowledgePointRevision>().DisableTracking();
        using var pointTracking = Repo<CourseKnowledgePoint>().DisableTracking();
        var mappings = await Repo<TextbookKnowledgePointMapping>().GetQueryableAsync();
        var chapters = await Repo<TextbookChapter>().GetQueryableAsync();
        var revisions = await Repo<CourseKnowledgePointRevision>().GetQueryableAsync();
        var points = await Repo<CourseKnowledgePoint>().GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(
            from mapping in mappings
            join chapter in chapters on new { mapping.ChapterId, mapping.TextbookVersionId } equals new { ChapterId = chapter.Id, chapter.TextbookVersionId }
            join revision in revisions on new { mapping.KnowledgePointRevisionId, mapping.KnowledgePointId } equals new { KnowledgePointRevisionId = revision.Id, revision.KnowledgePointId }
            join point in points on revision.KnowledgePointId equals point.Id
            where ids.Contains(mapping.Id) && mapping.TenantId == book.TenantId && mapping.TextbookVersionId == book.Id &&
                chapter.TenantId == book.TenantId && revision.TenantId == book.TenantId && point.TenantId == book.TenantId &&
                point.SchoolId == book.SchoolId && point.EducationSubjectId == book.EducationSubjectId
            orderby chapter.DisplayOrder, chapter.Code, point.Code
            select new EducationCurriculumMappingSnapshot
            {
                MappingId = mapping.Id, ChapterId = chapter.Id, ChapterCode = chapter.Code, ChapterTitle = chapter.Title,
                KnowledgePointId = point.Id, KnowledgePointRevisionId = revision.Id, KnowledgePointCode = point.Code,
                KnowledgePointName = revision.Name, RevisionNumber = revision.RevisionNumber
            }, ct);
        if (rows.Count != ids.Distinct().Count()) throw new BusinessException(EducationCurriculumErrorCodes.ReferenceMismatch);
        return new EducationCurriculumSnapshot
        {
            TenantId = book.TenantId, TeacherUserId = userId, SchoolId = book.SchoolId, TextbookVersionId = book.Id,
            EducationSubjectId = subject.Id, SubjectName = subject.Name, Grade = book.Grade,
            CatalogCode = book.CatalogCode, CatalogRevision = book.CatalogRevision, TextbookTitle = book.Title,
            Publisher = book.Publisher, Edition = book.Edition, Volume = book.Volume, CapturedAtUtc = DateTime.UtcNow, Mappings = rows
        };
    }
}
