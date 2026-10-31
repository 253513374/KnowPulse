const test = require('node:test'), assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), vm = require('node:vm'), crypto = require('node:crypto');
const { createRequire } = require('node:module'), model = require('../utils/reminder');
const [school, task, recipient, publication, owner, noticeId, other] = Array.from({ length: 7 }, () => crypto.randomUUID());
const at = '2026-10-18T00:00:00Z';
const preview = () => ({ id: recipient, schoolId: school, taskId: task, publicationId: publication, studentName: 'PRIVATE-STUDENT', taskTitle: 'PRIVATE-TASK', previewFingerprint: 'a'.repeat(64), canSend: true, hasStarted: false, blockReason: '', endsAtUtc: at, serverNowUtc: at, nextAllowedAtUtc: null, privateObservation: 'PRIVATE-NOTE' });
const receipt = (id = noticeId) => ({ id, schoolId: school, taskId: task, publicationId: publication, recipientId: recipient, kind: 0, resultReleaseId: null, canOpen: true, recordedAtUtc: at, availableAtUtc: at, readAtUtc: null, weChatDelivery: 'NotConfigured', referenceAnswer: 'PRIVATE-ANSWER' });
function fixture(mode = 'teacher') {
  let state = { authenticated: true, generation: 1, context: { accountUserId: owner, tenantId: null, grantedPermissions: ['KnowPulse.Education.AssessmentManage', 'KnowPulse.Education.AssessmentParticipate'], workspaces: [{ role: mode === 'teacher' ? 1 : 2, schoolId: school, classes: [] }] } };
  const storage = new Map(), receipts = new Map(), listeners = new Set();
  const f = { requests: [], links: [], preview: preview(), notice: receipt(), failAfter: false, waitDialog: false };
  const platform = { getStorageSync: (key) => storage.get(key), setStorageSync: (key, value) => storage.set(key, structuredClone(value)), removeStorageSync: (key) => storage.delete(key),
    getRandomValues(o) { const b = crypto.randomBytes(16); o.success({ randomValues: b.buffer.slice(b.byteOffset, b.byteOffset + 16) }); },
    showModal(o) { if (f.waitDialog) f.dialog = o; else o.success({ confirm: true }); }, navigateTo(o) { f.links.push(o.url); }, reLaunch() {} };
  const session = { getState: () => state, refreshWorkspace: async () => state, async request(url, method = 'GET', body) {
    f.requests.push({ url, method, body: structuredClone(body) });
    if (url.includes('/reminder-targets?')) return { totalCount: 1, items: [structuredClone(f.preview)] };
    if (url.includes('/reminder-preview?')) { if (f.readWait) return f.readWait; return structuredClone(f.preview); }
    if (url.includes('/reminder-operation?')) return structuredClone(receipts.get(new URL('https://api.invalid' + url).searchParams.get('operationId')) || null);
    if (url.includes('/send-reminder?')) { if (f.failBefore) { f.failBefore = false; throw Object.assign(new Error(), { code: 'NETWORK_UNCONFIRMED' }); }
      if (body.previewFingerprint !== f.preview.previewFingerprint) throw Object.assign(new Error(), { status: 409 });
      const row = receipt(body.operationId); receipts.set(row.id, row); f.preview.canSend = false; f.preview.blockReason = 'CoolingDown'; f.preview.previewFingerprint = 'b'.repeat(64);
      if (f.failAfter) { f.failAfter = false; throw Object.assign(new Error(), { code: 'NETWORK_UNCONFIRMED' }); } return structuredClone(row);
    }
    if (url.includes('/my-notices?')) return { totalCount: 1, items: [structuredClone(f.notice)] };
    if (url.includes('/read-my-reminder/')) return { ...structuredClone(f.notice), readAtUtc: at };
    throw new Error('Unexpected request ' + url);
  } };
  const app = { session, configuration: { apiBaseUrl: 'https://api.invalid', clientId: 'client', appKey: 'main' }, subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); } };
  function page() { const file = path.resolve(__dirname, '../pages/assessment-reminders/index.js'); let definition;
    new vm.Script(fs.readFileSync(file, 'utf8')).runInNewContext({ require: createRequire(file), Page: (d) => definition = d, wx: platform, getApp: () => app });
    const p = { data: structuredClone(definition.data), setData(value) { assert.ok(Buffer.byteLength(JSON.stringify(value)) < 1024 * 1024); Object.assign(this.data, value); } };
    for (const [key, value] of Object.entries(definition)) if (typeof value === 'function') p[key] = value.bind(p); p.onLoad({ mode, schoolId: school, taskId: task }); return p;
  }
  return Object.assign(f, { page: page(), newPage: page, storage, platform, session, change(next) { state = next; listeners.forEach((fn) => fn(next)); } });
}
const click = (id = recipient) => ({ currentTarget: { dataset: { id } } });

test('reminder previews validate scope and project no answer/private observation into mobile views', async () => {
  const f = fixture(); await f.page.onShow(); await f.page.choose(click()); assert.equal(f.page.data.preview.name, 'PRIVATE-STUDENT'); assert.ok(!JSON.stringify(f.page.data).includes('PRIVATE-NOTE'));
  assert.throws(() => model.preview(preview(), other, task), { code: 'INVALID_RESPONSE' }); assert.throws(() => model.receipt({ ...receipt(), weChatDelivery: 'Delivered' }, school), { code: 'INVALID_RESPONSE' });
  assert.throws(() => model.preview({ ...preview(), studentName: '' }, school, task), { code: 'INVALID_RESPONSE' }); assert.throws(() => model.preview({ ...preview(), canSend: false, blockReason: undefined }, school, task), { code: 'INVALID_RESPONSE' });
  let resolve; f.readWait = new Promise((r) => resolve = r); const reading = f.page.choose(click()); f.page.onHide(); resolve(preview()); await reading; assert.equal(f.page.data.preview, null); assert.equal(f.page.data.items.length, 0);
});
test('a lost reminder response is recovered after a cold page without a second notice or stored names', async () => {
  const f = fixture(); await f.page.onShow(); await f.page.choose(click()); f.failAfter = true; await f.page.send(); assert.ok(f.page.data.pending);
  const stored = JSON.stringify([...f.storage.values()]); for (const text of ['PRIVATE-STUDENT', 'PRIVATE-TASK', 'PRIVATE-NOTE']) assert.ok(!stored.includes(text));
  f.page.onHide(); const cold = f.newPage(); await cold.onShow(); await cold.recover(); assert.equal(cold.data.pending, null); assert.equal(cold.data.items[0].canSend, false); assert.equal(f.requests.filter((r) => r.url.includes('/send-reminder?')).length, 1);
});
test('explicit cold retry recovers first and resends the exact confirmed metadata, while quota failure prevents untracked sends', async () => {
  const f = fixture(); await f.page.onShow(); await f.page.choose(click()); f.failBefore = true; await f.page.send(); f.page.onHide(); const cold = f.newPage(); await cold.onShow(); await cold.retry();
  const writes = f.requests.filter((r) => r.url.includes('/send-reminder?')); assert.equal(writes.length, 2); assert.deepEqual(writes[0].body, writes[1].body); assert.ok(f.requests.some((r) => r.url.includes('/reminder-operation?')));
  const q = fixture(); await q.page.onShow(); await q.page.choose(click()); q.platform.setStorageSync = () => { throw new Error('quota'); }; await q.page.send(); assert.equal(q.requests.filter((r) => r.method === 'POST').length, 0);
});
test('an account change during confirmation cannot send a reminder and corrupt tracking cannot guess an operation', async () => {
  const f = fixture(); await f.page.onShow(); await f.page.choose(click()); f.waitDialog = true; const sending = f.page.send(); const state = f.session.getState(); f.change({ ...state, generation: 2, context: { ...state.context, accountUserId: other } }); f.dialog.success({ confirm: true }); await sending;
  assert.equal(f.requests.filter((r) => r.method === 'POST').length, 0); assert.equal(f.storage.size, 0); assert.equal(f.page.data.preview, null);
  const q = fixture(); q.platform.getStorageSync = () => ({ bad: true, rawAnswer: 'PRIVATE-ANSWER' }); await q.page.onShow(); assert.equal(q.page.data.pending.invalid, true); await q.page.recover(); assert.equal(q.requests.filter((r) => r.url.includes('/reminder-operation?')).length, 0); await q.page.abandon();
});
test('student listing does not mark read or start an exam; explicit opening marks only the exact reminder then navigates', async () => {
  const f = fixture('student'); f.notice.kind = 1; await f.page.onShow(); assert.equal(f.requests.filter((r) => r.method === 'POST').length, 0); assert.ok(!JSON.stringify(f.page.data).includes('PRIVATE-ANSWER'));
  await f.page.open(click(noticeId)); assert.equal(f.requests.filter((r) => r.method === 'POST').length, 1); assert.ok(f.requests[1].url.endsWith(`/read-my-reminder/${school}`));
  assert.ok(f.links[0].endsWith('&id=' + recipient)); assert.ok(!f.requests.some((r) => r.url.includes('start-or-resume'))); f.page.onHide(); assert.equal(f.page.data.items.length, 0);
});

test('result messages pin publication and release, and replaced sources can be marked read without silently opening another result', async () => {
  const f = fixture('student'); f.notice.kind = 2; f.notice.resultReleaseId = other; f.notice.totalScore = 'PRIVATE-SCORE'; await f.page.onShow();
  assert.ok(!JSON.stringify(f.page.data).includes('PRIVATE-SCORE')); await f.page.open(click(noticeId));
  assert.ok(f.links[0].includes('/assessment-result/')); assert.ok(f.links[0].includes('taskRevisionId=' + publication)); assert.ok(f.links[0].endsWith('&releaseId=' + other));
  f.notice.canOpen = false; await f.page.onShow(); await f.page.open(click(noticeId)); assert.equal(f.links.length, 1); assert.ok(f.page.data.notice.includes('重新隐藏'));
  assert.throws(() => model.receipt({ ...receipt(), kind: 2 }, school), { code: 'INVALID_RESPONSE' });
});
