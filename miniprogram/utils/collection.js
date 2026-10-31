const { isGuid, time } = require('./assessment');
const { actor, uuid } = require('./feedback');
const error = (code = 'INVALID_RESPONSE') => Object.assign(new Error(code), { code });
const route = '/api/app/education-assessment';
function allowed(s, school) { return isGuid(school) && s.authenticated && s.context &&
  ['AssessmentParticipate', 'LearningParticipate'].every((p) => s.context.grantedPermissions.includes('KnowPulse.Education.' + p)) && s.context.workspaces.some((w) => w.role === 2 && w.schoolId === school); }
function row(v, school, recipient, question) {
  if (!v || ![v.id, v.recipientId, v.paperQuestionId].every(isGuid) || v.schoolId !== school || recipient && v.recipientId !== recipient || question && v.paperQuestionId !== question ||
      !Number.isSafeInteger(v.revisionNumber) || v.revisionNumber < 1 || typeof v.isCollected !== 'boolean' || typeof v.sourceAvailable !== 'boolean' || !Number.isFinite(Date.parse(v.recordedAtUtc))) throw error();
  return { id: v.id, recipientId: v.recipientId, questionId: v.paperQuestionId, version: v.revisionNumber, collected: v.isCollected, available: v.sourceAvailable,
    at: time(v.recordedAtUtc), title: typeof v.taskTitle === 'string' ? v.taskTitle : '' };
}
function context(v, school, recipient, question, expectedRelease) {
  if (!v || v.schoolId !== school || v.recipientId !== recipient || v.paperQuestionId !== question || typeof v.canCollect !== 'boolean' ||
      (v.canCollect ? !isGuid(v.resultReleaseId) : v.resultReleaseId != null)) throw error();
  const current = v.current ? row(v.current, school, recipient, question) : null;
  return { recipientId: recipient, questionId: question, current, canCollect: v.canCollect && (!expectedRelease || expectedRelease === v.resultReleaseId),
    changed: !!expectedRelease && v.resultReleaseId !== expectedRelease, releaseId: v.resultReleaseId || null };
}
const prefix = (config, s, school) => `knowpulse:collection-op:${encodeURIComponent(config.apiBaseUrl)}:${config.appKey}:${config.clientId}:${actor(s)}:${school}:`;
function commandInput(v, school, question) {
  if (!v || v.schoolId !== school || v.paperQuestionId !== question || !isGuid(v.operationId) || typeof v.isCollected !== 'boolean' ||
      !(v.expectedCurrentOperationId === null || isGuid(v.expectedCurrentOperationId)) || !(v.expectedResultReleaseId === null || isGuid(v.expectedResultReleaseId)) || v.isCollected && !isGuid(v.expectedResultReleaseId)) throw error('LOCAL_STATE_INVALID');
  return { schoolId: school, operationId: v.operationId, paperQuestionId: question, expectedCurrentOperationId: v.expectedCurrentOperationId,
    expectedResultReleaseId: v.expectedResultReleaseId, isCollected: v.isCollected };
}
function reference(v, school) {
  if (!v || !isGuid(v.recipientId) || !isGuid(v.questionId)) throw error('LOCAL_STATE_INVALID');
  return { recipientId: v.recipientId, questionId: v.questionId, input: commandInput(v.input, school, v.questionId) };
}
function pending(platform, config, session, school) {
  const start = prefix(config, session.getState(), school);
  try { return platform.getStorageInfoSync().keys.filter((k) => k.startsWith(start)).map((k) => {
    const ref = reference(platform.getStorageSync(k), school); if (k !== start + ref.recipientId + ':' + ref.questionId) throw error('LOCAL_STATE_INVALID'); return ref;
  }); } catch (e) { throw error(e.code === 'LOCAL_STATE_INVALID' ? e.code : 'STORAGE_REQUIRED'); }
}
function writer(platform, session, config, school, recipient, question, active = () => true) {
  const owner = actor(session.getState()); if (!owner || ![school, recipient, question].every(isGuid)) throw error('AUTH_REQUIRED');
  const key = prefix(config, session.getState(), school) + recipient + ':' + question;
  let ref = null, busy = false;
  try { const stored = platform.getStorageSync(key); if (stored) { ref = reference(stored, school); if (ref.recipientId !== recipient || ref.questionId !== question) throw error('LOCAL_STATE_INVALID'); } }
  catch (e) { throw error(e.code === 'LOCAL_STATE_INVALID' ? e.code : 'STORAGE_REQUIRED'); }
  const requireActive = () => { if (actor(session.getState()) !== owner || !active()) throw error('STALE_RESPONSE'); if (!allowed(session.getState(), school)) throw error('FORBIDDEN'); };
  const clear = () => { try { platform.removeStorageSync(key); ref = null; } catch (_) { throw error('STORAGE_REQUIRED'); } };
  function complete(dto) {
    requireActive(); const value = row(dto, school, recipient, question);
    if (!ref || value.id !== ref.input.operationId || value.collected !== ref.input.isCollected) throw error(); clear(); return value;
  }
  async function recover() {
    requireActive(); if (!ref) return null;
    const dto = await session.request(`${route}/my-collection-operation?schoolId=${school}&recipientId=${recipient}&operationId=${ref.input.operationId}`);
    requireActive(); return dto ? complete(dto) : null;
  }
  return {
    state: () => ref ? reference(ref, school) : null,
    async start(wanted, current, release) {
      requireActive(); if (busy || ref) throw error(ref ? 'PENDING_OPERATION' : 'BUSY'); busy = true;
      try { const operationId = await uuid(platform); requireActive();
        const input = commandInput({ schoolId: school, operationId, paperQuestionId: question, expectedCurrentOperationId: current?.id || null, expectedResultReleaseId: wanted ? release : null, isCollected: wanted }, school, question);
        const next = { recipientId: recipient, questionId: question, input };
        try { platform.setStorageSync(key, next); } catch (_) { throw error('STORAGE_REQUIRED'); }
        ref = next; return complete(await session.request(`${route}/set-my-collection/${recipient}`, 'POST', input));
      } finally { busy = false; }
    },
    async recover() { if (busy) throw error('BUSY'); busy = true; try { return await recover(); } finally { busy = false; } },
    async retry() { requireActive(); if (busy) throw error('BUSY'); busy = true;
      try { const value = await recover(); if (value) return value; if (!ref) throw error('PENDING_REQUIRED'); return complete(await session.request(`${route}/set-my-collection/${recipient}`, 'POST', ref.input)); }
      finally { busy = false; }
    },
    abandon() { requireActive(); if (busy) throw error('BUSY'); clear(); },
  };
}
module.exports = { allowed, row, context, pending, writer };
