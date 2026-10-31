const model = require('./notebook');
const files = require('./notebook-file');
const { actor, uuid } = require('./feedback');
module.exports = function command(platform, session, store, id, active) {
  const owner = actor(session.getState()); let busy = false;
  const requireOwner = () => { if (!owner || actor(session.getState()) !== owner || !model.allowed(session.getState())) throw model.error('FORBIDDEN'); if (!active()) throw model.error('STALE_RESPONSE'); };
  const pending = () => { requireOwner(); const p = store.pending(); if (!p) throw model.error('PENDING_REQUIRED'); return p; };
  const run = async (work) => { if (busy) throw model.error('BUSY'); busy = true; try { return await work(); } finally { busy = false; } };
  function valid(r, p) {
    if (!r) return null; if (!p.kind.startsWith('source-')) return model.receipt(r, p);
    const value = model.source(r, p.versionId);
    if (p.kind === 'source-add' && (value.id !== p.operationId || JSON.stringify(value.source) !== JSON.stringify(model.pointer(p.input.source))) || p.kind === 'source-remove' && (value.id !== p.sourceId || !value.removed)) throw model.error();
    return { noteId: id, kind: p.kind, operationId: p.operationId, versionId: p.versionId };
  }
  async function query(p) {
    const r = await session.request(p.kind.startsWith('source-') ? `/api/app/education-notebook-source/${id}/source-operation/${p.operationId}` : `/api/app/personal-notebook/operation/${p.operationId}`);
    requireOwner(); return valid(r, p);
  }
  async function retain(path, p) {
    if (!p.payload?.fileName) throw model.error('RETRY_BODY_LOST');
    try { await files.retain(platform, path, p.operationId, p.file, p.payload.fileName); store.retained(p.operationId); requireOwner(); }
    catch (e) { if (actor(session.getState()) !== owner || !model.allowed(session.getState())) store.clearPrivate(); throw e; }
  }
  async function send(p) {
    let r;
    if (p.kind.startsWith('source-')) {
      await session.request(`/api/app/education-notebook-source/${id}/${p.kind === 'source-add' ? 'source' : 'remove-source/' + p.sourceId}`, 'POST', p.input);
      requireOwner(); return query(p);
    }
    if (!p.payload) throw model.error('RETRY_BODY_LOST');
    if (p.kind === 'notebook-file') { await retain(store.path(p), p); requireOwner(); r = await session.uploadNotebookFile(id, p.versionId, p.operationId, store.path(p), p.payload.fileName); }
    else if (p.kind === 'delete') r = await session.request(`/api/app/personal-notebook/${id}?OperationId=${p.operationId}&ConcurrencyStamp=${encodeURIComponent(p.payload.concurrencyStamp)}`, 'DELETE');
    else r = await session.request('/api/app/personal-notebook' + (p.kind === 'create' ? '' : '/' + id + '/save'), 'POST', p.payload);
    requireOwner(); return valid(r, p);
  }
  return {
    async begin(kind, payload, versionId, sourceId, sourceTarget) {
      requireOwner(); if (store.pending()) throw model.error('PENDING_OPERATION');
      if (!['create', 'save', 'delete', 'source-add', 'source-remove'].includes(kind)) throw model.error('INVALID_PATH');
      const operationId = await uuid(platform); requireOwner();
      store.begin({ kind, operationId, versionId, sourceId, sourceTarget, ...(kind.startsWith('source-') ? { input: { ...payload, operationId } } : { payload: { ...payload, operationId } }) });
    },
    async beginFile(path, fileName, versionId) {
      requireOwner(); if (store.pending()) throw model.error('PENDING_OPERATION');
      const file = await files.inspect(platform, path, fileName), operationId = await uuid(platform); requireOwner();
      store.begin({ kind: 'notebook-file', operationId, versionId, file, payload: { operationId, fileName } }); await retain(path, store.pending());
    },
    reselect: (path) => retain(path, pending()),
    send: () => run(() => send(pending())), recover: () => run(() => query(pending())), retry: () => run(async () => { const p = pending(); return await query(p) || await send(p); }),
    state() { const p = store.pending(); return p ? { kind: p.kind, operationId: p.operationId, canRetry: !!p.payload || !!p.input } : null; },
  };
};
