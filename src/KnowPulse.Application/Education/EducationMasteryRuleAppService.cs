using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

[Authorize]
public class EducationMasteryRuleAppService : KnowPulseAppService, IEducationMasteryRuleAppService
{
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private IUnitOfWorkManager Uow => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();
    private async Task<T?> RefAsync<T>(Guid id) where T : class, IEntity<Guid>
    { using var tracking = Repo<T>().DisableTracking(); return await Repo<T>().FindAsync(id); }
    private async Task AuthorizeAsync(Guid schoolId, Guid subjectId, bool write = false)
    {
        await CheckMvpPermissionAsync(EducationPermissions.SchoolManage);
        var schools = LazyServiceProvider.LazyGetRequiredService<IEducationSchoolAppService>();
        await schools.GetSchoolConfigurationAsync(schoolId);
        var subject = await RefAsync<EducationSubject>(subjectId);
        if (subject == null || subject.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        if (!write) return;
        using var schoolTracking = Repo<School>().EnableTracking(); using var accountTracking = Repo<IdentityUser>().EnableTracking();
        var school = await Repo<School>().GetAsync(schoolId); var actor = await Repo<IdentityUser>().GetAsync(CurrentUser.Id!.Value);
        if (school.TenantId != CurrentTenant.Id || actor.TenantId != CurrentTenant.Id || !actor.IsActive) throw new AbpAuthorizationException();
        await Repo<School>().UpdateManyAsync([school], autoSave: true); await Repo<IdentityUser>().UpdateManyAsync([actor], autoSave: true);
        await schools.GetSchoolConfigurationAsync(schoolId);
    }
    private static EducationMasteryRuleRevisionDto Map(EducationMasteryRuleRevision revision) => new()
        { Id = revision.Id, RevisionNumber = revision.RevisionNumber, BasedOnRevisionId = revision.BasedOnRevisionId, IsEnabled = revision.IsEnabled,
            WindowDays = revision.WindowDays, MinimumIndependentQuestionCount = revision.MinimumIndependentQuestionCount, MinimumAssessmentCount = revision.MinimumAssessmentCount,
            MaximumLatestEvidenceAgeDays = revision.MaximumLatestEvidenceAgeDays, TargetRatio = revision.TargetRatio, Reason = revision.Reason,
            RequestFingerprint = revision.RequestFingerprint, RecordedAtUtc = revision.RecordedAtUtc };
    private async Task<EducationMasteryRuleRevision?> ReceiptAsync(Guid schoolId, Guid subjectId, Guid operationId, string? fingerprint = null)
    {
        await AuthorizeAsync(schoolId, subjectId);
        var revision = await RefAsync<EducationMasteryRuleRevision>(operationId); if (revision == null) return null;
        if (revision.TenantId != CurrentTenant.Id || revision.SchoolId != schoolId || revision.EducationSubjectId != subjectId) throw new AbpAuthorizationException();
        if (fingerprint != null && revision.RequestFingerprint != fingerprint) throw new BusinessException(EducationLearningReportErrors.OperationConflict);
        return revision;
    }
    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<EducationMasteryRuleDto> GetAsync(Guid schoolId, Guid subjectId)
    {
        await AuthorizeAsync(schoolId, subjectId); var rule = await RefAsync<EducationMasteryRule>(EducationMasteryRule.IdFor(CurrentTenant.Id, schoolId, subjectId));
        var current = rule?.CurrentRevisionId is Guid id ? await ReceiptAsync(schoolId, subjectId, id) : null;
        return new() { SchoolId = schoolId, EducationSubjectId = subjectId, ConcurrencyStamp = rule?.ConcurrencyStamp, Current = current == null ? null : Map(current) };
    }
    [Authorize(EducationPermissions.SchoolManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationMasteryRuleRevisionDto> SaveAsync(Guid schoolId, Guid subjectId, SaveEducationMasteryRuleDto input)
    {
        await CheckMvpPermissionAsync(EducationPermissions.SchoolManage); EducationRules.RequireId(input.OperationId);
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { schoolId, subjectId, input }))));
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var prior = await ReceiptAsync(schoolId, subjectId, input.OperationId, fingerprint);
            if (prior != null) { await uow.CompleteAsync(); return Map(prior); }
            if (!input.ConfirmSchoolPolicy) throw new BusinessException(EducationLearningErrorCodes.InvalidRule);
            await AuthorizeAsync(schoolId, subjectId, write: true);
            using var tracking = Repo<EducationMasteryRule>().EnableTracking();
            var root = await Repo<EducationMasteryRule>().FindAsync(EducationMasteryRule.IdFor(CurrentTenant.Id, schoolId, subjectId));
            if (root?.CurrentRevisionId != input.ExpectedRevisionId || root?.ConcurrencyStamp != input.ConcurrencyStamp) throw new AbpDbConcurrencyException();
            var created = root == null; root ??= new(CurrentTenant.Id, schoolId, subjectId);
            var revision = new EducationMasteryRuleRevision(input.OperationId, root, CurrentUser.Id!.Value, input.IsEnabled, input.WindowDays, input.MinimumIndependentQuestionCount,
                input.MinimumAssessmentCount, input.MaximumLatestEvidenceAgeDays, input.TargetRatio, input.Reason, root.ConcurrencyStamp, fingerprint, DateTime.UtcNow);
            if (created) await Repo<EducationMasteryRule>().InsertAsync(root, autoSave: true);
            root.Advance(revision); await Repo<EducationMasteryRuleRevision>().InsertAsync(revision, autoSave: true); await Repo<EducationMasteryRule>().UpdateManyAsync([root], autoSave: true);
            await uow.CompleteAsync(); return Map(revision);
        }
        catch (Exception error) when (error is not BusinessException && error is not AbpAuthorizationException && error is not OperationCanceledException)
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            var receipt = await ReceiptAsync(schoolId, subjectId, input.OperationId, fingerprint); if (receipt == null) throw;
            await recovery.CompleteAsync(); return Map(receipt);
        }
    }
    [Authorize(EducationPermissions.SchoolManage)]
    public virtual async Task<EducationMasteryRuleRevisionDto?> GetOperationAsync(Guid schoolId, Guid subjectId, Guid operationId)
    { var receipt = await ReceiptAsync(schoolId, subjectId, operationId); return receipt == null ? null : Map(receipt); }
}
