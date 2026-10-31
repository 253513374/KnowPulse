const { isGuid, time } = require('./assessment');
const invalid = () => { throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' }); };
const text = (s) => typeof s === 'string' ? s : '';
const statuses = ['尚未开始', '当前可查看', '方案依据已变化', '方案当前不可用', '已撤回'];
const states = ['尚未开始', '学习中', '当前受阻或遇到困难', '自报完成', '正式答卷已封存'];
const kinds = ['独立诊断', '教师讲解', '例题学习', '巩固练习', '正式复测', '材料阅读'];
const allowed = (s, schoolId, write = false) => s.authenticated && s.context && isGuid(schoolId) &&
  s.context.workspaces.some((w) => w.schoolId === schoolId && w.role === 2) && s.context.grantedPermissions.includes('KnowPulse.Education.TeachingPlanView') &&
  (!write || s.context.grantedPermissions.includes('KnowPulse.Education.LearningParticipate'));
function card(row, schoolId) {
  if (!row || row.schoolId !== schoolId || !isGuid(row.id) || !isGuid(row.versionId) || ![0, 1, 2, 3, 4].includes(row.availability) || !Number.isSafeInteger(row.versionNumber) || row.versionNumber < 1) invalid();
  const executionState = row.executionState == null ? 0 : row.executionState;
  if (![0, 1, 2, 3].includes(executionState)) invalid();
  return { id: row.id, schoolId, versionId: row.versionId, version: row.versionNumber, subject: text(row.subjectName), className: text(row.className), executionState,
    executionLabel: ['执行中', '已暂停，暂不开始新活动', '教师已结束本轮安排', '教师已终止本轮安排'][executionState],
    teacher: text(row.teacherName), recordedAt: time(row.recordedAtUtc), availability: row.availability, status: statuses[row.availability] };
}
function event(row, activityId) {
  if (!row || !isGuid(row.id) || row.activityId !== activityId || ![0, 1, 2].includes(row.action) || !Number.isSafeInteger(row.studentRevisionNumber) || row.studentRevisionNumber < 1) invalid();
  return { id: row.id, activityId, revision: row.studentRevisionNumber, action: row.action, label: ['开始学习', '自报完成', '困难反馈'][row.action],
    text: text(row.sharedText), difficulty: ['内容不理解', '时间不足', '材料无法读取', '其他困难'][row.difficulty] || '', minutes: row.reportedMinutes, at: time(row.recordedAtUtc) };
}
function view(dto, schoolId, planId, versionId) {
  const plan = card(dto && dto.plan, schoolId);
  if (plan.id !== planId || plan.versionId !== versionId || !Array.isArray(dto.activities) || dto.activities.length > 50) invalid();
  const body = plan.availability === 1 ? dto.plan.content : null;
  let steps = [], materials = []; let content = null;
  if (plan.availability === 1) {
    if (!body || !Array.isArray(body.steps) || body.steps.length < 1 || body.steps.length > 50 || !Array.isArray(body.materials) || !Array.isArray(body.targets) ||
        [body.startsAtUtc, body.endsAtUtc, body.reassessmentAtUtc].some((d) => !Number.isFinite(Date.parse(d)))) invalid();
    steps = body.steps; materials = body.materials;
    if (new Set(steps.map((s) => s.id)).size !== steps.length || steps.some((s) => !isGuid(s.id) || ![0, 1, 2, 3, 4, 5].includes(s.kind) || !Number.isSafeInteger(s.estimatedMinutes) || s.estimatedMinutes < 1 || !Number.isFinite(Date.parse(s.dueAtUtc)))) invalid();
    if (materials.length > 50 || new Set(materials.map((m) => m.id)).size !== materials.length || materials.some((m) => !isGuid(m.id) || !steps.some((s) => s.id === m.stepId))) invalid();
    content = { title: text(body.title), goal: text(body.studentGoal), teacherActions: text(body.teacherActions), success: text(body.successCriteria),
      starts: time(body.startsAtUtc), ends: time(body.endsAtUtc), reassessment: time(body.reassessmentAtUtc),
      targets: body.targets.map((t) => ({ code: text(t.code), name: text(t.name), version: t.revisionNumber })) };
  }
  const ids = new Set(), stepIds = new Set(), orders = new Set();
  const activities = dto.activities.map((r) => {
    if (r.canStart != null && typeof r.canStart !== 'boolean' || r.canReadMaterials != null && typeof r.canReadMaterials !== 'boolean') invalid();
    if (!isGuid(r.id) || !isGuid(r.stepId) || ids.has(r.id) || stepIds.has(r.stepId) || orders.has(r.stepOrder) || ![0, 1, 2, 3, 4].includes(r.state) || typeof r.materialsAvailable !== 'boolean' || !Number.isSafeInteger(r.stepOrder) || r.stepOrder < 1 || r.stepOrder > 50 || !Number.isSafeInteger(r.studentRevisionNumber) || r.studentRevisionNumber < 0 ||
        r.currentStudentRevisionId != null && !isGuid(r.currentStudentRevisionId)) invalid();
    ids.add(r.id); stepIds.add(r.stepId); orders.add(r.stepOrder);
    const current = r.currentStudent ? event(r.currentStudent, r.id) : null;
    if ((current ? current.id : null) !== (r.currentStudentRevisionId || null) || (current ? current.revision : 0) !== r.studentRevisionNumber) invalid();
    const step = steps.find((s) => s.id === r.stepId); if (body && (!step || steps[r.stepOrder - 1]?.id !== r.stepId)) invalid();
    const formal = r.formalAssessment;
    let assessment = null;
    if (formal) {
      assessment = { requiresPermission: !!formal.requiresAssessmentPermission, canOpen: false, stopped: false, sealed: '—' };
      if (!assessment.requiresPermission) { assessment.stopped = !!formal.isStopped; assessment.sealed = time(formal.sealedAtUtc); }
      if (!assessment.requiresPermission && formal.canOpen) {
        if (!isGuid(formal.recipientId)) invalid();
        assessment = { ...assessment, canOpen: true, recipientId: formal.recipientId, title: text(formal.title), canContinue: !!formal.attemptId, canStart: !!formal.canStart };
      }
    }
    const provided = !!r.firstProvidedAtUtc; const isFormal = step ? [0, 4].includes(step.kind) : !!formal;
    const executable = !!step && plan.availability === 1 && r.materialsAvailable && provided && !isFormal;
    return { id: r.id, stepId: r.stepId, order: r.stepOrder, title: step ? text(step.title) : `活动 ${r.stepOrder}`, kind: step ? step.kind : null,
      kindLabel: step ? kinds[step.kind] : '当前正文未开放', instructions: step ? text(step.instructions) : '', due: step ? time(step.dueAtUtc) : '', minutes: step ? step.estimatedMinutes : null,
      materialsAvailable: r.materialsAvailable, canReadMaterials: r.canReadMaterials !== false, materials: step ? materials.filter((m) => m.stepId === step.id).map((m) => ({ id: m.id, title: text(m.title) })) : [],
      state: r.state, stateLabel: states[r.state], provided, current, currentStudentRevisionId: r.currentStudentRevisionId || null, handled: time(r.teacherHandledAtUtc), assessment, isFormal,
      canStart: executable && plan.executionState === 0 && r.canStart !== false && (!current || current.action !== 0), canComplete: executable && !!current && current.action === 0,
      canReport: provided && plan.availability !== 4 };
  }).sort((a, b) => a.order - b.order);
  if (body && activities.length !== steps.length) invalid();
  return { plan, content, activities };
}
module.exports = { allowed, card, view, event };
