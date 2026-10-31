const test = require('node:test');
const assert = require('node:assert/strict');
const workload = require('../utils/workload');
const fs = require('node:fs'); const path = require('node:path'); const vm = require('node:vm'); const { createRequire } = require('node:module');
const ids = Array.from({ length: 5 }, (_, i) => [8, 4, 4, 4, 12].map((n) => String(i + 1).repeat(n)).join('-'));
const [school, id, source, publication, otherSchool] = ids;
const requestWindow = () => workload.window('2026-10-18', '2026-11-01');
function dto(n = 4) {
  const items = Array.from({ length: n }, (_, i) => ({ id, sourceId: source, publicationId: publication, kind: i % 4, state: i % 4, subjectName: '数学', className: '一班',
    startsAtUtc: '2026-10-17T00:00:00Z', dueAtUtc: '2026-10-19T01:00:00Z', estimatedMinutes: 10 }));
  return { schoolId: school, ...requestWindow(), calculatedAtUtc: '2026-10-18T00:01:00Z', unestimatedPlanCount: 1, omittedKinds: [], items,
    days: [{ dueDateUtc: '2026-10-19T00:00:00Z', subjectName: '数学', pendingCount: n, knownMinutes: items.filter((r) => r.state < 2).length * 10,
      blockedCount: items.filter((r) => r.state === 2).length, overdueCount: items.filter((r) => r.state === 3).length }] };
}
function fixture(role = 2) {
  let state = { authenticated: true, generation: 1, context: { accountUserId: source, tenantId: null, workspaces: [{ schoolId: school, role }],
    grantedPermissions: ['LearningParticipate', 'TeachingPlanView', 'AssessmentParticipate'].map((p) => 'KnowPulse.Education.' + p) } };
  const listeners = new Set(); const requests = []; const f = { response: dto(), hook: null, links: [], copies: [], transfers: [] };
  const change = (s) => { state = s; listeners.forEach((fn) => fn(s)); };
  const app = { subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); }, session: { getState: () => state,
    async refreshWorkspace() { const context = structuredClone(state.context); change({ ...state, context: null }); await Promise.resolve(); change({ ...state, context }); return state; },
    async request(url, method) { requests.push({ url, method }); if (f.hook) return f.hook(url); return structuredClone(f.response); },
    portalUrl: (route) => 'https://portal.example.invalid' + route,
  } };
  const platform = { navigateTo: (o) => f.links.push(o.url), setClipboardData(o) { f.copies.push(o.data); o.success(); }, reLaunch() {} };
  const file = path.resolve(__dirname, '../pages/workload/index.js'); let def;
  new vm.Script(fs.readFileSync(file, 'utf8'), { filename: file }).runInNewContext({ require: createRequire(file), Page: (d) => def = d, getApp: () => app, wx: platform });
  const page = { data: structuredClone(def.data), setData(v) { f.transfers.push(Buffer.byteLength(JSON.stringify(v))); Object.assign(this.data, v); } };
  for (const [k, v] of Object.entries(def)) if (typeof v === 'function') page[k] = v.bind(page);
  page.onLoad({ schoolId: school }); page.setData({ from: '2026-10-18', to: '2026-11-01' });
  return Object.assign(f, { page, app, change, requests });
}
const pick = (key) => ({ currentTarget: { dataset: { key } } });

test('workload uses real half-open UTC days and refuses impossible or overlong ranges', () => {
  assert.equal(workload.window('2026-03-01', '2026-04-01').endsAtUtc, '2026-04-01T00:00:00Z');
  for (const [from, to] of [['2026-02-30', '2026-03-01'], ['0000-01-01', '0000-01-02'], ['2026-03-01', '2026-04-02'], ['2026-03-01', '2026-03-01'], ['2026-3-1', '2026-04-01']])
    assert.throws(() => workload.window(from, to), { code: 'INVALID_WORKLOAD_WINDOW' });
});

test('projection groups UTC days, keeps blocked and overdue out of known minutes, and strips source bodies', () => {
  const raw = dto(); raw.items[0].privateNote = 'PRIVATE'; raw.content = 'BODY'; const result = workload.project(raw, school, requestWindow());
  assert.equal(result.knownMinutes, 20); assert.equal(result.days[0].blocked, 1); assert.equal(result.days[0].overdue, 1); assert.equal(result.unestimated, 1);
  assert.ok(result.partial); assert.ok(!JSON.stringify(result).includes('PRIVATE')); assert.ok(!JSON.stringify(result).includes('BODY'));
});

test('wrong school, wrong window, hidden-kind contradictions and inconsistent daily totals fail the whole projection', () => {
  for (const change of [(r) => r.schoolId = otherSchool, (r) => r.endsAtUtc = '2026-11-02T00:00:00Z', (r) => r.days[0].knownMinutes = 100,
    (r) => r.days[0].blockedCount = 0, (r) => r.days[0].dueDateUtc = 'invalid', (r) => r.omittedKinds = [3], (r) => r.items[0].dueAtUtc = '2026-11-01T00:00:00Z', (r) => r.items.push(r.items[0])]) {
    const raw = dto(); change(raw); assert.throws(() => workload.project(raw, school, requestWindow()), { code: 'INVALID_RESPONSE' });
  }
});

test('omitted kinds and unknown plans remain explicit when no known work can be listed', () => {
  const raw = dto(0); raw.days = []; raw.omittedKinds = [2, 3]; const value = workload.project(raw, school, requestWindow());
  assert.equal(value.knownMinutes, 0); assert.equal(value.items.length, 0); assert.equal(value.unestimated, 1); assert.ok(value.omitted.includes('正式测评')); assert.ok(value.partial);
  raw.omittedKinds.push(0); assert.throws(() => workload.project(raw, school, requestWindow()), { code: 'INVALID_RESPONSE' });
});

test('workload page sends only current-school read requests and transfers only one item page', async () => {
  const f = fixture(); f.response = dto(0); f.response.days = [];
  for (let i = 0; i < 3000; i++) f.response.items.push({ ...dto().items[1], id: (i + 10).toString(16).padStart(8, '0') + '-1111-1111-1111-111111111111' });
  f.response.days = [{ dueDateUtc: '2026-10-19T00:00:00Z', subjectName: '数学', pendingCount: 3000, knownMinutes: 30000, blockedCount: 0, overdueCount: 0 }];
  await f.page.onShow(); assert.equal(f.page.data.total, 3000); assert.equal(f.page.data.items.length, 20); assert.equal(f.page.data.knownMinutes, 30000);
  assert.ok(f.requests.every((r) => !r.method && r.url.includes('schoolId=' + school))); assert.ok(Math.max(...f.transfers) < 100000);
  f.page.paginate({ currentTarget: { dataset: { direction: 1 } } }); assert.equal(f.page.data.offset, 20); assert.equal(f.requests.length, 1);
  f.page.onHide(); assert.equal(f.page.result, null); assert.equal(f.page.data.knownMinutes, null); assert.equal(f.page.data.items.length, 0);
});

test('changing the date range clears old estimates before a new calculation and invalid ranges send no business read', async () => {
  const f = fixture(); await f.page.onShow(); assert.equal(f.page.data.knownMinutes, 20);
  f.page.change({ currentTarget: { dataset: { field: 'from' } }, detail: { value: '2026-02-30' } }); assert.equal(f.page.data.knownMinutes, null); assert.equal(f.page.data.items.length, 0);
  await f.page.load(); assert.equal(f.requests.length, 1); assert.ok(f.page.data.error);
});

test('four source links use fixed recipient or exact plan version, and opening never sends a start or submission', async () => {
  const f = fixture(); await f.page.onShow(); for (const row of f.page.data.items) f.page.open(pick(row.key));
  assert.equal(f.links.length, 4); assert.ok(f.links.some((p) => p.startsWith('/pages/study-task/') && p.endsWith('&id=' + id)));
  assert.ok(f.links.some((p) => p.startsWith('/pages/assessment-answer/'))); assert.equal(f.copies.length, 0);
  assert.ok(f.links.includes(`/pages/teaching-plan/index?schoolId=${school}&id=${source}&versionId=${publication}&stepId=${id}`));
  assert.ok(f.links.includes(`/pages/correction/index?schoolId=${school}&id=${id}`)); assert.equal(f.requests.length, 1);
});

test('a role or permission loss hides the schedule and cannot navigate an old source', async () => {
  const f = fixture(); await f.page.onShow(); const key = f.page.data.items[0].key; const state = f.app.session.getState();
  f.change({ ...state, context: { ...state.context, grantedPermissions: [] } }); f.page.open(pick(key)); assert.equal(f.links.length + f.copies.length, 0); assert.equal(f.page.result, null);
  const teacher = fixture(1); await teacher.page.onShow(); assert.equal(teacher.requests.length, 0); assert.equal(teacher.page.data.knownMinutes, null);
});

test('a response delivered after hiding or account change cannot restore the former schedule', async () => {
  const f = fixture(); let finish; f.hook = () => new Promise((resolve) => finish = resolve); const running = f.page.onShow(); while (!finish) await new Promise(setImmediate);
  f.page.onHide(); finish(dto()); await running; assert.equal(f.page.result, null); assert.equal(f.page.data.knownMinutes, null);
  const g = fixture(); g.hook = () => new Promise((resolve) => finish = resolve); const changing = g.page.onShow(); await new Promise(setImmediate);
  g.change({ authenticated: false, generation: 2, context: null }); finish(dto()); await changing; assert.equal(g.page.result, null);
});

test('missing optional permissions stop source navigation without changing the read-only estimate', async () => {
  const f = fixture(); await f.page.onShow(); const row = f.page.data.items[3];
  f.app.session.getState().context.grantedPermissions = ['KnowPulse.Education.LearningParticipate']; f.page.open(pick(row.key));
  assert.equal(f.links.length, 0); assert.ok(f.page.data.error); assert.equal(f.requests.length, 1);
});
