const { test } = require('node:test'), assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), vm = require('node:vm'), crypto = require('node:crypto'), { createRequire } = require('node:module');
const model = require('../utils/practice'), createStore = require('../utils/practice-store'), answers = require('../utils/answer-store');
const guid = () => crypto.randomUUID(), school = guid(), recipient = guid(), question = guid(), release = guid(), account = guid(), round = guid(), blank = guid();
const now = '2026-10-18T06:00:00Z', hash = 'a'.repeat(64), pick = (id, action) => ({ currentTarget: { dataset: { id, action } } }), input = (value) => ({ detail: { value } });
function fixture() {
  const identity = () => ({ accountUserId: account, tenantId: null, grantedPermissions: ['KnowPulse.Education.AssessmentParticipate', 'KnowPulse.Education.LearningParticipate'], workspaces: [{ schoolId: school, role: 2, classes: [] }] });
  let state = { generation: 1, authenticated: true, context: identity() }; const listeners = new Set(), storage = new Map(), files = new Map(), requests = [], receipts = new Map(), uploads = new Map(), links = [];
  const base = { id: round, schoolId: school, recipientId: recipient, paperQuestionId: question, startedAtUtc: now, submittedAtUtc: null, submissionOperationId: null, sourceAvailable: true,
    question: { id: question, type: 4, title: '同一冻结题目', stem: '独立说明解法', options: [{ optionKey: 'A', content: '选项甲' }, { optionKey: 'B', content: '选项乙' }], responseFields: [{ id: blank, stemMarker: '【1】' }], score: 10, privateSource: 'PRIVATE-TEACHER' }, submittedResponse: null, feedback: null, ownImages: [], privateProvenance: 'PRIVATE-TEACHER' };
  const rounds = new Map([[round, base]]), f = { base, identity, storage, files, requests, receipts, uploads, links, rounds, confirm: true, quota: false, source: true, release, lostStart: false, beforeStart: false, lostSubmit: false, beforeSubmit: false, lostImage: false, rejectSubmit: false, chosen: 'wxfile://picked.png', imageUploads: 0, exposureReads: 0 };
  const change = (s) => { state = s; for (const fn of listeners) fn(s); };
  const platform = { env: { USER_DATA_PATH: 'wxfile://owned' }, getStorageSync: (k) => storage.get(k), setStorageSync(k, r) { if (f.quota) throw new Error('quota'); storage.set(k, structuredClone(r)); }, removeStorageSync: (k) => storage.delete(k), getStorageInfoSync: () => ({ keys: [...storage.keys()] }),
    getRandomValues(o) { const b = crypto.randomBytes(16); o.success({ randomValues: b.buffer.slice(b.byteOffset, b.byteOffset + b.length) }); },
    showModal(o) { if (f.modal) f.modal(o); else o.success({ confirm: f.confirm }); }, chooseMedia: (o) => o.success({ tempFiles: [{ tempFilePath: f.chosen }] }),
    getFileInfo(o) { const b = files.get(o.filePath); if (!b) o.fail({}); else o.success({ size: b.length, digest: crypto.createHash('sha256').update(b).digest('hex') }); },
    getFileSystemManager: () => ({ readFile(o) { const b = files.get(o.filePath); if (!b) o.fail({}); else { const v = b.subarray(o.position, o.position + o.length); o.success({ data: v.buffer.slice(v.byteOffset, v.byteOffset + v.length) }); } }, copyFile(o) { if (!files.has(o.srcPath)) o.fail({}); else { files.set(o.destPath, Buffer.from(files.get(o.srcPath))); o.success({}); } }, unlinkSync(p) { if (f.unlinkBlocked) throw Object.assign(new Error('blocked'), { code: 'EACCES' }); if (!files.delete(p)) throw Object.assign(new Error('missing'), { code: 'ENOENT' }); } }),
    navigateTo(o) { links.push(o.url); }, setClipboardData(o) { f.clipboard = o.data; }, enableAlertBeforeUnload() {}, disableAlertBeforeUnload() {}, reLaunch() {},
  };
  const operation = (id, op, submitted) => ({ practiceId: id, operationId: op, isSubmission: submitted, requestFingerprint: hash, recordedAtUtc: now });
  const session = { getState: () => state, async refreshWorkspace() { const context = state.context || identity(); change({ ...state, context: null }); await Promise.resolve(); change({ ...state, context: structuredClone(context) }); return state; },
    async request(route, method = 'GET', body) {
      requests.push({ route, method, body: body && structuredClone(body) }); if (f.hook) { const value = f.hook(route, method, body); if (value !== undefined) return value; }
      const u = new URL('https://api.invalid' + route), p = u.searchParams;
      if (route.includes('/my-practice-list?')) { const all = p.get('SchoolId') === school ? (f.extraRows || [...rounds.values()].filter((r) => !p.get('RecipientId') || r.recipientId === p.get('RecipientId') && r.paperQuestionId === p.get('PaperQuestionId'))) : []; return { totalCount: all.length, items: structuredClone(all.slice(Number(p.get('SkipCount')), Number(p.get('SkipCount')) + 20)) }; }
      if (route.includes('/my-practice-start-operation?')) return structuredClone(receipts.get(p.get('operationId')) || null);
      if (route.includes('/start-my-practice-for-school?')) {
        if (f.beforeStart) throw model.error('NETWORK_UNCONFIRMED');
        if (p.get('schoolId') !== school || body.expectedResultReleaseId !== f.release || !f.source) throw Object.assign(model.error('PRACTICE_REJECTED'), { status: 403 });
        if (receipts.has(body.operationId)) return structuredClone(receipts.get(body.operationId));
        rounds.set(body.operationId, { ...structuredClone(base), id: body.operationId }); const r = operation(body.operationId, body.operationId, false); receipts.set(r.operationId, r);
        if (f.lostStart) { f.lostStart = false; throw model.error('NETWORK_UNCONFIRMED'); } return r;
      }
      if (route.includes('/my-practice-for-school?')) { const r = structuredClone(rounds.get(p.get('practiceId'))); assert.ok(r); r.sourceAvailable = f.source;
        r.ownImages = [...uploads.values()]; if (!f.source) { r.question = null; r.feedback = null; } else if (r.submittedAtUtc) f.exposureReads++;
        return r; }
      const match = u.pathname.match(/education-assessment\/([^/]+)\/(.*)/); if (!match) throw new Error('Unexpected route ' + route);
      const [, id, action] = match, r = rounds.get(id); assert.ok(r);
      if (action.startsWith('my-practice-operation/')) return structuredClone(receipts.get(action.split('/')[1]) || null);
      if (action.startsWith('my-practice-image-operation/')) return structuredClone(uploads.get(action.split('/')[1]) || null);
      if (action === 'preview-my-practice') { if (!f.source) throw Object.assign(model.error('PRACTICE_REJECTED'), { status: 403 }); return { fingerprint: hash, serverNowUtc: now }; }
      if (action === 'submit-my-practice') {
        if (f.beforeSubmit) throw model.error('NETWORK_UNCONFIRMED'); if (f.rejectSubmit) throw Object.assign(model.error('PRACTICE_REJECTED'), { status: 409 });
        if (receipts.has(body.operationId)) return structuredClone(receipts.get(body.operationId));
        assert.equal(body.confirmSharingWithTeacher, true); assert.equal(body.confirmPracticeOnly, true); assert.equal(body.preparationFingerprint, hash);
        r.submissionOperationId = body.operationId; r.submittedAtUtc = now; r.submittedResponse = structuredClone(body.response); r.feedback = { correctAnswers: ['REFERENCE-ANSWER'], analysis: 'REFERENCE-ANALYSIS', scoringDefinition: { referenceAnswer: 'REFERENCE-CRITERIA', scoringPoints: [], blanks: [] }, evaluation: { maxScore: 10, obtainedScore: r.question.type === 4 ? null : 10, parts: [] } };
        const op = operation(id, body.operationId, true); receipts.set(op.operationId, op); if (f.lostSubmit) { f.lostSubmit = false; throw model.error('NETWORK_UNCONFIRMED'); } return op;
      }
      throw new Error('Unexpected route ' + route);
    },
    async uploadPracticeImage(id, op, file) { assert.ok(rounds.has(id)); const b = files.get(file); assert.ok(b); f.imageUploads++;
      const r = { id: op, contentHash: crypto.createHash('sha256').update(b).digest('hex'), contentType: 'image/png', size: b.length, fileName: file.split('/').pop(), isUploaded: true }; uploads.set(op, r); if (f.lostImage) { f.lostImage = false; throw model.error('NETWORK_UNCONFIRMED'); } return r; },
    async download(route) { assert.match(route, /^\/api\/education\/practices\//); const file = 'wxfile://preview-' + guid(); files.set(file, Buffer.from([1])); if (f.downloadHook) return f.downloadHook(file); return file; },
    releaseFile: (file) => files.delete(file), portalUrl: (route) => 'https://portal.invalid' + route,
  };
  const app = { session, configuration: { apiBaseUrl: 'https://api.invalid', appKey: 'main', clientId: 'KnowPulse_WeChat_main' }, subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); } };
  function pageFor(name = 'practice', query = {}) { const file = path.join(__dirname, '../pages', name, 'index.js'); let definition;
    new vm.Script(fs.readFileSync(file, 'utf8'), { filename: file }).runInNewContext({ require: createRequire(file), Page: (r) => definition = r, wx: platform, getApp: () => app });
    const page = { data: structuredClone(definition.data), setData(v) { assert.ok(Buffer.byteLength(JSON.stringify(v)) < 1024 * 1024); Object.assign(this.data, structuredClone(v)); } };
    for (const [k, v] of Object.entries(definition)) if (typeof v === 'function') page[k] = v.bind(page); page.onLoad({ id: round, schoolId: school, ...query }); return page;
  }
  return Object.assign(f, { platform, session, app, change, pageFor });
}
const target = { recipientId: recipient, questionId: question, releaseId: release };
async function edit(p, text = '  本轮自写解法\n保留原文  ') { await p.allowLocal(); p.text(input(text)); }
async function prepare(p) { await p.prepare(); p.sharing(input(['share', 'practice'])); }
const png = () => Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a/e0AAAAASUVORK5CYII=', 'base64');

test('practice school list is bounded metadata and start needs explicit confirmation with no feedback read', async () => {
  const f = fixture(), p = f.pageFor('practices', target); await p.onShow(); assert.equal(p.data.rows.length, 1); assert.ok(!JSON.stringify(p.data).includes('PRIVATE-TEACHER'));
  f.confirm = false; await p.start(); assert.equal(f.rounds.size, 1); f.confirm = true; await p.start(); assert.equal(f.rounds.size, 2); assert.equal(f.exposureReads, 0); assert.match(f.links.at(-1), /pages\/practice\/index/);
});
test('a lost start response survives a cold page and recovers once without starting another round', async () => {
  const f = fixture(), p = f.pageFor('practices', target); await p.onShow(); f.lostStart = true; await p.start(); const op = p.data.pending.operationId; p.onUnload();
  const hub = f.pageFor('practices'); await hub.onShow(); assert.equal(hub.data.pendingTotal, 1); hub.manage(pick(op)); assert.ok(f.links.at(-1).includes('questionId=' + question));
  const cold = f.pageFor('practices', target); await cold.onShow(); await cold.recover(); assert.equal(f.rounds.size, 2); assert.ok(f.links.at(-1).includes('id=' + op)); assert.equal(f.requests.filter((r) => r.route.includes('/start-my-practice-for-school?')).length, 1);
});
test('start retry queries first and resends the identical original operation and release', async () => {
  const f = fixture(), p = f.pageFor('practices', target); await p.onShow(); f.beforeStart = true; await p.start(); const original = f.requests.at(-1).body; f.beforeStart = false; await p.retry();
  const commands = f.requests.filter((r) => r.route.includes('/start-my-practice-for-school?')); assert.deepEqual(commands[1].body, original); assert.ok(f.requests.some((r) => r.route.includes('my-practice-start-operation?')));
});
test('unsubmitted detail has no reference feedback and own drafts require local consent', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); p.text(input('ignored')); assert.equal(p.data.form, null); await edit(p); assert.equal(p.data.form.value.text, '  本轮自写解法\n保留原文  ');
  assert.ok(!JSON.stringify(p.data).includes('PRIVATE-TEACHER')); assert.ok(!JSON.stringify([...f.storage.values()]).includes('REFERENCE-')); assert.ok(!JSON.stringify([...f.storage.values()]).includes('独立说明解法'));
  p.web(); assert.equal(f.clipboard, `https://portal.invalid/education/practice?practiceId=${round}`); assert.match(p.data.notice, /不会自动同步/);
});
test('five frozen response shapes use stable option and blank identities', async () => {
  for (const type of [0, 1, 2, 3, 4]) {
    const f = fixture(); f.base.question.type = type; const p = f.pageFor(); await p.onShow(); await p.allowLocal();
    if (type < 3) p.choices(input(type === 1 ? ['A', 'B'] : type === 2 ? 'true' : 'A'));
    if (type === 3) p.blank({ ...input('填空原文'), currentTarget: { dataset: { id: blank } } }); if (type === 4) p.text(input('主观解法'));
    await prepare(p); await p.submit(); const response = f.base.submittedResponse; assert.equal(response.type, type); assert.equal(f.exposureReads, 0);
    if (type === 3) assert.equal(response.blanks[0].blankId, blank); if (type === 4) assert.equal(response.text, '主观解法');
  }
});
test('editing invalidates preview and both sharing confirmations; sealing does not automatically read feedback', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); await prepare(p); p.text(input('改后的本轮解法')); assert.equal(p.data.preview, null); assert.equal(p.data.share, false);
  await p.submit(); assert.equal(f.base.submittedAtUtc, null); await prepare(p); p.sharing(input(['share'])); await p.submit(); assert.equal(f.base.submittedAtUtc, null);
  p.sharing(input(['share', 'practice'])); await p.submit(); assert.ok(p.data.receipt); assert.equal(p.data.detail, null); assert.equal(f.exposureReads, 0); assert.ok(!JSON.stringify(p.data).includes('REFERENCE-'));
  await p.load(); assert.equal(p.data.detail.feedback.total, '—'); assert.equal(f.exposureReads, 1); assert.ok(JSON.stringify(p.data).includes('REFERENCE-ANSWER'));
});
test('lost submission recovers the exact receipt after process loss without replay or reference exposure', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); await prepare(p); f.lostSubmit = true; await p.submit(); const op = p.data.pending.operationId; p.onUnload();
  const cold = f.pageFor(); await cold.onShow(); assert.equal(f.exposureReads, 0); assert.equal(cold.data.detail, null); await cold.recover(); assert.equal(cold.data.receipt, op); assert.equal(f.exposureReads, 0); assert.equal(f.requests.filter((r) => /\/submit-my-practice$/.test(r.route)).length, 1);
});
test('unknown unsaved submission retry keeps identical body and queries before sending', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); await prepare(p); f.beforeSubmit = true; await p.submit(); const original = f.requests.at(-1).body;
  f.beforeSubmit = false; await p.retry(); const posts = f.requests.filter((r) => /\/submit-my-practice$/.test(r.route)); assert.deepEqual(posts[1].body, original); assert.ok(f.requests.some((r) => /\/my-practice-operation\//.test(r.route)));
});
test('quota failure preserves memory and prevents untracked starts and submissions', async () => {
  const f = fixture(), hub = f.pageFor('practices', target); await hub.onShow(); f.quota = true; await hub.start(); assert.equal(f.rounds.size, 1);
  f.quota = false; const p = f.pageFor(); await p.onShow(); await edit(p); await prepare(p); f.quota = true; await p.submit(); assert.equal(f.base.submittedAtUtc, null); assert.equal(p.local.text, '  本轮自写解法\n保留原文  ');
  f.platform.getStorageSync = () => { throw new Error('storage read'); }; await hub.action(async () => null); assert.match(hub.data.error, /无法保留操作标识/);
});
test('source closure hides reference material but retains the own submitted copy and images', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); await prepare(p); await p.submit(); f.source = false; await p.load();
  assert.equal(p.data.detail.question, null); assert.equal(p.data.detail.feedback, null); assert.equal(p.data.detail.response.text, '  本轮自写解法\n保留原文  '); assert.equal(p.data.form, null); assert.equal(f.exposureReads, 0);
});
test('a new release cannot silently replace the chosen start source', async () => {
  const f = fixture(), p = f.pageFor('practices', target); await p.onShow(); f.release = guid(); await p.start(); assert.ok(p.data.pending); assert.equal(f.rounds.size, 1); assert.equal(f.requests.at(-1).body.expectedResultReleaseId, release);
});
test('CAS refusal keeps the own draft, original operation and prepared request for receipt lookup', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); await prepare(p); f.rejectSubmit = true; await p.submit(); const op = p.data.pending.operationId;
  assert.match(p.data.error, /原草稿/); assert.equal(p.local.text, '  本轮自写解法\n保留原文  '); await p.recover(); assert.equal(p.data.pending.operationId, op);
});
test('a confirmed original image enters only the own draft and preserves rotation and order', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); f.files.set(f.chosen, png()); await p.addImage(); const id = p.local.images[0].attachmentId;
  assert.equal(f.base.submittedAtUtc, null); assert.equal(f.imageUploads, 1); assert.equal(f.files.has(answers.imagePath(f.platform, id, 'png')), false); p.editImage(pick(id, 'rotate')); assert.equal(p.local.images[0].rotationDegrees, 90);
  await prepare(p); await p.submit(); assert.equal(f.base.submittedResponse.images[0].rotationDegrees, 90); assert.equal(f.exposureReads, 0);
});
test('lost upload recovers without a second upload and reselect rejects a different original', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); f.files.set(f.chosen, png()); f.lostImage = true; await p.addImage(); const op = p.data.pending.operationId;
  const copy = answers.imagePath(f.platform, op, 'png'); assert.ok(f.files.has(copy)); f.files.set(f.chosen, Buffer.concat([png(), Buffer.from([1])])); await p.reselectImage(); assert.match(p.data.error, /原操作/); assert.equal(p.data.pending.operationId, op);
  await p.recover(); assert.equal(f.imageUploads, 1); assert.equal(p.local.images[0].attachmentId, op); assert.equal(f.files.has(copy), false);
});
test('late downloads and hidden responses cannot restore former question or image views', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); f.files.set(f.chosen, png()); await p.addImage(); const image = p.local.images[0].attachmentId;
  let finish, path; f.downloadHook = (file) => { path = file; return new Promise((resolve) => finish = () => resolve(file)); };
  const loading = p.image(pick(image)); await new Promise(setImmediate); p.onHide(); finish(); await loading; assert.equal(p.data.imagePath, ''); assert.equal(p.data.detail, null); assert.equal(f.files.has(path), false);
});
test('an account switch during confirmation cannot begin or submit under the next account', async () => {
  const f = fixture(), p = f.pageFor('practices', target); await p.onShow(); f.modal = (o) => { f.change({ ...f.session.getState(), generation: 2, context: { ...f.identity(), accountUserId: guid() } }); o.success({ confirm: true }); };
  await p.start(); assert.equal(f.rounds.size, 1); assert.equal(f.requests.filter((r) => r.method === 'POST').length, 0);
  const second = fixture(), detail = second.pageFor(); await detail.onShow(); await edit(detail); await prepare(detail);
  second.modal = (o) => { second.change({ ...second.session.getState(), generation: 2, context: { ...second.identity(), accountUserId: guid() } }); o.success({ confirm: true }); };
  await detail.submit(); assert.equal(second.base.submittedAtUtc, null); assert.equal(second.requests.filter((r) => r.route.endsWith('/submit-my-practice')).length, 0);
});
test('permission loss clears private drafts and pending payload while preserving operation identity', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); await prepare(p); f.beforeSubmit = true; await p.submit(); const op = p.data.pending.operationId;
  f.change({ ...f.session.getState(), context: { ...f.identity(), grantedPermissions: [] } }); assert.equal(p.data.form, null); assert.equal(p.local, null); assert.equal(p.store.draft(), null); assert.equal(p.store.pending().payload, null); assert.equal(p.store.pending().operationId, op);
});
test('draft and request TTL keep body-free recovery after expiry and logout', () => {
  const f = fixture(); let clock = Date.now(); const store = createStore(f.platform, f.app.configuration, f.identity(), school, round, () => clock);
  const value = { ...model.empty(4), text: 'SELF-WRITTEN' }, op = guid(); store.saveDraft(value); store.begin({ kind: 'submit', operationId: op, payload: { operationId: op, response: value } }); clock += 86400001;
  assert.equal(store.draft(), null); assert.equal(store.pending().payload, null); assert.equal(store.pending().operationId, op);
  store.acknowledge(op); store.saveDraft(value); store.begin({ kind: 'submit', operationId: op, payload: { operationId: op, response: value } }); assert.equal(answers.clearAccount(f.platform, f.app.configuration, f.identity()), true);
  assert.equal(store.draft(), null); assert.equal(store.pending().payload, null); assert.ok(!JSON.stringify([...f.storage.values()]).includes('SELF-WRITTEN'));
});
test('unconfirmed cleanup hides private content and warns while retaining a recoverable image operation', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); f.files.set(f.chosen, png()); f.lostImage = true; await p.addImage(); const op = p.data.pending.operationId, copy = answers.imagePath(f.platform, op, 'png');
  f.unlinkBlocked = true; f.change({ ...f.session.getState(), context: { ...f.identity(), grantedPermissions: [] } });
  assert.equal(p.data.form, null); assert.equal(p.local, null); assert.match(p.data.error, /清理未确认/); assert.ok(f.files.has(copy)); assert.throws(() => p.store.pending(), { code: 'LOCAL_CLEANUP_UNCONFIRMED' });
  assert.equal([...f.storage.values()].filter((r) => r.kind === 'image' && r.operationId === op).length, 1);
  f.unlinkBlocked = false; assert.ok(answers.clearAccount(f.platform, f.app.configuration, f.identity())); assert.equal(f.files.has(copy), false); assert.equal(p.store.pending().operationId, op);
});
test('wrong school, malformed records and oversized metadata pages fail closed', async () => {
  const f = fixture(), p = f.pageFor('practice', { schoolId: guid() }); await p.onShow(); assert.equal(p.data.detail, null);
  f.extraRows = Array.from({ length: 40 }, () => ({ ...structuredClone(f.base), id: guid() })); const hub = f.pageFor('practices'); await hub.onShow(); assert.equal(hub.data.rows.length, 20); hub.open(pick(hub.data.rows[0].id)); assert.match(f.links.at(-1), /pages\/practice\/index/);
  f.hook = (route) => route.includes('/my-practice-list?') ? { totalCount: 21, items: Array.from({ length: 21 }, () => f.base) } : undefined; await hub.load(); assert.equal(hub.data.rows.length, 0); assert.ok(hub.data.error);
});
