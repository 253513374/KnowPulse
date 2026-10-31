using System;
using System.Linq;
using System.Reflection;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationNotebookSourceContractTests
{
    [Fact]
    public void Notebook_Source_Operations_Should_All_Require_Personal_Library_Permission()
    {
        var methods = typeof(IEducationNotebookSourceAppService).GetMethods(); methods.Length.ShouldBe(5);
        foreach (var method in methods)
        {
            method.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy).ShouldBe([KnowPulsePermissions.KnowledgeBase.PersonalLibrary]);
            typeof(EducationNotebookSourceAppService).GetMethod(method.Name)!.GetCustomAttributes<AuthorizeAttribute>().Select(x => x.Policy)
                .ShouldBe([KnowPulsePermissions.KnowledgeBase.PersonalLibrary]);
            if (method.Name != "PreviewSourceAsync") method.GetParameters()[0].Name.ShouldBe("id");
        }
    }

    [Fact]
    public void Notebook_Metadata_Seams_Should_Not_Be_Public_Or_Remotely_Accessible()
    {
        foreach (var (type, name) in new[] { (typeof(EducationAssessmentAppService), "NotebookQuestionSourceAsync"), (typeof(EducationStudyTaskAppService), "NotebookMaterialSourceAsync") })
        {
            type.GetMethod(name).ShouldBeNull();
            type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetCustomAttribute<RemoteServiceAttribute>()!.IsEnabled.ShouldBeFalse();
        }
    }

    [Fact]
    public void Notebook_References_Should_Only_Contain_Typed_Identifiers_And_Current_Navigation_Metadata()
    {
        typeof(EducationNotebookSourcePointer).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "Kind", "RecipientId", "SourceVersionId", "ComponentId" }.Order());
        typeof(EducationNotebookSourceDto).GetProperties().Select(x => x.Name).Order().ShouldBe(new[] { "Id", "NoteVersionId", "Source", "Removed", "Available", "SchoolId", "Title", "OpenUrl" }.Order());
        foreach (var type in new[] { typeof(PreviewEducationNotebookSourceDto), typeof(AddEducationNotebookSourceDto), typeof(RemoveEducationNotebookSourceDto) })
            type.GetProperties().Select(x => x.Name).ShouldNotContain(x => x.Contains("Body") || x.Contains("Text") || x.Contains("Url") || x.Contains("StudentUserId") || x.Contains("SchoolId"));
    }
}
