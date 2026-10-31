const { isGuid } = require('./assessment');
const ttl = 24 * 60 * 60 * 1000;
const error = (code) => Object.assign(new Error(code), { code });
function prefix(configuration, identity) {
  if (!identity || !isGuid(identity.accountUserId) || !(identity.tenantId === null || isGuid(identity.tenantId))) throw error('AUTH_REQUIRED');
  return `knowpulse:answers:${encodeURIComponent(configuration.apiBaseUrl)}:${configuration.appKey}:${configuration.clientId}:${identity.tenantId || 'host'}:${identity.accountUserId}:`;
}
function imagePath(platform, operationId, extension) {
  if (!isGuid(operationId) || !['jpg', 'png', 'webp'].includes(extension) || !platform.env || typeof platform.env.USER_DATA_PATH !== 'string') throw error('IMAGE_UNAVAILABLE');
  return platform.env.USER_DATA_PATH + '/kp-assessment-' + operationId + '.' + extension;
}
function notebookFilePath(platform, operationId, extension) {
  if (!isGuid(operationId) || !/^[a-z0-9]{1,10}$/.test(extension) || !platform.env || typeof platform.env.USER_DATA_PATH !== 'string') throw error('FILE_INVALID');
  return platform.env.USER_DATA_PATH + '/kp-notebook-' + operationId + '.' + extension;
}
function removeImage(platform, pending) {
  if (pending?.kind === 'notebook-file') {
    try { platform.getFileSystemManager().unlinkSync(notebookFilePath(platform, pending.operationId, pending.file.extension)); }
    catch (failure) { if (failure.code !== 'ENOENT' && !/no such file or directory/i.test(String(failure.errMsg))) throw failure; }
    return;
  }
  if (!pending || pending.kind !== 'image' || !pending.image) return;
  const filePath = imagePath(platform, pending.operationId, pending.image.extension);
  try { platform.getFileSystemManager().unlinkSync(filePath); }
  catch (failure) { if (failure.code !== 'ENOENT' && !/no such file or directory/i.test(String(failure.errMsg))) throw failure; }
}
function clearAccount(platform, configuration, identity) {
  if (!identity) return true;
  const scope = prefix(configuration, identity); let okay = true;
  try {
    const keys = platform.getStorageInfoSync().keys.filter((k) => typeof k === 'string' && k.startsWith(scope));
    for (const key of keys) {
      try {
        const row = platform.getStorageSync(key);
        if (key.endsWith(':pending') && row && row.version === 1) {
          // Identifier-only recovery survives logout; its former body and own copied image do not.
          const reduced = { ...row, payload: null, cachedAt: null, needsImageCleanup: row.kind === 'image', needsFileCleanup: row.kind === 'notebook-file' };
          platform.setStorageSync(key, reduced);
          try { removeImage(platform, row); if (['image', 'notebook-file'].includes(row.kind)) platform.setStorageSync(key, { ...reduced, needsImageCleanup: false, needsFileCleanup: false }); }
          catch (_) { if (['image', 'notebook-file'].includes(row.kind)) okay = false; }
        } else platform.removeStorageSync(key);
      } catch (_) { okay = false; }
    }
  } catch (_) { okay = false; }
  return okay;
}
module.exports = function store(platform, configuration, identity, recipientId, now = () => Date.now()) {
  if (!isGuid(recipientId)) throw error('INVALID_RESPONSE');
  const scope = prefix(configuration, identity) + recipientId + ':';
  const read = (name) => {
    let row; try { row = platform.getStorageSync(scope + name); } catch (_) { throw error('STORAGE_REQUIRED'); }
    if (!row) return null;
    if (row.version !== 1 || !Number.isFinite(row.createdAt) || row.createdAt > now() + 60000) throw error('LOCAL_STATE_INVALID');
    return row;
  };
  const put = (name, row) => { try { platform.setStorageSync(scope + name, row); } catch (_) { throw error('STORAGE_REQUIRED'); } };
  const remove = (name) => { try { platform.removeStorageSync(scope + name); } catch (_) { throw error('STORAGE_REQUIRED'); } };
  return {
    drafts(current) {
      const values = {}; let expired = 0;
      for (const q of current.questions) {
        const name = 'draft:' + current.id + ':' + q.id; const row = read(name); if (!row) continue;
        if (now() - row.createdAt >= ttl) { remove(name); expired++; } else values[q.id] = row.draft;
      }
      return { values, expired };
    },
    saveDraft(value) {
      if (!isGuid(value.attemptId) || !isGuid(value.questionId)) throw error('LOCAL_STATE_INVALID');
      put('draft:' + value.attemptId + ':' + value.questionId, { version: 1, createdAt: now(), draft: value });
    },
    removeDraft(attemptId, questionId) { if (!isGuid(attemptId) || !isGuid(questionId)) throw error('LOCAL_STATE_INVALID'); remove('draft:' + attemptId + ':' + questionId); },
    pending() {
      const row = read('pending'); if (!row) return null;
      if (!['start', 'response', 'image', 'submit'].includes(row.kind) || !isGuid(row.operationId) || row.recipientId !== recipientId || row.kind !== 'start' && !isGuid(row.attemptId)) throw error('LOCAL_STATE_INVALID');
      if (['response', 'image'].includes(row.kind) && !isGuid(row.questionId) || row.payload && (typeof row.payload !== 'object' || row.payload.operationId !== row.operationId)) throw error('LOCAL_STATE_INVALID');
      if (row.kind === 'image' && (!row.image || !/^[a-f0-9]{64}$/.test(row.image.contentHash) || !Number.isSafeInteger(row.image.size) || row.image.size < 1 ||
          !['jpg', 'png', 'webp'].includes(row.image.extension) || row.image.contentType !== { jpg: 'image/jpeg', png: 'image/png', webp: 'image/webp' }[row.image.extension])) throw error('LOCAL_STATE_INVALID');
      let result = row;
      if (row.payload && now() - row.createdAt >= ttl) { result = { ...result, payload: null }; put('pending', result); }
      if (row.kind === 'image' && (row.needsImageCleanup || row.cachedAt !== null && now() - (Number.isFinite(row.cachedAt) ? row.cachedAt : row.createdAt) >= ttl)) {
        result = { ...result, cachedAt: null, needsImageCleanup: true }; put('pending', result);
        try { removeImage(platform, row); } catch (_) { throw error('LOCAL_CLEANUP_UNCONFIRMED'); }
        result = { ...result, needsImageCleanup: false }; put('pending', result);
      }
      return result;
    },
    begin(value) {
      if (read('pending')) throw error('PENDING_OPERATION');
      if (!isGuid(value.operationId) || !['start', 'response', 'image', 'submit'].includes(value.kind)) throw error('LOCAL_STATE_INVALID');
      put('pending', { ...value, version: 1, recipientId, createdAt: now() });
    },
    acknowledge(operationId) { const row = read('pending'); if (!row || row.operationId !== operationId) throw error('LOCAL_STATE_INVALID'); remove('pending'); },
    imageRetained(operationId) { const row = read('pending'); if (!row || row.kind !== 'image' || row.operationId !== operationId) throw error('LOCAL_STATE_INVALID'); put('pending', { ...row, cachedAt: now(), needsImageCleanup: false }); },
    imagePath: (pending) => imagePath(platform, pending.operationId, pending.image.extension),
    removeImage: (pending) => { try { removeImage(platform, pending); } catch (_) { throw error('LOCAL_CLEANUP_UNCONFIRMED'); } },
  };
};
module.exports.clearAccount = clearAccount;
module.exports.imagePath = imagePath;
module.exports.notebookFilePath = notebookFilePath;
module.exports.prefix = prefix;
