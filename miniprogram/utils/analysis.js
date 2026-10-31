const { isGuid, time } = require('./assessment');
const error = (code = 'INVALID_RESPONSE') => Object.assign(new Error(code), { code });
const text = (v) => typeof v === 'string' ? v : '';
const count = (v) => { if (!Number.isSafeInteger(v) || v < 0 || v > 2147483647) throw error(); return v; };
const date = (v) => typeof v === 'string' && Number.isFinite(Date.parse(v));
const states = ['未测评', '证据不足', '需要巩固', '达到当前目标'];
const assessmentStates = ['尚未开始', '正在作答', '等待封存', '等待完整确认评分', '已有完整确认评分', '缺考', '提交不在统计窗口'];
const reasons = ['计入独立证据', '证据已无效', '证据尚未核验', '作为练习', '同题或已有作答', '作答前已开放答案/提示', '不在统计窗口', '提示上下文待核验'];
function allowed(s, school, assignment) {
  return isGuid(school) && s.authenticated && s.context && s.context.grantedPermissions.includes('KnowPulse.Education.AnalysisManage') &&
    s.context.workspaces.some((w) => w.schoolId === school && w.role === 1 && (assignment == null || isGuid(assignment) && w.classes.some((c) => c.relationId === assignment)));
}
function assignments(s, school) {
  if (!allowed(s, school)) return []; const seen = new Set();
  return s.context.workspaces.filter((w) => w.schoolId === school && w.role === 1).flatMap((w) => w.classes).map((c) => {
    if (!isGuid(c.relationId) || seen.has(c.relationId)) throw error(); seen.add(c.relationId);
    return { id: c.relationId, label: `${text(c.className)} · ${text(c.subjectName)} · ${text(c.academicTermName)}` };
  });
}
function fraction(obtained, max, ratio) {
  if (!Number.isFinite(obtained) || !Number.isFinite(max) || obtained < 0 || max < obtained ||
      max === 0 && ratio !== null || max > 0 && (!Number.isFinite(ratio) || ratio < 0 || ratio > 1 || Math.abs(ratio - obtained / max) > 1e-9)) throw error();
  return { obtained: ratio === null ? '—' : String(obtained), max: ratio === null ? '—' : String(max), ratio: ratio === null ? '暂无有效证据' : `${(ratio * 100).toFixed(2)}%` };
}
function point(p) { if (!p || !isGuid(p.knowledgePointId) || !isGuid(p.knowledgePointRevisionId)) throw error(); return { id: p.knowledgePointRevisionId, code: text(p.code), name: text(p.name) }; }
function list(row, school, assignment) {
  if (!row || row.schoolId !== school || row.teachingAssignmentId !== assignment || ![row.id, row.textbookVersionId].every(isGuid) || !date(row.windowStartsAtUtc) || !date(row.windowEndsAtUtc) || Date.parse(row.windowStartsAtUtc) >= Date.parse(row.windowEndsAtUtc) || count(row.generation) < 1) throw error();
  return { id: row.id, schoolId: school, assignmentId: assignment, textbookId: row.textbookVersionId, generation: row.generation, windowStartsAtUtc: row.windowStartsAtUtc, windowEndsAtUtc: row.windowEndsAtUtc, from: time(row.windowStartsAtUtc), to: time(row.windowEndsAtUtc) };
}
const coverageNames = ['assignedCount', 'scoredCount', 'pendingGradingCount', 'absentCount', 'notStartedCount', 'inProgressCount', 'awaitingSealCount', 'outsideWindowCount'];
function coverage(row) {
  if (!row) throw error(); const c = Object.fromEntries(coverageNames.map((k) => [k, count(row[k])]));
  if (coverageNames.slice(1).reduce((sum, k) => sum + c[k], 0) !== c.assignedCount) throw error(); return c;
}
function head(dto, school, assignment, id) {
  const base = list(dto, school, assignment); if (base.id !== id || typeof dto.sourceIsCurrent !== 'boolean' || typeof dto.scopeNeedsUpdate !== 'boolean') throw error();
  const job = dto.currentJob; if (!job || !isGuid(job.id) || job.reportId !== id || ![0, 1, 2, 3, 4, 5].includes(job.status) || ![0, 1, 2, 3].includes(job.failureStage) || count(job.generation) < 1 || count(job.completedStudentCount) > count(job.studentCount)) throw error();
  const result = { ...base, current: false, snapshotId: null, snapshotGeneration: null, className: '', subject: '', book: '', points: [], assessments: [], coverage: null, rule: null,
    calculated: '—', job: { status: job.status, label: ['等待计算', '正在计算', '计算完成', '计算失败', '已被更新任务替代', '来源已变化'][job.status], generation: job.generation, completed: job.completedStudentCount,
      students: job.studentCount, heartbeat: time(job.heartbeatAtUtc), stage: ['—', '计算', '来源核验', '保存'][job.failureStage], capacity: !!job.capacityExceeded },
    warning: dto.scopeNeedsUpdate ? '教材范围或规则需重新核对，请在网页调整并重算。' : '当前来源尚未形成可展示快照；旧结果不作为当前结论。' };
  if (!dto.sourceIsCurrent) return result;
  const s = dto.summary; if (!s || !isGuid(dto.currentSnapshotId) || count(dto.snapshotGeneration) < 1 || !date(dto.calculatedAtUtc) || !Array.isArray(s.students) || s.students.length || !Array.isArray(s.points) || !Array.isArray(s.assessments)) throw error();
  const c = coverage(dto.coverage); c.students = count(dto.coverage.studentCount); c.scoredStudents = count(dto.coverage.scoredStudentCount); if (c.scoredStudents > c.students || c.scoredStudents > c.scoredCount) throw error();
  const points = s.points.map((p) => { const counts = [p.unassessedCount, p.insufficientEvidenceCount, p.needsConsolidationCount, p.currentTargetMetCount].map(count); if (counts.reduce((n, v) => n + v, 0) !== c.students) throw error(); return { ...point(p.point), ...fraction(p.obtainedScore, p.maxScore, p.ratio), counts }; });
  if (new Set(points.map((p) => p.id)).size !== points.length) throw error();
  const tasks = s.assessments.map((t) => { if (![t.taskId, t.taskRevisionId].every(isGuid) || count(t.visibleAssignedCount) > count(t.assignedCount)) throw error(); return { id: t.taskId, title: text(t.title), assigned: t.assignedCount, visible: t.visibleAssignedCount, starts: time(t.startsAtUtc), ends: time(t.endsAtUtc), endsAtUtc: t.endsAtUtc }; });
  if (new Set(tasks.map((t) => t.id)).size !== tasks.length) throw error();
  let rule = null;
  if (!s.ruleMissing) { const r = dto.rule; if (!r || !isGuid(r.id) || r.schoolId !== school || r.id !== s.ruleRevisionId || count(r.revisionNumber) < 1 || !Number.isFinite(r.targetRatio) || r.targetRatio <= 0 || r.targetRatio > 1) throw error();
    rule = { id: r.id, version: r.revisionNumber, windowDays: count(r.windowDays), questions: count(r.minimumIndependentQuestionCount), assessments: count(r.minimumAssessmentCount), age: count(r.maximumLatestEvidenceAgeDays), target: `${(r.targetRatio * 100).toFixed(2)}%` }; }
  return { ...result, current: true, snapshotId: dto.currentSnapshotId, snapshotGeneration: dto.snapshotGeneration, calculated: time(dto.calculatedAtUtc), calculatedAtUtc: dto.calculatedAtUtc, calculationVersion: text(s.calculationVersion),
    className: text(s.schoolClassName), subject: text(s.subjectName), book: text(s.textbookTitle), points, assessments: tasks, coverage: c, rule,
    warning: s.ruleMissing ? '未配置有效学科规则：有比例也只能标为证据不足，不能自行判断达标。' : '这是有效得分证据比例，不是能力概率；当前规则仍需学校教学样本校准。' };
}
function person(s) { if (!s || !isGuid(s.studentUserId)) throw error(); return { id: s.studentUserId, name: text(s.studentName), account: text(s.accountName) }; }
function studentCard(row) {
  return { ...person(row.student), ...coverage(row), assessed: count(row.assessedPointCount), targets: count(row.targetPointCount), needs: count(row.needsConsolidationCount), insufficient: count(row.insufficientEvidenceCount) };
}
function student(dto, expected, report) {
  const p = person(dto && dto.student), a = dto && dto.analysis;
  if (p.id !== expected || !a || !Array.isArray(a.points) || !Array.isArray(dto.assessments) || a.calculationVersion !== report.calculationVersion ||
      Date.parse(a.windowStartsAtUtc) !== Date.parse(report.windowStartsAtUtc) || Date.parse(a.windowEndsAtUtc) !== Date.parse(report.windowEndsAtUtc) ||
      Date.parse(a.calculatedAtUtc) !== Date.parse(report.calculatedAtUtc) || (a.ruleRevisionId || null) !== (report.rule?.id || null)) throw error();
  const points = a.points.map((v) => {
    if (![0, 1, 2, 3].includes(v.state) || typeof v.ruleMissing !== 'boolean' || v.ruleMissing !== !report.rule || v.ruleMissing && [2, 3].includes(v.state)) throw error();
    return { ...point(v.point), ...fraction(v.obtainedScore, v.maxScore, v.ratio), state: v.state, label: states[v.state], independent: count(v.independentQuestionCount), assessments: count(v.assessmentCount),
      latest: time(v.latestEvidenceAtUtc), unverified: count(v.unverifiedQuestionCount), practice: count(v.practiceQuestionCount), invalid: count(v.invalidQuestionCount),
      ruleMissing: !!v.ruleMissing, missingQuestions: count(v.missingIndependentQuestionCount), missingAssessments: count(v.missingAssessmentCount), recentMissing: !!v.recentEvidenceMissing };
  });
  if (new Set(points.map((v) => v.id)).size !== points.length || points.length !== count(a.targetPointCount)) throw error();
  const tasks = dto.assessments.map((v) => {
    const task = report.assessments.find((t) => t.id === v.taskId); if (!task || !isGuid(v.recipientId) || ![0, 1, 2, 3, 4, 5, 6].includes(v.state) || count(v.confirmedQuestionCount) > count(v.questionCount)) throw error();
    if (v.totalScore != null && (v.state !== 4 || !Number.isFinite(v.totalScore) || !Number.isFinite(v.maxScore) || v.totalScore < 0 || v.totalScore > v.maxScore)) throw error();
    return { id: v.taskId, title: task.title, state: v.state, label: assessmentStates[v.state], questions: v.questionCount, confirmed: v.confirmedQuestionCount,
      score: v.totalScore == null ? '尚无完整确认分数' : `${v.totalScore} / ${v.maxScore}`, at: time(v.effectiveAtUtc) };
  });
  return { ...p, assessed: count(a.assessedPointCount), targets: a.targetPointCount, calculated: time(a.calculatedAtUtc), calculationVersion: text(a.calculationVersion), points, assessments: tasks };
}
function evidence(row) {
  if (![row.questionGradeId, row.gradeRevisionId, row.taskId, row.submissionId, row.paperQuestionId].every(isGuid) || ![0, 1, 2, 3, 4, 5, 6, 7].includes(row.exclusionReason) || !Number.isFinite(row.maxScore) || !Number.isFinite(row.obtainedScore) || row.maxScore < row.obtainedScore || row.obtainedScore < 0) throw error();
  return { id: row.gradeRevisionId, gradeId: row.questionGradeId, gradeVersion: row.gradeRevisionId, taskId: row.taskId, at: time(row.effectiveAtUtc), max: String(row.maxScore), obtained: String(row.obtainedScore), reason: reasons[row.exclusionReason] };
}
async function read(session, school, assignment, id, snapshot) {
  if (![school, assignment, id].every(isGuid) || snapshot != null && !isGuid(snapshot)) throw error();
  const dto = await session.request(`/api/app/education-learning-report/for-school?reportId=${id}&schoolId=${school}&teachingAssignmentId=${assignment}`), report = head(dto, school, assignment, id);
  if (snapshot && (!report.current || report.snapshotId !== snapshot)) throw error('REPORT_VERSION_CHANGED'); return report;
}
const page = (row) => { if (!row || !Number.isSafeInteger(row.totalCount) || row.totalCount < 0 || !Array.isArray(row.items) || row.items.length > 20) throw error(); return row; };
module.exports = { allowed, assignments, list, head, studentCard, student, evidence, read, page, error, fraction, states, reasons };
