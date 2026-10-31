using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationAssessmentAppService
{
    private static EducationCorrectionRevisionDto CorrectionRevision(EducationCorrectionTaskRevision value) => new() { Id = value.Id, TaskId = value.TaskId, RevisionNumber = value.RevisionNumber,
        BasedOnRevisionId = value.BasedOnRevisionId, Kind = value.Kind, Content = value.Content(), GradeRevisionId = value.GradeRevisionId, ResultReleaseId = value.ResultReleaseId, PublicReason = value.PublicReason,
        StudentName = value.StudentName, ClassName = value.ClassName, SubjectName = value.SubjectName, RecordedAtUtc = value.RecordedAtUtc, RequestFingerprint = value.RequestFingerprint };
    private async Task<EducationCorrectionTaskRevision> CorrectionHeadAsync(EducationCorrectionTask root)
    {
        var row = await ReferenceAsync<EducationCorrectionTaskRevision>(root.CurrentRevisionId ?? throw CorrectionInvalid()) ?? throw CorrectionInvalid();
        if (row.TaskId != root.Id || row.TenantId != root.TenantId || row.StudentUserId != root.StudentUserId || row.OwnerUserId != root.OwnerUserId || row.RevisionNumber != root.RevisionNumber) throw CorrectionInvalid(); return row;
    }
    private static void CorrectionStamp(EducationCorrectionTask root, Guid? expected, string? stamp)
    { if (root.CurrentRevisionId != expected || root.ConcurrencyStamp != stamp) throw new AbpDbConcurrencyException(); }
    private async Task<EducationCorrectionTaskRevision?> CorrectionReceiptAsync(Guid taskId, Guid operationId, string? fingerprint = null)
    {
        await CorrectionTeacherAsync(); if (await ReferenceAsync<EducationCorrectionTask>(taskId) == null) return null; await CorrectionAccessAsync(taskId, false);
        var row = await ReferenceAsync<EducationCorrectionTaskRevision>(operationId); if (row == null) return null;
        if (row.TenantId != CurrentTenant.Id || row.OwnerUserId != CurrentUser.Id || row.TaskId != taskId) throw new AbpAuthorizationException();
        if (fingerprint != null && row.RequestFingerprint != fingerprint) throw new BusinessException(EducationCorrectionErrors.Conflict); return row;
    }
    private async Task AppendCorrectionAsync(EducationCorrectionTask root, EducationCorrectionTaskRevision revision)
    { root.Advance(revision); await Repo<EducationCorrectionTaskRevision>().InsertAsync(revision, autoSave: true); await Repo<EducationCorrectionTask>().UpdateManyAsync([root], autoSave: true); }
    private async Task<(DateTime From, DateTime To)> CorrectionWindowAsync(EducationCorrectionTask root)
    {
        var term = await ReferenceAsync<AcademicTerm>(root.AcademicTermId) ?? throw CorrectionInvalid(); var teaching = await ReferenceAsync<TeachingAssignment>(root.TeachingAssignmentId) ?? throw CorrectionInvalid();
        var member = await ReferenceAsync<SchoolMembership>(root.SchoolMembershipId) ?? throw CorrectionInvalid(); var enrollment = await ReferenceAsync<StudentEnrollment>(root.StudentEnrollmentId) ?? throw CorrectionInvalid();
        var from = new[] { term.StartsAtUtc, teaching.EffectiveFromUtc, member.EffectiveFromUtc, enrollment.EffectiveFromUtc }.Max();
        var to = new DateTime?[] { term.EndsAtUtc, teaching.EffectiveToUtc, member.EffectiveToUtc, enrollment.EffectiveToUtc }.Where(x => x.HasValue).Select(x => x!.Value).Min(); return (from, to);
    }
    private async Task<EducationCorrectionTargetDto> CorrectionTargetAsync(EducationCorrectionTask root, CorrectionGate gate)
    {
        var recipient = gate.Recipient; var revision = await ReferenceAsync<EducationAssessmentTaskRevision>(root.SourceTaskRevisionId) ?? throw CorrectionInvalid();
        var question = await ReferenceAsync<KnowPulse.Exam.ExamPaperQuestion>(root.PaperQuestionId) ?? throw CorrectionInvalid();
        var scope = Parse<EducationCurriculumSnapshot>(revision.CurriculumSnapshotJson); var window = await CorrectionWindowAsync(root);
        return new() { SourceTaskId = root.SourceTaskId, QuestionGradeId = root.QuestionGradeId, GradeRevisionId = gate.Confirmed.Id, ResultReleaseId = gate.Release?.Id, FullReviewAvailable = gate.FullReview,
            StudentName = recipient.StudentName, ClassName = revision.SchoolClassName, SubjectName = scope.SubjectName, QuestionTitle = question.Title, QuestionStem = question.Stem,
            MaxScore = gate.Confirmed.MaxScore, ObtainedScore = gate.Confirmed.ObtainedScore ?? throw CorrectionInvalid(), AvailableFromUtc = window.From, AvailableToUtc = window.To };
    }
    private async Task<EducationCorrectionTask> NewCorrectionRootAsync(Guid id, Guid gradeId)
    {
        await CorrectionTeacherAsync(); var source = await EvidenceSourceAsync(gradeId, write: true);
        var recipient = await ReferenceAsync<EducationAssessmentRecipient>(source.Attempt.RecipientId) ?? throw CorrectionInvalid();
        var root = new EducationCorrectionTask(id, source.Grade, source.Attempt, recipient, source.TaskRevision, source.Scope);
        await CorrectionStudentRelationAsync(root, write: true); return root;
    }
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationCorrectionTargetDto> GetCorrectionTargetAsync(Guid questionGradeId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var root = await NewCorrectionRootAsync(Guid.NewGuid(), questionGradeId);
        var result = await CorrectionTargetAsync(root, await CorrectionGateAsync(root)); await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationCorrectionRevisionDto> SaveCorrectionDraftAsync(Guid taskId, SaveEducationCorrectionDraftDto input)
    {
        await CorrectionTeacherAsync(); EducationRules.RequireId(taskId); EducationRules.RequireId(input.OperationId); var fingerprint = EducationCorrectionFingerprint.Draft(taskId, input);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var prior = await CorrectionReceiptAsync(taskId, input.OperationId, fingerprint);
        if (prior != null) { await uow.CompleteAsync(); return CorrectionRevision(prior); }
        var existing = await ReferenceAsync<EducationCorrectionTask>(taskId); EducationCorrectionTask root;
        if (existing == null) { if (input.ExpectedRevisionId != null || input.ConcurrencyStamp != null) throw new AbpDbConcurrencyException(); root = await NewCorrectionRootAsync(taskId, input.QuestionGradeId); }
        else { await CorrectionAccessAsync(taskId, false); using var tracking = Repo<EducationCorrectionTask>().EnableTracking(); root = await Repo<EducationCorrectionTask>().GetAsync(taskId); CorrectionStamp(root, input.ExpectedRevisionId, input.ConcurrencyStamp);
            if (root.QuestionGradeId != input.QuestionGradeId) throw CorrectionInvalid(); }
        input.Content.Validate(); var window = await CorrectionWindowAsync(root); if (input.Content.StartsAtUtc < window.From || input.Content.DueAtUtc > window.To) throw CorrectionInvalid();
        var gate = await CorrectionGateAsync(root); var target = await CorrectionTargetAsync(root, gate); if (target.ResultReleaseId == null) throw new BusinessException(EducationCorrectionErrors.Closed);
        var teacher = await ReferenceAsync<IdentityUser>(root.OwnerUserId) ?? throw CorrectionInvalid(); var name = string.Join(" ", new[] { teacher.Name, teacher.Surname }.Where(x => !string.IsNullOrWhiteSpace(x))); if (name.Length == 0) name = teacher.UserName;
        var revision = new EducationCorrectionTaskRevision(input.OperationId, root, EducationCorrectionRevisionKind.Draft, input.Content, target.GradeRevisionId, target.ResultReleaseId.Value,
            target.StudentName, target.ClassName, target.SubjectName, name, null, null, fingerprint, DateTime.UtcNow);
        if (existing == null) await Repo<EducationCorrectionTask>().InsertAsync(root, autoSave: true); await AppendCorrectionAsync(root, revision); await uow.CompleteAsync(); return CorrectionRevision(revision);
    }
    private async Task<EducationCorrectionTaskDto> CorrectionTaskDtoAsync(EducationCorrectionTask root) => new() { Id = root.Id, QuestionGradeId = root.QuestionGradeId, ConcurrencyStamp = root.ConcurrencyStamp,
        PublishedRevisionId = root.PublishedRevisionId, LastPublishedRevisionId = root.LastPublishedRevisionId, Current = CorrectionRevision(await CorrectionHeadAsync(root)) };
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationCorrectionTaskDto> GetCorrectionAsync(Guid taskId)
    { using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var value = await CorrectionTaskDtoAsync(await CorrectionAccessAsync(taskId, false)); await uow.CompleteAsync(); return value; }
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)]
    public virtual async Task<PagedResultDto<EducationCorrectionTaskDto>> GetCorrectionListAsync(PagedResultRequestDto input)
    {
        await CorrectionTeacherAsync(); var take = Page(input); var roots = await AsyncExecuter.ToListAsync((await Repo<EducationCorrectionTask>().GetQueryableAsync()).Where(x => x.TenantId == CurrentTenant.Id && x.OwnerUserId == CurrentUser.Id).OrderByDescending(x => x.CreationTime).ThenBy(x => x.Id).Take(1001));
        if (roots.Count > 1000) throw new BusinessException(EducationLearningReportErrors.Capacity); var values = new List<EducationCorrectionTaskDto>();
        foreach (var candidate in roots) { try { values.Add(await CorrectionTaskDtoAsync(await CorrectionAccessAsync(candidate.Id, false, write: false))); } catch (AbpAuthorizationException) { } }
        return new(values.Count, values.Skip(input.SkipCount).Take(take).ToList());
    }
    private async Task<EducationCorrectionPublishPreviewDto> CorrectionPublishPreviewAsync(EducationCorrectionTask root)
    {
        var source = await CorrectionHeadAsync(root); if (source.Kind != EducationCorrectionRevisionKind.Draft) throw CorrectionInvalid(); var gate = await CorrectionGateAsync(root); var target = await CorrectionTargetAsync(root, gate);
        if (!gate.FullReview || source.GradeRevisionId != gate.Confirmed.Id || source.ResultReleaseId != gate.Release!.Id || source.Content()!.DueAtUtc <= DateTime.UtcNow) throw new BusinessException(EducationCorrectionErrors.Changed);
        var window = await CorrectionWindowAsync(root); if (source.Content()!.StartsAtUtc < window.From || source.Content()!.DueAtUtc > window.To) throw CorrectionInvalid();
        return new() { DraftRevisionId = source.Id, ConcurrencyStamp = root.ConcurrencyStamp, Target = target,
            Fingerprint = Hash(new { root.Id, root.CurrentRevisionId, root.ConcurrencyStamp, source.ContentJson, source.GradeRevisionId, Release = gate.Release.Id, window.From, window.To, target.StudentName, target.ClassName }) };
    }
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationCorrectionPublishPreviewDto> PreviewCorrectionPublishAsync(Guid taskId)
    { using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var result = await CorrectionPublishPreviewAsync(await CorrectionAccessAsync(taskId, false)); await uow.CompleteAsync(); return result; }
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationCorrectionRevisionDto> PublishCorrectionAsync(Guid taskId, PublishEducationCorrectionDto input)
    {
        await CorrectionTeacherAsync(); EducationRules.RequireId(input.OperationId); var fingerprint = EducationCorrectionFingerprint.Publish(taskId, input);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var prior = await CorrectionReceiptAsync(taskId, input.OperationId, fingerprint); if (prior != null) { await uow.CompleteAsync(); return CorrectionRevision(prior); }
        await CorrectionAccessAsync(taskId, false); using var tracking = Repo<EducationCorrectionTask>().EnableTracking(); var root = await Repo<EducationCorrectionTask>().GetAsync(taskId);
        CorrectionStamp(root, input.ExpectedRevisionId, input.ConcurrencyStamp); var preview = await CorrectionPublishPreviewAsync(root);
        if (!input.ConfirmRecipientSourceAndSafeText || input.PreviewFingerprint != preview.Fingerprint) throw new BusinessException(EducationCorrectionErrors.Changed); var source = await CorrectionHeadAsync(root);
        var revision = new EducationCorrectionTaskRevision(input.OperationId, root, EducationCorrectionRevisionKind.Published, source.Content(), source.GradeRevisionId, source.ResultReleaseId,
            source.StudentName, source.ClassName, source.SubjectName, source.TeacherName, input.PublicReason, source, fingerprint, DateTime.UtcNow);
        root.Advance(revision); await Repo<EducationCorrectionTaskRevision>().InsertAsync(revision, autoSave: true); await Repo<EducationCorrectionDelivery>().InsertAsync(new(revision), autoSave: true);
        await Repo<EducationCorrectionTask>().UpdateManyAsync([root], autoSave: true); await uow.CompleteAsync(); return CorrectionRevision(revision);
    }
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationCorrectionRevisionDto> WithdrawCorrectionAsync(Guid taskId, WithdrawEducationCorrectionDto input)
    {
        await CorrectionTeacherAsync(); EducationRules.RequireId(input.OperationId); var fingerprint = EducationCorrectionFingerprint.Withdraw(taskId, input);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var prior = await CorrectionReceiptAsync(taskId, input.OperationId, fingerprint); if (prior != null) { await uow.CompleteAsync(); return CorrectionRevision(prior); }
        await CorrectionAccessAsync(taskId, false); using var tracking = Repo<EducationCorrectionTask>().EnableTracking(); var root = await Repo<EducationCorrectionTask>().GetAsync(taskId);
        CorrectionStamp(root, input.ExpectedRevisionId, input.ConcurrencyStamp); if (!input.ConfirmStudentVisibleWithdrawal) throw CorrectionInvalid(); var source = await CorrectionHeadAsync(root);
        var revision = new EducationCorrectionTaskRevision(input.OperationId, root, EducationCorrectionRevisionKind.Withdrawn, null, source.GradeRevisionId, source.ResultReleaseId,
            source.StudentName, source.ClassName, source.SubjectName, source.TeacherName, input.PublicReason, source, fingerprint, DateTime.UtcNow);
        await AppendCorrectionAsync(root, revision); await uow.CompleteAsync(); return CorrectionRevision(revision);
    }
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationCorrectionRevisionDto?> GetCorrectionOperationAsync(Guid taskId, Guid operationId)
    { using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var value = await CorrectionReceiptAsync(taskId, operationId); await uow.CompleteAsync(); return value == null ? null : CorrectionRevision(value); }
}
