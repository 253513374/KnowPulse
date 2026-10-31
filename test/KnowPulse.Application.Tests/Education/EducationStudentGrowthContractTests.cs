using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Xunit;

namespace KnowPulse.Education;

public class EducationStudentGrowthContractTests
{
    [Fact]
    public void Student_Growth_Endpoints_Should_Declare_Student_Permission_And_Never_Accept_A_Student_Identifier()
    {
        typeof(IEducationStudentGrowthAppService).GetMethods().Length.ShouldBe(6);
        foreach (var method in typeof(IEducationStudentGrowthAppService).GetMethods())
        {
            method.GetCustomAttribute<AuthorizeAttribute>()!.Policy.ShouldBe(EducationPermissions.AssessmentParticipate);
            typeof(EducationStudentGrowthAppService).GetMethod(method.Name)!.GetCustomAttribute<AuthorizeAttribute>()!.Policy.ShouldBe(EducationPermissions.AssessmentParticipate);
            method.GetParameters().Select(x => x.Name).ShouldNotContain("studentUserId");
        }
    }
    [Fact]
    public void Student_Growth_Nested_Contracts_Should_Keep_Explicit_Safe_Field_Allowlists()
    {
        var fields = new Dictionary<Type, string[]> {
            [typeof(EducationStudentGrowthValue)] = ["SnapshotId", "Context", "WindowStartsAtUtc", "WindowEndsAtUtc", "CalculatedAtUtc", "CalculationVersion", "RuleMissing", "Rule", "RuleDecisionRevisionNumber", "Points", "Assessments"],
            [typeof(EducationStudentGrowthContext)] = ["Id", "SchoolName", "ClassName", "TermName", "TeacherName", "SubjectName", "TextbookTitle", "MaximumWindowDays"],
            [typeof(EducationStudentGrowthPoint)] = ["Id", "RevisionNumber", "Code", "Name", "State", "ObtainedScore", "MaxScore", "Ratio", "IndependentQuestionCount", "AssessmentCount", "LatestEvidenceAtUtc", "UnverifiedQuestionCount", "PracticeQuestionCount", "InvalidQuestionCount", "MissingIndependentQuestionCount", "MissingAssessmentCount", "RecentEvidenceMissing"],
            [typeof(EducationStudentGrowthAssessment)] = ["TaskId", "RecipientId", "ResultReleaseId", "Title", "EffectiveAtUtc", "IsAbsent", "TotalScore", "MaxScore", "Visibility", "ComparisonLimit"],
            [typeof(EducationStudentGrowthRule)] = ["RevisionNumber", "WindowDays", "MinimumIndependentQuestionCount", "MinimumAssessmentCount", "MaximumLatestEvidenceAgeDays", "TargetRatio"],
            [typeof(EducationStudentGrowthEvidenceDto)] = ["TaskId", "RecipientId", "PaperQuestionId", "ResultReleaseId", "EffectiveAtUtc", "ObtainedScore", "MaxScore", "ExclusionReason"],
            [typeof(EducationStudentGrowthEvidencePageDto)] = ["HasRestrictedEvidence", "TotalCount", "Items"],
            [typeof(GetEducationStudentGrowthInput)] = ["WindowStartsAtUtc", "WindowEndsAtUtc"] };
        foreach (var (type, expected) in fields) type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(x => x.Name).Order().ShouldBe(expected.Order());
        new EducationStudentGrowthPoint().Ratio.ShouldBeNull();
    }
}
