const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const isGuid = (value) => typeof value === 'string' && guid.test(value) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value);
const invalid = () => { throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' }); };
const text = (value) => typeof value === 'string' ? value : '';
const strings = (value) => Array.isArray(value) ? value.map(text) : [];
const score = (value) => Number.isFinite(value) && value >= 0 ? String(value) : '—';
const time = (value) => typeof value === 'string' && Number.isFinite(Date.parse(value)) ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '—';
const permission = (mode) => 'KnowPulse.Education.' + (mode === 'teacher' ? 'AssessmentManage' : 'AssessmentParticipate');
const allowed = (state, mode, schoolId) => ['teacher', 'student'].includes(mode) && isGuid(schoolId) && state.authenticated && state.context &&
  state.context.grantedPermissions.includes(permission(mode)) && state.context.workspaces.some((item) => item.schoolId === schoolId && item.role === (mode === 'teacher' ? 1 : 2));

function taskPage(dto, mode, schoolId) {
  if (!dto || !Number.isSafeInteger(dto.totalCount) || dto.totalCount < 0 || !Array.isArray(dto.items)) invalid();
  const items = dto.items.map((row) => {
    const data = mode === 'teacher' ? row.currentRevision : row;
    if (!data || !isGuid(row.id) || data.schoolId !== schoolId || (mode === 'teacher' && (!isGuid(row.publishedRevisionId) || data.id !== row.publishedRevisionId))) invalid();
    return { id: row.id, title: text(data.title), className: text(data.schoolClassName),
      subjectName: text(mode === 'teacher' ? data.curriculum && data.curriculum.subjectName : data.subjectName),
      endsAt: time(data.endsAtUtc), status: mode === 'teacher' ? '已发布测评' :
        data.currentResultVisibility === 2 ? '完整讲评已开放' : data.currentResultVisibility === 1 ? '成绩已开放' : '成绩尚未开放' };
  });
  return { items, total: dto.totalCount };
}

function overview(dto, taskId) {
  const counts = ['recipientCount', 'unstartedCount', 'inProgressCount', 'submittedCount', 'gradingCompleteCount', 'pendingQuestionCount', 'confirmedGradeSetVersion'];
  if (!dto || dto.taskId !== taskId || !isGuid(dto.publishedRevisionId) || !Array.isArray(dto.submissions) ||
      counts.some((key) => !Number.isSafeInteger(dto[key]) || dto[key] < 0)) invalid();
  const values = {}; for (const key of counts) values[key] = dto[key];
  return { ...values, publishedRevisionId: dto.publishedRevisionId,
    closed: isGuid(dto.closureId), released: isGuid(dto.currentResultReleaseId),
    submissions: dto.submissions.map((row) => {
      if (!isGuid(row.submissionId) || !Number.isSafeInteger(row.questionCount) || !Number.isSafeInteger(row.confirmedQuestionCount) ||
          row.questionCount < 1 || row.confirmedQuestionCount < 0 || row.confirmedQuestionCount > row.questionCount) invalid();
      const complete = row.confirmedQuestionCount === row.questionCount;
      return { id: row.submissionId, name: text(row.studentName), accountName: text(row.accountName),
        confirmed: row.confirmedQuestionCount, questions: row.questionCount, total: complete ? score(row.totalScore) : '—', max: score(row.maxScore), complete };
    }) };
}

function result(dto, recipientId) {
  if (!dto || dto.recipientId !== recipientId || !isGuid(dto.taskId) || ![0, 1, 2].includes(dto.currentResultVisibility) ||
      ![null, undefined, 0, 1].includes(dto.outcome) || !Array.isArray(dto.questions)) invalid();
  const base = { visibility: dto.currentResultVisibility, absent: dto.outcome === 1, total: '—', max: '—',
    releasedAt: time(dto.releasedAtUtc), scoreOpensAt: time(dto.scoreOpensAtUtc), reviewOpensAt: time(dto.reviewOpensAtUtc), questions: [] };
  if (base.absent || dto.currentResultVisibility === 0) return base;
  if (!isGuid(dto.resultReleaseId) || !Number.isFinite(dto.totalScore) || !Number.isFinite(dto.maxScore) || dto.totalScore < 0 || dto.totalScore > dto.maxScore) invalid();
  base.total = score(dto.totalScore); base.max = score(dto.maxScore);
  if (dto.currentResultVisibility !== 2) return base;
  if (!isGuid(dto.attemptId)) invalid();
  base.attemptId = dto.attemptId;
  base.releaseId = dto.resultReleaseId; base.allowCollection = dto.allowQuestionCollection === true;
  base.questions = dto.questions.map(question);
  return base;
}
function question(q) {
  if (!isGuid(q.id)) invalid();
  const rule = q.scoringDefinition || {}; const response = q.response;
  const blanks = Array.isArray(rule.blanks) ? rule.blanks : [];
  return { id: q.id, order: q.displayOrder, title: text(q.title), stem: text(q.stem), total: score(q.obtainedScore), max: score(q.maxScore),
    options: (q.options || []).map((option) => ({ key: text(option.optionKey), content: text(option.content) })),
    unanswered: !response || (!response.text && !(response.selectedOptionKeys || []).length && !(response.blanks || []).length && !(response.images || []).length),
    responseText: text(response && response.text), selected: strings(response && response.selectedOptionKeys).join('、'),
    blanks: ((response && response.blanks) || []).map((blank) => ({ id: blank.blankId, marker: text((blanks.find((b) => b.id === blank.blankId) || {}).stemMarker), text: text(blank.text) })),
    images: ((response && response.images) || []).map((image) => {
      if (!isGuid(image.attachmentId) || ![0, 90, 180, 270].includes(image.rotationDegrees)) invalid();
      return { id: image.attachmentId, rotation: image.rotationDegrees };
    }),
    answers: strings(q.correctAnswers).join('、'), reference: text(rule.referenceAnswer), analysis: text(q.analysis), feedback: text(q.feedback),
    blankAnswers: blanks.map((blank) => ({ id: blank.id, marker: text(blank.stemMarker), answers: strings(blank.allowedAnswers).join(' / ') })),
    points: (rule.scoringPoints || []).map((point) => ({ id: point.id, description: text(point.description), max: score(point.score),
      total: score(((q.parts || []).find((part) => part.partId === point.id) || {}).obtainedScore) })) };
}

module.exports = { isGuid, allowed, permission, taskPage, overview, result, question, time };
