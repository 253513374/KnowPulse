using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Authorization;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationLearningReportAppService
{
    [Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationLearningReportTrendDto> GetTrendAsync(Guid reportId, Guid snapshotId, Guid schoolId, Guid teachingAssignmentId, GetEducationLearningTrendInput input)
    {
        var take = Page(input); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var current = await CurrentAsync(reportId, schoolId, teachingAssignmentId); var snapshot = Expected(current, snapshotId);
        var job = await RefAsync<EducationLearningReportJob>(snapshot.JobId) ?? throw Changed();
        if (job.ReportId != reportId || job.OwnerUserId != CurrentUser.Id || job.TenantId != CurrentTenant.Id || job.SourceFingerprint != snapshot.SourceFingerprint) throw Changed();
        var source = job.Source(); var student = input.StudentUserId.HasValue ? source.Students.SingleOrDefault(x => x.Student.StudentUserId == input.StudentUserId)?.Student ?? throw new AbpAuthorizationException() : null;
        var rows = EducationLearningTrend.Calculate(source, await Reader.TrendBasesAsync(source), input.StudentUserId);
        var result = new EducationLearningReportTrendDto { ReportId = reportId, SnapshotId = snapshotId, SchoolId = current.Root.SchoolId, TeachingAssignmentId = current.Root.TeachingAssignmentId,
            ClassName = source.SchoolClassName, SubjectName = source.Curriculum.SubjectName, TextbookTitle = source.Curriculum.TextbookTitle, WindowStartsAtUtc = source.WindowStartsAtUtc, WindowEndsAtUtc = source.WindowEndsAtUtc,
            CalculatedAtUtc = snapshot.CalculatedAtUtc, ComparisonVersion = EducationLearningTrend.ComparisonVersion, TotalCount = rows.Count, Items = rows.Skip(input.SkipCount).Take(take).ToList(),
            Student = student == null ? null : new() { Id = student.StudentUserId, Name = student.StudentName, Account = student.AccountName } };
        await uow.CompleteAsync(); return result;
    }
}
