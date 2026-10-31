const answers = require('./answer-store');
const { isGuid } = require('./assessment');
const practice = require('./practice');
const ttl = 24 * 60 * 60 * 1000;
function create(platform, config, identity, schoolId, id, recipientId, questionId, now) {
  const start = recipientId != null;
  if (![schoolId, ...(start ? [recipientId, questionId] : [id])].every(isGuid)) throw practice.error('INVALID_PATH');
  const scope = answers.prefix(config, identity) + (start ? `practice-start:${schoolId}:${recipientId}:${questionId}:` : `practice:${schoolId}:${id}:`);
  const read = (name) => { try { return platform.getStorageSync(scope + name) || null; } catch (_) { throw practice.error('STORAGE_REQUIRED'); } };
  const put = (name, row) => { try { platform.setStorageSync(scope + name, row); } catch (_) { throw practice.error('STORAGE_REQUIRED'); } };
  const remove = (name) => { try { platform.removeStorageSync(scope + name); } catch (_) { throw practice.error('STORAGE_REQUIRED'); } };
  const valid = (r) => { if (r.version !== 1 || !Number.isFinite(r.createdAt) || r.createdAt > now() + 60000) throw practice.error('LOCAL_STATE_INVALID'); };
  const unlink = (r) => { if (r?.kind !== 'image') return; try { platform.getFileSystemManager().unlinkSync(answers.imagePath(platform, r.operationId, r.image.extension)); }
    catch (e) { if (e.code !== 'ENOENT' && !/no such file or directory/i.test(String(e.errMsg))) throw practice.error('LOCAL_CLEANUP_UNCONFIRMED'); } };
  return {
    draft() { const r = read('draft'); if (!r) return null; valid(r); if (r.practiceId !== id) throw practice.error('LOCAL_STATE_INVALID'); if (now() - r.createdAt >= ttl) { remove('draft'); return null; } return practice.response(r.response); },
    saveDraft(response) { put('draft', { version: 1, createdAt: now(), practiceId: id, response: practice.response(response) }); },
    removeDraft() { remove('draft'); },
    pending() {
      let r = read('pending'); if (!r) return null; valid(r);
      if (!isGuid(r.operationId) || (start ? r.kind !== 'start' || r.recipientId !== recipientId || r.input?.paperQuestionId !== questionId || r.input?.operationId !== r.operationId || !isGuid(r.input.expectedResultReleaseId) : !['submit', 'image'].includes(r.kind) || r.practiceId !== id) ||
          r.payload && r.payload.operationId !== r.operationId) throw practice.error('LOCAL_STATE_INVALID');
      if (r.kind === 'image' && (!r.image || !/^[a-f0-9]{64}$/.test(r.image.contentHash) || !Number.isSafeInteger(r.image.size) || r.image.size < 12 || r.image.size > practice.imagePolicy.maxFileBytes ||
          !['jpg', 'png', 'webp'].includes(r.image.extension) || r.image.contentType !== { jpg: 'image/jpeg', png: 'image/png', webp: 'image/webp' }[r.image.extension])) throw practice.error('LOCAL_STATE_INVALID');
      if (r.payload && now() - r.createdAt >= ttl) { r = { ...r, payload: null }; put('pending', r); }
      if (r.kind === 'image' && (r.needsImageCleanup || now() - (r.cachedAt || r.createdAt) >= ttl)) { r = { ...r, cachedAt: null, needsImageCleanup: true }; put('pending', r); unlink(r); r = { ...r, needsImageCleanup: false }; put('pending', r); }
      return r;
    },
    begin(r) { if (read('pending')) throw practice.error('PENDING_OPERATION'); put('pending', { ...r, version: 1, createdAt: now(), practiceId: id, recipientId, cachedAt: null }); },
    retained(op) { const r = this.pending(); if (r?.operationId !== op || r.kind !== 'image') throw practice.error('LOCAL_STATE_INVALID'); put('pending', { ...r, cachedAt: now(), needsImageCleanup: false }); },
    acknowledge(op) { const r = this.pending(); if (r?.operationId !== op) throw practice.error('LOCAL_STATE_INVALID'); unlink(r); remove('pending'); },
    clearPrivate() { remove('draft'); const r = this.pending(); if (r) { put('pending', { ...r, payload: null, cachedAt: null, needsImageCleanup: r.kind === 'image' }); unlink(r); if (r.kind === 'image') put('pending', { ...r, payload: null, cachedAt: null, needsImageCleanup: false }); } },
    imagePath: (r) => answers.imagePath(platform, r.operationId, r.image.extension),
  };
}
module.exports = (platform, config, identity, school, id, now = () => Date.now()) => create(platform, config, identity, school, id, null, null, now);
module.exports.start = (platform, config, identity, school, recipient, question, now = () => Date.now()) => create(platform, config, identity, school, null, recipient, question, now);
