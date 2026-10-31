using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.Conventions;
using KnowPulse.Education;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Xunit;

namespace KnowPulse.Education;

public class EducationTeachingActivityContractTests
{
    [Fact]
    public void Formal_Assessment_Binding_And_Review_Should_Require_All_Three_Teacher_Permissions()
    {
        var methods = typeof(IEducationTeachingFormalAssessmentAppService).GetMethods(); methods.Length.ShouldBe(7);
        var expected = new[] { EducationPermissions.TeachingPlanManage, EducationPermissions.AnalysisManage, EducationPermissions.AssessmentManage };
        foreach (var method in methods) {
            method.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected.Order());
            typeof(EducationTeachingPlanAppService).GetMethod(method.Name)!.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected.Order());
        }
    }
    [Fact]
    public void Activity_Contract_And_Service_Should_Require_Exact_Teacher_View_And_Participation_Permissions()
    {
        var methods = typeof(IEducationTeachingPlanActivityAppService).GetMethods(); methods.Length.ShouldBe(14);
        foreach (var method in methods) {
            var student = method.Name.StartsWith("GetMy") || method.Name == "SaveMyActivityAsync";
            var expected = student ? method.Name == "SaveMyActivityAsync" ? new[] { EducationPermissions.TeachingPlanView, EducationPermissions.LearningParticipate } :
                new[] { EducationPermissions.TeachingPlanView } : new[] { EducationPermissions.TeachingPlanManage, EducationPermissions.AnalysisManage };
            method.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected.Order());
            typeof(EducationTeachingPlanAppService).GetMethod(method.Name)!.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected.Order());
        }
    }
    [Fact]
    public void Teacher_Mobile_Plan_Routes_Should_Keep_School_And_Assignment_As_Query_Scope()
    {
        var type = typeof(EducationTeachingPlanAppService);
        foreach (var (name, path) in new[] { ("GetTeacherPlansAsync", "teacher-plans"), ("GetActivityProgressForSchoolAsync", "activity-progress-for-school"),
            ("SaveActivityFollowUpForSchoolAsync", "save-activity-follow-up-for-school"), ("GetActivityFollowUpOperationForSchoolAsync", "activity-follow-up-operation-for-school") }) {
            var method = type.GetMethod(name)!; var action = new ActionModel(method, method.GetCustomAttributes(true)) { ActionName = name, Controller = new ControllerModel(type.GetTypeInfo(), []) };
            foreach (var p in method.GetParameters()) action.Parameters.Add(new ParameterModel(p, p.GetCustomAttributes(true)) { Action = action, ParameterName = p.Name! });
            new ConventionalRouteBuilder(Options.Create(new AbpConventionalControllerOptions())).Build("app", "EducationTeachingPlan", action, name.StartsWith("Get") ? "GET" : "POST", new ConventionalControllerSetting(type.Assembly, "app", "Default"))
                .ShouldBe("api/app/education-teaching-plan/" + path);
        }
    }
    [Fact]
    public void Student_Activity_Fields_Should_Exclude_Private_Teacher_Revision_Numbers_Notes_And_Operation_Fingerprints()
    {
        var expected = new Dictionary<Type, string[]> {
            [typeof(EducationStudentActivityDto)] = ["Id","MaterialsAvailable","StepId","StepOrder","CurrentStudentRevisionId","StudentRevisionNumber","State","FirstProvidedAtUtc","CompletedAtUtc","CurrentStudent","TeacherHandledAtUtc","FormalAssessment","CanStart","CanReadMaterials"],
            [typeof(EducationActivityFormalAssessmentDto)] = ["RequiresAssessmentPermission","CanOpen","IsStopped","CanStart","TaskId","RecipientId","AttemptId","Title","SealedAtUtc"],
            [typeof(EducationStudentActivityEventDto)] = ["Id","ActivityId","StudentRevisionNumber","Action","Difficulty","SharedText","ReportedMinutes","RecordedAtUtc"],
            [typeof(EducationMyActivitiesDto)] = ["Plan","Activities"],
            [typeof(EducationActivityWorkloadDto)] = ["StartsAtUtc","EndsAtUtc","CalculatedAtUtc","UncountedPlanCount","Days"] };
        foreach (var (type, fields) in expected) type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(x => x.Name).Order().ShouldBe(fields.Order());
        typeof(SaveEducationMyActivityDto).GetProperties().Select(x => x.Name).ShouldNotContain("StudentUserId");
    }
}
