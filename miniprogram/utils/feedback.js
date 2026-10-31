const { isGuid, time } = require('./assessment');
const invalid = () => { throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' }); };
const text = (value) => typeof value === 'string' ? value : '';
const visibility = ['草稿', '等待完整讲评', '已开放', '已撤回'];
const kinds = ['日常评语', '测评评语', '阶段评语'];
const allowed = (state, mode, schoolId) => ['teacher', 'student'].includes(mode) && state.authenticated && state.context && isGuid(schoolId) &&
  state.context.grantedPermissions.includes('KnowPulse.Education.' + (mode === 'teacher' ? 'FeedbackManage' : 'FeedbackView')) &&
  state.context.workspaces.some((w) => w.schoolId === schoolId && w.role === (mode === 'teacher' ? 1 : 2));
const actor = (state) => state.authenticated && state.context && isGuid(state.context.accountUserId) ?
  `${state.context.tenantId || 'host'}:${state.context.accountUserId}` : null;
const query = (target) => `TeachingAssignmentId=${encodeURIComponent(target.teachingAssignmentId)}&StudentEnrollmentId=${encodeURIComponent(target.studentEnrollmentId)}&SchoolMembershipId=${encodeURIComponent(target.schoolMembershipId)}`;
const teachingAllowed = (s, school, teaching) => allowed(s, 'teacher', school) && isGuid(teaching) && s.context.workspaces.some((w) => w.schoolId === school && w.role === 1 && w.classes.some((c) => c.relationId === teaching));
const phraseScope = (school, teaching) => `SchoolId=${school}&TeachingAssignmentId=${teaching}`;
function observation(row, school, target) {
  if (!row || !isGuid(row.id) || row.schoolId !== school || row.teachingAssignmentId !== target.teachingAssignmentId ||
      row.studentEnrollmentId !== target.studentEnrollmentId || row.schoolMembershipId !== target.schoolMembershipId || !text(row.text).trim() || row.text.length > 4000 || !Number.isFinite(Date.parse(row.observedAtUtc))) invalid();
  return { id: row.id, text: row.text, at: time(row.observedAtUtc), observedAtUtc: row.observedAtUtc };
}
function phrase(row, expectedId) {
  const r = row && row.currentRevision;
  if (!row || !isGuid(row.id) || expectedId && row.id !== expectedId || !text(row.concurrencyStamp) || !r || r.phraseId !== row.id || !isGuid(r.id) || !Number.isSafeInteger(r.revisionNumber) || r.revisionNumber < 1 ||
      !text(r.text).trim() || r.text.length > 4000 || typeof r.isArchived !== 'boolean' || !Number.isFinite(Date.parse(r.recordedAtUtc))) invalid();
  return { id: row.id, stamp: row.concurrencyStamp, revisionId: r.id, version: r.revisionNumber, text: r.text, archived: r.isArchived, at: time(r.recordedAtUtc) };
}
function observationContent(text, date, clock) {
  const at = new Date(date + 'T' + clock + ':00');
  if (typeof text !== 'string' || !text.trim() || text.length > 4000 || !Number.isFinite(at.getTime()) || localParts(at.toISOString()).date !== date || localParts(at.toISOString()).time !== clock)
    throw Object.assign(new Error('INVALID_FEEDBACK_FORM'), { code: 'INVALID_FEEDBACK_FORM' });
  return { text, observedAtUtc: at.toISOString() };
}

function teachingList(rows, state, schoolId) {
  if (!Array.isArray(rows)) invalid();
  const relations = state.context.workspaces.filter((w) => w.schoolId === schoolId && w.role === 1).reduce((all, w) => all.concat(w.classes.map((c) => c.relationId)), []);
  return rows.filter((row) => relations.includes(row.id)).map((row) => ({ id: row.id, label: `${text(row.schoolName)} · ${text(row.className)} · ${text(row.subjectName)}` }));
}
function page(dto, project) {
  if (!dto || !Array.isArray(dto.items) || !Number.isSafeInteger(dto.totalCount) || dto.totalCount < 0) invalid();
  return { items: dto.items.map(project), total: dto.totalCount };
}
function teacherCard(row) {
  if (!isGuid(row.id) || ![0, 1, 2, 3].includes(row.visibility) || !row.currentRevision) invalid();
  return { id: row.id, name: text(row.studentName), accountName: text(row.accountName), className: text(row.className), subjectName: text(row.subjectName),
    version: row.currentRevision.revisionNumber, status: visibility[row.visibility], hasDraft: !!row.draft, opened: !!row.publishedVersionOpenedAtUtc };
}
function studentCard(row, schoolId) {
  if (!isGuid(row.id) || !isGuid(row.versionId) || row.schoolId !== schoolId || ![2, 3].includes(row.visibility)) invalid();
  return { id: row.id, versionId: row.versionId, version: row.versionNumber, teacherName: text(row.teacherName), schoolName: text(row.schoolName), className: text(row.className), subjectName: text(row.subjectName),
    status: visibility[row.visibility], recorded: time(row.recordedAtUtc), opened: !!row.openedAtUtc };
}
function studentDetail(row, id, versionId, schoolId) {
  if (!row || row.id !== id || row.versionId !== versionId) invalid();
  const card = studentCard(row, schoolId);
  return { ...card, withdrawn: row.visibility === 3, text: row.visibility === 2 ? text(row.text) : '', context: row.visibility === 2 ? text(row.context) : '',
    occurred: row.visibility === 2 ? time(row.occurredAtUtc) : '', periodEnds: row.visibility === 2 ? time(row.periodEndsAtUtc) : '',
    assessments: row.visibility === 2 ? (row.assessmentRecipientIds || []).filter(isGuid) : [] };
}
function targetContext(value, schoolId, expected) {
  if (!value || value.schoolId !== schoolId || !value.teaching || !value.student || value.teaching.id !== expected.teachingAssignmentId ||
      value.student.studentEnrollmentId !== expected.studentEnrollmentId || value.student.schoolMembershipId !== expected.schoolMembershipId) invalid();
  return value;
}
function localParts(value = new Date().toISOString()) {
  const date = new Date(value); if (!Number.isFinite(date.getTime())) invalid();
  const pad = (n) => String(n).padStart(2, '0');
  return { date: `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`, time: `${pad(date.getHours())}:${pad(date.getMinutes())}` };
}
function content(form) {
  const occurred = new Date(form.date + 'T' + form.time + ':00');
  const ends = form.kind === 2 ? new Date(form.endDate + 'T' + form.endTime + ':00') : null;
  if (![0, 1, 2].includes(form.kind) || !text(form.context).trim() || form.context.length > 256 || !text(form.text).trim() || form.text.length > 4000 ||
      !Number.isFinite(occurred.getTime()) || (ends && (!Number.isFinite(ends.getTime()) || ends <= occurred)) ||
      !Array.isArray(form.assessmentTaskIds) || form.assessmentTaskIds.length > 10 || new Set(form.assessmentTaskIds).size !== form.assessmentTaskIds.length ||
      form.assessmentTaskIds.some((id) => !isGuid(id)) || (form.kind === 1 && form.assessmentTaskIds.length === 0))
    throw Object.assign(new Error('INVALID_FEEDBACK_FORM'), { code: 'INVALID_FEEDBACK_FORM' });
  const unchanged = (raw, date, clock) => raw && localParts(raw).date === date && localParts(raw).time === clock;
  return { kind: form.kind, context: form.context, text: form.text, occurredAtUtc: unchanged(form.originalOccurredAtUtc, form.date, form.time) ? form.originalOccurredAtUtc : occurred.toISOString(),
    periodEndsAtUtc: ends ? unchanged(form.originalPeriodEndsAtUtc, form.endDate, form.endTime) ? form.originalPeriodEndsAtUtc : ends.toISOString() : null,
    assessmentTaskIds: form.assessmentTaskIds.slice(), selectedObservationId: form.selectedObservationId || null };
}
function formFrom(value) {
  const at = localParts(value ? value.occurredAtUtc : undefined); const end = localParts(value && value.periodEndsAtUtc || new Date(Date.now() + 86400000).toISOString());
  return { kind: value ? value.kind : 0, context: value ? value.context : '', text: value ? value.text : '', date: at.date, time: at.time,
    endDate: end.date, endTime: end.time, originalOccurredAtUtc: value ? value.occurredAtUtc : null, originalPeriodEndsAtUtc: value ? value.periodEndsAtUtc : null,
    assessmentTaskIds: value ? value.assessmentTaskIds.slice() : [], selectedObservationId: value ? value.selectedObservationId : null };
}
function uuid(platform) {
  return new Promise((resolve, reject) => {
    if (typeof platform.getRandomValues !== 'function') { reject(Object.assign(new Error('RANDOM_REQUIRED'), { code: 'RANDOM_REQUIRED' })); return; }
    platform.getRandomValues({ length: 16, success(value) {
      const bytes = new Uint8Array(value.randomValues);
      if (bytes.length !== 16) { reject(Object.assign(new Error('RANDOM_REQUIRED'), { code: 'RANDOM_REQUIRED' })); return; }
      bytes[6] = (bytes[6] & 15) | 64; bytes[8] = (bytes[8] & 63) | 128;
      const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
      resolve(`${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`);
    }, fail() { reject(Object.assign(new Error('RANDOM_REQUIRED'), { code: 'RANDOM_REQUIRED' })); } });
  });
}
module.exports = { allowed, teachingAllowed, actor, query, phraseScope, observation, observationContent, phrase, teachingList, page, teacherCard, studentCard, studentDetail, targetContext, content, formFrom, localParts, uuid, kinds, visibility };
