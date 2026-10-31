const test = require('node:test'); const assert = require('node:assert/strict'); const crypto = require('node:crypto');
const fs = require('node:fs'); const path = require('node:path'); const vm = require('node:vm'); const { createRequire } = require('node:module');
const model = require('../utils/plan'); const study = require('../utils/study');
const ids = Array.from({ length: 13 }, (_, i) => [8, 4, 4, 4, 12].map((n) => (i + 1).toString(16).repeat(n)).join('-'));
const [school, owner, planId, versionId, stepA, stepB, activityA, activityB, materialId, attachmentId, otherSchool, otherUser, oldReceipt] = ids;
const dto = () => ({ plan: { id: planId, schoolId: school, versionId, versionNumber: 1, teacherName: '任课老师', className: '一班', subjectName: '数学', availability: 1,
  recordedAtUtc: '2026-10-18T00:00:00Z', content: { title: '巩固方案', studentGoal: '核对思路', teacherActions: '讲解', successCriteria: '独立新题', startsAtUtc: '2026-10-18T00:00:00Z', endsAtUtc: '2026-10-20T00:00:00Z', reassessmentAtUtc: '2026-10-20T00:00:00Z', targets: [{ code: 'P1', name: '有理数', revisionNumber: 1 }], steps: [
    { id: stepA, kind: 3, title: '巩固活动', instructions: '活动要求', estimatedMinutes: 10, dueAtUtc: '2026-10-19T00:00:00Z' },
    { id: stepB, kind: 4, title: '复测', instructions: '等待正式测评', estimatedMinutes: 15, dueAtUtc: '2026-10-20T00:00:00Z' }], materials: [{ id: materialId, stepId: stepA, title: '指定讲义' }] } },
  activities: [activityA, activityB].map((id, i) => ({ id, stepId: [stepA, stepB][i], stepOrder: i + 1, state: 0, materialsAvailable: true, firstProvidedAtUtc: null,
    currentStudentRevisionId: null, studentRevisionNumber: 0, currentStudent: null, currentFollowUp: { internalTeacherNote: 'PRIVATE-HANDLING' } })) });
const field = (name, value) => ({ currentTarget: { dataset: { field: name } }, detail: { value } });
const action = (n) => ({ currentTarget: { dataset: { action: n } } });
const pick = (id) => ({ currentTarget: { dataset: { id } } });

test('execution state is separate from publication availability and never copies private review data', () => {
  for (const state of [0, 1, 2, 3]) {
    const value = { ...dto().plan, executionState: state, reason: 'PRIVATE-REVIEW', currentDecisionId: otherUser };
    const card = model.card(value, school); assert.equal(card.executionState, state); assert.ok(card.executionLabel); assert.equal(card.availability, 1);
    assert.ok(!JSON.stringify(card).includes('PRIVATE-REVIEW')); assert.equal(card.currentDecisionId, undefined);
  }
  assert.throws(() => model.card({ ...dto().plan, executionState: '1' }, school), { code: 'INVALID_RESPONSE' });
});

test('paused and terminal plans stop new starts while an already started activity can report completion', () => {
  for (const state of [1, 2, 3]) {
    const value = dto(); value.plan.executionState = state;
    value.activities[0].firstProvidedAtUtc = '2026-10-18T00:01:00Z'; value.activities[0].canStart = false;
    const row = model.view(value, school, planId, versionId).activities[0]; assert.equal(row.canStart, false); assert.equal(row.canReport, true);
    value.activities[0].currentStudentRevisionId = oldReceipt; value.activities[0].studentRevisionNumber = 1;
    value.activities[0].currentStudent = { id: oldReceipt, activityId: activityA, studentRevisionNumber: 1, action: 0 };
    assert.equal(model.view(value, school, planId, versionId).activities[0].canComplete, true);
  }
});

test('a paused plan preserves the original formally started assessment link without inventing a new attempt', () => {
  const value = dto(); value.plan.executionState = 1;
  value.activities[1].formalAssessment = { canOpen: true, recipientId: otherUser, attemptId: oldReceipt, isStopped: true, canStart: true };
  const formal = model.view(value, school, planId, versionId).activities[1].assessment;
  assert.equal(formal.canContinue, true); assert.equal(formal.stopped, true); assert.equal(formal.recipientId, otherUser);
});

test('malformed execution capabilities are rejected instead of enabling a task', () => {
  for (const field of ['canStart', 'canReadMaterials']) { const value = dto(); value.activities[0][field] = 1; assert.throws(() => model.view(value, school, planId, versionId), { code: 'INVALID_RESPONSE' }); }
});

test('the actual native page refuses a paused start or unstarted material read and keeps its history available', async () => {
  const f = fixture(); f.dto.plan.executionState = 1; f.dto.activities[0].canStart = false; f.dto.activities[0].canReadMaterials = false;
  await f.page.onShow(); const before = f.requests.length;
  assert.equal(f.page.data.plan.executionState, 1); assert.equal(f.page.data.activity.canStart, false); assert.equal(f.page.data.activity.canReadMaterials, false);
  await f.page.progress(action(0)); await f.page.material(pick(materialId)); assert.equal(f.requests.length, before); assert.ok(Array.isArray(f.page.data.history));
  f.dto.plan.executionState = 0; f.dto.activities[0].canStart = true; f.dto.activities[0].canReadMaterials = true; await f.page.load();
  assert.equal(f.page.data.activity.canStart, true); f.page.onHide(); assert.equal(f.page.data.plan, null);
});

function fixture(pageName = 'teaching-plan', role = 2) {
  let state = { authenticated: true, generation: 1, context: { accountUserId: owner, tenantId: null, workspaces: [{ schoolId: school, role }],
    grantedPermissions: ['TeachingPlanView', 'LearningParticipate', 'AssessmentParticipate'].map((p) => 'KnowPulse.Education.' + p) } };
  let knownContext = state.context; const listeners = new Set(), storage = new Map(), receipts = new Map(), requests = [];
  const f = { dto: dto(), events: [], hook: null, failAfter: false, failBefore: false, storageBlocked: false, dialogDelay: false, links: [], released: [], opened: [], materialText: '原始讲义', download: null, transfers: [] };
  const change = (s) => { state = s; if (s.context) knownContext = s.context; listeners.forEach((fn) => fn(s)); };
  const platform = { getStorageSync: (k) => storage.get(k), setStorageSync(k, v) { if (f.storageBlocked) throw new Error('full'); storage.set(k, structuredClone(v)); }, removeStorageSync: (k) => storage.delete(k),
    getRandomValues(o) { const b = crypto.randomBytes(16); o.success({ randomValues: b.buffer.slice(b.byteOffset, b.byteOffset + b.length) }); },
    showModal(o) { if (f.dialogDelay) f.dialog = o; else o.success({ confirm: true }); }, enableAlertBeforeUnload() {}, disableAlertBeforeUnload() {}, navigateTo: (o) => f.links.push(o.url), reLaunch() {},
    openDocument: (o) => f.opened.push(o), setClipboardData(o) { f.clipboard = o.data; if (o.success) o.success(); },
  };
  const session = { getState: () => state, async refreshWorkspace() { const context = structuredClone(state.context || knownContext); change({ ...state, context: null }); await Promise.resolve(); change({ ...state, context }); return state; },
    async request(url, method, body) {
      requests.push({ url, method, body: body && structuredClone(body) }); if (f.hook) { const result = f.hook(url, method, body); if (result !== undefined) return result; }
      if (url.includes('/my-list?')) return { totalCount: 1, items: [{ ...structuredClone(f.dto.plan), content: null }] };
      if (url.includes('/my-activities-for-school?')) {
        const query = new URL('https://api.invalid' + url).searchParams;
        if (query.get('schoolId') !== f.dto.plan.schoolId) throw Object.assign(new Error('scope'), { code: 'FORBIDDEN' });
        if (query.get('expectedVersionId') !== f.dto.plan.versionId) throw Object.assign(new Error('changed'), { status: 409 });
        if (f.dto.plan.availability === 1) f.dto.activities.forEach((r) => r.firstProvidedAtUtc = '2026-10-18T00:00:00Z');
        return structuredClone(f.dto);
      }
      if (url.includes('/my-activity-operation?')) return structuredClone(receipts.get(new URL('https://api.invalid' + url).searchParams.get('operationId')) || null);
      if (url.includes('/my-activity-history?')) { const q = new URL('https://api.invalid' + url).searchParams; const events = f.events.filter((e) => e.activityId === q.get('activityId')).slice().reverse(); return { totalCount: events.length, items: structuredClone(events.slice(Number(q.get('SkipCount')), Number(q.get('SkipCount')) + 20)) }; }
      if (url.includes('/save-my-activity/')) {
        if (f.failBefore) { f.failBefore = false; throw Object.assign(new Error('lost'), { code: 'NETWORK_UNCONFIRMED' }); }
        const row = f.dto.activities.find((r) => r.id === url.split('/').pop()); assert.ok(row);
        if (row.currentStudentRevisionId !== body.expectedStudentRevisionId) throw Object.assign(new Error('changed'), { status: 409 });
        const value = { id: body.operationId, activityId: row.id, studentRevisionNumber: row.studentRevisionNumber + 1, action: body.action, difficulty: body.difficulty, sharedText: body.sharedText, reportedMinutes: body.reportedMinutes, recordedAtUtc: '2026-10-18T00:01:00Z' };
        f.events.push(value); receipts.set(value.id, value); row.currentStudent = value; row.currentStudentRevisionId = value.id; row.studentRevisionNumber++; row.state = [1, 3, 2][body.action];
        if (f.failAfter) { f.failAfter = false; throw Object.assign(new Error('lost'), { code: 'NETWORK_UNCONFIRMED' }); } return structuredClone(value);
      }
      if (url.includes('/my-material?')) return { id: materialId, title: '讲义', isAvailable: true, contentText: f.materialText, attachments: [{ id: attachmentId, fileName: 'lecture.pdf', contentType: 'application/pdf', size: 64 }], knowledgeVersionId: otherUser };
      throw new Error('Unexpected route: ' + url);
    }, async download(url) { f.downloadUrl = url; return f.download ? await f.download : 'wxfile://owned-plan'; }, releaseFile: (p) => f.released.push(p), portalUrl: (p) => 'https://portal.invalid' + p,
  };
  const app = { configuration: { apiBaseUrl: 'https://api.invalid', appKey: 'main', clientId: 'KnowPulse_WeChat_main' }, session, subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); } };
  function page(name, query = {}) {
    const file = path.resolve(__dirname, '../pages', name, 'index.js'); let def;
    new vm.Script(fs.readFileSync(file, 'utf8'), { filename: file }).runInNewContext({ require: createRequire(file), Page: (d) => def = d, wx: platform, getApp: () => app });
    const p = { data: structuredClone(def.data), setData(v) { f.transfers.push(Buffer.byteLength(JSON.stringify(v))); Object.assign(this.data, v); } };
    for (const [k, v] of Object.entries(def)) if (typeof v === 'function') p[k] = v.bind(p); p.onLoad({ id: planId, versionId, schoolId: school, ...query }); return p;
  }
  return Object.assign(f, { page: page(pageName), newPage: page, requests, storage, receipts, platform, session, change });
}

test('plan listing is school-scoped metadata without provision and uses an exact version link', async () => {
  const f = fixture('teaching-plans'); await f.page.onShow(); assert.equal(f.page.data.items.length, 1); assert.ok(f.requests[0].url.includes('schoolId=' + school));
  assert.ok(f.dto.activities.every((r) => r.firstProvidedAtUtc === null)); f.page.open(pick(planId)); assert.ok(f.links[0].endsWith('&versionId=' + versionId));
  assert.ok(!JSON.stringify(f.page.data).includes('活动要求')); f.page.onHide(); assert.equal(f.page.data.items.length, 0);
});

test('explicit plan opening exposes student-safe content and actual activities without a progress write', async () => {
  const f = fixture(); await f.page.onShow(); assert.equal(f.page.data.activity.id, activityA); assert.ok(f.page.data.activity.canStart); assert.equal(f.events.length, 0);
  assert.ok(f.dto.activities.every((r) => r.firstProvidedAtUtc)); assert.ok(!JSON.stringify(f.page.data).includes('PRIVATE-HANDLING'));
  assert.ok(!JSON.stringify(f.page.data).includes('knowledgeVersionId')); assert.ok(f.requests.every((r) => !r.method));
});

test('start and explicit completion preserve literal shared text and exact student heads', async () => {
  const f = fixture(); await f.page.onShow(); await f.page.progress(action(0)); const started = f.events[0].id;
  f.page.change(field('text', '  原文总结\n')); f.page.change(field('share', ['yes'])); await f.page.progress(action(1)); assert.equal(f.events.length, 1);
  f.page.change(field('activityOnly', ['yes'])); f.page.change(field('minutes', '9')); await f.page.progress(action(1));
  assert.equal(f.events.length, 2); assert.equal(f.events[1].sharedText, '  原文总结\n'); assert.equal(f.events[1].reportedMinutes, 9);
  const writes = f.requests.filter((r) => r.url.includes('/save-my-activity/')); assert.equal(writes[1].body.expectedStudentRevisionId, started);
  assert.equal(f.page.data.historyTotal, 2); assert.equal(f.page.data.activity.state, 3); assert.ok(!JSON.stringify([...f.storage.values()]).includes('原文总结'));
});

test('lost committed progress survives a cold page and recovers the original event without replay', async () => {
  const f = fixture(); await f.page.onShow(); f.failAfter = true; await f.page.progress(action(0)); const op = f.page.data.pending.operationId;
  f.page.onUnload(); const cold = f.newPage('teaching-plan'); await cold.onShow(); assert.equal(cold.data.pending.operationId, op); assert.equal(cold.data.canRetry, false);
  await cold.recover(); assert.equal(cold.data.pending, null); assert.equal(cold.data.receipt.id, op); assert.equal(f.events.length, 1); assert.equal(f.requests.filter((r) => r.url.includes('/save-my-activity/')).length, 1);
});

test('an original receipt can be recovered after the selected publication changes without substituting the new version', async () => {
  const f = fixture(); await f.page.onShow(); f.failAfter = true; await f.page.progress(action(0)); const op = f.page.data.pending.operationId; f.page.onUnload();
  f.dto.plan.versionId = otherUser; const cold = f.newPage('teaching-plan'); await cold.onShow(); assert.equal(cold.data.plan, null); assert.equal(cold.data.pending.operationId, op);
  await cold.recover(); assert.equal(cold.data.receipt.id, op); assert.equal(cold.data.plan, null); assert.equal(cold.versionId, versionId); assert.ok(cold.data.error); assert.equal(f.events.length, 1);
});

test('explicit retry queries first and resends only the original activity, head, operation and text', async () => {
  const f = fixture(); await f.page.onShow(); f.page.change(field('text', '原请求')); f.page.change(field('share', ['yes'])); f.failBefore = true; await f.page.progress(action(0));
  f.page.change(field('text', '替换尝试')); await f.page.retry(); const writes = f.requests.filter((r) => r.url.includes('/save-my-activity/'));
  assert.deepEqual(writes[0].body, writes[1].body); assert.equal(writes[1].body.sharedText, '原请求'); assert.equal(f.events.length, 1); assert.ok(f.requests.some((r) => r.url.includes('/my-activity-operation?')));
});

test('cloud head changes preserve the original form and require explicit discard before another write', async () => {
  const f = fixture(); await f.page.onShow(); f.page.change(field('text', '未上交文字'));
  const row = f.dto.activities[0]; row.currentStudentRevisionId = oldReceipt; row.studentRevisionNumber = 1; row.currentStudent = { id: oldReceipt, activityId: activityA, studentRevisionNumber: 1, action: 0, sharedText: null };
  await f.page.load(); assert.equal(f.page.data.conflict, true); assert.equal(f.page.data.form.text, '未上交文字');
  await f.page.progress(action(0)); assert.equal(f.events.length, 0); await f.page.discard(); assert.equal(f.page.data.conflict, false); assert.equal(f.page.data.form.text, '');
});

test('formal activities use the real recipient link and cannot receive self-reported completion', async () => {
  const f = fixture(); f.dto.activities[1].formalAssessment = { canOpen: true, canStart: true, recipientId: otherUser, title: '真实复测' }; await f.page.onShow();
  await f.page.select({ detail: { value: '1' } }); assert.equal(f.page.data.activity.isFormal, true); assert.equal(f.page.data.activity.canComplete, false);
  await f.page.progress(action(1)); assert.equal(f.events.length, 0); f.page.assessment(); assert.ok(f.links[0].endsWith('&id=' + otherUser)); assert.ok(f.requests.every((r) => !r.url.includes('/start-or-resume/')));
});

test('withheld plan bodies keep provided student history without leaking old instructions or teacher notes', async () => {
  const f = fixture(); await f.page.onShow(); await f.page.progress(action(0)); f.dto.plan.availability = 2;
  await f.page.load(); assert.equal(f.page.data.content, null); assert.equal(f.page.data.activity.instructions, ''); assert.equal(f.page.data.activity.materials.length, 0); assert.equal(f.page.data.historyTotal, 1);
  assert.equal(f.page.data.activity.canStart, false); assert.equal(f.page.data.activity.canReport, true); assert.ok(!JSON.stringify(f.page.data).includes('活动要求'));
});

test('unavailable materials block start/completion but retain an explicit provided-activity difficulty path', async () => {
  const f = fixture(); await f.page.onShow(); f.dto.activities[0].materialsAvailable = false; f.dto.activities[0].state = 2; await f.page.load();
  assert.equal(f.page.data.activity.canStart, false); assert.equal(f.page.data.activity.canComplete, false); assert.equal(f.page.data.activity.canReport, true);
  f.page.change(field('text', '材料已不可读')); f.page.change(field('share', ['yes'])); f.page.change(field('difficulty', '2')); await f.page.progress(action(2));
  assert.equal(f.events[0].difficulty, 2); assert.equal(f.events[0].sharedText, '材料已不可读');
});

test('material previews keep only one Unicode-safe text segment and release owned downloads on hide', async () => {
  const f = fixture(); f.materialText = '字'.repeat(5999) + '😀' + '后'.repeat(6500); await f.page.onShow(); await f.page.material(pick(materialId));
  const first = f.page.data.material.text; assert.ok(first.length <= 6000); assert.equal(f.page.data.material.pages, 3); f.page.materialPage({ currentTarget: { dataset: { direction: 1 } } }); const second = f.page.data.material.text;
  f.page.materialPage({ currentTarget: { dataset: { direction: 1 } } }); assert.equal(first + second + f.page.data.material.text, f.materialText);
  await f.page.attachment(pick(attachmentId)); assert.equal(f.opened[0].fileType, 'pdf'); assert.equal(f.opened[0].showMenu, false); assert.ok(f.downloadUrl.includes(`/activities/${activityA}/materials/${materialId}/attachments/${attachmentId}`));
  f.page.onHide(); assert.ok(f.released.includes('wxfile://owned-plan')); assert.equal(f.page.rawMaterial, null); assert.equal(f.page.data.material, null);
});

test('leaving during a material download prevents opening a viewer and cleans its late temporary file', async () => {
  const f = fixture(); await f.page.onShow(); await f.page.material(pick(materialId)); let finish; f.download = new Promise((resolve) => finish = resolve);
  const pending = f.page.attachment(pick(attachmentId)); f.page.onHide(); finish('wxfile://late-plan'); await pending;
  assert.equal(f.opened.length, 0); assert.ok(f.released.includes('wxfile://late-plan')); assert.equal(f.page.data.imagePath, '');
});

test('late confirmation after leaving cannot write or create a new pending operation', async () => {
  const f = fixture(); await f.page.onShow(); f.dialogDelay = true; const running = f.page.progress(action(0)); f.page.onHide(); f.dialog.success({ confirm: true }); await running;
  assert.equal(f.events.length, 0); assert.equal(f.storage.size, 0);
});

test('write-permission removal still permits recovery of a prior committed event without another write', async () => {
  const f = fixture(); await f.page.onShow(); f.failAfter = true; await f.page.progress(action(0));
  const s = f.session.getState(); f.change({ ...s, context: { ...s.context, grantedPermissions: ['KnowPulse.Education.TeachingPlanView', 'KnowPulse.Education.AssessmentParticipate'] } }); await f.page.load();
  assert.equal(f.page.data.canWrite, false); assert.equal(f.page.data.canRetry, false); await f.page.recover(); assert.equal(f.page.data.pending, null); assert.equal(f.events.length, 1);
});

test('wrong-school deep links cannot discover another workspace pending receipt or mark the plan provided', async () => {
  const f = fixture(); await f.page.onShow(); f.failBefore = true; await f.page.progress(action(0)); const pendingCount = f.storage.size; f.page.onUnload();
  const s = f.session.getState(); f.change({ ...s, context: { ...s.context, workspaces: [...s.context.workspaces, { role: 2, schoolId: otherSchool }] } });
  const wrong = f.newPage('teaching-plan', { schoolId: otherSchool }); await wrong.onShow(); const before = f.requests.filter((r) => r.url.includes('/my-activity-operation?')).length; await wrong.recover();
  assert.equal(wrong.data.pending, null); assert.equal(f.storage.size, pendingCount); assert.equal(f.requests.filter((r) => r.url.includes('/my-activity-operation?')).length, before);
});

test('switching account or losing read authority clears private input and retry body while retaining only identifiers', async () => {
  const f = fixture(); await f.page.onShow(); f.page.change(field('text', 'PRIVATE-UNSENT')); f.page.change(field('share', ['yes'])); f.failBefore = true; await f.page.progress(action(0));
  f.change({ ...f.session.getState(), generation: 2, context: null, termination: 'forbidden' }); assert.equal(f.page.form.text, ''); assert.equal(f.page.data.form, null); assert.equal(f.page.writer.state().canRetry, false);
  await f.page.load(); assert.ok(f.page.data.plan); assert.ok(f.page.data.pending); assert.equal(f.page.data.form.text, '');
  const s = f.session.getState(); f.change({ ...s, generation: 3, context: { ...s.context, accountUserId: otherUser } }); const before = f.requests.length; await f.page.load(); assert.equal(f.requests.length, before); assert.equal(f.page.data.plan, null);
});

test('storage failure prevents sending an operation and a read-only role cannot submit progress', async () => {
  const f = fixture(); await f.page.onShow(); f.storageBlocked = true; await f.page.progress(action(0)); assert.equal(f.events.length, 0);
  const teacher = fixture('teaching-plan', 1); await teacher.page.onShow(); assert.equal(teacher.requests.length, 0); assert.equal(teacher.page.data.plan, null);
});

test('projection rejects mismatched activity heads and hides formal metadata when assessment permission is required', () => {
  const raw = dto(); raw.activities[1].formalAssessment = { requiresAssessmentPermission: true, canOpen: true, recipientId: otherUser, title: 'HIDDEN-FORMAL', sealedAtUtc: '2026-10-18T00:00:00Z' };
  const value = model.view(raw, school, planId, versionId); assert.equal(value.activities[1].assessment.canOpen, false); assert.equal(value.activities[1].assessment.sealed, '—'); assert.ok(!JSON.stringify(value).includes('HIDDEN-FORMAL'));
  raw.activities[0].currentStudentRevisionId = oldReceipt; assert.throws(() => model.view(raw, school, planId, versionId), { code: 'INVALID_RESPONSE' });
});

test('shared material segmentation preserves supplementary characters across every boundary', () => {
  for (const offset of [5998, 5999, 6000, 11999]) { const text = '甲'.repeat(offset) + '😀' + '乙'.repeat(7000); const row = { id: materialId, text, attachments: [] }; const pages = study.materialPage(row, 0).pages;
    assert.equal(Array.from({ length: pages }, (_, i) => study.materialPage(row, i).text).join(''), text); }
});
