const { test } = require('node:test');
const assert = require('node:assert/strict');
const assessment = require('../utils/assessment');
const readScope = require('../utils/read-scope');
const school = '11111111-1111-1111-1111-111111111111';
const task = '22222222-2222-2222-2222-222222222222';
const recipient = '33333333-3333-3333-3333-333333333333';
const release = '44444444-4444-4444-4444-444444444444';
const question = '55555555-5555-5555-5555-555555555555';
const image = '66666666-6666-6666-6666-666666666666';
const attempt = '77777777-7777-7777-7777-777777777777';
const state = () => ({ generation: 1, authenticated: true, context: { workspaces: [{ role: 2, schoolId: school }], grantedPermissions: ['KnowPulse.Education.AssessmentParticipate'] } });
const full = () => ({ taskId: task, recipientId: recipient, resultReleaseId: release, attemptId: attempt, outcome: 0, currentResultVisibility: 2,
  totalScore: 0, maxScore: 10, questions: [{ id: question, title: '冻结题', stem: '题干', obtainedScore: 0, maxScore: 10,
    response: { text: '我的首答', selectedOptionKeys: ['B'], images: [{ attachmentId: image, rotationDegrees: 90 }] },
    correctAnswers: ['A'], analysis: '解析', feedback: '教师反馈', scoringDefinition: { referenceAnswer: '参考' }, privateSource: 'DO-NOT-PROJECT' }] });

test('school-confirmed role and functional permission are both required', () => {
  const s = state(); assert.ok(assessment.allowed(s, 'student', school)); assert.ok(!assessment.allowed(s, 'teacher', school));
  s.context.grantedPermissions = ['KnowPulse.Education.AssessmentManage']; assert.ok(!assessment.allowed(s, 'teacher', school));
  s.context.workspaces[0].role = 1; assert.ok(assessment.allowed(s, 'teacher', school));
  assert.ok(!assessment.allowed(s, 'teacher', task)); assert.ok(!assessment.allowed(s, 'admin', school));
});

test('hidden, score-only and absent projections never carry question or partial score data', () => {
  for (const visibility of [0, 1]) {
    const dto = full(); dto.currentResultVisibility = visibility;
    const view = assessment.result(dto, recipient);
    assert.deepEqual(view.questions, []); assert.equal(view.total, visibility === 0 ? '—' : '0');
    assert.ok(!JSON.stringify(view).includes('我的首答')); assert.ok(!JSON.stringify(view).includes('参考'));
  }
  const dto = full(); dto.outcome = 1; dto.totalScore = null;
  assert.equal(assessment.result(dto, recipient).total, '—'); assert.deepEqual(assessment.result(dto, recipient).questions, []);
});

test('full review preserves zero, original answer, image rotation and feedback but no private provenance', () => {
  const view = assessment.result(full(), recipient);
  assert.equal(view.total, '0'); assert.equal(view.questions[0].responseText, '我的首答'); assert.equal(view.questions[0].feedback, '教师反馈');
  assert.deepEqual(view.questions[0].images, [{ id: image, rotation: 90 }]);
  assert.ok(!JSON.stringify(view).includes('DO-NOT-PROJECT'));
});

test('wrong recipient, unknown visibility, unconfirmed totals and invalid image identities fail closed', () => {
  assert.throws(() => assessment.result(full(), task), { code: 'INVALID_RESPONSE' });
  for (const change of [(d) => d.currentResultVisibility = 3, (d) => d.totalScore = null, (d) => d.questions[0].response.images[0].rotationDegrees = 45]) {
    const dto = full(); change(dto); assert.throws(() => assessment.result(dto, recipient), { code: 'INVALID_RESPONSE' });
  }
});

test('task pagination keeps server total and rejects another school or an unpublished teacher revision', () => {
  const dto = { totalCount: 41, items: [{ id: recipient, taskId: task, schoolId: school, title: '我的任务', currentResultVisibility: 0 }] };
  assert.equal(assessment.taskPage(dto, 'student', school).total, 41);
  assert.throws(() => assessment.taskPage(dto, 'student', task), { code: 'INVALID_RESPONSE' });
  assert.throws(() => assessment.taskPage({ totalCount: 1, items: [{ id: task, publishedRevisionId: release, currentRevision: { id: question, schoolId: school } }] }, 'teacher', school), { code: 'INVALID_RESPONSE' });
});

test('teacher progress with a partial mark never presents a final total or a student release', () => {
  const dto = { taskId: task, publishedRevisionId: release, recipientCount: 2, unstartedCount: 1, inProgressCount: 0, submittedCount: 1,
    gradingCompleteCount: 0, pendingQuestionCount: 1, confirmedGradeSetVersion: 0, closureId: null, currentResultReleaseId: null,
    submissions: [{ submissionId: recipient, studentName: '同名', accountName: 'student-a', questionCount: 2, confirmedQuestionCount: 1, totalScore: 5, maxScore: 10 }] };
  const view = assessment.overview(dto, task);
  assert.equal(view.submissions[0].total, '—'); assert.equal(view.released, false); assert.equal(view.closed, false);
  assert.equal(view.submissions[0].accountName, 'student-a');
});

function readerFixture() {
  let current = state(); const listeners = new Set(); let released = 0;
  const page = { data: { body: null }, setData(value) { Object.assign(this.data, value); } };
  const app = { session: { getState: () => current, async refreshWorkspace() { current = { ...current, context: structuredClone(current.context) }; listeners.forEach((fn) => fn(current)); return current; } },
    subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); } };
  const reader = readScope(page, app, (s) => assessment.allowed(s, 'student', school), () => ({ body: null }), () => released++);
  const change = (value) => { current = value; listeners.forEach((fn) => fn(current)); };
  reader.show(); return { page, reader, change, get released() { return released; }, app };
}

test('leaving the page discards a late business response and releases its view', async () => {
  const f = readerFixture(); let finish;
  const work = f.reader.run(() => new Promise((resolve) => finish = resolve), (value) => f.page.setData({ body: value }));
  await new Promise(setImmediate); f.reader.hide(); finish('PRIVATE-OLD-BODY'); await work;
  assert.equal(f.page.data.body, null); assert.ok(f.released > 0);
});

test('account change clears content immediately and rejects a pending response', async () => {
  const f = readerFixture(); let finish;
  const work = f.reader.run(() => new Promise((resolve) => finish = resolve), (value) => f.page.setData({ body: value }));
  await new Promise(setImmediate); f.change({ generation: 2, authenticated: false, context: null }); finish('PRIVATE-OLD-BODY'); await work;
  assert.equal(f.page.data.body, null); assert.ok(f.page.data.error.includes('登录'));
});

test('new workspace context invalidates an old read even when the account generation is unchanged', async () => {
  const f = readerFixture(); let finish;
  const work = f.reader.run(() => new Promise((resolve) => finish = resolve), (value) => f.page.setData({ body: value }));
  await new Promise(setImmediate); f.change(state()); finish('OLDER-CONTEXT'); await work;
  assert.equal(f.page.data.body, null);
});

test('lost school permission is checked before business reads and disposing prevents UI writes', async () => {
  const f = readerFixture(); let called = false;
  const restricted = state(); restricted.context.grantedPermissions = []; f.change(restricted);
  await f.reader.run(async () => { called = true; }, () => {}); assert.equal(called, false); assert.ok(f.page.data.error);
  f.reader.dispose(); f.page.setData = () => { throw new Error('disposed page updated'); };
  f.change(state()); await f.reader.run(async () => 'never', () => {});
});
