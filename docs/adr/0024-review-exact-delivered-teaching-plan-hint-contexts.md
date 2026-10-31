# Review exact delivered teaching plan hint contexts

Date: 2026-10-17. Status: accepted for the service and compiled Web slice. Logged-in browser/device acceptance remains separate.

Subsequent decision: ADR 0025 implements inline activity execution, difficulty and private handling; its formal material/reassessment and runtime limits remain separate.

This extends ADRs 0019, 0020 and 0023. The previous manual-plan slice deliberately recorded delivered contexts as Unverified. It did not provide a teacher review path; treating every free-form plan as a confirmed answer or silently declaring it harmless would both misstate the evidence.

## Decision

Add three plan-context operations: read the exact context, save a versioned hint decision and recover an original operation. Each requires TeachingPlanManage, AnalysisManage and AssessmentManage, explicitly in the application service as well as on its contract. The third permission is essential because context comparison includes frozen question answers. Plan-management permission alone must not grant grading-answer access.

The reviewer must still be the plan's author, with an active account and current recorded teaching, student enrollment/membership, school and tenant authority. An existing receipt is reauthorized too. A plan/publication ID, listing or publication action cannot substitute for an actual delivered-context row. Students and other teachers cannot acquire the context or private decision reasons by knowing an ID.

A review binds the actual exposure, student, plan and exact published revision. That revision must contain the recorded frozen task/recipient/paper/question/definition. The teacher compares the exact student-safe plan content and frozen question, not the latest private draft, a revised public plan or a newly edited question. Internal plan notes and change reasons are not part of the delivered content. A later draft or withdrawal does not rewrite past delivery; its author can review the past context while current authority still permits it. This does not reopen a withdrawn plan to students.

The decision remains the existing three-valued use: Unverified, Confirmed (answer/hint present) or ConfirmedNoHints. Every write requires explicit confirmation of the exact context and a reason. An append-only revision records author, predecessor, UTC time, expected concurrency stamp, request fingerprint and stable operation ID. The root advances atomically with the child. A retry with the same original request returns the original receipt without rolling a later decision back; reuse of that operation ID for another request is rejected. Stale first writes and updates cannot silently replace a current decision.

No new generic polymorphic exposure table replaces the existing feedback facts. Two plan-review tables reuse the existing domain conventions and pair to the existing plan-delivery ledger and published revision. This avoids nullable alternative foreign-key associations or rewriting feedback review history.

## Evidence and presentation

The teacher's existing graded-question exposure list merges assessment-review, feedback and plan contexts in chronological order before paging. It shows a plan review link only to an authorized author. It does not reveal another author's private plan content or review reasons. Direct operations independently recheck authority; a visible link is not an access grant.

The dedicated Web review page renders the actual delivered plan and frozen question, the current decision and reason, and the explicit confirmation form. Before sending a write it retains the exact command in session storage scoped to tenant, account and exposure. Recovery queries the original operation, and resend preserves that original request. It never treats a missing/unknown response as proof of failure. Leaving, refreshing dirty content and explicitly discarding a pending command have guards; discarding explains that a server commit may exist. Real browser interaction remains a separate acceptance gate.

Report and student-growth source capture read only the current paired decision metadata, not reasons or plan/question text. Teacher source fingerprints retain the review revision ID, so a changed decision invalidates an old captured teacher report until recomputation. Student public fingerprints still follow ADR 0022: private review IDs and changes that do not affect a student's visible evidence are not independent public version signals.

A ConfirmedNoHints decision clears only this exact plan context. It cannot clear full assessment-review delivery, an earlier attempt, another feedback/plan context or a known original-question relationship. Exposure time remains the actual first delivery, not the review time. Earlier observations are not retroactively treated as exposed merely because a later context was reviewed. No decision creates a score, independent observation, completion or mastery fact.

The new teaching-plan source enum value is for the combined teacher-list projection. Existing assessment/feedback exposure constructors and persisted associations retain their original meaning. The plan ledger remains separate. The shared private/no-store response filter covers both the plan and evidence service interfaces, including the combined context metadata list. Generic audit ignores the new context and decision bodies, including reasons and frozen answers.

## Persistence and limits

Migration 20261017120955_AddEducationTeachingPlanHintReviews adds two tables, five Restrict foreign keys and a composite alternate key on the existing delivery ledger. It changes no existing row or column and does not backfill a decision for existing delivered contexts. Missing decisions remain Unverified.

The database pairs the exact delivered student/plan/version, publication author, child scope, predecessor and current pointer. It constrains nonempty reasons, defined use values, revision state and unique exposure/sequence (including host scope). Semantic checks for an actual Published kind and question membership remain explicit domain/application rules. Down removes only these new tables and the new alternate key.

Source quotas, list paging, account/school barriers and historical-access policy remain as previously defined. This slice does not implement AI plan drafting, learning/difficulty/correction execution, new-question reassessment, school retention or teacher handover, plan notifications or real WeChat. SQLite substitutes and isolated PostgreSQL constraints/rollback checks are not proof of full application concurrency, device behavior or production load.
