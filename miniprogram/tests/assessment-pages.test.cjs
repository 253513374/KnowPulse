const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { createRequire } = require('node:module');
const school = '11111111-1111-1111-1111-111111111111';
const task = '22222222-2222-2222-2222-222222222222';
const recipient = '33333333-3333-3333-3333-333333333333';
const release = '44444444-4444-4444-4444-444444444444';
const question = '55555555-5555-5555-5555-555555555555';
const image = '66666666-6666-6666-6666-666666666666';
const attempt = '77777777-7777-7777-7777-777777777777';
const meta = () => ({ id: recipient, taskId: task, schoolId: school, title: '合成数学测评', subjectName: '数学', schoolClassName: '一班' });
const result = () => ({ taskId: task, recipientId: recipient, resultReleaseId: release, attemptId: attempt, outcome: 0, currentResultVisibility: 2,
  totalScore: 8, maxScore: 10, questions: [{ id: question, title: '题目', stem: '冻结题干', obtainedScore: 8, maxScore: 10, response: { text: '私有原答', images: [{ attachmentId: image, rotationDegrees: 90 }] } }] });

function fixture(name, role = 2) {
  let state = { generation: 1, authenticated: true, context: { workspaces: [{ role, schoolId: school }], grantedPermissions: ['KnowPulse.Education.' + (role === 1 ? 'AssessmentManage' : 'AssessmentParticipate')] } };
  const listeners = new Set(); const requests = []; const released = []; const clipboard = [];
  const f = { read: null, download: null, platform: { navigateTo() {}, reLaunch() {}, setClipboardData(options) { clipboard.push(options.data); options.success(); } } };
  const change = (value) => { state = value; listeners.forEach((listener) => listener(state)); };
  const app = { subscribe(listener) { listeners.add(listener); return () => listeners.delete(listener); }, session: {
    getState: () => state,
    async refreshWorkspace() { const context = state.context; change({ ...state, context: null }); await Promise.resolve(); change({ ...state, context: structuredClone(context) }); return state; },
    async request(url) { requests.push(url); return f.read(url); },
    async download(url) { requests.push(url); return f.download(url); },
    releaseFile(file) { released.push(file); }, portalUrl: (route) => 'https://teacher.example.invalid' + route,
  } };
  const file = path.resolve(__dirname, '../pages', name, 'index.js'); let definition;
  new vm.Script(fs.readFileSync(file, 'utf8'), { filename: file }).runInNewContext({ Page: (value) => definition = value, require: createRequire(file), getApp: () => app, wx: f.platform });
  const page = { data: structuredClone(definition.data), setData(value) { Object.assign(this.data, value); } };
  for (const [key, value] of Object.entries(definition)) if (typeof value === 'function') page[key] = value.bind(page);
  return Object.assign(f, { page, app, change, requests, released, clipboard });
}

test('native result page loads the actual safe projection and clears text and files when hidden', async () => {
  const f = fixture('assessment-result'); f.read = (url) => url.includes('my-result') ? result() : meta();
  f.download = async () => 'wxfile://owned-image'; f.page.onLoad({ id: recipient, schoolId: school }); await f.page.onShow();
  assert.equal(f.page.data.detail.questions[0].responseText, '私有原答');
  await f.page.image({ currentTarget: { dataset: { id: image } } }); assert.equal(f.page.data.imageRotation, 90);
  f.page.onHide(); assert.equal(f.page.data.detail, null); assert.equal(f.page.data.imagePath, ''); assert.deepEqual(f.released, ['wxfile://owned-image']);
});
test('growth result navigation sends the exact expected release before any question image can be read', async () => {
  const f = fixture('assessment-result'); f.read = (url) => url.includes('my-result') ? result() : meta();
  f.page.onLoad({ id: recipient, schoolId: school, releaseId: release, questionId: question }); await f.page.onShow(); assert.ok(f.page.data.detail); assert.ok(f.requests.some((u) => u.includes('expectedResultReleaseId=' + release)));
});
test('growth result link refuses a different returned release without rendering or enabling image reads', async () => {
  const f = fixture('assessment-result'); f.read = (url) => url.includes('my-result') ? { ...result(), resultReleaseId: task } : meta(); f.download = () => { throw Error('stale result image requested'); };
  f.page.onLoad({ id: recipient, schoolId: school, releaseId: release }); await f.page.onShow(); assert.equal(f.page.data.detail, null); assert.match(f.page.data.error, /发布版本/); await f.page.image({ currentTarget: { dataset: { id: image } } });
});

test('a downloaded image arriving after leaving the result page is removed without rendering', async () => {
  const f = fixture('assessment-result'); f.read = (url) => url.includes('my-result') ? result() : meta(); let finish;
  f.download = () => new Promise((resolve) => finish = resolve);
  f.page.onLoad({ id: recipient, schoolId: school }); await f.page.onShow();
  const reading = f.page.image({ currentTarget: { dataset: { id: image } } }); f.page.onHide(); finish('wxfile://late'); await reading;
  assert.equal(f.page.data.imagePath, ''); assert.deepEqual(f.released, ['wxfile://late']);
});

test('result page rejects a different school and never requests an image absent from the opened result', async () => {
  const f = fixture('assessment-result'); f.read = (url) => url.includes('my-result') ? result() : { ...meta(), schoolId: task };
  f.download = () => { throw new Error('unapproved image requested'); };
  f.page.onLoad({ id: recipient, schoolId: school }); await f.page.onShow(); assert.equal(f.page.data.detail, null); assert.ok(f.page.data.error);
  await f.page.image({ currentTarget: { dataset: { id: image } } });
});

test('teacher progress uses real task and published revision and copies only an authenticated Web continuation URL', async () => {
  const f = fixture('assessment-progress', 1);
  f.read = (url) => url.includes('grading-overview') ? { taskId: task, publishedRevisionId: release, recipientCount: 2, unstartedCount: 1, inProgressCount: 0,
    submittedCount: 1, gradingCompleteCount: 0, pendingQuestionCount: 1, confirmedGradeSetVersion: 0, submissions: [] } :
    { id: task, publishedRevisionId: release, currentRevision: { id: release, schoolId: school, title: '真实发布任务', curriculum: { subjectName: '数学' } } };
  f.page.onLoad({ id: task, schoolId: school }); await f.page.onShow();
  assert.equal(f.page.data.detail.pendingQuestionCount, 1); f.page.continueOnWeb();
  assert.deepEqual(f.clipboard, ['https://teacher.example.invalid/education/assessment-grading/' + task]);
  f.change({ generation: 2, authenticated: false, context: null }); assert.equal(f.page.data.detail, null); f.page.continueOnWeb(); assert.equal(f.clipboard.length, 1);
});

test('task list paginates the real school query and a student cannot switch it to teacher mode', async () => {
  const f = fixture('assessments'); f.read = () => ({ totalCount: 41, items: [{ ...meta(), currentResultVisibility: 0 }] });
  f.page.onLoad({ mode: 'student', schoolId: school }); await f.page.onShow(); await f.page.next();
  assert.equal(f.page.data.offset, 20); assert.ok(f.requests.at(-1).includes('SkipCount=20')); assert.equal(f.page.data.total, 41);
  const forged = fixture('assessments'); forged.read = () => { throw new Error('forged role performed a read'); };
  forged.page.onLoad({ mode: 'teacher', schoolId: school }); await forged.page.onShow(); assert.equal(forged.requests.length, 0); assert.ok(forged.page.data.error);
});

test('long full review sends only the selected question to the view and drops its in-memory result on leaving', async () => {
  const f = fixture('assessment-result'); const dto = result(); dto.maxScore = 640;
  dto.questions = Array.from({ length: 64 }, (_, i) => ({ ...result().questions[0], id: '88888888-8888-8888-8888-' + (i + 10).toString(16).padStart(12, '0'), title: '题目' + i, stem: '长题干'.repeat(10000), response: null }));
  f.read = (url) => url.includes('my-result') ? dto : meta(); f.page.onLoad({ id: recipient, schoolId: school }); await f.page.onShow();
  assert.ok(Buffer.byteLength(JSON.stringify(dto)) > 1024 * 1024);
  assert.equal(f.page.data.detail.questionCount, 64); assert.equal(f.page.data.detail.questions.length, 1);
  assert.ok(Buffer.byteLength(JSON.stringify(f.page.data)) < 1024 * 1024);
  f.page.question({ currentTarget: { dataset: { direction: 1 } } }); assert.equal(f.page.data.detail.questions[0].title, '题目1');
  f.page.onHide(); assert.equal(f.page.fullResult, null); assert.equal(f.page.data.detail, null);
});
