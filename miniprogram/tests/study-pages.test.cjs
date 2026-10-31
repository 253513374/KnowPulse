const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const crypto = require('node:crypto');
const { createRequire } = require('node:module');
const ids = Array.from({ length: 8 }, (_, i) => String(i + 1).repeat(8) + '-' + [4, 4, 4, 12].map((n) => String(i + 1).repeat(n)).join('-'));
const [school, user, recipient, publication, materialId, fileId, otherSchool, otherUser] = ids;
const data = () => ({ id: recipient, schoolId: school, publicationId: publication, versionNumber: 1, title: '巩固学习', teacherName: '任课教师', className: '一班', subjectName: '数学',
  startsAtUtc: '2026-01-01T00:00:00Z', endsAtUtc: '2030-01-01T00:00:00Z', serverNowUtc: '2026-10-18T00:00:00Z', estimatedMinutes: 20,
  availability: 1, state: 0, materialsAvailable: true, instructions: '任务要求', materials: [{ id: materialId, title: '讲义' }], currentStudentRevisionId: null, currentStudent: null, firstProvidedAtUtc: null });
const tap = (field, value) => ({ currentTarget: { dataset: { field } }, detail: { value } });
const action = (n) => ({ currentTarget: { dataset: { action: n } } });
const pick = (id) => ({ currentTarget: { dataset: { id } } });

function fixture(name = 'study-task', role = 2) {
  let state = { authenticated: true, generation: 1, context: { accountUserId: user, tenantId: null, grantedPermissions: ['KnowPulse.Education.LearningParticipate'], workspaces: [{ role, schoolId: school, classes: [] }] } };
  const listeners = new Set(); const storage = new Map(); const requests = []; const receipts = new Map(); let knownContext = state.context;
  const f = { dto: data(), events: [], failAfter: false, failBefore: false, blockedStorage: false, hook: null, delayDialog: false, file: null, released: [], opened: [] };
  const change = (s) => { state = s; if (s.context) knownContext = s.context; listeners.forEach((fn) => fn(s)); };
  const platform = { getRandomValues(o) { const b = crypto.randomBytes(16); o.success({ randomValues: b.buffer.slice(b.byteOffset, b.byteOffset + b.length) }); },
    getStorageSync: (k) => storage.get(k), setStorageSync(k, v) { if (f.blockedStorage) throw new Error('full'); storage.set(k, structuredClone(v)); }, removeStorageSync: (k) => storage.delete(k),
    showModal(o) { if (f.delayDialog) f.dialog = o; else o.success({ confirm: true }); }, enableAlertBeforeUnload() {}, disableAlertBeforeUnload() {}, navigateTo(o) { f.navigation = o.url; }, reLaunch() {},
    openDocument(o) { f.opened.push(o); },
  };
  const session = { getState: () => state, async refreshWorkspace() { const context = structuredClone(state.context || knownContext); change({ ...state, context: null }); await Promise.resolve(); change({ ...state, context }); return state; },
    async request(url, method, body) {
      requests.push({ url, method, body: body && structuredClone(body) });
      if (f.hook) { const r = f.hook(url, method, body); if (r !== undefined) return r; }
      if (url.includes('/my-list?')) return { totalCount: 1, items: [structuredClone(f.dto)] };
      if (url.includes('/open-my-for-school?')) { assert.ok(url.includes('schoolId=' + school)); if ([1, 2].includes(f.dto.availability)) f.dto.firstProvidedAtUtc = '2026-10-18T00:00:00Z'; return structuredClone(f.dto); }
      if (url.includes('/my-history?')) { const offset = Number(new URL('https://api.invalid' + url).searchParams.get('SkipCount')); return { totalCount: f.events.length, items: structuredClone(f.events.slice().reverse().slice(offset, offset + 20)) }; }
      if (url.includes('/my-operation?')) return structuredClone(receipts.get(new URL('https://api.invalid' + url).searchParams.get('operationId')) || null);
      if (url.includes('/my-material?')) return { id: materialId, title: '指定材料', isAvailable: [1, 2].includes(f.dto.availability), contentText: '<script>literal teaching text</script>',
        attachments: [{ id: fileId, fileName: '讲义.pdf', contentType: 'application/pdf', size: 128 }], internalTeacherNote: 'PRIVATE-TEACHER', knowledgeItemId: otherUser };
      if (url.includes('/save-my-progress/')) {
        if (f.failBefore) { f.failBefore = false; throw Object.assign(new Error('lost'), { code: 'NETWORK_UNCONFIRMED' }); }
        if (f.dto.currentStudentRevisionId !== body.expectedStudentRevisionId) throw Object.assign(new Error('changed'), { status: 409 });
        const row = { id: body.operationId, recipientId: recipient, studentRevisionNumber: f.events.length + 1, action: body.action, difficulty: body.difficulty,
          sharedText: body.sharedText, reportedMinutes: body.reportedMinutes, recordedAtUtc: '2026-10-18T00:01:00Z', requestFingerprint: 'a'.repeat(64) };
        f.events.push(row); receipts.set(row.id, row); f.dto.currentStudent = row; f.dto.currentStudentRevisionId = row.id; f.dto.state = [1, 3, 2][row.action];
        if (f.failAfter) { f.failAfter = false; throw Object.assign(new Error('lost'), { code: 'NETWORK_UNCONFIRMED' }); }
        return structuredClone(row);
      }
      throw new Error('Unexpected test route: ' + url);
    },
    async download(url) { f.downloadUrl = url; return f.file ? await f.file : 'wxfile://private-material'; }, releaseFile(p) { f.released.push(p); },
  };
  const app = { session, configuration: { apiBaseUrl: 'https://api.example.invalid', appKey: 'main', clientId: 'KnowPulse_WeChat_main' }, subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); } };
  function page(name) {
    const file = path.resolve(__dirname, '../pages', name, 'index.js'); let definition;
    new vm.Script(fs.readFileSync(file, 'utf8'), { filename: file }).runInNewContext({ require: createRequire(file), Page: (v) => definition = v, getApp: () => app, wx: platform });
    const p = { data: structuredClone(definition.data), setData(v) { Object.assign(this.data, v); } };
    for (const [key, value] of Object.entries(definition)) if (typeof value === 'function') p[key] = value.bind(p);
    p.onLoad({ id: recipient, schoolId: school }); return p;
  }
  return Object.assign(f, { page: page(name), newPage: page, session, platform, requests, receipts, storage, change });
}

test('school-scoped list paginates metadata without providing materials or starting activities', async () => {
  const f = fixture('study-tasks'); await f.page.onShow(); assert.equal(f.page.data.items.length, 1);
  assert.ok(f.requests[0].url.includes('schoolId=' + school)); assert.equal(f.dto.firstProvidedAtUtc, null); assert.equal(f.events.length, 0);
  f.page.open(pick(recipient)); assert.ok(f.navigation.endsWith('&id=' + recipient)); assert.equal(f.requests.filter((r) => r.method === 'POST').length, 0);
  f.page.onHide(); assert.equal(f.page.data.items.length, 0);
});

test('wrong-school metadata and teacher-only workspaces cannot expose a student task', async () => {
  const f = fixture('study-tasks'); f.dto.schoolId = otherSchool; await f.page.onShow(); assert.equal(f.page.data.items.length, 0); assert.ok(f.page.data.error);
  const t = fixture('study-task', 1); await t.page.onShow(); assert.equal(t.requests.length, 0); assert.equal(t.page.data.detail, null);
});

test('explicit task opening provides only current material access and never invents progress or mastery', async () => {
  const f = fixture(); await f.page.onShow(); assert.ok(f.dto.firstProvidedAtUtc); assert.equal(f.events.length, 0);
  await f.page.material(pick(materialId)); assert.equal(f.page.data.material.text, '<script>literal teaching text</script>');
  assert.ok(!JSON.stringify(f.page.data).includes('PRIVATE-TEACHER')); assert.ok(!JSON.stringify(f.page.data).includes('knowledgeItemId'));
  await f.page.attachment(pick(fileId)); assert.ok(f.downloadUrl.includes(`/recipients/${recipient}/materials/${materialId}/attachments/${fileId}`));
  assert.equal(f.opened[0].fileType, 'pdf'); assert.equal(f.opened[0].showMenu, false);
  f.page.onHide(); assert.deepEqual(f.released, ['wxfile://private-material']); assert.equal(f.page.data.material, null);
});

test('start and explicit completion share only confirmed text and preserve the expected student head', async () => {
  const f = fixture(); await f.page.onShow(); await f.page.progress(action(0)); assert.equal(f.events.length, 1);
  const started = f.events[0].id; f.page.change(tap('text', '我的总结')); f.page.change(tap('minutes', '18')); f.page.change(tap('share', ['yes']));
  await f.page.progress(action(1)); assert.equal(f.events.length, 1);
  f.page.change(tap('activityOnly', ['yes'])); await f.page.progress(action(1)); assert.equal(f.events.length, 2);
  const input = f.requests.filter((r) => r.url.includes('/save-my-progress/'))[1].body;
  assert.equal(input.expectedStudentRevisionId, started); assert.equal(input.sharedText, '我的总结'); assert.equal(input.confirmActivityOnly, true);
  assert.equal(f.page.data.detail.stateLabel, '自报完成'); assert.equal(f.page.data.historyTotal, 2);
  assert.ok(!JSON.stringify([...f.storage.values()]).includes('我的总结'));
});

test('lost committed progress recovers the original receipt across a cold page without duplicating an event', async () => {
  const f = fixture(); await f.page.onShow(); f.failAfter = true; await f.page.progress(action(0));
  const op = f.page.data.pending.operationId; assert.equal(f.events.length, 1); assert.ok(f.page.data.canRetry);
  f.page.onUnload(); const cold = f.newPage('study-task'); await cold.onShow(); assert.equal(cold.data.pending.operationId, op); assert.equal(cold.data.canRetry, false);
  await cold.recover(); assert.equal(cold.data.pending, null); assert.equal(f.events.length, 1);
  assert.equal(f.requests.filter((r) => r.url.includes('/save-my-progress/')).length, 1); assert.equal(f.storage.size, 0);
});

test('explicit retry queries first and resends only the same original operation and body', async () => {
  const f = fixture(); await f.page.onShow(); f.failBefore = true; await f.page.progress(action(0)); const pending = f.page.data.pending;
  f.page.change(tap('text', 'attempt to replace')); assert.equal(f.page.data.form.text, '');
  await f.page.retry(); const writes = f.requests.filter((r) => r.url.includes('/save-my-progress/'));
  assert.equal(writes.length, 2); assert.deepEqual(writes[0].body, writes[1].body); assert.equal(writes[1].body.operationId, pending.operationId);
  assert.ok(f.requests.findIndex((r) => r.url.includes('/my-operation?')) > f.requests.indexOf(writes[0])); assert.equal(f.events.length, 1);
});

test('a changed cloud journal preserves original input and never silently rebases it on refresh', async () => {
  const f = fixture(); await f.page.onShow(); f.page.change(tap('text', '原输入'));
  f.dto.currentStudentRevisionId = otherUser; await f.page.load(); assert.equal(f.page.data.conflict, true); assert.equal(f.page.data.form.text, '原输入');
  await f.page.progress(action(0)); assert.equal(f.events.length, 0);
  await f.page.discard(); assert.equal(f.page.data.conflict, false); assert.equal(f.page.data.form.text, '');
});

test('withdrawn materials lose body and attachments while own handed-in history remains recoverable', async () => {
  const f = fixture(); await f.page.onShow(); await f.page.progress(action(0));
  f.page.change(tap('text', '已上交的历史总结')); f.page.change(tap('share', ['yes'])); f.page.change(tap('activityOnly', ['yes'])); await f.page.progress(action(1));
  await f.page.material(pick(materialId)); f.dto.availability = 5; f.dto.materialsAvailable = false; await f.page.load();
  assert.equal(f.page.data.detail.instructions, ''); assert.equal(f.page.data.detail.materials.length, 0); assert.equal(f.page.data.material, null);
  assert.equal(f.page.data.history[0].text, '已上交的历史总结'); assert.equal(f.page.data.detail.canComplete, false);
  f.page.change(tap('text', '材料已无法读取')); f.page.change(tap('difficulty', '2')); f.page.change(tap('share', ['yes'])); await f.page.progress(action(2));
  assert.equal(f.events[2].difficulty, 2); assert.equal(f.events[2].sharedText, '材料已无法读取');
});

test('a late material response and a download after leaving cannot restore hidden content or retain a file', async () => {
  const f = fixture(); await f.page.onShow(); let finish;
  f.hook = (u) => u.includes('/my-material?') ? new Promise((resolve) => finish = resolve) : undefined;
  const reading = f.page.material(pick(materialId)); f.page.onHide(); finish({ id: materialId, isAvailable: true, attachments: [], contentText: 'LATE-BODY' }); await reading;
  assert.equal(f.page.data.material, null); f.hook = null; await f.page.onShow(); await f.page.material(pick(materialId));
  f.file = new Promise((resolve) => finish = resolve); const download = f.page.attachment(pick(fileId)); f.page.onHide(); finish('wxfile://late'); await download;
  assert.ok(f.released.includes('wxfile://late')); assert.equal(f.opened.length, 0); assert.equal(f.page.data.imagePath, '');
});

test('confirming a progress dialog after leaving cannot send or persist a new operation', async () => {
  const f = fixture(); await f.page.onShow(); f.delayDialog = true; const running = f.page.progress(action(0));
  f.page.onHide(); f.dialog.success({ confirm: true }); await running; assert.equal(f.events.length, 0); assert.equal(f.storage.size, 0);
});

test('switching account clears form and exact retry text and refuses to read the old student task', async () => {
  const f = fixture(); await f.page.onShow(); f.page.change(tap('text', 'PRIVATE-INPUT')); f.page.change(tap('share', ['yes'])); f.failBefore = true; await f.page.progress(action(0));
  const s = f.session.getState(); f.change({ ...s, generation: 2, context: { ...s.context, accountUserId: otherUser } });
  assert.equal(f.page.data.form, null); assert.equal(f.page.form.text, ''); assert.equal(f.page.writer.state().canRetry, false);
  const count = f.requests.length; await f.page.load(); assert.equal(f.requests.length, count); assert.ok(!JSON.stringify(f.page.data).includes('PRIVATE-INPUT'));
});

test('local storage failure prevents a new command, and a cold unresolved command cannot invent a retry body', async () => {
  const f = fixture(); await f.page.onShow(); f.blockedStorage = true; await f.page.progress(action(0)); assert.equal(f.events.length, 0);
  f.blockedStorage = false; f.failBefore = true; await f.page.progress(action(0)); f.page.onUnload(); const cold = f.newPage('study-task'); await cold.onShow();
  const n = f.requests.filter((r) => r.url.includes('/save-my-progress/')).length; await cold.retry(); assert.equal(f.requests.filter((r) => r.url.includes('/save-my-progress/')).length, n);
  assert.ok(cold.data.pending); assert.equal(cold.data.canRetry, false);
});

test('unsupported, mismatched and oversized files cannot invoke a native viewer or send a download', async () => {
  const f = fixture(); await f.page.onShow();
  f.hook = (u) => u.includes('/my-material?') ? { id: materialId, isAvailable: true, attachments: [
    { id: fileId, fileName: 'page.html', contentType: 'text/html', size: 32 },
    { id: otherSchool, fileName: 'fake.pdf', contentType: 'text/html', size: 32 },
    { id: otherUser, fileName: 'large.pdf', contentType: 'application/pdf', size: 21 * 1024 * 1024 }], contentText: '文字' } : undefined;
  await f.page.material(pick(materialId)); for (const id of [fileId, otherSchool, otherUser]) await f.page.attachment(pick(id));
  assert.equal(f.downloadUrl, undefined); assert.equal(f.opened.length, 0);
});

test('a late committed progress response leaves its identifier pending and is recovered after returning', async () => {
  const f = fixture(); await f.page.onShow(); let finish; let posted;
  f.hook = (url, method, body) => url.includes('/save-my-progress/') ? new Promise((resolve) => { finish = resolve; posted = body; }) : undefined;
  const running = f.page.progress(action(0)); while (!finish) await new Promise(setImmediate);
  f.page.onHide(); const row = { id: posted.operationId, recipientId: recipient, action: 0, difficulty: null, sharedText: null, reportedMinutes: null,
    studentRevisionNumber: 1, requestFingerprint: 'b'.repeat(64), recordedAtUtc: '2026-10-18T01:00:00Z' };
  f.receipts.set(row.id, row); f.events.push(row); f.dto.currentStudent = row; f.dto.currentStudentRevisionId = row.id; f.dto.state = 1; finish(row); await running;
  assert.equal(f.page.data.detail, null); assert.equal(f.storage.size, 1); assert.equal(f.page.writer.state().canRetry, false);
  f.hook = null; await f.page.onShow(); await f.page.recover(); assert.equal(f.storage.size, 0); assert.equal(f.events.length, 1);
});

test('refreshing workspace identity discards a material response from the former authorization context', async () => {
  const f = fixture(); await f.page.onShow(); let finish;
  f.hook = (u) => u.includes('/my-material?') ? new Promise((resolve) => finish = resolve) : undefined;
  const pending = f.page.material(pick(materialId)); await f.session.refreshWorkspace();
  finish({ id: materialId, isAvailable: true, contentText: 'OLD-CONTEXT', attachments: [] }); await pending;
  assert.equal(f.page.data.material, null); assert.equal(f.page.data.detail, null);
});

test('true authorization loss drops unshared form and retry text while retaining only the operation reference', async () => {
  const f = fixture(); await f.page.onShow(); f.page.change(tap('text', 'PRIVATE-DIFFICULTY')); f.page.change(tap('share', ['yes']));
  f.failBefore = true; await f.page.progress(action(0)); const op = f.page.data.pending.operationId;
  f.change({ ...f.session.getState(), generation: 2, context: null, termination: 'forbidden' });
  assert.equal(f.page.data.form, null); assert.equal(f.page.form.text, ''); assert.equal(f.page.writer.state().canRetry, false);
  assert.equal(f.page.writer.state().pending.operationId, op); assert.ok(!JSON.stringify([...f.storage.values()]).includes('PRIVATE-DIFFICULTY'));
  await f.page.load(); assert.ok(f.page.data.detail); assert.equal(f.page.data.pending.operationId, op); assert.equal(f.page.data.canRetry, false);
  assert.equal(f.session.getState().termination, 'forbidden'); assert.equal(f.page.data.form.text, '');
  const before = f.requests.filter((r) => r.url.includes('/save-my-progress/')).length; await f.page.recover();
  assert.ok(f.page.data.pending); assert.equal(f.requests.filter((r) => r.url.includes('/save-my-progress/')).length, before);
});

test('future, overdue, superseded and withdrawn tasks keep distinct body and execution gates', async () => {
  for (const availability of [0, 2, 3, 4, 5]) {
    const f = fixture(); f.dto.availability = availability; f.dto.instructions = 'RESTRICTED-INSTRUCTIONS'; await f.page.onShow();
    const row = f.page.data.detail; assert.ok(row); assert.equal(row.canStart, false); assert.equal(row.canComplete, false);
    assert.equal(row.canReport, availability === 2);
    assert.equal(row.materials.length, availability === 2 ? 1 : 0); assert.equal(row.instructions, availability === 2 ? 'RESTRICTED-INSTRUCTIONS' : '');
    await f.page.progress(action(0)); await f.page.progress(action(1)); assert.equal(f.events.length, 0);
  }
});

test('standalone materials share bounded Unicode-safe paging and clear the complete body on hide', async () => {
  const f = fixture(); await f.page.onShow(); const body = '甲'.repeat(5999) + '😀' + '乙'.repeat(6500);
  f.hook = (url) => url.includes('/my-material?') ? { id: materialId, isAvailable: true, contentText: body, attachments: [] } : undefined;
  await f.page.material(pick(materialId)); const parts = [f.page.data.material.text];
  while (f.page.data.material.page < f.page.data.material.pages) { f.page.materialPage({ currentTarget: { dataset: { direction: 1 } } }); parts.push(f.page.data.material.text); }
  assert.equal(parts.join(''), body); assert.ok(parts.every((s) => s.length <= 6000)); f.page.onHide(); assert.equal(f.page.rawMaterial, null); assert.equal(f.page.data.material, null);
});
