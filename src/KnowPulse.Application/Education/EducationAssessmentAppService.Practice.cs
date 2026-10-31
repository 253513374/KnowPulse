using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationAssessmentAppService
{
    private static BusinessException PracticeInvalid() => new(EducationPracticeErrors.Invalid);
    private async Task PracticeStudentAsync() { await StudentAsync(); await CheckMvpPermissionAsync(EducationPermissions.LearningParticipate); }
    private async Task PracticeTeacherAsync() { await TeacherAsync(); await CheckMvpPermissionAsync(EducationPermissions.LearningManage); }
    private async Task<(EducationAssessmentRecipient Recipient, EducationAssessmentTaskRevision Revision)> PracticeRelationAsync(Guid recipientId, bool student, bool barrier = false)
    {
        if (student) await PracticeStudentAsync(); else await PracticeTeacherAsync();
        var row = await ReferenceAsync<EducationAssessmentRecipient>(recipientId) ?? throw new AbpAuthorizationException();
        var revision = await ReferenceAsync<EducationAssessmentTaskRevision>(row.TaskRevisionId) ?? throw new AbpAuthorizationException();
        if (row.TenantId != CurrentTenant.Id || revision.TenantId != CurrentTenant.Id || revision.TaskId != row.TaskId ||
            revision.SchoolId != row.SchoolId || revision.SchoolClassId != row.SchoolClassId || (student ? row.StudentUserId : revision.OwnerUserId) != CurrentUser.Id)
            throw new AbpAuthorizationException();
        if (student) await Access.EnsureStudentAsync(revision.SchoolId, revision.AcademicTermId, revision.SchoolClassId,
            row.SchoolMembershipId, row.StudentEnrollmentId, row.StudentUserId, barrier);
        else {
            await Access.EnsureTeacherRelationAsync(Parse<EducationCurriculumSnapshot>(revision.CurriculumSnapshotJson), revision.AcademicTermId, revision.SchoolClassId, barrier);
            await Access.ValidateRecordedStudentAsync(revision, row);
        }
        return (row, revision);
    }
    private async Task<EducationQuestionPractice> PracticeAccessAsync(Guid id, bool student, bool barrier = false)
    {
        if (student) await PracticeStudentAsync(); else await PracticeTeacherAsync();
        var row = await ReferenceAsync<EducationQuestionPractice>(id) ?? throw new AbpAuthorizationException();
        if (row.TenantId != CurrentTenant.Id || (student ? row.StudentUserId : row.TeacherUserId) != CurrentUser.Id || !student && !row.SubmittedAtUtc.HasValue)
            throw new AbpAuthorizationException();
        var (recipient, revision) = await PracticeRelationAsync(row.RecipientId, student, barrier);
        if (row.StudentUserId != recipient.StudentUserId || row.TaskId != recipient.TaskId || row.TaskRevisionId != revision.Id || row.TeacherUserId != revision.OwnerUserId ||
            row.ExamPaperId != revision.ExamPaperId) throw new AbpAuthorizationException();
        return row;
    }
    private sealed record PracticeSource(EducationAssessmentRecipient Recipient, EducationAssessmentTaskRevision Revision, EducationAssessmentResultRelease Release,
        EducationAssessmentResultReleaseItem Item, EducationAssessmentReleasedGradeReference Grade, ExamPaperQuestion Question);
    private async Task<PracticeSource> PracticeSourceAsync(Guid recipientId, Guid questionId, bool student, bool barrier = false)
    {
        var (recipient, revision) = await PracticeRelationAsync(recipientId, student, barrier);
        await Access.EnsureRecordedTeacherRelationAsync(Parse<EducationCurriculumSnapshot>(revision.CurriculumSnapshotJson), revision.AcademicTermId, revision.SchoolClassId);
        using var tracking = barrier ? Repo<EducationAssessmentTask>().EnableTracking() : null;
        var task = barrier ? await Repo<EducationAssessmentTask>().GetAsync(recipient.TaskId) : await ReferenceAsync<EducationAssessmentTask>(recipient.TaskId) ?? throw PracticeInvalid();
        if (barrier) await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true);
        var release = task.CurrentResultReleaseId is Guid rid ? await ReferenceAsync<EducationAssessmentResultRelease>(rid) : null;
        if (task.TenantId != CurrentTenant.Id || task.OwnerUserId != revision.OwnerUserId || task.PublishedRevisionId != revision.Id || release == null || release.TenantId != task.TenantId ||
            release.TaskId != task.Id || release.TaskRevisionId != revision.Id || release.ConfirmedGradeSetVersion != task.ConfirmedGradeSetVersion || release.VisibilityAt(DateTime.UtcNow) != ExamResultVisibility.FullReview)
            throw new BusinessException(EducationPracticeErrors.SourceUnavailable);
        var item = (await ReferencesAsync<EducationAssessmentResultReleaseItem>(x => x.ResultReleaseId == release.Id && x.RecipientId == recipientId && x.StudentUserId == recipient.StudentUserId && x.TenantId == CurrentTenant.Id)).SingleOrDefault();
        var reference = item?.Grades.Grades.SingleOrDefault(x => x.PaperQuestionId == questionId);
        var grade = reference == null ? null : await ReferenceAsync<EducationAssessmentQuestionGrade>(reference.QuestionGradeId);
        var confirmed = reference == null ? null : await ReferenceAsync<EducationAssessmentGradeRevision>(reference.GradeRevisionId);
        var question = await ReferenceAsync<ExamPaperQuestion>(questionId);
        if (item == null || reference == null || grade == null || confirmed == null || question == null || grade.TenantId != task.TenantId || confirmed.TenantId != task.TenantId ||
            grade.StudentUserId != recipient.StudentUserId || grade.OwnerUserId != revision.OwnerUserId || grade.TaskRevisionId != revision.Id || grade.SubmissionId != item.SubmissionId ||
            grade.PaperQuestionId != questionId || grade.CurrentConfirmedRevisionId != confirmed.Id || confirmed.QuestionGradeId != grade.Id || confirmed.SubmissionId != item.SubmissionId ||
            confirmed.Kind != EducationAssessmentGradeRevisionKind.Confirmed || question.TenantId != task.TenantId || question.PaperId != revision.ExamPaperId)
            throw new BusinessException(EducationPracticeErrors.SourceUnavailable);
        return new(recipient, revision, release, item, reference, question);
    }
    private async Task<PracticeSource> CurrentPracticeSourceAsync(EducationQuestionPractice row, bool student, bool barrier = false)
    {
        var source = await PracticeSourceAsync(row.RecipientId, row.PaperQuestionId, student, barrier);
        if (source.Revision.Id != row.TaskRevisionId || source.Release.Id != row.ResultReleaseId || source.Grade.GradeRevisionId != row.GradeRevisionId ||
            source.Item.SubmissionId != row.SourceSubmissionId || source.Question.OriginalQuestionId != row.OriginalQuestionId || source.Question.QuestionDefinitionRevisionId != row.QuestionDefinitionRevisionId)
            throw new BusinessException(EducationPracticeErrors.SourceUnavailable);
        return source;
    }
    private static EducationPracticeOperationDto PracticeReceipt(EducationQuestionPractice row, bool submission) => new()
    {
        PracticeId = row.Id, OperationId = submission ? row.SubmissionOperationId!.Value : row.Id, IsSubmission = submission,
        RequestFingerprint = submission ? row.SubmissionFingerprint! : row.StartFingerprint, RecordedAtUtc = submission ? row.SubmittedAtUtc!.Value : row.StartedAtUtc
    };
    private static EducationPracticeListDto PracticeList(EducationQuestionPractice row, Guid schoolId) => new()
    { Id = row.Id, SchoolId = schoolId, RecipientId = row.RecipientId, PaperQuestionId = row.PaperQuestionId, StartedAtUtc = row.StartedAtUtc, SubmittedAtUtc = row.SubmittedAtUtc };
    private static EducationStudentAssessmentQuestionDto PracticeQuestion(ExamPaperQuestion question) => new()
    {
        Id = question.Id, Type = question.Type, Title = question.Title, Stem = question.Stem, DisplayOrder = question.DisplayOrder, Score = question.Score,
        Options = Parse<List<ExamPaperQuestionOptionDto>>(question.OptionsJson), ResponseFields = question.ScoringDefinitionJson == null ? [] :
            QuestionScoringDefinition.Parse(question.ScoringDefinitionJson).Blanks.OrderBy(x => x.DisplayOrder).Select(x => new ExamResponseFieldDto { Id = x.Id, DisplayOrder = x.DisplayOrder, StemMarker = x.StemMarker }).ToList()
    };
    private async Task<EducationPracticeDto> PracticeViewAsync(Guid id, bool student, Guid? schoolId = null)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var row = await PracticeAccessAsync(id, student);
        var revision = await ReferenceAsync<EducationAssessmentTaskRevision>(row.TaskRevisionId) ?? throw new AbpAuthorizationException();
        if (schoolId.HasValue && revision.SchoolId != schoolId) throw new AbpAuthorizationException();
        var dto = GradeDto<EducationPracticeDto>(PracticeList(row, revision.SchoolId));
        dto.SubmissionOperationId = row.SubmissionOperationId;
        dto.SubmittedResponse = row.ResponseJson == null ? null : EducationAssessmentResponseValue.Parse(row.ResponseJson);
        PracticeSource? source = null;
        try {
            source = await CurrentPracticeSourceAsync(row, student);
            if (student && row.SubmittedAtUtc.HasValue) source = await CurrentPracticeSourceAsync(row, true, barrier: true);
        }
        catch (Exception error) when (error is AbpAuthorizationException or BusinessException) { }
        if (source != null) {
            dto.SourceAvailable = true; dto.Question = PracticeQuestion(source.Question);
            if (row.SubmittedAtUtc.HasValue) {
                var review = await StudentReleasedQuestionAsync(await ReferenceAsync<EducationAssessmentSubmission>(row.SourceSubmissionId) ?? throw PracticeInvalid(), source.Grade);
                dto.Feedback = new() { CorrectAnswers = review.CorrectAnswers, Analysis = review.Analysis, ScoringDefinition = review.ScoringDefinition,
                    Evaluation = Parse<EducationPracticeEvaluation>(row.EvaluationJson!) };
                if (student) await LazyServiceProvider.LazyGetRequiredService<EducationQuestionExposureRecorder>().RecordAsync(source.Recipient, source.Release, paperQuestionId: row.PaperQuestionId);
            }
        }
        var images = await ReferencesAsync<EducationPracticeImage>(x => x.PracticeId == row.Id && x.StudentUserId == row.StudentUserId && x.TenantId == row.TenantId && x.UploadedAtUtc != null);
        dto.OwnImages = images.Where(x => student || dto.SubmittedResponse?.Images.Any(image => image.AttachmentId == x.Id) == true).Select(PracticeImageDto).ToList();
        await PracticeAccessAsync(id, student); await uow.CompleteAsync(); return dto;
    }

    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationPracticeOperationDto> StartMyPracticeAsync(Guid recipientId, StartEducationPracticeDto input)
    {
        await PracticeStudentAsync(); EducationRules.RequireId(input.OperationId); EducationRules.RequireId(input.ExpectedResultReleaseId); EducationRules.RequireId(input.PaperQuestionId);
        var fingerprint = EducationPracticeFingerprint.Start(recipientId, input);
        try {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var prior = await ReferenceAsync<EducationQuestionPractice>(input.OperationId);
            if (prior != null) {
                await PracticeAccessAsync(prior.Id, true);
                if (prior.RecipientId != recipientId || prior.StartFingerprint != fingerprint) throw new BusinessException(EducationPracticeErrors.Conflict);
                await uow.CompleteAsync(); return PracticeReceipt(prior, false);
            }
            var source = await PracticeSourceAsync(recipientId, input.PaperQuestionId, true, true);
            if (source.Release.Id != input.ExpectedResultReleaseId) throw new BusinessException(EducationPracticeErrors.SourceUnavailable);
            var row = new EducationQuestionPractice(input.OperationId, source.Recipient, source.Revision, source.Release, source.Item, source.Question, fingerprint, DateTime.UtcNow);
            await Repo<EducationQuestionPractice>().InsertAsync(row, autoSave: true); await uow.CompleteAsync(); return PracticeReceipt(row, false);
        } catch (Exception error) when (Recoverable(error)) {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            var prior = await ReferenceAsync<EducationQuestionPractice>(input.OperationId); if (prior == null) throw;
            await PracticeAccessAsync(prior.Id, true); if (prior.RecipientId != recipientId || prior.StartFingerprint != fingerprint) throw new BusinessException(EducationPracticeErrors.Conflict);
            await recovery.CompleteAsync(); return PracticeReceipt(prior, false);
        }
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationPracticeDto> GetMyPracticeAsync(Guid id) => PracticeViewAsync(id, true);

    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationPracticeOperationDto> StartMyPracticeForSchoolAsync(Guid schoolId, Guid recipientId, StartEducationPracticeDto input)
    {
        EducationRules.RequireId(schoolId);
        using (var read = Uow.Begin(requiresNew: true, isTransactional: false)) {
            var (_, revision) = await PracticeRelationAsync(recipientId, true);
            if (revision.SchoolId != schoolId) throw new AbpAuthorizationException();
            await read.CompleteAsync();
        }
        return await StartMyPracticeAsync(recipientId, input);
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationPracticeDto> GetMyPracticeForSchoolAsync(Guid practiceId, Guid schoolId)
    { EducationRules.RequireId(schoolId); return PracticeViewAsync(practiceId, true, schoolId); }

    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationPracticeOperationDto?> GetMyPracticeStartOperationAsync(Guid schoolId, Guid recipientId, Guid operationId)
    {
        EducationRules.RequireId(schoolId); EducationRules.RequireId(operationId);
        using var read = Uow.Begin(requiresNew: true, isTransactional: false);
        var (_, revision) = await PracticeRelationAsync(recipientId, true);
        if (revision.SchoolId != schoolId) throw new AbpAuthorizationException();
        var row = await ReferenceAsync<EducationQuestionPractice>(operationId);
        if (row == null) { await read.CompleteAsync(); return null; }
        if (row.RecipientId != recipientId || row.TaskRevisionId != revision.Id) throw new AbpAuthorizationException();
        await PracticeAccessAsync(row.Id, true); await read.CompleteAsync(); return PracticeReceipt(row, false);
    }

    private async Task<string> PracticePreparationAsync(EducationQuestionPractice row, EducationAssessmentResponseValue response, PracticeSource source)
    {
        if (row.SubmittedAtUtc.HasValue) throw PracticeInvalid();
        EducationQuestionPractice.ValidateResponse(response);
        EducationAssessmentScoring.Calculate(source.Question, response);
        var images = await PracticeSelectedImagesAsync(row, response);
        return Hash(new { row.Id, row.ConcurrencyStamp, row.ResultReleaseId, row.GradeRevisionId, Response = response.ToCanonicalJson(), Images = images.Select(x => new { x.Id, x.ContentHash, x.Size }) });
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationPracticePreparationDto> PreviewMyPracticeAsync(Guid id, PreviewEducationPracticeDto input)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var row = await PracticeAccessAsync(id, true);
        var source = await CurrentPracticeSourceAsync(row, true, true); var result = new EducationPracticePreparationDto
            { Fingerprint = await PracticePreparationAsync(row, input.Response, source), ServerNowUtc = DateTime.UtcNow };
        await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationPracticeOperationDto> SubmitMyPracticeAsync(Guid id, SubmitEducationPracticeDto input)
    {
        await PracticeStudentAsync(); EducationRules.RequireId(input.OperationId);
        var fingerprint = EducationPracticeFingerprint.Submit(id, input);
        try {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            await PracticeAccessAsync(id, true); using var tracking = Repo<EducationQuestionPractice>().EnableTracking(); var row = await Repo<EducationQuestionPractice>().GetAsync(id);
            if (row.SubmissionOperationId.HasValue) {
                if (row.SubmissionOperationId != input.OperationId || row.SubmissionFingerprint != fingerprint) throw new BusinessException(EducationPracticeErrors.Conflict);
                await uow.CompleteAsync(); return PracticeReceipt(row, true);
            }
            var source = await CurrentPracticeSourceAsync(row, true, true);
            if (await PracticePreparationAsync(row, input.Response, source) != input.PreparationFingerprint) throw new AbpDbConcurrencyException();
            row.Submit(input.OperationId, input.Response, source.Question, input.PreparationFingerprint, fingerprint, input.ConfirmSharingWithTeacher, input.ConfirmPracticeOnly, DateTime.UtcNow);
            await Repo<EducationQuestionPractice>().UpdateManyAsync([row], autoSave: true); await uow.CompleteAsync(); return PracticeReceipt(row, true);
        } catch (Exception error) when (Recoverable(error)) {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false); var row = await PracticeAccessAsync(id, true);
            if (row.SubmissionOperationId != input.OperationId) throw;
            if (row.SubmissionFingerprint != fingerprint) throw new BusinessException(EducationPracticeErrors.Conflict);
            await recovery.CompleteAsync(); return PracticeReceipt(row, true);
        }
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationPracticeOperationDto?> GetMyPracticeOperationAsync(Guid id, Guid operationId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: false); var row = await PracticeAccessAsync(id, true);
        var result = operationId == row.Id ? PracticeReceipt(row, false) : operationId == row.SubmissionOperationId ? PracticeReceipt(row, true) : null;
        await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationPracticeListDto>> GetMyPracticeListAsync(GetEducationPracticeListDto input)
    {
        await PracticeStudentAsync(); if (input.SchoolId.HasValue) EducationRules.RequireId(input.SchoolId.Value);
        var take = Page(input); using var uow = Uow.Begin(requiresNew: true, isTransactional: false);
        var now = DateTime.UtcNow;
        var query = from practice in await Repo<EducationQuestionPractice>().GetQueryableAsync()
            join recipient in await Repo<EducationAssessmentRecipient>().GetQueryableAsync() on practice.RecipientId equals recipient.Id
            join revision in await Repo<EducationAssessmentTaskRevision>().GetQueryableAsync() on practice.TaskRevisionId equals revision.Id
            join school in await Repo<School>().GetQueryableAsync() on revision.SchoolId equals school.Id
            join term in await Repo<AcademicTerm>().GetQueryableAsync() on revision.AcademicTermId equals term.Id
            join schoolClass in await Repo<SchoolClass>().GetQueryableAsync() on revision.SchoolClassId equals schoolClass.Id
            join member in await Repo<SchoolMembership>().GetQueryableAsync() on recipient.SchoolMembershipId equals member.Id
            join enrollment in await Repo<StudentEnrollment>().GetQueryableAsync() on recipient.StudentEnrollmentId equals enrollment.Id
            where practice.TenantId == CurrentTenant.Id && practice.StudentUserId == CurrentUser.Id && recipient.TenantId == CurrentTenant.Id && revision.TenantId == CurrentTenant.Id &&
                school.TenantId == CurrentTenant.Id && term.TenantId == CurrentTenant.Id && schoolClass.TenantId == CurrentTenant.Id && member.TenantId == CurrentTenant.Id && enrollment.TenantId == CurrentTenant.Id &&
                recipient.StudentUserId == practice.StudentUserId && recipient.TaskId == practice.TaskId && recipient.TaskRevisionId == revision.Id && revision.TaskId == practice.TaskId && revision.OwnerUserId == practice.TeacherUserId &&
                recipient.SchoolId == school.Id && recipient.SchoolClassId == schoolClass.Id && term.SchoolId == school.Id && now >= term.StartsAtUtc && now < term.EndsAtUtc &&
                schoolClass.SchoolId == school.Id && schoolClass.AcademicTermId == term.Id && member.SchoolId == school.Id && member.UserId == practice.StudentUserId && member.Role == SchoolMemberRole.Student &&
                member.EffectiveFromUtc <= now && (member.EffectiveToUtc == null || member.EffectiveToUtc > now) && enrollment.SchoolId == school.Id && enrollment.SchoolClassId == schoolClass.Id &&
                enrollment.StudentUserId == practice.StudentUserId && enrollment.EffectiveFromUtc <= now && (enrollment.EffectiveToUtc == null || enrollment.EffectiveToUtc > now) &&
                (!input.SchoolId.HasValue || revision.SchoolId == input.SchoolId) &&
                (!input.RecipientId.HasValue || practice.RecipientId == input.RecipientId) && (!input.PaperQuestionId.HasValue || practice.PaperQuestionId == input.PaperQuestionId)
            select new { Practice = practice, SchoolId = revision.SchoolId };
        var count = await AsyncExecuter.CountAsync(query); var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.Practice.StartedAtUtc).ThenBy(x => x.Practice.Id).Skip(input.SkipCount).Take(take));
        foreach (var row in rows) await PracticeAccessAsync(row.Practice.Id, true);
        await PracticeStudentAsync(); await uow.CompleteAsync(); return new(count, rows.Select(x => PracticeList(x.Practice, x.SchoolId)).ToList());
    }
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationPracticeListDto>> GetPracticeSubmissionsAsync(Guid submissionId, Guid? questionId, PagedResultRequestDto input)
    {
        await PracticeTeacherAsync(); var take = Page(input); using var uow = Uow.Begin(requiresNew: true, isTransactional: false);
        var (submission, attempt) = await GradeSubmissionAsync(submissionId); var (_, revision) = await PracticeRelationAsync(attempt.RecipientId, false);
        var query = (await Repo<EducationQuestionPractice>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.TeacherUserId == CurrentUser.Id &&
            x.SourceSubmissionId == submission.Id && x.SubmittedAtUtc != null && (!questionId.HasValue || x.PaperQuestionId == questionId));
        var count = await AsyncExecuter.CountAsync(query); var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.SubmittedAtUtc).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take));
        await uow.CompleteAsync(); return new(count, rows.Select(x => PracticeList(x, revision.SchoolId)).ToList());
    }
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationPracticeDto> GetPracticeSubmissionAsync(Guid id) => PracticeViewAsync(id, false);
}
