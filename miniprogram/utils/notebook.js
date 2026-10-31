const { isGuid, time } = require('./assessment');
const { actor } = require('./feedback');
const { attachmentType } = require('./study');
const error = (code = 'INVALID_RESPONSE') => Object.assign(new Error(code), { code });
const allowed = (s) => !!(actor(s) && s.context.grantedPermissions.includes('KnowPulse.KnowledgeBase.PersonalLibrary'));
const text = (v, max, nullable = false) => { if (nullable && v == null) return ''; if (typeof v !== 'string' || v.length > max) throw error(); return v; };
function content(r, draft = false) {
  if (!r || r.customTags == null || !Array.isArray(r.customTags) || r.customTags.length > 20 || r.materialYear != null && (!Number.isInteger(r.materialYear) || r.materialYear < 1 || r.materialYear > 9999)) throw error('NOTE_INVALID');
  const value = { title: text(r.title, 256), summary: text(r.summary, 1024, true), contentText: text(r.contentText, 8192, true),
    subjectName: text(r.subjectName, 128, true), materialYear: r.materialYear ?? null, customTags: r.customTags.map((t) => text(t, 64)) };
  if (!draft && !value.title.trim() || value.customTags.some((t) => !t.trim())) throw error('NOTE_INVALID'); return value;
}
function metadata(r, owner, id) {
  if (!r || r.id !== id || r.ownerUserId !== owner || !r.concurrencyStamp || r.concurrencyStamp.length > 40 || !Number.isSafeInteger(r.latestVersionNumber) || r.latestVersionNumber < 0 ||
      (r.latestVersionNumber === 0 ? r.latestVersionId != null : !isGuid(r.latestVersionId))) throw error();
  const c = content({ ...r, contentText: '' }); return { ...c, id, concurrencyStamp: r.concurrencyStamp, latestVersionId: r.latestVersionId, latestVersionNumber: r.latestVersionNumber };
}
function version(r, id, body = false) {
  if (!r || !isGuid(r.id) || r.noteId !== id || !Number.isSafeInteger(r.versionNumber) || r.versionNumber < 1 || !Number.isFinite(Date.parse(r.createdAtUtc)) || ![0, 1, 2, 3, 4].includes(r.status)) throw error();
  return { id: r.id, title: text(r.title, 256), versionNumber: r.versionNumber, created: time(r.createdAtUtc), status: r.status, ...(body ? { contentText: text(r.contentText, 8192, true) } : {}) };
}
function card(r, owner) {
  if (!r || !isGuid(r.id) || r.ownerUserId !== owner || r.materialType !== 7 || !isGuid(r.libraryId)) throw error();
  return { id: r.id, title: text(r.title, 256), subjectName: text(r.subjectName, 128, true), materialYear: r.materialYear ?? null };
}
function page(dto, project) {
  if (!dto || !Array.isArray(dto.items) || dto.items.length > 20 || !Number.isSafeInteger(dto.totalCount) || dto.totalCount < dto.items.length) throw error();
  return { items: dto.items.map(project), total: dto.totalCount };
}
function attachment(r, v) {
  if (!r || !isGuid(r.id) || r.knowledgeVersionId !== v || !Number.isSafeInteger(r.size) || r.size < 0 || typeof r.contentType !== 'string') throw error();
  return { id: r.id, name: text(r.fileName, 256), size: r.size, type: attachmentType(r) };
}
function pointer(r) {
  if (!r || ![0, 1].includes(r.kind) || ![r.recipientId, r.sourceVersionId, r.componentId].every(isGuid)) throw error();
  return { kind: r.kind, recipientId: r.recipientId, sourceVersionId: r.sourceVersionId, componentId: r.componentId };
}
function source(r, v) {
  if (!r || !isGuid(r.id) || r.noteVersionId !== v || typeof r.removed !== 'boolean' || typeof r.available !== 'boolean' || r.available && (!isGuid(r.schoolId) || r.removed) || !r.available && (r.schoolId != null || r.title != null || r.openUrl != null)) throw error();
  return { id: r.id, noteVersionId: v, source: pointer(r.source), removed: r.removed, available: r.available, schoolId: r.available ? r.schoolId : null, title: r.available ? text(r.title, 2048) : '来源当前不可用' };
}
function sources(r, v) {
  if (!r || !r.concurrencyStamp || !Array.isArray(r.items) || r.items.length > 200) throw error();
  return { stamp: r.concurrencyStamp, items: r.items.map((s) => source(s, v)) };
}
function sourceUrl(r) {
  if (!r.available || r.removed || !isGuid(r.schoolId)) throw error('SOURCE_UNAVAILABLE'); const p = pointer(r.source);
  return p.kind === 0 ? `/pages/assessment-result/index?id=${p.recipientId}&schoolId=${r.schoolId}&questionId=${p.componentId}&taskRevisionId=${p.sourceVersionId}` :
    `/pages/study-task/index?id=${p.recipientId}&schoolId=${r.schoolId}&materialId=${p.componentId}&publicationId=${p.sourceVersionId}`;
}
function receipt(r, p) {
  if (!r) return null;
  if (r.operationId !== p.operationId || !isGuid(r.noteId) || p.noteId && r.noteId !== p.noteId || !Number.isFinite(Date.parse(r.recordedAtUtc)) ||
      r.kind !== { create: 0, save: 1, 'notebook-file': 2, delete: 3 }[p.kind] || r.kind !== 3 && (!isGuid(r.versionId) || !Number.isSafeInteger(r.versionNumber) || r.versionNumber < 1) ||
      r.kind === 2 && (r.attachmentId !== p.operationId || r.contentHash !== p.file.contentHash || r.fileSize !== p.file.size || r.versionId !== p.versionId)) throw error();
  return { noteId: r.noteId, operationId: r.operationId, kind: r.kind, versionId: r.versionId, versionNumber: r.versionNumber, attachmentId: r.attachmentId };
}
module.exports = { allowed, error, content, metadata, version, card, page, attachment, pointer, source, sources, sourceUrl, receipt, blank: () => ({ title: '', summary: '', contentText: '', subjectName: '', materialYear: null, customTags: [] }) };
