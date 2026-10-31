# Version manual teaching plans and authorize their student delivery

Date: 2026-10-17. Status: accepted for the manual teacher/student Web slice. This extends ADRs 0019–0022; it does not change formal assessment release or claim complete learning execution.

Subsequent decision: ADR 0024 adds review of exact delivered plan contexts; the Unverified-only limitation below describes this original slice.

Subsequent decision: ADR 0025 assigns versioned inline learning activities and records progress/difficulty/handling; the instruction-only limitation below describes this original slice.

Subsequent decision: ADR 0044 adds anonymous, private AI draft jobs and explicit adoption into these same plan versions. The no-AI-job limitation below describes this original slice; confirmation, publication and current student gates remain mandatory.

Subsequent decision: ADR 0045 adds independent execution control and private teacher review decisions. Content publication, withdrawal and current student availability remain separate from pausing/ending new execution.

## Decision

A plan belongs to one teacher and one student in an exact school, term, class, teaching assignment, enrollment, membership, subject and textbook context. Eight teacher operations require both TeachingPlanManage and AnalysisManage, the current active account and current recorded teaching authority. Two student operations require TeachingPlanView and derive the student from the caller; they never accept a student identity to impersonate. Existing tenant and relationship checks remain mandatory.

The first draft must reference the teacher's current, complete student report snapshot. The service recaptures the report source under its authority barrier and rejects a stale snapshot before creating the plan. A draft freezes the student's point estimates, own evidence and assessment facts, task selection, exact question/definition references, adopted rule decision and window. Its fingerprint excludes other students' marks and the task-wide grade-set or release pointer. A correction affecting only another student must not invalidate this student's plan. Changing the student's own relevant scoring, eligibility, curriculum or rules invalidates the basis.

Before confirming or publishing, the service recaptures that same frozen scope and requires the own-student basis to remain current. It does not require the original class report to remain the current report forever. A later class report, unrelated class changes or replacing a score-only release with a full-review release must not by themselves invalidate unchanged personal evidence.

A plan contains a student goal, teacher actions, success criteria, fixed target point revisions, bounded period, reassessment time and concrete ordered steps with instructions, estimated minutes and due times. Targets must be in the captured basis. A reassessment step at the declared reassessment time is mandatory. If any selected target is unassessed or has insufficient evidence, confirmation requires a diagnostic first step. Missing evidence cannot be silently presented as a measured weakness. Confirmed/published schedules must still fit the known term, teaching, membership and enrollment end dates.

The supported content is teacher-authored inline text. Confirmation and publication independently require explicit confirmation of recipient/content, inline-material rights and student-safe text. These checks are not a substitute for authorization or source freshness. No AI plan job, external knowledge/material resolver or automatic task creation is introduced in this slice.

## Versions and recovery

Every save, confirmation, publication and withdrawal creates an immutable revision. Confirmation freezes exactly the saved draft, and publication freezes exactly the confirmed revision; edited content requires another draft. All operations use a stable operation ID, request fingerprint, expected current revision and root concurrency stamp. An authorized retry recovers the committed original receipt before testing stale expectations. Reusing an operation ID for different content is rejected.

The root tracks current private, current published, last published and public-state revisions separately. New drafts or confirmations preserve the existing published version. Withdrawal clears the published pointer, retains the last publication and exposes a distinct body-free public withdrawal revision. Saving a new private draft after withdrawal must not leak its ID, counter or content as student state. Replacement/withdrawal reasons and internal notes remain teacher-private.

Revision validation precedes insertion of a new root. Append and pointer advancement share a relational transaction. The Web stores the complete pending command in session storage before sending it, bound to account/tenant. A lost response can recover the original operation or resend the exact command; it is not treated as a proved failure. Navigation and edits are guarded while a command is pending, and explicit abandonment explains that the server may already have committed.

## Student boundary and actual delivery

Lists contain public-state cards without plan bodies; never-published plans are absent. Opening requires the expected public version and rereads current enrollment, membership, account, tenant, recorded teacher authority and basis. All explicitly linked assessment bases must currently permit FullReview before free-form plan text is delivered, since it could include hints or answers. Available plans with linked assessments also require AssessmentParticipate. Waiting, changed, unavailable and withdrawn responses contain no body.

The student projection is an explicit separate type: public version/time, teacher/class/subject labels, goal/actions/criteria, schedule, point labels/revision numbers and ordered steps. It excludes source reports, grades, private observations/notes/reasons, internal mapping identifiers and source fingerprints. A stored revision ID never bypasses current gates. MVC applies private/no-store and no-cache headers by service interface; generic audit serialization excludes bodies and private source objects.

Student reads hold School then student-account barriers. Source recapture in this path skips the teacher-account CAS only after those barriers are already held; it still validates recorded teacher facts. Teacher source callers retain their default School/teacher-account barrier. This avoids taking teacher and student account locks in reverse order or substituting a teacher identity during a student request.

Actual authorized body delivery records body-free context metadata for the frozen basis questions in the same transaction, idempotent per published plan/recipient/question/student. Cards, waiting responses and merely publishing a plan do not invent delivery. No reading/completion fact is inferred from these rows. A plan without basis questions creates no question-context rows.

Plan text may include hints, so the learning source includes these delivered contexts as Unverified from their actual delivery time. Earlier measurements remain unchanged. This is not a claim that a specific answer was provided; existing evidence qualification conservatively handles unknown context. A teacher review/clearance workflow for plan contexts is still required. Plan instructions do not themselves create learning assignments, corrections, completion evidence or new independent reassessment questions.

## Persistence and limits

Migration 20261017114653_AddEducationTeachingPlans adds three tables and sixteen Restrict paired foreign keys. Current/public pointers, child identity, recorded school context, fixed recipient/paper/question/definition and version sequence are constrained. Non-draft source and predecessor are both explicitly non-null: SQL null equality must not admit a confirmation without a predecessor. Withdrawal versions cannot retain bodies or internal notes. Existing rows/columns and historical nullable definitions are unchanged.

The existing report quotas apply to source recapture; basis/content are bounded to 8 MiB/256 KiB, with at most 100 targets and 50 steps. Lists explicitly cap the scanned candidates at 1,000 rather than silently truncating, and actual context inserts are batched. SQLite service tests and isolated PostgreSQL constraints/rollback checks provide bounded evidence, not full application concurrency, browser/device or production-capacity acceptance.

Remaining work includes AI drafting and model failure recovery, referenced material authorization, plan-context review, actual learning/difficulty/correction/reassessment execution, plan notifications, teacher trends/history policy and real WeChat. School retention, historical access and teacher handover remain unresolved policy work; this slice does not grant a new administrator/history bypass.
