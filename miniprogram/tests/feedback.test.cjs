const { test } = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const feedback = require('../utils/feedback');
const command = require('../utils/feedback-command');
const school = '11111111-1111-1111-1111-111111111111';
const owner = '22222222-2222-2222-2222-222222222222';
const root = '33333333-3333-3333-3333-333333333333';
const version = '44444444-4444-4444-4444-444444444444';
const teaching = '55555555-5555-5555-5555-555555555555';
const enrollment = '66666666-6666-6666-6666-666666666666';
const member = '77777777-7777-7777-7777-777777777777';
const config = { apiBaseUrl: 'https://api.example.invalid', appKey: 'main', clientId: 'KnowPulse_WeChat_main' };
const active = () => ({ authenticated: true, generation: 1, context: { accountUserId: owner, tenantId: null, grantedPermissions: ['KnowPulse.Education.FeedbackManage'], workspaces: [{ role: 1, schoolId: school, classes: [{ relationId: teaching }] }] } });
const random = (options) => { const bytes = crypto.randomBytes(16); options.success({ randomValues: bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.length) }); };

test('feedback requires the confirmed school role plus its own functional permission', () => {
  const state = active(); assert.ok(feedback.allowed(state, 'teacher', school)); assert.ok(!feedback.allowed(state, 'student', school));
  state.context.grantedPermissions = ['KnowPulse.Education.AssessmentManage']; assert.ok(!feedback.allowed(state, 'teacher', school));
});

test('private observation times must represent real local dates and text stays bounded and unchanged', () => {
  const value = feedback.observationContent(' 原文\n第二行 ', '2026-02-28', '12:34'); assert.equal(value.text, ' 原文\n第二行 '); assert.ok(value.observedAtUtc.endsWith('Z'));
  for (const args of [['正文', '2026-02-30', '12:34'], ['正文', '2026-02-28', '25:00'], ['   ', '2026-02-28', '12:34'], ['x'.repeat(4001), '2026-02-28', '12:34']]) assert.throws(() => feedback.observationContent(...args), { code: 'INVALID_FEEDBACK_FORM' });
});

test('teacher teaching selector includes only actual relations in the chosen school', () => {
  const values = feedback.teachingList([{ id: teaching, schoolName: '学校', className: '班级', subjectName: '数学' }, { id: root, schoolName: '另一校' }], active(), school);
  assert.equal(values.length, 1); assert.equal(values[0].id, teaching);
});

test('student cards and withdrawn detail never include body, internal reason or observation', () => {
  const dto = { id: root, versionId: version, versionNumber: 2, schoolId: school, visibility: 2, teacherName: '老师', text: 'PRIVATE-BODY', reason: 'PRIVATE-REASON', selectedObservationId: 'PRIVATE-OBS' };
  const card = feedback.studentCard(dto, school); assert.ok(!JSON.stringify(card).includes('PRIVATE'));
  dto.visibility = 3; const detail = feedback.studentDetail(dto, root, version, school); assert.equal(detail.text, ''); assert.deepEqual(detail.assessments, []);
  assert.throws(() => feedback.studentDetail(dto, root, teaching, school), { code: 'INVALID_RESPONSE' });
  assert.throws(() => feedback.studentCard(dto, teaching), { code: 'INVALID_RESPONSE' });
});

test('new forms use the current local date and edits preserve original UTC precision until time changes', () => {
  const empty = feedback.formFrom(null); assert.notEqual(empty.date, '1970-01-01');
  const raw = { kind: 0, context: '课堂', text: '建议', occurredAtUtc: '2026-10-18T00:00:01.1234567Z', periodEndsAtUtc: null, assessmentTaskIds: [], selectedObservationId: null };
  const form = feedback.formFrom(raw); form.text = '新的建议'; assert.equal(feedback.content(form).occurredAtUtc, raw.occurredAtUtc);
});

test('assessment and period forms enforce explicit bindings, valid windows and bounded text', () => {
  const form = feedback.formFrom(null); Object.assign(form, { context: '背景', text: '建议', kind: 1 });
  assert.throws(() => feedback.content(form), { code: 'INVALID_FEEDBACK_FORM' }); form.assessmentTaskIds = [root]; assert.equal(feedback.content(form).kind, 1);
  form.kind = 2; form.endDate = form.date; form.endTime = form.time; assert.throws(() => feedback.content(form), { code: 'INVALID_FEEDBACK_FORM' });
  form.kind = 0; form.text = 'x'.repeat(4001); assert.throws(() => feedback.content(form), { code: 'INVALID_FEEDBACK_FORM' });
});

test('operation UUIDs require platform randomness and no insecure fallback', async () => {
  const first = await feedback.uuid({ getRandomValues: random }); const second = await feedback.uuid({ getRandomValues: random });
  assert.match(first, /^[a-f0-9-]{14}4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/); assert.notEqual(first, second);
  await assert.rejects(feedback.uuid({}), { code: 'RANDOM_REQUIRED' });
});

function fixture() {
  let state = active(); const storage = new Map(); const requests = []; const receipts = new Map();
  const f = { lose: false, reject: false, quota: false, readMissing: false };
  const platform = { getRandomValues: random, getStorageSync: (key) => storage.get(key), setStorageSync(key, value) { if (f.quota) throw new Error('quota'); storage.set(key, structuredClone(value)); }, removeStorageSync: (key) => storage.delete(key) };
  const session = { getState: () => state, async request(path, method, body) {
    requests.push({ path, method, body: body && structuredClone(body) });
    if (path.includes('/operation?')) return f.readMissing ? null : receipts.get(new URL('https://api.invalid' + path).searchParams.get('operationId')) || null;
    if (f.reject) throw Object.assign(new Error('conflict'), { code: 'REQUEST_FAILED', status: 409 });
    const receipt = { id: body.operationId, feedbackId: root, kind: path.includes('save-draft') ? 0 : path.includes('/publish/') ? 1 : 2 };
    receipts.set(body.operationId, receipt);
    if (f.lose) { f.lose = false; throw Object.assign(new Error('unknown'), { code: 'NETWORK_UNCONFIRMED' }); }
    return receipt;
  } };
  return Object.assign(f, { platform, session, storage, requests, receipts, change: (value) => state = value, writer: () => command(platform, session, config, root, school) });
}

test('a confirmed write persists only recovery identifiers and never its text or reason', async () => {
  const f = fixture(); f.lose = true; const writer = f.writer();
  await assert.rejects(writer.start('draft', { content: { text: 'PRIVATE-TEXT' }, reason: 'PRIVATE-REASON' }), { code: 'NETWORK_UNCONFIRMED' });
  const stored = [...f.storage.values()][0]; assert.deepEqual(Object.keys(stored).sort(), ['feedbackId', 'kind', 'operationId']);
  assert.ok(!JSON.stringify([...f.storage.values()]).includes('PRIVATE')); assert.ok(writer.state().pending); assert.ok(writer.state().canRetry);
  const recovered = await writer.recover(); assert.equal(recovered.id, stored.operationId); assert.equal(writer.state().pending, null); assert.equal(f.requests.filter((r) => r.method === 'POST').length, 1);
});

test('explicit retry first checks the receipt and uses the identical operation, payload and root', async () => {
  const f = fixture(); f.lose = true; const writer = f.writer(); const input = { content: { text: 'ORIGINAL' } };
  await assert.rejects(writer.start('draft', input)); input.content.text = 'CHANGED'; f.readMissing = true;
  await writer.retry(); const posts = f.requests.filter((r) => r.method === 'POST'); assert.equal(posts.length, 2); assert.deepEqual(posts[0], posts[1]);
});

test('a cold editor can recover a committed operation but cannot replay a lost body', async () => {
  const f = fixture(); f.lose = true; await assert.rejects(f.writer().start('publish', { reason: 'PRIVATE' }));
  const restored = f.writer(); assert.equal(restored.state().canRetry, false); assert.ok(await restored.recover());
  f.lose = true; const next = f.writer(); await assert.rejects(next.start('draft', { content: { text: 'UNSAVED' } }));
  f.readMissing = true; const cold = f.writer(); await assert.rejects(cold.retry(), { code: 'RETRY_BODY_LOST' });
  assert.equal(f.requests.filter((r) => r.method === 'POST').length, 2);
});

test('storage failure prevents sending and pending work prevents minting a replacement command', async () => {
  const f = fixture(); f.quota = true; await assert.rejects(f.writer().start('draft', {}), { code: 'STORAGE_REQUIRED' }); assert.equal(f.requests.length, 0);
  f.quota = false; f.lose = true; const writer = f.writer(); await assert.rejects(writer.start('draft', {}));
  await assert.rejects(writer.start('publish', {}), { code: 'PENDING_OPERATION' }); assert.equal(f.requests.length, 1);
});

test('a conflict retains the exact original operation until the user explicitly closes tracking', async () => {
  const f = fixture(); f.reject = true; const writer = f.writer(); await assert.rejects(writer.start('draft', {}), { status: 409 });
  assert.ok(writer.state().pending); assert.equal(await writer.recover(), null); writer.abandon(); assert.equal(writer.state().pending, null); assert.equal(f.storage.size, 0);
});

test('another account cannot recover, retry or clear the previous account operation', async () => {
  const f = fixture(); f.lose = true; const writer = f.writer(); await assert.rejects(writer.start('draft', { text: 'PRIVATE' }));
  const changed = active(); changed.context.accountUserId = enrollment; f.change(changed);
  await assert.rejects(writer.recover(), { code: 'STALE_RESPONSE' }); assert.throws(() => writer.abandon(), { code: 'STALE_RESPONSE' });
  const other = f.writer(); assert.equal(other.state().pending, null); assert.equal(f.storage.size, 1);
});

test('a mismatched receipt cannot clear a pending command', async () => {
  const f = fixture(); f.lose = true; const writer = f.writer(); await assert.rejects(writer.start('draft', {}));
  const id = writer.state().pending.operationId; f.receipts.set(id, { id, feedbackId: teaching, kind: 0 });
  await assert.rejects(writer.recover(), { code: 'INVALID_RESPONSE' }); assert.ok(writer.state().pending);
});
