const { isGuid, time } = require('./assessment');
const permissions = ['KnowPulse.Education.AssessmentParticipate', 'KnowPulse.Education.FeedbackView', 'KnowPulse.Education.TeachingPlanView'];
const labels = ['测评消息', '评语消息', '教学方案消息'];
const text = ['老师提醒你查看测评任务', '你有新的测评任务，请查看安排', '测评结果有更新，请核对获准范围', '老师给你的评语有更新', '一条评语已撤回', '老师下发了新的教学方案，请查看安排', '一份教学方案已撤回', '教学方案执行状态有更新，请查看当前安排', '老师已处理你提交的学习困难，请查看对应活动'];
const invalid = () => { throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' }); };
const allowed = (s, school, source) => [0, 1, 2].includes(source) && isGuid(school) && s.authenticated && s.context && s.context.grantedPermissions.includes(permissions[source]) &&
  s.context.workspaces.some((w) => w.role === 2 && w.schoolId === school && w.classes.length > 0);
const enabled = (s, school) => [0, 1, 2].some((source) => allowed(s, school, source));
function sources(raw, state, school) {
  if (!Array.isArray(raw) || raw.length > 3 || new Set(raw).size !== raw.length || raw.some((source) => !allowed(state, school, source))) invalid();
  return raw.map((id) => ({ id, label: labels[id] }));
}
function message(row, school, source) {
  if (!row || row.schoolId !== school || row.source !== source || ![row.id, row.referenceId, row.publicationId].every(isGuid) ||
      !(source === 0 ? [0, 1, 2] : source === 1 ? [3, 4] : source === 2 ? [5, 6, 7, 8] : []).includes(row.kind) || typeof row.canOpen !== 'boolean' ||
      (row.kind === 2 ? !isGuid(row.resultReleaseId) : row.resultReleaseId != null) || (row.kind === 8 ? !isGuid(row.stepId) : row.stepId != null) || [4, 6].includes(row.kind) && row.canOpen ||
      source === 1 && row.id !== row.publicationId || !Number.isFinite(Date.parse(row.availableAtUtc)) ||
      row.readAtUtc != null && (!Number.isFinite(Date.parse(row.readAtUtc)) || Date.parse(row.readAtUtc) < Date.parse(row.availableAtUtc)) || row.weChatDelivery !== 'NotConfigured') invalid();
  return { id: row.id, schoolId: school, source, kind: row.kind, referenceId: row.referenceId, publicationId: row.publicationId, resultReleaseId: row.resultReleaseId || null,
    stepId: row.stepId || null, canOpen: row.canOpen, message: text[row.kind], at: time(row.availableAtUtc), read: row.readAtUtc ? time(row.readAtUtc) : '未读', isRead: row.readAtUtc != null };
}
function page(dto, school, source) {
  if (!dto || !Number.isSafeInteger(dto.totalCount) || dto.totalCount < 0 || !Array.isArray(dto.items) || dto.items.length > 20) invalid();
  return { total: dto.totalCount, items: dto.items.map((row) => message(row, school, source)) };
}
function destination(row) {
  if (!row.canOpen) return null;
  if (row.source === 2) return `/pages/teaching-plan/index?schoolId=${row.schoolId}&id=${row.referenceId}&versionId=${row.publicationId}` + (row.stepId ? `&stepId=${row.stepId}` : '');
  if (row.source === 1) return `/pages/feedback-view/index?schoolId=${row.schoolId}&id=${row.referenceId}&versionId=${row.publicationId}`;
  return row.kind === 2 ? `/pages/assessment-result/index?schoolId=${row.schoolId}&id=${row.referenceId}&taskRevisionId=${row.publicationId}&releaseId=${row.resultReleaseId}`
    : `/pages/assessment-answer/index?schoolId=${row.schoolId}&id=${row.referenceId}`;
}
module.exports = { allowed, enabled, sources, message, page, destination };
