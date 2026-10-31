const { isGuid, time } = require('./assessment');
const { error, page } = require('./analysis');
const { comparison } = require('./growth');
const count = (n) => { if (!Number.isSafeInteger(n) || n < 0 || n > 2147483647) throw error(); return n; };
const text = (s) => { if (typeof s !== 'string' || s.length > 8192) throw error(); return s; };
function project(dto, report, studentId) {
  if (!dto || dto.reportId !== report.id || dto.snapshotId !== report.snapshotId || dto.schoolId !== report.schoolId || dto.teachingAssignmentId !== report.assignmentId ||
      Date.parse(dto.windowStartsAtUtc) !== Date.parse(report.windowStartsAtUtc) || Date.parse(dto.windowEndsAtUtc) !== Date.parse(report.windowEndsAtUtc) || Date.parse(dto.calculatedAtUtc) !== Date.parse(report.calculatedAtUtc) ||
      dto.comparisonVersion !== 'frozen-paper-observed-score-v1' || (studentId ? dto.student?.id !== studentId : dto.student != null)) throw error('REPORT_VERSION_CHANGED');
  page(dto); if (dto.totalCount < dto.items.length || dto.totalCount > report.assessments.length || !studentId && dto.totalCount !== report.assessments.length) throw error();
  const rows = dto.items.map((r) => {
    const task = report.assessments.find((t) => t.id === r.taskId);
    if (!task || r.title !== task.title || !Number.isFinite(Date.parse(r.effectiveAtUtc)) || Date.parse(r.endsAtUtc) !== Date.parse(task.endsAtUtc) || count(r.paperSnapshotVersion) < 1 || count(r.questionCount) < 1 || count(r.originalAssignedCount) !== task.assigned || count(r.assignedCount) !== (studentId ? 1 : task.visible) ||
        !Number.isFinite(r.maxScore) || r.maxScore <= 0 || !Number.isInteger(r.comparisonLimit) || r.comparisonLimit < 0 || r.comparisonLimit > 255 ||
        (r.comparedWithTaskId == null ? !(r.comparisonLimit & 1) : (r.comparisonLimit & 1) || !report.assessments.some((t) => t.id === r.comparedWithTaskId) || r.comparedWithTaskId === r.taskId)) throw error();
    const counts = {}; for (const key of ['scoredCount', 'pendingGradingCount', 'absentCount', 'notStartedCount', 'inProgressCount', 'awaitingSealCount', 'outsideWindowCount']) counts[key] = count(r[key]);
    if (Object.values(counts).reduce((a, b) => a + b, 0) !== r.assignedCount || (r.scoredCount === 0 ? r.score != null || r.ratio != null : !Number.isFinite(r.score) || r.score < 0 || r.score > r.maxScore || !Number.isFinite(r.ratio) || Math.abs(r.ratio - r.score / r.maxScore) > 1e-9)) throw error();
    return { id: r.taskId, title: task.title, at: time(r.effectiveAtUtc), version: r.paperSnapshotVersion, questions: r.questionCount, original: r.originalAssignedCount, included: r.assignedCount, ...counts,
      score: r.score == null ? '尚无完整确认分数' : String(r.score), max: String(r.maxScore), percent: r.ratio == null ? null : Number((r.ratio * 100).toFixed(2)), comparison: comparison(r.comparisonLimit) };
  });
  if (new Set(rows.map((r) => r.id)).size !== rows.length) throw error();
  return { rows, total: dto.totalCount, student: dto.student == null ? null : { id: studentId, name: text(dto.student.name), account: text(dto.student.account) }, comparisonVersion: dto.comparisonVersion };
}
module.exports = { project };
