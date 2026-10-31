const correction = require('./correction');
const images = require('./answer-image');
const { actor, uuid } = require('./feedback');
module.exports = function command(platform, session, store, deliveryId, schoolId, active) {
  const owner = actor(session.getState()); let busy = false;
  const requireOwner = () => { if (!owner || actor(session.getState()) !== owner || !correction.allowed(session.getState(), schoolId)) throw correction.error('FORBIDDEN'); if (!active()) throw correction.error('STALE_RESPONSE'); };
  const pending = () => { requireOwner(); const p = store.pending(); if (!p) throw correction.error('PENDING_REQUIRED'); return p; };
  function valid(row, p) {
    if (!row) return null;
    if (row.id !== p.operationId) throw correction.error();
    if (p.kind === 'image') {
      if (row.contentHash !== p.image.contentHash || row.size !== p.image.size || row.contentType !== p.image.contentType) throw correction.error();
      return row.isUploaded ? row : null;
    }
    correction.submission(row, deliveryId);
    if (p.payload && ((row.basedOnSubmissionId || null) !== p.payload.expectedSubmissionId || JSON.stringify(correction.content(row.content)) !== JSON.stringify(p.payload.content))) throw correction.error();
    return row;
  }
  async function query(p) {
    const row = await session.request(`/api/app/education-assessment/my-correction-${p.kind === 'image' ? 'image-operation' : 'operation'}?deliveryId=${deliveryId}&operationId=${p.operationId}`);
    requireOwner(); return valid(row, p);
  }
  async function send(p) {
    let row;
    if (p.kind === 'image') {
      await images.retain(platform, store.imagePath(p), p.operationId, p.image, correction.imagePolicy); requireOwner();
      row = await session.uploadCorrectionImage(deliveryId, p.operationId, store.imagePath(p));
    } else { if (!p.payload) throw correction.error('RETRY_BODY_LOST'); row = await session.request(`/api/app/education-assessment/submit-my-correction/${deliveryId}`, 'POST', p.payload); }
    requireOwner(); const confirmed = valid(row, p); if (!confirmed) throw correction.error(); return confirmed;
  }
  async function retainCopy(filePath, p) {
    try { await images.retain(platform, filePath, p.operationId, p.image, correction.imagePolicy); store.retained(p.operationId); requireOwner(); }
    catch (e) { if (actor(session.getState()) !== owner || !correction.allowed(session.getState(), schoolId)) store.clearPrivate(); throw e; }
  }
  const run = async (work) => { if (busy) throw correction.error('BUSY'); busy = true; try { return await work(); } finally { busy = false; } };
  return {
    async beginSubmission(input) {
      requireOwner(); if (store.pending()) throw correction.error('PENDING_OPERATION'); const operationId = await uuid(platform); requireOwner();
      store.begin({ kind: 'submit', operationId, payload: { ...input, operationId } });
    },
    async beginImage(filePath) {
      requireOwner(); if (store.pending()) throw correction.error('PENDING_OPERATION');
      const image = await images.inspect(platform, filePath, correction.imagePolicy), operationId = await uuid(platform); requireOwner();
      store.begin({ kind: 'image', operationId, image, payload: null });
      await retainCopy(filePath, store.pending());
    },
    async reselect(filePath) { const p = pending(); if (p.kind !== 'image') throw correction.error('PENDING_REQUIRED'); await retainCopy(filePath, p); },
    send: () => run(() => send(pending())),
    recover: () => run(() => query(pending())),
    retry: () => run(async () => { const p = pending(); return await query(p) || await send(p); }),
    state() { const p = store.pending(); return p ? { kind: p.kind, operationId: p.operationId, canRetry: p.kind === 'image' || !!p.payload } : null; },
  };
};
