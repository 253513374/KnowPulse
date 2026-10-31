const assessment = require('./assessment');
const { isGuid, time } = assessment;
const error = (code = 'INVALID_RESPONSE') => Object.assign(new Error(code), { code });
const availability = ['可订正', '尚未开始', '已截止', '已被新版替换', '已撤回', '完整讲评未开放', '原评分已变化', '当前不可执行'];
const work = ['尚未上交', '等待教师复核', '需要完善', '已通过复核'];
const allowed = (s, school) => assessment.allowed(s, 'student', school) && s.context.grantedPermissions.includes('KnowPulse.Education.LearningParticipate');
function content(row, submitted = false) {
  if (!row || !['originalThinking', 'currentSolution', 'checkingStrategy'].every((k) => typeof row[k] === 'string') ||
      row.originalThinking.length > 4000 || row.currentSolution.length > 8192 || row.checkingStrategy.length > 4000 || !Array.isArray(row.images) || row.images.length > 5 ||
      row.images.some((i) => !i || !isGuid(i.attachmentId) || ![0, 90, 180, 270].includes(i.rotationDegrees)) || new Set(row.images.map((i) => i.attachmentId)).size !== row.images.length ||
      submitted && (!row.checkingStrategy.trim() || !row.currentSolution.trim() && row.images.length === 0)) throw error('CORRECTION_INVALID');
  return { originalThinking: row.originalThinking, currentSolution: row.currentSolution, checkingStrategy: row.checkingStrategy,
    images: row.images.map((i) => ({ attachmentId: i.attachmentId, rotationDegrees: i.rotationDegrees })) };
}
function submission(row, delivery) {
  if (!row || !isGuid(row.id) || row.deliveryId !== delivery || !Number.isSafeInteger(row.submissionNumber) || row.submissionNumber < 1 || !/^[a-f0-9]{64}$/.test(row.requestFingerprint)) throw error();
  return { id: row.id, number: row.submissionNumber, basedOn: row.basedOnSubmissionId || null, content: content(row.content, true), at: time(row.submittedAtUtc) };
}
function review(row, delivery, submitted) {
  if (!row || !isGuid(row.id) || row.deliveryId !== delivery || row.submissionId !== submitted || ![0, 1].includes(row.decision) || typeof row.studentFeedback !== 'string') throw error();
  return { id: row.id, submissionId: submitted, label: row.decision === 1 ? '已通过复核' : '需要完善', feedback: row.studentFeedback, at: time(row.publishedAtUtc) };
}
function card(row, school, body = false) {
  if (!row || row.schoolId !== school || !isGuid(row.id) || !isGuid(row.publicationId) || !isGuid(row.assessmentRecipientId) || !availability[row.availability] ||
      !Number.isSafeInteger(row.versionNumber) || row.versionNumber < 1 || ![null, undefined, 0, 1, 2, 3].includes(row.workState) ||
      [row.startsAtUtc, row.dueAtUtc, row.serverNowUtc].some((d) => !Number.isFinite(Date.parse(d)))) throw error();
  const readable = body && [0, 2].includes(row.availability), current = body && row.currentSubmission ? submission(row.currentSubmission, row.id) : null;
  if (body && (current?.id || null) !== (row.currentSubmissionId || null)) throw error();
  return { id: row.id, publicationId: row.publicationId, version: row.versionNumber, schoolId: school, recipientId: row.assessmentRecipientId,
    teacher: row.teacherName || '', subject: row.subjectName || '', className: row.className || '', starts: time(row.startsAtUtc), due: time(row.dueAtUtc),
    availability: row.availability, status: availability[row.availability], workState: row.workState == null ? '暂不可判断' : work[row.workState],
    title: readable ? row.title || '订正任务' : '订正任务', instructions: readable ? row.instructions || '' : '', publicReason: body ? row.publicReason || '' : '',
    canSubmit: row.availability === 0, current, currentId: row.currentSubmissionId || null,
    review: body && ![5, 6, 7].includes(row.availability) && row.currentReview && current ? review(row.currentReview, row.id, current.id) : null,
    question: readable && row.originalQuestion ? assessment.question(row.originalQuestion) : null };
}
module.exports = { allowed, content, submission, review, card, error, imagePolicy: { maxFileBytes: 5 * 1024 * 1024, allowedContentTypes: ['image/jpeg', 'image/png', 'image/webp'] } };
