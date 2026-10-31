const practice = require('./practice');
const images = require('./answer-image');
const { actor, uuid } = require('./feedback');
const { isGuid } = require('./assessment');
module.exports = function command(platform, session, store, schoolId, id, recipientId, active) {
  const owner = actor(session.getState()); let busy = false;
  const requireOwner = () => { if (!owner || actor(session.getState()) !== owner || !practice.allowed(session.getState(), schoolId)) throw practice.error('FORBIDDEN'); if (!active()) throw practice.error('STALE_RESPONSE'); };
  const pending = () => { requireOwner(); const p = store.pending(); if (!p) throw practice.error('PENDING_REQUIRED'); return p; };
  function valid(r, p) {
    if (!r) return null;
    if (p.kind === 'image') { const row = practice.image(r); if (row.id !== p.operationId || row.contentHash !== p.image.contentHash || row.size !== p.image.size || row.contentType !== p.image.contentType) throw practice.error(); return row; }
    if (r.operationId !== p.operationId || r.practiceId !== (p.kind === 'start' ? p.operationId : id) || r.isSubmission !== (p.kind === 'submit') || !/^[a-f0-9]{64}$/.test(r.requestFingerprint) || !Number.isFinite(Date.parse(r.recordedAtUtc))) throw practice.error();
    return { practiceId: r.practiceId, operationId: r.operationId, isSubmission: r.isSubmission, recordedAtUtc: r.recordedAtUtc };
  }
  async function query(p) {
    const route = p.kind === 'start' ? `my-practice-start-operation?schoolId=${schoolId}&recipientId=${recipientId}&operationId=${p.operationId}` :
      `${id}/${p.kind === 'image' ? 'my-practice-image-operation' : 'my-practice-operation'}/${p.operationId}`;
    const r = await session.request('/api/app/education-assessment/' + route); requireOwner();
    if (p.kind === 'image' && r?.isUploaded === false) return null;
    return valid(r, p);
  }
  async function send(p) {
    let r;
    if (p.kind === 'image') { await images.retain(platform, store.imagePath(p), p.operationId, p.image, practice.imagePolicy); requireOwner(); r = await session.uploadPracticeImage(id, p.operationId, store.imagePath(p)); }
    else if (p.kind === 'start') r = await session.request(`/api/app/education-assessment/start-my-practice-for-school?schoolId=${schoolId}&recipientId=${recipientId}`, 'POST', p.input);
    else { if (!p.payload) throw practice.error('RETRY_BODY_LOST'); r = await session.request(`/api/app/education-assessment/${id}/submit-my-practice`, 'POST', p.payload); }
    requireOwner(); return valid(r, p);
  }
  async function retain(file, p) { try { await images.retain(platform, file, p.operationId, p.image, practice.imagePolicy); store.retained(p.operationId); requireOwner(); }
    catch (e) { if (actor(session.getState()) !== owner || !practice.allowed(session.getState(), schoolId)) store.clearPrivate(); throw e; } }
  const run = async (work) => { if (busy) throw practice.error('BUSY'); busy = true; try { return await work(); } finally { busy = false; } };
  return {
    async beginStart(question, release) { requireOwner(); if (![recipientId, question, release].every(isGuid)) throw practice.error('INVALID_PATH'); const operationId = await uuid(platform); requireOwner(); store.begin({ kind: 'start', operationId, input: { operationId, paperQuestionId: question, expectedResultReleaseId: release } }); },
    async beginSubmit(input) { requireOwner(); const operationId = await uuid(platform); requireOwner(); store.begin({ kind: 'submit', operationId, payload: { ...input, operationId } }); },
    async beginImage(file) { requireOwner(); if (store.pending()) throw practice.error('PENDING_OPERATION'); const image = await images.inspect(platform, file, practice.imagePolicy), operationId = await uuid(platform); requireOwner(); store.begin({ kind: 'image', operationId, image, payload: null }); await retain(file, store.pending()); },
    async reselect(file) { const p = pending(); if (p.kind !== 'image') throw practice.error('PENDING_REQUIRED'); await retain(file, p); },
    send: () => run(() => send(pending())), recover: () => run(() => query(pending())), retry: () => run(async () => { const p = pending(); return await query(p) || await send(p); }),
    state() { const p = store.pending(); return p ? { kind: p.kind, operationId: p.operationId, canRetry: p.kind !== 'submit' || !!p.payload } : null; },
  };
};
