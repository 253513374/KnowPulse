const test = require('node:test'), assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), vm = require('node:vm'), crypto = require('node:crypto');
const { createRequire } = require('node:module'); const model = require('../utils/teacher-plan'); const teacherCommand = require('../utils/teacher-plan-command');
const ids = Array.from({ length: 12 }, (_, i) => [8, 4, 4, 4, 12].map((n) => (i + 1).toString(16).repeat(n)).join('-'));
const [school, assignment, owner, planId, versionId, stepA, stepB, activityA, activityB, studentEvent, other, v6] = ids;
const date = '2026-10-18T00:00:00Z';
const context = () => ({ accountUserId: owner, tenantId: null, grantedPermissions: ['TeachingPlanManage', 'AnalysisManage'].map((p) => 'KnowPulse.Education.' + p), workspaces: [{ role: 1, schoolId: school, classes: [{ relationId: assignment, className: '一班', subjectName: '数学', academicTermName: '本期' }] }] });
function dto() { return { schoolId: school, teachingAssignmentId: assignment, planId, publishedRevisionId: versionId, isCurrentPublication: true, executionState: 0, studentName: '学生甲', className: '一班', subjectName: '数学',
  controlReason: 'PRIVATE-CONTROL', content: { title: '巩固安排', studentGoal: '核对思路', teacherActions: '逐步讲解', successCriteria: '教师复核新题', startsAtUtc: date, endsAtUtc: '2026-10-20T00:00:00Z', reassessmentAtUtc: '2026-10-20T00:00:00Z', targets: [{ code: 'P1', name: '方程', revisionNumber: 1 }], materials: [],
    steps: [{ id: stepA, kind: 2, title: '例题', instructions: '核对例题依据', estimatedMinutes: 10, dueAtUtc: '2026-10-19T00:00:00Z' }, { id: stepB, kind: 4, title: '新题复测', instructions: '独立测评', estimatedMinutes: 10, dueAtUtc: '2026-10-20T00:00:00Z' }] },
  versions: [{ id: versionId, revisionNumber: 3, recordedAtUtc: date }], activities: [activityA, activityB].map((id, i) => ({ id, stepId: [stepA, stepB][i], stepOrder: i + 1, state: i ? 4 : 2, materialsAvailable: true, canStart: true, canReadMaterials: true,
    revisionNumber: i ? 0 : 1, currentRevisionId: i ? null : studentEvent, concurrencyStamp: 'current-stamp', studentRevisionNumber: i ? 0 : 1, currentStudentRevisionId: i ? null : studentEvent, firstProvidedAtUtc: date,
    currentStudent: i ? null : { id: studentEvent, activityId: activityA, studentRevisionNumber: 1, action: 2, difficulty: 1, sharedText: '需要协调时间', recordedAtUtc: date },
    formalAssessment: i ? { canOpen: true, taskId: other, recipientId: other, attemptId: other, isStopped: false, title: '新题测评', sealedAtUtc: date, referenceAnswer: 'PRIVATE-ANSWER' } : null })) }; }
function fixture(name = 'teacher-plan') {
  let state = { authenticated: true, generation: 1, context: context() }; const storage = new Map(), receipts = new Map(), listeners = new Set();
  const f = { dto: dto(), requests: [], links: [], failAfter: false, failBefore: false, waitDialog: false };
  const platform = { getStorageSync: (k) => storage.get(k), setStorageSync: (k, v) => storage.set(k, structuredClone(v)), removeStorageSync: (k) => storage.delete(k),
    getRandomValues(o) { const b = crypto.randomBytes(16); o.success({ randomValues: b.buffer.slice(b.byteOffset, b.byteOffset + 16) }); },
    showModal(o) { if (f.waitDialog) f.dialog = o; else o.success({ confirm: true }); }, enableAlertBeforeUnload() {}, disableAlertBeforeUnload() {}, navigateTo(o) { f.links.push(o.url); }, reLaunch() {}, setClipboardData(o) { f.copy = o.data; } };
  const session = { getState: () => state, async refreshWorkspace() { return state; }, portalUrl: (p) => 'https://portal.invalid' + p,
    async request(url, method = 'GET', body) {
      f.requests.push({ url, method, body: structuredClone(body) });
      if (url.includes('/teacher-plans?')) { const q = new URL('https://api.invalid' + url).searchParams; return { totalCount: 1, items: q.get('SkipCount') === '0' ? [{ id: planId, schoolId: school, teachingAssignmentId: assignment, publishedRevisionId: versionId, publishedRevisionNumber: 3, isCurrentPublication: true, executionState: 0, studentName: '学生甲', subjectName: '数学', className: '一班', content: 'PRIVATE-CONTENT' }] : [] }; }
      if (url.includes('/activity-progress-for-school?')) { if (f.readWait) return f.readWait; return structuredClone(f.dto); }
      if (url.includes('/activity-follow-up-operation-for-school?')) return structuredClone(receipts.get(new URL('https://api.invalid' + url).searchParams.get('operationId')) || null);
      if (url.includes('/save-activity-follow-up-for-school?')) {
        if (f.failBefore) { f.failBefore = false; throw Object.assign(new Error('lost'), { code: 'NETWORK_UNCONFIRMED' }); }
        const row = f.dto.activities[0]; if (body.expectedRevisionId !== row.currentRevisionId) throw Object.assign(new Error('changed'), { status: 409 });
        const receipt = { id: body.operationId, activityId: row.id, revisionNumber: row.revisionNumber + 1, action: 3, replyToStudentRevisionId: body.expectedStudentRevisionId, internalTeacherNote: body.internalTeacherNote, recordedAtUtc: date };
        row.currentRevisionId = receipt.id; row.revisionNumber++; row.currentFollowUp = receipt; row.concurrencyStamp = 'new-stamp'; receipts.set(receipt.id, receipt);
        if (f.failAfter) { f.failAfter = false; throw Object.assign(new Error('lost'), { code: 'NETWORK_UNCONFIRMED' }); } return structuredClone(receipt);
      }
      throw new Error('Unexpected route ' + url);
    } };
  const app = { configuration: { apiBaseUrl: 'https://api.invalid', appKey: 'main', clientId: 'client' }, session, subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); } };
  const change = (s) => { state = s; listeners.forEach((fn) => fn(s)); };
  function page(name) { const file = path.resolve(__dirname, '../pages', name, 'index.js'); let def;
    new vm.Script(fs.readFileSync(file, 'utf8'), { filename: file }).runInNewContext({ require: createRequire(file), Page: (d) => def = d, wx: platform, getApp: () => app });
    const p = { data: structuredClone(def.data), setData(v) { assert.ok(Buffer.byteLength(JSON.stringify(v)) < 1024 * 1024); Object.assign(this.data, v); } };
    for (const [k, v] of Object.entries(def)) if (typeof v === 'function') p[k] = v.bind(p); p.onLoad({ schoolId: school, assignmentId: assignment, id: planId, versionId }); return p;
  }
  return Object.assign(f, { page: page(name), newPage: page, session, storage, receipts, platform, change });
}
const field = (field, value) => ({ currentTarget: { dataset: { field } }, detail: { value } });
const note = (p) => { p.change(field('note', 'PRIVATE-FOLLOW-UP 核对后约定沟通')); p.change(field('confirm', ['yes'])); };

test('malformed local tracking stays blocked and can be explicitly closed without sending a guessed operation', async () => {
  const f = fixture(); f.platform.getStorageSync = () => ({ bad: true, rawText: 'PRIVATE-CORRUPT' });
  const writer = teacherCommand(f.platform, f.session, { apiBaseUrl: 'https://api.invalid', appKey: 'main', clientId: 'client' }, planId, school, assignment, () => true);
  assert.equal(writer.state().pending.invalid, true); assert.ok(!JSON.stringify(writer.state()).includes('PRIVATE-CORRUPT')); await assert.rejects(writer.recover(), { code: 'LOCAL_STATE_INVALID' }); assert.equal(f.requests.length, 0); writer.abandon(); assert.equal(writer.state().pending, null);
});

test('teacher plan list uses selected school/assignment, metadata paging and an exact version link', async () => {
  const entry = fixture('workspace'); await entry.page.teacherPlans({ currentTarget: { dataset: { schoolId: school } } }); assert.ok(entry.links[0].includes('/teacher-plans/index?schoolId=' + school));
  const c = context(); c.workspaces.push({ role: 2, schoolId: school, classes: [] }); entry.change({ authenticated: true, generation: 1, context: c }); await entry.page.selectRole({ currentTarget: { dataset: { role: 2 } } });
  assert.equal(entry.page.data.workspaces[0].teacherPlanAllowed, false); await entry.page.teacherPlans({ currentTarget: { dataset: { schoolId: school } } }); assert.equal(entry.links.length, 1);
  const f = fixture('teacher-plans'); await f.page.onShow(); assert.equal(f.page.data.total, 1); assert.ok(f.requests[0].url.includes('TeachingAssignmentId=' + assignment)); assert.ok(!JSON.stringify(f.page.data).includes('PRIVATE-CONTENT'));
  f.page.open({ currentTarget: { dataset: { id: planId } } }); assert.ok(f.links[0].endsWith('&versionId=' + versionId)); await f.page.paginate({ currentTarget: { dataset: { direction: 1 } } }); assert.equal(f.page.data.offset, 0); f.page.onHide(); assert.equal(f.page.data.items.length, 0);
});
test('teacher view separates blocked and sealed facts and rejects mixed scope without copying hidden source fields', () => {
  const r = model.view(dto(), school, assignment, planId, versionId); assert.equal(r.header.blocked, 1); assert.equal(r.header.sealed, 1); assert.equal(r.activities[0].canHandle, true); assert.ok(!JSON.stringify(r).includes('PRIVATE-ANSWER')); assert.ok(!JSON.stringify(r).includes('PRIVATE-CONTROL'));
  assert.throws(() => model.view(dto(), other, assignment, planId, versionId), { code: 'INVALID_RESPONSE' });
  const old = dto(); old.isCurrentPublication = false; old.versions.push({ id: v6, revisionNumber: 6, recordedAtUtc: date }); assert.equal(model.view(old, school, assignment, planId, versionId).activities[0].canHandle, false);
  const limited = dto(); limited.activities[1].formalAssessment = { requiresAssessmentPermission: true }; assert.equal(model.view(limited, school, assignment, planId, versionId).header.sealed, '未知');
  const blocked = dto(); blocked.activities[0].currentStudent.action = 1; blocked.activities[0].materialsAvailable = false; const facts = model.view(blocked, school, assignment, planId, versionId); assert.equal(facts.header.complete, 1); assert.equal(facts.header.blocked, 1);
});
test('lost teacher follow-up response persists only identifiers and cold recovery reads the original receipt without another write', async () => {
  const f = fixture(); await f.page.onShow(); note(f.page); f.failAfter = true; await f.page.save(); assert.ok(f.page.data.pending); assert.ok(!JSON.stringify([...f.storage.values()]).includes('PRIVATE-FOLLOW-UP'));
  f.page.onHide(); assert.equal(f.page.data.header, null); assert.equal(f.page.data.note, ''); const cold = f.newPage('teacher-plan'); await cold.onShow(); assert.equal(cold.data.canRetry, false); await cold.recover();
  assert.equal(cold.data.pending, null); assert.equal(cold.data.activity.follow.note, 'PRIVATE-FOLLOW-UP 核对后约定沟通'); assert.equal(f.requests.filter((r) => r.method === 'POST').length, 1); cold.onUnload();
});
test('same-page retry first queries and then sends the unchanged original private request', async () => {
  const f = fixture(); await f.page.onShow(); note(f.page); f.failBefore = true; await f.page.save(); assert.equal(f.page.data.canRetry, true); await f.page.retry();
  const writes = f.requests.filter((r) => r.method === 'POST'); assert.equal(writes.length, 2); assert.deepEqual(writes[0].body, writes[1].body); assert.ok(f.requests.some((r) => r.url.includes('operation-for-school?'))); assert.equal(f.page.data.pending, null);
});
test('changing account during the confirmation clears private data and cannot write the previous teacher request', async () => {
  const f = fixture(); await f.page.onShow(); note(f.page); f.waitDialog = true; const saving = f.page.save(); const c = context(); c.accountUserId = other; f.change({ authenticated: true, generation: 2, context: c }); f.dialog.success({ confirm: true }); await saving;
  assert.equal(f.requests.filter((r) => r.method === 'POST').length, 0); assert.equal(f.page.data.activity, null); assert.equal(f.page.data.note, ''); assert.equal(f.storage.size, 0);
});
test('a late teacher progress response after hiding cannot restore the private page', async () => {
  const f = fixture(); let resolve; f.readWait = new Promise((r) => resolve = r); const loading = f.page.onShow(); await new Promise((r) => setImmediate(r)); f.page.onHide(); resolve(dto()); await loading; assert.equal(f.page.data.header, null); assert.equal(f.page.data.activity, null);
});
