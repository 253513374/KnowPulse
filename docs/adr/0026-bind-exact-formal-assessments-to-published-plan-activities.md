# Bind exact formal assessments to published plan activities

Date: 2026-10-17. Status: accepted for the service and compiled Web slice. Extends ADRs 0014–0025 and replaces ADR 0025's deferred formal-binding boundary. This does not finish the education PRD.

Follow-up: ADR 0045 adds explicit pause/resume and teacher continue/adjust/end decisions. New formal starts consult that shared execution gate; started attempts keep the original rules below. The deferred execution-control statement at the end records this original slice.

Follow-up: [ADR 0027](0027-student-image-editing-and-explicit-local-draft-conflicts.md) adds actual student image editing/buffering and explicit draft-base conflict choices. The deferred image-editor statement below records this slice's original boundary.

Diagnostic and reassessment activities can bind one already published assessment owned by the plan author. The binding freezes the exact plan publication/activity/student, assessment publication/fixed recipient and paper version. A recipient cannot be bound to another activity. The existing assessment owns attempts, responses, sealing, grading, closure and unified release; the activity never copies or fabricates those facts.

## New questions, scope and explicit review

Preview and binding require current authority and publication, the same school, term, class, assignment, subject and textbook, exact frozen target-point coverage, an unstarted fixed student recipient, one allowed attempt, a future end within the activity window and a duration within its estimate. Every frozen question has an approved definition and conserved curriculum allocations. The author sees the exact public plan, frozen question content/options/answers/analysis/scoring points and confirms original relationships, new-question requirements and absence of hints. A new question ID is not proof of novelty.

The existing source reader checks the author's known original relationships, original plan-basis questions, earlier attempts including ungraded/abandoned/soft-deleted facts, prior exposures and any other formal assignment, even if that binding was stopped before starting. Preview returns a controlled fingerprint; binding rechecks it and the activity/task/paper heads under the existing school/teacher barrier. A first student start rechecks novelty under the assessment school/student/task barriers. These checks are limited by existing source quotas and known relationships; they do not claim automatic semantic equivalence detection.

## Actual context and corrections

Binding, preview, lists and exam start do not mark plan provision. Normalized bound-question rows plus the activity's actual first provision produce metadata-only virtual plan contexts. If provision preceded binding, that actual earlier timestamp is retained. The initial explicit no-hints conclusion belongs to this exact immutable plan and frozen paper; subsequent immutable review revisions may classify it as unverified, hints/answers or confirmed no hints. Reasons and review IDs remain private. Stopping execution does not erase previously provided context. Soft deletion does not erase source history.

New execution can be enabled only with an explicit exact-context no-hints review. Correcting this context cannot clear an independent real full-review exposure. Ordinary evidence qualification and the existing calculator still decide whether a scored response is usable as independent evidence. An activity binding or sealed answer alone neither publishes a score nor proves mastery.

## Stopping and progress

Withdrawal, publication replacement or a disabling/hint review stops new starts for the linked recipient, including direct assessment calls. A previously started attempt retains its original timer, response/submit/deadline rules and result authorization. Old operation receipts remain recoverable under current ownership/relations. The separately published quiz continues to use its own frozen assessment rules if only an unrelated plan-basis freshness check changes; stop/replacement explicitly ends new execution. Plan-body and self-reported activity operations keep their existing freshness gate.

Formal progress derives from the actual sealed submission timestamp, including deadline sealing, without appending a student-completion revision. A current student difficulty remains visible as blocked while its formal seal is separately visible. Teacher counts distinguish self-reported activity completion from sealed formal submissions awaiting evaluation. Workload excludes authorized sealed/stopped formal assignments; it remains a refreshable estimate, not an atomic cross-plan snapshot.

## Delivery, privacy and persistence

Seven author operations require TeachingPlanManage, AnalysisManage and AssessmentManage. Actual teacher task/paper content remains behind those permissions and current owner/teaching/student scope. Student formal metadata requires AssessmentParticipate and caller-derived identity; public DTOs exclude answers, reasons, context classification, private revision heads and fingerprints. Existing private/no-store and audit-body exclusions include the new contract and sensitive types.

The teacher Web previews/binds and corrects/stops the exact context with persisted original commands. The student Web opens a real assessment, edits text/selection/blank responses, saves full question responses, recovers receipts and explicitly submits confirmed server answers. Account/tenant/recipient-scoped tab storage retains unsaved drafts and exact pending commands. A recovered old save re-reads the current server head instead of replacing it with the old receipt. The page preserves linked image identifiers but does not yet upload/edit images. Client display times do not replace server deadline enforcement.

Migration 20261017135022_AddEducationTeachingFormalAssessments adds three tables, three static alternate keys on existing identity scopes and ten Restrict paired foreign keys, with recipient/activity uniqueness and review-state checks. It rewrites no old rows/columns and backfills no binding, provision or completion. Domain/application checks remain responsible for semantic scope, timing and novelty.

Logged-in browser and WeChat/device flows, real teaching calibration, material-version/attachment access, submitted corrections, pause/resume scheduling, final intervention decisions, AI planning and full application concurrency/capacity acceptance remain outside this slice.
