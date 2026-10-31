const answer = require('./answer-store');
const { isGuid } = require('./assessment');
const correction = require('./correction');
const ttl = 24 * 60 * 60 * 1000;
module.exports = function store(platform, config, identity, schoolId, deliveryId, now = () => Date.now()) {
  if (![schoolId, deliveryId].every(isGuid)) throw correction.error('INVALID_PATH');
  const scope = answer.prefix(config, identity) + `correction:${schoolId}:${deliveryId}:`;
  const read = (name) => { try { return platform.getStorageSync(scope + name) || null; } catch (_) { throw correction.error('STORAGE_REQUIRED'); } };
  const put = (name, row) => { try { platform.setStorageSync(scope + name, row); } catch (_) { throw correction.error('STORAGE_REQUIRED'); } };
  const remove = (name) => { try { platform.removeStorageSync(scope + name); } catch (_) { throw correction.error('STORAGE_REQUIRED'); } };
  const removeImage = (row) => { if (row?.kind !== 'image') return; try { platform.getFileSystemManager().unlinkSync(answer.imagePath(platform, row.operationId, row.image.extension)); }
    catch (e) { if (e.code !== 'ENOENT' && !/no such file or directory/i.test(String(e.errMsg))) throw correction.error('LOCAL_CLEANUP_UNCONFIRMED'); } };
  return {
    draft() {
      const row = read('draft'); if (!row) return null;
      if (row.version !== 1 || !Number.isFinite(row.createdAt) || row.base != null && !isGuid(row.base)) throw correction.error('LOCAL_STATE_INVALID');
      if (now() - row.createdAt >= ttl) { remove('draft'); return null; }
      return { base: row.base || null, content: correction.content(row.content) };
    },
    saveDraft(row) { if (row.base != null && !isGuid(row.base)) throw correction.error('LOCAL_STATE_INVALID'); put('draft', { version: 1, createdAt: now(), base: row.base || null, content: correction.content(row.content) }); },
    removeDraft() { remove('draft'); },
    pending() {
      let row = read('pending'); if (!row) return null;
      if (row.version !== 1 || row.deliveryId !== deliveryId || !isGuid(row.operationId) || !['image', 'submit'].includes(row.kind) || !Number.isFinite(row.createdAt) ||
          row.payload && row.payload.operationId !== row.operationId || row.kind === 'image' && (!row.image || !/^[a-f0-9]{64}$/.test(row.image.contentHash) || !Number.isSafeInteger(row.image.size) || row.image.size < 12 || row.image.size > correction.imagePolicy.maxFileBytes || !['jpg', 'png', 'webp'].includes(row.image.extension) || !correction.imagePolicy.allowedContentTypes.includes(row.image.contentType))) throw correction.error('LOCAL_STATE_INVALID');
      if (row.payload && now() - row.createdAt >= ttl) { row = { ...row, payload: null }; put('pending', row); }
      if (row.kind === 'image' && (row.needsImageCleanup || now() - (Number.isFinite(row.cachedAt) ? row.cachedAt : row.createdAt) >= ttl)) {
        row = { ...row, cachedAt: null, needsImageCleanup: true }; put('pending', row); removeImage(row); row = { ...row, needsImageCleanup: false }; put('pending', row);
      }
      return row;
    },
    begin(row) { if (read('pending')) throw correction.error('PENDING_OPERATION'); put('pending', { ...row, version: 1, deliveryId, createdAt: now(), cachedAt: null }); },
    retained(operationId) { const row = read('pending'); if (!row || row.version !== 1 || row.deliveryId !== deliveryId || row.kind !== 'image' || row.operationId !== operationId) throw correction.error('LOCAL_STATE_INVALID'); put('pending', { ...row, cachedAt: now() }); },
    acknowledge(id) { const row = this.pending(); if (row?.operationId !== id) throw correction.error('LOCAL_STATE_INVALID'); removeImage(row); remove('pending'); },
    clearPrivate() { remove('draft'); const row = this.pending(); if (row) { put('pending', { ...row, payload: null, cachedAt: null, needsImageCleanup: row.kind === 'image' }); removeImage(row); if (row.kind === 'image') put('pending', { ...row, payload: null, cachedAt: null, needsImageCleanup: false }); } },
    imagePath(row) { return answer.imagePath(platform, row.operationId, row.image.extension); },
  };
};
