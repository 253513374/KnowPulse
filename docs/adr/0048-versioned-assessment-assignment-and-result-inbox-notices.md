# ADR 0048: Version assessment assignment and result inbox notices

Status: Accepted, 2026-10-18. Extends ADRs 0014, 0017 and 0047 for FR14/FR17/FR34. Other notification sources and configured WeChat delivery remain separate work.

## Decision and facts

Reuse the existing assessment reminder record for three bounded kinds: manual reminder, assignment notice and result-release notice. A new successful assessment publication saves one generic assignment notice per frozen recipient in the same relational transaction. A non-Hidden result release saves one result notice per closure recipient, including an absent recipient without inventing a score. It fixes the exact task publication and result-release identifier. Existing publication/release operation recovery returns the original fact before creating any further notices; filtered unique indexes enforce one assignment notice per recipient and one result notice per release/recipient.

These are durable database facts, not external sends. The publication transaction contains only the source facts and their generic in-app records; no WeChat, model or other external delivery is invoked inside it. A database persistence failure still fails that transaction. An eventual external-delivery failure must not roll back an already committed source. Current WeChat state remains explicitly NotConfigured rather than claiming success or failure without an attempt.

Keep source publication time, notice recorded time, earliest in-app availability and first read separate. Assignment/manual notices are immediately available under the current reader's authority. Result notices become available no earlier than both the release and its score-opening time; FullReview content still uses its own later gate. No message carries a score, answer, weakness, private reason/observation or unreleased body. Notices do not start an exam, mark learning complete, provide a plan/question or add mastery evidence.

## Supersession and reading

The first successor's release time decides whether an older scheduled result notice ever became available. If the source was replaced/hidden on or before the planned availability, suppress that pending notice permanently, including direct read attempts after its nominal due time. A previously available notice remains an authorized historical metadata fact with its first read time. Its CanOpen is false when its result release is no longer current or available; explicitly marking the message read does not substitute another result.

The new GetMyNotices operation pages all three kinds after current school, exact frozen student relationships, time and canceled-schedule filters in SQL. The original GetMyReminders route remains manual-only for old clients. The contract now has seven operations. Both keep the existing AssessmentParticipate permission, tenant/account checks and private/no-store response policy. Manual command recovery accepts only Manual records; automatic notices do not consume the one-hour manual-reminder interval.

Opening an available assignment enters the existing exam preflight. Opening an available result fixes school, recipient, task publication and result release in the existing source API. That API rechecks before exposing grades/questions or recording exposure. Native and Web clients reject late cross-context responses; Web result reads/images also guard account, tenant, route and selected-result generation. Missing/replaced/hidden source links require explicit reselection, with no silent version switch. No generic arbitrary URL is stored or accepted as a message destination.

## Persistence and limits

Migration 20261018104238_AddAutomaticAssessmentNotices adds Kind, ResultReleaseId and AvailableAtUtc. Existing records retain Manual classification, original identifiers, fingerprints and read times; availability is backfilled by copying their own recorded time. There is no automatic backfill of old task/result publications. New checks pair kinds with source references and bound read/availability times. A paired result-release/task/publication key and filtered uniqueness complement the original recipient/owner keys. New code requires the migrated schema; mixed old/new Host rolling deployment is not claimed.

Focused source/receipt/visibility tests, client Page-script tests, builds and model/migration checks validate this slice without a full regression run, following the user's request. An isolated PostgreSQL run proves migration/seed and actual DDL; it does not prove production application concurrency, a real old-school-data upgrade or browser/device behavior. Full FR34 still includes other notification sources and actual WeChat configuration, subscription, delivery and recovery.
