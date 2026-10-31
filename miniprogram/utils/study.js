const { isGuid, time } = require('./assessment');
const invalid = () => { throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' }); };
const text = (value) => typeof value === 'string' ? value : '';
const availability = ['尚未开始', '可学习', '已截止', '已被新版本替换', '已撤回', '材料暂不可用'];
const states = ['尚未开始', '学习中', '遇到困难', '自报完成'];
const actions = ['开始学习', '自报完成', '困难反馈'];
const difficulties = ['内容不理解', '时间不足', '材料无法读取', '其他困难'];
const allowed = (state, schoolId) => isGuid(schoolId) && state.authenticated && state.context &&
  state.context.grantedPermissions.includes('KnowPulse.Education.LearningParticipate') && state.context.workspaces.some((w) => w.schoolId === schoolId && w.role === 2);

function event(row, recipientId) {
  if (!row || !isGuid(row.id) || row.recipientId !== recipientId || ![0, 1, 2].includes(row.action) ||
      !Number.isSafeInteger(row.studentRevisionNumber) || row.studentRevisionNumber < 1 || !/^[a-f0-9]{64}$/i.test(row.requestFingerprint)) invalid();
  return { id: row.id, revision: row.studentRevisionNumber, action: row.action, label: actions[row.action], difficulty: difficulties[row.difficulty] || '',
    text: text(row.sharedText), minutes: row.reportedMinutes, at: time(row.recordedAtUtc) };
}
function task(row, schoolId, body = false) {
  if (!row || row.schoolId !== schoolId || !isGuid(row.id) || !isGuid(row.publicationId) || !availability[row.availability] || !states[row.state] ||
      !Number.isFinite(Date.parse(row.serverNowUtc)) || !Array.isArray(row.materials)) invalid();
  const readable = body && [1, 2].includes(row.availability) && row.materialsAvailable === true;
  return { id: row.id, schoolId, publicationId: row.publicationId, title: text(row.title), version: row.versionNumber, teacher: text(row.teacherName),
    className: text(row.className), subject: text(row.subjectName), textbook: text(row.textbookTitle), starts: time(row.startsAtUtc), ends: time(row.endsAtUtc),
    minutes: row.estimatedMinutes, availability: row.availability, status: availability[row.availability], state: row.state, stateLabel: states[row.state],
    reason: text(row.publicReason), instructions: readable ? text(row.instructions) : '',
    materials: readable ? row.materials.map((m) => { if (!isGuid(m.id)) invalid(); return { id: m.id, title: text(m.title) }; }) : [],
    currentStudentRevisionId: row.currentStudentRevisionId || null, current: row.currentStudent ? event(row.currentStudent, row.id) : null,
    handled: time(row.teacherHandledAtUtc), canStart: row.availability === 1 && row.state !== 1,
    canComplete: row.availability === 1 && row.state === 1,
    canReport: !!row.firstProvidedAtUtc && [1, 2, 5].includes(row.availability) };
}
function attachmentType(row) {
  // shortcut: preview common files up to 20 MB; extend formats and size after device acceptance.
  const ext = text(row.fileName).split('.').pop().toLowerCase();
  const types = { pdf: 'application/pdf', doc: 'application/msword', docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    xls: 'application/vnd.ms-excel', xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', ppt: 'application/vnd.ms-powerpoint',
    pptx: 'application/vnd.openxmlformats-officedocument.presentationml.presentation', jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', webp: 'image/webp' };
  return types[ext] === row.contentType && row.size > 0 && row.size <= 20 * 1024 * 1024 ? ext : '';
}
function material(row, id) {
  if (!row || row.id !== id || typeof row.isAvailable !== 'boolean' || !Array.isArray(row.attachments)) invalid();
  return { id, title: text(row.title), available: row.isAvailable, text: row.isAvailable ? text(row.contentText) : '',
    attachments: row.isAvailable ? row.attachments.map((a) => { if (!isGuid(a.id) || !Number.isSafeInteger(a.size) || a.size < 0) invalid();
      return { id: a.id, name: text(a.fileName), size: a.size, type: attachmentType(a) }; }) : [] };
}
function materialPage(row, index) {
  const size = 5999; // Leave room to keep a surrogate pair on the same page.
  const pages = Math.max(1, Math.ceil(row.text.length / size));
  if (!Number.isInteger(index) || index < 0 || index >= pages) throw Object.assign(new Error('INVALID_PATH'), { code: 'INVALID_PATH' });
  let start = index * size, end = Math.min(row.text.length, (index + 1) * size);
  const low = (at) => row.text.charCodeAt(at) >= 0xdc00 && row.text.charCodeAt(at) <= 0xdfff;
  if (start > 0 && low(start)) start--;
  if (end < row.text.length && low(end)) end--;
  return { ...row, text: row.text.slice(start, end), page: index + 1, pages };
}
module.exports = { allowed, task, event, material, materialPage, attachmentType, difficulties };
