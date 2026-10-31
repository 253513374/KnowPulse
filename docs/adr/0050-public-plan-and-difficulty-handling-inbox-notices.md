# ADR 0050: Public plan changes and difficulty handling in the student inbox

Status: Accepted, 2026-10-18. Extends ADRs 0023, 0025, 0045 and 0049 for FR34. This completes the next in-app source; real WeChat delivery remains unconfigured.

## Source facts and privacy

Store an EducationTeachingPlanNotice in the same relational unit of work as a successful publication, withdrawal, actual public execution-state change or confirmed difficulty handling. Draft/confirmation and private Continue decisions create no notice. Replaying a source operation returns its existing receipt before notification insertion; filtered unique indexes additionally prevent duplicates for each source revision. Do not manufacture notices for historical events during migration.

Each notice has its own random public identifier, school/student/teacher/plan scope, the exact published content version, kind and first message-read timestamp. Typed internal source references pair content, decision or activity/follow-up with the same plan and recipient. They are database facts, not public teacher-event identifiers. Do not copy private reasons, evidence, teacher notes, student difficulty text, answers, scores or mastery into notices. The public handling projection includes only the original public step identifier. No sender, arbitrary URL or business-content cache is added.

## Reading and navigation

Add TeachingPlan as the third shared inbox source, governed by TeachingPlanView and a current student workspace in the selected school. The source service checks the explicit permission and active account again. List filters tenant, student, school and effective school/term/class/membership/enrollment before count and paging, with final source-scope checks. There is no lifetime event cap or fabricated cross-source count.

Message reading uses the existing School/account write barrier, reloads the plan and notice after it, and preserves the first read time. It never calls body provision, activity provision, question exposure, formal-attempt creation or student activity commands. A message read is not participation, completion, grading or independent mastery evidence.

CanOpen requires the same publication still current and its original source available under current permissions. A handling notice also requires that its private follow-up still handles the current student reply. Withdrawal and obsolete sources can remain readable as generic messages while CanOpen is false. They never redirect to a replacement publication or private event. Execution messages say only that the public arrangement changed; the original authorized page shows its current state for that exact content version.

Web and native inboxes build fixed links using school, plan, published version and, for handling, public step. The existing native teaching-plan page already accepts these public identifiers. The Web activity page now uses its school-scoped source operation when linked from the inbox and explicitly locates the step; a missing step does not silently select another activity. Legacy source operations and routes remain compatible. Existing private/no-store response filtering includes the plan service.

## Persistence and verification

Migration 20261018115039_AddEducationTeachingPlanNotices creates 16 columns, six paired source foreign keys, two checks, three filtered source uniqueness indexes and the needed plan/activity alternate keys. ReadAtUtc is nullable without an inferred default. Existing plan, decision, activity, submission and historical read records remain unchanged; apply the migration before the new service version. Mixed old/new Host rolling deployment has not been accepted.

Validate with the solution build, two focused route/field/permission contracts, six source/inbox service cases and 36 native Page-script checks, plus model consistency and an isolated PostgreSQL DbMigrator/seed/schema run. These are local source and migration checks, not full browser, real WeChat/SDK/WXML/device, old school-data upgrade or production multi-Host acceptance. No dependency or real external sender is added.
