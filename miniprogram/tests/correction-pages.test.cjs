const test = require('node:test'), assert = require('node:assert/strict'), crypto = require('node:crypto');
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm'), { createRequire } = require('node:module');
const model = require('../utils/correction'), answerStore = require('../utils/answer-store');
const ids = Array.from({ length: 9 }, (_, i) => crypto.createHash('md5').update('correction-fixture-' + i).digest('hex').replace(/(.{8})(.{4})(.{4})(.{4})(.{12})/, '$1-$2-$3-$4-$5'));
const [school, owner, delivery, publication, recipient, question, otherSchool, otherOwner, oldId] = ids;
const now = '2026-10-18T00:00:00Z', fingerprint = 'a'.repeat(64);
const empty = () => ({ originalThinking: '', currentSolution: '', checkingStrategy: '', images: [] });
const field = (name, value) => ({ currentTarget: { dataset: { field: name } }, detail: { value } });
const pick = (id, action) => ({ currentTarget: { dataset: { id, action } } });
function fixture() {
  const context = () => ({ accountUserId: owner, tenantId: null, grantedPermissions: ['KnowPulse.Education.AssessmentParticipate', 'KnowPulse.Education.LearningParticipate'], workspaces: [{ schoolId: school, role: 2 }] });
  let state = { generation: 1, authenticated: true, context: context(), termination: null }; const listeners = new Set(), storage = new Map(), files = new Map(), requests = [], receipts = new Map(), uploaded = new Map(), history = [], links = [], dialogs = [];
  const dto = { id: delivery, schoolId: school, taskId: oldId, publicationId: publication, versionNumber: 1, assessmentRecipientId: recipient, teacherName: '任课老师', className: '一班', subjectName: '数学', title: '重新核对解法', instructions: '说明检查策略', startsAtUtc: now, dueAtUtc: '2026-10-20T00:00:00Z', serverNowUtc: now, availability: 0, workState: 0, currentSubmissionId: null, currentSubmission: null, currentReview: null,
    originalQuestion: { id: question, displayOrder: 1, title: '原题', stem: '2 + 3 = ?', obtainedScore: 8, maxScore: 10, response: { text: '首答原文', selectedOptionKeys: [], blanks: [], images: [] }, options: [], correctAnswers: [], analysis: '已开放解析', scoringDefinition: { referenceAnswer: '5', scoringPoints: [] }, privateSource: 'PRIVATE-SOURCE' } };
  const f = { dto, context, storage, files, requests, receipts, uploaded, history, links, dialogs, lost: false, before: false, breakStorage: false, confirm: true, picked: 'wxfile://picked.png', uploadedCount: 0 };
  const change = (s) => { state = s; for (const fn of listeners) fn(state); };
  const platform = { env: { USER_DATA_PATH: 'wxfile://owned' }, getStorageSync: (k) => storage.get(k), setStorageSync(k, row) { if (f.breakStorage) throw new Error('quota'); storage.set(k, structuredClone(row)); }, removeStorageSync: (k) => storage.delete(k), getStorageInfoSync: () => ({ keys: [...storage.keys()] }),
    getRandomValues(o) { const b = crypto.randomBytes(o.length); o.success({ randomValues: b.buffer.slice(b.byteOffset, b.byteOffset + b.length) }); },
    showModal(o) { dialogs.push(o.content); if (f.modal) f.modal(o); else o.success({ confirm: f.confirm }); },
    chooseMedia(o) { if (f.pickHook) f.pickHook(o); else o.success({ tempFiles: [{ tempFilePath: f.picked }] }); },
    getFileInfo(o) { const b = files.get(o.filePath); if (!b) o.fail({}); else o.success({ size: b.length, digest: crypto.createHash('sha256').update(b).digest('hex') }); },
    getFileSystemManager: () => ({ readFile(o) { const b = files.get(o.filePath); if (!b) o.fail({}); else { const slice = b.subarray(o.position, o.position + o.length); o.success({ data: slice.buffer.slice(slice.byteOffset, slice.byteOffset + slice.length) }); } }, copyFile(o) { if (!files.has(o.srcPath)) o.fail({}); else { files.set(o.destPath, Buffer.from(files.get(o.srcPath))); o.success({}); } }, unlinkSync(p) { if (!files.delete(p)) throw Object.assign(new Error('missing'), { code: 'ENOENT' }); } }),
    enableAlertBeforeUnload() {}, disableAlertBeforeUnload() {}, navigateTo: (o) => links.push(o.url), reLaunch() {}, setClipboardData() {},
  };
  const session = { getState: () => state,
    async refreshWorkspace() { const original = state.context || context(); change({ ...state, context: null }); await Promise.resolve(); change({ ...state, context: structuredClone(original) }); return state; },
    async request(route, method = 'GET', body) {
      requests.push({ route, method, body: body && structuredClone(body) }); if (f.hook) { const result = f.hook(route, method, body); if (result !== undefined) return result; }
      if (route.includes('/my-correction-list?')) return { totalCount: 1, items: [{ ...dto, originalQuestion: null, title: null, instructions: null, currentSubmission: null, currentReview: null }] };
      if (route.includes('/my-correction-for-school?')) return structuredClone(dto);
      if (route.includes('/my-correction-operation?')) return structuredClone(receipts.get(new URL('https://api.invalid' + route).searchParams.get('operationId')) || null);
      if (route.includes('/my-correction-image-operation?')) return structuredClone(uploaded.get(new URL('https://api.invalid' + route).searchParams.get('operationId')) || null);
      if (route.includes('/my-correction-history?')) { const u = new URL('https://api.invalid' + route), head = u.searchParams.get('expectedSubmissionId'); if ((head || null) !== dto.currentSubmissionId) throw Object.assign(new Error('head'), { status: 409 }); return { totalCount: history.length, items: structuredClone(history.slice(Number(u.searchParams.get('SkipCount')), Number(u.searchParams.get('SkipCount')) + 20)) }; }
      if (route.includes('/preview-my-correction/')) {
        if (body.expectedSubmissionId !== dto.currentSubmissionId) throw Object.assign(new Error('head'), { status: 409 });
        if (dto.availability !== 0) throw Object.assign(new Error('closed'), { code: 'CORRECTION_REJECTED', status: 403 });
        return { deliveryId: delivery, expectedSubmissionId: body.expectedSubmissionId, content: structuredClone(body.content), fingerprint, images: body.content.images.map((i) => uploaded.get(i.attachmentId)), dueAtUtc: dto.dueAtUtc, serverNowUtc: now };
      }
      if (route.includes('/submit-my-correction/')) {
        if (f.before) throw Object.assign(new Error('offline'), { code: 'NETWORK_UNCONFIRMED' });
        if (receipts.has(body.operationId)) return structuredClone(receipts.get(body.operationId));
        if (body.expectedSubmissionId !== dto.currentSubmissionId) throw Object.assign(new Error('head'), { status: 409 });
        assert.equal(body.previewFingerprint, fingerprint); assert.equal(body.confirmSharingWithTeacher, true);
        const row = { id: body.operationId, deliveryId: delivery, submissionNumber: receipts.size + 1, basedOnSubmissionId: body.expectedSubmissionId, content: structuredClone(body.content), submittedAtUtc: now, requestFingerprint: fingerprint };
        receipts.set(row.id, row); dto.currentSubmissionId = row.id; dto.currentSubmission = row; dto.currentReview = null; dto.workState = 1; history.unshift({ submission: row, reviews: [] });
        if (f.lost) { f.lost = false; throw Object.assign(new Error('lost'), { code: 'NETWORK_UNCONFIRMED' }); } return structuredClone(row);
      }
      throw new Error('unexpected route ' + route);
    },
    async uploadCorrectionImage(id, op, file) { assert.equal(id, delivery); f.uploadedCount++; const bytes = files.get(file); assert.ok(bytes); const row = { id: op, contentHash: crypto.createHash('sha256').update(bytes).digest('hex'), size: bytes.length, contentType: 'image/png', fileName: file.split('/').pop(), isUploaded: true }; uploaded.set(op, row); if (f.lostUpload) { f.lostUpload = false; throw Object.assign(new Error('lost'), { code: 'NETWORK_UNCONFIRMED' }); } return row; },
    async download(route) { const file = 'wxfile://preview-' + crypto.randomUUID(); files.set(file, Buffer.from([1])); if (f.downloadHook) return f.downloadHook(file); return file; },
    releaseFile: (file) => files.delete(file), portalUrl: (route) => 'https://portal.invalid' + route,
  };
  const app = { session, configuration: { apiBaseUrl: 'https://api.invalid', appKey: 'main', clientId: 'KnowPulse_WeChat_main' }, subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); } };
  function pageFor(name = 'correction', query = {}) {
    const file = path.join(__dirname, '../pages', name, 'index.js'); let definition;
    new vm.Script(fs.readFileSync(file, 'utf8'), { filename: file }).runInNewContext({ require: createRequire(file), Page: (d) => definition = d, wx: platform, getApp: () => app });
    const p = { data: structuredClone(definition.data), setData(v) { assert.ok(Buffer.byteLength(JSON.stringify(v)) < 1024 * 1024); Object.assign(this.data, structuredClone(v)); } };
    for (const [k, v] of Object.entries(definition)) if (typeof v === 'function') p[k] = v.bind(p); p.onLoad({ id: delivery, schoolId: school, ...query }); return p;
  }
  Object.assign(f, { platform, session, app, change, pageFor, page: pageFor() }); return f;
}
const edit = async (f, value = '  当前解法\n原文  ') => { await f.page.allowLocal(); f.page.change(field('currentSolution', value)); f.page.change(field('checkingStrategy', '验算并比较')); };
const share = async (f) => { await f.page.preview(); f.page.sharing({ detail: { value: ['yes'] } }); };
const png = () => Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a/e0AAAAASUVORK5CYII=', 'base64');

test('school list keeps metadata only and opening links to one delivery without provision', async () => {
  const f = fixture(), list = f.pageFor('corrections'); await list.onShow(); assert.equal(list.data.total, 1); assert.ok(!JSON.stringify(list.data).includes('已开放解析')); list.open(pick(delivery)); assert.equal(f.links[0], `/pages/correction/index?schoolId=${school}&id=${delivery}`); assert.equal(f.requests.length, 1); assert.ok(f.requests[0].route.includes('schoolId=' + school));
});
test('explicit exact-school opening strips raw source fields and shares no original body into local storage', async () => {
  const f = fixture(); await f.page.onShow(); assert.equal(f.page.data.detail.question.stem, '2 + 3 = ?'); assert.ok(!JSON.stringify(f.page.data).includes('PRIVATE-SOURCE')); assert.equal(f.storage.size, 0); f.page.original(); assert.equal(f.links[0], `/pages/assessment-result/index?schoolId=${school}&id=${recipient}`);
});
test('consent, exact preview and sharing precede one literal immutable submission, leaving the first answer intact', async () => {
  const f = fixture(); await f.page.onShow(); f.page.change(field('currentSolution', '不得暗存')); assert.equal(f.storage.size, 0); await edit(f); await f.page.submit(); assert.equal(f.receipts.size, 0); await share(f); assert.equal(f.page.data.preview.currentSolution, '  当前解法\n原文  '); await f.page.submit();
  assert.equal(f.receipts.size, 1); assert.equal(f.page.data.cloud.content.currentSolution, '  当前解法\n原文  '); assert.equal(f.page.data.cloud.content.originalThinking, ''); assert.equal(f.dto.originalQuestion.response.text, '首答原文'); assert.equal(f.page.data.historyTotal, 1); assert.equal(f.storage.size, 0);
});
test('edit, rotation and refresh invalidate a prepared sharing snapshot', async () => {
  const f = fixture(); await f.page.onShow(); await edit(f); await share(f); f.page.change(field('checkingStrategy', '重新检查')); assert.equal(f.page.data.preview, null); assert.equal(f.page.data.share, false); await f.page.submit(); assert.equal(f.receipts.size, 0); await share(f); await f.page.load(); assert.equal(f.page.data.preview, null);
});
test('a late confirmation cannot submit after leaving or changing the form', async () => {
  const f = fixture(); await f.page.onShow(); await edit(f); await share(f); let dialog; f.modal = (o) => dialog = o; const running = f.page.submit(); f.page.onHide(); dialog.success({ confirm: true }); await running; assert.equal(f.receipts.size, 0); assert.equal(f.page.data.form, null);
});
test('lost committed submission recovers cold by its original operation without duplicate writes', async () => {
  const f = fixture(); await f.page.onShow(); await edit(f); await share(f); f.lost = true; await f.page.submit(); const op = f.page.data.pending.operationId; assert.equal(f.receipts.size, 1); f.page.onHide(); f.page = f.pageFor(); await f.page.onShow(); assert.equal(f.page.data.consent, false); await f.page.recover(); assert.equal(f.page.data.pending, null); assert.equal(f.page.data.cloud.id, op); assert.equal(f.requests.filter((r) => r.route.includes('/submit-my-correction/')).length, 1);
});
test('explicit retry queries first and resends the same body and id only after an uncommitted loss', async () => {
  const f = fixture(); await f.page.onShow(); await edit(f); await share(f); f.before = true; await f.page.submit(); const first = f.requests.find((r) => r.route.includes('/submit-my-correction/')).body; assert.equal(f.receipts.size, 0); f.before = false; await f.page.retry(); const writes = f.requests.filter((r) => r.route.includes('/submit-my-correction/')); assert.equal(writes.length, 2); assert.deepEqual(writes[1].body, first); assert.ok(f.requests.findIndex((r) => r.route.includes('/my-correction-operation?')) < f.requests.indexOf(writes[1]));
});
test('refresh retains the original submission base and requires explicit comparison before rebase', async () => {
  const f = fixture(); await f.page.onShow(); await edit(f, '本机未上交'); const remote = { id: oldId, deliveryId: delivery, submissionNumber: 1, basedOnSubmissionId: null, content: { ...empty(), currentSolution: '其他设备上交', checkingStrategy: '复核' }, requestFingerprint: fingerprint, submittedAtUtc: now };
  f.dto.currentSubmissionId = oldId; f.dto.currentSubmission = remote; f.history.push({ submission: remote, reviews: [] }); await f.page.load(); assert.equal(f.page.local.base, null); assert.equal(f.page.data.conflict, true); assert.equal(f.page.data.form.currentSolution, '本机未上交'); await f.page.preview(); assert.equal(f.page.data.preview, null); await f.page.rebase(); assert.equal(f.page.local.base, oldId); assert.equal(f.page.data.form.currentSolution, '本机未上交'); await share(f); await f.page.submit(); assert.equal(f.page.data.cloud.basedOn, oldId);
});
test('quota failures retain page input and prevent an untracked submission', async () => {
  const f = fixture(); await f.page.onShow(); await edit(f); f.breakStorage = true; f.page.change(field('currentSolution', '尚未成功暂存')); assert.equal(f.page.data.unpersisted, true); assert.equal(f.page.data.form.currentSolution, '尚未成功暂存'); await f.page.preview(); assert.equal(f.page.data.preview, null); f.breakStorage = false; f.page.saveLocal(); await share(f); f.breakStorage = true; await f.page.submit(); assert.equal(f.receipts.size, 0); assert.equal(f.requests.filter((r) => r.route.includes('/submit-my-correction/')).length, 0);
});
test('image upload remains private until selected, previewed and submitted; rotation never changes bytes', async () => {
  const f = fixture(); f.files.set(f.picked, png()); await f.page.onShow(); await edit(f); await f.page.upload(); assert.equal(f.receipts.size, 0); assert.equal(f.page.data.form.images.length, 1); assert.equal([...f.files.keys()].filter((p) => p.startsWith('wxfile://owned')).length, 0);
  const id = f.page.data.form.images[0].attachmentId; f.page.editImage(pick(id, 'rotate')); assert.equal(f.page.data.form.images[0].rotationDegrees, 90); await share(f); await f.page.submit(); assert.equal(f.page.data.cloud.content.images[0].rotationDegrees, 90); assert.deepEqual(f.files.get(f.picked), png());
});
test('lost image confirmation is queried cold without uploading or attaching twice', async () => {
  const f = fixture(); f.files.set(f.picked, png()); await f.page.onShow(); await edit(f); f.lostUpload = true; await f.page.upload(); const id = f.page.data.pending.operationId; assert.equal(f.uploadedCount, 1); f.page.onHide(); f.page = f.pageFor(); await f.page.onShow(); await f.page.recover(); assert.equal(f.uploadedCount, 1); assert.equal(f.page.data.form.images.filter((i) => i.attachmentId === id).length, 1); assert.equal(f.page.data.pending, null);
});
test('wrong reselected original bytes cannot be retried as the pending image', async () => {
  const f = fixture(); f.files.set(f.picked, png()); await f.page.onShow(); await edit(f); f.session.uploadCorrectionImage = async () => { throw Object.assign(new Error('offline'), { code: 'NETWORK_UNCONFIRMED' }); }; await f.page.upload(); const op = f.page.data.pending.operationId;
  for (const key of [...f.files.keys()]) if (key.startsWith('wxfile://owned')) f.files.delete(key); const other = Buffer.concat([png(), Buffer.from([1])]); f.files.set(f.picked, other); await f.page.reselect(); assert.ok(f.page.data.error.includes('不同')); assert.equal(f.page.data.pending.operationId, op); assert.equal(f.uploadedCount, 0);
});
test('a late selected image cannot start an upload after hiding', async () => {
  const f = fixture(); await f.page.onShow(); await edit(f); let pick; f.pickHook = (o) => pick = o; const running = f.page.upload(); f.page.onHide(); pick.success({ tempFiles: [{ tempFilePath: f.picked }] }); await running; assert.equal(f.uploadedCount, 0); assert.equal(f.page.data.pending, null);
});
test('a late image download is released and cannot reopen a hidden preview', async () => {
  const f = fixture(); f.files.set(f.picked, png()); await f.page.onShow(); await edit(f); await f.page.upload(); const id = f.page.data.form.images[0].attachmentId; let finish, file; f.downloadHook = (p) => { file = p; return new Promise((r) => finish = r); }; const running = f.page.image(pick(id)); while (!finish) await new Promise(setImmediate); f.page.onHide(); finish(file); await running; assert.equal(f.page.data.imagePath, ''); assert.ok(!f.files.has(file));
});
test('withheld source hides original question and teacher advice but retains owned handed-in history', async () => {
  const f = fixture(); await f.page.onShow(); await edit(f); await share(f); await f.page.submit(); const submitted = f.dto.currentSubmissionId; const review = { id: crypto.randomUUID(), deliveryId: delivery, submissionId: submitted, decision: 1, studentFeedback: 'PRIVATE-UNTIL-OPEN', publishedAtUtc: now }; f.dto.currentReview = review; f.history[0].reviews = [review]; f.dto.availability = 5; await f.page.load(); assert.equal(f.page.data.detail.question, null); assert.equal(f.page.data.detail.review, null); assert.equal(f.page.data.detail.canSubmit, false); assert.equal(f.page.data.cloud.content.currentSolution, '  当前解法\n原文  '); assert.ok(!JSON.stringify(f.page.data).includes('PRIVATE-UNTIL-OPEN'));
  f.dto.availability = 0; await f.page.load(); assert.equal(f.page.data.detail.review.submissionId, submitted); assert.equal(f.page.data.history[0].reviews[0].feedback, 'PRIVATE-UNTIL-OPEN');
});
test('current review must belong to the exact submitted copy', () => { const f = fixture(); const current = { id: oldId, deliveryId: delivery, submissionNumber: 1, content: { ...empty(), currentSolution: '解法', checkingStrategy: '验算' }, requestFingerprint: fingerprint, submittedAtUtc: now }; f.dto.currentSubmissionId = oldId; f.dto.currentSubmission = current; f.dto.currentReview = { id: crypto.randomUUID(), deliveryId: delivery, submissionId: question, decision: 1, studentFeedback: '旧副本意见', publishedAtUtc: now }; assert.throws(() => model.card(f.dto, school, true), { code: 'INVALID_RESPONSE' }); });
test('true authority loss clears private local content and request bodies, retaining only recoverable references', async () => {
  const f = fixture(); await f.page.onShow(); await edit(f, 'PRIVATE-DRAFT'); await share(f); f.before = true; await f.page.submit(); const op = f.page.data.pending.operationId; f.change({ ...f.session.getState(), generation: 2, context: null, termination: 'forbidden' }); assert.equal(f.page.data.form, null); assert.ok(!JSON.stringify([...f.storage.values()]).includes('PRIVATE-DRAFT')); await f.page.load(); assert.equal(f.page.data.pending.operationId, op); assert.equal(f.page.data.pending.canRetry, false); assert.equal(f.page.data.form, null);
});
test('school/actor isolation and role loss never restore another delivery draft', async () => {
  const f = fixture(); await f.page.onShow(); await edit(f, 'SCHOOL-PRIVATE'); f.page.onHide(); f.change({ ...f.session.getState(), context: { ...f.context(), workspaces: [{ schoolId: otherSchool, role: 2 }] } }); f.page = f.pageFor('correction', { schoolId: otherSchool }); await f.page.onShow(); assert.equal(f.page.data.form, null); assert.equal(f.page.data.detail, null); assert.ok(f.page.data.error);
});
test('session logout cleanup also removes correction drafts and copied originals under the shared account prefix', async () => {
  const f = fixture(); f.files.set(f.picked, png()); await f.page.onShow(); await edit(f, 'PRIVATE-LOCAL'); f.session.uploadCorrectionImage = async () => { throw Object.assign(new Error('offline'), { code: 'NETWORK_UNCONFIRMED' }); }; await f.page.upload(); const op = f.page.data.pending.operationId;
  assert.equal(answerStore.clearAccount(f.platform, f.app.configuration, f.context()), true); assert.ok(!JSON.stringify([...f.storage.values()]).includes('PRIVATE-LOCAL')); assert.equal(f.page.store.pending().operationId, op); assert.equal(f.page.store.draft(), null); assert.equal([...f.files.keys()].filter((p) => p.startsWith('wxfile://owned')).length, 0);
});
test('expired draft and pending submission body are removed on next use without inventing a retry body', async () => {
  const f = fixture(); await f.page.onShow(); await edit(f, 'EXPIRED-PRIVATE'); await share(f); f.before = true; await f.page.submit(); const op = f.page.data.pending.operationId; f.page.onHide();
  for (const [key, row] of f.storage) f.storage.set(key, { ...row, createdAt: Date.now() - 25 * 60 * 60 * 1000 });
  f.page = f.pageFor(); await f.page.onShow(); assert.equal(f.page.data.form, null); assert.equal(f.page.data.pending.operationId, op); assert.equal(f.page.data.pending.canRetry, false); assert.ok(!JSON.stringify([...f.storage.values()]).includes('EXPIRED-PRIVATE')); await f.page.recover(); assert.equal(f.receipts.size, 0);
});
test('an expired unconfirmed image can retain the same original again without immediately expiring its new copy', async () => {
  const f = fixture(); f.files.set(f.picked, png()); await f.page.onShow(); await edit(f); const upload = f.session.uploadCorrectionImage;
  f.session.uploadCorrectionImage = async () => { throw Object.assign(new Error('offline'), { code: 'NETWORK_UNCONFIRMED' }); }; await f.page.upload(); const op = f.page.data.pending.operationId; f.page.onHide();
  for (const [key, row] of f.storage) f.storage.set(key, { ...row, createdAt: Date.now() - 25 * 60 * 60 * 1000, cachedAt: Date.now() - 25 * 60 * 60 * 1000 });
  f.page = f.pageFor(); await f.page.onShow(); assert.equal(f.page.data.form, null); await f.page.allowLocal(); f.session.uploadCorrectionImage = upload; await f.page.reselect();
  assert.equal(f.uploadedCount, 1); assert.equal(f.page.data.pending, null); assert.equal(f.page.data.form.images[0].attachmentId, op); assert.equal(f.page.data.form.currentSolution, '');
});
test('late reselection copy after authority loss is cleaned without sending the former upload', async () => {
  const f = fixture(); f.files.set(f.picked, png()); await f.page.onShow(); await edit(f); f.session.uploadCorrectionImage = async () => { throw Object.assign(new Error('offline'), { code: 'NETWORK_UNCONFIRMED' }); }; await f.page.upload();
  for (const key of [...f.files.keys()]) if (key.startsWith('wxfile://owned')) f.files.delete(key);
  const original = f.platform.getFileSystemManager; let copy;
  f.platform.getFileSystemManager = () => ({ ...original(), copyFile(o) { copy = o; } });
  const running = f.page.reselect(); while (!copy) await new Promise(setImmediate);
  f.change({ ...f.session.getState(), generation: 2, context: null, termination: 'forbidden' }); f.files.set(copy.destPath, png()); copy.success({}); await running;
  assert.equal([...f.files.keys()].filter((p) => p.startsWith('wxfile://owned')).length, 0); assert.equal(f.uploadedCount, 0); assert.equal(f.page.data.form, null);
});
test('superseded and withdrawn deliveries keep owned records but cannot reveal the old original question or execute', async () => {
  for (const availability of [1, 2, 3, 4, 5, 6, 7]) { const f = fixture(); f.dto.availability = availability; await f.page.onShow(); assert.equal(f.page.data.detail.canSubmit, false); assert.equal(!!f.page.data.detail.question, availability === 2); await f.page.allowLocal(); f.page.change(field('currentSolution', '不可发送')); assert.equal(f.page.data.form.currentSolution, ''); assert.equal(f.requests.filter((r) => r.method === 'POST').length, 0); }
});
