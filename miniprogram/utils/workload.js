const { isGuid, time } = require('./assessment');
const { allowed } = require('./study');
const invalid = () => { throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' }); };
const kinds = ['教学方案活动', '材料学习', '订正', '正式测评'];
const states = ['待完成', '尚未开始', '当前受阻', '已截止'];
const count = (n) => Number.isSafeInteger(n) && n >= 0 && n <= 2147483647;
const date = (s) => typeof s === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(s) && Number(s.slice(0, 4)) >= 1 && Number.isFinite(Date.parse(s + 'T00:00:00Z')) && new Date(s + 'T00:00:00Z').toISOString().slice(0, 10) === s;
function window(from, to) {
  if (!date(from) || !date(to) || from >= to || (Date.parse(to + 'T00:00:00Z') - Date.parse(from + 'T00:00:00Z')) / 86400000 > 31)
    throw Object.assign(new Error('INVALID_WORKLOAD_WINDOW'), { code: 'INVALID_WORKLOAD_WINDOW' });
  return { startsAtUtc: from + 'T00:00:00Z', endsAtUtc: to + 'T00:00:00Z' };
}
function project(dto, schoolId, requested) {
  const start = Date.parse(requested.startsAtUtc), end = Date.parse(requested.endsAtUtc);
  if (!dto || dto.schoolId !== schoolId || !isGuid(schoolId) || Date.parse(dto.startsAtUtc) !== start || Date.parse(dto.endsAtUtc) !== end ||
      !Number.isFinite(Date.parse(dto.calculatedAtUtc)) || !count(dto.unestimatedPlanCount) || !Array.isArray(dto.items) || dto.items.length > 3000 || !Array.isArray(dto.days) || !Array.isArray(dto.omittedKinds)) invalid();
  const omitted = [...new Set(dto.omittedKinds)]; if (omitted.some((k) => ![0, 1, 2, 3].includes(k))) invalid();
  if (omitted.includes(0) && dto.unestimatedPlanCount > 0) invalid();
  const keys = new Set();
  const items = dto.items.map((r) => {
    const due = Date.parse(r.dueAtUtc); const key = `${r.kind}:${r.sourceId}:${r.publicationId}:${r.id}`;
    if (!isGuid(r.id) || !isGuid(r.sourceId) || !isGuid(r.publicationId) || ![0, 1, 2, 3].includes(r.kind) || ![0, 1, 2, 3].includes(r.state) ||
        omitted.includes(r.kind) || !count(r.estimatedMinutes) || !Number.isFinite(Date.parse(r.startsAtUtc)) || !Number.isFinite(due) || due < start || due >= end || keys.has(key)) invalid();
    keys.add(key);
    return { key, id: r.id, sourceId: r.sourceId, publicationId: r.publicationId, kind: r.kind, kindLabel: kinds[r.kind], state: r.state, stateLabel: states[r.state],
      subject: typeof r.subjectName === 'string' ? r.subjectName : '', className: typeof r.className === 'string' ? r.className : '',
      starts: time(r.startsAtUtc), due: time(r.dueAtUtc), dueDate: new Date(due).toISOString().slice(0, 10), minutes: r.estimatedMinutes,
      known: r.state === 0 || r.state === 1 };
  });
  const days = new Map(); const daySubjects = new Set();
  for (const d of dto.days) {
    const value = Date.parse(d.dueDateUtc);
    if (!Number.isFinite(value) || value < start || value >= end || value % 86400000 !== 0) invalid();
    const key = new Date(value).toISOString().slice(0, 10); const pair = key + ':' + d.subjectName;
    if (typeof d.subjectName !== 'string' || daySubjects.has(pair) ||
        [d.pendingCount, d.knownMinutes, d.blockedCount, d.overdueCount].some((n) => !count(n)) || d.pendingCount === 0 || d.blockedCount + d.overdueCount > d.pendingCount) invalid();
    daySubjects.add(pair);
    const day = days.get(key) || { date: key, pending: 0, minutes: 0, blocked: 0, overdue: 0 };
    day.pending += d.pendingCount; day.minutes += d.knownMinutes; day.blocked += d.blockedCount; day.overdue += d.overdueCount; days.set(key, day);
  }
  const summaries = [...days.values()].sort((a, b) => a.date.localeCompare(b.date));
  if (summaries.reduce((n, d) => n + d.pending, 0) !== items.length || summaries.reduce((n, d) => n + d.minutes, 0) !== items.filter((r) => r.known).reduce((n, r) => n + r.minutes, 0)) invalid();
  for (const day of summaries) {
    const rows = items.filter((r) => r.dueDate === day.date);
    if (day.pending !== rows.length || day.minutes !== rows.filter((r) => r.known).reduce((n, r) => n + r.minutes, 0) ||
        day.blocked !== rows.filter((r) => r.state === 2).length || day.overdue !== rows.filter((r) => r.state === 3).length) invalid();
  }
  return { items, days: summaries, omitted: omitted.map((k) => kinds[k]).join('、'), unestimated: dto.unestimatedPlanCount,
    knownMinutes: summaries.reduce((n, d) => n + d.minutes, 0), calculatedAt: time(dto.calculatedAtUtc),
    partial: omitted.length > 0 || dto.unestimatedPlanCount > 0 || items.some((r) => !r.known) };
}
function target(item, schoolId, state) {
  if (!item || !allowed(state, schoolId)) return null;
  const permissions = state.context.grantedPermissions;
  if (item.kind === 0 && !permissions.includes('KnowPulse.Education.TeachingPlanView') || [2, 3].includes(item.kind) && !permissions.includes('KnowPulse.Education.AssessmentParticipate')) return null;
  if (![item.id, item.sourceId, item.publicationId].every(isGuid)) return null;
  if (item.kind === 1) return { url: `/pages/study-task/index?schoolId=${schoolId}&id=${item.id}` };
  if (item.kind === 3) return { url: `/pages/assessment-answer/index?schoolId=${schoolId}&id=${item.id}` };
  if (item.kind === 0) return { url: `/pages/teaching-plan/index?schoolId=${schoolId}&id=${item.sourceId}&versionId=${item.publicationId}&stepId=${item.id}` };
  if (item.kind === 2) return { url: `/pages/correction/index?schoolId=${schoolId}&id=${item.id}` };
  return null;
}
module.exports = { allowed, window, project, target };
