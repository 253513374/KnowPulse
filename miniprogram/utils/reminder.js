const assessment = require('./assessment');
const { actor, uuid } = require('./feedback');
const fail = (code = 'INVALID_RESPONSE') => { throw Object.assign(new Error(code), { code }); };
const fingerprint = (v) => typeof v === 'string' && /^[0-9a-f]{64}$/i.test(v);
const reasons = { Submitted: '已交卷，无需催办', Closed: '已封卷，不再催办', Expired: '已截止，不再催办', PlanStopped: '关联方案已停止或不可执行', CoolingDown: '一小时内不重复催办' };
function receipt(row, school, task, recipient) {
  if (!row || row.schoolId !== school || ![row.id, row.taskId, row.publicationId, row.recipientId].every(assessment.isGuid) || task && row.taskId !== task || recipient && row.recipientId !== recipient ||
      ![0, 1, 2].includes(row.kind) || typeof row.canOpen !== 'boolean' || (row.kind === 2 ? !assessment.isGuid(row.resultReleaseId) : row.resultReleaseId != null) ||
      !Number.isFinite(Date.parse(row.recordedAtUtc)) || !Number.isFinite(Date.parse(row.availableAtUtc)) || Date.parse(row.availableAtUtc) < Date.parse(row.recordedAtUtc) ||
      row.readAtUtc != null && (!Number.isFinite(Date.parse(row.readAtUtc)) || Date.parse(row.readAtUtc) < Date.parse(row.availableAtUtc)) || row.weChatDelivery !== 'NotConfigured') fail();
  return { id: row.id, schoolId: school, taskId: row.taskId, publicationId: row.publicationId, recipientId: row.recipientId, kind: row.kind, resultReleaseId: row.resultReleaseId || null, canOpen: row.canOpen,
    message: ['老师提醒你查看测评任务', '你有新的测评任务，请查看安排', '测评结果有更新，请核对获准查看范围'][row.kind], at: assessment.time(row.availableAtUtc), read: row.readAtUtc ? assessment.time(row.readAtUtc) : '未读', weChat: '微信未配置，未投递' };
}
function preview(row, school, task) {
  if (!row || row.schoolId !== school || row.taskId !== task || ![row.id, row.publicationId].every(assessment.isGuid) || !fingerprint(row.previewFingerprint) ||
      typeof row.canSend !== 'boolean' || typeof row.hasStarted !== 'boolean' || typeof row.blockReason !== 'string' ||
      typeof row.studentName !== 'string' || !row.studentName.trim() || row.studentName.length > 256 || typeof row.taskTitle !== 'string' || !row.taskTitle.trim() || row.taskTitle.length > 256 ||
      !Number.isFinite(Date.parse(row.endsAtUtc)) || !Number.isFinite(Date.parse(row.serverNowUtc)) ||
      row.nextAllowedAtUtc != null && !Number.isFinite(Date.parse(row.nextAllowedAtUtc)) || row.canSend !== (row.blockReason === '') || row.blockReason && !reasons[row.blockReason]) fail();
  return { id: row.id, schoolId: school, taskId: task, publicationId: row.publicationId, name: typeof row.studentName === 'string' ? row.studentName : '', title: typeof row.taskTitle === 'string' ? row.taskTitle : '',
    fingerprint: row.previewFingerprint, canSend: row.canSend, status: reasons[row.blockReason] || (row.hasStarted ? '作答中，尚未交卷' : '尚未开始'), ends: assessment.time(row.endsAtUtc), next: assessment.time(row.nextAllowedAtUtc) };
}
function page(dto, mode, school, task) {
  if (!dto || !Number.isSafeInteger(dto.totalCount) || dto.totalCount < 0 || !Array.isArray(dto.items) || dto.items.length > 20) fail();
  return { total: dto.totalCount, items: dto.items.map((r) => mode === 'teacher' ? preview(r, school, task) : receipt(r, school)) };
}
function command(platform, session, config, school, task, active) {
  const owner = actor(session.getState()); if (!owner || ![school, task].every(assessment.isGuid)) fail('AUTH_REQUIRED');
  const key = `knowpulse:reminder-op:${encodeURIComponent(config.apiBaseUrl)}:${config.appKey}:${config.clientId}:${owner}:${school}:${task}`;
  let pending, busy = false;
  try { pending = platform.getStorageSync(key) || null; } catch (_) { fail('STORAGE_REQUIRED'); }
  if (pending) pending = pending.schoolId !== school || pending.taskId !== task || ![pending.recipientId, pending.operationId, pending.publicationId].every(assessment.isGuid) || !fingerprint(pending.previewFingerprint)
    ? { invalid: true } : { schoolId: school, taskId: task, recipientId: pending.recipientId, operationId: pending.operationId, publicationId: pending.publicationId, previewFingerprint: pending.previewFingerprint };
  const guard = () => { if (actor(session.getState()) !== owner || !active()) fail('STALE_RESPONSE'); if (!assessment.allowed(session.getState(), 'teacher', school)) fail('FORBIDDEN'); };
  function complete(row) {
    guard(); const parsed = receipt(row, school, task, pending.recipientId);
    if (parsed.kind !== 0 || parsed.id !== pending.operationId || parsed.publicationId !== pending.publicationId) fail();
    try { platform.removeStorageSync(key); } catch (_) { fail('STORAGE_REQUIRED'); } pending = null; return parsed;
  }
  async function recover() { guard(); if (!pending) return null; if (pending.invalid) fail('LOCAL_STATE_INVALID'); const r = await session.request(`/api/app/education-assessment/reminder-operation?recipientId=${pending.recipientId}&schoolId=${school}&operationId=${pending.operationId}`); guard(); return r ? complete(r) : null; }
  const send = async () => complete(await session.request(`/api/app/education-assessment/send-reminder?recipientId=${pending.recipientId}&schoolId=${school}`, 'POST', { operationId: pending.operationId, previewFingerprint: pending.previewFingerprint, confirmRecipient: true }));
  return {
    state: () => ({ pending: pending ? { ...pending } : null }),
    async start(row) { guard(); if (busy || pending) fail('PENDING_OPERATION'); if (!row.canSend || row.schoolId !== school || row.taskId !== task || ![row.id, row.publicationId].every(assessment.isGuid) || !fingerprint(row.fingerprint)) fail(); busy = true;
      try { const operationId = await uuid(platform); guard(); const ref = { schoolId: school, taskId: task, recipientId: row.id, operationId, publicationId: row.publicationId, previewFingerprint: row.fingerprint };
        try { platform.setStorageSync(key, ref); } catch (_) { fail('STORAGE_REQUIRED'); } pending = ref; return await send();
      } finally { busy = false; } },
    async recover() { guard(); if (busy) fail('BUSY'); busy = true; try { return await recover(); } finally { busy = false; } },
    async retry() { guard(); if (busy) fail('BUSY'); busy = true; try { const r = await recover(); if (r || !pending) return r; guard(); return await send(); } finally { busy = false; } },
    abandon() { guard(); if (busy) fail('BUSY'); try { platform.removeStorageSync(key); } catch (_) { fail('STORAGE_REQUIRED'); } pending = null; },
  };
}
module.exports = { preview, receipt, page, command };
