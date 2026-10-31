using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationStudyTaskContractTests
{
    [Fact]
    public void Study_Task_Routes_Should_Require_Their_Own_Permissions_Without_Report_Or_Plan_Permissions()
    {
        var methods = typeof(IEducationStudyTaskAppService).GetMethods(); methods.Length.ShouldBe(22);
        foreach (var method in methods) {
            var student = method.Name.Contains("My"); var materials = !student && (method.Name.Contains("MaterialSource") || method.Name.Contains("TeacherMaterial"));
            var expected = materials ? new[] { EducationPermissions.LearningManage, KnowPulsePermissions.KnowledgeBase.PersonalLibrary }
                : new[] { student ? EducationPermissions.LearningParticipate : EducationPermissions.LearningManage };
            method.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected.Order());
            typeof(EducationStudyTaskAppService).GetMethod(method.Name)!.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).Order().ShouldBe(expected.Order());
        }
        typeof(EducationMaterialDelivery).GetCustomAttribute<RemoteServiceAttribute>()!.IsEnabled.ShouldBeFalse();
        typeof(EducationClassRoster).GetCustomAttribute<RemoteServiceAttribute>()!.IsEnabled.ShouldBeFalse();
    }
    [Fact]
    public void Study_Student_Response_Should_Use_An_Explicit_Public_Allowlist()
    {
        typeof(EducationMyStudyTaskDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "Id", "SchoolId", "TaskId", "PublicationId", "VersionNumber", "Title", "TeacherName", "ClassName", "SubjectName", "TextbookTitle",
            "StartsAtUtc", "EndsAtUtc", "ServerNowUtc", "EstimatedMinutes", "Availability", "State", "MaterialsAvailable", "Instructions", "PublicReason", "Materials", "CurrentStudentRevisionId", "CurrentStudent", "FirstProvidedAtUtc", "TeacherHandledAtUtc" }.Order());
        typeof(EducationStudentStudyEventDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "Id", "RequestFingerprint", "RecipientId", "StudentRevisionNumber", "Action", "Difficulty", "SharedText", "ReportedMinutes", "RecordedAtUtc" }.Order());
        typeof(EducationStudyMaterialDisplay).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "Id", "Title" }.Order());
    }
    [Fact]
    public void Study_Command_Fingerprint_Should_Survive_Browser_Roundtrip_And_Include_Exact_Materials_And_Confirmations()
    {
        var task = Guid.NewGuid(); var command = new SaveEducationStudyTaskDto { OperationId = Guid.NewGuid(), Scope = new() { TeacherTextbookSelectionId = Guid.NewGuid() },
            Content = new() { StartsAtUtc = DateTime.UtcNow, EndsAtUtc = DateTime.UtcNow.AddDays(1), Materials = [new() { Id = Guid.NewGuid(), Title = "指定教材", KnowledgeVersionId = Guid.NewGuid() }] } };
        var hash = EducationStudyTaskFingerprint.Draft(task, command); var copy = JsonSerializer.Deserialize<SaveEducationStudyTaskDto>(JsonSerializer.Serialize(command))!;
        EducationStudyTaskFingerprint.Draft(task, copy).ShouldBe(hash); copy.Content.Materials[0].AttachmentIds.Add(Guid.NewGuid()); EducationStudyTaskFingerprint.Draft(task, copy).ShouldNotBe(hash);
        var publish = new PublishEducationStudyTaskDto { OperationId = Guid.NewGuid() }; var before = EducationStudyTaskFingerprint.Publish(task, publish);
        publish.ConfirmRecipientsMaterialsAndSafeText = true; EducationStudyTaskFingerprint.Publish(task, publish).ShouldNotBe(before);
    }
}
