const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const crypto = require('node:crypto');
const { createRequire } = require('node:module');
const model = require('../utils/collection');
const id = () => crypto.randomUUID();
const school = id(), recipient = id(), question = id(), release = id(), account = id();
const click = (value, kind) => ({ currentTarget: { dataset: { id: value, kind } } });
function fixture() {
  const context = { accountUserId: account, tenantId: null, workspaces: [{ role: 2, schoolId: school, classes: [] }], grantedPermissions: ['KnowPulse.Education.AssessmentParticipate', 'KnowPulse.Education.LearningParticipate'] };
  let state = { generation: 1, authenticated: true, context, termination: null };
  const listeners = new Set(), storage = new Map(), requests = [], receipts = new Map(), links = [];
  const f = { head: null, canCollect: true, release, confirm: true, lose: false, missing: false, quota: false, hook: null, extraRows: null };
  const change = (s) => { state = s; for (const fn of listeners) fn(s); };
  const platform = { getRandomValues(o) { const bytes = crypto.randomBytes(16); o.success({ randomValues: bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.length) }); },
    getStorageSync: (k) => storage.get(k), setStorageSync(k, v) { if (f.quota) throw new Error('quota'); storage.set(k, structuredClone(v)); }, removeStorageSync: (k) => storage.delete(k), getStorageInfoSync: () => ({ keys: [...storage.keys()] }),
    showModal(o) { if (f.modal) f.modal(o); else o.success({ confirm: f.confirm }); }, navigateTo(o) { links.push(o.url); }, reLaunch() {} };
  const session = { getState: () => state, async refreshWorkspace() { const before = state.context; change({ ...state, context: null }); await Promise.resolve(); change({ ...state, context: structuredClone(before) }); return state; },
    async request(route, method = 'GET', body) {
      requests.push({ route, method, body: body && structuredClone(body) });
      if (f.hook) { const value = f.hook(route, method, body); if (value !== undefined) return value; }
      const p = new URL('https://api.invalid' + route).searchParams;
      if (route.includes('/my-collections?')) { const all = f.extraRows || (f.head?.isCollected ? [{ ...f.head, sourceAvailable: f.canCollect, stem: 'PRIVATE-EXTRA-BODY' }] : []); const skip = Number(p.get('SkipCount')); return { totalCount: all.length, items: structuredClone(all.slice(skip, skip + 20)) }; }
      if (route.includes('/my-collection?')) return { schoolId: school, recipientId: recipient, paperQuestionId: question, canCollect: f.canCollect, resultReleaseId: f.canCollect ? f.release : null, current: f.head && { ...f.head, sourceAvailable: f.canCollect } };
      if (route.includes('/my-collection-operation?')) return f.missing ? null : structuredClone(receipts.get(p.get('operationId')) || null);
      if (route.includes('/set-my-collection/')) {
        if (body.expectedCurrentOperationId !== (f.head?.id || null)) throw Object.assign(new Error('conflict'), { code: 'COLLECTION_REJECTED', status: 409 });
        if (body.isCollected && (!f.canCollect || body.expectedResultReleaseId !== f.release)) throw Object.assign(new Error('closed'), { code: 'COLLECTION_REJECTED', status: 403 });
        const row = { id: body.operationId, schoolId: school, recipientId: recipient, paperQuestionId: question, revisionNumber: (f.head?.revisionNumber || 0) + 1, isCollected: body.isCollected, recordedAtUtc: new Date().toISOString(), sourceAvailable: f.canCollect, taskTitle: '原测评' };
        f.head = row; receipts.set(row.id, structuredClone(row)); if (f.lose) { f.lose = false; throw Object.assign(new Error('lost'), { code: 'NETWORK_UNCONFIRMED' }); } return structuredClone(row);
      }
      throw new Error('Unexpected request');
    } };
  const app = { configuration: { apiBaseUrl: 'https://api.invalid', appKey: 'main', clientId: 'KnowPulse_WeChat_main' }, session, subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); } };
  function page(name = 'question-collections', query = { schoolId: school, recipientId: recipient, questionId: question, releaseId: release }) {
    const file = path.resolve(__dirname, '../pages', name, 'index.js'); let def;
    new vm.Script(fs.readFileSync(file, 'utf8'), { filename: file }).runInNewContext({ require: createRequire(file), Page: (v) => def = v, getApp: () => app, wx: platform });
    const p = { data: structuredClone(def.data), setData(v) { assert.ok(Buffer.byteLength(JSON.stringify(v)) < 1024 * 1024); Object.assign(this.data, v); } };
    for (const [k, v] of Object.entries(def)) if (typeof v === 'function') p[k] = v.bind(p); p.onLoad(query); return p;
  }
  return Object.assign(f, { app, session, platform, change, storage, requests, receipts, links, page });
}

test('collection page reads only scoped metadata until explicit confirmation and never copies source bodies', async () => {
  const f = fixture(), p = f.page(); await p.onShow(); assert.ok(p.data.target.canCollect); assert.equal(f.requests.filter((r) => r.method === 'POST').length, 0);
  f.confirm = false; await p.set(click()); assert.equal(f.head, null); f.confirm = true; await p.set(click()); assert.equal(f.head.isCollected, true); assert.equal(p.data.rows.length, 1);
  assert.ok(!JSON.stringify(p.data).includes('PRIVATE-EXTRA')); assert.ok(f.requests.every((r) => !r.route.includes('my-result') && !r.route.includes('practice')));
});
test('a lost collection response is recovered after a cold page without a repeated write or source content in storage', async () => {
  const f = fixture(), p = f.page(); await p.onShow(); f.lose = true; await p.set(click()); assert.equal(p.data.pendingTotal, 1); assert.ok(p.data.error);
  const op = p.data.pending[0].operationId; assert.ok(!JSON.stringify([...f.storage.values()]).includes('原测评')); p.onUnload(); const cold = f.page('question-collections', { schoolId: school }); await cold.onShow();
  await cold.pendingAction(click(op, 'recover')); assert.equal(cold.data.pendingTotal, 0); assert.equal(cold.data.rows[0].id, op); assert.equal(f.requests.filter((r) => r.method === 'POST').length, 1);
});
test('an older exact receipt never rolls back a later removal', async () => {
  const f = fixture(), p = f.page(); await p.onShow(); f.lose = true; await p.set(click()); const op = p.data.pending[0].operationId;
  const removal = { ...f.head, id: id(), revisionNumber: 2, isCollected: false }; f.head = removal;
  await p.pendingAction(click(op, 'recover')); assert.equal(f.head.id, removal.id); assert.equal(p.data.rows.length, 0); assert.equal(p.data.target.current.collected, false);
});
test('explicit retry first queries and sends the identical original request, while changing school or account cannot reuse it', async () => {
  const f = fixture(), p = f.page(); await p.onShow(); f.hook = (route, method) => { if (method === 'POST') throw Object.assign(new Error('lost'), { code: 'NETWORK_UNCONFIRMED' }); };
  await p.set(click()); const op = p.data.pending[0].operationId, first = f.requests.find((r) => r.method === 'POST'); f.hook = null; f.missing = true; await p.pendingAction(click(op, 'retry'));
  assert.deepEqual(f.requests.filter((r) => r.method === 'POST')[1], first); assert.equal(p.data.pendingTotal, 0);
  assert.equal(model.pending(f.platform, f.app.configuration, f.session, id()).length, 0);
});
test('new release or closed full review blocks new collection but allows removal of an existing reference', async () => {
  const f = fixture(), p = f.page(); await p.onShow(); await p.set(click()); f.canCollect = false; await p.load(); assert.equal(p.data.target.canCollect, false);
  assert.equal(p.data.rows[0].available, false); await p.set(click()); assert.equal(f.head.isCollected, false); const count = f.requests.length; await p.set(click()); assert.equal(f.requests.length, count);
  f.canCollect = true; f.release = id(); await p.load(); assert.equal(p.data.target.changed, true); assert.equal(p.data.target.canCollect, false);
});
test('CAS refusal preserves the operation and shows the new current state without substituting a new operation', async () => {
  const f = fixture(), p = f.page(); await p.onShow(); f.head = { id: id(), schoolId: school, recipientId: recipient, paperQuestionId: question, revisionNumber: 1, isCollected: true, recordedAtUtc: new Date().toISOString(), sourceAvailable: true, taskTitle: '原测评' };
  await p.set(click()); assert.equal(p.data.pendingTotal, 1); assert.ok(p.data.error.includes('变化')); assert.equal(p.data.target.current.collected, true);
  const op = p.data.pending[0].operationId; await p.pendingAction(click(op, 'abandon')); assert.equal(p.data.pendingTotal, 0); assert.equal(p.data.target.current.collected, true);
});
test('storage quota prevents an untracked mutation', async () => {
  const f = fixture(), p = f.page(); await p.onShow(); f.quota = true; await p.set(click()); assert.equal(f.requests.filter((r) => r.method === 'POST').length, 0); assert.equal(f.head, null);
});
test('metadata pages are bounded to twenty items and pagination does not retrieve question or answer bodies', async () => {
  const f = fixture(), p = f.page('question-collections', { schoolId: school }); f.extraRows = Array.from({ length: 21 }, () => ({ id: id(), schoolId: school, recipientId: recipient, paperQuestionId: id(), revisionNumber: 1, isCollected: true, recordedAtUtc: new Date().toISOString(), sourceAvailable: false, taskTitle: '原任务', answer: 'PRIVATE-ANSWER' }));
  await p.onShow(); assert.equal(p.data.rows.length, 20); assert.equal(p.data.total, 21); await p.page({ currentTarget: { dataset: { direction: 1 } } }); assert.equal(p.data.rows.length, 1); assert.ok(!JSON.stringify(p.data).includes('PRIVATE-ANSWER')); assert.ok(f.requests.every((r) => r.method === 'GET'));
});
test('a wrong school or malformed scope response cannot enter the collection view', async () => {
  const f = fixture(), p = f.page(); f.hook = (route) => route.includes('/my-collection?') ? { schoolId: id(), recipientId: recipient, paperQuestionId: question, current: null, canCollect: true, resultReleaseId: release } : undefined;
  await p.onShow(); assert.equal(p.data.target, null); assert.ok(p.data.error);
  const invalid = f.page('question-collections', { schoolId: school, recipientId: 'bad', questionId: question }); const n = f.requests.length; await invalid.onShow(); assert.equal(f.requests.length, n);
});
test('hiding and permission loss block late responses and clear old references from view', async () => {
  for (const leave of ['hide', 'permission']) {
    const f = fixture(), p = f.page(); let finish; f.hook = (route) => route.includes('/my-collections?') ? new Promise((r) => finish = r) : undefined;
    const load = p.onShow(); while (!finish) await new Promise(setImmediate);
    if (leave === 'hide') p.onHide(); else f.change({ ...f.session.getState(), context: { ...f.session.getState().context, grantedPermissions: [] } });
    finish({ totalCount: 0, items: [] }); await load; assert.equal(p.data.rows.length, 0); assert.equal(p.data.target, null);
  }
});
test('an account switch while a confirmation is open cannot create a command for the next account', async () => {
  const f = fixture(), p = f.page(); await p.onShow(); let confirm; f.modal = (o) => confirm = o; const set = p.set(click()); while (!confirm) await new Promise(setImmediate);
  f.change({ ...f.session.getState(), context: { ...f.session.getState().context, accountUserId: id() } }); confirm.success({ confirm: true }); await set;
  assert.equal(f.requests.filter((r) => r.method === 'POST').length, 0); assert.equal(f.storage.size, 0);
});
test('original-result navigation pins the selected question and does not itself read the result', async () => {
  const f = fixture(), p = f.page(); await p.onShow(); await p.set(click()); const count = f.requests.length; p.open(click(f.head.id)); assert.ok(f.links[0].includes('questionId=' + question)); assert.equal(f.requests.length, count);
});
test('dual-role workspace refresh preserves an explicitly selected role without restoring a role after account change', async () => {
  const f = fixture(); f.change({ ...f.session.getState(), context: { ...f.session.getState().context, workspaces: [{ role: 1, schoolId: school, classes: [] }, { role: 2, schoolId: school, classes: [] }] } });
  const p = f.page('workspace', {}); await p.selectRole({ currentTarget: { dataset: { role: 2 } } }); assert.equal(p.data.selectedRole, 2);
  await p.collections({ currentTarget: { dataset: { schoolId: school } } }); assert.ok(f.links[0].includes('/question-collections/')); assert.equal(p.data.selectedRole, 2);
  f.change({ ...f.session.getState(), generation: 2 }); assert.equal(p.data.selectedRole, null);
});
test('browser pending-key discovery is scoped to the current account prefix', () => {
  const file = path.resolve(__dirname, '../../src/KnowPulse.Blazor.Client/wwwroot/js/education-collections.js');
  const source = fs.readFileSync(file, 'utf8').replace('export function', 'function');
  const result = new vm.Script(source + '\nkeys("current.")').runInNewContext({ sessionStorage: { 'current.one': 'metadata', 'other.private': 'private', 'current.two': 'metadata' } });
  assert.deepEqual(Array.from(result).sort(), ['current.one', 'current.two']);
});
