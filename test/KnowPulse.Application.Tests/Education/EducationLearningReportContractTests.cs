using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationLearningReportContractTests
{
    [Fact]
    public void Report_And_Rule_Contracts_Should_Require_Separate_Functional_Permissions()
    {
        foreach (var (contract, implementation, permission, count) in new[] {
            (typeof(IEducationLearningReportAppService), typeof(EducationLearningReportAppService), EducationPermissions.AnalysisManage, 11),
            (typeof(IEducationMasteryRuleAppService), typeof(EducationMasteryRuleAppService), EducationPermissions.SchoolManage, 3) })
        {
            contract.GetMethods().Length.ShouldBe(count);
            foreach (var method in contract.GetMethods()) {
                method.GetCustomAttribute<AuthorizeAttribute>()!.Policy.ShouldBe(permission);
                implementation.GetMethod(method.Name)!.GetCustomAttribute<AuthorizeAttribute>()!.Policy.ShouldBe(permission);
            }
        }
    }

    [Fact]
    public void Internal_Capture_And_Background_Processing_Should_Not_Be_Remote_Surfaces()
    {
        foreach (var type in new[] { typeof(EducationLearningReportSourceReader), typeof(EducationLearningReportProcessor), typeof(EducationAssessmentAccessGuard) })
            type.GetCustomAttribute<RemoteServiceAttribute>()!.IsEnabled.ShouldBeFalse();
        typeof(EducationLearningReportJobDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] {
            "Id", "ReportId", "Generation", "RetryOfJobId", "Status", "AttemptCount", "CompletedStudentCount", "StudentCount", "RequestedAtUtc",
            "HeartbeatAtUtc", "CompletedAtUtc", "FailureStage", "CapacityExceeded", "SourceFingerprint", "RequestFingerprint" }.Order());
        new EducationLearningReportDto().Summary.ShouldBeNull(); new EducationLearningReportDto().CurrentSnapshotId.ShouldBeNull();
    }
}
