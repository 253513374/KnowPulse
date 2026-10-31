using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationAssessmentAppService
{
    private async Task<(EducationAssessmentRecipient Recipient, EducationAssessmentTaskRevision Revision)> CollectionTargetAsync(Guid schoolId, Guid recipientId, Guid? questionId = null, bool barrier = false)
    {
        EducationRules.RequireId(schoolId);
        var target = await PracticeRelationAsync(recipientId, true, barrier);
        if (target.Revision.SchoolId != schoolId) throw new AbpAuthorizationException();
        if (questionId.HasValue && !await AsyncExecuter.AnyAsync((await Repo<ExamPaperQuestion>().GetQueryableAsync())
            .Where(x => x.Id == questionId && x.PaperId == target.Revision.ExamPaperId && x.TenantId == CurrentTenant.Id))) throw new AbpAuthorizationException();
        return target;
    }
    private async Task<EducationQuestionCollection?> CollectionHeadAsync(Guid recipientId, Guid questionId) => await AsyncExecuter.FirstOrDefaultAsync(
        (await Repo<EducationQuestionCollection>().GetQueryableAsync()).Where(x => x.StudentUserId == CurrentUser.Id && x.TenantId == CurrentTenant.Id &&
            x.RecipientId == recipientId && x.PaperQuestionId == questionId).OrderByDescending(x => x.RevisionNumber));

    private async Task<EducationQuestionCollectionDto> CollectionDtoAsync(EducationQuestionCollection row)
    {
        var (_, revision) = await CollectionTargetAsync(row.SchoolId, row.RecipientId);
        if (row.TenantId != CurrentTenant.Id || row.StudentUserId != CurrentUser.Id || revision.Id != row.TaskRevisionId) throw new AbpAuthorizationException();
        var available = false;
        if (row.IsCollected) {
            try { var source = await PracticeSourceAsync(row.RecipientId, row.PaperQuestionId, true); available = source.Revision.AllowWrongQuestionCollection; }
            catch (Exception error) when (error is BusinessException or AbpAuthorizationException) { }
        }
        await CollectionTargetAsync(row.SchoolId, row.RecipientId);
        return new() { Id = row.Id, SchoolId = row.SchoolId, RecipientId = row.RecipientId, PaperQuestionId = row.PaperQuestionId, RevisionNumber = row.RevisionNumber,
            IsCollected = row.IsCollected, RecordedAtUtc = row.RecordedAtUtc, SourceAvailable = available, TaskTitle = revision.Title };
    }

    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationQuestionCollectionDto> SetMyCollectionAsync(Guid recipientId, SetEducationQuestionCollectionDto input)
    {
        await PracticeStudentAsync(); EducationRules.RequireId(input.OperationId); EducationRules.RequireId(input.PaperQuestionId);
        if (input.ExpectedCurrentOperationId == Guid.Empty || input.ExpectedResultReleaseId == Guid.Empty) throw new BusinessException(EducationQuestionCollectionErrors.Invalid);
        var fingerprint = Hash(new { Command = "question-collection", recipientId, input });
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var (recipient, revision) = await CollectionTargetAsync(input.SchoolId, recipientId, input.PaperQuestionId, barrier: true);
        var previous = await ReferenceAsync<EducationQuestionCollection>(input.OperationId);
        if (previous != null) {
            if (previous.StudentUserId != CurrentUser.Id || previous.TenantId != CurrentTenant.Id || previous.RecipientId != recipientId || previous.RequestFingerprint != fingerprint)
                throw new BusinessException(EducationQuestionCollectionErrors.Conflict);
            var receipt = await CollectionDtoAsync(previous); await uow.CompleteAsync(); return receipt;
        }
        var head = await CollectionHeadAsync(recipientId, input.PaperQuestionId);
        if (head?.Id != input.ExpectedCurrentOperationId) throw new AbpDbConcurrencyException();
        Guid? release = null;
        if (input.IsCollected) {
            try {
                var source = await PracticeSourceAsync(recipientId, input.PaperQuestionId, true, barrier: true);
                if (!source.Revision.AllowWrongQuestionCollection || source.Release.Id != input.ExpectedResultReleaseId) throw new BusinessException(EducationQuestionCollectionErrors.SourceUnavailable);
                release = source.Release.Id;
            } catch (BusinessException) { throw new BusinessException(EducationQuestionCollectionErrors.SourceUnavailable); }
        }
        var row = new EducationQuestionCollection(input.OperationId, recipient, revision, input.PaperQuestionId, input.IsCollected, release, head, fingerprint, DateTime.UtcNow);
        await Repo<EducationQuestionCollection>().InsertAsync(row, autoSave: true);
        var result = await CollectionDtoAsync(row); await uow.CompleteAsync(); return result;
    }

    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)]
    public virtual async Task<EducationQuestionCollectionStateDto> GetMyCollectionAsync(Guid schoolId, Guid recipientId, Guid paperQuestionId)
    {
        await PracticeStudentAsync(); EducationRules.RequireId(paperQuestionId);
        await CollectionTargetAsync(schoolId, recipientId, paperQuestionId);
        var head = await CollectionHeadAsync(recipientId, paperQuestionId);
        var result = new EducationQuestionCollectionStateDto { SchoolId = schoolId, RecipientId = recipientId, PaperQuestionId = paperQuestionId,
            Current = head == null ? null : await CollectionDtoAsync(head) };
        try { var source = await PracticeSourceAsync(recipientId, paperQuestionId, true);
            if (source.Revision.AllowWrongQuestionCollection) { result.CanCollect = true; result.ResultReleaseId = source.Release.Id; } }
        catch (Exception error) when (error is BusinessException or AbpAuthorizationException) { }
        await CollectionTargetAsync(schoolId, recipientId);
        return result;
    }

    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)]
    public virtual async Task<EducationQuestionCollectionDto?> GetMyCollectionOperationAsync(Guid schoolId, Guid recipientId, Guid operationId)
    {
        await PracticeStudentAsync(); EducationRules.RequireId(operationId); await CollectionTargetAsync(schoolId, recipientId);
        var row = await ReferenceAsync<EducationQuestionCollection>(operationId); if (row == null) return null;
        if (row.StudentUserId != CurrentUser.Id || row.TenantId != CurrentTenant.Id || row.RecipientId != recipientId || row.SchoolId != schoolId) throw new AbpAuthorizationException();
        return await CollectionDtoAsync(row);
    }

    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)]
    public virtual async Task<PagedResultDto<EducationQuestionCollectionDto>> GetMyCollectionsAsync(GetEducationQuestionCollectionsDto input)
    {
        await PracticeStudentAsync(); EducationRules.RequireId(input.SchoolId); var take = Page(input); var now = DateTime.UtcNow;
        var all = (await Repo<EducationQuestionCollection>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.StudentUserId == CurrentUser.Id && x.SchoolId == input.SchoolId);
        var latest = all.Where(x => x.IsCollected && !all.Any(y => y.RecipientId == x.RecipientId && y.PaperQuestionId == x.PaperQuestionId && y.RevisionNumber > x.RevisionNumber));
        var query = from row in latest
            join recipient in await Repo<EducationAssessmentRecipient>().GetQueryableAsync() on row.RecipientId equals recipient.Id
            join revision in await Repo<EducationAssessmentTaskRevision>().GetQueryableAsync() on row.TaskRevisionId equals revision.Id
            join school in await Repo<School>().GetQueryableAsync() on row.SchoolId equals school.Id
            join term in await Repo<AcademicTerm>().GetQueryableAsync() on revision.AcademicTermId equals term.Id
            join schoolClass in await Repo<SchoolClass>().GetQueryableAsync() on revision.SchoolClassId equals schoolClass.Id
            join member in await Repo<SchoolMembership>().GetQueryableAsync() on recipient.SchoolMembershipId equals member.Id
            join enrollment in await Repo<StudentEnrollment>().GetQueryableAsync() on recipient.StudentEnrollmentId equals enrollment.Id
            where recipient.TenantId == CurrentTenant.Id && revision.TenantId == CurrentTenant.Id && school.TenantId == CurrentTenant.Id && term.TenantId == CurrentTenant.Id &&
                schoolClass.TenantId == CurrentTenant.Id && member.TenantId == CurrentTenant.Id && enrollment.TenantId == CurrentTenant.Id &&
                recipient.StudentUserId == row.StudentUserId && recipient.TaskId == row.TaskId && recipient.TaskRevisionId == revision.Id && revision.TaskId == row.TaskId && revision.SchoolId == school.Id &&
                recipient.SchoolId == school.Id && recipient.SchoolClassId == schoolClass.Id && term.SchoolId == school.Id && now >= term.StartsAtUtc && now < term.EndsAtUtc &&
                schoolClass.SchoolId == school.Id && schoolClass.AcademicTermId == term.Id && member.SchoolId == school.Id && member.UserId == row.StudentUserId && member.Role == SchoolMemberRole.Student &&
                member.EffectiveFromUtc <= now && (member.EffectiveToUtc == null || member.EffectiveToUtc > now) && enrollment.SchoolId == school.Id && enrollment.SchoolClassId == schoolClass.Id &&
                enrollment.StudentUserId == row.StudentUserId && enrollment.EffectiveFromUtc <= now && (enrollment.EffectiveToUtc == null || enrollment.EffectiveToUtc > now)
            select row;
        var count = await AsyncExecuter.CountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.RecordedAtUtc).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take));
        var result = new System.Collections.Generic.List<EducationQuestionCollectionDto>();
        foreach (var row in rows) result.Add(await CollectionDtoAsync(row));
        await PracticeStudentAsync(); return new(count, result);
    }
}
