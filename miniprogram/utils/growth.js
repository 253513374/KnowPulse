const { isGuid, time } = require('./assessment');
const { fraction, states, reasons } = require('./analysis');
const error = (code = 'INVALID_RESPONSE') => Object.assign(new Error(code), { code });
const allowed = (s, school) => !!(isGuid(school) && s.authenticated && s.context && s.context.grantedPermissions.includes('KnowPulse.Education.AssessmentParticipate') && s.context.workspaces.some((w) => w.schoolId === school && w.role === 2));
const count = (n) => { if (!Number.isSafeInteger(n) || n < 0 || n > 2147483647) throw error(); return n; };
const text = (s) => { if (typeof s !== 'string' || s.length > 8192) throw error(); return s; };
const date = (d) => typeof d === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(d) && d.slice(0, 4) !== '0000' && Number.isFinite(Date.parse(d + 'T00:00:00Z')) && new Date(d + 'T00:00:00Z').toISOString().slice(0, 10) === d;
const instant = (s) => typeof s === 'string' && /Z$/.test(s) && Number.isFinite(Date.parse(s));
const utc = (s) => new Date(s).toISOString().slice(0, 19).replace('T', ' ') + ' UTC';
function window(from, to, maximum, now = Date.now()) {
  const today = new Date(now).toISOString().slice(0, 10);
  if (!date(from) || !date(to) || from > to || to > today || !Number.isInteger(maximum) || maximum < 1 || maximum > 730) throw error('GROWTH_WINDOW_INVALID');
  const start = Date.parse(from + 'T00:00:00Z'), end = to === today ? now : Date.parse(to + 'T00:00:00Z') + 86400000;
  if (start >= end || end - start > maximum * 86400000) throw error('GROWTH_WINDOW_INVALID');
  return { startsAtUtc: new Date(start).toISOString(), endsAtUtc: new Date(end).toISOString() };
}
function fixedWindow(from, to, now = Date.now()) {
  if (!instant(from) || !instant(to) || Date.parse(from) >= Date.parse(to) || Date.parse(to) > now || Date.parse(to) - Date.parse(from) > 730 * 86400000) throw error('GROWTH_WINDOW_INVALID');
  return { startsAtUtc: from, endsAtUtc: to };
}
function context(r) {
  if (!r || !isGuid(r.id) || !Number.isInteger(r.maximumWindowDays) || r.maximumWindowDays < 1 || r.maximumWindowDays > 730) throw error();
  return { id: r.id, school: text(r.schoolName), className: text(r.className), term: text(r.termName), teacher: text(r.teacherName), subject: text(r.subjectName), book: text(r.textbookTitle), maximumWindowDays: r.maximumWindowDays };
}
function contexts(dto, school) {
  if (!dto || dto.schoolId !== school || !Array.isArray(dto.items) || dto.items.length > 500) throw error();
  const rows = dto.items.map(context); if (new Set(rows.map((r) => r.id)).size !== rows.length) throw error(); return rows;
}
const comparisons = [[1, '首个记录，尚无前次可比'], [2, '知识点覆盖不同'], [4, '满分不同'], [8, '名义难度构成不同'], [16, '冻结评分口径不同'], [32, '存在缺失成绩，不能直接比较'], [64, '纳入或完整评分的学生范围不同'], [128, '缺少完整冻结题目元数据，不能直接比较']];
const comparison = (limit) => limit === 0 ? '与紧邻前次口径可比；分数变化不等于教学干预效果' : comparisons.filter(([bit]) => (limit & bit) !== 0).map(([, label]) => label).join('；');
function assessment(r) {
  if (!r || ![r.taskId, r.recipientId, r.resultReleaseId].every(isGuid) || ![1, 2].includes(r.visibility) || typeof r.isAbsent !== 'boolean' || !instant(r.effectiveAtUtc) || !Number.isInteger(r.comparisonLimit) || r.comparisonLimit < 0 || r.comparisonLimit > 63 ||
      (r.isAbsent ? r.totalScore != null || r.maxScore != null : !Number.isFinite(r.totalScore) || !Number.isFinite(r.maxScore) || r.totalScore < 0 || r.maxScore <= 0 || r.totalScore > r.maxScore)) throw error();
  return { id: r.taskId, recipientId: r.recipientId, releaseId: r.resultReleaseId, title: text(r.title), at: time(r.effectiveAtUtc), effectiveAtUtc: r.effectiveAtUtc,
    absent: r.isAbsent, obtained: r.isAbsent ? '—' : String(r.totalScore), max: r.isAbsent ? '—' : String(r.maxScore), percent: r.isAbsent ? null : Number((r.totalScore / r.maxScore * 100).toFixed(2)),
    visibility: r.visibility, comparison: comparison(r.comparisonLimit) };
}
function point(r, missingRule) {
  if (!r || !isGuid(r.id) || count(r.revisionNumber) < 1 || ![0, 1, 2, 3].includes(r.state) || missingRule && r.state > 1 || typeof r.recentEvidenceMissing !== 'boolean' || r.latestEvidenceAtUtc != null && !instant(r.latestEvidenceAtUtc) ||
      (r.maxScore === 0) !== (r.state === 0)) throw error();
  const row = { id: r.id, revision: r.revisionNumber, code: text(r.code), name: text(r.name), state: r.state, label: states[r.state], ...fraction(r.obtainedScore, r.maxScore, r.ratio), latest: time(r.latestEvidenceAtUtc) };
  for (const key of ['independentQuestionCount', 'assessmentCount', 'unverifiedQuestionCount', 'practiceQuestionCount', 'invalidQuestionCount', 'missingIndependentQuestionCount', 'missingAssessmentCount']) row[key] = count(r[key]);
  if (r.maxScore === 0 && (row.independentQuestionCount || row.assessmentCount) || r.maxScore > 0 && (!row.independentQuestionCount || !row.assessmentCount)) throw error();
  return { ...row, recentMissing: r.recentEvidenceMissing };
}
function project(dto, school, id, requested, expected) {
  const r = dto && dto.value;
  if (!dto || dto.schoolId !== school || !r || !isGuid(r.snapshotId) || context(r.context).id !== id || Date.parse(r.windowStartsAtUtc) !== Date.parse(requested.startsAtUtc) || Date.parse(r.windowEndsAtUtc) !== Date.parse(requested.endsAtUtc) || !instant(r.calculatedAtUtc) || typeof r.ruleMissing !== 'boolean' ||
      !Array.isArray(r.assessments) || r.assessments.length > 500 || !Array.isArray(r.points) || r.points.length > 3000 || r.ruleMissing !== (r.rule == null)) throw error();
  if (expected && r.snapshotId !== expected) throw error('GROWTH_CHANGED');
  const ctx = context(r.context); let rule = null;
  if (r.rule) { const v = r.rule;
    if (count(v.revisionNumber) < 1 || !Number.isFinite(v.targetRatio) || v.targetRatio <= 0 || v.targetRatio > 1 || count(v.windowDays) < 1 || v.windowDays > 730 || count(v.minimumIndependentQuestionCount) < 1 || count(v.minimumAssessmentCount) < 1 || count(v.maximumLatestEvidenceAgeDays) < 1) throw error();
    rule = { revision: v.revisionNumber, days: v.windowDays, questions: v.minimumIndependentQuestionCount, assessments: v.minimumAssessmentCount, age: v.maximumLatestEvidenceAgeDays, target: (v.targetRatio * 100).toFixed(2) + '%' };
  }
  const tasks = r.assessments.map(assessment), points = r.points.map((p) => point(p, r.ruleMissing));
  if (new Set(tasks.map((t) => t.id)).size !== tasks.length || new Set(points.map((p) => p.id)).size !== points.length || tasks.some((t) => Date.parse(t.effectiveAtUtc) < Date.parse(requested.startsAtUtc) || Date.parse(t.effectiveAtUtc) >= Date.parse(requested.endsAtUtc))) throw error();
  return { snapshotId: r.snapshotId, context: ctx, calculated: time(r.calculatedAtUtc), calculationVersion: text(r.calculationVersion), rule, decision: r.ruleDecisionRevisionNumber == null ? null : count(r.ruleDecisionRevisionNumber),
    warning: r.ruleMissing ? '未配置有效学科规则：有比例也只能说明当前证据，不自行判断达标。' : '这是有效得分证据比例，不是能力概率或永久掌握结论。',
    from: utc(r.windowStartsAtUtc), to: utc(r.windowEndsAtUtc), points, assessments: tasks };
}
function evidence(dto, school, report) {
  const r = dto && dto.evidence;
  if (!dto || dto.schoolId !== school || !r || !Array.isArray(r.items) || r.items.length > 20 || count(r.totalCount) < r.items.length || typeof r.hasRestrictedEvidence !== 'boolean') throw error();
  const rows = r.items.map((v) => {
    const task = report.assessments.find((t) => t.id === v.taskId);
    if (!task || task.visibility !== 2 || task.recipientId !== v.recipientId || task.releaseId !== v.resultReleaseId || !isGuid(v.paperQuestionId) || !instant(v.effectiveAtUtc) || !Number.isFinite(v.obtainedScore) || !Number.isFinite(v.maxScore) || v.obtainedScore < 0 || v.maxScore <= 0 || v.obtainedScore > v.maxScore || !Number.isInteger(v.exclusionReason) || !reasons[v.exclusionReason]) throw error('GROWTH_CHANGED');
    return { id: task.id + ':' + v.paperQuestionId, recipientId: task.recipientId, releaseId: task.releaseId, questionId: v.paperQuestionId, title: task.title, at: time(v.effectiveAtUtc), obtained: String(v.obtainedScore), max: String(v.maxScore), reason: reasons[v.exclusionReason] };
  });
  if (new Set(rows.map((v) => v.id)).size !== rows.length) throw error();
  return { items: rows, total: r.totalCount, restricted: r.hasRestrictedEvidence };
}
async function read(session, school, id, requested, expected) {
  const dto = await session.request(`/api/app/education-student-growth/my-growth-for-school?contextId=${id}&schoolId=${school}&WindowStartsAtUtc=${encodeURIComponent(requested.startsAtUtc)}&WindowEndsAtUtc=${encodeURIComponent(requested.endsAtUtc)}`);
  return project(dto, school, id, requested, expected);
}
const query = (window) => `from=${encodeURIComponent(window.startsAtUtc)}&to=${encodeURIComponent(window.endsAtUtc)}`;
const resultUrl = (row, school) => `/pages/assessment-result/index?id=${row.recipientId}&schoolId=${school}&releaseId=${row.releaseId}${row.questionId ? '&questionId=' + row.questionId : ''}`;
module.exports = { allowed, error, window, fixedWindow, contexts, context, project, evidence, read, query, resultUrl, comparison };
