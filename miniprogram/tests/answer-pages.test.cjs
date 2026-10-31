const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { createRequire } = require('node:module');
const id = (n) => `${String(n).padStart(8, '0')}-2222-2222-2222-222222222222`;
const recipient = id(1), attemptId = id(2), revision = id(3), schoolId = id(4), userId = id(5);
const configuration = { apiBaseUrl: 'https://api.example.invalid', appKey: 'main', clientId: 'KnowPulse_WeChat_main' };
const clone = (v) => JSON.parse(JSON.stringify(v));
const response = (type, text = null) => ({ schemaVersion: 1, type, selectedOptionKeys: [], blanks: [], text, images: [] });
function fixture(types = [4]) {
  const storage = new Map(); const files = new Map(); const operations = new Map(); const uploads = new Map(); const listeners = new Set(); const requests = []; const dialogs = [];
  const f = { lostSave: false, lostImage: false, quota: false, confirm: true, pick: null, delayedPick: null, modal: null, mounts: [], transfers: [], saveNumber: 0, loseSaveNumber: 0 };
  const context = () => ({ accountUserId: userId, tenantId: null, grantedPermissions: ['KnowPulse.Education.AssessmentParticipate'], workspaces: [{ role: 2, schoolId, classes: [] }] });
  let state = { generation: 1, authenticated: true, context: context(), termination: null };
  const task = { id: recipient, taskId: id(8), schoolId, taskRevisionId: revision, currentAttemptId: null, canStart: true, title: '合成测评', instructions: '明确开始才计时', subjectName: '数学', schoolClassName: '一班', durationMinutes: 10,
    startsAtUtc: new Date(Date.now() - 60000).toISOString(), endsAtUtc: new Date(Date.now() + 600000).toISOString(), serverNowUtc: new Date().toISOString(), questionCount: types.length };
  const dto = { id: attemptId, recipientId: recipient, taskRevisionId: revision, paperSnapshotVersion: 1, responseSetVersion: 0, status: 0, submissionId: null,
    expiresAtUtc: task.endsAtUtc, serverNowUtc: task.serverNowUtc, responses: [], paper: { questionCount: types.length, questions: types.map((type, i) => ({ id: id(20 + i), type, title: '第' + (i + 1) + '题', stem: '冻结题干', displayOrder: i + 1, score: 10,
      options: [{ optionKey: 'A', content: '甲' }, { optionKey: 'B', content: '乙' }], responseFields: type === 3 ? [{ id: id(50 + i), stemMarker: '空1' }, { id: id(60 + i), stemMarker: '空2' }] : [] })) } };
  let submission = null;
  const change = (next) => { state = next; listeners.forEach((fn) => fn(state)); };
  const policy = { maxFileBytes: 1024 * 1024, maxImagesPerResponse: 3, allowedContentTypes: ['image/jpeg', 'image/png', 'image/webp'] };
  const platform = { env: { USER_DATA_PATH: 'wxfile://owned' },
    getStorageSync: (key) => storage.get(key), getStorageInfoSync: () => ({ keys: [...storage.keys()] }), setStorageSync(key, row) { if (f.quota) throw new Error('quota'); storage.set(key, structuredClone(row)); }, removeStorageSync: (key) => storage.delete(key),
    getRandomValues(options) { const b = crypto.randomBytes(options.length); options.success({ randomValues: b.buffer.slice(b.byteOffset, b.byteOffset + b.length) }); },
    showModal(options) { dialogs.push(options.content); if (f.modal) f.modal(options); else options.success({ confirm: f.confirm }); },
    chooseMedia(options) { if (f.delayedPick) f.delayedPick(options); else options.success({ tempFiles: [{ tempFilePath: f.pick }] }); }, enableAlertBeforeUnload() {}, disableAlertBeforeUnload() {}, navigateTo() {},
    getFileInfo(options) { const b = files.get(options.filePath); if (!b) options.fail({}); else options.success({ size: b.length, digest: crypto.createHash(options.digestAlgorithm).update(b).digest('hex') }); },
    getFileSystemManager: () => ({ readFile(options) { const b = files.get(options.filePath); if (!b) options.fail({}); else { const bytes = b.subarray(options.position, options.position + options.length); options.success({ data: bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.length) }); } },
      copyFile(options) { if (!files.has(options.srcPath)) options.fail({}); else { files.set(options.destPath, Buffer.from(files.get(options.srcPath))); options.success({}); } },
      unlinkSync(filePath) { if (!files.delete(filePath)) throw Object.assign(new Error('missing'), { code: 'ENOENT' }); } }),
  };
  const fingerprint = () => crypto.createHash('sha256').update(JSON.stringify({ responses: dto.responses.map((r) => r.id), uploads: [...uploads.keys()] })).digest('hex');
  const session = {
    getState: () => state,
    async refreshWorkspace() { const original = state.context || context(); change({ ...state, context: null }); await Promise.resolve(); change({ ...state, context: structuredClone(original) }); return state; },
    async login() { change({ ...state, generation: state.generation + 1, authenticated: false, context: null, termination: 'login' }); change({ ...state, authenticated: true, context: context(), termination: null }); },
    async request(route, method = 'GET', body) {
      requests.push({ route, method, body: body && structuredClone(body) });
      if (route.includes('/my-student-task/')) return structuredClone(task);
      if (route.includes('/answer-image-policy/')) return policy;
      if (route.includes('/attempt/')) return structuredClone(dto);
      if (route.includes('/response-revision?')) return operations.get(new URL('https://api.invalid' + route).searchParams.get('operationId')) || null;
      if (route.includes('/answer-image?')) return uploads.get(new URL('https://api.invalid' + route).searchParams.get('operationId')) || null;
      if (route.includes('/submission/')) return submission && structuredClone(submission);
      if (route.includes('/start-or-resume/')) { if (f.startConflictOnce) { f.startConflictOnce = false; throw Object.assign(new Error('conflict'), { status: 409 }); } task.currentAttemptId = attemptId; task.canStart = false; return structuredClone(dto); }
      if (route.includes('/save-response/')) {
        if (body.expectedResponseSetVersion !== dto.responseSetVersion) throw Object.assign(new Error('conflict'), { status: 409 });
        const old = dto.responses.findIndex((r) => r.paperQuestionId === body.paperQuestionId);
        const row = { id: body.operationId, attemptId, paperSnapshotVersion: 1, paperQuestionId: body.paperQuestionId, resultResponseSetVersion: ++dto.responseSetVersion, response: structuredClone(body.response), savedAtUtc: new Date().toISOString() };
        if (old < 0) dto.responses.push(row); else dto.responses[old] = row; operations.set(row.id, structuredClone(row)); f.saveNumber++;
        if (f.lostSave || f.loseSaveNumber === f.saveNumber) { f.lostSave = false; throw Object.assign(new Error('lost'), { code: 'NETWORK_UNCONFIRMED' }); }
        return structuredClone(row);
      }
      if (route.includes('/preview-submission/')) return { attemptId, paperSnapshotVersion: 1, responseSetVersion: dto.responseSetVersion, canSubmit: !submission, submissionId: dto.submissionId, fingerprint: fingerprint(),
        questions: dto.paper.questions.map((q) => { const r = dto.responses.find((r) => r.paperQuestionId === q.id); return { id: q.id, title: q.title, displayOrder: q.displayOrder, savedResponseRevisionId: r ? r.id : null, isUnanswered: !r || !r.response.text && !r.response.selectedOptionKeys.length && !r.response.blanks.some((b) => b.text) && !r.response.images.length,
          unansweredBlankCount: q.responseFields.filter((field) => !r || !r.response.blanks.some((b) => b.blankId === field.id && b.text.trim())).length, savedImageCount: r ? r.response.images.length : 0 }; }),
        unlinkedImages: [...uploads.values()].filter((i) => !dto.responses.some((r) => r.response.images.some((ref) => ref.attachmentId === i.id))) };
      if (route.includes('/submit/')) {
        if (body.expectedPreparationFingerprint !== fingerprint() || body.expectedResponseSetVersion !== dto.responseSetVersion) throw Object.assign(new Error('conflict'), { status: 409 });
        submission = { id: f.deadlineWinner ? id(100) : body.operationId, attemptId, cause: f.deadlineWinner ? 1 : 0, effectiveAtUtc: new Date().toISOString(), sealedAtUtc: new Date().toISOString(), responses: structuredClone(dto.responses) };
        dto.submissionId = submission.id; dto.status = 1; return structuredClone(submission);
      }
      throw new Error('Unexpected test route ' + route);
    },
    async uploadAnswerImage(target, body, filePath) {
      requests.push({ route: 'uploadAnswerImage', method: 'POST', body: structuredClone(body) }); assert.equal(target, attemptId); const b = files.get(filePath); assert.ok(b);
      assert.equal(crypto.createHash('sha256').update(b).digest('hex'), body.contentHash);
      const row = { id: body.operationId, attemptId, paperQuestionId: body.paperQuestionId, contentHash: body.contentHash, size: b.length, contentType: 'image/png', fileName: path.basename(filePath), isUploaded: true };
      uploads.set(row.id, row); if (f.lostImage) { f.lostImage = false; throw Object.assign(new Error('lost'), { code: 'NETWORK_UNCONFIRMED' }); } return row;
    }, releaseFile() {}, async download() { return 'wxfile://downloaded'; },
  };
  const app = { configuration, session, subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); } };
  function mount() {
    const file = path.resolve(__dirname, '../pages/assessment-answer/index.js'); let definition;
    new vm.Script(fs.readFileSync(file, 'utf8'), { filename: file }).runInNewContext({ require: createRequire(file), wx: platform, getApp: () => app, Page: (v) => definition = v, setInterval: () => 1, clearInterval() {} });
    const page = { data: structuredClone(definition.data), setData(v) { f.transfers.push(Buffer.byteLength(JSON.stringify(v))); Object.assign(this.data, v); } };
    for (const [key, val] of Object.entries(definition)) if (typeof val === 'function') page[key] = val.bind(page);
    page.onLoad({ id: recipient, schoolId }); f.mounts.push(page); return page;
  }
  const edit = (page, field, val, fieldId) => page.change({ currentTarget: { dataset: { field, id: fieldId } }, detail: { value: val } });
  const started = async (page) => { await page.onShow(); await page.start(); await page.allowStorage(); return page; };
  Object.assign(f, { page: mount(), mount, started, edit, dto, task, files, storage, requests, uploads, operations, session, change, dialogs, getSubmission: () => submission, context }); return f;
}

test('native preflight is read-only, exact school-bound and requires explicit start and local-storage consent', async () => {
  const f = fixture(); await f.page.onShow(); assert.equal(f.page.data.ready, true); assert.equal(f.page.data.question, null); assert.equal(f.requests.filter((r) => r.method === 'POST').length, 0);
  await f.page.start(); assert.equal(f.page.data.count, 1); assert.equal(f.page.data.canEdit, false); f.edit(f.page, 'text', 'IGNORED'); assert.equal(f.page.drafts[id(20)], undefined);
  await f.page.allowStorage(); f.edit(f.page, 'text', ' 本机\n原文 '); assert.equal(f.page.data.question.text, ' 本机\n原文 '); assert.equal(f.dto.responses.length, 0);
  await f.page.save(); assert.equal(f.dto.responses[0].response.text, ' 本机\n原文 '); assert.equal(f.page.data.question.dirty, false); assert.equal(f.page.data.pending, null); f.page.onUnload();
});
test('lost save survives a cold page and recovers the same operation without writing twice', async () => {
  const f = fixture(); await f.started(f.page); f.edit(f.page, 'text', 'UNKNOWN-SAVE'); f.lostSave = true; await f.page.save(); const original = f.page.data.pending.operationId;
  f.page.onHide(); f.page.onUnload(); const cold = f.mount(); await cold.onShow(); assert.equal(cold.data.pending.operationId, original); assert.equal(cold.data.question.text, 'UNKNOWN-SAVE');
  await cold.recover(); assert.equal(cold.data.pending, null); assert.equal(cold.data.question.dirty, false); assert.equal(f.requests.filter((r) => r.route.includes('/save-response/')).length, 1); cold.onUnload();
});
test('recovered old save never replaces a newer cloud answer and requires explicit comparison before replacing it', async () => {
  const f = fixture(); await f.started(f.page); f.edit(f.page, 'text', '本机旧文'); f.lostSave = true; await f.page.save();
  f.dto.responses[0] = { ...f.dto.responses[0], id: id(90), resultResponseSetVersion: ++f.dto.responseSetVersion, response: response(4, '另一设备新文') };
  await f.page.recover(); assert.equal(f.page.data.question.conflict, true); assert.equal(f.page.data.question.text, '本机旧文'); assert.equal(f.dto.responses[0].response.text, '另一设备新文');
  const before = f.requests.filter((r) => r.method === 'POST').length; await f.page.save(); assert.equal(f.requests.filter((r) => r.method === 'POST').length, before);
  f.page.toggleCloud(); assert.equal(f.page.data.question.text, '另一设备新文'); await f.page.chooseLocal(); await f.page.save(); assert.equal(f.dto.responses[0].response.text, '本机旧文'); f.page.onUnload();
});
test('all five native response controls preserve selected order, stable blank identities and literal subjective text', async () => {
  const f = fixture([0, 1, 2, 3, 4]); await f.started(f.page);
  f.edit(f.page, 'choice', 'B'); await f.page.save(); f.page.question({ currentTarget: { dataset: { direction: 1 } } });
  f.edit(f.page, 'choice', ['B', 'A']); await f.page.save(); f.page.question({ currentTarget: { dataset: { direction: 1 } } });
  f.edit(f.page, 'choice', 'false'); await f.page.save(); f.page.question({ currentTarget: { dataset: { direction: 1 } } });
  f.edit(f.page, 'blank', '同文', id(53)); f.edit(f.page, 'blank', '同文', id(63)); await f.page.save(); f.page.question({ currentTarget: { dataset: { direction: 1 } } });
  f.edit(f.page, 'text', ' 原样\n x + 2 '); await f.page.save();
  assert.equal(f.dto.responses[0].response.selectedOptionKeys[0], 'B'); assert.deepEqual(f.dto.responses[1].response.selectedOptionKeys, ['B', 'A']); assert.equal(f.dto.responses[2].response.selectedOptionKeys[0], 'false');
  assert.equal(f.dto.responses[3].response.blanks.length, 2); assert.equal(f.dto.responses[4].response.text, ' 原样\n x + 2 '); f.page.onUnload();
});
test('an uploaded image remains a local draft until save; unknown replacement retains original position until its receipt', async () => {
  const f = fixture(); await f.started(f.page); f.pick = 'wxfile://picked'; f.files.set(f.pick, Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a/e0AAAAASUVORK5CYII=', 'base64'));
  await f.page.addImage({ currentTarget: { dataset: {} } }); const first = f.page.data.question.images[0].attachmentId;
  assert.equal(f.dto.responses.length, 0); assert.equal(f.page.data.question.dirty, true); await f.page.save();
  f.lostImage = true; await f.page.addImage({ currentTarget: { dataset: { id: first } } }); assert.equal(f.page.data.question.images[0].attachmentId, first); assert.ok(f.page.data.pending);
  await f.page.recover(); const second = f.page.data.question.images[0].attachmentId; assert.notEqual(second, first); assert.equal(f.dto.responses[0].response.images[0].attachmentId, first);
  f.page.imageAction({ currentTarget: { dataset: { id: second, action: 'rotate' } } }); await f.page.save(); assert.equal(f.dto.responses[0].response.images[0].rotationDegrees, 90); assert.equal(f.uploads.size, 2);
  assert.equal([...f.files.keys()].filter((p) => p.includes('/kp-assessment-')).length, 0); f.page.onUnload();
});
test('saved-only submission lists local exclusions, requires both confirmations and seals only confirmed answers', async () => {
  const f = fixture(); await f.started(f.page); f.edit(f.page, 'text', '服务器答案'); await f.page.save(); f.edit(f.page, 'text', '本机未保存修改');
  await f.page.prepare(); assert.equal(f.page.data.review.exclusionTotal, 1); assert.equal(f.page.data.review.hasExclusions, true);
  f.page.confirm({ detail: { value: ['confirmed'] } }); await f.page.submit(); assert.equal(f.getSubmission(), null);
  f.page.confirm({ detail: { value: ['confirmed', 'savedOnly'] } }); await f.page.submit(); assert.ok(f.getSubmission()); assert.equal(f.getSubmission().responses[0].response.text, '服务器答案');
  assert.equal(f.page.data.submission.cause, '手动提交'); assert.equal(f.page.data.question.text, '本机未保存修改'); assert.equal(f.page.data.canEdit, false); f.page.onUnload();
});
test('sequential synchronization stops on an unknown operation while preserving remaining drafts', async () => {
  const f = fixture([4, 4]); await f.started(f.page); f.edit(f.page, 'text', '第一题'); f.page.question({ currentTarget: { dataset: { direction: 1 } } }); f.edit(f.page, 'text', '第二题');
  f.loseSaveNumber = 2; await f.page.saveAll(); assert.equal(f.dto.responses.length, 2); assert.equal(f.page.data.pending.kind, 'response'); assert.equal(f.page.data.draftCount, 1); assert.equal(f.page.data.review, null);
  await f.page.recover(); assert.equal(f.page.data.draftCount, 0); f.page.onUnload();
});
test('a deadline winner is displayed as server cutoff and no second submission is invented', async () => {
  const f = fixture(); await f.started(f.page); f.edit(f.page, 'text', '已保存'); await f.page.save(); await f.page.prepare(); f.page.confirm({ detail: { value: ['confirmed'] } }); f.deadlineWinner = true;
  await f.page.submit(); assert.equal(f.page.data.submission.cause, '截止封存'); assert.equal(f.page.data.submission.id, id(100)); assert.ok(f.page.data.notice.includes('已有其他封存操作')); f.page.onUnload();
});
test('leaving during image selection cannot create an upload or restore a hidden view', async () => {
  const f = fixture(); await f.started(f.page); let selected; f.delayedPick = (o) => selected = o;
  f.files.set('wxfile://late', Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a/e0AAAAASUVORK5CYII=', 'base64'));
  const pending = f.page.addImage({ currentTarget: { dataset: {} } }); await Promise.resolve(); f.page.onHide(); selected.success({ tempFiles: [{ tempFilePath: 'wxfile://late' }] }); await pending;
  assert.equal(f.requests.filter((r) => r.route === 'uploadAnswerImage').length, 0); assert.equal(f.page.data.question, null); f.page.onUnload();
});
test('storage pressure preserves running input and prevents sending a command without a persistent operation', async () => {
  const f = fixture(); await f.started(f.page); f.quota = true; f.edit(f.page, 'text', '内存文字'); assert.equal(f.page.data.unpersisted, true); assert.equal(f.page.data.question.text, '内存文字');
  await f.page.save(); assert.equal(f.requests.filter((r) => r.route.includes('/save-response/')).length, 0); f.quota = false; await f.page.save(); assert.equal(f.page.data.unpersisted, false); f.page.onUnload();
});
test('expiry hides the page, same-account explicit login recovers the draft and account switching cannot reveal it', async () => {
  const f = fixture(); await f.started(f.page); f.edit(f.page, 'text', 'PRIVATE-DRAFT');
  f.change({ generation: 2, authenticated: false, context: null, termination: 'expired' }); assert.equal(f.page.data.question, null); await f.page.reauthenticate(); assert.equal(f.page.data.question.text, 'PRIVATE-DRAFT');
  f.change({ generation: 4, authenticated: true, context: { ...f.context(), accountUserId: id(99) }, termination: null }); assert.equal(f.page.data.question, null); assert.equal(Object.keys(f.page.drafts).length, 0); await f.page.load(); assert.equal(f.page.data.ready, false); f.page.onUnload();
});
test('long papers transfer only the current question and paged review metadata to the native view', async () => {
  const f = fixture(Array(64).fill(4)); for (const q of f.dto.paper.questions) q.stem = '长题干'.repeat(4000);
  await f.started(f.page); assert.equal(f.page.data.count, 64); assert.equal(f.page.data.question.id, id(20)); f.page.question({ currentTarget: { dataset: { direction: 1 } } }); assert.equal(f.page.data.question.id, id(21));
  await f.page.prepare(); assert.equal(f.page.data.review.questions.length, 20); f.page.reviewPage({ currentTarget: { dataset: { direction: 1 } } }); assert.equal(f.page.data.review.questions[0].id, id(40));
  assert.ok(Math.max(...f.transfers) < 1024 * 1024); f.page.onHide(); assert.equal(f.page.data.question, null); assert.equal(f.page.data.review, null); f.page.onUnload();
});

test('teacher-only or wrong-school identity cannot fetch a native student task', async () => {
  const f = fixture(); const ctx = f.context(); ctx.workspaces[0].role = 1;
  f.change({ generation: 2, authenticated: true, context: ctx, termination: null }); await f.page.onShow(); assert.equal(f.page.data.ready, false);
  assert.equal(f.requests.length, 0); f.page.onUnload();
});

test('the local deadline disables editing but does not pretend the server already sealed or automatically send a submission', async () => {
  const f = fixture(); await f.started(f.page); f.edit(f.page, 'text', '尚未保存'); f.dto.expiresAtUtc = new Date(Date.now() - 1000).toISOString(); f.dto.serverNowUtc = new Date().toISOString();
  await f.page.load(); assert.equal(f.page.data.canEdit, false); assert.equal(f.page.data.submission, null); assert.equal(f.page.data.question.text, '尚未保存');
  assert.equal(f.requests.filter((r) => r.route.includes('/submit/')).length, 0); f.page.onUnload();
});

test('a contested start remains pending and explicit receipt-first retry keeps its exact operation instead of starting again automatically', async () => {
  const f = fixture(); await f.page.onShow(); await f.page.allowStorage(); f.startConflictOnce = true; await f.page.start();
  assert.equal(f.page.attempt, null); assert.equal(f.page.data.pending.kind, 'start'); const operation = f.page.data.pending.operationId;
  assert.equal(f.requests.filter((r) => r.route.includes('/start-or-resume/')).length, 1); await f.page.recover(); assert.equal(f.page.attempt, null);
  await f.page.retry(); assert.ok(f.page.attempt); assert.equal(f.page.data.pending, null);
  const starts = f.requests.filter((r) => r.route.includes('/start-or-resume/')); assert.equal(starts.length, 2); assert.equal(starts[0].body.operationId, operation); assert.equal(starts[1].body.operationId, operation); f.page.onUnload();
});

test('native single choice can be cleared deliberately without silently changing the saved answer', async () => {
  const f = fixture([0]); await f.started(f.page); f.edit(f.page, 'choice', 'A'); await f.page.save(); f.page.clearChoices();
  assert.equal(f.page.data.question.options.some((o) => o.selected), false); assert.equal(f.dto.responses[0].response.selectedOptionKeys[0], 'A');
  await f.page.save(); assert.equal(f.dto.responses[0].response.selectedOptionKeys.length, 0); f.page.onUnload();
});

test('confirming a start dialog after leaving cannot start the assessment or persist a new command', async () => {
  const f = fixture(); await f.page.onShow(); let dialog; f.modal = (options) => dialog = options;
  const pending = f.page.start(); f.page.onHide(); dialog.success({ confirm: true }); await pending;
  assert.equal(f.task.currentAttemptId, null); assert.equal(f.requests.filter((r) => r.method === 'POST').length, 0); assert.equal([...f.storage.keys()].filter((k) => k.endsWith(':pending')).length, 0); f.page.onUnload();
});

test('confirming discard after leaving keeps the existing private draft instead of deleting it in the background', async () => {
  const f = fixture(); await f.started(f.page); f.edit(f.page, 'text', 'KEEP-PRIVATE-DRAFT'); let dialog; f.modal = (options) => dialog = options;
  const pending = f.page.discard(); f.page.onHide(); dialog.success({ confirm: true }); await pending;
  assert.equal(f.page.drafts[id(20)].value.text, 'KEEP-PRIVATE-DRAFT'); assert.ok(JSON.stringify([...f.storage.values()]).includes('KEEP-PRIVATE-DRAFT')); assert.equal(f.page.data.question, null); f.page.onUnload();
});
