const { test } = require('node:test'), assert = require('node:assert/strict'), crypto = require('node:crypto'), fs = require('node:fs'), path = require('node:path'), vm = require('node:vm'), { createRequire } = require('node:module');
const growth = require('../utils/growth');
const guid = () => crypto.randomUUID(), school = guid(), contextId = guid(), snapshot = guid(), account = guid();
const at = new Date(Date.now() - 1000).toISOString();
const click = (id) => ({ currentTarget: { dataset: { id } } }), pageClick = (kind, direction = 1) => ({ currentTarget: { dataset: { kind, direction } } });
function fixture() {
  let state = { authenticated: true, generation: 1, context: { accountUserId: account, tenantId: null, workspaces: [{ schoolId: school, role: 2, classes: [] }], grantedPermissions: ['KnowPulse.Education.AssessmentParticipate'] } };
  const listeners = new Set(), requests = [], links = [], copies = [];
  const context = { id: contextId, schoolName: '本人学校', className: '七年级', termName: '当前学期', teacherName: '任课教师', subjectName: '数学', textbookTitle: '已开放测评教材', maximumWindowDays: 730, privateOtherStudent: 'PRIVATE-OTHER-STUDENT' };
  const tasks = Array.from({ length: 23 }, (_, i) => ({ taskId: guid(), recipientId: guid(), resultReleaseId: guid(), title: '已开放测评' + i, effectiveAtUtc: at, isAbsent: false, totalScore: i === 0 ? 0 : 8, maxScore: 10, visibility: i === 0 ? 1 : 2, comparisonLimit: i === 0 ? 1 : 2 | 4 | 8 | 16 }));
  const points = Array.from({ length: 23 }, (_, i) => ({ id: guid(), revisionNumber: 1, code: 'KP' + i, name: '知识点' + i, state: 1, obtainedScore: i === 0 ? 0 : 8, maxScore: 10, ratio: i === 0 ? 0 : .8,
    independentQuestionCount: 1, assessmentCount: 1, latestEvidenceAtUtc: at, unverifiedQuestionCount: 0, practiceQuestionCount: 0, invalidQuestionCount: 0, missingIndependentQuestionCount: 0, missingAssessmentCount: 0, recentEvidenceMissing: false }));
  const report = { snapshotId: snapshot, context, calculatedAtUtc: at, calculationVersion: 'actual-server-algorithm', ruleMissing: true, rule: null, ruleDecisionRevisionNumber: null, points, assessments: tasks, answers: 'PRIVATE-REFERENCE', teacherNotes: 'PRIVATE-OBSERVATION' };
  const rows = Array.from({ length: 23 }, () => ({ taskId: tasks[1].taskId, recipientId: tasks[1].recipientId, paperQuestionId: guid(), resultReleaseId: tasks[1].resultReleaseId, effectiveAtUtc: at, obtainedScore: 4, maxScore: 5, exclusionReason: 0, teacherFeedback: 'PRIVATE-FEEDBACK' }));
  const f = { report, tasks, points, rows, contexts: [context], requests, links, copies, restricted: true, state: () => state };
  function change(value) { state = { ...state, ...value }; listeners.forEach((fn) => fn(state)); }
  const session = {
    getState: () => state,
    async refreshWorkspace() { const c = state.context; change({ context: null }); change({ context: structuredClone(c) }); return state; },
    async request(route, method = 'GET', body) {
      requests.push({ route, method, body }); assert.equal(method, 'GET'); if (f.hook) { const r = f.hook(route); if (r !== undefined) return r; }
      const u = new URL(route, 'https://api.invalid');
      if (u.pathname.includes('/my-contexts-for-school/')) return { schoolId: f.replySchool || school, items: structuredClone(f.contexts) };
      if (u.pathname.endsWith('/my-growth-for-school')) return { schoolId: f.replySchool || school, value: { ...structuredClone(report), windowStartsAtUtc: u.searchParams.get('WindowStartsAtUtc'), windowEndsAtUtc: u.searchParams.get('WindowEndsAtUtc') } };
      if (u.pathname.endsWith('/my-point-evidence-for-school')) { const skip = Number(u.searchParams.get('SkipCount')); return { schoolId: f.replySchool || school, evidence: { items: structuredClone(rows.slice(skip, skip + 20)), totalCount: rows.length, hasRestrictedEvidence: f.restricted } }; }
      throw new Error('unexpected route ' + route);
    }, portalUrl: (p) => 'https://portal.invalid' + p,
  };
  const app = { session, subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); } }, platform = { navigateTo: (o) => links.push(o.url), reLaunch() {}, setClipboardData: (o) => copies.push(o.data), setStorageSync() { throw Error('no business cache'); } };
  function pageFor(name = 'growth', query = {}) {
    const file = path.join(__dirname, '../pages', name, 'index.js'); let def;
    new vm.Script(fs.readFileSync(file, 'utf8'), { filename: file }).runInNewContext({ require: createRequire(file), Page: (p) => def = p, getApp: () => app, wx: platform });
    const page = { data: structuredClone(def.data), setData(v) { assert.ok(Buffer.byteLength(JSON.stringify(v)) < 1024 * 1024); Object.assign(this.data, structuredClone(v)); } };
    for (const [k, value] of Object.entries(def)) if (typeof value === 'function') page[k] = value.bind(page);
    const requested = growth.window(new Date(Date.now() - 86400000).toISOString().slice(0, 10), new Date().toISOString().slice(0, 10), 730);
    page.onLoad({ schoolId: school, id: contextId, snapshotId: snapshot, pointId: points[0].id, ...(name === 'growth-evidence' ? { from: requested.startsAtUtc, to: requested.endsAtUtc } : {}), ...query }); return page;
  }
  return Object.assign(f, { session, app, platform, change, pageFor });
}
test('student growth requires both current selected school role and assessment permission', () => {
  const f = fixture(); assert.ok(growth.allowed(f.state(), school)); assert.ok(!growth.allowed(f.state(), guid()));
  f.change({ context: { ...f.state().context, workspaces: [{ role: 1, schoolId: school }] } }); assert.ok(!growth.allowed(f.state(), school));
  f.change({ context: { ...f.state().context, workspaces: [{ role: 2, schoolId: school }], grantedPermissions: [] } }); assert.ok(!growth.allowed(f.state(), school));
});
test('school context list is at most twenty visible safe labels and metadata-only reads', async () => {
  const f = fixture(); f.contexts = Array.from({ length: 23 }, (_, i) => ({ ...f.contexts[0], id: i ? guid() : contextId })); const p = f.pageFor('growth-contexts'); await p.onShow(); assert.equal(p.data.rows.length, 20); assert.equal(p.data.total, 23); assert.ok(!JSON.stringify(p.data).includes('PRIVATE-'));
  await p.paginate(pageClick('', 1)); assert.equal(p.data.rows.length, 3); assert.equal(p.data.offset, 20); assert.equal(f.requests.length, 2);
});
test('a teacher role or wrong school cannot load student growth through a native entry', async () => {
  const f = fixture(); f.change({ context: { ...f.state().context, workspaces: [{ role: 1, schoolId: school }] } }); const p = f.pageFor(); await p.onShow(); assert.equal(p.data.head, null); assert.equal(f.requests.length, 0);
});
test('UTC date validation includes a historical end day and clamps today to the exact original request time', () => {
  const now = Date.parse('2026-10-18T08:45:23Z'); const w = growth.window('2026-10-17', '2026-10-18', 2, now); assert.equal(w.startsAtUtc, '2026-10-17T00:00:00.000Z'); assert.equal(w.endsAtUtc, '2026-10-18T08:45:23.000Z');
  assert.equal(growth.window('2026-10-16', '2026-10-17', 2, now).endsAtUtc, '2026-10-18T00:00:00.000Z');
  for (const [a, b, n] of [['2026-02-30', '2026-03-01', 30], ['2026-10-18', '2026-10-19', 3], ['2026-10-17', '2026-10-18', 1], ['2026-10-18', '2026-10-17', 2]]) assert.throws(() => growth.window(a, b, n, now), { code: 'GROWTH_WINDOW_INVALID' });
});
test('overview distinguishes true zero, absence and unassessed evidence without hidden teacher data', async () => {
  const f = fixture(); Object.assign(f.tasks[2], { isAbsent: true, totalScore: null, maxScore: null, comparisonLimit: 32 }); Object.assign(f.points[2], { obtainedScore: 0, maxScore: 0, ratio: null, state: 0, independentQuestionCount: 0, assessmentCount: 0 });
  const p = f.pageFor(); await p.onShow(); assert.equal(p.data.head.context.subject, '数学'); assert.ok(p.data.head.from.endsWith(' UTC')); assert.equal(p.data.assessments[0].percent, 0); assert.equal(p.data.assessments[2].percent, null); assert.equal(p.data.points[2].obtained, '—'); assert.equal(p.data.points[0].obtained, '0'); assert.match(p.data.head.warning, /未配置/); assert.ok(!JSON.stringify(p.data).includes('PRIVATE-'));
});
test('comparison limits retain all server reasons and never invent an improvement or client mastery threshold', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); const label = p.data.assessments[1].comparison; for (const token of ['覆盖', '满分', '难度', '评分']) assert.ok(label.includes(token)); assert.equal(p.data.points[1].label, '证据不足'); assert.ok(!JSON.stringify(p.data).includes('提升了'));
});
test('point and assessment paging recaptures the same exact window and snapshot rather than mixing generations', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); const request = f.requests.find((r) => r.route.includes('my-growth-for-school?')).route; await p.paginate(pageClick('point')); assert.equal(p.data.points.length, 3); assert.equal(f.requests.filter((r) => r.route.includes('my-growth-for-school?')).at(-1).route, request);
  f.report.snapshotId = guid(); await p.paginate(pageClick('assessment')); assert.equal(p.data.head, null); assert.equal(p.data.points.length, 0); assert.match(p.data.error, /快照/);
});
test('editing dates clears old private results immediately and invalid dates send no growth request', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); const before = f.requests.filter((r) => r.route.includes('my-growth-for-school?')).length;
  p.change({ currentTarget: { dataset: { field: 'from' } }, detail: { value: '2026-02-30' } }); assert.equal(p.data.head, null); await p.view(); assert.equal(f.requests.filter((r) => r.route.includes('my-growth-for-school?')).length, before); assert.match(p.data.error, /UTC/);
});
test('evidence links pin the actual context, point, snapshot and exact original time window', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); p.basis(click(f.points[0].id)); const u = new URL(f.links.at(-1), 'https://native.invalid'); assert.equal(u.searchParams.get('snapshotId'), snapshot); assert.equal(u.searchParams.get('pointId'), f.points[0].id); assert.ok(u.searchParams.get('to').endsWith('Z'));
});
test('review-only basis uses twenty safe rows and never requests answers or source-body endpoints', async () => {
  const f = fixture(), p = f.pageFor('growth-evidence'); await p.onShow(); assert.equal(p.data.rows.length, 20); assert.equal(p.data.total, 23); assert.equal(p.data.restricted, true); assert.ok(!JSON.stringify(p.data).includes('PRIVATE-'));
  assert.ok(f.requests.every((r) => r.route.startsWith('/api/app/education-student-growth/'))); await p.paginate(pageClick('', 1)); assert.equal(p.data.rows.length, 3); assert.equal(p.data.offset, 20);
});
test('a new overview snapshot prevents old basis requests and removes all former evidence labels', async () => {
  const f = fixture(), p = f.pageFor('growth-evidence'); await p.onShow(); const before = f.requests.filter((r) => r.route.includes('my-point-evidence-for-school')).length; f.report.snapshotId = guid(); await p.load(); assert.equal(p.data.head, null); assert.equal(p.data.rows.length, 0); assert.equal(f.requests.filter((r) => r.route.includes('my-point-evidence-for-school')).length, before); assert.match(p.data.error, /变化/);
});
test('a mismatched or score-only evidence row is never rendered as a readable question', async () => {
  const f = fixture(), p = f.pageFor('growth-evidence'); f.rows[0].taskId = f.tasks[0].taskId; await p.onShow(); assert.equal(p.data.rows.length, 0); assert.match(p.data.error, /变化/);
});
test('explicit result navigation pins current release and only review basis carries a question id', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); p.result(click(f.tasks[0].taskId)); let u = new URL(f.links.at(-1), 'https://native.invalid'); assert.equal(u.searchParams.get('releaseId'), f.tasks[0].resultReleaseId); assert.equal(u.searchParams.get('questionId'), null);
  const basis = f.pageFor('growth-evidence'); await basis.onShow(); basis.result(click(basis.data.rows[0].id)); u = new URL(f.links.at(-1), 'https://native.invalid'); assert.equal(u.searchParams.get('releaseId'), f.tasks[1].resultReleaseId); assert.equal(u.searchParams.get('questionId'), f.rows[0].paperQuestionId);
});
test('source changed or capacity errors leave no stale overview or automatic retry', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); f.hook = (r) => { if (r.includes('my-growth-for-school?')) throw growth.error('GROWTH_UPDATING'); }; const before = f.requests.length; await p.refresh(); assert.equal(p.data.head, null); assert.equal(f.requests.length - before, 2); assert.match(p.data.error, /手动/);
});
test('hide, permission loss and account change clear private reports and stop former links', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); p.onHide(); assert.equal(p.data.head, null); assert.equal(p.report, null); const before = f.links.length; p.result(click(f.tasks[0].taskId)); assert.equal(f.links.length, before);
  await p.onShow(); f.change({ generation: 2, context: { ...f.state().context, accountUserId: guid(), grantedPermissions: [] } }); assert.equal(p.data.head, null); p.basis(click(f.points[0].id)); assert.equal(f.links.length, before);
});
test('late overview response after leaving cannot restore a previous account view', async () => {
  const f = fixture(), p = f.pageFor(); let complete;
  f.hook = (r) => r.includes('my-growth-for-school?') ? new Promise((resolve) => complete = resolve) : undefined; const work = p.onShow(); await new Promise(setImmediate); p.onHide(); complete({ schoolId: school, value: { ...f.report, windowStartsAtUtc: p.requested.startsAtUtc, windowEndsAtUtc: p.requested.endsAtUtc } }); await work; assert.equal(p.data.head, null); assert.equal(p.report, null);
});
test('wrong-school projections fail closed before any visible growth fields are populated', async () => { const f = fixture(), p = f.pageFor(); f.replySchool = guid(); await p.onShow(); assert.equal(p.data.head, null); assert.ok(p.data.error); });
test('read-only growth Web handoff contains no student, snapshot, score or credentials', async () => { const f = fixture(), p = f.pageFor(); await p.onShow(); p.web(); assert.equal(f.copies.at(-1), 'https://portal.invalid/education/growth'); });
