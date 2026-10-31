const test = require('node:test'), assert = require('node:assert/strict'), crypto = require('node:crypto');
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm'), { createRequire } = require('node:module');
const model = require('../utils/analysis');
const id = (name) => crypto.createHash('md5').update('analysis-' + name).digest('hex').replace(/(.{8})(.{4})(.{4})(.{4})(.{12})/, '$1-$2-$3-$4-$5');
const [school, assignment, owner, reportId, snapshot, book, studentId, otherStudent, pointId, taskId, grade, gradeVersion] = ['school', 'assignment', 'owner', 'report', 'snapshot', 'book', 'student', 'other', 'point', 'task', 'grade', 'grade-version'].map(id);
const from = '2026-10-17T00:00:00Z', to = '2026-10-18T00:00:00Z';
const counts = (scored = 1) => ({ assignedCount: 1, scoredCount: scored, pendingGradingCount: 0, absentCount: 0, notStartedCount: 1 - scored, inProgressCount: 0, awaitingSealCount: 0, outsideWindowCount: 0 });
const person = (user = studentId) => ({ studentUserId: user, studentName: user === studentId ? '学生甲' : '学生乙', accountName: user === studentId ? 'student-a' : 'student-b', schoolMembershipId: id('membership'), privateExtra: 'PRIVATE-IDENTITY' });
const point = (index = 0) => ({ knowledgePointId: id('point-root-' + index), knowledgePointRevisionId: index ? id('point-' + index) : pointId, code: 'P' + index, name: '有理数' });
const estimate = (index = 0) => ({ point: point(index), state: 1, obtainedScore: 8, maxScore: 10, ratio: 0.8, independentQuestionCount: 1, assessmentCount: 1, latestEvidenceAtUtc: from, unverifiedQuestionCount: 0, practiceQuestionCount: 0, invalidQuestionCount: 0, ruleMissing: true, missingIndependentQuestionCount: 0, missingAssessmentCount: 0, recentEvidenceMissing: false });
function fixture() {
  const context = () => ({ accountUserId: owner, tenantId: null, grantedPermissions: ['KnowPulse.Education.AnalysisManage', 'KnowPulse.Education.AssessmentManage'], workspaces: [{ schoolId: school, role: 1, classes: [{ relationId: assignment, className: '七年一班', subjectName: '数学', academicTermName: '上学期' }] }] });
  let state = { generation: 1, authenticated: true, context: context() }; const listeners = new Set(), requests = [], links = [], copies = [];
  const dto = { id: reportId, schoolId: school, teachingAssignmentId: assignment, textbookVersionId: book, generation: 1, windowStartsAtUtc: from, windowEndsAtUtc: to,
    sourceIsCurrent: true, scopeNeedsUpdate: false, currentSnapshotId: snapshot, snapshotGeneration: 1, calculatedAtUtc: to, currentJob: { id: snapshot, reportId, generation: 1, status: 2, completedStudentCount: 2, studentCount: 2, failureStage: 0, capacityExceeded: false, heartbeatAtUtc: to, requestFingerprint: 'PRIVATE-HASH' },
    coverage: { ...counts(), assignedCount: 2, notStartedCount: 1, studentCount: 2, scoredStudentCount: 1 },
    summary: { students: [], points: [{ point: point(), obtainedScore: 8, maxScore: 10, ratio: 0.8, unassessedCount: 1, insufficientEvidenceCount: 1, needsConsolidationCount: 0, currentTargetMetCount: 0 }],
      assessments: [{ taskId, taskRevisionId: id('task-version'), title: '有理数测评', assignedCount: 2, visibleAssignedCount: 2, startsAtUtc: from, endsAtUtc: to }],
      ruleMissing: true, schoolClassName: '七年一班', subjectName: '数学', textbookTitle: '数学上册', calculationVersion: 'score-evidence/v1', sourceFingerprint: 'PRIVATE-SOURCE-FP' }, rule: null };
  const people = [{ student: person(), ...counts(), assessedPointCount: 1, targetPointCount: 1, needsConsolidationCount: 0, insufficientEvidenceCount: 1 }, { student: person(otherStudent), ...counts(0), assessedPointCount: 0, targetPointCount: 1, needsConsolidationCount: 0, insufficientEvidenceCount: 0 }];
  const student = { student: person(), analysis: { targetPointCount: 1, assessedPointCount: 1, windowStartsAtUtc: from, windowEndsAtUtc: to, calculatedAtUtc: to, calculationVersion: 'score-evidence/v1', points: [estimate()], evidence: [{ rawBody: 'PRIVATE-ANSWER', qualificationReason: 'PRIVATE-REASON' }] },
    assessments: [{ taskId, recipientId: id('recipient'), state: 4, questionCount: 1, confirmedQuestionCount: 1, totalScore: 8, maxScore: 10, effectiveAtUtc: from, responseText: 'PRIVATE-ANSWER' }] };
  const evidence = [{ questionGradeId: grade, gradeRevisionId: gradeVersion, taskId, submissionId: id('submission'), paperQuestionId: id('paper-question'), qualificationRevisionId: id('qualification'), effectiveAtUtc: from, maxScore: 10, obtainedScore: 8, exclusionReason: 0, rawAnswer: 'PRIVATE-ANSWER', qualificationReason: 'PRIVATE-REASON' }];
  const f = { dto, people, student, evidence, requests, links, copies, context };
  const change = (s) => { state = s; for (const fn of listeners) fn(s); };
  const session = { getState: () => state, async refreshWorkspace() { const original = state.context || context(); change({ ...state, context: null }); await Promise.resolve(); change({ ...state, context: structuredClone(original) }); return state; },
    async request(route, method = 'GET', body) {
      requests.push({ route, method, body }); if (f.hook) { const result = f.hook(route); if (result !== undefined) return result; }
      const u = new URL('https://api.invalid' + route);
      if (u.pathname === '/api/app/education-learning-report') return { totalCount: 1, items: [structuredClone(dto)] };
      if (u.pathname.endsWith('/for-school')) return structuredClone(dto);
      if (u.pathname.endsWith('/students')) return { totalCount: people.length, items: structuredClone(people.slice(Number(u.searchParams.get('SkipCount')), Number(u.searchParams.get('SkipCount')) + 20)) };
      if (u.pathname.endsWith('/student')) return structuredClone(student);
      if (u.pathname.endsWith('/point-evidence')) { const rows = u.searchParams.has('taskId') ? evidence.filter((r) => r.taskId === u.searchParams.get('taskId')) : evidence; return { totalCount: rows.length, items: structuredClone(rows.slice(Number(u.searchParams.get('SkipCount')), Number(u.searchParams.get('SkipCount')) + 20)) }; }
      throw new Error('unexpected route ' + route);
    }, portalUrl: (p) => 'https://portal.invalid' + p,
  };
  const app = { session, subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); } }, platform = { navigateTo: (o) => links.push(o.url), setClipboardData: (o) => copies.push(o.data), reLaunch() {}, setStorageSync() { throw new Error('business storage must not be used'); } };
  function pageFor(name, query = {}) {
    const file = path.join(__dirname, '../pages', name, 'index.js'); let def;
    new vm.Script(fs.readFileSync(file, 'utf8'), { filename: file }).runInNewContext({ require: createRequire(file), Page: (d) => def = d, wx: platform, getApp: () => app });
    const p = { data: structuredClone(def.data), setData(v) { assert.ok(Buffer.byteLength(JSON.stringify(v)) < 1024 * 1024); Object.assign(this.data, structuredClone(v)); } };
    for (const [k, v] of Object.entries(def)) if (typeof v === 'function') p[k] = v.bind(p);
    p.onLoad({ schoolId: school, assignmentId: assignment, id: reportId, snapshotId: snapshot, studentId, pointId, ...query }); return p;
  }
  return Object.assign(f, { session, app, platform, change, pageFor });
}
const pick = (id) => ({ currentTarget: { dataset: { id } } });
const pageEvent = (kind, direction = 1) => ({ currentTarget: { dataset: { kind, direction } } });

test('teacher report list uses only an effective school assignment and no writes or source bodies', async () => {
  const f = fixture(), p = f.pageFor('reports'); await p.onShow(); assert.equal(p.data.items.length, 1); assert.ok(f.requests[0].route.includes('schoolId=' + school)); assert.ok(f.requests[0].route.includes('teachingAssignmentId=' + assignment)); assert.ok(!JSON.stringify(p.data).includes('PRIVATE-')); p.open(pick(reportId)); assert.equal(f.links[0], `/pages/report/index?schoolId=${school}&assignmentId=${assignment}&id=${reportId}`); assert.ok(f.requests.every((r) => r.method === 'GET'));
});
test('student and manager-style roles cannot use teacher analysis even if the feature permission is present', async () => {
  for (const role of [0, 2, 3]) { const f = fixture(); const s = f.session.getState(); f.change({ ...s, context: { ...s.context, workspaces: [{ schoolId: school, role, classes: [] }] } }); const p = f.pageFor('reports'); await p.onShow(); assert.equal(f.requests.length, 0); assert.equal(p.data.items.length, 0); }
});
test('class coverage comes from the whole snapshot and labels students separately from assessment pairs', async () => {
  const f = fixture(), p = f.pageFor('report'); await p.onShow(); assert.equal(p.data.head.coverage.students, 2); assert.equal(p.data.head.coverage.scoredStudents, 1); assert.equal(p.data.head.coverage.assignedCount, 2); assert.equal(p.data.head.coverage.notStartedCount, 1); assert.equal(p.data.points[0].ratio, '80.00%'); assert.ok(p.data.head.warning.includes('证据不足')); assert.ok(!JSON.stringify(p.data).includes('PRIVATE-')); p.student(pick(studentId)); assert.ok(f.links[0].includes('snapshotId=' + snapshot));
});
test('missing evidence is not zero, while a measured genuine zero remains 0 percent', async () => {
  const f = fixture(); f.student.analysis.points[0] = { ...estimate(), obtainedScore: 0, ratio: 0 }; f.student.analysis.points.push({ ...estimate(1), state: 0, obtainedScore: 0, maxScore: 0, ratio: null, independentQuestionCount: 0, assessmentCount: 0 }); f.student.analysis.targetPointCount = 2;
  const p = f.pageFor('report-student'); await p.onShow(); assert.equal(p.data.points[0].ratio, '0.00%'); assert.equal(p.data.points[0].label, '证据不足'); assert.equal(p.data.points[1].ratio, '暂无有效证据'); assert.equal(p.data.points[1].label, '未测评'); assert.equal(p.data.points[1].obtained, '—'); assert.equal(p.data.points[1].max, '—');
});
test('ungraded and missing assessment states never acquire an invented zero total', async () => {
  for (const state of [0, 1, 2, 3, 5, 6]) { const f = fixture(); f.student.assessments[0] = { ...f.student.assessments[0], state, totalScore: null }; const p = f.pageFor('report-student'); await p.onShow(); assert.equal(p.data.tasks[0].score, '尚无完整确认分数'); assert.notEqual(p.data.tasks[0].label, '已有完整确认评分'); }
});
test('an old snapshot still current in facts is distinct from the queued next generation', async () => {
  const f = fixture(); f.dto.generation = 2; f.dto.currentJob = { ...f.dto.currentJob, id: id('next-job'), generation: 2, status: 0 }; const p = f.pageFor('report'); await p.onShow(); assert.equal(p.data.head.generation, 2); assert.equal(p.data.head.snapshotGeneration, 1); p.student(pick(studentId)); assert.ok(f.links[0].includes('snapshotId=' + snapshot)); assert.ok(!f.links[0].includes(id('next-job')));
});
test('stale metadata cannot smuggle an old summary or rule back into the class view', async () => {
  const f = fixture(), p = f.pageFor('report'); await p.onShow(); f.dto.sourceIsCurrent = false; f.dto.summary.textbookTitle = 'PRIVATE-STALE-BOOK'; await p.refresh(); assert.equal(p.data.head.current, false); assert.equal(p.data.head.coverage, null); assert.equal(p.data.students.length, 0); assert.equal(p.full.points.length, 0); assert.ok(!JSON.stringify(p.data).includes('PRIVATE-STALE')); assert.equal(f.requests.filter((r) => r.route.includes('/students?')).length, 1);
});
test('a replaced student snapshot fails before reading a different generation student row', async () => {
  const f = fixture(); f.dto.currentSnapshotId = id('next-snapshot'); f.dto.snapshotGeneration = 2; const p = f.pageFor('report-student'); await p.onShow(); assert.equal(p.data.person, null); assert.ok(p.data.error.includes('快照')); assert.equal(f.requests.filter((r) => r.route.includes('/student?')).length, 0);
});
test('a source race during a child read clears the former result and offers report refresh', async () => {
  const f = fixture(), p = f.pageFor('report-student'); await p.onShow(); f.hook = (route) => route.includes('/for-school?') ? Promise.resolve({ ...structuredClone(f.dto), sourceIsCurrent: false }) : undefined;
  await p.load(); assert.equal(p.data.person, null); assert.equal(p.full, null); assert.ok(p.data.error.includes('快照'));
});
test('a report-source business refusal does not leave a partial class roster in view', async () => {
  const f = fixture(), p = f.pageFor('report'); await p.onShow(); f.hook = (route) => { if (route.includes('/students?')) throw Object.assign(new Error('changed'), { code: 'REPORT_UNAVAILABLE', status: 403 }); };
  await p.refresh(); assert.equal(p.data.head, null); assert.equal(p.data.students.length, 0); assert.ok(p.data.error.includes('报告来源')); assert.ok(f.session.getState().context);
});
test('student and point responses must match the requested identity and exact selected revision', async () => {
  const f = fixture(); f.student.student = person(otherStudent); const p = f.pageFor('report-student'); await p.onShow(); assert.equal(p.data.person, null);
  const g = fixture(), q = g.pageFor('report-evidence', { pointId: id('not-in-snapshot') }); await q.onShow(); assert.equal(q.data.point, null); assert.equal(g.requests.filter((r) => r.route.includes('/point-evidence?')).length, 0);
});
test('knowledge-point evidence preserves inclusion/exclusion and exact grade linkage without answer bodies', async () => {
  const f = fixture(), p = f.pageFor('report-evidence'); await p.onShow(); assert.equal(p.data.rows[0].reason, '计入独立证据'); assert.equal(p.data.rows[0].obtained, '8'); assert.equal(p.data.rows[0].max, '10'); assert.ok(!JSON.stringify(p.data).includes('PRIVATE-')); p.review(pick(gradeVersion)); assert.equal(f.copies[0], 'https://portal.invalid/education/evidence/' + grade);
  for (let i = 1; i < 8; i++) assert.ok(model.evidence({ ...f.evidence[0], exclusionReason: i }).reason !== '计入独立证据');
});
test('analysis access alone never enables the separate Web grading/evidence-edit privilege', async () => {
  const f = fixture(); const s = f.session.getState(); f.change({ ...s, context: { ...s.context, grantedPermissions: ['KnowPulse.Education.AnalysisManage'] } }); const p = f.pageFor('report-evidence'); await p.onShow(); assert.equal(p.data.rows.length, 1); assert.equal(p.data.canReview, false); p.review(pick(gradeVersion)); assert.equal(f.copies.length, 0);
});
test('class pagination keeps whole-roster totals and rechecks the same snapshot rather than summing the visible page', async () => {
  const f = fixture(); f.people = f.people; f.people.splice(0, 2, ...Array.from({ length: 21 }, (_, i) => ({ ...f.people[0], student: person(id('roster-' + i)) })));
  f.dto.coverage = { ...counts(), assignedCount: 21, scoredCount: 21, notStartedCount: 0, studentCount: 21, scoredStudentCount: 21 }; f.dto.summary.points[0].insufficientEvidenceCount = 21; f.dto.summary.points[0].unassessedCount = 0;
  const p = f.pageFor('report'); await p.onShow(); assert.equal(p.data.students.length, 20); assert.equal(p.data.head.coverage.students, 21); await p.paginate(pageEvent('students')); assert.equal(p.data.students.length, 1); assert.equal(p.data.head.coverage.students, 21); assert.ok(f.requests.filter((r) => r.route.includes('/students?')).every((r) => r.route.includes('snapshotId=' + snapshot)));
});
test('large per-student analysis never serializes its raw evidence set or more than 20 point cards', async () => {
  const f = fixture(); f.student.analysis.points = Array.from({ length: 25 }, (_, i) => estimate(i)); f.student.analysis.targetPointCount = 25; f.student.analysis.evidence = Array.from({ length: 4000 }, () => ({ privateBody: 'PRIVATE-EVIDENCE'.repeat(40) }));
  const p = f.pageFor('report-student'); await p.onShow(); assert.equal(p.data.points.length, 20); assert.ok(!JSON.stringify(p.data).includes('PRIVATE-EVIDENCE')); await p.paginate(pageEvent('points')); assert.equal(p.data.points.length, 5); assert.equal(f.requests.filter((r) => r.route.includes('/student?')).length, 1); assert.equal(f.requests.filter((r) => r.route.includes('/for-school?')).length, 2);
});
test('losing role or assignment clears teacher data and prevents navigation from stale cards', async () => {
  const f = fixture(), p = f.pageFor('report'); await p.onShow(); const s = f.session.getState(); f.change({ ...s, context: { ...s.context, workspaces: [{ schoolId: school, role: 1, classes: [] }] } }); assert.equal(p.full, null); assert.equal(p.data.head, null); p.student(pick(studentId)); assert.equal(f.links.length, 0);
});
test('late report response after hiding or workspace refresh cannot restore a former teacher view', async () => {
  for (const leave of ['hide', 'context']) { const f = fixture(), p = f.pageFor('report-student'); let finish; f.hook = (route) => route.includes('/student?') ? new Promise((r) => finish = r) : undefined; const running = p.onShow(); while (!finish) await new Promise(setImmediate); if (leave === 'hide') p.onHide(); else await f.session.refreshWorkspace(); finish(structuredClone(f.student)); await running; assert.equal(p.data.person, null); assert.equal(p.full, null); }
});
test('malformed scope, class totals and score ratios are rejected without invented partial conclusions', () => {
  const f = fixture(); assert.throws(() => model.head(f.dto, id('other-school'), assignment, reportId), { code: 'INVALID_RESPONSE' }); f.dto.coverage.assignedCount = 99; assert.throws(() => model.head(f.dto, school, assignment, reportId)); f.dto.coverage.assignedCount = 2; f.dto.summary.points[0].ratio = 0.7; assert.throws(() => model.head(f.dto, school, assignment, reportId));
});
test('student calculation must match the report window, timestamp and rule, and missing rules cannot declare success', () => {
  const f = fixture(), report = model.head(f.dto, school, assignment, reportId); const wrong = structuredClone(f.student); wrong.analysis.windowStartsAtUtc = '2026-10-16T00:00:00Z'; assert.throws(() => model.student(wrong, studentId, report));
  const wrongTime = structuredClone(f.student); wrongTime.analysis.calculatedAtUtc = '2026-10-19T00:00:00Z'; assert.throws(() => model.student(wrongTime, studentId, report));
  f.student.analysis.points[0].state = 3; assert.throws(() => model.student(f.student, studentId, report));
});
test('a missing assignment in a detail link is not interpreted as all of the teacher school', async () => {
  for (const name of ['report', 'report-student', 'report-evidence', 'report-trend']) { const f = fixture(), p = f.pageFor(name, { assignmentId: '' }); await p.onShow(); assert.equal(f.requests.length, 0); assert.equal(p.data.head, null); }
});

function trendFixture(f, student = true) {
  const dto = { reportId, snapshotId: snapshot, schoolId: school, teachingAssignmentId: assignment, windowStartsAtUtc: from, windowEndsAtUtc: to, calculatedAtUtc: to, comparisonVersion: 'frozen-paper-observed-score-v1', student: student ? { id: studentId, name: '学生甲', account: 'student-a', privateExtra: 'PRIVATE-IDENTITY' } : null, totalCount: 1,
    items: [{ taskId, title: '有理数测评', effectiveAtUtc: from, endsAtUtc: to, paperSnapshotVersion: 1, questionCount: 1, originalAssignedCount: 2, assignedCount: student ? 1 : 2, scoredCount: 1, pendingGradingCount: 0, absentCount: 0, notStartedCount: student ? 0 : 1, inProgressCount: 0, awaitingSealCount: 0, outsideWindowCount: 0, score: 0, maxScore: 10, ratio: 0, comparedWithTaskId: null, comparisonLimit: student ? 1 : 33, rawAnswer: 'PRIVATE-ANSWER' }], sourceFingerprint: 'PRIVATE-SOURCE' };
  f.hook = (route) => route.startsWith('/api/app/education-learning-report/trend?') ? structuredClone(dto) : undefined;
  return dto;
}

test('teacher trend retains a genuine zero, distinguishes class missing samples and strips private fields', async () => {
  for (const student of [false, true]) { const f = fixture(); trendFixture(f, student); const p = f.pageFor('report-trend', { studentId: student ? studentId : '' }); await p.onShow(); assert.equal(p.data.rows[0].score, '0'); assert.equal(p.data.rows[0].percent, 0); assert.equal(p.data.rows[0].notStartedCount, student ? 0 : 1); assert.ok(!JSON.stringify(p.data).includes('PRIVATE-')); const call = f.requests.find((r) => r.route.includes('/trend?')); assert.ok(call.route.includes('snapshotId=' + snapshot)); assert.equal(call.route.includes('StudentUserId='), student); assert.ok(f.requests.every((r) => r.method === 'GET')); p.onHide(); assert.equal(p.data.head, null); assert.equal(p.data.rows.length, 0); }
});

test('pending marks remain missing with a known maximum and comparison limits explain every difference', async () => {
  const f = fixture(), dto = trendFixture(f); Object.assign(dto.items[0], { score: null, ratio: null, scoredCount: 0, pendingGradingCount: 1, comparisonLimit: 255 }); const p = f.pageFor('report-trend'); await p.onShow(); assert.equal(p.data.rows[0].percent, null); assert.equal(p.data.rows[0].score, '尚无完整确认分数'); assert.equal(p.data.rows[0].max, '10'); for (const word of ['覆盖不同', '满分不同', '难度构成不同', '评分口径不同', '缺失成绩', '学生范围不同', '元数据']) assert.ok(p.data.rows[0].comparison.includes(word));
});

test('trend links pin the parent snapshot and a single student assessment through the point basis', async () => {
  const f = fixture(); trendFixture(f); const report = f.pageFor('report'); await report.onShow(); report.trend(); assert.ok(f.links.pop().includes('snapshotId=' + snapshot));
  const p = f.pageFor('report-trend'); await p.onShow(); p.basis(pick(taskId)); const link = f.links.pop(); assert.ok(link.includes('snapshotId=' + snapshot)); assert.ok(link.includes('studentId=' + studentId)); assert.ok(link.includes('taskId=' + taskId));
  const detail = f.pageFor('report-student', { taskId }); await detail.onShow(); assert.equal(detail.data.taskTitle, '有理数测评'); detail.point(pick(pointId)); assert.ok(f.links.pop().endsWith('&taskId=' + taskId));
  const basis = f.pageFor('report-evidence', { taskId }); await basis.onShow(); assert.equal(basis.data.rows.length, 1); assert.equal(basis.data.taskTitle, '有理数测评'); assert.ok(f.requests.some((r) => r.route.includes('/point-evidence?') && r.route.endsWith('&taskId=' + taskId)));
});

test('trend rejects stale scope, student, malformed counts and inconsistent missing scores', async () => {
  const variants = [(d) => d.snapshotId = id('new-snapshot'), (d) => d.schoolId = id('other-school'), (d) => d.student.id = otherStudent, (d) => d.items[0].assignedCount = 2, (d) => d.items[0].ratio = 0.5, (d) => d.items[0].score = null, (d) => d.items[0].comparisonLimit = 256];
  for (const change of variants) { const f = fixture(), dto = trendFixture(f); change(dto); const p = f.pageFor('report-trend'); await p.onShow(); assert.equal(p.data.head, null); assert.equal(p.data.rows.length, 0); assert.ok(p.data.error); }
});

test('trend verifies the parent current snapshot before requesting scores and discards a late response after hiding', async () => {
  const f = fixture(); trendFixture(f); f.dto.sourceIsCurrent = false; let p = f.pageFor('report-trend'); await p.onShow(); assert.equal(p.data.head, null); assert.ok(!f.requests.some((r) => r.route.includes('/trend?')));
  const g = fixture(), dto = trendFixture(g); let release; g.hook = (r) => r.includes('/trend?') ? new Promise((resolve) => release = () => resolve(dto)) : undefined; p = g.pageFor('report-trend'); const reading = p.onShow(); while (!release) await new Promise(setImmediate); p.onHide(); release(); await reading; assert.equal(p.data.head, null); assert.equal(p.data.rows.length, 0);
});

test('foreign task filtering is refused and a mismatched evidence response is never displayed', async () => {
  for (const page of ['report-student', 'report-evidence']) { const f = fixture(), p = f.pageFor(page, { taskId: id('foreign-task') }); await p.onShow(); assert.equal(p.data.head, null); assert.ok(!f.requests.some((r) => r.route.includes('/point-evidence?'))); }
  const f = fixture(); f.evidence[0].taskId = id('foreign-task'); const p = f.pageFor('report-evidence', { taskId }); f.hook = (r) => r.includes('/point-evidence?') ? { totalCount: 1, items: f.evidence } : undefined; await p.onShow(); assert.equal(p.data.rows.length, 0); assert.equal(p.data.head, null);
});

test('trend pages twenty rows without replacing the selected report snapshot or fetching source answers', async () => {
  const f = fixture(), dto = trendFixture(f, false), template = structuredClone(dto.items[0]); dto.totalCount = 21;
  f.dto.summary.assessments = Array.from({ length: 21 }, (_, i) => ({ ...f.dto.summary.assessments[0], taskId: id('paged-task-' + i) }));
  const rows = f.dto.summary.assessments.map((task, i) => ({ ...template, taskId: task.taskId, comparedWithTaskId: i ? f.dto.summary.assessments[i - 1].taskId : null, comparisonLimit: i ? 32 : 33 }));
  f.hook = (route) => { if (!route.includes('/trend?')) return; const offset = Number(new URL('https://api.invalid' + route).searchParams.get('SkipCount')); return { ...dto, items: rows.slice(offset, offset + 20) }; };
  const p = f.pageFor('report-trend', { studentId: '' }); await p.onShow(); assert.equal(p.data.rows.length, 20); p.paginate(pageEvent('', -1)); await p.paginate(pageEvent('', 1)); assert.equal(p.data.rows.length, 1); assert.equal(p.data.offset, 20); assert.equal(p.data.total, 21); assert.ok(f.requests.filter((r) => r.route.includes('/trend?')).every((r) => r.route.includes('snapshotId=' + snapshot) && r.route.includes('MaxResultCount=20'))); assert.ok(f.requests.every((r) => /\/(for-school|trend)\?/.test(r.route)));
});

test('teacher trend clears observations immediately when teaching authority is lost', async () => {
  const f = fixture(); trendFixture(f); const p = f.pageFor('report-trend'); await p.onShow(); assert.equal(p.data.rows.length, 1);
  const s = f.session.getState(); f.change({ ...s, context: { ...s.context, workspaces: [{ schoolId: school, role: 2, classes: [] }] } }); assert.equal(p.data.head, null); assert.equal(p.data.rows.length, 0); const count = f.requests.length; await p.load(); assert.equal(f.requests.length, count); assert.ok(p.data.error);
});
