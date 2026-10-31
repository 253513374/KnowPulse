const analysis = require('./analysis');
const plan = require('./plan');
const { isGuid, time } = require('./assessment');
const invalid = () => { throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' }); };
const text = (v) => typeof v === 'string' ? v : '';
const states = ['执行中', '已暂停新执行', '教师已结束本轮安排', '教师已终止本轮安排'];
const allowed = (s, school, assignment) => analysis.allowed(s, school, assignment) && s.context.grantedPermissions.includes('KnowPulse.Education.TeachingPlanManage');
function card(r, school, assignment) {
  if (!r || r.schoolId !== school || r.teachingAssignmentId !== assignment || ![r.id, r.publishedRevisionId].every(isGuid) ||
      !Number.isSafeInteger(r.publishedRevisionNumber) || r.publishedRevisionNumber < 1 || ![0, 1, 2, 3].includes(r.executionState) || typeof r.isCurrentPublication !== 'boolean') invalid();
  return { id: r.id, schoolId: school, assignmentId: assignment, versionId: r.publishedRevisionId, version: r.publishedRevisionNumber,
    student: text(r.studentName), className: text(r.className), subject: text(r.subjectName), status: r.isCurrentPublication ? states[r.executionState] : '已撤回，仅查看当前有权历史' };
}
function followUp(r, activityId) {
  if (!r || !isGuid(r.id) || r.activityId !== activityId || r.action !== 3 || !isGuid(r.replyToStudentRevisionId) ||
      !Number.isSafeInteger(r.revisionNumber) || r.revisionNumber < 1 || typeof r.internalTeacherNote !== 'string' || !r.internalTeacherNote.trim() ||
      r.internalTeacherNote.length > 2000 || !Number.isFinite(Date.parse(r.recordedAtUtc))) invalid();
  return { id: r.id, replyId: r.replyToStudentRevisionId, note: r.internalTeacherNote, at: time(r.recordedAtUtc) };
}
function view(r, school, assignment, id, versionId) {
  if (!r || r.schoolId !== school || r.teachingAssignmentId !== assignment || r.planId !== id || r.publishedRevisionId !== versionId ||
      typeof r.isCurrentPublication !== 'boolean' || ![0, 1, 2, 3].includes(r.executionState) || !Array.isArray(r.versions) || r.versions.length > 1000) invalid();
  const versions = r.versions.map((v) => { if (!isGuid(v.id) || !Number.isSafeInteger(v.revisionNumber) || v.revisionNumber < 1 || !Number.isFinite(Date.parse(v.recordedAtUtc))) invalid(); return { id: v.id, number: v.revisionNumber, label: `第 ${v.revisionNumber} 版 · ${time(v.recordedAtUtc)}` }; });
  if (new Set(versions.map((v) => v.id)).size !== versions.length) invalid();
  const selected = versions.find((v) => v.id === versionId); if (!selected) invalid();
  const base = plan.view({ plan: { id, schoolId: school, versionId, versionNumber: selected.number, availability: 1, executionState: r.executionState, content: r.content }, activities: r.activities }, school, id, versionId);
  const activities = base.activities.map((row) => {
    const source = r.activities.find((a) => a.id === row.id);
    if (!Number.isSafeInteger(source.revisionNumber) || source.revisionNumber < 0 || (source.currentRevisionId != null && !isGuid(source.currentRevisionId)) ||
        (source.revisionNumber === 0) !== (source.currentRevisionId == null) || typeof source.concurrencyStamp !== 'string' || !source.concurrencyStamp || source.concurrencyStamp.length > 40) invalid();
    const follow = source.currentFollowUp ? followUp(source.currentFollowUp, row.id) : null;
    return { id: row.id, order: row.order, title: row.title, instructions: row.instructions, kind: row.kindLabel, state: row.state, stateLabel: row.stateLabel,
      materialsAvailable: row.materialsAvailable, current: row.current, formal: row.assessment, versionId: source.currentRevisionId || null, stamp: source.concurrencyStamp,
      currentStudentId: source.currentStudentRevisionId || null, follow: follow && follow.replyId === row.currentStudentRevisionId ? follow : null,
      canHandle: r.isCurrentPublication && row.current?.action === 2 };
  });
  return { header: { student: text(r.studentName), className: text(r.className), subject: text(r.subjectName), title: base.content.title,
      goal: base.content.goal, version: selected.number, current: r.isCurrentPublication, execution: states[r.executionState],
      complete: activities.filter((a) => a.current?.action === 1).length, blocked: activities.filter((a) => a.state === 2).length,
      sealed: activities.some((a) => a.formal?.requiresPermission) ? '未知' : activities.filter((a) => a.formal && a.formal.sealed !== '—').length }, versions, activities };
}
module.exports = { allowed, assignments: analysis.assignments, page: analysis.page, card, view, followUp };
