# ADR 0043: Teacher trends from current report snapshots and frozen paper metadata

Status: Accepted, 2026-10-18

## Context

FR30/31 needs class and individual trends across assessments, with access to the scoring evidence behind a selected assessment. ADR 0021 already captures confirmed marks and all assignment states in immutable, owner-scoped teacher reports. ADR 0022/0042 governs a different audience: students see only their own currently released results. A teacher trend must preserve that distinction and must not infer progress from incomparable papers or turn missing scores into zero.

## Decision

Add one AnalysisManage GET, `education-learning-report/trend`, requiring the report, expected snapshot, school and teaching assignment. An optional student selects one person already present in that snapshot. Existing report ownership, current account/school/teaching checks, current-source validation and the snapshot expectation run before reading observations. This endpoint neither creates jobs nor publishes grades. The independently protected Web page and native teacher page consume the same service.

Keep source schema, report fingerprints, persisted snapshot JSON and student-growth calculations unchanged. Read confirmed score/state observations from the exact snapshot's captured job. Comparison metadata comes only from the captured paper ID and version: immutable frozen question definition IDs, type, score, multiple-choice scoring mode, nominal difficulty and curriculum-point mappings. Do not read question bodies, answers, mutable question-bank definitions or rubrics for comparison. No new table, permission or migration is required. Frozen metadata remains subject to the existing report bounds of 500 tasks and 50,000 rows; no partial comparison is silently returned on capacity failure.

For class observations, average only complete confirmed paper scores. Count a genuine zero; exclude incomplete, absent, unstarted, in-progress, awaiting-seal and outside-window records from the mean and disclose those states separately. Show original assigned and currently included counts. A student row shows that student's complete confirmed score, not the class mean. A missing score remains nullable with its known paper maximum.

Order class observations by the latest effective submission among complete scores, using the deadline when none exists. Order individual observations by the available effective submission, otherwise deadline; break ties by assessment start and task ID. Compare only with the adjacent preceding row, including missing observations; never skip a missing row to manufacture comparability. The first row has no preceding comparison.

Comparison limits independently disclose different point coverage, maximum, nominal difficulty composition, frozen scoring basis, missing complete scores, different included/scored student sets, and missing frozen metadata. Exact equality of frozen definition/type/score/mode sequences is deliberately conservative: different questions may be useful observations but do not establish an equivalent scoring basis. The transient comparison keys and cohort identifiers never enter response DTOs or persistence. Even a comparable raw-score increase does not establish an intervention effect or add independent learning evidence.

The existing point-evidence GET gains an optional trailing task ID, preserving callers that omit it. Check that the selected student was assigned that captured task, then filter before totals and pagination. Native student/basis pages carry and verify that task; Web trends offer points from the same report window while labeling the evidence section as assessment-specific. Whole-window mastery signals do not become a conclusion about the selected assessment. Original grading review still requires AssessmentManage independently.

Web and native views display at most 20 assessment/evidence rows per page. Bars always have textual scores and counts; missing values have no zero-length score bar. Native pages reuse read-scope clearing and fixed snapshot checks, persist no report business cache, and distinguish source change/read concurrency from permission loss. The Web page discards responses from superseded query parameters. A pending same-source computation may coexist with the earlier still-current snapshot; a source or snapshot change rejects the old link instead of replacing its generation silently.

## Consequences and acceptance limits

Teacher analysis can include confirmed grades that students cannot yet read. Reading it creates no student answer exposure and never substitutes for explicit uniform result publication. Ending the real teaching relation blocks old online report/trend links while preserving stored facts. Existing private/no-store filtering and controlled parameter auditing also cover the new GET/DTOs.

Domain, contract, EF and native Page-script tests verify comparison/missing-state rules, paging, exact-task basis and current authority. Local HTTPS/PostgreSQL acceptance uses real report workers, signed tokens and publication protocols; only the external WeChat proof is synthetic. Actual WeChat configuration, SDK/WXML rendering, devices, complete browser/weak-network flows, school rule calibration and production capacity/concurrency still need separate acceptance. This decision completes a teacher-trend slice, not the full education PRD.
