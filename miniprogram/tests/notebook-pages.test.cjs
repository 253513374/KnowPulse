const { test } = require('node:test'), assert = require('node:assert/strict'), crypto = require('node:crypto'), fs = require('node:fs'), path = require('node:path'), vm = require('node:vm'), { createRequire } = require('node:module');
const model = require('../utils/notebook'), stores = require('../utils/notebook-store'), commands = require('../utils/notebook-command'), answers = require('../utils/answer-store');
const guid = () => crypto.randomUUID(), account = guid(), id = guid(), school = guid(), recipient = guid(), component = guid(), publication = guid();
const clock = '2026-10-18T08:00:00Z', config = { apiBaseUrl: 'https://api.invalid', portalBaseUrl: 'https://portal.invalid', appKey: 'main', clientId: 'KnowPulse_WeChat_main' };
const event = (field, value) => ({ currentTarget: { dataset: { field } }, detail: { value } }), click = (id, kind) => ({ currentTarget: { dataset: { id, kind } } });
function fixture() {
  let state = { authenticated: true, generation: 1, context: { accountUserId: account, tenantId: null, workspaces: [], grantedPermissions: ['KnowPulse.KnowledgeBase.PersonalLibrary'] } };
  const listeners = new Set(), storage = new Map(), files = new Map(), links = [], requests = [], receipts = new Map(), notes = new Map(), attachments = new Map(), refs = [];
  const version = guid(), note = { id, ownerUserId: account, title: '自己的推理', summary: '自己的摘要', subjectName: '数学', materialYear: 2026, customTags: ['反思'], concurrencyStamp: guid(), latestVersionId: version, latestVersionNumber: 1,
    versions: [{ id: version, noteId: id, title: '自己的推理', contentText: 'PRIVATE-OWN-BODY', versionNumber: 1, status: 0, createdAtUtc: clock }] }; notes.set(id, note);
  const f = { state: () => state, storage, files, links, requests, receipts, notes, attachments, refs, confirm: true, writes: 0, uploads: 0, failBefore: false, lostResponse: false, exposure: 0 };
  function change(patch) { state = { ...state, ...patch }; listeners.forEach((fn) => fn(state)); }
  const platform = {
    env: { USER_DATA_PATH: 'wxfile://user' }, getStorageSync(k) { if (f.readFail) throw Error('unavailable'); return structuredClone(storage.get(k)); }, setStorageSync(k, v) { if (f.quota) throw Error('quota'); storage.set(k, structuredClone(v)); }, removeStorageSync: (k) => storage.delete(k), getStorageInfoSync: () => ({ keys: [...storage.keys()] }),
    getRandomValues(o) { const b = crypto.randomBytes(16); o.success({ randomValues: b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength) }); },
    showModal(o) { o.success({ confirm: f.confirm }); }, navigateTo: (o) => links.push(o.url), redirectTo: (o) => links.push(o.url), reLaunch: (o) => links.push(o.url), setClipboardData(o) { f.clipboard = o.data; o.success(); },
    chooseMessageFile(o) { o.success({ tempFiles: [{ path: 'wxfile://selected', name: '原始文件.pdf', size: 4, type: 'file' }] }); },
    getFileInfo(o) { const bytes = files.get(o.filePath); if (!bytes) return o.fail(); o.success({ size: bytes.length, digest: crypto.createHash('sha256').update(bytes).digest('hex') }); },
    getFileSystemManager() { return { copyFile(o) { if (!files.has(o.srcPath)) return o.fail(); files.set(o.destPath, Buffer.from(files.get(o.srcPath))); if (f.copyHook) f.copyHook(); o.success(); }, unlinkSync(p) { if (f.unlinkFail) throw Error('denied'); if (!files.delete(p)) throw Object.assign(Error('absent'), { code: 'ENOENT' }); } }; },
    openDocument(o) { f.opened = o.filePath; },
  };
  function lost(r) { if (f.lostResponse) { f.lostResponse = false; throw model.error('NETWORK_UNCONFIRMED'); } if (f.afterWrite) f.afterWrite(); return structuredClone(r); }
  const session = {
    getState: () => state,
    async refreshWorkspace() { const context = state.context; change({ context: null }); change({ context }); return state; },
    async request(route, method = 'GET', body) {
      requests.push({ route, method, body: structuredClone(body) }); if (!model.allowed(state)) throw model.error('FORBIDDEN');
      const u = new URL(route, config.apiBaseUrl), parts = u.pathname.split('/').filter(Boolean);
      if (method === 'DELETE') { assert.equal(body, undefined); body = { operationId: u.searchParams.get('OperationId'), concurrencyStamp: u.searchParams.get('ConcurrencyStamp') }; }
      if (parts[2] === 'personal-notebook') {
        if (parts[3] === 'operation') return structuredClone(receipts.get(parts[4]) || null);
        if (method !== 'GET') {
          if (f.failBefore) throw model.error('NETWORK_UNCONFIRMED'); if (receipts.has(body.operationId)) return structuredClone(receipts.get(body.operationId));
          if (parts.length === 3) {
            const noteId = guid(), v = guid(), r = { ...structuredClone(body), id: noteId, ownerUserId: account, concurrencyStamp: guid(), latestVersionId: v, latestVersionNumber: 1,
              versions: [{ id: v, noteId, title: body.title, contentText: body.contentText, versionNumber: 1, status: 0, createdAtUtc: clock }] }; notes.set(noteId, r); f.writes++;
            const receipt = { operationId: body.operationId, noteId, kind: 0, versionId: v, versionNumber: 1, recordedAtUtc: clock }; receipts.set(body.operationId, receipt); return lost(receipt);
          }
          const n = notes.get(parts[3]); if (!n || n.concurrencyStamp !== body.concurrencyStamp) throw model.error('NOTE_REJECTED');
          let receipt;
          if (method === 'DELETE') { notes.delete(n.id); receipt = { operationId: body.operationId, noteId: n.id, kind: 3, recordedAtUtc: clock }; }
          else {
            if (body.expectedVersionId !== n.latestVersionId) throw model.error('NOTE_REJECTED'); const v = guid(), number = n.latestVersionNumber + 1;
            Object.assign(n, body, { concurrencyStamp: guid(), latestVersionId: v, latestVersionNumber: number }); n.versions.push({ id: v, noteId: n.id, title: body.title, contentText: body.contentText, versionNumber: number, status: 0, createdAtUtc: clock });
            receipt = { operationId: body.operationId, noteId: n.id, kind: 1, versionId: v, versionNumber: number, recordedAtUtc: clock };
          }
          f.writes++; receipts.set(body.operationId, receipt); return lost(receipt);
        }
        const n = notes.get(parts[3]); const skip = Number(u.searchParams.get('SkipCount') || 0), paged = (r) => ({ items: structuredClone(r.slice(skip, skip + 20)), totalCount: r.length });
        if (parts.length === 3) return paged([...notes.values()].map((r) => ({ ...r, libraryId: guid(), materialType: 7 })));
        if (!n) throw model.error('FORBIDDEN'); if (parts.length === 4) return structuredClone(n);
        if (parts[4] === 'versions') return paged([...n.versions].reverse());
        if (parts[4] === 'version') return structuredClone(n.versions.find((v) => v.id === parts[5]));
        if (parts[4] === 'attachments') return paged([...(attachments.get(parts[5]) || [])]);
      }
      if (parts[2] === 'education-notebook-source') {
        if (parts[3] === 'preview-source') { assert.equal(method, 'POST'); assert.equal(body.recipientId, recipient); return { available: true, schoolId: school, title: '当前来源标题', source: { kind: 1, recipientId: recipient, sourceVersionId: publication, componentId: component } }; }
        const n = notes.get(parts[3]); if (parts[4] === 'source-operation') return structuredClone(receipts.get(parts[5]) || null);
        if (parts[4] === 'sources') return { concurrencyStamp: n.concurrencyStamp, items: structuredClone(refs.filter((r) => r.noteVersionId === parts[5] && !r.removed)) };
        if (f.failBefore) throw model.error('NETWORK_UNCONFIRMED');
        const r = parts[4] === 'source' ? { id: body.operationId, noteVersionId: body.noteVersionId, source: structuredClone(body.source), removed: false, available: true, schoolId: school, title: '当前来源标题' } : refs.find((r) => r.id === parts[5]);
        if (parts[4] === 'source') refs.push(r); else Object.assign(r, { removed: true, available: false, schoolId: null, title: null, openUrl: null });
        receipts.set(body.operationId, r); n.concurrencyStamp = guid(); f.writes++; return lost({ concurrencyStamp: n.concurrencyStamp, items: [] });
      }
      throw Error('Unexpected ' + route);
    },
    async uploadNotebookFile(noteId, v, op, file, filename) {
      if (f.failBefore) throw model.error('NETWORK_UNCONFIRMED'); f.uploads++; const b = files.get(file); assert.ok(b);
      const r = { operationId: op, noteId, versionId: v, versionNumber: 1, kind: 2, attachmentId: op, contentHash: crypto.createHash('sha256').update(b).digest('hex'), fileSize: b.length, recordedAtUtc: clock };
      receipts.set(op, r); attachments.set(v, [{ id: op, knowledgeVersionId: v, fileName: filename, size: b.length, contentType: 'application/pdf' }]); return lost(r);
    },
    async download(route) { requests.push({ route }); const p = 'wxfile://download'; files.set(p, Buffer.from('file')); if (f.downloadHook) f.downloadHook(); return p; },
    releaseFile: (p) => files.delete(p), portalUrl: (p) => config.portalBaseUrl + p,
  };
  const app = { session, configuration: config, subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); } };
  function pageFor(name = 'notebook', query = { id }) {
    const file = path.join(__dirname, '../pages', name, 'index.js'); let definition;
    new vm.Script(fs.readFileSync(file, 'utf8'), { filename: file }).runInNewContext({ require: createRequire(file), Page: (p) => definition = p, getApp: () => app, wx: platform });
    const p = { data: structuredClone(definition.data), setData(v) { assert.ok(Buffer.byteLength(JSON.stringify(v)) < 1024 * 1024); Object.assign(this.data, structuredClone(v)); } };
    for (const [key, fn] of Object.entries(definition)) if (typeof fn === 'function') p[key] = fn.bind(p); p.onLoad(query); return p;
  }
  return Object.assign(f, { platform, session, app, pageFor, change });
}
async function edit(p, value = '自己的新解法') { await p.allowLocal(); p.change(event('contentText', value)); }
test('account permission works without any school role; roles alone grant no notebook', () => {
  const f = fixture(); assert.equal(model.allowed(f.state()), true); f.change({ context: { ...f.state().context, grantedPermissions: [], workspaces: [{ role: 1, schoolId: school }] } }); assert.equal(model.allowed(f.state()), false);
});
test('native list stays bounded metadata and private filters are encoded', async () => {
  const f = fixture(), p = f.pageFor('notebooks', {}); await p.onShow(); assert.equal(p.data.rows.length, 1); assert.ok(!JSON.stringify(p.data).includes('PRIVATE-OWN-BODY'));
  p.filter(event('keyword', '正文&标签')); await p.search(); assert.match(f.requests.at(-1).route, /Keyword=%E6%AD%A3%E6%96%87%26%E6%A0%87%E7%AD%BE/);
});
test('drafts require consent, preserve whitespace and survive a cold page only for this account', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); p.change(event('contentText', 'ignored')); assert.equal(p.data.form, null); await edit(p, '  私有\n原文  '); p.onUnload();
  const cold = f.pageFor(); await cold.onShow(); assert.equal(cold.data.form.contentText, '  私有\n原文  '); assert.equal(f.writes, 0);
  f.change({ generation: 2, context: { ...f.state().context, accountUserId: guid() } }); assert.equal(cold.data.form, null); assert.equal(cold.data.current, null); assert.ok(!JSON.stringify([...f.storage.values()]).includes('私有\n原文'));
});
test('lost create response recovers first on a cold page without duplicate creation', async () => {
  const f = fixture(), p = f.pageFor('notebook', {}); await p.onShow(); await edit(p); p.change(event('title', '新笔记')); f.lostResponse = true; await p.save(); assert.equal(f.writes, 1); assert.ok(p.data.pending); p.onUnload();
  const cold = f.pageFor('notebook', {}); const index = f.requests.length; await cold.onShow(); assert.match(f.requests[index].route, /\/operation\//); assert.equal(f.writes, 1); assert.equal(cold.visible, false); assert.match(f.links.at(-1), /\/notebook\/index\?id=/);
});
test('a source carried into a new note survives cold creation recovery as identifiers only', async () => {
  const f = fixture(), p = f.pageFor('notebook', { sourceKind: '1', recipientId: recipient, componentId: component, sourceVersionId: publication }); await p.onShow(); await edit(p); p.change(event('title', '来源反思')); f.lostResponse = true; await p.save(); p.onUnload();
  const cold = f.pageFor('notebook', {}); await cold.onShow(); const link = f.links.at(-1); assert.ok(link.includes('sourceVersionId=' + publication)); assert.ok(link.includes('componentId=' + component)); assert.equal(f.refs.length, 0);
});
test('a changed source publication cannot silently replace the source selected on the original page', async () => {
  const f = fixture(), p = f.pageFor('notebook', { id, sourceKind: '1', recipientId: recipient, componentId: component, sourceVersionId: guid() }); await p.onShow(); await p.previewSource(); assert.equal(p.data.target.available, false); await p.addSource(); assert.equal(f.refs.length, 0); assert.equal(p.data.current.contentText, 'PRIVATE-OWN-BODY');
});
test('exact retry queries receipt first and preserves original body, operation and base', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p, '原始保存文字'); f.failBefore = true; await p.save(); const original = f.requests.at(-1).body; f.failBefore = false;
  await p.recover(click(null, 'retry')); const writes = f.requests.filter((r) => r.method === 'POST' && r.route.endsWith('/save')); assert.equal(writes.length, 2); assert.deepEqual(writes[1].body, original); assert.equal(f.writes, 1); assert.equal(p.data.current.contentText, '原始保存文字');
});
test('a lost save receipt cannot roll back a newer Web version', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p, '保存v2'); f.lostResponse = true; await p.save(); const n = f.notes.get(id), v = guid(); n.latestVersionId = v; n.latestVersionNumber = 3; n.concurrencyStamp = guid(); n.versions.push({ id: v, noteId: id, title: '网页v3', contentText: 'WEB-V3', status: 0, versionNumber: 3, createdAtUtc: clock });
  await p.recover(); assert.equal(p.data.current.contentText, 'WEB-V3'); assert.equal(p.data.metadata.latestVersionNumber, 3); assert.equal(f.writes, 1);
});
test('Web concurrency keeps local text and requires explicit comparison before a new save', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p, '本机保留'); const n = f.notes.get(id); n.concurrencyStamp = guid(); await p.save(); assert.ok(p.data.pending); assert.equal(f.writes, 0); await p.abandon(); assert.equal(p.data.form.contentText, '本机保留'); assert.equal(p.data.conflict, true);
  f.confirm = false; await p.rebase(); assert.equal(p.data.conflict, true); f.confirm = true; await p.rebase(); await p.save(); assert.equal(f.writes, 1);
});
test('quota failure prevents sending a new write and retains in-memory text', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); f.quota = true; await p.save(); assert.equal(f.writes, 0); assert.equal(p.data.form.contentText, '自己的新解法'); assert.match(p.data.error, /操作标识/);
});
test('storage read failure after committed save is reported without an unhandled exception or a second write', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); f.afterWrite = () => f.readFail = true; await p.save(); assert.equal(f.writes, 1); assert.ok(p.data.error); assert.match(p.data.notice, /云端已返回/);
  await p.recover(); assert.ok(p.data.error); assert.equal(f.writes, 1);
  f.afterWrite = null; f.readFail = false; await p.load(); assert.equal(p.data.current.contentText, '自己的新解法'); assert.equal(f.writes, 1); assert.equal(p.data.pending, null);
});
test('confirmed file cleanup failure retains recovery state until the same receipt can be acknowledged', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); f.files.set('wxfile://selected', Buffer.from('PDF!')); f.afterWrite = () => f.unlinkFail = true; await p.chooseFile(click(null, 'file')); assert.equal(f.uploads, 1); assert.equal(p.data.pending.kind, 'notebook-file'); assert.match(p.data.error, /清理未确认/);
  f.afterWrite = null; f.unlinkFail = false; await p.recover(); assert.equal(f.uploads, 1); assert.equal(p.data.pending, null);
});
test('tag entry keeps a trailing delimiter so the next tag can be typed', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); p.change(event('tags', '反思，')); assert.equal(p.data.form.tagsText, '反思，'); p.change(event('tags', '反思，检查')); await p.save(); assert.deepEqual(p.data.metadata.customTags, ['反思', '检查']);
});
test('attachment response loss has one upload and original file name, then clears only its own copy', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); f.files.set('wxfile://selected', Buffer.from('PDF!')); f.lostResponse = true; await p.chooseFile(click(null, 'file'));
  assert.equal(f.uploads, 1); assert.equal([...f.files.keys()].filter((k) => k.includes('/kp-notebook-')).length, 1); await p.recover(); assert.equal(f.uploads, 1); assert.equal(p.data.attachments[0].name, '原始文件.pdf'); assert.ok(![...f.files.keys()].some((k) => k.includes('/kp-notebook-'))); assert.ok(f.files.has('wxfile://selected'));
});
test('different reselected bytes do not replace pending attachment', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); f.files.set('wxfile://selected', Buffer.from('PDF!')); f.failBefore = true; await p.chooseFile(click(null, 'file')); const pending = p.data.pending.operationId;
  f.files.set('wxfile://selected', Buffer.from('evil')); await p.chooseFile(click(null, 'reselect')); assert.equal(p.data.pending.operationId, pending); assert.equal(f.uploads, 0); assert.match(p.data.error, /内容不同/);
});
test('logout strips file name and body but preserves identifier-only receipt lookup', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); f.files.set('wxfile://selected', Buffer.from('PDF!')); f.failBefore = true; await p.chooseFile(click(null, 'file'));
  assert.equal(answers.clearAccount(f.platform, config, f.state().context), true); const rows = [...f.storage.values()]; assert.equal(rows.length, 1); assert.equal(rows[0].payload, null); assert.ok(!JSON.stringify(rows).includes('原始文件')); assert.ok(![...f.files.keys()].some((k) => k.includes('/kp-notebook-')));
});
test('file cleanup failure stays visible and preserves a retryable cleanup marker', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); await edit(p); f.files.set('wxfile://selected', Buffer.from('PDF!')); f.failBefore = true; await p.chooseFile(click(null, 'file')); f.unlinkFail = true;
  assert.equal(answers.clearAccount(f.platform, config, f.state().context), false); assert.ok([...f.storage.values()].find((r) => r.needsFileCleanup));
});
test('late download after account change is released and never displayed', async () => {
  const f = fixture(), p = f.pageFor(); f.attachments.set(f.notes.get(id).latestVersionId, [{ id: component, knowledgeVersionId: f.notes.get(id).latestVersionId, fileName: 'a.pdf', size: 4, contentType: 'application/pdf' }]); await p.onShow();
  f.downloadHook = () => f.change({ generation: 2, context: { ...f.state().context, accountUserId: guid() } }); await p.attachment(click(component)); assert.equal(f.opened, undefined); assert.ok(!f.files.has('wxfile://download')); assert.equal(p.data.current, null);
});
test('source preview and add stay body-free and never open the original source', async () => {
  const f = fixture(), p = f.pageFor('notebook', { id, sourceKind: '1', recipientId: recipient, componentId: component }); await p.onShow(); await p.previewSource(); await p.addSource(); assert.equal(f.refs.length, 1); assert.equal(f.exposure, 0); assert.equal(p.data.sources.length, 1); assert.ok(!JSON.stringify(f.storage).includes('当前来源标题'));
  p.openSource(click(f.refs[0].id)); assert.match(f.links.at(-1), new RegExp('publicationId=' + publication)); assert.match(f.links.at(-1), new RegExp('materialId=' + component));
});
test('source withdrawal hides navigation while own note remains readable and removable', async () => {
  const f = fixture(), p = f.pageFor('notebook', { id, sourceKind: '1', recipientId: recipient, componentId: component }); await p.onShow(); await p.previewSource(); await p.addSource(); const r = f.refs[0]; Object.assign(r, { available: false, schoolId: null, title: null, openUrl: null }); await p.load();
  assert.equal(p.data.current.contentText, 'PRIVATE-OWN-BODY'); const before = f.links.length; p.openSource(click(r.id)); assert.equal(f.links.length, before); await p.removeSource(click(r.id)); assert.equal(r.removed, true);
});
test('old add-source receipt after removal is accepted without restoring the removed pointer', async () => {
  const f = fixture(), p = f.pageFor('notebook', { id, sourceKind: '1', recipientId: recipient, componentId: component }); await p.onShow(); await p.previewSource(); f.lostResponse = true; await p.addSource(); const r = f.refs[0]; Object.assign(r, { removed: true, available: false, schoolId: null, title: null, openUrl: null }); await p.recover(); assert.equal(f.writes, 1); assert.equal(p.data.pending, null); assert.equal(p.data.sources.length, 0);
});
test('24-hour expiry strips original payload and draft while retaining operation identity', () => {
  const f = fixture(); let now = Date.now(); const store = stores(f.platform, config, f.state().context, id, () => now), op = guid(); store.saveDraft({ form: { contentText: 'secret' } }); store.begin({ kind: 'save', operationId: op, payload: { operationId: op, contentText: 'secret' } }); now += 24 * 60 * 60 * 1000;
  assert.equal(store.draft(), null); assert.equal(store.pending().payload, null); assert.equal(store.pending().operationId, op);
});
test('a cold expired request never reconstructs its body from the current form', async () => {
  const f = fixture(); const store = stores(f.platform, config, f.state().context, id), op = guid(); store.begin({ kind: 'save', operationId: op, payload: null }); const c = commands(f.platform, f.session, store, id, () => true);
  await assert.rejects(c.retry(), { code: 'RETRY_BODY_LOST' }); assert.equal(f.writes, 0); assert.match(f.requests.at(-1).route, /operation/);
});
test('private web handoff carries no note body or credentials', async () => { const f = fixture(), p = f.pageFor(); await p.onShow(); p.web(); assert.equal(f.clipboard, 'https://portal.invalid/education/notebook'); assert.match(p.data.notice, /不会自动同步/); });
test('delete uses ABP query-bound DTO and an exact original receipt after response loss', async () => {
  const f = fixture(), p = f.pageFor(); await p.onShow(); f.lostResponse = true; await p.remove(); assert.equal(f.notes.has(id), false); assert.equal(p.data.pending.kind, 'delete'); await p.recover(); assert.equal(p.visible, false); assert.match(f.links.at(-1), /notebooks\/index/); assert.equal(f.writes, 1);
});
