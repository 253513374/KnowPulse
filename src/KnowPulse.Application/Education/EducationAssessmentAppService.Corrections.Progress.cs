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
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationAssessmentAppService
{
    private static EducationCorrectionSubmissionDto CorrectionSubmission(EducationCorrectionSubmission value) => new() { Id = value.Id, DeliveryId = value.DeliveryId, SubmissionNumber = value.SubmissionNumber,
        BasedOnSubmissionId = value.BasedOnSubmissionId, Content = value.Content(), SubmittedAtUtc = value.SubmittedAtUtc, RequestFingerprint = value.RequestFingerprint };
    private static EducationCorrectionReviewDto CorrectionReview(EducationCorrectionReview value) => new() { Id = value.Id, DeliveryId = value.DeliveryId, SubmissionId = value.SubmissionId,
        BasedOnReviewId = value.BasedOnReviewId, ReviewNumber = value.ReviewNumber, Decision = value.Decision, StudentFeedback = value.StudentFeedback, PublishedAtUtc = value.PublishedAtUtc, RequestFingerprint = value.RequestFingerprint };
    private async Task<EducationCorrectionSubmission?> CorrectionSubmittedHeadAsync(EducationCorrectionDelivery delivery)
    {
        var value = delivery.CurrentSubmissionId is Guid id ? await ReferenceAsync<EducationCorrectionSubmission>(id) ?? throw CorrectionInvalid() : null;
        if (value != null && (value.DeliveryId != delivery.Id || value.TenantId != delivery.TenantId || value.StudentUserId != delivery.StudentUserId || value.OwnerUserId != delivery.OwnerUserId || value.SubmissionNumber != delivery.SubmissionNumber)) throw CorrectionInvalid(); return value;
    }
    private async Task<EducationMyCorrectionDto> CorrectionViewAsync(EducationCorrectionDelivery delivery, EducationCorrectionTask root, EducationCorrectionTaskRevision publication, bool body, bool student)
    {
        var gate = await CorrectionGateAsync(root, barrier: body); var status = await CorrectionAvailabilityAsync(root, publication, gate); var content = publication.Content()!;
        var mayRead = status is EducationCorrectionAvailability.Available or EducationCorrectionAvailability.Overdue;
        EducationAssessmentStudentQuestionResultDto? question = null;
        if (student && body && mayRead) {
            using var tracking = Repo<EducationCorrectionDelivery>().EnableTracking(); delivery = await Repo<EducationCorrectionDelivery>().GetAsync(delivery.Id);
            delivery.MarkProvided(DateTime.UtcNow); await Repo<EducationCorrectionDelivery>().UpdateManyAsync([delivery], autoSave: true);
            question = await StudentReleasedQuestionAsync(gate.Submission, gate.Reference!);
            await LazyServiceProvider.LazyGetRequiredService<EducationQuestionExposureRecorder>().RecordAsync(gate.Recipient, gate.Release!, paperQuestionId: root.PaperQuestionId);
        }
        var submitted = body ? await CorrectionSubmittedHeadAsync(delivery) : null; EducationCorrectionReview? review = null;
        if ((!student || gate.FullReview && gate.Confirmed.Id == publication.GradeRevisionId) && delivery.CurrentReviewId is Guid rid) {
            var actual = await ReferenceAsync<EducationCorrectionReview>(rid) ?? throw CorrectionInvalid();
            if (actual.DeliveryId != delivery.Id || actual.TenantId != delivery.TenantId || actual.StudentUserId != delivery.StudentUserId || actual.OwnerUserId != delivery.OwnerUserId) throw CorrectionInvalid();
            if (actual.SubmissionId == delivery.CurrentSubmissionId) review = actual;
        }
        string? reason = null;
        if (gate.FullReview && gate.Confirmed.Id == publication.GradeRevisionId && body) {
            if (root.PublishedRevisionId is Guid pid && pid != publication.Id) reason = (await ReferenceAsync<EducationCorrectionTaskRevision>(pid))?.PublicReason;
            else if (root.PublishedRevisionId == null) reason = (await AsyncExecuter.FirstOrDefaultAsync((await Repo<EducationCorrectionTaskRevision>().GetQueryableAsync())
                .Where(x => x.TaskId == root.Id && x.TenantId == root.TenantId && x.Kind == EducationCorrectionRevisionKind.Withdrawn).OrderByDescending(x => x.RevisionNumber)))?.PublicReason;
        }
        EducationCorrectionWorkState? workState = gate.FullReview && gate.Confirmed.Id == publication.GradeRevisionId
            ? delivery.CurrentSubmissionId == null ? EducationCorrectionWorkState.NotSubmitted : review == null ? EducationCorrectionWorkState.AwaitingReview
                : review.Decision == EducationCorrectionReviewDecision.Accepted ? EducationCorrectionWorkState.Accepted : EducationCorrectionWorkState.NeedsRevision : null;
        return new() { SchoolId = root.SchoolId, WorkState = workState, Id = delivery.Id, TaskId = root.Id, PublicationId = publication.Id, VersionNumber = publication.RevisionNumber, AssessmentRecipientId = root.AssessmentRecipientId,
            Title = body && (!student || mayRead) ? content.Title : null, TeacherName = publication.TeacherName, ClassName = publication.ClassName, SubjectName = publication.SubjectName,
            Instructions = body && (!student || mayRead) ? content.Instructions : null, PublicReason = reason, StartsAtUtc = content.StartsAtUtc, DueAtUtc = content.DueAtUtc, EstimatedMinutes = content.EstimatedMinutes,
            ServerNowUtc = DateTime.UtcNow, Availability = status, CurrentSubmissionId = delivery.CurrentSubmissionId, CurrentSubmission = submitted == null ? null : CorrectionSubmission(submitted),
            CurrentReview = !body || review == null ? null : CorrectionReview(review), OriginalQuestion = question };
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationMyCorrectionDto> GetMyCorrectionAsync(Guid deliveryId)
        => GetMyCorrectionCoreAsync(deliveryId, null);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationMyCorrectionDto> GetMyCorrectionForSchoolAsync(Guid deliveryId, Guid schoolId)
        => GetMyCorrectionCoreAsync(deliveryId, EducationRules.RequireId(schoolId));
    private async Task<EducationMyCorrectionDto> GetMyCorrectionCoreAsync(Guid deliveryId, Guid? schoolId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (delivery, root, publication) = await CorrectionDeliveryAsync(deliveryId, true);
        if (schoolId.HasValue && root.SchoolId != schoolId.Value) throw new AbpAuthorizationException();
        var value = await CorrectionViewAsync(delivery, root, publication, true, true); await uow.CompleteAsync(); return value;
    }
    [RemoteService(false), Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationMyCorrectionDto> GetMyCorrectionWorkloadMetadataAsync(Guid deliveryId)
    { using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (delivery, root, publication) = await CorrectionDeliveryAsync(deliveryId, true); var result = await CorrectionViewAsync(delivery, root, publication, false, true); await uow.CompleteAsync(); return result; }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationMyCorrectionDto>> GetMyCorrectionListAsync(PagedResultRequestDto input, Guid? schoolId = null)
    {
        await CorrectionStudentAsync(); var take = Page(input); List<EducationCorrectionDelivery> rows;
        using (var listing = Uow.Begin(requiresNew: true, isTransactional: false)) {
            var tasks = await Repo<EducationCorrectionTask>().GetQueryableAsync();
            rows = await AsyncExecuter.ToListAsync((await Repo<EducationCorrectionDelivery>().GetQueryableAsync())
                .Where(x => x.TenantId == CurrentTenant.Id && x.StudentUserId == CurrentUser.Id &&
                    (!schoolId.HasValue || tasks.Any(t => t.Id == x.TaskId && t.TenantId == CurrentTenant.Id && t.SchoolId == schoolId.Value)))
                .OrderByDescending(x => x.AssignedAtUtc).ThenBy(x => x.Id).Take(1001)); await listing.CompleteAsync(); }
        if (rows.Count > 1000) throw new BusinessException(EducationLearningReportErrors.Capacity); var values = new List<EducationMyCorrectionDto>();
        foreach (var row in rows) { using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            try { var (delivery, root, publication) = await CorrectionDeliveryAsync(row.Id, true); values.Add(await CorrectionViewAsync(delivery, root, publication, false, true)); }
            catch (AbpAuthorizationException) { continue; } await uow.CompleteAsync(); }
        return new(values.Count, values.Skip(input.SkipCount).Take(take).ToList());
    }
    private async Task<EducationCorrectionSubmissionPreviewDto> CorrectionSubmissionPreviewAsync(EducationCorrectionDelivery delivery, EducationCorrectionTask root,
        EducationCorrectionTaskRevision publication, PreviewEducationCorrectionDto input)
    {
        input.Content.Validate(submitted: true); if (input.ExpectedSubmissionId != delivery.CurrentSubmissionId) throw new AbpDbConcurrencyException();
        var gate = await CorrectionGateAsync(root); if (await CorrectionAvailabilityAsync(root, publication, gate) != EducationCorrectionAvailability.Available || delivery.FirstProvidedAtUtc == null) throw new BusinessException(EducationCorrectionErrors.Closed);
        var images = await CorrectionSelectedImagesAsync(delivery, input.Content); var content = publication.Content()!;
        return new() { DeliveryId = delivery.Id, ExpectedSubmissionId = delivery.CurrentSubmissionId, Content = input.Content, Images = images.Select(CorrectionImage).ToList(), DueAtUtc = content.DueAtUtc, ServerNowUtc = DateTime.UtcNow,
            Fingerprint = Hash(new { delivery.Id, delivery.PublicationId, delivery.CurrentSubmissionId, publication.GradeRevisionId, Release = gate.Release!.Id, publication.ContentJson, input.Content,
                Images = images.Select(x => new { x.Id, x.ContentHash, x.Size, x.UploadedAtUtc }) }) };
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationCorrectionSubmissionPreviewDto> PreviewMyCorrectionAsync(Guid deliveryId, PreviewEducationCorrectionDto input)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (delivery, root, publication) = await CorrectionDeliveryAsync(deliveryId, true);
        var result = await CorrectionSubmissionPreviewAsync(delivery, root, publication, input); await uow.CompleteAsync(); return result;
    }
    private async Task<EducationCorrectionSubmission?> CorrectionSubmissionReceiptAsync(EducationCorrectionDelivery delivery, Guid operationId, string? fingerprint = null)
    {
        var row = await ReferenceAsync<EducationCorrectionSubmission>(operationId); if (row == null) return null;
        if (row.DeliveryId != delivery.Id || row.TenantId != delivery.TenantId || row.StudentUserId != delivery.StudentUserId || row.OwnerUserId != delivery.OwnerUserId) throw new AbpAuthorizationException();
        if (fingerprint != null && row.RequestFingerprint != fingerprint) throw new BusinessException(EducationCorrectionErrors.Conflict); return row;
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationCorrectionSubmissionDto> SubmitMyCorrectionAsync(Guid deliveryId, SubmitEducationCorrectionDto input)
    {
        await CorrectionStudentAsync(); EducationRules.RequireId(input.OperationId); var fingerprint = EducationCorrectionFingerprint.Submit(deliveryId, input);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (delivery, root, publication) = await CorrectionDeliveryAsync(deliveryId, true);
        var prior = await CorrectionSubmissionReceiptAsync(delivery, input.OperationId, fingerprint); if (prior != null) { await uow.CompleteAsync(); return CorrectionSubmission(prior); }
        var preview = await CorrectionSubmissionPreviewAsync(delivery, root, publication, input); if (!input.ConfirmSharingWithTeacher || input.PreviewFingerprint != preview.Fingerprint) throw new BusinessException(EducationCorrectionErrors.Changed);
        using var tracking = Repo<EducationCorrectionDelivery>().EnableTracking(); var current = await Repo<EducationCorrectionDelivery>().GetAsync(delivery.Id);
        if (current.CurrentSubmissionId != input.ExpectedSubmissionId) throw new AbpDbConcurrencyException(); var now = DateTime.UtcNow;
        if (now < publication.Content()!.StartsAtUtc || now >= publication.Content()!.DueAtUtc) throw new BusinessException(EducationCorrectionErrors.Closed);
        var submission = new EducationCorrectionSubmission(input.OperationId, current, input.Content, input.ConfirmSharingWithTeacher, preview.Fingerprint, fingerprint, now);
        current.Submit(submission); await Repo<EducationCorrectionSubmission>().InsertAsync(submission, autoSave: true); await Repo<EducationCorrectionDelivery>().UpdateManyAsync([current], autoSave: true);
        await uow.CompleteAsync(); return CorrectionSubmission(submission);
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationCorrectionSubmissionDto?> GetMyCorrectionOperationAsync(Guid deliveryId, Guid operationId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (delivery, _, _) = await CorrectionDeliveryAsync(deliveryId, true);
        var row = await CorrectionSubmissionReceiptAsync(delivery, operationId); await uow.CompleteAsync(); return row == null ? null : CorrectionSubmission(row);
    }
    private async Task<PagedResultDto<EducationCorrectionHistoryDto>> CorrectionHistoryAsync(EducationCorrectionDelivery delivery, bool reviewsVisible, PagedResultRequestDto input)
    {
        var take = Page(input); var query = (await Repo<EducationCorrectionSubmission>().GetQueryableAsync()).Where(x => x.DeliveryId == delivery.Id && x.TenantId == delivery.TenantId && x.StudentUserId == delivery.StudentUserId && x.OwnerUserId == delivery.OwnerUserId);
        var count = await AsyncExecuter.CountAsync(query); var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.SubmissionNumber).Skip(input.SkipCount).Take(take)); var ids = rows.Select(x => x.Id).ToList();
        var reviews = reviewsVisible ? await AsyncExecuter.ToListAsync((await Repo<EducationCorrectionReview>().GetQueryableAsync()).Where(x => x.DeliveryId == delivery.Id && x.TenantId == delivery.TenantId && ids.Contains(x.SubmissionId)).OrderBy(x => x.ReviewNumber).Take(1001)) : [];
        if (reviews.Count > 1000) throw new BusinessException(EducationLearningReportErrors.Capacity);
        return new(count, rows.Select(x => new EducationCorrectionHistoryDto { Submission = CorrectionSubmission(x), Reviews = reviews.Where(r => r.SubmissionId == x.Id).Select(CorrectionReview).ToList() }).ToList());
    }
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationCorrectionHistoryDto>> GetMyCorrectionHistoryAsync(Guid deliveryId, Guid? expectedSubmissionId, PagedResultRequestDto input)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (delivery, root, publication) = await CorrectionDeliveryAsync(deliveryId, true);
        if (delivery.CurrentSubmissionId != expectedSubmissionId) throw new AbpDbConcurrencyException(); var gate = await CorrectionGateAsync(root);
        var result = await CorrectionHistoryAsync(delivery, gate.FullReview && gate.Confirmed.Id == publication.GradeRevisionId, input); await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationCorrectionHistoryDto>> GetCorrectionSubmissionsAsync(Guid deliveryId, PagedResultRequestDto input)
    { using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (delivery, _, _) = await CorrectionDeliveryAsync(deliveryId, false); var result = await CorrectionHistoryAsync(delivery, true, input); await uow.CompleteAsync(); return result; }
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationCorrectionProgressDto> GetCorrectionProgressAsync(Guid taskId, Guid? publicationId)
    {
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var root = await CorrectionAccessAsync(taskId, false); var id = publicationId ?? root.PublishedRevisionId ?? root.LastPublishedRevisionId ?? throw CorrectionInvalid();
        var publication = await ReferenceAsync<EducationCorrectionTaskRevision>(id) ?? throw CorrectionInvalid(); if (publication.TaskId != root.Id || publication.Kind != EducationCorrectionRevisionKind.Published) throw new AbpAuthorizationException();
        var delivery = (await ReferencesAsync<EducationCorrectionDelivery>(x => x.PublicationId == id && x.TenantId == root.TenantId)).Single();
        var versions = await ReferencesAsync<EducationCorrectionTaskRevision>(x => x.TaskId == root.Id && x.TenantId == root.TenantId && x.Kind == EducationCorrectionRevisionKind.Published);
        var result = new EducationCorrectionProgressDto { TaskId = root.Id, PublicationId = id, StudentName = publication.StudentName, ConcurrencyStamp = delivery.ConcurrencyStamp,
            ReviewHeadId = delivery.CurrentReviewId, IsCurrent = root.PublishedRevisionId == id, Delivery = await CorrectionViewAsync(delivery, root, publication, true, false),
            Publications = versions.OrderByDescending(x => x.RevisionNumber).Select(x => new EducationActivityVersionDto { Id = x.Id, RevisionNumber = x.RevisionNumber, RecordedAtUtc = x.RecordedAtUtc }).ToList() };
        await uow.CompleteAsync(); return result;
    }
    private async Task<EducationCorrectionReview?> CorrectionReviewReceiptAsync(EducationCorrectionDelivery delivery, Guid operationId, string? fingerprint = null)
    {
        var row = await ReferenceAsync<EducationCorrectionReview>(operationId); if (row == null) return null;
        if (row.DeliveryId != delivery.Id || row.TenantId != delivery.TenantId || row.OwnerUserId != delivery.OwnerUserId || row.StudentUserId != delivery.StudentUserId) throw new AbpAuthorizationException();
        if (fingerprint != null && row.RequestFingerprint != fingerprint) throw new BusinessException(EducationCorrectionErrors.Conflict); return row;
    }
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationCorrectionReviewDto> PublishCorrectionReviewAsync(Guid deliveryId, PublishEducationCorrectionReviewDto input)
    {
        await CorrectionTeacherAsync(); EducationRules.RequireId(input.OperationId); var fingerprint = EducationCorrectionFingerprint.Review(deliveryId, input);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (delivery, root, publication) = await CorrectionDeliveryAsync(deliveryId, false);
        var prior = await CorrectionReviewReceiptAsync(delivery, input.OperationId, fingerprint); if (prior != null) { await uow.CompleteAsync(); return CorrectionReview(prior); }
        var gate = await CorrectionGateAsync(root); if (!gate.FullReview || gate.Confirmed.Id != publication.GradeRevisionId) throw new BusinessException(EducationCorrectionErrors.Changed);
        using var tracking = Repo<EducationCorrectionDelivery>().EnableTracking(); var current = await Repo<EducationCorrectionDelivery>().GetAsync(delivery.Id);
        if (current.CurrentSubmissionId != input.ExpectedSubmissionId || current.CurrentReviewId != input.ExpectedReviewId || current.ConcurrencyStamp != input.ConcurrencyStamp) throw new AbpDbConcurrencyException();
        var submission = await CorrectionSubmittedHeadAsync(current) ?? throw CorrectionInvalid();
        var review = new EducationCorrectionReview(input.OperationId, current, submission, input.Decision, input.StudentFeedback, input.ConfirmStudentVisibleFeedback, fingerprint, DateTime.UtcNow);
        current.Review(review); await Repo<EducationCorrectionReview>().InsertAsync(review, autoSave: true); await Repo<EducationCorrectionDelivery>().UpdateManyAsync([current], autoSave: true);
        await uow.CompleteAsync(); return CorrectionReview(review);
    }
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationCorrectionReviewDto?> GetCorrectionReviewOperationAsync(Guid deliveryId, Guid operationId)
    { using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var (delivery, _, _) = await CorrectionDeliveryAsync(deliveryId, false); var row = await CorrectionReviewReceiptAsync(delivery, operationId); await uow.CompleteAsync(); return row == null ? null : CorrectionReview(row); }
}
