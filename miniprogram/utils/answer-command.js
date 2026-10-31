const assessment = require('./assessment');
const { uuid, actor } = require('./feedback');
const images = require('./answer-image');
const error = (code) => Object.assign(new Error(code), { code });

module.exports = function command(platform, session, store, recipientId, schoolId, active = () => true) {
  const owner = actor(session.getState()); let busy = false;
  const requireOwner = () => { if (!owner || actor(session.getState()) !== owner || !assessment.allowed(session.getState(), 'student', schoolId)) throw error('FORBIDDEN'); if (!active()) throw error('STALE_RESPONSE'); };
  const pending = () => { requireOwner(); const p = store.pending(); if (!p) throw error('PENDING_REQUIRED'); return p; };
  function valid(receipt, p) {
    if (!receipt) return null;
    if (!assessment.isGuid(receipt.id)) throw error('INVALID_RESPONSE');
    if (p.kind === 'start') { if (receipt.recipientId !== recipientId) throw error('INVALID_RESPONSE'); }
    else if (receipt.attemptId !== p.attemptId || (p.kind !== 'submit' && (receipt.id !== p.operationId || receipt.paperQuestionId !== p.questionId))) throw error('INVALID_RESPONSE');
    if (p.kind === 'image') {
      if (receipt.contentHash !== p.image.contentHash || receipt.size !== p.image.size || receipt.contentType !== p.image.contentType) throw error('INVALID_RESPONSE');
      if (!receipt.isUploaded) return null;
    }
    return receipt;
  }
  async function query(p) {
    let value;
    if (p.kind === 'start') {
      const task = await session.request('/api/app/education-assessment/my-student-task/' + recipientId);
      if (task.id !== recipientId || task.schoolId !== schoolId) throw error('INVALID_RESPONSE');
      value = task.currentAttemptId ? await session.request('/api/app/education-assessment/attempt/' + task.currentAttemptId) : null;
    } else if (p.kind === 'response') value = await session.request(`/api/app/education-assessment/response-revision?attemptId=${p.attemptId}&operationId=${p.operationId}`);
    else if (p.kind === 'image') value = await session.request(`/api/app/education-assessment/answer-image?attemptId=${p.attemptId}&operationId=${p.operationId}`);
    else value = await session.request('/api/app/education-assessment/submission/' + p.attemptId);
    requireOwner(); return valid(value, p);
  }
  async function send(p, policy) {
    requireOwner(); if (!p.payload && !['image', 'start'].includes(p.kind)) throw error('RETRY_BODY_LOST');
    let value;
    if (p.kind === 'image') {
      const filePath = store.imagePath(p); await images.retain(platform, filePath, p.operationId, p.image, policy); requireOwner();
      value = await session.uploadAnswerImage(p.attemptId, { operationId: p.operationId, paperQuestionId: p.questionId, contentHash: p.image.contentHash }, filePath);
    } else {
      const endpoint = p.kind === 'start' ? 'start-or-resume/' + recipientId : (p.kind === 'response' ? 'save-response/' : 'submit/') + p.attemptId;
      value = await session.request('/api/app/education-assessment/' + endpoint, 'POST', p.kind === 'start' ? { operationId: p.operationId } : p.payload);
    }
    requireOwner(); const receipt = valid(value, p); if (!receipt) throw error('INVALID_RESPONSE'); return receipt;
  }
  const run = async (action) => { if (busy) throw error('BUSY'); busy = true; try { return await action(); } finally { busy = false; } };
  return {
    state() { requireOwner(); const p = store.pending(); return p ? { kind: p.kind, operationId: p.operationId, questionId: p.questionId || null, canRetry: !!p.payload || ['image', 'start'].includes(p.kind) } : null; },
    async begin(kind, payload, extra = {}) {
      requireOwner(); if (store.pending()) throw error('PENDING_OPERATION');
      const operationId = await uuid(platform); requireOwner();
      store.begin({ kind, operationId, attemptId: extra.attemptId || null, questionId: extra.questionId || null, image: extra.image || null, replaces: extra.replaces || null,
        payload: JSON.parse(JSON.stringify({ ...payload, operationId })) });
      return store.pending();
    },
    send: (policy) => run(() => send(pending(), policy)),
    recover: () => run(() => query(pending())),
    retry: (policy) => run(async () => { const p = pending(); const receipt = await query(p); return receipt || await send(p, policy); }),
    acknowledge(operationId) { requireOwner(); store.acknowledge(operationId); },
    abandon() { requireOwner(); if (busy) throw error('BUSY'); const p = pending(); store.removeImage(p); store.acknowledge(p.operationId); },
  };
};
