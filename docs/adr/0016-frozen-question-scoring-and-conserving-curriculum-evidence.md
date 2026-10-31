# Calculate frozen question scores and conserve curriculum evidence

Status: accepted for the domain calculation slice. The grading persistence, teacher confirmation, task-wide result release and learning-analysis use cases remain required work under PRD FR16/FR17/FR29–FR33.

## Decision

`EducationAssessmentScoring.Calculate` uses only the frozen `ExamPaperQuestion` and one original structured response. It does not reuse the enterprise `ExamAnswer` normalization or read later source questions and current answers. Single choice, multiple choice and true/false produce explicit whole-question scores. Fill blanks preserve stable blank IDs and execute only the frozen Exact/IgnoreCase/TrimWhitespace rules; Manual blanks and all subjective points, including unanswered ones, remain pending with nullable scores. Any pending part prevents a complete question total. Unknown identities, incompatible shapes and invalid or missing required rules are rejected.

The existing approval flow permits a correct choice answer to differ in case from its option key, and permits true/false aliases including “对/错” and display options on a boolean question. Resolve only this frozen answer evidence using its established semantics. A student's selected option remains an exact stable identity, and neither their text/order nor the frozen snapshot is changed. Boolean response identities remain `true` and `false` regardless of display options.

`EducationAssessmentGradeEvidence.Build` accepts a complete confirmed question result and checks every part's identity, maximum and mapping against the frozen rule. Every mapping records its stable knowledge-point and revision IDs, maximum, earned score and `education-grade-evidence-v1`. Pending parts, unknown mappings, changed maxima and non-conserving or invalid two-decimal scores are rejected. Correct, incorrect and unanswered objective questions all produce full mapping evidence.

For blanks and subjective questions, aggregate the actual frozen point scores by mapping. For objective questions, use the confirmed mapping weights and maxima, convert earned marks to integer cents, take each weighted floor, then distribute remaining cents in fixed descending fractional-remainder order with mapping ID as the tie-breaker. Never exceed a mapping's frozen maximum. A capped row can require more than one pass over the other rows; the remaining count is bounded by the mapping count and a pass with no progress is rejected. Full marks use the confirmed maxima directly. The mapping maximum and earned sums must exactly equal the question maximum and earned sums.

For example, a 1.00-point question can have weights `[.3055,.3055,.3055,.0245,.0245,.0345]` and maxima `[.31,.31,.31,.02,.02,.03]`. A valid .98 result allocates `[.31,.30,.30,.02,.02,.03]` in stable mapping order. A one-pass remainder algorithm cannot allocate its last cent because the three small mappings are capped. This case is covered through an approved and frozen multiple-choice question with 49 of 50 correct options selected.

## Scope and verification

These are concrete project domain functions and value records, with no new provider, scoring interface or strategy framework. They extend the frozen definitions from [ADR 0013](0013-explicit-question-scoring-and-approved-definition-revisions.md) and the original-response/submission boundary from [ADR 0015](0015-structured-response-revisions-and-single-assessment-submission.md). Future grading revisions must preserve these rule and algorithm versions together with the exact submission and response references.

Domain tests verify scoring compatibility, raw-response preservation, manual pending states, complete evidence, rounding, cap interactions and stable mapping allocation. They do not prove automatic grading jobs, relational grading revisions, teacher release, student visibility, reports, WeChat or a complete teaching cycle. Actual execution results remain in the [implementation record](../education-implementation-progress.md).
