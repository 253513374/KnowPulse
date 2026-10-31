const test = require('node:test');
const assert = require('node:assert/strict');
const createSession = require('../utils/session');

const configuration = { apiBaseUrl: 'https://api.example.invalid', portalBaseUrl: 'https://portal.example.invalid', appKey: 'main', clientId: 'KnowPulse_WeChat_main' };
const bindingId = '11111111-1111-1111-1111-111111111111';
const accountUserId = '22222222-2222-2222-2222-222222222222';
const tenantId = '33333333-3333-3333-3333-333333333333';
const schoolId = '44444444-4444-4444-4444-444444444444';
const bindingCode = bindingId.replaceAll('-', '') + '.' + 'a'.repeat(43);
const metadata = { bindingId, accountUserId, tenantId };
test('account assistance uses a fixed public portal path without identity, credentials or arbitrary destinations', () => {
  const f = fixture(metadata);
  assert.equal(f.session.portalUrl('/account/assistance'), configuration.portalBaseUrl + '/account/assistance');
  assert.equal(f.session.portalUrl('/account/assistance?accountUserId=' + accountUserId), null);
  assert.equal(f.session.portalUrl('//other.example.invalid/account/assistance'), null);
  assert.equal(f.session.portalUrl('https://other.example.invalid/account/assistance'), null);
  assert.equal(f.session.portalUrl('/account/assistance#bindingCode=secret'), null);
});
test('teaching relation business rejection retains identity without retry while real denial clears it', async () => {
  const f = fixture(metadata); await f.session.login(); let calls = 0;
  f.state.hook = (o) => { calls++; o.success({ statusCode: 403, data: { error: { code: 'KnowPulse:TeachingRelationRequest:Changed', message: 'PRIVATE-REMOTE' } } }); return true; };
  await assert.rejects(f.session.request('/api/app/education-school/submit-relation-request/' + schoolId, 'POST', {}), { code: 'RELATION_REJECTED' }); assert.equal(calls, 1); assert.ok(f.session.getState().context);
  await assert.rejects(f.session.request('/api/app/education-school/my-relation-request/' + bindingId), { code: 'FORBIDDEN' }); assert.equal(f.session.getState().context, null);
});
test('invitation source rejection keeps the verified account and never retries or exposes remote reasons', async () => {
  const f = fixture(metadata); await f.session.login(); let calls = 0;
  f.state.hook = (o) => { calls++; o.success({ statusCode: 403, data: { error: { code: 'KnowPulse:SchoolInvitation:Unavailable', message: 'PRIVATE-REMOTE-REASON' } } }); return true; };
  await assert.rejects(f.session.request('/api/app/education-school/respond-to-invitation/' + bindingId, 'POST', {}), { code: 'INVITATION_REJECTED' });
  assert.equal(calls, 1); assert.ok(f.session.getState().context);
  await assert.rejects(f.session.request('/api/app/education-school/my-invitation/' + bindingId), { code: 'FORBIDDEN' }); assert.equal(f.session.getState().context, null);
});
test('student growth snapshot or busy-source failures preserve identity without retry and real denial clears it', async () => {
  const f = fixture(metadata); await f.session.login(); let calls = 0;
  f.state.hook = (o) => { calls++; o.success({ statusCode: 403, data: { error: { code: 'KnowPulse:LearningReport:SourceChanged', message: 'PRIVATE-REMOTE' } } }); return true; };
  await assert.rejects(f.session.request(`/api/app/education-student-growth/my-point-evidence-for-school?contextId=${bindingId}&schoolId=${schoolId}`), { code: 'GROWTH_CHANGED' }); assert.equal(calls, 1); assert.ok(f.session.getState().context);
  f.state.hook = (o) => { o.success({ statusCode: 409, data: null }); return true; };
  await assert.rejects(f.session.request(`/api/app/education-student-growth/my-growth-for-school?contextId=${bindingId}&schoolId=${schoolId}`), { code: 'GROWTH_UPDATING' }); assert.ok(f.session.getState().context);
  f.state.hook = (o) => { o.success({ statusCode: 403, data: { error: { code: 'Volo.Authorization' } } }); return true; };
  await assert.rejects(f.session.request(`/api/app/education-student-growth/my-contexts-for-school/${schoolId}`), { code: 'FORBIDDEN' }); assert.equal(f.session.getState().context, null);
});
test('exact original-result release change is bounded only on a pinned growth result link', async () => {
  const f = fixture(metadata); await f.session.login();
  f.state.hook = (o) => { o.success({ statusCode: 403, data: { error: { code: 'KnowPulse:LearningReport:SourceChanged' } } }); return true; };
  await assert.rejects(f.session.request(`/api/app/education-assessment/my-result-for-school?recipientId=${bindingId}&schoolId=${schoolId}&expectedResultReleaseId=${accountUserId}`), { code: 'GROWTH_CHANGED' }); assert.ok(f.session.getState().context);
  await assert.rejects(f.session.request(`/api/app/education-assessment/my-result-for-school?recipientId=${bindingId}&schoolId=${schoolId}`), { code: 'FORBIDDEN' });
  assert.equal(f.session.portalUrl('/education/growth'), configuration.portalBaseUrl + '/education/growth'); assert.equal(f.session.portalUrl('/education/growth?snapshotId=' + bindingId), null);
});
test('notebook upload binds exact note/version/operation and keeps encoded original filename off the URL', async () => {
  const f = fixture(metadata); await f.session.login(); let sent;
  f.platform.env = { USER_DATA_PATH: 'wxfile://owned' };
  f.platform.uploadFile = (o) => { sent = o; o.success({ statusCode: 200, data: JSON.stringify({ operationId: schoolId }) }); };
  const own = require('../utils/answer-store').notebookFilePath(f.platform, schoolId, 'pdf');
  await f.session.uploadNotebookFile(bindingId, accountUserId, schoolId, own, '我的反思.pdf');
  assert.ok(sent.url.endsWith(`/notebooks/${bindingId}/versions/${accountUserId}/attachments/operations/${schoolId}`)); assert.equal(sent.header['X-KnowPulse-File-Name'], encodeURIComponent('我的反思.pdf')); assert.equal(sent.header.__tenant, tenantId);
  await assert.rejects(f.session.uploadNotebookFile(bindingId, accountUserId, schoolId, 'wxfile://external.pdf', 'a.pdf'), { code: 'INVALID_PATH' });
  await assert.rejects(f.session.uploadNotebookFile(bindingId, accountUserId, schoolId, own, '../secret.pdf'), { code: 'FILE_INVALID' });
  assert.equal(f.session.portalUrl('/education/notebook'), configuration.portalBaseUrl + '/education/notebook'); assert.equal(f.session.portalUrl('/education/notebook?token=secret'), null);
});
test('notebook CAS refusal preserves the original editor but actual ownership denial clears it', async () => {
  const f = fixture(metadata); await f.session.login();
  f.state.hook = (o) => { o.success({ statusCode: 403, data: { error: { code: 'KnowPulse:PersonalNotebook:Changed' } } }); return true; };
  await assert.rejects(f.session.request(`/api/app/personal-notebook/${bindingId}/save`, 'POST', {}), { code: 'NOTE_REJECTED' }); assert.ok(f.session.getState().context);
  f.state.hook = (o) => { o.success({ statusCode: 403, data: { error: { code: 'Volo.Authorization' } } }); return true; };
  await assert.rejects(f.session.request(`/api/app/personal-notebook/${bindingId}`), { code: 'FORBIDDEN' }); assert.equal(f.session.getState().context, null);
});
test('bounded notebook attachment and organization refusals do not erase private pending payloads', async () => {
  const f = fixture(metadata); await f.session.login(); const generation = f.session.getState().generation;
  for (const code of ['KnowPulse:00024', 'KnowPulse:00025', 'KnowPulse:00026', 'KnowPulse:PersonalKnowledgeOrganization:InvalidOrganization']) {
    f.state.hook = (o) => { o.success({ statusCode: 403, data: { error: { code } } }); return true; };
    await assert.rejects(f.session.request(`/api/app/personal-notebook/${bindingId}/save`, 'POST', {}), { code: 'NOTE_REJECTED' }); assert.equal(f.session.getState().generation, generation); assert.ok(f.session.getState().context);
  }
});
test('practice business refusal preserves a pending writer only on exact POST paths, never on reads', async () => {
  const f = fixture(metadata); await f.session.login(); let calls = 0;
  f.state.hook = (o) => { calls++; o.success({ statusCode: 403, data: { error: { code: 'KnowPulse:Practice:SourceUnavailable', message: 'PRIVATE-REMOTE' } } }); return true; };
  await assert.rejects(f.session.request(`/api/app/education-assessment/${bindingId}/submit-my-practice`, 'POST', {}), { code: 'PRACTICE_REJECTED' });
  assert.equal(calls, 1); assert.ok(f.session.getState().context);
  await assert.rejects(f.session.request(`/api/app/education-assessment/my-practice-for-school?practiceId=${bindingId}&schoolId=${schoolId}`), { code: 'FORBIDDEN' }); assert.equal(f.session.getState().context, null);
});
test('practice image transfers bind the actual round, operation and owned path and reject extra download parameters', async () => {
  const f = fixture(metadata); await f.session.login(); let uploaded, downloaded; const removed = [];
  f.platform.uploadFile = (o) => { uploaded = o; o.success({ statusCode: 200, data: JSON.stringify({ id: accountUserId }) }); };
  f.platform.env = { USER_DATA_PATH: 'wxfile://owned' };
  const own = require('../utils/answer-store').imagePath(f.platform, accountUserId, 'png');
  await f.session.uploadPracticeImage(bindingId, accountUserId, own); assert.ok(uploaded.url.endsWith(`/${bindingId}/upload-my-practice-image/${accountUserId}`)); assert.equal(uploaded.header.Authorization, 'Bearer PRIVATE-ACCESS-TOKEN');
  await assert.rejects(f.session.uploadPracticeImage(bindingId, accountUserId, 'wxfile://external.png'), { code: 'INVALID_PATH' });
  f.platform.getFileSystemManager = () => ({ unlink(o) { removed.push(o.filePath); o.success(); } });
  f.platform.downloadFile = (o) => { downloaded = o; o.success({ statusCode: 200, tempFilePath: 'wxfile://practice' }); };
  const route = `/api/education/practices/${bindingId}/images/${accountUserId}`;
  assert.equal(await f.session.download(route), 'wxfile://practice'); assert.ok(downloaded.url.endsWith(route)); f.session.releaseFile('wxfile://practice');
  await assert.rejects(f.session.download(route + '?external=private'), { code: 'INVALID_PATH' }); assert.ok(removed.includes('wxfile://practice'));
  const handoff = `/education/practice?practiceId=${bindingId}`; assert.equal(f.session.portalUrl(handoff), configuration.portalBaseUrl + handoff); assert.equal(f.session.portalUrl(handoff + '&token=private'), null);
});
test('collection write refusals preserve identity only on the exact write route and never retry', async () => {
  const f = fixture(metadata); await f.session.login(); let calls = 0;
  f.state.hook = (o) => { calls++; o.success({ statusCode: 403, data: { error: { code: 'KnowPulse:QuestionCollection:SourceUnavailable', message: 'PRIVATE-REMOTE' } } }); return true; };
  await assert.rejects(f.session.request('/api/app/education-assessment/set-my-collection/' + bindingId, 'POST', {}), { code: 'COLLECTION_REJECTED' }); assert.equal(calls, 1); assert.ok(f.session.getState().context);
  await assert.rejects(f.session.request('/api/app/education-assessment/my-collection?recipientId=' + bindingId), { code: 'FORBIDDEN' }); assert.equal(f.session.getState().context, null);
});
test('only bounded teacher-note write business failures preserve the editor identity', async () => {
  for (const endpoint of ['observation-for-school', 'save-phrase']) {
    const f = fixture(metadata); await f.session.login(); let calls = 0;
    f.state.hook = (o) => { calls++; o.success({ statusCode: 403, data: { error: { code: 'KnowPulse:EducationFeedback:OperationConflict', message: 'PRIVATE-REMOTE' } } }); return true; };
    await assert.rejects(f.session.request(`/api/app/education-feedback/${endpoint}/${schoolId}`, 'POST', {}), { code: 'TEACHER_NOTE_REJECTED' });
    assert.equal(calls, 1); assert.ok(f.session.getState().context);
    await assert.rejects(f.session.request('/api/app/education-feedback/observations-for-school/schoolId=' + schoolId), { code: 'FORBIDDEN' }); assert.equal(f.session.getState().context, null);
  }
});
const workspace = (roles = [1, 2]) => ({ accountUserId, tenantId, grantedPermissions: [], workspaces: roles.map((role) => ({ schoolId, schoolName: '合成学校', role, classes: [] })) });
const tick = () => new Promise(setImmediate);
const imageRoute = `/api/education/assessment/attempts/${bindingId}/images/${accountUserId}`;
const studyFileRoute = `/api/education/study/recipients/${bindingId}/materials/${accountUserId}/attachments/${schoolId}`;
const planFileRoute = `/api/education/teaching/activities/${bindingId}/materials/${accountUserId}/attachments/${schoolId}`;

test('plan material downloads pin all three ids and remove a denied tempfile without exposing a public URL', async () => {
  const f = fixture(metadata); await f.session.login(); let sent; const removed = [];
  f.platform.getFileSystemManager = () => ({ unlink(o) { removed.push(o.filePath); o.success(); } });
  f.platform.downloadFile = (o) => { sent = o; o.success({ statusCode: 200, tempFilePath: 'wxfile://plan' }); };
  assert.equal(await f.session.download(planFileRoute), 'wxfile://plan'); assert.equal(sent.header.Authorization, 'Bearer PRIVATE-ACCESS-TOKEN'); assert.equal(sent.header.__tenant, tenantId); assert.ok(sent.url.endsWith(planFileRoute));
  f.session.releaseFile('wxfile://plan'); await assert.rejects(f.session.download(planFileRoute + '?external=https://other.invalid'), { code: 'INVALID_PATH' });
  f.platform.downloadFile = (o) => o.success({ statusCode: 403, tempFilePath: 'wxfile://plan-denied' });
  await assert.rejects(f.session.download(planFileRoute), { code: 'FORBIDDEN' }); assert.ok(removed.includes('wxfile://plan-denied')); assert.equal(f.session.getState().context, null);
});

test('bounded plan activity refusal keeps the original editing identity but cannot authorize a material read', async () => {
  const f = fixture(metadata); await f.session.login();
  f.state.hook = (o) => { o.success({ statusCode: 403, data: { error: { code: 'KnowPulse:TeachingPlan:MaterialUnavailable', message: 'PRIVATE-REMOTE' } } }); return true; };
  await assert.rejects(f.session.request('/api/app/education-teaching-plan/save-my-activity/' + bindingId, 'POST', {}), { code: 'PLAN_REJECTED' }); assert.ok(f.session.getState().context);
  await assert.rejects(f.session.request('/api/app/education-teaching-plan/my-material?activityId=' + bindingId + '&materialId=' + accountUserId), { code: 'FORBIDDEN' }); assert.equal(f.session.getState().context, null);
});
test('teacher follow-up business refusal retains pending editing only for POST and safe Web paths carry no tokens', async () => {
  const f = fixture(metadata); await f.session.login();
  f.state.hook = (o) => { o.success({ statusCode: 403, data: { error: { code: 'KnowPulse:TeachingPlan:SourceChanged' } } }); return true; };
  const route = `/api/app/education-teaching-plan/save-activity-follow-up-for-school?activityId=${bindingId}&schoolId=${schoolId}&teachingAssignmentId=${accountUserId}`;
  await assert.rejects(f.session.request(route, 'POST', {}), { code: 'PLAN_REJECTED' }); assert.ok(f.session.getState().context);
  await assert.rejects(f.session.request(route), { code: 'FORBIDDEN' }); assert.equal(f.session.getState().context, null);
  const path = `/education/teaching-plans/${bindingId}/progress`; assert.equal(f.session.portalUrl(path), configuration.portalBaseUrl + path); assert.equal(f.session.portalUrl(path + '?token=private'), null);
});

test('only a reminder mutation business refusal retains context; the same code on a read still denies access', async () => {
  const f = fixture(metadata); await f.session.login();
  f.state.hook = (o) => { o.success({ statusCode: 403, data: { error: { code: 'KnowPulse:EducationAssessment:ReminderUnavailable' } } }); return true; };
  const route = `/api/app/education-assessment/send-reminder?recipientId=${bindingId}&schoolId=${schoolId}`;
  await assert.rejects(f.session.request(route, 'POST', {}), { code: 'REMINDER_REJECTED' }); assert.ok(f.session.getState().context);
  await assert.rejects(f.session.request(route), { code: 'FORBIDDEN' }); assert.equal(f.session.getState().context, null);
});

test('workload Web handoff permits only exact plan publication and correction delivery routes', () => {
  const f = fixture();
  const plan = `/education/teaching-plans/${bindingId}/activities?versionId=${accountUserId}`;
  const correction = `/education/corrections/my?deliveryId=${schoolId}`;
  assert.equal(f.session.portalUrl(plan), configuration.portalBaseUrl + plan); assert.equal(f.session.portalUrl(correction), configuration.portalBaseUrl + correction);
  for (const route of [null, {}, plan + '&token=private', correction + '&next=https://external.invalid', '/education/teaching-plans/' + bindingId, plan.replace(accountUserId, '00000000-0000-0000-0000-000000000000'), '//external.invalid'])
    assert.equal(f.session.portalUrl(route), null);
});

test('a bounded workload quota refusal retains identity while an authorization denial still clears projections', async () => {
  const f = fixture(metadata); await f.session.login();
  f.state.hook = (o) => { o.success({ statusCode: 403, data: { error: { code: 'KnowPulse:LearningWorkload:Capacity', message: 'PRIVATE-REMOTE' } } }); return true; };
  await assert.rejects(f.session.request('/api/app/education-teaching-plan/my-learning-workload?schoolId=' + schoolId), { code: 'WORKLOAD_REJECTED' });
  assert.ok(f.session.getState().context); assert.equal(f.session.getState().termination, null);
  await assert.rejects(f.session.request('/api/app/education-teaching-plan/my-learning-workload/another-resource'), { code: 'FORBIDDEN' }); assert.equal(f.session.getState().context, null);
});

test('study downloads pin all recipient/material/attachment ids, bearer and tenant and clean denied files', async () => {
  const f = fixture(metadata); await f.session.login(); const removed = []; let sent;
  f.platform.getFileSystemManager = () => ({ unlink(o) { removed.push(o.filePath); o.success(); } });
  f.platform.downloadFile = (o) => { sent = o; o.success({ statusCode: 200, tempFilePath: 'wxfile://study' }); };
  assert.equal(await f.session.download(studyFileRoute), 'wxfile://study'); assert.ok(sent.url.endsWith(studyFileRoute));
  assert.equal(sent.header.Authorization, 'Bearer PRIVATE-ACCESS-TOKEN'); assert.equal(sent.header.__tenant, tenantId);
  f.session.releaseFile('wxfile://study'); assert.deepEqual(removed, ['wxfile://study']);
  await assert.rejects(f.session.download(studyFileRoute + '?url=https://external.invalid'), { code: 'INVALID_PATH' });
  f.platform.downloadFile = (o) => o.success({ statusCode: 403, tempFilePath: 'wxfile://denied' });
  await assert.rejects(f.session.download(studyFileRoute), { code: 'FORBIDDEN' }); assert.ok(removed.includes('wxfile://denied')); assert.equal(f.session.getState().context, null);
});

test('study business refusals preserve pending editing, while material authorization denial still clears identity projections', async () => {
  const f = fixture(metadata); await f.session.login();
  f.state.hook = (o) => { o.success({ statusCode: 403, data: { error: { code: 'KnowPulse:StudyTask:Closed', message: 'PRIVATE-REMOTE' } } }); return true; };
  await assert.rejects(f.session.request('/api/app/education-study-task/save-my-progress/' + bindingId, 'POST', {}), { code: 'STUDY_REJECTED' });
  assert.ok(f.session.getState().context);
  await assert.rejects(f.session.request('/api/app/education-study-task/my-material?recipientId=' + bindingId + '&materialId=' + accountUserId), { code: 'FORBIDDEN' });
  assert.equal(f.session.getState().context, null);
});

function fixture(initialMetadata = null) {
  const storage = new Map(); const requests = []; let count = 0;
  const state = { hook: null, throwStorage: false, workspace: workspace() };
  const platform = {
    getStorageSync: () => initialMetadata,
    getStorageInfoSync: () => ({ keys: [...storage.keys()] }),
    removeStorageSync: (key) => storage.delete(key),
    setStorageSync(key, value) { if (state.throwStorage) throw new Error('unavailable'); storage.set(key, structuredClone(value)); },
    login(options) { options.success({ code: 'one-time-' + (++count) }); },
    request(options) {
      requests.push(options);
      if (state.hook && state.hook(options)) return;
      const data = options.url.endsWith('/exchange') ? metadata : options.url.endsWith('/connect/token')
        ? { access_token: 'PRIVATE-ACCESS-TOKEN', token_type: 'Bearer', expires_in: 300 }
        : options.url.endsWith('/my-workspace') ? structuredClone(state.workspace) : {};
      options.success({ statusCode: 200, data });
    },
  };
  const observed = [];
  const session = createSession(platform, configuration, (value) => observed.push(structuredClone(value)));
  return { session, requests, storage, state, platform, observed };
}
test('analysis handoff pins the portal report entry and exact evidence root, without credential or query escapes', () => {
  const f = fixture(); assert.equal(f.session.portalUrl('/education/learning-reports'), configuration.portalBaseUrl + '/education/learning-reports');
  assert.equal(f.session.portalUrl('/education/evidence/' + bindingId), configuration.portalBaseUrl + '/education/evidence/' + bindingId);
  for (const p of ['/education/learning-reports?token=private', '/education/evidence/' + bindingId + '?next=other', '/education/evidence/not-a-guid', '//other.invalid/education/learning-reports']) assert.equal(f.session.portalUrl(p), null);
});
test('a bounded analysis source-change refusal keeps identity but cannot disguise another report authorization denial', async () => {
  const f = fixture(metadata); await f.session.login();
  f.state.hook = (o) => { o.success({ statusCode: 403, data: { error: { code: 'KnowPulse:LearningReport:SourceChanged', message: 'PRIVATE-REMOTE' } } }); return true; };
  await assert.rejects(f.session.request(`/api/app/education-learning-report/student?reportId=${bindingId}&snapshotId=${accountUserId}&studentUserId=${accountUserId}`), { code: 'REPORT_UNAVAILABLE' }); assert.ok(f.session.getState().context);
  await assert.rejects(f.session.request(`/api/app/education-learning-report/trend?reportId=${bindingId}&snapshotId=${accountUserId}`), { code: 'REPORT_UNAVAILABLE' }); assert.ok(f.session.getState().context);
  await assert.rejects(f.session.request('/api/app/education-learning-report/for-school?reportId=' + bindingId), { code: 'FORBIDDEN' }); assert.equal(f.session.getState().context, null);
});
test('report read concurrency is distinguished from a write conflict without auto retrying or clearing identity', async () => {
  const f = fixture(metadata); await f.session.login(); let calls = 0;
  f.state.hook = (o) => { calls++; o.success({ statusCode: 409, data: { error: { code: null } } }); return true; };
  await assert.rejects(f.session.request(`/api/app/education-learning-report/for-school?reportId=${bindingId}`), { code: 'REPORT_UPDATING' }); assert.equal(calls, 1); assert.ok(f.session.getState().context);
  await assert.rejects(f.session.request(`/api/app/education-learning-report/trend?reportId=${bindingId}`), { code: 'REPORT_UPDATING' }); assert.equal(calls, 2); assert.ok(f.session.getState().context);
  await assert.rejects(f.session.request(`/api/app/education-learning-report/refresh/${bindingId}`, 'POST', {}), { code: 'REQUEST_FAILED' }); assert.equal(calls, 3);
});

test('correction multipart pins delivery, operation, copied original, bearer and tenant', async () => {
  const f = fixture(metadata); await f.session.login(); f.platform.env = { USER_DATA_PATH: 'wxfile://owned' }; let sent;
  f.platform.uploadFile = (o) => { sent = o; o.success({ statusCode: 200, data: JSON.stringify({ id: bindingId, isUploaded: true }) }); };
  const file = `wxfile://owned/kp-assessment-${bindingId}.png`;
  assert.equal((await f.session.uploadCorrectionImage(accountUserId, bindingId, file)).id, bindingId);
  assert.equal(sent.url, configuration.apiBaseUrl + `/api/app/education-assessment/upload-my-correction-image?deliveryId=${accountUserId}&operationId=${bindingId}`);
  assert.equal(sent.filePath, file); assert.equal(sent.header.Authorization, 'Bearer PRIVATE-ACCESS-TOKEN'); assert.equal(sent.header.__tenant, tenantId); assert.equal(sent.header['content-type'], undefined);
  sent = null; await assert.rejects(f.session.uploadCorrectionImage(accountUserId, bindingId, 'wxfile://unowned.png'), { code: 'INVALID_PATH' }); assert.equal(sent, null);
});
test('bounded correction refusal preserves editing identity while an image read denial clears it and its temp file', async () => {
  const f = fixture(metadata); await f.session.login(); f.platform.env = { USER_DATA_PATH: 'wxfile://owned' };
  f.platform.uploadFile = (o) => o.success({ statusCode: 403, data: JSON.stringify({ error: { code: 'KnowPulse:Correction:Closed', message: 'PRIVATE-REMOTE' } }) });
  await assert.rejects(f.session.uploadCorrectionImage(accountUserId, bindingId, `wxfile://owned/kp-assessment-${bindingId}.png`), { code: 'CORRECTION_REJECTED' }); assert.ok(f.session.getState().context);
  f.state.hook = (o) => { o.success({ statusCode: 403, data: { error: { code: 'KnowPulse:Correction:Changed' } } }); return true; };
  await assert.rejects(f.session.request('/api/app/education-assessment/preview-my-correction/' + bindingId, 'POST', {}), { code: 'CORRECTION_REJECTED' });
  const deleted = []; f.platform.getFileSystemManager = () => ({ unlink(o) { deleted.push(o.filePath); o.success(); } });
  f.platform.downloadFile = (o) => { assert.equal(o.header.Authorization, 'Bearer PRIVATE-ACCESS-TOKEN'); o.success({ statusCode: 403, tempFilePath: 'wxfile://denied-correction' }); };
  await assert.rejects(f.session.download(`/api/education/corrections/deliveries/${accountUserId}/images/${bindingId}`), { code: 'FORBIDDEN' }); assert.equal(f.session.getState().context, null); assert.deepEqual(deleted, ['wxfile://denied-correction']);
});

test('native multipart upload pins attempt, operation, file path, bearer and tenant without serializing a boundary header', async () => {
  const f = fixture(metadata); await f.session.login(); f.platform.env = { USER_DATA_PATH: 'wxfile://owned' }; let sent;
  f.platform.uploadFile = (options) => { sent = options; options.success({ statusCode: 200, data: JSON.stringify({ id: bindingId, isUploaded: true }) }); };
  const body = { operationId: bindingId, paperQuestionId: accountUserId, contentHash: 'a'.repeat(64) };
  const received = await f.session.uploadAnswerImage(tenantId, body, `wxfile://owned/kp-assessment-${bindingId}.png`);
  assert.equal(received.isUploaded, true); assert.equal(sent.name, 'file'); assert.equal(sent.header.Authorization, 'Bearer PRIVATE-ACCESS-TOKEN'); assert.equal(sent.header.__tenant, tenantId);
  assert.equal(sent.header['content-type'], undefined); assert.ok(sent.url.endsWith(`/${tenantId}?OperationId=${bindingId}&PaperQuestionId=${accountUserId}&ContentHash=${'a'.repeat(64)}`));
  sent = null; await assert.rejects(f.session.uploadAnswerImage(tenantId, body, 'wxfile://other-account.png'), { code: 'INVALID_PATH' }); assert.equal(sent, null);
});

test('a late multipart response after logout cannot restore a former session or produce a valid client receipt', async () => {
  const f = fixture(metadata); await f.session.login(); f.platform.env = { USER_DATA_PATH: 'wxfile://owned' }; let finish;
  f.platform.uploadFile = (options) => finish = options;
  const pending = f.session.uploadAnswerImage(tenantId, { operationId: bindingId, paperQuestionId: accountUserId, contentHash: 'a'.repeat(64) }, `wxfile://owned/kp-assessment-${bindingId}.png`);
  await f.session.logout(); finish.success({ statusCode: 200, data: JSON.stringify({ id: bindingId }) }); await assert.rejects(pending, { code: 'STALE_RESPONSE' }); assert.equal(f.session.getState().authenticated, false);
});

test('local private cleanup failure is reported after logout without preventing token revocation or exposing a session', async () => {
  const f = fixture(metadata); await f.session.login();
  f.storage.set(`knowpulse:answers:${encodeURIComponent(configuration.apiBaseUrl)}:${configuration.appKey}:${configuration.clientId}:${tenantId}:${accountUserId}:draft`, { private: 'LOCAL' });
  f.platform.removeStorageSync = () => { throw new Error('blocked'); };
  await assert.rejects(f.session.logout(), { code: 'LOCAL_CLEANUP_UNCONFIRMED' }); assert.equal(f.session.getState().authenticated, false); assert.equal(f.session.getState().cleanupWarning, true);
  assert.ok(f.requests.some((r) => r.url.endsWith('/connect/revocat')));
});

test('bounded ABP editing-rule 403 preserves identity and drafts while true authorization denial still clears the workspace', async () => {
  const f = fixture(metadata); await f.session.login();
  f.state.hook = (o) => { o.success({ statusCode: 403, data: { error: { code: 'KnowPulse:EducationAssessment:AttemptClosed', message: 'PRIVATE-REMOTE' } } }); return true; };
  await assert.rejects(f.session.request('/api/app/education-assessment/save-response/' + bindingId, 'POST', {}), { code: 'ASSESSMENT_REJECTED' });
  assert.equal(f.session.getState().authenticated, true); assert.ok(f.session.getState().context); assert.equal(f.session.getState().termination, null);
  await assert.rejects(f.session.request('/api/app/education-assessment/attempt/' + bindingId), { code: 'FORBIDDEN' }); assert.equal(f.session.getState().context, null);
});

test('multipart editing-rule refusal preserves the authenticated editor rather than deleting local image recovery state', async () => {
  const f = fixture(metadata); await f.session.login(); f.platform.env = { USER_DATA_PATH: 'wxfile://owned' };
  f.platform.uploadFile = (o) => o.success({ statusCode: 403, data: JSON.stringify({ error: { code: 'KnowPulse:EducationAssessment:ImageLimitExceeded' } }) });
  await assert.rejects(f.session.uploadAnswerImage(tenantId, { operationId: bindingId, paperQuestionId: accountUserId, contentHash: 'a'.repeat(64) }, `wxfile://owned/kp-assessment-${bindingId}.png`), { code: 'ASSESSMENT_REJECTED' });
  assert.equal(f.session.getState().authenticated, true); assert.ok(f.session.getState().context);
});

test('a resource authorization refusal hides the workspace without deleting unrelated personal answer drafts', async () => {
  const f = fixture(metadata); await f.session.login(); const key = `knowpulse:answers:${encodeURIComponent(configuration.apiBaseUrl)}:${configuration.appKey}:${configuration.clientId}:${tenantId}:${accountUserId}:another-recipient:draft`;
  f.storage.set(key, { version: 1, draft: 'OWN-UNSAVED-WORK' });
  f.state.hook = (o) => { o.success({ statusCode: 403, data: { error: { code: 'Volo.Authorization:010002' } } }); return true; };
  await assert.rejects(f.session.request('/api/app/education-assessment/attempt/' + bindingId), { code: 'FORBIDDEN' });
  assert.equal(f.session.getState().context, null); assert.equal(f.storage.get(key).draft, 'OWN-UNSAVED-WORK');
});

test('private image download pins the bearer and tenant, then releases only owned temporary files', async () => {
  const f = fixture(metadata); await f.session.login(); const removed = []; let request;
  f.platform.getFileSystemManager = () => ({ unlink(options) { removed.push(options.filePath); options.success(); } });
  f.platform.downloadFile = (options) => { request = options; options.success({ statusCode: 200, tempFilePath: 'wxfile://owned-answer' }); };
  const path = await f.session.download(imageRoute);
  assert.equal(request.url, configuration.apiBaseUrl + imageRoute); assert.equal(request.header.Authorization, 'Bearer PRIVATE-ACCESS-TOKEN');
  assert.equal(request.header.__tenant, tenantId); f.session.releaseFile('wxfile://someone-else'); assert.deepEqual(removed, []);
  f.session.releaseFile(path); f.session.releaseFile(path); assert.deepEqual(removed, [path]);
  assert.ok(!JSON.stringify([...f.storage.entries()]).includes('owned-answer'));
});

test('logout clears downloaded originals and discards an image delivered after logout', async () => {
  const f = fixture(metadata); await f.session.login(); const removed = []; let pending;
  f.platform.getFileSystemManager = () => ({ unlink(options) { removed.push(options.filePath); options.success(); } });
  f.platform.downloadFile = (options) => { options.success({ statusCode: 200, tempFilePath: 'wxfile://first' }); };
  await f.session.download(imageRoute);
  f.platform.downloadFile = (options) => { pending = options; };
  const late = f.session.download(imageRoute); const rejected = assert.rejects(late, { code: 'STALE_RESPONSE' });
  await f.session.logout(); assert.deepEqual(removed, ['wxfile://first']);
  pending.success({ statusCode: 200, tempFilePath: 'wxfile://late' }); await rejected;
  assert.deepEqual(removed, ['wxfile://first', 'wxfile://late']);
});

test('image authorization failure clears current projections and removes its returned temporary file', async () => {
  for (const statusCode of [401, 403]) {
    const f = fixture(metadata); await f.session.login(); const removed = [];
    f.platform.getFileSystemManager = () => ({ unlink(options) { removed.push(options.filePath); options.success(); } });
    f.platform.downloadFile = (options) => options.success({ statusCode, tempFilePath: 'wxfile://denied' });
    await assert.rejects(f.session.download(imageRoute), { code: statusCode === 401 ? 'AUTH_REQUIRED' : 'FORBIDDEN' });
    assert.equal(f.session.getState().context, null); assert.deepEqual(removed, ['wxfile://denied']);
  }
});

test('temporary cleanup failures do not restore a logged-out session', async () => {
  const f = fixture(metadata); await f.session.login();
  f.platform.getFileSystemManager = () => ({ unlink(options) { options.fail(); } });
  f.platform.downloadFile = (options) => options.success({ statusCode: 200, tempFilePath: 'wxfile://owned' });
  await f.session.download(imageRoute); await f.session.logout(); assert.equal(f.session.getState().authenticated, false);
});

test('downloads reject arbitrary API and external destinations before sending a credential', async () => {
  const f = fixture(metadata); await f.session.login(); let called = false;
  f.platform.downloadFile = () => { called = true; };
  for (const path of ['https://other.invalid/file', '/api/other-file', imageRoute + '?redirect=other'])
    await assert.rejects(f.session.download(path), { code: 'INVALID_PATH' });
  assert.equal(called, false);
});

test('Web handoff allows only the exact grading route or account binding and carries no credential', () => {
  const session = createSession({ getStorageSync: () => null }, { ...configuration, portalBaseUrl: 'https://teacher.example.invalid' });
  assert.equal(session.portalUrl('/education/assessment-grading/' + bindingId), 'https://teacher.example.invalid/education/assessment-grading/' + bindingId);
  for (const path of ['//other.invalid', '/education/assessment-grading/' + bindingId + '?token=secret', '/api/identity/users']) assert.equal(session.portalUrl(path), null);
});

test('binding uses separate one-time WeChat codes and stores only identity metadata', async () => {
  const f = fixture(); const result = await f.session.bindAndLogin(bindingCode);
  assert.equal(result.authenticated, true); assert.equal(result.busy, false);
  const exchange = f.requests.find((item) => item.url.endsWith('/exchange'));
  const token = f.requests.find((item) => item.url.endsWith('/connect/token'));
  assert.equal(exchange.data.code, 'one-time-1'); assert.equal(new URLSearchParams(token.data).get('code'), 'one-time-2');
  assert.equal(new URLSearchParams(token.data).get('scope'), 'KnowPulse'); assert.equal(token.header.__tenant, tenantId);
  assert.deepEqual([...f.storage.values()], [metadata]);
  const stored = JSON.stringify([...f.storage.entries()]); assert.ok(!stored.includes(bindingCode)); assert.ok(!stored.includes('PRIVATE-ACCESS-TOKEN'));
  assert.equal(f.requests.find((item) => item.url.endsWith('/my-workspace')).header.Authorization, 'Bearer PRIVATE-ACCESS-TOKEN');
});

test('a restored binding authenticates with a fresh WeChat code without creating another binding', async () => {
  const f = fixture(metadata); await f.session.login();
  assert.equal(f.requests.filter((item) => item.url.endsWith('/exchange')).length, 0);
  assert.equal(f.storage.size, 0); assert.equal(f.session.getState().context.accountUserId, accountUserId);
});

test('an unknown binding response is not automatically retried and can use the original code with a new WeChat proof', async () => {
  const f = fixture(); let failed = false;
  f.state.hook = (options) => { if (!failed && options.url.endsWith('/exchange')) { failed = true; options.fail({}); return true; } return false; };
  await assert.rejects(f.session.bindAndLogin(bindingCode), { code: 'NETWORK_UNCONFIRMED' });
  assert.equal(f.requests.length, 1); assert.equal(f.session.getState().authenticated, false);
  await f.session.bindAndLogin(bindingCode);
  const exchanges = f.requests.filter((item) => item.url.endsWith('/exchange'));
  assert.equal(exchanges.length, 2); assert.equal(exchanges[0].data.bindingCode, exchanges[1].data.bindingCode);
  assert.notEqual(exchanges[0].data.code, exchanges[1].data.code);
});

test('logout prevents a delayed token response from restoring the previous identity', async () => {
  const f = fixture(metadata); let pending;
  f.state.hook = (options) => { if (options.url.endsWith('/connect/token')) { pending = options; return true; } return false; };
  const login = f.session.login(); await tick(); assert.ok(pending);
  await f.session.logout(); pending.success({ statusCode: 200, data: { access_token: 'OLD-TOKEN', token_type: 'Bearer', expires_in: 300 } });
  await assert.rejects(login, { code: 'STALE_RESPONSE' });
  assert.equal(f.session.getState().authenticated, false); assert.equal(f.session.getState().context, null); assert.equal(f.session.getState().busy, false);
});

test('only the latest workspace refresh can replace the current projection', async () => {
  const f = fixture(metadata); await f.session.login(); const pending = [];
  f.state.hook = (options) => { if (options.url.endsWith('/my-workspace')) { pending.push(options); return true; } return false; };
  const first = f.session.refreshWorkspace(); const second = f.session.refreshWorkspace();
  assert.equal(f.session.getState().context, null);
  pending[1].success({ statusCode: 200, data: workspace([2]) }); const current = await second; assert.equal(current.busy, false);
  pending[0].success({ statusCode: 200, data: workspace([1]) }); await assert.rejects(first, { code: 'STALE_RESPONSE' });
  assert.deepEqual(f.session.getState().context.workspaces.map((item) => item.role), [2]);
});

test('a denied mutation is not retried and clears the cached workspace', async () => {
  const f = fixture(metadata); await f.session.login();
  f.state.hook = (options) => { if (options.url.endsWith('/api/private-command')) { options.success({ statusCode: 403, data: {} }); return true; } return false; };
  await assert.rejects(f.session.request('/api/private-command', 'POST', { operationId: 'original' }), { code: 'FORBIDDEN' });
  assert.equal(f.requests.filter((item) => item.url.endsWith('/api/private-command')).length, 1);
  assert.equal(f.session.getState().authenticated, true); assert.equal(f.session.getState().context, null);
  f.state.workspace = workspace([2]); await f.session.refreshWorkspace(); assert.equal(f.session.getState().busy, false);
});

test('an expired session removes the token and all workspace projections', async () => {
  const f = fixture(metadata); await f.session.login();
  f.state.hook = (options) => { options.success({ statusCode: 401, data: {} }); return true; };
  await assert.rejects(f.session.request('/api/private'), { code: 'AUTH_REQUIRED' });
  assert.equal(f.session.getState().authenticated, false); assert.equal(f.session.getState().context, null);
});

test('logout clears local data immediately and revokes the access token without unbinding the account', async () => {
  const f = fixture(metadata); await f.session.login(); let pending;
  f.state.hook = (options) => { if (options.url.endsWith('/connect/revocat')) { pending = options; return true; } return false; };
  const logout = f.session.logout(); assert.equal(f.session.getState().authenticated, false); assert.equal(f.session.getState().context, null);
  assert.equal(f.session.getState().hasBinding, true); assert.equal(pending.header.Authorization, undefined);
  assert.equal(new URLSearchParams(pending.data).get('token'), 'PRIVATE-ACCESS-TOKEN'); assert.equal(pending.header.__tenant, tenantId);
  pending.success({ statusCode: 200, data: {} }); await logout;
});

test('failed server logout is reported while the device remains signed out', async () => {
  const f = fixture(metadata); await f.session.login();
  f.state.hook = (options) => { options.fail({}); return true; };
  await assert.rejects(f.session.logout(), { code: 'LOGOUT_UNCONFIRMED' }); assert.equal(f.session.getState().authenticated, false);
});

test('metadata storage failure does not lose the confirmed binding within the current session', async () => {
  const f = fixture(); f.state.throwStorage = true; await f.session.bindAndLogin(bindingCode);
  assert.equal(f.session.getState().storageWarning, true); assert.equal(f.session.getState().authenticated, true); assert.equal(f.storage.size, 0);
});

for (const corruption of ['account', 'tenant', 'role']) test(`a ${corruption} mismatch cannot populate the workspace`, async () => {
  const f = fixture(metadata);
  if (corruption === 'account') f.state.workspace.accountUserId = '55555555-5555-5555-5555-555555555555';
  if (corruption === 'tenant') f.state.workspace.tenantId = null;
  if (corruption === 'role') f.state.workspace.workspaces[0].role = 3;
  await assert.rejects(f.session.login(), { code: 'INVALID_RESPONSE' }); assert.equal(f.session.getState().authenticated, false); assert.equal(f.session.getState().context, null);
});

test('unconfigured clients and arbitrary destinations cannot send credentials', async () => {
  const f = fixture(metadata); const off = createSession(f.platform, { ...configuration, apiBaseUrl: '' });
  await assert.rejects(off.login(), { code: 'NOT_CONFIGURED' }); assert.equal(f.requests.length, 0);
  await f.session.login(); const count = f.requests.length;
  await assert.rejects(f.session.request('https://other.example.invalid/api'), { code: 'INVALID_PATH' }); assert.equal(f.requests.length, count);
});
