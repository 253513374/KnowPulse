const assessment = require('./assessment');
const answer = require('./answer');
const { allowed } = require('./collection');
const error = (code = 'INVALID_RESPONSE') => Object.assign(new Error(code), { code });
const imagePolicy = { maxFileBytes: 5 * 1024 * 1024, allowedContentTypes: ['image/jpeg', 'image/png', 'image/webp'] };
function question(q) {
  if (!q || !assessment.isGuid(q.id) || ![0, 1, 2, 3, 4].includes(q.type) || typeof q.title !== 'string' || typeof q.stem !== 'string' ||
      !Array.isArray(q.options) || !Array.isArray(q.responseFields) || q.options.length > 100 || q.responseFields.length > 100 ||
      q.options.some((o) => !o || typeof o.optionKey !== 'string' || !o.optionKey || typeof o.content !== 'string') || new Set(q.options.map((o) => o.optionKey)).size !== q.options.length ||
      q.responseFields.some((b) => !b || !assessment.isGuid(b.id) || typeof b.stemMarker !== 'string') || new Set(q.responseFields.map((b) => b.id)).size !== q.responseFields.length) throw error();
  return { id: q.id, type: q.type, title: q.title, stem: q.stem, score: q.score,
    options: q.options.map((o) => ({ optionKey: o.optionKey, content: o.content })), responseFields: q.responseFields.map((b) => ({ id: b.id, stemMarker: b.stemMarker })) };
}
function response(r, q) {
  if (!r || ![0, 1, 2, 3, 4].includes(r.type) || !Array.isArray(r.selectedOptionKeys) || !Array.isArray(r.blanks) || !Array.isArray(r.images) || r.images.length > 5 ||
      r.selectedOptionKeys.some((k) => typeof k !== 'string' || !k || k.length > 100) || r.blanks.some((b) => !b || !assessment.isGuid(b.blankId))) throw error();
  return answer.response(r, q || { type: r.type, options: r.selectedOptionKeys.map((key) => ({ optionKey: key })), responseFields: r.blanks.map((b) => ({ id: b.blankId })) });
}
function card(r, school) {
  if (!r || ![r.id, r.recipientId, r.paperQuestionId].every(assessment.isGuid) || r.schoolId !== school || !Number.isFinite(Date.parse(r.startedAtUtc)) ||
      r.submittedAtUtc != null && !Number.isFinite(Date.parse(r.submittedAtUtc))) throw error();
  return { id: r.id, schoolId: school, recipientId: r.recipientId, questionId: r.paperQuestionId, started: assessment.time(r.startedAtUtc), submitted: r.submittedAtUtc != null };
}
function image(r) {
  if (!r || !assessment.isGuid(r.id) || r.isUploaded !== true || typeof r.fileName !== 'string' || !/^[a-f0-9]{64}$/.test(r.contentHash) ||
      !Number.isSafeInteger(r.size) || r.size < 12 || r.size > imagePolicy.maxFileBytes || !imagePolicy.allowedContentTypes.includes(r.contentType)) throw error();
  return { id: r.id, fileName: r.fileName, contentHash: r.contentHash, size: r.size, contentType: r.contentType, isUploaded: true };
}
function detail(r, school, id) {
  const c = card(r, school); if (c.id !== id || typeof r.sourceAvailable !== 'boolean' || !Array.isArray(r.ownImages) || r.ownImages.length > 40 ||
      c.submitted !== assessment.isGuid(r.submissionOperationId) || !r.sourceAvailable && (r.question != null || r.feedback != null) || !c.submitted && (r.feedback != null || r.submittedResponse != null)) throw error();
  const q = r.sourceAvailable ? question(r.question) : null; if (q && q.id !== c.questionId) throw error();
  const own = c.submitted ? response(r.submittedResponse, q) : null;
  let feedback = null;
  if (r.sourceAvailable && c.submitted) {
    const f = r.feedback, e = f && f.evaluation;
    if (!e || !Number.isFinite(e.maxScore) || e.maxScore <= 0 || !(e.obtainedScore === null || Number.isFinite(e.obtainedScore) && e.obtainedScore >= 0 && e.obtainedScore <= e.maxScore) || !Array.isArray(e.parts)) throw error();
    feedback = assessment.question({ ...q, response: own, correctAnswers: f.correctAnswers, analysis: f.analysis, scoringDefinition: f.scoringDefinition,
      parts: e.parts, maxScore: e.maxScore, obtainedScore: e.obtainedScore });
  }
  return { ...c, sourceAvailable: r.sourceAvailable, question: q, response: own, feedback, images: r.ownImages.map(image) };
}
function form(q, r) {
  return { ...q, value: response(r, q), options: (q.type === 2 ? [{ optionKey: 'true', content: '正确' }, { optionKey: 'false', content: '错误' }] : q.options).map((o) => ({ ...o, selected: r.selectedOptionKeys.includes(o.optionKey) })),
    fields: q.responseFields.map((f) => ({ ...f, text: r.blanks.find((b) => b.blankId === f.id)?.text || '' })) };
}
module.exports = { allowed, error, question, response, card, detail, image, form, imagePolicy, empty: (type) => ({ schemaVersion: 1, type, selectedOptionKeys: [], blanks: [], text: null, images: [] }) };
