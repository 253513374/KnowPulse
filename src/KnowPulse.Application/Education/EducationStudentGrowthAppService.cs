using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using KnowPulse.Exam;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

[Authorize]
public class EducationStudentGrowthAppService : KnowPulseAppService, IEducationStudentGrowthAppService
{
    private IRepository<EducationStudentGrowthSnapshot, Guid> Snapshots => LazyServiceProvider.LazyGetRequiredService<IRepository<EducationStudentGrowthSnapshot, Guid>>();
    private EducationLearningReportSourceReader Reader => LazyServiceProvider.LazyGetRequiredService<EducationLearningReportSourceReader>();
    private IUnitOfWorkManager Uow => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();
    private async Task StudentAsync()
    {
        await CheckMvpPermissionAsync(EducationPermissions.AssessmentParticipate);
        await LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAccessGuard>().CurrentAccountAsync();
    }
    [Authorize(EducationPermissions.AssessmentParticipate)]
    public virtual async Task<ListResultDto<EducationStudentGrowthContext>> GetMyContextsAsync()
    { await StudentAsync(); return new(await Reader.GetStudentContextsAsync()); }

    private async Task SchoolAsync(Guid schoolId)
    {
        await StudentAsync();
        var workspace = await LazyServiceProvider.LazyGetRequiredService<IEducationSchoolAppService>().GetMyWorkspaceAsync();
        if (schoolId == Guid.Empty || !workspace.Workspaces.Any(x => x.SchoolId == schoolId && x.Role == SchoolMemberRole.Student)) throw new AbpAuthorizationException();
    }
    [Authorize(EducationPermissions.AssessmentParticipate)]
    public virtual async Task<EducationStudentGrowthContextsForSchoolDto> GetMyContextsForSchoolAsync(Guid schoolId)
    {
        await SchoolAsync(schoolId); var items = await Reader.GetStudentContextsAsync(schoolId: schoolId); await SchoolAsync(schoolId);
        return new() { SchoolId = schoolId, Items = items };
    }
    [Authorize(EducationPermissions.AssessmentParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudentGrowthForSchoolDto> GetMyGrowthForSchoolAsync(Guid contextId, Guid schoolId, GetEducationStudentGrowthInput input)
    {
        await SchoolAsync(schoolId); var value = await GrowthCoreAsync(contextId, input, schoolId); await SchoolAsync(schoolId);
        return new() { SchoolId = schoolId, Value = value };
    }
    [Authorize(EducationPermissions.AssessmentParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationStudentGrowthEvidenceForSchoolDto> GetMyPointEvidenceForSchoolAsync(Guid contextId, Guid schoolId, Guid snapshotId, Guid pointId, PagedResultRequestDto input)
    {
        await SchoolAsync(schoolId); var evidence = await EvidenceCoreAsync(contextId, snapshotId, pointId, input, schoolId); await SchoolAsync(schoolId);
        return new() { SchoolId = schoolId, Evidence = evidence };
    }

    private sealed record Projection(EducationStudentGrowthSnapshot Snapshot, EducationLearningAnalysisResult Analysis);
    private Projection Project(EducationStudentGrowthSource source)
    {
        var now = DateTime.UtcNow;
        var analysis = EducationLearningAnalysis.Calculate(source.Scope, source.Evidence, source.Attempts, source.Relationships, source.Exposures, now, source.Rule);
        var rule = source.Rule;
        var value = new EducationStudentGrowthValue { Context = source.Context, WindowStartsAtUtc = source.Scope.WindowStartsAtUtc,
            WindowEndsAtUtc = source.Scope.WindowEndsAtUtc, CalculatedAtUtc = now, CalculationVersion = analysis.CalculationVersion,
            RuleMissing = rule == null, RuleDecisionRevisionNumber = source.RuleDecisionRevisionNumber,
            Rule = rule == null ? null : new(rule.RevisionNumber, rule.WindowDays, rule.MinimumIndependentQuestionCount, rule.MinimumAssessmentCount, rule.MaximumLatestEvidenceAgeDays, rule.TargetRatio),
            Assessments = source.Assessments, Points = analysis.Points.OrderBy(x => x.Point.Code).ThenBy(x => x.Point.KnowledgePointRevisionId).Select(x => new EducationStudentGrowthPoint
            { Id = EducationStudentGrowthSnapshot.PointFor(source.Context.Id, x.Point.KnowledgePointRevisionId), RevisionNumber = source.PointRevisionNumbers[x.Point.KnowledgePointRevisionId], Code = x.Point.Code, Name = x.Point.Name, State = x.State,
                ObtainedScore = x.ObtainedScore, MaxScore = x.MaxScore, Ratio = x.Ratio, IndependentQuestionCount = x.IndependentQuestionCount, AssessmentCount = x.AssessmentCount,
                LatestEvidenceAtUtc = x.LatestEvidenceAtUtc, UnverifiedQuestionCount = x.UnverifiedQuestionCount, PracticeQuestionCount = x.PracticeQuestionCount,
                InvalidQuestionCount = x.InvalidQuestionCount, MissingIndependentQuestionCount = x.MissingIndependentQuestionCount, MissingAssessmentCount = x.MissingAssessmentCount,
                RecentEvidenceMissing = x.RecentEvidenceMissing }).ToList() };
        // Private history identifiers and unrelated hidden grade versions are never a public version signal.
        var fingerprintInput = new { Value = value with { CalculatedAtUtc = default },
            VisibleEvidence = source.Evidence.OrderBy(x => x.GradeRevisionId).Select(x => new { x.GradeRevisionId, x.QualificationRevisionId, x.InvalidationRevisionId }),
            Decisions = analysis.Evidence.OrderBy(x => x.GradeRevisionId).Select(x => new { x.GradeRevisionId, x.ExclusionReason }) };
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(EducationLearningReportJson.Write(fingerprintInput, EducationLearningReportJson.MaxSourceBytes))));
        var recipient = source.Recipient;
        return new(new(CurrentTenant.Id, recipient.StudentUserId, recipient.StudentEnrollmentId, recipient.SchoolMembershipId,
            source.AcademicTermId, recipient.SchoolClassId, source.Curriculum, fingerprint, value), analysis);
    }
    private void Own(EducationStudentGrowthSnapshot snapshot, Guid contextId)
    {
        if (snapshot.TenantId != CurrentTenant.Id || snapshot.StudentUserId != CurrentUser.Id || snapshot.ContextId != contextId) throw new AbpAuthorizationException();
    }
    [Authorize(EducationPermissions.AssessmentParticipate), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationStudentGrowthValue> GetMyGrowthAsync(Guid contextId, GetEducationStudentGrowthInput input)
        => GrowthCoreAsync(contextId, input);
    private async Task<EducationStudentGrowthValue> GrowthCoreAsync(Guid contextId, GetEducationStudentGrowthInput input, Guid? schoolId = null)
    {
        await StudentAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var source = await Reader.CaptureStudentAsync(contextId, input.WindowStartsAtUtc, input.WindowEndsAtUtc, schoolId: schoolId); var projection = Project(source);
        using var tracking = Snapshots.DisableTracking(); var prior = await Snapshots.FindAsync(projection.Snapshot.Id);
        if (prior != null)
        {
            Own(prior, contextId);
            if (prior.SourceFingerprint != projection.Snapshot.SourceFingerprint || prior.WindowStartsAtUtc != input.WindowStartsAtUtc || prior.WindowEndsAtUtc != input.WindowEndsAtUtc)
                throw new BusinessException(EducationLearningReportErrors.OperationConflict);
        }
        else await Snapshots.InsertAsync(projection.Snapshot, autoSave: true);
        await uow.CompleteAsync(); return (prior ?? projection.Snapshot).Result();
    }
    [Authorize(EducationPermissions.AssessmentParticipate), UnitOfWork(IsDisabled = true)]
    public virtual Task<EducationStudentGrowthEvidencePageDto> GetMyPointEvidenceAsync(Guid contextId, Guid snapshotId, Guid pointId, PagedResultRequestDto input)
        => EvidenceCoreAsync(contextId, snapshotId, pointId, input);
    private async Task<EducationStudentGrowthEvidencePageDto> EvidenceCoreAsync(Guid contextId, Guid snapshotId, Guid pointId, PagedResultRequestDto input, Guid? schoolId = null)
    {
        await StudentAsync(); if (input.SkipCount < 0 || input.MaxResultCount is < 1 or > 100) throw new BusinessException(EducationLearningReportErrors.Invalid);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); using var tracking = Snapshots.DisableTracking();
        var snapshot = await Snapshots.FindAsync(snapshotId) ?? throw new AbpAuthorizationException(); Own(snapshot, contextId);
        if (schoolId.HasValue && snapshot.SchoolId != schoolId) throw new AbpAuthorizationException();
        var source = await Reader.CaptureStudentAsync(contextId, snapshot.WindowStartsAtUtc, snapshot.WindowEndsAtUtc, schoolId: schoolId); var projection = Project(source);
        if (projection.Snapshot.SourceFingerprint != snapshot.SourceFingerprint) throw new BusinessException(EducationLearningReportErrors.SourceChanged);
        var point = source.Scope.Points.SingleOrDefault(x => EducationStudentGrowthSnapshot.PointFor(contextId, x.KnowledgePointRevisionId) == pointId) ?? throw new AbpAuthorizationException();
        var relevant = source.Evidence.Where(x => x.Points.Any(p => p.KnowledgePointRevisionId == point.KnowledgePointRevisionId)).ToList();
        var visible = relevant.Where(x => source.Assessments.Single(a => a.TaskId == x.TaskId).Visibility == ExamResultVisibility.FullReview).OrderBy(x => x.EffectiveAtUtc).ThenBy(x => x.GradeRevisionId).ToList();
        var result = new EducationStudentGrowthEvidencePageDto { TotalCount = visible.Count, HasRestrictedEvidence = visible.Count != relevant.Count,
            Items = visible.Skip(input.SkipCount).Take(input.MaxResultCount).Select(x => { var assessment = source.Assessments.Single(a => a.TaskId == x.TaskId);
                var scores = x.Points.Where(p => p.KnowledgePointRevisionId == point.KnowledgePointRevisionId).ToList();
                return new EducationStudentGrowthEvidenceDto { TaskId = x.TaskId, RecipientId = assessment.RecipientId, PaperQuestionId = x.PaperQuestionId,
                    ResultReleaseId = assessment.ResultReleaseId, EffectiveAtUtc = x.EffectiveAtUtc, MaxScore = scores.Sum(p => p.MaxScore), ObtainedScore = scores.Sum(p => p.ObtainedScore),
                    ExclusionReason = projection.Analysis.Evidence.Single(d => d.GradeRevisionId == x.GradeRevisionId).ExclusionReason }; }).ToList() };
        await uow.CompleteAsync(); return result;
    }
}
