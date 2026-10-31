# ADR 0051: Account-bound school invitations and explicit membership confirmation

Status: Accepted, 2026-10-18. Implements the teacher/student invitation part of FR01/FR35 under ADRs 0008 and 0037. Account recovery, assisted unbinding, relationship corrections and school shutdown remain separate work and policy decisions.

Relation correction follow-up: [ADR 0052](0052-teacher-relation-correction-requests-and-atomic-school-resolution.md) implements teacher requests and school-confirmed atomic relation changes. The initial-slice exclusions above are not a current claim that this part is still absent. Account recovery, assisted unbinding and school shutdown remain subsequent work.

## Existing accounts, explicit school authority

A current effective school manager with SchoolManage may invite a verified exact active account in the same tenant as Teacher or Student. Reuse the existing school account search; names, class codes and client role selection are not identity proof. Require explicit verification acknowledgement. The invitation targets an immutable account/school/role and a planned membership period. It is an in-app record, not an emailed link, bearer token, new account or WeChat proof. No external message is sent. Manager membership still uses the existing authorized member/import workflow.

The recipient has an authenticated entry independent of existing school membership, in Web and the shared-AppID native workspace. They review the school name and stable code, role and dates, explicitly acknowledge identity and accept, or decline with a fixed identity/school-role/do-not-join reason. A name alone need not identify a school. They can inspect expired and processed records. No arbitrary account can claim an invitation by knowing its identifier. Revocation reasons and manager operation metadata are absent from the recipient DTO.

## Membership and terminal facts

SchoolInvitation stores issue scope, UTC planned period/deadline, original request fingerprint, terminal actor/operation/fingerprint/time and optional resulting member. Local default confirmation validity is three days, with a seven-day maximum. Formal school identity/notice and support policies remain D10/D06 work; this implementation does not claim that an administrator checkbox completes real-world identity verification.

Pending expiration is calculated from deadline and planned end, without a background worker or rewriting accepted history. Response rechecks the active account, original issuer's current effective manager membership and active account, school and source version under School/account barriers. Original functional permission is checked at issuance; an otherwise eligible invitation remains issued approval until revoked/expired, and acceptance grants no ABP functional permission. Current school managers can revoke a pending invitation with a private reason. A completed invitation cannot be revoked to remove its member.

Accept in the same transaction as member creation. Actual new membership starts at the later of planned start and confirmation time. Reuse an existing nondeleted same-school/account/role member that already covers the entire requested period; otherwise use the original membership overlap checks. Do not extend an ended relation, invent teaching/enrollment or grant scores/analysis access. Personal library ownership/initialization stays on the same account. A replayed accepted invitation returns its historical member reference and cannot restore a subsequently ended member.

Create uses its operation identifier as the invitation key and a scope/actor/request fingerprint. Response and revocation preserve their own operation fingerprints; same-request replay returns its original fact, conflicting reuse is rejected. A filtered unique completion-operation index defends deduplication. School/account barriers and aggregate concurrency checks contend with current relationship/account writers; production multi-Host races are not established by SQLite tests.

## Clients and persistence

Web school management links to issue/search/history/revocation; Web and native workspaces link to personal invitations even with no school role. Both require an explicit confirmation before writing and preserve source identifiers for uncertain replies. Persist only account/environment-scoped operation metadata. Original payloads remain in page memory; leaving or account changes clears them. Recovery reads the exact invitation and matches the original completion nonce and result kind. It does not relabel another operation or current status as the original receipt, automatically resend, switch account or recreate membership. Clearing a local reference does not cancel a server operation. Inspect processed invitations to locate a cold-start native operation whose response succeeded.

ABP routes are verified from the current conventional route builder. Single invitation detail/response use path invitationId. Manager list/create use path schoolId; manager detail/revoke with two identifiers use query parameters. Existing IEducationSchoolAppService proxy registration and private/no-store filter apply to all seven new operations. Native known business rejections retain the verified login; actual authorization denial still clears the context.

Migration 20261018121744_AddSchoolInvitations adds one 21-column table, five restrictive foreign keys including the same-school/account/role member pair, four validity/completion checks and one filtered unique completion nonce. It adds the member principal key but neither backfills invitations nor rewrites existing members, libraries or teaching facts. Apply before enabling the new service version; rolling mixed-version deployment is not accepted here.

Verification: solution build; one seven-route/permission/recipient-field contract; two integrated invitation lifecycle/scope/source cases; 53 focused native command/page/session checks; model consistency and isolated PostgreSQL migration/seed/schema verification. Full browser, real WeChat/SDK/device, real school-data upgrades and production concurrent load remain unverified. No new dependency is introduced.
