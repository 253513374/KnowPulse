using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public partial class EducationTeachingPlanTests
{
    private static (EducationTeachingPlanBasis Basis, EducationPlanGenerationRequest Request, EducationTeachingPlan Plan, EducationTeachingPlanGenerationJob Job) GenerationFixture()
    {
        var basis = Basis(); basis.Curriculum.TextbookTitle = "初中数学"; var plan = Plan(basis);
        var request = new EducationPlanGenerationRequest { ReportId = basis.ReportId, ReportSnapshotId = basis.ReportSnapshotId, StudentUserId = basis.Student.StudentUserId,
            StartsAtUtc = Now, EndsAtUtc = Now.AddDays(1), ReassessmentAtUtc = Now.AddDays(1), TargetPointRevisionIds = [basis.Points[0].Point.KnowledgePointRevisionId], MaximumEstimatedMinutes = 60 };
        return (basis, request, plan, new(Guid.NewGuid(), plan.Id, request, basis, plan.AcademicTermId, plan.SchoolClassId, new string('b', 64), Now));
    }
    private static string ProposalJson(EducationPlanGenerationRequest request) => JsonSerializer.Serialize(new { title = "有理数学习安排", studentGoal = "先诊断，再解释与独立练习", teacherActions = "核对学生口头说明，针对新题反馈", successCriteria = "独立完成新题后由教师复核",
        steps = new[] { "Diagnostic", "Explanation", "WorkedExample", "Practice", "Reassessment" }.Select((kind, index) => new { kind, title = kind, instructions = "独立说明步骤并核对依据", estimatedMinutes = 10,
            dueAtUtc = index == 4 ? request.ReassessmentAtUtc : request.StartsAtUtc.AddHours(index + 1) }).ToArray() });
    [Fact]
    public void Plan_Generation_Should_Create_Stable_Editable_Steps_Without_Automatically_Publishing_A_Plan()
    {
        var f = GenerationFixture(); var run = Guid.NewGuid(); f.Job.Claim(run, Now, 120).ShouldBeTrue(); f.Job.Claim(Guid.NewGuid(), Now.AddSeconds(1), 120).ShouldBeFalse();
        var content = EducationTeachingPlanGenerationOutput.Parse(f.Job.Id, "```json\n" + ProposalJson(f.Request) + "\n```", f.Request, f.Basis);
        EducationTeachingPlanGenerationOutput.Parse(f.Job.Id, ProposalJson(f.Request), f.Request, f.Basis).Steps.Select(x => x.Id).ShouldBe(content.Steps.Select(x => x.Id));
        f.Job.RecordModel(run, "LiteLLM", "chat", "v1", Guid.NewGuid(), new string('c', 64), 4096, 0.2, Now.AddSeconds(1));
        f.Job.RecordUsage(run, 120, 30, 5, null, new string('d', 64), Now.AddSeconds(1)); f.Job.Complete(run, content, Now.AddSeconds(2));
        f.Job.Status.ShouldBe(EducationPlanGenerationStatus.Completed); f.Job.Content()!.Steps.Count.ShouldBe(5); f.Job.EstimatedCostUsd.ShouldBeNull(); f.Job.UsageMayBeIncomplete.ShouldBeFalse(); f.Plan.CurrentRevisionId.ShouldBeNull();
        var draft = Revision(f.Plan, f.Basis, content, EducationTeachingPlanRevisionKind.Draft); draft.LinkGeneration(f.Job); f.Plan.Advance(draft); draft.GenerationJobId.ShouldBe(f.Job.Id);
        var confirmed = Revision(f.Plan, f.Basis, content, EducationTeachingPlanRevisionKind.Confirmed, draft); confirmed.GenerationJobId.ShouldBe(f.Job.Id); f.Plan.PublishedRevisionId.ShouldBeNull();
        Should.Throw<BusinessException>(() => f.Job.Complete(run, content, Now.AddSeconds(3)));
    }
    [Fact]
    public void Plan_Generation_Expired_Lease_Should_Stop_The_Round_Without_Claiming_A_New_Call_Or_Accepting_Late_Output()
    {
        var f = GenerationFixture(); var run = Guid.NewGuid(); f.Job.Claim(run, Now, 10).ShouldBeTrue();
        f.Job.RecordModel(run, "LiteLLM", "chat", "v1", null, new string('c', 64), 400, 0, Now.AddSeconds(1));
        f.Job.Heartbeat(Guid.NewGuid(), Now.AddSeconds(1), 10).ShouldBeFalse(); f.Job.Heartbeat(run, Now.AddSeconds(1), 10).ShouldBeTrue();
        f.Job.Claim(Guid.NewGuid(), Now.AddSeconds(12), 10).ShouldBeFalse(); f.Job.Status.ShouldBe(EducationPlanGenerationStatus.Failed); f.Job.Failure.ShouldBe(EducationPlanGenerationFailure.LeaseExpired); f.Job.UsageMayBeIncomplete.ShouldBeTrue();
        var content = EducationTeachingPlanGenerationOutput.Parse(f.Job.Id, ProposalJson(f.Request), f.Request, f.Basis);
        Should.Throw<BusinessException>(() => f.Job.Complete(run, content, Now.AddSeconds(12))); f.Job.Content().ShouldBeNull();
    }
    [Theory]
    [InlineData("unknown-field")]
    [InlineData("duplicate-field")]
    [InlineData("missing-kind")]
    [InlineData("numeric-kind")]
    [InlineData("missing-diagnosis")]
    [InlineData("wrong-due")]
    [InlineData("unordered")]
    [InlineData("too-much-work")]
    [InlineData("empty-instructions")]
    [InlineData("material-invention")]
    [InlineData("invalid-json")]
    public void Plan_Generation_Output_Should_Reject_Untrusted_Or_Unsupported_Content(string invalid)
    {
        var f = GenerationFixture(); var raw = ProposalJson(f.Request); var json = JsonNode.Parse(raw)!.AsObject(); var steps = json["steps"]!.AsArray();
        switch (invalid) {
            case "unknown-field": json["studentId"] = Guid.NewGuid().ToString(); break;
            case "duplicate-field": raw = raw.Replace("\"title\":", "\"title\":\"PRIVATE-MODEL-BODY\",\"title\":"); break;
            case "missing-kind": steps[0]!.AsObject().Remove("kind"); break;
            case "numeric-kind": steps[0]!["kind"] = 0; break;
            case "missing-diagnosis": steps[0]!["kind"] = "Practice"; break;
            case "wrong-due": steps[^1]!["dueAtUtc"] = f.Request.StartsAtUtc.ToString("O"); break;
            case "unordered": steps[1]!["dueAtUtc"] = f.Request.StartsAtUtc.ToString("O"); break;
            case "too-much-work": steps[0]!["estimatedMinutes"] = 60; break;
            case "empty-instructions": steps[0]!["instructions"] = ""; break;
            case "material-invention": json["materials"] = new JsonArray(); break;
            case "invalid-json": raw = "PRIVATE-MODEL-BODY"; break;
        }
        if (invalid is not ("duplicate-field" or "invalid-json")) raw = json.ToJsonString();
        var error = Should.Throw<BusinessException>(() => EducationTeachingPlanGenerationOutput.Parse(f.Job.Id, raw, f.Request, f.Basis)); error.Message.ShouldNotContain("PRIVATE-MODEL-BODY");
    }
    [Fact]
    public void Plan_Generation_Request_And_Provenance_Should_Bind_The_Exact_Student_Points_And_Plan()
    {
        var f = GenerationFixture(); f.Request.StudentUserId = Guid.NewGuid(); Should.Throw<BusinessException>(() => f.Request.Validate(f.Basis));
        f.Request.StudentUserId = f.Basis.Student.StudentUserId; f.Request.TargetPointRevisionIds = [Guid.NewGuid()]; Should.Throw<BusinessException>(() => f.Request.Validate(f.Basis));
        var other = GenerationFixture(); var draft = Revision(other.Plan, other.Basis, Content(other.Basis), EducationTeachingPlanRevisionKind.Draft); Should.Throw<BusinessException>(() => draft.LinkGeneration(f.Job));
    }
}
