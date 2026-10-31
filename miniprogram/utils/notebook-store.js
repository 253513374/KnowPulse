const answers = require('./answer-store');
const { isGuid } = require('./assessment');
const { error } = require('./notebook');
const ttl = 24 * 60 * 60 * 1000;
function create(platform, config, identity, id, now = () => Date.now()) {
  if (id !== null && !isGuid(id)) throw error('INVALID_PATH');
  const scope = answers.prefix(config, identity) + 'notebook:' + (id || 'new') + ':';
  const read = (key) => { try { return platform.getStorageSync(scope + key) || null; } catch (_) { throw error('STORAGE_REQUIRED'); } };
  const put = (key, row) => { try { platform.setStorageSync(scope + key, row); } catch (_) { throw error('STORAGE_REQUIRED'); } };
  const remove = (key) => { try { platform.removeStorageSync(scope + key); } catch (_) { throw error('STORAGE_REQUIRED'); } };
  const valid = (r) => { if (r.version !== 1 || !Number.isFinite(r.createdAt) || r.createdAt > now() + 60000) throw error('LOCAL_STATE_INVALID'); };
  function unlink(r) {
    if (r.kind !== 'notebook-file') return;
    try { platform.getFileSystemManager().unlinkSync(answers.notebookFilePath(platform, r.operationId, r.file.extension)); }
    catch (e) { if (e.code !== 'ENOENT' && !/no such file or directory/i.test(String(e.errMsg))) throw error('LOCAL_CLEANUP_UNCONFIRMED'); }
  }
  return {
    draft() { const r = read('draft'); if (!r) return null; valid(r); if (now() - r.createdAt >= ttl) { remove('draft'); return null; } return r.value; },
    saveDraft(value) { put('draft', { version: 1, createdAt: now(), value }); },
    removeDraft() { remove('draft'); },
    pending() {
      let r = read('pending'); if (!r) return null; valid(r);
      if (!isGuid(r.operationId) || r.noteId !== id || !['create', 'save', 'delete', 'notebook-file', 'source-add', 'source-remove'].includes(r.kind) ||
          r.sourceTarget && (![0, 1].includes(r.sourceTarget.kind) || ![r.sourceTarget.recipientId, r.sourceTarget.componentId].every(isGuid) || r.sourceTarget.sourceVersionId != null && !isGuid(r.sourceTarget.sourceVersionId)) ||
          r.payload && r.payload.operationId !== r.operationId || r.input && r.input.operationId !== r.operationId ||
          ['source-add', 'source-remove'].includes(r.kind) && (!r.input || r.input.noteVersionId !== r.versionId || !isGuid(r.versionId)) ||
          r.kind === 'notebook-file' && (!isGuid(r.versionId) || !r.file || !/^[a-f0-9]{64}$/.test(r.file.contentHash) || !Number.isSafeInteger(r.file.size) || r.file.size < 1 || r.file.size > 20 * 1024 * 1024 || !/^[a-z0-9]{1,10}$/.test(r.file.extension))) throw error('LOCAL_STATE_INVALID');
      if (now() - r.createdAt >= ttl && r.payload) { r = { ...r, payload: null }; put('pending', r); }
      if (r.kind === 'notebook-file' && (r.needsFileCleanup || now() - (r.cachedAt || r.createdAt) >= ttl)) {
        r = { ...r, cachedAt: null, needsFileCleanup: true }; put('pending', r); unlink(r); r = { ...r, needsFileCleanup: false }; put('pending', r);
      }
      return r;
    },
    begin(r) { if (read('pending')) throw error('PENDING_OPERATION'); put('pending', { ...r, noteId: id, version: 1, createdAt: now(), cachedAt: null }); },
    retained(op) { const r = this.pending(); if (r?.operationId !== op || r.kind !== 'notebook-file') throw error('LOCAL_STATE_INVALID'); put('pending', { ...r, cachedAt: now(), needsFileCleanup: false }); },
    acknowledge(op) { const r = this.pending(); if (r?.operationId !== op) throw error('LOCAL_STATE_INVALID'); unlink(r); remove('pending'); },
    clearPrivate() { remove('draft'); const r = this.pending(); if (!r) return; const reduced = { ...r, payload: null, cachedAt: null, needsFileCleanup: r.kind === 'notebook-file' }; put('pending', reduced); unlink(r); put('pending', { ...reduced, needsFileCleanup: false }); },
    path: (p) => answers.notebookFilePath(platform, p.operationId, p.file.extension),
  };
}
module.exports = create;
module.exports.pending = function pending(platform, config, identity) {
  const prefix = answers.prefix(config, identity) + 'notebook:'; let keys;
  try { keys = platform.getStorageInfoSync().keys; } catch (_) { throw error('STORAGE_REQUIRED'); }
  return keys.filter((k) => typeof k === 'string' && k.startsWith(prefix) && k.endsWith(':pending')).map((k) => {
    const target = k.slice(prefix.length, -':pending'.length); const id = target === 'new' ? null : target;
    const p = create(platform, config, identity, id).pending(); return p ? { noteId: id, kind: p.kind, operationId: p.operationId } : null;
  }).filter(Boolean);
};
