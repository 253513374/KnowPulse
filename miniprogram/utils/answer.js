const { isGuid } = require('./assessment');
const fail = () => { throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' }); };
const clone = (value) => JSON.parse(JSON.stringify(value));
const empty = (type) => ({ schemaVersion: 1, type, selectedOptionKeys: [], blanks: [], text: null, images: [] });
const head = (attempt, questionId) => attempt.responses.find((r) => r.paperQuestionId === questionId) || null;
const headId = (attempt, questionId) => { const response = head(attempt, questionId); return response ? response.id : null; };

function response(value, question) {
  if (!value || value.schemaVersion !== 1 || value.type !== question.type || !Array.isArray(value.selectedOptionKeys) || !Array.isArray(value.blanks) || !Array.isArray(value.images) ||
      value.selectedOptionKeys.length > 100 || value.blanks.length > 100 || value.images.length > 20 || !(value.text === null || typeof value.text === 'string' && value.text.length <= 20000)) fail();
  const keys = question.type === 2 ? ['true', 'false'] : question.options.map((o) => o.optionKey);
  if (new Set(value.selectedOptionKeys).size !== value.selectedOptionKeys.length || value.selectedOptionKeys.some((k) => !keys.includes(k)) ||
      new Set(value.blanks.map((b) => b.blankId)).size !== value.blanks.length || value.blanks.some((b) => !question.responseFields.some((f) => f.id === b.blankId) || typeof b.text !== 'string' || b.text.length > 20000) ||
      new Set(value.images.map((i) => i.attachmentId)).size !== value.images.length || value.images.some((i) => !isGuid(i.attachmentId) || ![0, 90, 180, 270].includes(i.rotationDegrees))) fail();
  if ((question.type < 3 && (value.blanks.length || value.text !== null || value.images.length)) || ([0, 2].includes(question.type) && value.selectedOptionKeys.length > 1) ||
      (question.type === 3 && (value.selectedOptionKeys.length || value.text !== null || value.images.length)) || (question.type === 4 && (value.selectedOptionKeys.length || value.blanks.length))) fail();
  const safe = { schemaVersion: 1, type: question.type, selectedOptionKeys: value.selectedOptionKeys.slice(), blanks: value.blanks.map((b) => ({ blankId: b.blankId, text: b.text })),
    text: value.text, images: value.images.map((i) => ({ attachmentId: i.attachmentId, rotationDegrees: i.rotationDegrees })) };
  if (JSON.stringify(safe).length > 200000) fail(); return safe;
}

function attempt(dto, task) {
  if (!dto || !isGuid(dto.id) || dto.id !== task.currentAttemptId || dto.recipientId !== task.id || dto.taskRevisionId !== task.taskRevisionId || ![0, 1].includes(dto.status) ||
      !Number.isSafeInteger(dto.paperSnapshotVersion) || dto.paperSnapshotVersion < 1 || !Number.isSafeInteger(dto.responseSetVersion) || dto.responseSetVersion < 0 ||
      !Number.isFinite(Date.parse(dto.expiresAtUtc)) || !Number.isFinite(Date.parse(dto.serverNowUtc)) || !dto.paper || !Array.isArray(dto.paper.questions) ||
      dto.paper.questionCount !== dto.paper.questions.length || dto.paper.questionCount < 1 || !Array.isArray(dto.responses)) fail();
  const questions = dto.paper.questions.map((q) => {
    if (!isGuid(q.id) || ![0, 1, 2, 3, 4].includes(q.type) || !Array.isArray(q.options) || !Array.isArray(q.responseFields)) fail();
    if (q.options.some((o) => typeof o.optionKey !== 'string' || !o.optionKey || typeof o.content !== 'string') || new Set(q.options.map((o) => o.optionKey)).size !== q.options.length ||
        q.responseFields.some((f) => !isGuid(f.id) || typeof f.stemMarker !== 'string') || new Set(q.responseFields.map((f) => f.id)).size !== q.responseFields.length) fail();
    return { id: q.id, type: q.type, title: typeof q.title === 'string' ? q.title : '', stem: typeof q.stem === 'string' ? q.stem : '', displayOrder: q.displayOrder, score: q.score,
      options: q.options.map((o) => ({ optionKey: o.optionKey, content: o.content })), responseFields: q.responseFields.map((f) => ({ id: f.id, stemMarker: f.stemMarker })) };
  });
  if (new Set(questions.map((q) => q.id)).size !== questions.length || new Set(dto.responses.map((r) => r.paperQuestionId)).size !== dto.responses.length) fail();
  const responses = dto.responses.map((r) => {
    const q = questions.find((q) => q.id === r.paperQuestionId);
    if (!q || !isGuid(r.id) || r.attemptId !== dto.id || r.paperSnapshotVersion !== dto.paperSnapshotVersion || !Number.isSafeInteger(r.resultResponseSetVersion) || r.resultResponseSetVersion < 1 || r.resultResponseSetVersion > dto.responseSetVersion) fail();
    return { id: r.id, paperQuestionId: q.id, response: response(r.response, q), savedAtUtc: r.savedAtUtc };
  });
  return { id: dto.id, recipientId: dto.recipientId, taskRevisionId: dto.taskRevisionId, status: dto.status, paperSnapshotVersion: dto.paperSnapshotVersion,
    responseSetVersion: dto.responseSetVersion, expiresAtUtc: dto.expiresAtUtc, serverNowUtc: dto.serverNowUtc, submissionId: dto.submissionId || null, questions, responses };
}

function draft(value, question, current) {
  if (!value || value.attemptId !== current.id || value.paperSnapshotVersion !== current.paperSnapshotVersion || value.questionId !== question.id ||
      !(value.baseRevisionId === null || isGuid(value.baseRevisionId))) fail();
  return { attemptId: value.attemptId, paperSnapshotVersion: value.paperSnapshotVersion, questionId: question.id, baseRevisionId: value.baseRevisionId, value: response(value.value, question) };
}
function edit(current, questionId, drafts) {
  const question = current.questions.find((q) => q.id === questionId); if (!question) fail();
  if (!drafts[questionId]) drafts[questionId] = { attemptId: current.id, paperSnapshotVersion: current.paperSnapshotVersion, questionId,
    baseRevisionId: headId(current, questionId), value: head(current, questionId) ? clone(head(current, questionId).response) : empty(question.type) };
  return drafts[questionId];
}
function conflict(current, local) { return local.baseRevisionId !== headId(current, local.questionId); }
function preview(dto, current) {
  if (!dto || dto.attemptId !== current.id || dto.paperSnapshotVersion !== current.paperSnapshotVersion || dto.responseSetVersion !== current.responseSetVersion ||
      typeof dto.fingerprint !== 'string' || !/^[a-f0-9]{64}$/i.test(dto.fingerprint) || !Array.isArray(dto.questions) || dto.questions.length !== current.questions.length || !Array.isArray(dto.unlinkedImages)) fail();
  const ids = new Set();
  const questions = dto.questions.map((q) => {
    if (!current.questions.some((p) => p.id === q.id) || ids.has(q.id) || q.savedResponseRevisionId !== headId(current, q.id) || typeof q.isUnanswered !== 'boolean' || !Number.isSafeInteger(q.unansweredBlankCount) || q.unansweredBlankCount < 0 || !Number.isSafeInteger(q.savedImageCount) || q.savedImageCount < 0) fail();
    ids.add(q.id); return { id: q.id, displayOrder: q.displayOrder, title: q.title, isUnanswered: q.isUnanswered, unansweredBlankCount: q.unansweredBlankCount, savedImageCount: q.savedImageCount };
  });
  const unlinkedImages = dto.unlinkedImages.map((i) => {
    if (!isGuid(i.id) || i.attemptId !== current.id || !current.questions.some((q) => q.id === i.paperQuestionId)) fail();
    return { id: i.id, questionId: i.paperQuestionId, name: i.fileName, uploaded: i.isUploaded === true };
  });
  return { fingerprint: dto.fingerprint, canSubmit: dto.canSubmit === true, submissionId: dto.submissionId || null, questions, unlinkedImages };
}
function projected(current, questionId, drafts) {
  const q = current.questions.find((q) => q.id === questionId); if (!q) fail();
  const local = drafts[questionId]; const saved = head(current, questionId); const value = local ? local.value : saved ? saved.response : empty(q.type);
  const options = q.type === 2 ? [{ optionKey: 'true', content: '正确' }, { optionKey: 'false', content: '错误' }] : q.options;
  return { ...q, dirty: !!local, conflict: !!local && conflict(current, local), savedAtUtc: saved ? saved.savedAtUtc : null,
    value: clone(value), options: options.map((o) => ({ ...o, selected: value.selectedOptionKeys.includes(o.optionKey) })),
    fields: q.responseFields.map((f) => ({ ...f, text: (value.blanks.find((b) => b.blankId === f.id) || {}).text || '' })), server: saved ? clone(saved.response) : null };
}
module.exports = { attempt, response, draft, edit, conflict, preview, projected, headId, clone };
