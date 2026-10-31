using System;
using System.Linq;
using KnowPulse.KnowledgeBase;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public partial class EducationTeachingPlanTests
{
    [Fact]
    public void Teaching_Material_References_Should_Be_Bounded_And_Frozen_With_Their_Exact_Nonformal_Step()
    {
        var basis = Basis(); var step = new EducationTeachingPlanStep { Id = Guid.NewGuid(), Kind = EducationTeachingPlanStepKind.Reading, Title = "阅读", Instructions = "阅读指定讲义", EstimatedMinutes = 5, DueAtUtc = Now.AddHours(2) };
        var content = Content(basis) with { Steps = [.. Content(basis).Steps, step] };
        var material = new EducationTeachingPlanMaterial { Id = Guid.NewGuid(), StepId = step.Id, Title = "指定版本讲义", KnowledgeItemId = Guid.NewGuid(), KnowledgeVersionId = Guid.NewGuid(), AttachmentIds = [Guid.NewGuid()] };
        content = content with { Materials = [material] }; content.Validate();
        var revision = Revision(Plan(basis), basis, content, EducationTeachingPlanRevisionKind.Draft);
        var file = material.AttachmentIds.Single(); material.AttachmentIds.Clear(); revision.Content()!.Materials.Single().AttachmentIds.Single().ShouldBe(file);
        foreach (var invalid in new[] { material with { StepId = content.Steps[0].Id }, material with { StepId = Guid.NewGuid() },
            material with { KnowledgeVersionId = Guid.Empty }, material with { SchoolKnowledgeShareId = Guid.Empty },
            material with { AttachmentIds = [file, file] }, material with { AttachmentIds = Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()).ToList() } })
            Should.Throw<BusinessException>(() => (content with { Materials = [invalid] }).Validate());
        Should.Throw<BusinessException>(() => (content with { Materials = [material, material] }).Validate());
        var old = Content(basis); EducationLearningReportJson.Write(old, 256 * 1024).ShouldNotContain("materials");
        EducationLearningReportJson.Read<EducationTeachingPlanContent>(EducationLearningReportJson.Write(old, 256 * 1024), 256 * 1024).Materials.ShouldBeEmpty();
    }
    [Fact]
    public void Student_Assignment_Purpose_Should_Require_Explicit_Author_Consent_And_Not_Imply_Download()
    {
        Should.Throw<BusinessException>(() => SchoolKnowledgeShare.EnsureValidUses(SchoolKnowledgeShareUses.AssignToStudents));
        var share = new SchoolKnowledgeShare(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "讲义", null, SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.AssignToStudents, Now);
        share.Allows(SchoolKnowledgeShareUses.AssignToStudents).ShouldBeTrue(); share.Allows(SchoolKnowledgeShareUses.Download).ShouldBeFalse(); share.Allows(SchoolKnowledgeShareUses.GenerateQuestions).ShouldBeFalse();
    }
}
