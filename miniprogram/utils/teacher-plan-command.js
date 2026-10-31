const plan = require('./teacher-plan');
const { actor, uuid } = require('./feedback');
const { isGuid } = require('./assessment');
const error = (code) => Object.assign(new Error(code), { code });
module.exports = function command(platform, session, config, planId, schoolId, assignmentId, active) {
  const owner = actor(session.getState()); if (!owner || ![planId, schoolId, assignmentId].every(isGuid)) throw error('AUTH_REQUIRED');
  const key = `knowpulse:teacher-plan-op:${encodeURIComponent(config.apiBaseUrl)}:${config.appKey}:${config.clientId}:${owner}:${schoolId}:${planId}`;
  let pending = null, payload = null, busy = false;
  try { pending = platform.getStorageSync(key) || null; } catch (_) { throw error('STORAGE_REQUIRED'); }
  if (pending) pending = pending.planId !== planId || pending.schoolId !== schoolId || pending.assignmentId !== assignmentId || ![pending.activityId, pending.operationId].every(isGuid)
    ? { invalid: true } : { planId, schoolId, assignmentId, activityId: pending.activityId, operationId: pending.operationId };
  const requireOwner = () => { if (actor(session.getState()) !== owner || !active()) throw error('STALE_RESPONSE'); if (!plan.allowed(session.getState(), schoolId, assignmentId)) throw error('FORBIDDEN'); };
  const query = `schoolId=${schoolId}&teachingAssignmentId=${assignmentId}`;
  function complete(row) {
    requireOwner(); if (!row || !pending || row.id !== pending.operationId) throw error('INVALID_RESPONSE');
    plan.followUp(row, pending.activityId);
    if (payload && (row.internalTeacherNote !== payload.internalTeacherNote || row.replyToStudentRevisionId !== payload.expectedStudentRevisionId)) throw error('INVALID_RESPONSE');
    try { platform.removeStorageSync(key); } catch (_) { throw error('STORAGE_REQUIRED'); }
    pending = null; payload = null; return row;
  }
  async function recover() { requireOwner(); if (!pending) return null; if (pending.invalid) throw error('LOCAL_STATE_INVALID'); const row = await session.request(`/api/app/education-teaching-plan/activity-follow-up-operation-for-school?planId=${planId}&operationId=${pending.operationId}&${query}`); requireOwner(); return row ? complete(row) : null; }
  const send = () => session.request(`/api/app/education-teaching-plan/save-activity-follow-up-for-school?activityId=${pending.activityId}&${query}`, 'POST', payload);
  return {
    state: () => ({ pending: pending ? { ...pending } : null, canRetry: !!payload, busy }), dropPayload() { payload = null; },
    async start(activityId, input) {
      requireOwner(); if (busy || pending) throw error('PENDING_OPERATION'); if (!isGuid(activityId)) throw error('INVALID_PATH'); busy = true;
      try { const operationId = await uuid(platform); requireOwner(); const ref = { planId, schoolId, assignmentId, activityId, operationId };
        try { platform.setStorageSync(key, ref); } catch (_) { throw error('STORAGE_REQUIRED'); }
        pending = ref; payload = JSON.parse(JSON.stringify({ ...input, operationId })); return complete(await send());
      } finally { busy = false; }
    },
    async recover() { if (busy) throw error('BUSY'); busy = true; try { return await recover(); } finally { busy = false; } },
    async retry() { requireOwner(); if (busy) throw error('BUSY'); busy = true; try { const row = await recover(); if (row) return row; if (!pending || !payload) throw error('RETRY_BODY_LOST'); requireOwner(); return complete(await send()); } finally { busy = false; } },
    abandon() { requireOwner(); if (busy) throw error('BUSY'); try { platform.removeStorageSync(key); } catch (_) { throw error('STORAGE_REQUIRED'); } pending = null; payload = null; },
  };
};
