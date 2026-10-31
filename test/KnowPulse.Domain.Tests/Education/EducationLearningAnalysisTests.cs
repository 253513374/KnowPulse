using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationLearningAnalysisTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    private static Guid Id(int value) => Guid.Parse($"70000000-0000-0000-0000-{value:D12}");
    private static EducationLearningAnalysisScope Scope() => new()
    { SchoolId = Id(1), TeachingAssignmentId = Id(2), StudentUserId = Id(3), EducationSubjectId = Id(4), TextbookVersionId = Id(5),
      WindowStartsAtUtc = Now.AddDays(-10), WindowEndsAtUtc = Now, Points = [new(Id(10), Id(11), "A", "知识点甲"), new(Id(20), Id(21), "B", "知识点乙")] };
    private static EducationLearningQuestionEvidence Row() => new()
    {
        QuestionGradeId = Guid.NewGuid(), GradeRevisionId = Guid.NewGuid(), SchoolId = Id(1), TeachingAssignmentId = Id(2), StudentUserId = Id(3), EducationSubjectId = Id(4), TextbookVersionId = Id(5),
        TaskId = Guid.NewGuid(), SubmissionId = Guid.NewGuid(), AttemptId = Guid.NewGuid(), PaperQuestionId = Guid.NewGuid(), OriginalQuestionId = Guid.NewGuid(),
        QuestionDefinitionRevisionId = Guid.NewGuid(), DisplayOrder = 1, AttemptStartedAtUtc = Now.AddDays(-4), EffectiveAtUtc = Now.AddDays(-3), GradeConfirmedAtUtc = Now.AddDays(-2),
        MaxScore = 10, ObtainedScore = 8, Use = EducationLearningEvidenceUse.Independent, QualificationRevisionId = Guid.NewGuid(),
        NoHintsOrAnswersConfirmed = true, NewRequirementsConfirmed = true, OriginalRelationshipsChecked = true,
        Points = [new(Id(101), Id(10), Id(11), 6, 4), new(Id(102), Id(20), Id(21), 4, 4)]
    };
    private static EducationMasteryCalculationRule Rule() => new()
    { Id = Id(300), SchoolId = Id(1), EducationSubjectId = Id(4), RevisionNumber = 1, WindowDays = 30,
      MinimumIndependentQuestionCount = 1, MinimumAssessmentCount = 1, MaximumLatestEvidenceAgeDays = 10, TargetRatio = .8m };
    private static EducationLearningAnalysisResult Calculate(IReadOnlyCollection<EducationLearningQuestionEvidence> rows,
        EducationMasteryCalculationRule? rule = null, IReadOnlyCollection<EducationLearningQuestionRelationship>? relationships = null,
        IReadOnlyCollection<EducationLearningQuestionExposure>? exposures = null, EducationLearningAnalysisScope? scope = null,
        IReadOnlyCollection<EducationLearningQuestionAttempt>? history = null) =>
        EducationLearningAnalysis.Calculate(scope ?? Scope(), rows, history ?? rows.Select(Attempt).ToArray(), relationships ?? [], exposures ?? [], Now, rule);
    private static EducationLearningQuestionAttempt Attempt(EducationLearningQuestionEvidence row) => new(row.TenantId, row.SchoolId, row.EducationSubjectId, row.StudentUserId,
        row.TaskId, row.AttemptId, row.PaperQuestionId, row.OriginalQuestionId, row.DisplayOrder, row.AttemptStartedAtUtc);

    [Theory]
    [InlineData(EducationLearningExposureUse.Unverified, EducationLearningExclusionReason.UnverifiedExposure)]
    [InlineData(EducationLearningExposureUse.Confirmed, EducationLearningExclusionReason.EarlierExposure)]
    public void Unverified_Study_Contact_Is_Exact_While_Confirmed_Hints_Still_Cover_The_Original(EducationLearningExposureUse use, EducationLearningExclusionReason expected)
    {
        var row = Row(); var other = new EducationLearningQuestionExposure(Guid.NewGuid(), row.StudentUserId, row.OriginalQuestionId, Now.AddDays(-5), use, null, Guid.NewGuid());
        Calculate([row], exposures: [other]).Evidence.Single().ExclusionReason.ShouldBe(use == EducationLearningExposureUse.Confirmed ? EducationLearningExclusionReason.EarlierExposure : EducationLearningExclusionReason.None);
        Calculate([row], exposures: [other with { PaperQuestionId = row.PaperQuestionId }]).Evidence.Single().ExclusionReason.ShouldBe(expected);
        Calculate([row], exposures: [other]).CalculationVersion.ShouldBe(EducationLearningAnalysis.ExactContextAlgorithmVersion);
        Calculate([row], exposures: [other with { PaperQuestionId = null }]).CalculationVersion.ShouldBe(EducationLearningAnalysis.AlgorithmVersion);
        Calculate([row], exposures: [other with { PaperQuestionId = null }]).Evidence.Single().ExclusionReason.ShouldBe(expected);
    }
    [Fact]
    public void Exact_No_Hints_Should_Not_Clear_Another_Real_Review_Exposure_And_Legacy_Json_Should_Stay_Identical()
    {
        var row = Row(); var noHints = new EducationLearningQuestionExposure(Guid.NewGuid(), row.StudentUserId, row.OriginalQuestionId, Now.AddDays(-5), EducationLearningExposureUse.ConfirmedNoHints, Guid.NewGuid(), row.PaperQuestionId);
        Calculate([row], exposures: [noHints]).Evidence.Single().ExclusionReason.ShouldBe(EducationLearningExclusionReason.None);
        var real = new EducationLearningQuestionExposure(Guid.NewGuid(), row.StudentUserId, row.OriginalQuestionId, Now.AddDays(-5));
        Calculate([row], exposures: [noHints, real]).Evidence.Single().ExclusionReason.ShouldBe(EducationLearningExclusionReason.EarlierExposure);
        var legacy = new { real.Id, real.StudentUserId, real.OriginalQuestionId, real.ExposedAtUtc, real.Use, real.HintReviewRevisionId };
        System.Text.Json.JsonSerializer.Serialize(real).ShouldBe(System.Text.Json.JsonSerializer.Serialize(legacy));
        var legacyCanonical = new { EducationLearningAnalysis.AlgorithmVersion,
            Scope = Scope() with { Points = Scope().Points.OrderBy(x => x.KnowledgePointId).ThenBy(x => x.KnowledgePointRevisionId).ToArray() },
            Rule = (EducationMasteryCalculationRule?)null, Evidence = new[] { row }.OrderBy(x => x.GradeRevisionId), History = new[] { Attempt(row) }.OrderBy(x => x.AttemptId).ThenBy(x => x.PaperQuestionId),
            Relationships = Array.Empty<EducationLearningQuestionRelationship>(), Exposures = new[] { legacy } };
        var legacyHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(legacyCanonical))));
        Calculate([row], exposures: [real]).SourceFingerprint.ShouldBe(legacyHash);
    }
    [Fact]
    public void Confirmed_Study_Hints_Should_Still_Block_Known_Variants_After_An_Exact_No_Hints_Decision()
    {
        var row = Row(); var original = Guid.NewGuid();
        var known = new EducationLearningQuestionExposure(Guid.NewGuid(), row.StudentUserId, original, Now.AddDays(-5), EducationLearningExposureUse.Confirmed, Guid.NewGuid(), Guid.NewGuid());
        var exact = new EducationLearningQuestionExposure(Guid.NewGuid(), row.StudentUserId, row.OriginalQuestionId, Now.AddDays(-5), EducationLearningExposureUse.ConfirmedNoHints, Guid.NewGuid(), row.PaperQuestionId);
        Calculate([row], exposures: [known, exact], relationships: [new(Guid.NewGuid(), original, row.OriginalQuestionId)]).Evidence.Single().ExclusionReason.ShouldBe(EducationLearningExclusionReason.EarlierExposure);
    }

    [Fact]
    public void Missing_Evidence_Is_Unassessed_And_Never_Zero_Percent()
    {
        var result = Calculate([]);
        result.Points.All(x => x.State == EducationMasteryState.Unassessed && x.Ratio == null && x.IndependentQuestionCount == 0).ShouldBeTrue();
        result.TargetPointCount.ShouldBe(2); result.AssessedPointCount.ShouldBe(0); result.Evidence.ShouldBeEmpty();
        result.WindowStartsAtUtc.ShouldBe(Scope().WindowStartsAtUtc); result.WindowEndsAtUtc.ShouldBe(Scope().WindowEndsAtUtc);
    }

    [Fact]
    public void Unverified_Scores_Do_Not_Become_Independent_Evidence_Or_A_Zero_Score()
    {
        var result = Calculate([Row() with { Use = EducationLearningEvidenceUse.Unverified, QualificationRevisionId = null }], Rule());
        result.Points.All(x => x.State == EducationMasteryState.Unassessed && x.Ratio == null && x.UnverifiedQuestionCount == 1).ShouldBeTrue();
        result.Evidence.Single().ExclusionReason.ShouldBe(EducationLearningExclusionReason.Unverified);
    }

    [Fact]
    public void Unconfigured_Rules_Show_Only_The_Actual_Ratio_And_Insufficient_Evidence()
    {
        var result = Calculate([Row()]); var a = result.Points.Single(x => x.Point.Code == "A");
        a.Ratio.ShouldBe(4m / 6m); a.State.ShouldBe(EducationMasteryState.InsufficientEvidence); a.RuleMissing.ShouldBeTrue();
        result.Points.All(x => x.State == EducationMasteryState.InsufficientEvidence).ShouldBeTrue(); result.RuleRevisionId.ShouldBeNull();
    }

    [Fact]
    public void Equal_Total_Scores_Can_Have_Different_Knowledge_Point_Findings_Without_Duplicating_Whole_Question_Marks()
    {
        var first = Calculate([Row()], Rule());
        var second = Calculate([Row() with { Points = [new(Id(101), Id(10), Id(11), 6, 6), new(Id(102), Id(20), Id(21), 4, 2)] }], Rule());
        first.Points.Sum(x => x.ObtainedScore).ShouldBe(8); second.Points.Sum(x => x.ObtainedScore).ShouldBe(8);
        first.Points.Sum(x => x.MaxScore).ShouldBe(10); second.Points.Sum(x => x.MaxScore).ShouldBe(10);
        first.Points.Single(x => x.Point.Code == "A").State.ShouldBe(EducationMasteryState.NeedsConsolidation);
        second.Points.Single(x => x.Point.Code == "A").State.ShouldBe(EducationMasteryState.CurrentTargetMet);
        first.Points.Single(x => x.Point.Code == "B").State.ShouldBe(EducationMasteryState.CurrentTargetMet);
        second.Points.Single(x => x.Point.Code == "B").State.ShouldBe(EducationMasteryState.NeedsConsolidation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Revisions_And_Known_Copies_Do_Not_Add_Another_Independent_Question(bool copy)
    {
        var first = Row(); var next = Row() with { OriginalQuestionId = copy ? Guid.NewGuid() : first.OriginalQuestionId,
            AttemptStartedAtUtc = Now.AddDays(-2), EffectiveAtUtc = Now.AddDays(-1), GradeConfirmedAtUtc = Now };
        EducationLearningQuestionRelationship[] links = copy ? [new(Guid.NewGuid(), first.OriginalQuestionId, next.OriginalQuestionId)] : [];
        var result = Calculate([next, first], Rule(), links);
        result.Points.All(x => x.IndependentQuestionCount == 1 && x.AssessmentCount == 1 && x.PracticeQuestionCount == 1).ShouldBeTrue();
        result.Points.Sum(x => x.ObtainedScore).ShouldBe(8);
        result.Evidence.Single(x => x.GradeRevisionId == next.GradeRevisionId).ExclusionReason.ShouldBe(EducationLearningExclusionReason.EarlierAttempt);
    }

    [Fact]
    public void Transitive_And_Cyclic_Known_Relationships_Form_One_Stable_Original_Group()
    {
        var a = Row(); var b = Row() with { AttemptStartedAtUtc = Now.AddDays(-3), EffectiveAtUtc = Now.AddDays(-2), GradeConfirmedAtUtc = Now };
        var c = Row() with { AttemptStartedAtUtc = Now.AddDays(-2), EffectiveAtUtc = Now.AddDays(-1), GradeConfirmedAtUtc = Now };
        EducationLearningQuestionRelationship[] links = [new(Guid.NewGuid(), a.OriginalQuestionId, b.OriginalQuestionId), new(Guid.NewGuid(), b.OriginalQuestionId, c.OriginalQuestionId), new(Guid.NewGuid(), c.OriginalQuestionId, a.OriginalQuestionId)];
        var result = Calculate([a, b, c], Rule(), links);
        result.Evidence.Select(x => x.OriginalGroupId).Distinct().Count().ShouldBe(1);
        result.Evidence.Single(x => x.ExclusionReason == EducationLearningExclusionReason.None).GradeRevisionId.ShouldBe(a.GradeRevisionId);
        Calculate([c, a, b], Rule(), links.Reverse().ToArray(), scope: Scope() with { Points = Scope().Points.Reverse().ToArray() }).SourceFingerprint.ShouldBe(result.SourceFingerprint);
    }

    [Theory]
    [InlineData(-25, true)]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Answers_Or_Hints_Before_Or_During_An_Attempt_Exclude_It_But_Later_Review_Does_Not(int hoursFromSubmission, bool practice)
    {
        var row = Row(); var exposure = new EducationLearningQuestionExposure(Guid.NewGuid(), row.StudentUserId, row.OriginalQuestionId, row.EffectiveAtUtc.AddHours(hoursFromSubmission));
        var result = Calculate([row], Rule(), exposures: [exposure]);
        result.Evidence.Single().ExclusionReason.ShouldBe(practice ? EducationLearningExclusionReason.EarlierExposure : EducationLearningExclusionReason.None);
        result.Points.All(x => x.IndependentQuestionCount == (practice ? 0 : 1)).ShouldBeTrue();
    }

    [Fact]
    public void Exposure_To_A_Known_Original_Group_Also_Excludes_The_Copy()
    {
        var row = Row(); var otherQuestion = Guid.NewGuid();
        var result = Calculate([row], Rule(), [new(Guid.NewGuid(), row.OriginalQuestionId, otherQuestion)],
            [new(Guid.NewGuid(), row.StudentUserId, otherQuestion, row.AttemptStartedAtUtc.AddMinutes(-1))]);
        result.Evidence.Single().ExclusionReason.ShouldBe(EducationLearningExclusionReason.EarlierExposure);
    }

    [Theory]
    [InlineData(EducationLearningExposureUse.Unverified)]
    [InlineData(EducationLearningExposureUse.ConfirmedNoHints)]
    public void A_Linked_Feedback_Context_Is_Not_Automatically_Proof_Of_An_Answer_Or_Hint(EducationLearningExposureUse use)
    {
        var row = Row(); var exposure = new EducationLearningQuestionExposure(Guid.NewGuid(), row.StudentUserId, row.OriginalQuestionId, row.AttemptStartedAtUtc.AddHours(-1),
            use, use == EducationLearningExposureUse.ConfirmedNoHints ? Guid.NewGuid() : null);
        var result = Calculate([row], Rule(), exposures: [exposure]);
        result.Evidence.Single().ExclusionReason.ShouldBe(use == EducationLearningExposureUse.Unverified ? EducationLearningExclusionReason.UnverifiedExposure : EducationLearningExclusionReason.None);
        result.Points.All(x => x.PracticeQuestionCount == 0).ShouldBeTrue();
        result.Points.All(x => x.UnverifiedQuestionCount == (use == EducationLearningExposureUse.Unverified ? 1 : 0)).ShouldBeTrue();
    }

    [Fact]
    public void Dismissing_A_Possible_Hint_Context_Requires_An_Actual_Review_Revision_Reference()
    {
        var row = Row(); var exposure = new EducationLearningQuestionExposure(Guid.NewGuid(), row.StudentUserId, row.OriginalQuestionId,
            row.AttemptStartedAtUtc.AddHours(-1), EducationLearningExposureUse.ConfirmedNoHints);
        Should.Throw<BusinessException>(() => Calculate([row], Rule(), exposures: [exposure])).Code.ShouldBe(EducationLearningErrorCodes.InvalidEvidence);
    }

    [Fact]
    public void A_Previous_Attempt_Outside_The_Window_Does_Not_Make_The_Repeated_Question_New()
    {
        var prior = Row() with { AttemptStartedAtUtc = Now.AddDays(-13), EffectiveAtUtc = Now.AddDays(-12), GradeConfirmedAtUtc = Now.AddDays(-11) };
        var repeat = Row() with { OriginalQuestionId = prior.OriginalQuestionId };
        var result = Calculate([repeat, prior], Rule());
        result.Evidence.Single(x => x.GradeRevisionId == prior.GradeRevisionId).ExclusionReason.ShouldBe(EducationLearningExclusionReason.OutsideWindow);
        result.Evidence.Single(x => x.GradeRevisionId == repeat.GradeRevisionId).ExclusionReason.ShouldBe(EducationLearningExclusionReason.EarlierAttempt);
        result.Points.All(x => x.Ratio == null && x.PracticeQuestionCount == 1).ShouldBeTrue();
    }

    [Fact]
    public void An_Earlier_Ungraded_Or_Abandoned_Attempt_Also_Prevents_A_False_New_Independent_Question()
    {
        var row = Row(); var previous = Attempt(row) with { AttemptId = Guid.NewGuid(), TaskId = Guid.NewGuid(), PaperQuestionId = Guid.NewGuid(), StartedAtUtc = row.AttemptStartedAtUtc.AddDays(-1) };
        var result = Calculate([row], Rule(), history: [Attempt(row), previous]);
        result.Evidence.Single().ExclusionReason.ShouldBe(EducationLearningExclusionReason.EarlierAttempt);
        result.Points.All(x => x.Ratio == null && x.PracticeQuestionCount == 1).ShouldBeTrue();
    }

    [Fact]
    public void Evidence_Window_Is_Half_Open_And_Does_Not_Count_The_End_Instant()
    {
        var scope = Scope() with { WindowEndsAtUtc = Now.AddHours(-1) };
        var begin = Row() with { AttemptStartedAtUtc = scope.WindowStartsAtUtc.AddHours(-1), EffectiveAtUtc = scope.WindowStartsAtUtc, GradeConfirmedAtUtc = scope.WindowStartsAtUtc.AddHours(1) };
        var end = Row() with { AttemptStartedAtUtc = scope.WindowEndsAtUtc.AddHours(-1), EffectiveAtUtc = scope.WindowEndsAtUtc, GradeConfirmedAtUtc = Now };
        var result = Calculate([begin, end], Rule(), scope: scope);
        result.Evidence.Single(x => x.GradeRevisionId == end.GradeRevisionId).ExclusionReason.ShouldBe(EducationLearningExclusionReason.OutsideWindow);
        result.Points.All(x => x.IndependentQuestionCount == 1).ShouldBeTrue();
    }

    [Fact]
    public void Adequacy_Explains_Independent_Question_And_Assessment_Gaps()
    {
        var first = Row(); var other = Row() with { TaskId = first.TaskId, SubmissionId = first.SubmissionId, AttemptId = first.AttemptId };
        var questions = Calculate([first], Rule() with { MinimumIndependentQuestionCount = 2 });
        questions.Points.All(x => x.State == EducationMasteryState.InsufficientEvidence && x.MissingIndependentQuestionCount == 1).ShouldBeTrue();
        var tasks = Calculate([first, other], Rule() with { MinimumIndependentQuestionCount = 2, MinimumAssessmentCount = 2 });
        tasks.Points.All(x => x.State == EducationMasteryState.InsufficientEvidence && x.MissingAssessmentCount == 1 && x.IndependentQuestionCount == 2).ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Evidence_Recency_Uses_The_Report_Window_And_An_Exact_Inclusive_Age_Boundary(bool older)
    {
        var at = Now.AddDays(-5).AddTicks(older ? -1 : 0);
        var result = Calculate([Row() with { AttemptStartedAtUtc = at.AddHours(-1), EffectiveAtUtc = at, GradeConfirmedAtUtc = at.AddHours(1) }], Rule() with { MaximumLatestEvidenceAgeDays = 5 });
        result.Points.All(x => x.RecentEvidenceMissing == older).ShouldBeTrue();
        result.Points.Single(x => x.Point.Code == "B").State.ShouldBe(older ? EducationMasteryState.InsufficientEvidence : EducationMasteryState.CurrentTargetMet);
    }

    [Fact]
    public void Classification_Uses_The_Unrounded_Ratio()
    {
        var row = Row() with { MaxScore = 999.99m, ObtainedScore = 799.99m, Points = [new(Id(101), Id(10), Id(11), 999.99m, 799.99m)] };
        var a = Calculate([row], Rule()).Points.Single(x => x.Point.Code == "A");
        decimal.Round(a.Ratio!.Value, 4).ShouldBe(.8m); a.State.ShouldBe(EducationMasteryState.NeedsConsolidation);
    }

    [Fact]
    public void Multiple_Mappings_To_One_Point_Conserve_Scores_And_Count_One_Question()
    {
        var row = Row() with { Points = [new(Id(101), Id(10), Id(11), 6, 4), new(Id(102), Id(10), Id(11), 4, 4)] };
        var a = Calculate([row], Rule()).Points.Single(x => x.Point.Code == "A");
        a.MaxScore.ShouldBe(10); a.ObtainedScore.ShouldBe(8); a.IndependentQuestionCount.ShouldBe(1); a.State.ShouldBe(EducationMasteryState.CurrentTargetMet);
    }

    [Fact]
    public void A_Changed_Knowledge_Point_Revision_Is_Not_Silently_Merged_With_Old_Evidence()
    {
        var scope = Scope() with { Points = [new(Id(10), Id(12), "A", "修订后的甲"), Scope().Points[1]] };
        var result = Calculate([Row()], Rule(), scope: scope);
        result.Points.Single(x => x.Point.Code == "A").Ratio.ShouldBeNull(); result.AssessedPointCount.ShouldBe(1);
    }

    [Fact]
    public void Replaying_The_Same_Grade_Is_Idempotent_And_Correction_Replaces_Rather_Than_Accumulates()
    {
        var row = Row(); var original = Calculate([row], Rule());
        var replay = Calculate([row, row with { Points = row.Points.Reverse().ToArray() }], Rule());
        replay.SourceFingerprint.ShouldBe(original.SourceFingerprint); replay.Evidence.Count.ShouldBe(1);
        var corrected = row with { GradeRevisionId = Guid.NewGuid(), ObtainedScore = 10, Points = [new(Id(101), Id(10), Id(11), 6, 6), new(Id(102), Id(20), Id(21), 4, 4)] };
        var current = Calculate([corrected], Rule()); current.SourceFingerprint.ShouldNotBe(original.SourceFingerprint);
        current.Points.Sum(x => x.ObtainedScore).ShouldBe(10);
        Should.Throw<BusinessException>(() => Calculate([row, corrected], Rule())).Code.ShouldBe(EducationLearningErrorCodes.InvalidEvidence);
    }

    [Fact]
    public void Equivalent_Decimal_Scales_Do_Not_Create_Conflicts_Or_Another_Report_Source_Version()
    {
        var row = Row(); var first = Calculate([row], Rule());
        var restored = row with { MaxScore = 10.00m, ObtainedScore = 8.00m,
            Points = [new(Id(101), Id(10), Id(11), 6.00m, 4.00m), new(Id(102), Id(20), Id(21), 4.00m, 4.00m)] };
        var replay = Calculate([restored, row], Rule() with { TargetRatio = .8000m });
        replay.SourceFingerprint.ShouldBe(first.SourceFingerprint); replay.Evidence.Count.ShouldBe(1);
    }

    [Fact]
    public void Invalidated_Evidence_Is_Excluded_And_Does_Not_Become_A_Zero_Grade()
    {
        var result = Calculate([Row() with { InvalidationRevisionId = Guid.NewGuid() }], Rule());
        result.Points.All(x => x.InvalidQuestionCount == 1 && x.Ratio == null && x.State == EducationMasteryState.Unassessed).ShouldBeTrue();
    }

    [Theory]
    [InlineData("student")]
    [InlineData("teacher")]
    [InlineData("school")]
    [InlineData("subject")]
    [InlineData("book")]
    [InlineData("tenant")]
    [InlineData("missing-qualification")]
    [InlineData("hints-unconfirmed")]
    [InlineData("requirements-unconfirmed")]
    [InlineData("origin-unchecked")]
    [InlineData("invalid-marks")]
    [InlineData("non-conserving")]
    [InlineData("duplicate-mapping")]
    [InlineData("precision")]
    [InlineData("tiny-precision")]
    public void Invalid_Or_Cross_Scope_Inputs_Are_Rejected(string change)
    {
        var row = Row(); row = change switch
        {
            "student" => row with { StudentUserId = Guid.NewGuid() }, "teacher" => row with { TeachingAssignmentId = Guid.NewGuid() },
            "school" => row with { SchoolId = Guid.NewGuid() }, "subject" => row with { EducationSubjectId = Guid.NewGuid() },
            "book" => row with { TextbookVersionId = Guid.NewGuid() }, "tenant" => row with { TenantId = Guid.NewGuid() },
            "missing-qualification" => row with { QualificationRevisionId = null }, "hints-unconfirmed" => row with { NoHintsOrAnswersConfirmed = false },
            "requirements-unconfirmed" => row with { NewRequirementsConfirmed = false }, "origin-unchecked" => row with { OriginalRelationshipsChecked = false },
            "invalid-marks" => row with { ObtainedScore = 11 }, "non-conserving" => row with { ObtainedScore = 9 },
            "duplicate-mapping" => row with { Points = [row.Points[0], row.Points[0]] }, "precision" => row with { ObtainedScore = 8.001m },
            "tiny-precision" => row with { ObtainedScore = .000000001m }, _ => row
        };
        Should.Throw<BusinessException>(() => Calculate([row], Rule())).Code.ShouldBe(EducationLearningErrorCodes.InvalidEvidence);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("threshold")]
    [InlineData("questions")]
    [InlineData("assessments")]
    [InlineData("age")]
    [InlineData("window")]
    [InlineData("subject")]
    public void Invalid_Or_Unrelated_Rules_Do_Not_Create_A_Determined_Classification(string change)
    {
        var rule = Rule(); rule = change switch
        {
            "version" => rule with { RevisionNumber = 0 }, "threshold" => rule with { TargetRatio = 0 },
            "questions" => rule with { MinimumIndependentQuestionCount = 0 }, "assessments" => rule with { MinimumAssessmentCount = 0 },
            "age" => rule with { MaximumLatestEvidenceAgeDays = 31 }, "window" => rule with { WindowDays = 5 },
            "subject" => rule with { EducationSubjectId = Guid.NewGuid() }, _ => rule
        };
        Should.Throw<BusinessException>(() => Calculate([Row()], rule)).Code.ShouldBe(EducationLearningErrorCodes.InvalidRule);
    }
}
