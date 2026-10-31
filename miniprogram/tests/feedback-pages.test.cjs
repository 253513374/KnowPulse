const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const crypto = require('node:crypto');
const { createRequire } = require('node:module');
const school = '11111111-1111-1111-1111-111111111111';
const owner = '22222222-2222-2222-2222-222222222222';
const root = '33333333-3333-3333-3333-333333333333';
const teaching = '55555555-5555-5555-5555-555555555555';
const enrollment = '66666666-6666-6666-6666-666666666666';
const member = '77777777-7777-7777-7777-777777777777';
const target = () => ({ schoolId: school, teaching: { id: teaching, schoolName: '学校', className: '一班', subjectName: '数学' }, student: { studentEnrollmentId: enrollment, schoolMembershipId: member, studentUserId: member, studentName: '同名学生', accountName: 'student-a' } });

function fixture(name = 'feedback-edit', role = 1) {
  const context = () => ({ accountUserId: owner, tenantId: null, grantedPermissions: ['KnowPulse.Education.' + (role === 1 ? 'FeedbackManage' : 'FeedbackView')], workspaces: [{ role, schoolId: school, classes: [{ relationId: teaching }] }] });
  let state = { authenticated: true, generation: 1, termination: null, context: context() }; const listeners = new Set(); const storage = new Map(); const requests = []; const receipts = new Map(); const dialogs = [];
  const f = { record: null, loseDraft: false, read: null, confirm: true, version: 0, observations: [], phraseRows: new Map(), loseNote: false };
  const change = (value) => { state = value; listeners.forEach((fn) => fn(state)); };
  const platform = { getRandomValues(options) { const b = crypto.randomBytes(16); options.success({ randomValues: b.buffer.slice(b.byteOffset, b.byteOffset + b.length) }); },
    getStorageSync: (key) => storage.get(key), setStorageSync: (key, value) => storage.set(key, structuredClone(value)), removeStorageSync: (key) => storage.delete(key),
    showModal(options) { dialogs.push(options.content); options.success({ confirm: f.confirm }); }, enableAlertBeforeUnload() {}, disableAlertBeforeUnload() {}, navigateTo(options) { f.navigation = options.url; }, setClipboardData(options) { f.clipboard = options.data; } };
  const session = { getState: () => state, async refreshWorkspace() { const old = state.context || context(); change({ ...state, context: null }); await Promise.resolve(); change({ ...state, context: structuredClone(old) }); return state; },
    async login() { change({ ...state, generation: state.generation + 1, authenticated: false, context: null, termination: 'login' }); change({ ...state, authenticated: true, context: context(), termination: null }); },
    async request(url, method, body) {
      requests.push({ url, method, body: body && structuredClone(body) });
      if (f.read) { const value = f.read(url, method, body); if (value !== undefined) return value; }
      if (url.includes('/my-workspace/')) return f.record;
      if (url.includes('/target?')) return target();
      if (url.includes('/assessment-list?')) return [];
      if (url.includes('/observation-operation?') || url.includes('/phrase-operation?')) return receipts.get(new URL('https://api.invalid' + url).searchParams.get('operationId')) || null;
      if (url.includes('/observations-for-school/') || url.includes('/phrases?')) {
        const params = new URL('https://api.invalid' + url).searchParams, skip = Number(params.get('SkipCount'));
        const all = url.includes('/phrases?') ? [...f.phraseRows.values()].filter((r) => params.get('includeArchived') === 'true' || !r.currentRevision.isArchived) : f.observations;
        return { totalCount: all.length, items: structuredClone(all.slice(skip, skip + 20)) };
      }
      if (url.includes('/phrase/')) return structuredClone(f.phraseRows.get(new URL('https://api.invalid' + url).pathname.split('/').pop()) || null);
      if (url.includes('/observation-for-school/') || url.includes('/save-phrase/')) {
        let receipt;
        if (url.includes('/observation-for-school/')) { receipt = { ...body, id: body.operationId, schoolId: school }; f.observations.unshift(receipt); }
        else {
          const id = url.split('/').pop(), previous = f.phraseRows.get(id);
          if (previous && (previous.concurrencyStamp !== body.concurrencyStamp || previous.currentRevision.id !== body.expectedRevisionId)) throw Object.assign(new Error('conflict'), { status: 409 });
          receipt = { id: body.operationId, phraseId: id, text: body.text, isArchived: body.isArchived, recordedAtUtc: new Date().toISOString(), revisionNumber: (previous?.currentRevision.revisionNumber || 0) + 1 };
          f.phraseRows.set(id, { id, concurrencyStamp: 'stamp-' + receipt.id, currentRevision: receipt });
        }
        receipts.set(receipt.id, structuredClone(receipt));
        if (f.loseNote) { f.loseNote = false; throw Object.assign(new Error('lost'), { code: 'NETWORK_UNCONFIRMED' }); }
        return structuredClone(receipt);
      }
      if (url.includes('/operation?')) return receipts.get(new URL('https://api.invalid' + url).searchParams.get('operationId')) || null;
      if (url.includes('/preview-publish/')) return { feedback: structuredClone(f.record), previewFingerprint: 'f'.repeat(64), assessments: [] };
      if (method === 'POST') {
        if (f.record && body.concurrencyStamp !== f.record.concurrencyStamp) throw Object.assign(new Error('conflict'), { status: 409 });
        const kind = url.includes('save-draft') ? 0 : url.includes('/publish/') ? 1 : 2;
        const revision = { id: body.operationId, feedbackId: root, kind, revisionNumber: ++f.version, content: body.content || (kind === 1 ? f.record.draft.content : null) };
        receipts.set(revision.id, revision);
        f.record = { ...(f.record || {}), id: root, teachingAssignmentId: teaching, studentEnrollmentId: enrollment, schoolMembershipId: member,
          studentName: '同名学生', accountName: 'student-a', className: '一班', subjectName: '数学', concurrencyStamp: 'stamp-' + f.version,
          currentRevision: revision, draft: kind === 0 ? revision : f.record.draft, published: kind === 1 ? revision : kind === 2 ? null : f.record && f.record.published,
          visibility: kind === 0 ? f.record && f.record.visibility || 0 : kind === 1 ? 2 : 3 };
        if (kind === 0 && f.loseDraft) { f.loseDraft = false; throw Object.assign(new Error('lost'), { code: 'NETWORK_UNCONFIRMED' }); }
        return revision;
      }
      throw new Error('Unexpected test request');
    }, portalUrl: (route) => 'https://teacher.example.invalid' + route };
  const app = { configuration: { apiBaseUrl: 'https://api.example.invalid', appKey: 'main', clientId: 'KnowPulse_WeChat_main' }, session, subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); } };
  const file = path.resolve(__dirname, '../pages', name, 'index.js'); let definition;
  new vm.Script(fs.readFileSync(file, 'utf8'), { filename: file }).runInNewContext({ require: createRequire(file), Page: (value) => definition = value, wx: platform, getApp: () => app });
  const page = { data: structuredClone(definition.data), setData(value) { Object.assign(this.data, value); } };
  for (const [key, value] of Object.entries(definition)) if (typeof value === 'function') page[key] = value.bind(page);
  page.onLoad({ id: root, schoolId: school, mode: role === 1 ? 'teacher' : 'student', teachingId: teaching, enrollmentId: enrollment, membershipId: member });
  const edit = (field, value) => page.change({ currentTarget: { dataset: { field } }, detail: { value } });
  return Object.assign(f, { page, session, change, storage, requests, receipts, dialogs, edit });
}

test('editor resolves an exact recipient, retains an unknown save and recovers its original cloud version', async () => {
  const f = fixture(); await f.page.onShow(); assert.equal(f.page.data.target.student.accountName, 'student-a');
  f.edit('context', '课堂'); f.edit('text', 'PUBLIC-FEEDBACK'); f.loseDraft = true; await f.page.save();
  assert.ok(f.page.data.pending); assert.equal(f.page.data.form.text, 'PUBLIC-FEEDBACK');
  assert.ok(!JSON.stringify([...f.storage.values()]).includes('PUBLIC-FEEDBACK'));
  await f.page.recover(); assert.equal(f.page.data.pending, null); assert.equal(f.page.data.record.version, 1); assert.equal(f.page.data.dirty, false);
  assert.equal(f.requests.filter((r) => r.url.includes('/save-draft/')).length, 1);
});

const noteChange = (p, field, value) => p.privateChange({ currentTarget: { dataset: { field } }, detail: { value } });
const pick = (id) => ({ currentTarget: { dataset: { id } } });

test('private notes load only after an explicit open and saving an observation neither saves nor publishes feedback', async () => {
  const f = fixture(); await f.page.onShow(); assert.equal(f.requests.filter((r) => r.url.includes('observations-for-school')).length, 0);
  f.edit('context', '课堂'); f.edit('text', '未保存学生评语'); await f.page.openPrivate(); noteChange(f.page, 'observationText', 'PRIVATE-OBS\n原文'); await f.page.saveObservation();
  assert.equal(f.observations.length, 1); assert.equal(f.record, null); assert.equal(f.page.data.observationText, ''); assert.equal(f.page.data.form.text, '未保存学生评语');
  assert.equal(f.page.data.observations[0].text, 'PRIVATE-OBS\n原文'); assert.equal(f.page.data.pending, null); assert.ok(!JSON.stringify([...f.storage.values()]).includes('PRIVATE-OBS'));
});

test('an uncertain private observation keeps its original operation and a cold editor recovers without another write', async () => {
  const f = fixture(); await f.page.onShow(); await f.page.openPrivate(); noteChange(f.page, 'observationText', 'PRIVATE-OBS'); f.loseNote = true; await f.page.saveObservation();
  const pending = structuredClone(f.page.data.pending); assert.equal(pending.kind, 'observation'); assert.ok(!JSON.stringify([...f.storage.values()]).includes('PRIVATE-OBS'));
  f.page.onUnload(); const g = fixture(); for (const [k, v] of f.storage) g.storage.set(k, v); for (const [k, v] of f.receipts) g.receipts.set(k, v); g.observations = f.observations;
  await g.page.onShow(); assert.equal(g.page.data.canRetry, false); await g.page.recover(); assert.equal(g.page.data.pending, null); assert.equal(g.requests.filter((r) => r.method === 'POST').length, 0);
});

test('selecting one private observation needs confirmation, invalidates preview and remains a separate editable draft', async () => {
  const f = fixture(); await f.page.onShow(); await f.page.openPrivate(); noteChange(f.page, 'observationText', '保留可公开这一条'); await f.page.saveObservation();
  f.edit('text', '原学生评语'); f.confirm = false; await f.page.selectObservation(pick(f.observations[0].id)); assert.equal(f.page.data.form.text, '原学生评语');
  f.confirm = true; await f.page.selectObservation(pick(f.observations[0].id)); assert.equal(f.page.data.form.text, '保留可公开这一条'); assert.equal(f.page.data.form.selectedObservationId, f.observations[0].id);
  f.edit('context', '课堂'); f.edit('text', '只保留允许公开的部分'); await f.page.save(); assert.equal(f.observations[0].text, '保留可公开这一条'); assert.equal(f.record.draft.content.text, '只保留允许公开的部分'); assert.equal(f.record.published, null);
});

test('personal phrases save in their own records and only explicit selection copies current text without auto-publishing', async () => {
  const f = fixture(); await f.page.onShow(); f.edit('context', '课堂'); f.edit('text', '学生原编辑'); await f.page.openPrivate(); noteChange(f.page, 'phraseText', '先补充推理再检查单位'); await f.page.savePhrase();
  assert.equal(f.phraseRows.size, 1); assert.equal(f.record, null); assert.equal(f.page.data.form.text, '学生原编辑'); const id = [...f.phraseRows.keys()][0];
  f.confirm = false; await f.page.usePhrase(pick(id)); assert.equal(f.page.data.form.text, '学生原编辑');
  f.confirm = true; await f.page.usePhrase(pick(id)); assert.equal(f.page.data.form.text, '先补充推理再检查单位'); assert.equal(f.page.data.form.selectedObservationId, null); assert.equal(f.record, null);
  await f.page.save(); assert.equal(f.record.draft.content.text, '先补充推理再检查单位'); assert.equal(f.record.published, null);
});

test('a cloud phrase conflict retains local text and the original baseline until explicit reread', async () => {
  const f = fixture(); await f.page.onShow(); await f.page.openPrivate(); noteChange(f.page, 'phraseText', '初稿'); await f.page.savePhrase(); const id = [...f.phraseRows.keys()][0]; await f.page.editPhrase(pick(id));
  const base = f.page.phrase.revisionId; noteChange(f.page, 'phraseText', '手机未保存'); const cloud = f.phraseRows.get(id); cloud.concurrencyStamp = 'web-new'; cloud.currentRevision = { ...cloud.currentRevision, id: crypto.randomUUID(), revisionNumber: 2, text: '网页新版本' };
  await f.page.savePhrase(); assert.ok(f.page.data.pending); assert.equal(f.page.data.phraseText, '手机未保存'); assert.equal(f.page.phrase.revisionId, base);
  await f.page.comparePhrase(); assert.equal(f.page.data.phraseConflict, true); assert.equal(f.page.data.phraseCloudText, '网页新版本'); assert.equal(f.page.phrase.revisionId, base);
  await f.page.abandon(); assert.equal(f.page.data.phraseText, '手机未保存'); f.confirm = false; await f.page.editPhrase(pick(id)); assert.equal(f.page.data.phraseText, '手机未保存');
  f.confirm = true; await f.page.editPhrase(pick(id)); assert.equal(f.page.data.phraseText, '网页新版本'); assert.equal(f.page.data.phraseConflict, false);
});

test('archive and restore preserve copied feedback, and archived phrases cannot be selected from stale cards', async () => {
  const f = fixture(); await f.page.onShow(); await f.page.openPrivate(); noteChange(f.page, 'phraseText', '原常用语'); await f.page.savePhrase(); const id = [...f.phraseRows.keys()][0];
  await f.page.usePhrase(pick(id)); await f.page.archivePhrase(pick(id)); assert.equal(f.page.data.phrases.length, 0); assert.equal(f.page.data.form.text, '原常用语');
  await f.page.archived({ detail: { value: ['archived'] } }); assert.equal(f.page.data.phrases[0].archived, true); f.edit('text', '新编辑'); await f.page.usePhrase(pick(id)); assert.equal(f.page.data.form.text, '新编辑');
  await f.page.archivePhrase(pick(id)); assert.equal(f.phraseRows.get(id).currentRevision.isArchived, false); assert.equal(f.page.data.form.text, '新编辑');
});

test('lost phrase response recovers its original receipt after a later cloud version without replacing the newer phrase', async () => {
  const f = fixture(); await f.page.onShow(); await f.page.openPrivate(); noteChange(f.page, 'phraseText', 'PRIVATE-PHRASE'); f.loseNote = true; await f.page.savePhrase(); const id = f.page.data.pending.phraseId;
  assert.ok(!JSON.stringify([...f.storage.values()]).includes('PRIVATE-PHRASE')); f.phraseRows.get(id).currentRevision.text = '较新网页文字'; await f.page.recover();
  assert.equal(f.page.data.pending, null); assert.equal(f.page.data.phrases[0].text, '较新网页文字'); assert.equal(f.requests.filter((r) => r.url.includes('save-phrase/')).length, 1);
});

test('private read scope, malformed rows and a phrase identity mismatch cannot enter the view or feedback text', async () => {
  const f = fixture(); await f.page.onShow(); f.read = (url) => url.includes('/observations-for-school/') ? { totalCount: 1, items: [{ id: root, schoolId: root, text: 'WRONG-SCHOOL' }] } : undefined;
  await f.page.openPrivate(); assert.equal(f.page.data.observations.length, 0); assert.ok(!JSON.stringify(f.page.data).includes('WRONG-SCHOOL'));
  f.read = null; noteChange(f.page, 'phraseText', '常用语'); await f.page.savePhrase(); const id = [...f.phraseRows.keys()][0]; f.edit('text', '原文');
  f.read = (url) => url.includes('/phrase/') ? { ...structuredClone(f.phraseRows.get(id)), id: member } : undefined; await f.page.usePhrase(pick(id)); assert.equal(f.page.data.form.text, '原文');
});

test('hiding or losing the exact teaching relation clears private views and late note responses cannot restore them', async () => {
  for (const leave of ['hide', 'relation']) {
    const f = fixture(); await f.page.onShow(); let finish; f.read = (url) => url.includes('/observations-for-school/') ? new Promise((r) => finish = r) : undefined;
    const run = f.page.openPrivate(); while (!finish) await new Promise(setImmediate);
    if (leave === 'hide') f.page.onHide(); else { const state = f.session.getState(); f.change({ ...state, context: { ...state.context, workspaces: [{ role: 1, schoolId: school, classes: [] }] } }); }
    finish({ totalCount: 0, items: [] }); await run; assert.equal(f.page.data.privateOpen, false); assert.equal(f.page.data.observations.length, 0); assert.equal(f.page.data.phrases.length, 0);
    if (leave === 'relation') assert.equal(f.page.form, null);
  }
});

test('private pending request bodies survive expiry only in memory and are destroyed on explicit logout', async () => {
  const f = fixture(); await f.page.onShow(); await f.page.openPrivate(); noteChange(f.page, 'observationText', 'PRIVATE-UNSAVED');
  f.change({ generation: 2, authenticated: false, context: null, termination: 'expired' }); assert.equal(f.page.data.observationText, ''); assert.equal(f.page.observationText, 'PRIVATE-UNSAVED');
  await f.page.reauthenticate(); await f.page.openPrivate(); assert.equal(f.page.data.observationText, 'PRIVATE-UNSAVED');
  f.change({ generation: 3, authenticated: false, context: null, termination: 'logout' }); assert.equal(f.page.observationText, ''); assert.equal(f.page.phraseText, ''); assert.equal(f.page.data.phrases.length, 0);
});

test('editor requires saved preview and confirmation; editing invalidates preview without publishing', async () => {
  const f = fixture(); await f.page.onShow(); f.edit('context', '课堂'); f.edit('text', '建议'); await f.page.save();
  await f.page.publish(); assert.equal(f.record.visibility, 0);
  await f.page.previewPublish(); assert.equal(f.page.data.preview.name, '同名学生');
  f.page.confirm({ detail: { value: ['confirmed'] } }); f.edit('text', '修改后的建议'); await f.page.publish(); assert.equal(f.record.visibility, 0);
  await f.page.save(); await f.page.previewPublish(); f.page.confirm({ detail: { value: ['confirmed'] } }); await f.page.publish();
  assert.equal(f.record.visibility, 2); assert.ok(f.dialogs.some((v) => v.includes('这一个学生')));
});

test('withdrawing needs an explicit reason and leaves a generic withdrawn version', async () => {
  const f = fixture(); await f.page.onShow(); f.edit('context', '课堂'); f.edit('text', '建议'); await f.page.save(); await f.page.previewPublish();
  f.page.confirm({ detail: { value: ['confirmed'] } }); await f.page.publish(); await f.page.withdraw(); assert.equal(f.record.visibility, 2);
  f.page.reason({ detail: { value: '重新核对后撤回' } }); await f.page.withdraw(); assert.equal(f.record.visibility, 3); assert.equal(f.record.currentRevision.content, null);
});

test('another cloud version causes a conflict and never overwrites or silently rebases local text', async () => {
  const f = fixture(); await f.page.onShow(); f.edit('context', '课堂'); f.edit('text', '初稿'); await f.page.save();
  f.edit('text', '本机未保存'); f.record.concurrencyStamp = 'other-device'; f.record.currentRevision = { ...f.record.currentRevision, id: member }; f.record.draft = { ...f.record.draft, content: { ...f.record.draft.content, text: '云端新稿' } };
  await f.page.load(); assert.equal(f.page.data.conflict, true); assert.equal(f.page.data.form.text, '本机未保存'); assert.equal(f.page.data.cloudText, '云端新稿');
  const before = f.requests.length; await f.page.save(); assert.equal(f.record.draft.content.text, '云端新稿'); assert.equal(f.requests.length, before);
  await f.page.reload(); assert.equal(f.page.data.form.text, '云端新稿'); assert.equal(f.page.data.pending, null);
});

test('expiry hides editing and permits same-account reauthentication; explicit logout destroys private memory', async () => {
  const f = fixture(); await f.page.onShow(); f.edit('context', '课堂'); f.edit('text', 'PRIVATE-UNSAVED'); f.page.reason({ detail: { value: 'PRIVATE-REASON' } });
  f.change({ generation: 2, authenticated: false, context: null, termination: 'expired' }); assert.equal(f.page.data.form, null); assert.equal(f.page.form.text, 'PRIVATE-UNSAVED');
  await f.page.reauthenticate(); assert.equal(f.page.data.form.text, 'PRIVATE-UNSAVED');
  f.change({ generation: 4, authenticated: false, context: null, termination: 'logout' }); assert.equal(f.page.form, null); assert.equal(f.page.data.reason, '');
});

test('leaving clears visible content and unloading drops exact retry bodies', async () => {
  const f = fixture(); await f.page.onShow(); f.edit('context', '课堂'); f.edit('text', 'PRIVATE'); f.loseDraft = true; await f.page.save();
  f.page.onHide(); assert.equal(f.page.data.form, null); assert.equal(f.page.data.target, null);
  f.page.onUnload(); assert.equal(f.page.form, null); assert.equal(f.page.writer.state().canRetry, false);
});

test('switching account clears private editor text and cannot reveal it after a workspace reload', async () => {
  const f = fixture(); await f.page.onShow(); f.edit('context', '课堂'); f.edit('text', 'PRIVATE-OLD-ACCOUNT'); f.loseDraft = true; await f.page.save();
  f.change({ generation: 2, authenticated: true, termination: null, context: { ...f.session.getState().context, accountUserId: member } });
  assert.equal(f.page.form, null); assert.equal(f.page.data.form, null); assert.equal(f.page.data.target, null); assert.equal(f.page.writer.state().canRetry, false);
  await f.page.load(); assert.equal(f.page.data.ready, false); assert.ok(!JSON.stringify(f.page.data).includes('PRIVATE-OLD-ACCOUNT'));
});

test('student view opens only the selected version and strips text from a withdrawal', async () => {
  const f = fixture('feedback-view', 2); f.page.versionId = member;
  f.read = (url) => url.includes('/open-my-for-school?') ? { id: root, versionId: member, versionNumber: 3, schoolId: school, visibility: 3, text: 'WITHDRAWN-PRIVATE', selectedObservationId: 'HIDDEN' } : undefined;
  await f.page.onShow(); assert.equal(f.page.data.detail.text, ''); assert.equal(f.page.data.detail.withdrawn, true);
  f.page.onHide(); assert.equal(f.page.data.detail, null);
});

test('teacher center uses the conventional list route and opens only the selected student identifiers', async () => {
  const f = fixture('feedback');
  f.read = (url) => {
    if (url.endsWith('/teaching-list')) return [{ id: teaching, schoolName: '学校', className: '一班', subjectName: '数学' }];
    if (url.startsWith('/api/app/education-feedback?')) {
      assert.equal(new URL('https://api.invalid' + url).searchParams.get('teachingAssignmentId'), teaching);
      return { totalCount: 0, items: [] };
    }
    if (url.includes('/students/')) return { totalCount: 1, items: [target().student] };
  };
  await f.page.onShow(); assert.equal(f.page.data.error, ''); assert.equal(f.page.data.studentTotal, 1);
  await f.page.create({ currentTarget: { dataset: { id: enrollment } } });
  assert.ok(f.navigation.includes('&enrollmentId=' + enrollment)); assert.ok(f.navigation.includes('&membershipId=' + member));
  assert.ok(!f.navigation.includes('student-a')); f.page.onHide(); assert.equal(f.page.data.students.length, 0);
});

test('student center scopes metadata and notices to the school without opening or marking feedback read', async () => {
  const f = fixture('feedback', 2);
  f.read = (url) => {
    assert.equal(new URL('https://api.invalid' + url).searchParams.get('schoolId'), school);
    if (url.includes('/my-list?')) return { totalCount: 1, items: [{ id: root, schoolId: school, versionId: member, versionNumber: 2, visibility: 2, text: 'NOT-A-CARD-BODY' }] };
    if (url.includes('/my-notices?')) return { totalCount: 1, items: [{ id: member, feedbackId: root, isCurrentVersion: true, isRead: false, kind: 1 }] };
  };
  await f.page.onShow(); assert.equal(f.page.data.total, 1); assert.ok(!JSON.stringify(f.page.data).includes('NOT-A-CARD-BODY'));
  assert.equal(f.requests.filter((r) => r.method === 'POST').length, 0);
  f.page.open({ currentTarget: { dataset: { id: root, version: member } } }); assert.ok(f.navigation.endsWith('&versionId=' + member));
});

test('loss of teacher permission clears private memory and exact retry text', async () => {
  const f = fixture(); await f.page.onShow(); f.edit('context', '课堂'); f.edit('text', 'PRIVATE'); f.loseDraft = true; await f.page.save();
  f.change({ generation: 2, authenticated: true, context: null, termination: 'forbidden' });
  assert.equal(f.page.form, null); assert.equal(f.page.data.form, null); assert.equal(f.page.writer.state().canRetry, false);
});

test('Web handoff requires a confirmed saved record and never implies unsaved text transfers', async () => {
  const f = fixture(); await f.page.onShow(); f.page.continueOnWeb(); assert.equal(f.clipboard, undefined);
  f.edit('context', '课堂'); f.edit('text', '建议'); await f.page.save(); f.page.continueOnWeb();
  assert.equal(f.clipboard, 'https://teacher.example.invalid/education/feedback?feedbackId=' + root);
  f.clipboard = undefined; f.edit('text', '未保存'); f.page.continueOnWeb(); assert.equal(f.clipboard, undefined);
});
