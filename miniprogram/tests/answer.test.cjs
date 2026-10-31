const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const answer = require('../utils/answer');
const createStore = require('../utils/answer-store');
const createCommand = require('../utils/answer-command');
const images = require('../utils/answer-image');
const id = (n) => `${String(n).padStart(8, '0')}-1111-1111-1111-111111111111`;
const recipient = id(1), attemptId = id(2), revision = id(3), schoolId = id(4), userId = id(5), questionId = id(6), blankId = id(7);
const config = { apiBaseUrl: 'https://api.example.invalid', appKey: 'main', clientId: 'KnowPulse_WeChat_main' };
const identity = { accountUserId: userId, tenantId: null };
const task = () => ({ id: recipient, currentAttemptId: attemptId, taskRevisionId: revision });
const value = (type = 4) => ({ schemaVersion: 1, type, selectedOptionKeys: [], blanks: [], text: null, images: [] });
const raw = (type = 4) => ({ id: attemptId, recipientId: recipient, taskRevisionId: revision, status: 0, paperSnapshotVersion: 1, responseSetVersion: 0,
  expiresAtUtc: new Date(Date.now() + 60000).toISOString(), serverNowUtc: new Date().toISOString(), submissionId: null, responses: [],
  paper: { questionCount: 1, questions: [{ id: questionId, type, stem: '题干', title: '试题', options: [{ optionKey: 'A', content: '甲' }, { optionKey: 'B', content: '乙' }], responseFields: [{ id: blankId, stemMarker: '{{1}}' }] }], analysis: 'FORBIDDEN-ANSWER' } });
function fixture() {
  let clock = Date.now(); const storage = new Map(); const files = new Map(); const requests = [];
  const platform = { env: { USER_DATA_PATH: 'wxfile://user' },
    getStorageSync: (key) => storage.get(key), setStorageSync: (key, row) => storage.set(key, structuredClone(row)), removeStorageSync: (key) => storage.delete(key), getStorageInfoSync: () => ({ keys: [...storage.keys()] }),
    getRandomValues(options) { const bytes = crypto.randomBytes(options.length); options.success({ randomValues: bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.length) }); },
    getFileInfo(options) { const bytes = files.get(options.filePath); if (!bytes) options.fail({}); else options.success({ size: bytes.length, digest: crypto.createHash(options.digestAlgorithm).update(bytes).digest('hex') }); },
    getFileSystemManager() { return { readFile(options) { const bytes = files.get(options.filePath); if (!bytes) options.fail({}); else { const b = bytes.subarray(options.position, options.position + options.length); options.success({ data: b.buffer.slice(b.byteOffset, b.byteOffset + b.length) }); } },
      copyFile(options) { if (!files.has(options.srcPath)) options.fail({}); else { files.set(options.destPath, Buffer.from(files.get(options.srcPath))); options.success({}); } },
      unlinkSync(filePath) { if (!files.delete(filePath)) throw Object.assign(new Error('missing'), { code: 'ENOENT' }); } }; },
  };
  let state = { authenticated: true, context: { ...identity, grantedPermissions: ['KnowPulse.Education.AssessmentParticipate'], workspaces: [{ role: 2, schoolId }] } };
  const f = { receipt: null, fail: false };
  const session = { getState: () => state, async request(route, method = 'GET', body) { requests.push({ route, method, body: structuredClone(body) }); if (method === 'GET') return f.receipt; if (f.fail) throw Object.assign(new Error('lost'), { code: 'NETWORK_UNCONFIRMED' }); return f.receipt; } };
  const store = createStore(platform, config, identity, recipient, () => clock);
  return Object.assign(f, { store, platform, storage, files, requests, session, advance: (ms) => clock += ms, change: (v) => state = v,
    command: () => createCommand(platform, session, store, recipient, schoolId) });
}

test('answer projection accepts only the exact frozen attempt and drops answer keys and provenance', () => {
  const dto = raw(); dto.paper.questions[0].answers = ['PRIVATE-KEY']; const actual = answer.attempt(dto, task());
  assert.ok(!JSON.stringify(actual).includes('PRIVATE')); assert.ok(!JSON.stringify(actual).includes('FORBIDDEN-ANSWER'));
  assert.throws(() => answer.attempt(dto, { ...task(), currentAttemptId: id(9) }));
  dto.responses = [{ id: id(8), attemptId, paperQuestionId: questionId, paperSnapshotVersion: 1, resultResponseSetVersion: 1, response: value() }]; assert.throws(() => answer.attempt(dto, task()));
});
test('question drafts clone original values, preserve literal text and keep their original response base across refresh', () => {
  const current = answer.attempt(raw(), task()); const drafts = {}; const local = answer.edit(current, questionId, drafts); local.value.text = ' 原文\n = x ';
  const refreshed = { ...current, responseSetVersion: 2, responses: [{ id: id(8), paperQuestionId: questionId, response: { ...value(), text: '另一设备' } }] };
  assert.equal(answer.conflict(refreshed, local), true); assert.equal(local.baseRevisionId, null); assert.equal(answer.projected(refreshed, questionId, drafts).value.text, ' 原文\n = x ');
  assert.equal(current.responses.length, 0); local.baseRevisionId = answer.headId(refreshed, questionId); assert.equal(answer.conflict(refreshed, local), false);
});
test('response validation preserves option ordering and repeated blank text while refusing unknown identities and incompatible fields', () => {
  const q = raw(1).paper.questions[0]; const multiple = { ...value(1), selectedOptionKeys: ['B', 'A'] }; assert.deepEqual(answer.response(multiple, q).selectedOptionKeys, ['B', 'A']);
  assert.throws(() => answer.response({ ...multiple, selectedOptionKeys: ['A', 'A'] }, q)); assert.throws(() => answer.response({ ...multiple, text: '' }, q));
  const blanks = { ...raw(3).paper.questions[0], responseFields: [{ id: blankId, stemMarker: '1' }, { id: id(8), stemMarker: '2' }] };
  const fill = { ...value(3), blanks: [{ blankId, text: '同文' }, { blankId: id(8), text: '同文' }] }; assert.equal(answer.response(fill, blanks).blanks.length, 2);
  assert.throws(() => answer.response({ ...fill, images: [{ attachmentId: id(8), rotationDegrees: 0 }] }, blanks));
});
test('local storage separates accounts and attempts, expires drafts, and preserves an identifier-only unknown operation', async () => {
  const f = fixture(); const current = answer.attempt(raw(), task()); const local = answer.edit(current, questionId, {}); local.value.text = 'PRIVATE-DRAFT'; f.store.saveDraft(local);
  assert.equal(f.store.drafts(current).values[questionId].value.text, 'PRIVATE-DRAFT');
  const peer = createStore(f.platform, config, { accountUserId: id(9), tenantId: null }, recipient); assert.equal(Object.keys(peer.drafts(current).values).length, 0);
  const writer = f.command(); await writer.begin('response', { response: local.value }, { attemptId, questionId }); f.advance(86400000);
  assert.equal(f.store.drafts(current).expired, 1); assert.equal(f.store.pending().payload, null); assert.ok(writer.state().operationId); assert.equal(writer.state().canRetry, false);
});
test('pending commands persist before writes, recover first and never substitute a new operation or a newer answer', async () => {
  const f = fixture(); const writer = f.command(); const p = await writer.begin('response', { paperQuestionId: questionId, response: { ...value(), text: 'EXACT' } }, { attemptId, questionId });
  f.fail = true; await assert.rejects(writer.send(), { code: 'NETWORK_UNCONFIRMED' });
  await assert.rejects(writer.begin('submit', {}, { attemptId }), { code: 'PENDING_OPERATION' });
  f.receipt = { id: p.operationId, attemptId, paperQuestionId: questionId }; const receipt = await writer.retry(); assert.equal(receipt.id, p.operationId);
  assert.equal(f.requests.filter((r) => r.method === 'POST').length, 1); assert.equal(f.requests.at(-1).route, `/api/app/education-assessment/response-revision?attemptId=${attemptId}&operationId=${p.operationId}`);
  writer.acknowledge(p.operationId); assert.equal(writer.state(), null);
});
test('a quota failure prevents an untracked write and another account cannot read or clear its command', async () => {
  const f = fixture(); const writer = f.command(); f.platform.setStorageSync = () => { throw new Error('quota'); };
  await assert.rejects(writer.begin('start', {}), { code: 'STORAGE_REQUIRED' }); assert.equal(f.requests.length, 0);
  f.platform.setStorageSync = (key, row) => f.storage.set(key, structuredClone(row)); await writer.begin('start', {});
  f.change({ authenticated: false, context: null }); assert.throws(() => writer.state(), { code: 'FORBIDDEN' }); assert.throws(() => writer.abandon(), { code: 'FORBIDDEN' });
});
test('submission recovery may report the single deadline winner without claiming it used the manual operation', async () => {
  const f = fixture(); const writer = f.command(); const p = await writer.begin('submit', { expectedResponseSetVersion: 1 }, { attemptId });
  f.receipt = { id: id(9), attemptId, cause: 1 }; const result = await writer.recover(); assert.notEqual(result.id, p.operationId); assert.equal(result.cause, 1);
});
test('native SHA-256 image checks pin type and bytes, copy only to the operation path and reject a different retry', async () => {
  const f = fixture(); const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a/e0AAAAASUVORK5CYII=', 'base64'); f.files.set('wxfile://pick', png);
  const policy = { maxFileBytes: 1024, allowedContentTypes: ['image/png'] }; const metadata = await images.inspect(f.platform, 'wxfile://pick', policy); assert.equal(metadata.extension, 'png');
  const target = await images.retain(f.platform, 'wxfile://pick', id(8), metadata, policy); assert.equal(target, 'wxfile://user/kp-assessment-' + id(8) + '.png');
  f.files.set('wxfile://different', Buffer.concat([png, Buffer.from('different')])); await assert.rejects(images.retain(f.platform, 'wxfile://different', id(8), metadata, policy), { code: 'IMAGE_MISMATCH' });
  f.platform.getFileInfo = (o) => o.success({ size: png.length, digest: 'a'.repeat(32) }); await assert.rejects(images.inspect(f.platform, 'wxfile://pick', policy), { code: 'IMAGE_INVALID' });
});
test('logout cleanup removes only own private drafts and copied files, retaining no command body and preserving other accounts', async () => {
  const f = fixture(); const local = answer.edit(answer.attempt(raw(), task()), questionId, {}); local.value.text = 'PRIVATE'; f.store.saveDraft(local);
  const writer = f.command(); const p = await writer.begin('image', { contentHash: 'a'.repeat(64) }, { attemptId, questionId, image: { extension: 'png', contentHash: 'a'.repeat(64), size: 68, contentType: 'image/png' } });
  const filePath = f.store.imagePath(p); f.files.set(filePath, Buffer.from('private')); const foreign = 'knowpulse:answers:foreign'; f.storage.set(foreign, 'KEEP'); f.files.set('wxfile://user/other.png', Buffer.from('KEEP'));
  assert.equal(createStore.clearAccount(f.platform, config, identity), true); assert.ok(!JSON.stringify([...f.storage.values()]).includes('PRIVATE'));
  assert.equal(f.store.pending().payload, null); assert.ok(f.store.pending().operationId); assert.equal(f.files.has(filePath), false); assert.equal(f.storage.get(foreign), 'KEEP'); assert.equal(f.files.has('wxfile://user/other.png'), true);
  assert.equal(createStore.clearAccount(f.platform, config, identity), true);
});

test('a copied image with an interrupted cache timestamp still expires logically without losing its operation reference', async () => {
  const f = fixture(); const writer = f.command(); const p = await writer.begin('image', {}, { attemptId, questionId, image: { extension: 'png', contentHash: 'a'.repeat(64), size: 68, contentType: 'image/png' } });
  const file = f.store.imagePath(p); f.files.set(file, Buffer.from('retained before timestamp write')); f.advance(86400000);
  assert.equal(f.store.pending().operationId, p.operationId); assert.equal(f.store.pending().payload, null); assert.equal(f.files.has(file), false);
  assert.equal(f.store.pending().needsImageCleanup, false);
});
