# ADR 0044: Anonymous AI teaching-plan drafts with explicit teacher adoption

Status: Accepted, 2026-10-18

## Context

FR32/36 needs editable proposals and recoverable model failures. ADR 0023 already owns private plan versions, teacher confirmation, publication and student access; ADRs 0025–0031 own actual activities, materials and formal assessment links. Generation must reuse those paths and must not publish, create an exam, infer mastery or send a student's raw work to a model.

## Decision

Add four teacher operations to `IEducationTeachingPlanAppService`: generate, read one receipt, list metadata and explicitly retry a failed round. All require TeachingPlanGenerate, TeachingPlanManage and AnalysisManage, the current active account, tenant, ownership and exact current school/teaching/student relationships. List SQL filters current authority before count and paging and projects metadata only, followed by the usual .NET authority check. Detailed responses expose a completed proposal only while its frozen personal basis and schedule remain valid.

The request fixes the report/snapshot/student, 1–20 target point revisions, period, reassessment time and a total 5–10,080 minute budget. Capture the same own-student basis used by manual plans. Recapture it before the provider call, before completing and when adopting. A newer class snapshot alone does not invalidate an unchanged personal basis; a relevant grade, curriculum, eligibility or rule change does. Do not silently substitute a new report.

Only an explicit projection reaches `IRagChatClient`: subject, grade, textbook, selected point names/codes and evidence summary, uncertainty/gaps, period and budget. No identifying basis serialization, account/student GUIDs or names, original questions/answers/marks, other students, feedback, private observations/notes or knowledge bodies are sent. The prompt treats supplied text as data. This is input minimization; it is not proof of genuine model quality or a formal anonymity guarantee for arbitrary curriculum labels.

Use the separate `AiModelUsage.TeachingPlanGeneration` binding (value 4). Existing usages retain their values and no deployment is automatically bound. Resolve through the existing model catalog and chat adapter; cap output at 8,192 tokens. Reject malformed JSON, unknown/duplicate fields, numeric/unknown step kinds, invalid ordering/dates, excess size or budget and missing required teaching steps. Accept 4–20 steps with explanation, worked example or reading, practice and final reassessment; uncertainty requires an initial diagnostic. The server fixes targets/schedule and derives stable step identifiers. Models cannot supply material references, citations or identifiers.

## Jobs, authority and recovery

Persist a private job before returning. A Host worker polls bounded candidates, claims a run with a lease and heartbeats it. No school/account barrier or database transaction remains open across the external call. Build the owner's principal from current database roles at execution start and again when the provider returns; recheck all three functional permissions, account state, current relationships, run ownership, lease and own-student source before accepting the result. Old request claims cannot authorize completion after role removal. Permission/relationship/source failure retains a controlled failure receipt and available usage, without a readable proposal.

One active job per owner is enforced by the database. Default UTC-day quota is 20 requests per tenant/owner, including explicit new retry rounds; default model timeout/lease are 90/120 seconds. Configuration is validated under `TeachingPlanGeneration`. An expired processing lease becomes a failed round, not an automatic paid replay. Only an explicit failed-round retry creates a new `RetryOfJobId`; stable operation IDs recover the original receipt. Reading, refreshing or recovering a receipt does not invoke a model. Bounded optimistic persistence retries do not replay the provider call. Worker-only execution is not remotely exposed.

Store controlled failure stage, prompt/output hashes, prompt version, provider/deployment/model version, configured limits, reported tokens and duration. Do not log raw prompts, provider exceptions or rejected outputs. The validated editable proposal and frozen basis are private business artifacts; generic audit excludes their JSON and request DTOs. Cost remains nullable/unknown because the existing catalog price fields have no explicit billing-unit contract. Never present unknown cost as zero. Provider cancellation or a lost response may leave usage incomplete; an explicit new round may incur another charge.

## Adoption and existing student delivery

Generation creates no `EducationTeachingPlan` or revision. The Web opens the proposal in the existing dirty editor. Only explicit SaveDraft creates a private revision with typed `GenerationJobId`; paired foreign keys bind that provenance to the same plan, student and owner. Teacher edits are preserved, and reopening an already saved proposal opens the current saved teacher version. Confirmation/publication copy provenance and keep all existing recipient/content, text/rights and freshness checks. Editing an already adopted plan uses the ordinary plan permissions; removing AI permission must not disable manual work.

Teachers add authorized material versions with the existing picker after saving. Generated text does not automatically search private/shared libraries or authorize material use. D03 remains unchanged: withdrawal stops subsequent online body/file reads while submitted facts remain. Diagnostic/reassessment instructions require a separate explicit link to a real formal assessment before execution. Published versions use the existing student Web/native contract, FullReview gates, context delivery and activity services; no new native generation UI, automatic progress, grade, exposure or mastery claim is introduced.

The Web uses the same account/tenant-scoped pending-command recovery pattern, storing only generation request metadata in session storage. It guards pending/dirty navigation, offers explicit retry/abandon and an independent manual path. A stale read clears the view; no automatic report switch, model rerun or publication follows.

## Persistence and evidence

Migration `20261018073845_AddEducationTeachingPlanGeneration` adds one job table and a nullable plan-revision origin, with six job paired foreign keys, a paired revision-origin key, one-active-owner partial uniqueness and state/source/usage checks. Existing commands omit the nullable origin when absent, preserving prior serialized request fingerprints. No historical score or plan body is rewritten.

Current full tests pass: 174 application, 383 domain and 686 EF cases (1,243), plus 303 native tests. Isolated PostgreSQL/real HTTPS, the production HTTP model adapter with a synthetic loopback upstream, signed native sessions and actual student Page code verified explicit save/confirm/publish, invalid output/recovery, in-flight grade correction and real role revocation, restored roles and ended-teaching access. Seven invalid PostgreSQL writes were rejected and rolled back. Genuine upstream quality/billing, multi-Host production concurrency, real WeChat/SDK/devices and the complete browser/weak-network workflow remain separate acceptance gates; the browser check here covered login and the empty authorized list after teaching ended. This is an FR32/36 slice, not completion of the full education PRD.
