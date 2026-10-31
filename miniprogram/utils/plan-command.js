const plan = require('./plan');
const { actor, uuid } = require('./feedback');
const { isGuid } = require('./assessment');
const error = (code) => Object.assign(new Error(code), { code });

module.exports = function planCommand(platform, session, config, planId, schoolId, active) {
  const owner = actor(session.getState());
  if (!owner || !isGuid(planId) || !isGuid(schoolId)) throw error('AUTH_REQUIRED');
  const key = `knowpulse:plan-op:${encodeURIComponent(config.apiBaseUrl)}:${config.appKey}:${config.clientId}:${owner}:${schoolId}:${planId}`;
  let pending = null, payload = null, busy = false;
  try { pending = platform.getStorageSync(key) || null; } catch (_) { throw error('STORAGE_REQUIRED'); }
  if (pending && (pending.planId !== planId || pending.schoolId !== schoolId || !isGuid(pending.activityId) || !isGuid(pending.operationId))) throw error('LOCAL_STATE_INVALID');
  const requireRead = () => { if (actor(session.getState()) !== owner || !active()) throw error('STALE_RESPONSE'); if (!plan.allowed(session.getState(), schoolId)) throw error('FORBIDDEN'); };
  const requireWrite = () => { requireRead(); if (!plan.allowed(session.getState(), schoolId, true)) throw error('FORBIDDEN'); };
  function complete(row) {
    requireRead(); if (!row || !pending || row.id !== pending.operationId) throw error('INVALID_RESPONSE');
    plan.event(row, pending.activityId);
    if (payload && (row.action !== payload.action || (row.sharedText || null) !== (payload.sharedText || null) || (row.difficulty ?? null) !== payload.difficulty || (row.reportedMinutes ?? null) !== payload.reportedMinutes)) throw error('INVALID_RESPONSE');
    try { platform.removeStorageSync(key); } catch (_) { throw error('STORAGE_REQUIRED'); }
    pending = null; payload = null; return row;
  }
  async function recover() {
    requireRead(); if (!pending) return null;
    const row = await session.request(`/api/app/education-teaching-plan/my-activity-operation?planId=${planId}&operationId=${pending.operationId}`);
    requireRead(); return row ? complete(row) : null;
  }
  return {
    state: () => ({ pending: pending ? { ...pending } : null, canRetry: !!payload, busy }),
    dropPayload() { payload = null; },
    async start(activityId, input) {
      requireWrite(); if (busy) throw error('BUSY'); if (pending) throw error('PENDING_OPERATION'); if (!isGuid(activityId)) throw error('INVALID_PATH');
      busy = true;
      try {
        const operationId = await uuid(platform); requireWrite(); const ref = { planId, schoolId, activityId, operationId };
        try { platform.setStorageSync(key, ref); } catch (_) { throw error('STORAGE_REQUIRED'); }
        pending = ref; payload = JSON.parse(JSON.stringify({ ...input, operationId }));
        return complete(await session.request(`/api/app/education-teaching-plan/save-my-activity/${activityId}`, 'POST', payload));
      } finally { busy = false; }
    },
    async recover() { if (busy) throw error('BUSY'); busy = true; try { return await recover(); } finally { busy = false; } },
    async retry() {
      requireWrite(); if (busy) throw error('BUSY'); busy = true;
      try { const row = await recover(); if (row) return row; if (!pending || !payload) throw error('RETRY_BODY_LOST'); requireWrite();
        return complete(await session.request(`/api/app/education-teaching-plan/save-my-activity/${pending.activityId}`, 'POST', payload));
      } finally { busy = false; }
    },
    abandon() { requireRead(); if (busy) throw error('BUSY'); try { platform.removeStorageSync(key); } catch (_) { throw error('STORAGE_REQUIRED'); } pending = null; payload = null; },
  };
};
