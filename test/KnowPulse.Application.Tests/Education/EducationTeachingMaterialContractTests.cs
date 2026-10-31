using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Xunit;

namespace KnowPulse.Education;

public class EducationTeachingMaterialContractTests
{
    [Fact]
    public void Teaching_Material_Routes_Should_Separate_Teacher_Source_Permissions_From_Student_Grants()
    {
        var methods = typeof(IEducationTeachingMaterialAppService).GetMethods(); methods.Length.ShouldBe(6);
        foreach (var method in methods) {
            var expected = method.Name.Contains("MyMaterial") ? new[] { EducationPermissions.TeachingPlanView, EducationPermissions.LearningParticipate }
                : new[] { EducationPermissions.TeachingPlanManage, EducationPermissions.AnalysisManage, KnowPulsePermissions.KnowledgeBase.PersonalLibrary };
            method.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected.Order());
            typeof(EducationTeachingPlanAppService).GetMethod(method.Name)!.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected.Order());
        }
    }
    [Fact]
    public void Teaching_Material_Student_Responses_Should_Not_Expose_Source_Library_Or_Storage_Identifiers()
    {
        var fields = new Dictionary<Type, string[]> {
            [typeof(EducationMaterialContentDto)] = ["Id", "Title", "VersionNumber", "IsAvailable", "ContentText", "Attachments"],
            [typeof(EducationMaterialAttachmentDto)] = ["Id", "FileName", "ContentType", "Size"],
            [typeof(EducationStudentTeachingMaterial)] = ["Id", "StepId", "Title"] };
        foreach (var (type, expected) in fields) type.GetProperties().Select(x => x.Name).Order().ShouldBe(expected.Order());
        var dto = new EducationMaterialContentDto(); dto.IsAvailable.ShouldBeFalse(); dto.ContentText.ShouldBeNull(); dto.Attachments.ShouldBeEmpty();
    }
}
