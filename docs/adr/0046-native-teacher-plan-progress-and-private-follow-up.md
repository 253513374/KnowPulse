# ADR 0046: Native teacher plan progress and private difficulty follow-up

Status: Accepted, 2026-10-18. Extends ADRs 0025 and 0045 under FR18/FR33 and the one-AppID decision D02.

## Decision

The teacher workspace adds a school/teaching-assignment plan list and exact-publication progress page. The list pages the author's last published plan metadata in SQL, including withdrawn plans under current authority, without selecting revision body JSON or imposing a lifetime candidate quota. Opening a card fixes its publication; a version picker explicitly selects another historical publication. A historical or withdrawn publication is read-only. Formal editing, confirmation, publication and execution decisions continue in the existing Web workflow.

Four activity-contract operations add the metadata list and school/assignment-scoped progress, follow-up and operation recovery. Both TeachingPlanManage and AnalysisManage, the current teacher account/tenant, ownership and effective exact teaching/student relationships are required. School/assignment mismatch is rejected before content, mutation or receipt delivery. The existing Web operations and follow-up request fingerprint remain unchanged; both clients use the same immutable records and recovery receipts. Progress reads do not manufacture student provision, completion or new submissions.

## Evidence and private data

The mobile page projects only plan teaching content, selected activity/current student submission, actual formal sealing metadata and the owner's matching follow-up. It omits raw answers, hidden grades, private execution reasons and unused source fields. Current self-reported completion is counted from the student event independently of material blocking; formal sealing is a separate fact. Without AssessmentManage, formal details/counts remain explicitly unknown. None of these counts estimates mastery or intervention effectiveness.

A teacher explicitly confirms a private handling note against the exact current student difficulty and activity head. The existing concurrency/receipt rules prevent overwriting a newer difficulty or follow-up. The student receives only its matching handling time; the note does not become a student comment, alter the student's sequence, complete an activity or change a grade. Sending a public comment still requires the separate confirmed feedback workflow. Notes retain existing audit exclusions and are not sent to a model.

## Mobile recovery and limits

Pending storage contains only school, assignment, plan, activity and operation identifiers, scoped by API, app, client, tenant and account. Private text and the exact retry request stay in page memory and are cleared on hiding/leaving or authority change. Recovery queries the original receipt before an explicit same-request retry; cold recovery cannot recreate lost text. A malformed local reference blocks writes and guessed recovery until the user explicitly closes tracking. Closing tracking does not undo a possible server commit. Confirmed receipts reload current progress rather than restoring an old head.

Read results, modal callbacks and writes are guarded by current identity/context and visible page scope. No business-content cache is added. Explicit Web links carry only the plan identifier and require normal Web login and fresh authorization. No entity, migration, permission, provider or dependency is added.

Validation for this slice deliberately uses builds, focused contract/service tests, related Node tests and static page checks. It does not claim real WeChat authentication/domain configuration, SDK/WXML rendering, devices, complete browser/weak-network flows or production concurrency acceptance. The full education PRD remains in progress.
