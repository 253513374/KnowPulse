# ADR 0049: Student inbox and independent feedback message reading

Status: Accepted, 2026-10-18. Extends ADRs 0018 and 0048 for FR20/FR28/FR34. This first shared entry uses the two existing notice sources; plan/activity notices and WeChat delivery remain subsequent work.

Follow-up: [ADR 0050](0050-public-plan-and-difficulty-handling-inbox-notices.md) adds the third TeachingPlan source and optional public StepId. The original two sources and feedback body-read separation remain compatible; the deferred plan statements below describe this ADR's initial slice. WeChat delivery remains unconfigured.

## Shared entry, source authority

Add a student inbox in Web and the one-AppID student workspace. Sources are assessment and feedback, each with its own current-school page and count. Do not pretend a source page is a global unread count or merge independently paged sources into an incorrect timeline. The shared application service projects only fixed message kinds, school, opaque message/source identifiers, availability/read times, capability and NotConfigured channel status. It accepts no message URL, body, author or recipient identity from a client.

The server discovers sources through the existing current workspace projection: a real effective student class in the selected school plus each source's functional permission. List/read check AssessmentParticipate or FeedbackView explicitly, then delegate to the original source's ownership, tenant, school/term/enrollment and version rules. Teacher or administrator identity is not an inbox grant. No new universal permission bypasses a source. Responses are private/no-store; no message business cache is introduced.

Assessment messages reuse ADR 0048's three kinds, source availability/cancellation, original read facts and exact result links. Feedback notices reuse their existing immutable publication/withdrawal identifiers and current visibility policy. The original notices and body-read APIs remain compatible. The shared envelope contains no task/feedback text, grade, weakness, answer, observation, internal reason or private grading identifiers.

## Message reading is not feedback body reading

Feedback previously exposed IsRead from EducationFeedbackRead, which means the student actually opened a version. Keep that fact unchanged. Add a separate first MessageReadAtUtc on the notice. Reading generic metadata checks the exact school and current student relationship under the existing School/account barrier, reloads the source after that barrier, and sets the timestamp only once. It does not open the feedback body, record an exposure, start an exam or write a body-read row. Old and withdrawn messages can be marked message-read while CanOpen is false; no later source version is silently substituted.

An available source link fixes its public version. Feedback gains OpenMyForSchool with school validation before body/reading/exposure; legacy OpenMy remains. Native feedback viewing uses the scoped operation. Web feedback links carry school/feedback/version and revalidate through that operation. Read/render callbacks are guarded by account/tenant, route and request generation; changing source or hiding the native page clears prior projections. Source content is provided only by the original authorized page after explicit navigation.

## Persistence and validation boundary

Migration 20261018112218_AddFeedbackMessageReading adds one nullable UTC column and a read-time check. It does not infer message reads from historical body reads or overwrite existing read facts. No dependency, provider or new permission is added. Existing source limits remain limits; the shared inbox does not claim to remove them or deliver every remaining FR34 source.

Use focused contract, scope/permission/read-separation service and native Page-script checks, plus required builds/model/migration validation. Real WeChat, SDK/device rendering, complete browser/weak-network behavior, real school data upgrades and production multi-Host load remain outside this local slice. Plan publication/withdrawal, execution-state and difficulty-handling notifications still need their own controlled public records before joining this entry.
