const feedback = require('./feedback');
const { isGuid } = require('./assessment');
const error = (code) => Object.assign(new Error(code), { code });

module.exports = function feedbackCommand(platform, session, config, feedbackId, schoolId, recipient) {
  const owner = feedback.actor(session.getState());
  if (!owner || !isGuid(feedbackId)) throw error('AUTH_REQUIRED');
  const key = `knowpulse:feedback-op:${encodeURIComponent(config.apiBaseUrl)}:${config.appKey}:${config.clientId}:${owner}:${feedbackId}`;
  let pending = null; let payload = null; let busy = false;
  try { pending = platform.getStorageSync(key) || null; } catch (_) { throw error('STORAGE_REQUIRED'); }
  const kinds = ['draft', 'publish', 'withdraw', 'observation', 'phrase'];
  if (pending && (pending.feedbackId !== feedbackId || !isGuid(pending.operationId) || !kinds.includes(pending.kind) || pending.kind === 'phrase' && !isGuid(pending.phraseId))) throw error('LOCAL_STATE_INVALID');
  const requireOwner = () => { if (feedback.actor(session.getState()) !== owner) throw error('STALE_RESPONSE'); };
  const requireWrite = () => { requireOwner(); if (!feedback.allowed(session.getState(), 'teacher', schoolId) || recipient && !feedback.teachingAllowed(session.getState(), schoolId, recipient.teachingAssignmentId)) throw error('FORBIDDEN'); };
  const path = (kind) => kind === 'observation' ? `/api/app/education-feedback/observation-for-school/${schoolId}` :
    kind === 'phrase' ? `/api/app/education-feedback/save-phrase/${pending.phraseId}` : `/api/app/education-feedback/${kind === 'draft' ? 'save-draft' : kind}/${feedbackId}`;
  function complete(receipt) {
    requireOwner();
    if (!receipt || !pending || receipt.id !== pending.operationId) throw error('INVALID_RESPONSE');
    if (pending.kind === 'observation') {
      feedback.observation(receipt, schoolId, recipient);
      if (payload && (receipt.text !== payload.text || Date.parse(receipt.observedAtUtc) !== Date.parse(payload.observedAtUtc))) throw error('INVALID_RESPONSE');
    } else if (pending.kind === 'phrase') {
      if (receipt.phraseId !== pending.phraseId || !isGuid(receipt.phraseId) || typeof receipt.text !== 'string' || !receipt.text.trim() || receipt.text.length > 4000 ||
          !Number.isSafeInteger(receipt.revisionNumber) || receipt.revisionNumber < 1 || !Number.isFinite(Date.parse(receipt.recordedAtUtc)) || typeof receipt.isArchived !== 'boolean' ||
          payload && (receipt.text !== payload.text || receipt.isArchived !== payload.isArchived)) throw error('INVALID_RESPONSE');
    } else if (receipt.feedbackId !== feedbackId || receipt.kind !== { draft: 0, publish: 1, withdraw: 2 }[pending.kind]) throw error('INVALID_RESPONSE');
    // Only identifiers persist. Exact retry text remains in this running editor and is dropped on logout/unload.
    try { platform.removeStorageSync(key); } catch (_) { throw error('STORAGE_REQUIRED'); }
    pending = null; payload = null; return receipt;
  }
  async function recover() {
    requireWrite(); if (!pending) return null;
    const route = pending.kind === 'observation' ? `/api/app/education-feedback/observation-operation?schoolId=${schoolId}&operationId=${pending.operationId}&${feedback.query(recipient)}` :
      pending.kind === 'phrase' ? `/api/app/education-feedback/phrase-operation?phraseId=${pending.phraseId}&operationId=${pending.operationId}&${feedback.phraseScope(schoolId, recipient.teachingAssignmentId)}` :
      `/api/app/education-feedback/operation?feedbackId=${feedbackId}&operationId=${pending.operationId}`;
    const receipt = await session.request(route);
    requireOwner(); return receipt ? complete(receipt) : null;
  }
  return {
    state: () => ({ pending: pending ? { ...pending } : null, canRetry: payload !== null, busy }),
    dropPayload() { payload = null; },
    async start(kind, input) {
      requireWrite(); if (busy) throw error('BUSY'); if (pending) throw error('PENDING_OPERATION');
      if (!kinds.includes(kind) || ['observation', 'phrase'].includes(kind) && (!recipient || !feedback.teachingAllowed(session.getState(), schoolId, recipient.teachingAssignmentId))) throw error('INVALID_PATH');
      busy = true;
      try {
        const operationId = await feedback.uuid(platform); requireWrite();
        const ref = { feedbackId, operationId, kind, ...(kind === 'phrase' ? { phraseId: input.phraseId } : {}) };
        if (kind === 'phrase' && !isGuid(ref.phraseId)) throw error('INVALID_PATH');
        try { platform.setStorageSync(key, ref); } catch (_) { throw error('STORAGE_REQUIRED'); }
        pending = ref; payload = JSON.parse(JSON.stringify({ ...input, operationId }));
        if (kind === 'phrase') delete payload.phraseId;
        return complete(await session.request(path(kind), 'POST', payload));
      } finally { busy = false; }
    },
    async recover() { if (busy) throw error('BUSY'); busy = true; try { return await recover(); } finally { busy = false; } },
    async retry() {
      requireWrite(); if (busy) throw error('BUSY'); busy = true;
      try {
        const receipt = await recover(); if (receipt) return receipt;
        if (!pending || !payload) throw error('RETRY_BODY_LOST');
        return complete(await session.request(path(pending.kind), 'POST', payload));
      } finally { busy = false; }
    },
    abandon() {
      requireOwner(); if (busy) throw error('BUSY');
      try { platform.removeStorageSync(key); } catch (_) { throw error('STORAGE_REQUIRED'); }
      pending = null; payload = null;
    },
  };
};
