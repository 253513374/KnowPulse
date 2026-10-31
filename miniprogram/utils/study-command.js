const study = require('./study');
const { actor, uuid } = require('./feedback');
const { isGuid } = require('./assessment');
const error = (code) => Object.assign(new Error(code), { code });

module.exports = function studyCommand(platform, session, config, recipientId, schoolId, active) {
  const owner = actor(session.getState());
  if (!owner || !isGuid(recipientId) || !isGuid(schoolId)) throw error('AUTH_REQUIRED');
  const key = `knowpulse:study-op:${encodeURIComponent(config.apiBaseUrl)}:${config.appKey}:${config.clientId}:${owner}:${recipientId}`;
  let pending = null; let payload = null; let busy = false;
  try { pending = platform.getStorageSync(key) || null; } catch (_) { throw error('STORAGE_REQUIRED'); }
  if (pending && (pending.recipientId !== recipientId || !isGuid(pending.operationId))) throw error('LOCAL_STATE_INVALID');
  const requireActive = () => { if (actor(session.getState()) !== owner || !active()) throw error('STALE_RESPONSE'); if (!study.allowed(session.getState(), schoolId)) throw error('FORBIDDEN'); };
  function complete(row) {
    requireActive(); if (!row || !pending || row.id !== pending.operationId) throw error('INVALID_RESPONSE');
    study.event(row, recipientId);
    if (payload && (row.action !== payload.action || (row.sharedText || null) !== (payload.sharedText || null) || (row.difficulty ?? null) !== payload.difficulty || (row.reportedMinutes ?? null) !== payload.reportedMinutes)) throw error('INVALID_RESPONSE');
    try { platform.removeStorageSync(key); } catch (_) { throw error('STORAGE_REQUIRED'); }
    pending = null; payload = null; return row;
  }
  async function recover() {
    requireActive(); if (!pending) return null;
    const row = await session.request(`/api/app/education-study-task/my-operation?recipientId=${recipientId}&operationId=${pending.operationId}`);
    requireActive(); return row ? complete(row) : null;
  }
  return {
    state: () => ({ pending: pending ? { ...pending } : null, canRetry: !!payload, busy }),
    dropPayload() { payload = null; },
    async start(input) {
      requireActive(); if (busy) throw error('BUSY'); if (pending) throw error('PENDING_OPERATION');
      busy = true;
      try {
        const operationId = await uuid(platform); requireActive();
        const ref = { recipientId, operationId };
        try { platform.setStorageSync(key, ref); } catch (_) { throw error('STORAGE_REQUIRED'); }
        pending = ref; payload = JSON.parse(JSON.stringify({ ...input, operationId }));
        return complete(await session.request(`/api/app/education-study-task/save-my-progress/${recipientId}`, 'POST', payload));
      } finally { busy = false; }
    },
    async recover() { if (busy) throw error('BUSY'); busy = true; try { return await recover(); } finally { busy = false; } },
    async retry() {
      requireActive(); if (busy) throw error('BUSY'); busy = true;
      try { const row = await recover(); if (row) return row; if (!pending || !payload) throw error('RETRY_BODY_LOST');
        return complete(await session.request(`/api/app/education-study-task/save-my-progress/${recipientId}`, 'POST', payload));
      } finally { busy = false; }
    },
    abandon() { requireActive(); if (busy) throw error('BUSY'); try { platform.removeStorageSync(key); } catch (_) { throw error('STORAGE_REQUIRED'); } pending = null; payload = null; },
  };
};
