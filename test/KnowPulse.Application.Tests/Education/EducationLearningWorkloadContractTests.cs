using System;
using System.Linq;
using System.Reflection;
using KnowPulse.HttpApi.Education;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationLearningWorkloadContractTests
{
    [Fact]
    public void Learning_Workload_Should_Require_Learning_Permission_And_Keep_Internal_Metadata_Readers_Off_HTTP()
    {
        var method = typeof(IEducationLearningWorkloadAppService).GetMethods().Single(); method.Name.ShouldBe("GetMyLearningWorkloadAsync");
        method.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).ShouldBe([EducationPermissions.LearningParticipate]);
        typeof(EducationTeachingPlanAppService).GetMethod(method.Name)!.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).ShouldBe([EducationPermissions.LearningParticipate]);
        typeof(EducationStudyTaskAppService).GetMethod("GetMyWorkloadMetadataAsync")!.GetCustomAttribute<RemoteServiceAttribute>()!.IsEnabled.ShouldBeFalse();
        typeof(EducationAssessmentAppService).GetMethod("GetMyCorrectionWorkloadMetadataAsync")!.GetCustomAttribute<RemoteServiceAttribute>()!.IsEnabled.ShouldBeFalse();
    }
    [Fact]
    public void Learning_Workload_Should_Project_Only_Navigation_Schedule_And_Estimates()
    {
        typeof(GetEducationActivityWorkloadDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "StartsAtUtc", "EndsAtUtc" }.Order());
        typeof(EducationLearningWorkloadItemDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "Kind", "Id", "SourceId", "PublicationId", "SubjectName", "ClassName", "StartsAtUtc", "DueAtUtc", "EstimatedMinutes", "State" }.Order());
        typeof(EducationLearningWorkloadDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "SchoolId", "StartsAtUtc", "EndsAtUtc", "CalculatedAtUtc", "UnestimatedPlanCount", "OmittedKinds", "Days", "Items" }.Order());
        var parameters = typeof(IEducationLearningWorkloadAppService).GetMethods().Single().GetParameters();
        parameters[0].ParameterType.ShouldBe(typeof(GetEducationActivityWorkloadDto));
        parameters[1].Name.ShouldBe("schoolId"); parameters[1].IsOptional.ShouldBeTrue(); parameters[1].DefaultValue.ShouldBeNull();
    }
}
