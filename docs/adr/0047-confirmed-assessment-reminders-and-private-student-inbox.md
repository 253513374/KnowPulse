# ADR 0047: Confirm assessment reminders and keep reading separate from execution

Status: Accepted, 2026-10-18. Extends ADRs 0014, 0037 and 0045 for FR15/FR18/FR34 and AC16. This slice covers explicit formal-assessment reminders; it does not complete all notification sources or WeChat delivery.

Automatic-notice follow-up: [ADR 0048](0048-versioned-assessment-assignment-and-result-inbox-notices.md) adds assignment/result kinds, scheduled availability, supersession and a new all-kind GetMyNotices operation. The original manual list/command semantics remain compatible. Six operations below describe this ADR's original slice; the extended contract has seven.

## Decision

A teacher with AssessmentManage and the current exact teaching/student relationships can page a task's current recipients, refresh a single recipient preview, then explicitly confirm a generic in-app reminder. Preview identifies the student, exact publication, real start/submission/deadline, current plan execution gate and prior reminder. Sending recaptures that source under the existing relational School/account/task barriers. Changed preview conflicts; completed, closed, expired, paused/withdrawn plan or ineffective relationships reject new reminders. Reminders never recreate a task, extend a deadline or alter an attempt, grade or result release.

Each reminder fixes the original operation ID, tenant, author, school, task/publication, recipient/student, request fingerprint and recorded time. The operation ID is its primary key; retrying the same confirmed command recovers that one fact before checking later completion/cooldown changes. A different command cannot reuse it. The initial development throttle is at least sixty minutes for the same task and student, evaluated server-side across reminder records; the preview exposes the next allowed time. This floor is explicit in both clients, not a school load or notification-capacity acceptance claim.

The student inbox pages only their current school's effective frozen assignment relations. Its DTO contains identifiers, recorded/read times and channel state; the displayed message is fixed generic text. It contains no task instructions, score, weakness, answer, private observation or unreleased content. Listing does not mark read. Explicit opening rechecks the student's exact current assignment and records the first read time once, then navigates to the existing preflight task page. Starting remains a separate confirmed action, and the original source/result/material gates remain authoritative.

## Delivery and recovery

The database row is the in-app delivery fact, independent of the original task's publication. The first read time is separate from delivery and from source provision, learning completion or starting an exam. Real WeChat authentication/domain/template configuration is unavailable per D02; this implementation returns NotConfigured and explicitly says no WeChat delivery was attempted. It invokes no WeChat sender, model or external messaging account. Configured delivery, failure/recovery and further notification sources remain subsequent PRD work.

Web session tracking and native persistent tracking retain only account-scoped operation/scope identifiers and the exact preview fingerprint, never student names or task/body text. Both clients recover the original receipt before an explicit same-command retry, including after reloading. A malformed local reference blocks guessed requests until tracking is explicitly closed; closing tracking does not undo a possible server commit. Success reloads current targets rather than restoring the old preview. Identity/route/generation guards prevent late reads or confirmation callbacks from applying to another context. Native hiding clears visible teaching data.

Six service-contract operations reuse the existing assessment service and permissions. Current ownership, tenant and school checks are server-side; no administrator or historical-access bypass is introduced. Responses are private/no-store. Migration 20261018100946_AddEducationAssessmentReminders adds one table, paired recipient and owner/publication foreign keys, a read-time constraint, bounded fingerprint and lookup indexes. It backfills no reminder, read, attempt or delivery claim.

## Validation boundary

Use focused contract/service and native Page-script checks plus the required build/model/migration validation, following the user's request to avoid excessive regression testing. An isolated local PostgreSQL DbMigrator run verifies the actual migration and seed without touching the shared database; it does not verify application-level concurrent sending. Real WeChat, SDK/device rendering, complete browser interactions/weak-network cases, school policy and production multi-Host capacity still require acceptance. Full FR34, other learning-source reminders and the full education PRD remain active.
