const originPattern = /^https:\/\/[A-Za-z0-9.-]+(?::[0-9]{1,5})?$/;
const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const isGuid = (value) => typeof value === 'string' && guidPattern.test(value) && value !== '00000000-0000-0000-0000-000000000000';
function isPlanActivityPath(path) {
  const match = path.match(/^\/education\/teaching-plans\/([0-9a-f-]+)\/activities\?versionId=([0-9a-f-]+)$/i);
  return match && isGuid(match[1]) && isGuid(match[2]);
}
function isTeacherPlanPath(path) {
  const match = path.match(/^\/education\/teaching-plans\/([0-9a-f-]+)\/(progress|execution)$/i);
  return match && isGuid(match[1]);
}
const editRejections = new Set(['PreparationRequired', 'PreparationChanged', 'ConflictingOperation', 'OutsideWindow', 'AlreadyStarted', 'AttemptExpired', 'CapacityExceeded',
  'InvalidResponse', 'ResponseVersionConflict', 'AttemptClosed', 'InvalidSubmission', 'IncompleteSubmission', 'InvalidAttachment', 'InvalidImage', 'AttachmentNotUploaded', 'ImageLimitExceeded', 'ImageStorageFailed']);
const isEditRejection = (path, status, data) => (status === 400 || status === 403) && /^\/api\/app\/education-assessment\/(start-or-resume|save-response|submit|upload-answer-image)\//.test(path) &&
  data && data.error && typeof data.error.code === 'string' && data.error.code.startsWith('KnowPulse:EducationAssessment:') && editRejections.has(data.error.code.slice('KnowPulse:EducationAssessment:'.length));
const isStudyRejection = (path, status, data) => (status === 400 || status === 403) && /^\/api\/app\/education-study-task\/save-my-progress\/[0-9a-f-]+$/i.test(path) &&
  data && data.error && ['Invalid', 'Changed', 'OperationConflict', 'Closed'].some((code) => data.error.code === 'KnowPulse:StudyTask:' + code);
const isWorkloadRejection = (path, status, data) => (status === 400 || status === 403) && /^\/api\/app\/education-teaching-plan\/my-learning-workload(?:\?|$)/.test(path) &&
  data && data.error && ['InvalidWindow', 'Capacity'].some((code) => data.error.code === 'KnowPulse:LearningWorkload:' + code);
const isPlanRejection = (path, status, data, method) => method === 'POST' && (status === 400 || status === 403) && /^\/api\/app\/education-teaching-plan\/(?:save-my-activity\/[0-9a-f-]+$|save-activity-follow-up-for-school\?)/i.test(path) &&
  data && data.error && ['Invalid', 'SourceChanged', 'OperationConflict', 'MaterialUnavailable', 'AssessmentStopped'].some((code) => data.error.code === 'KnowPulse:TeachingPlan:' + code);
const isReminderRejection = (path, status, data, method) => method === 'POST' && (status === 400 || status === 403) && /^\/api\/app\/education-assessment\/send-reminder\?recipientId=[0-9a-f-]{36}&schoolId=[0-9a-f-]{36}$/i.test(path) &&
  data && data.error && ['ReminderUnavailable', 'ConflictingOperation', 'InvalidRevision'].some((code) => data.error.code === 'KnowPulse:EducationAssessment:' + code);
const isInvitationRejection = (path, status, data, method) => method === 'POST' && (status === 400 || status === 403) && /^\/api\/app\/education-school\/respond-to-invitation\/[0-9a-f-]{36}$/i.test(path) &&
  data && data.error && ['Invalid', 'Unavailable', 'OperationConflict', 'PendingExists'].some(code => data.error.code === 'KnowPulse:SchoolInvitation:' + code);
const isRelationRejection = (path, status, data, method) => method === 'POST' && (status === 400 || status === 403) && /^\/api\/app\/education-school\/(?:submit-relation-request|withdraw-my-relation-request)\/[0-9a-f-]{36}$/i.test(path) &&
  data && data.error && ['Invalid', 'Changed', 'Conflict'].some(code => data.error.code === 'KnowPulse:TeachingRelationRequest:' + code);
const isCorrectionRejection = (path, status, data) => (status === 400 || status === 403) && /^\/api\/app\/education-assessment\/(?:(?:preview-my-correction|submit-my-correction)\/[0-9a-f-]+|upload-my-correction-image\?deliveryId=[0-9a-f-]+&operationId=[0-9a-f-]+)$/i.test(path) &&
  data && data.error && ['Invalid', 'Changed', 'Closed', 'OperationConflict', 'ImageUnavailable'].some((code) => data.error.code === 'KnowPulse:Correction:' + code);
const isAnalysisRejection = (path, status, data) => (status === 400 || status === 403) && /^\/api\/app\/education-learning-report\/(students|student|point-evidence|trend)\?/.test(path) &&
  data && data.error && ['SourceChanged', 'Capacity'].some((code) => data.error.code === 'KnowPulse:LearningReport:' + code);
const isTeacherNoteRejection = (path, status, data, method) => method === 'POST' && (status === 400 || status === 403) && /^\/api\/app\/education-feedback\/(?:observation-for-school|save-phrase)\/[0-9a-f-]+$/i.test(path) &&
  data && data.error && ['Invalid', 'OperationConflict'].some((code) => data.error.code === 'KnowPulse:EducationFeedback:' + code);
const isCollectionWrite = (path, method) => method === 'POST' && /^\/api\/app\/education-assessment\/set-my-collection\/[0-9a-f-]+$/i.test(path);
const isPracticeRejection = (path, status, data, method = 'POST') => method === 'POST' &&
  /^\/api\/app\/education-assessment\/(?:start-my-practice-for-school(?:\?|\/)|[0-9a-f-]+\/(?:preview-my-practice|submit-my-practice|upload-my-practice-image\/[0-9a-f-]+)(?:\?|$))/i.test(path) &&
  (status === 409 || [400, 403].includes(status) && data?.error && ['Invalid', 'SourceUnavailable', 'Conflict', 'ImageUnavailable'].some((c) => data.error.code === 'KnowPulse:Practice:' + c));

const isNotebookRejection = (path, status, data) => /^\/api\/(?:app\/(?:personal-notebook|education-notebook-source)(?:\/|\?|$)|knowledge\/notebooks\/)/i.test(path) &&
  (status === 409 || [400, 403].includes(status) && data?.error && (/^KnowPulse:(?:PersonalNotebook|NotebookSource):/.test(data.error.code) ||
    ['KnowPulse:PersonalKnowledgeOrganization:InvalidOrganization', 'KnowPulse:00024', 'KnowPulse:00025', 'KnowPulse:00026'].includes(data.error.code)));
const isGrowthRead = (path, method) => method === 'GET' && /^\/api\/app\/education-student-growth\/(?:my-contexts-for-school\/[0-9a-f-]+|my-growth-for-school\?|my-point-evidence-for-school\?)/i.test(path);

module.exports = function createSession(platform, configuration, changed = () => {}) {
  const config = { ...configuration };
  const configured = originPattern.test(config.apiBaseUrl) && /^[A-Za-z0-9-]{1,64}$/.test(config.appKey) && /^KnowPulse_WeChat_[A-Za-z0-9_-]{1,100}$/.test(config.clientId);
  const storageKey = `knowpulse:wechat:${encodeURIComponent(config.apiBaseUrl)}:${config.appKey}:${config.clientId}`;
  let generation = 0;
  let token = null;
  let context = null;
  let metadata = null;
  let busy = false;
  let refreshing = false;
  let workspaceVersion = 0;
  let storageWarning = false;
  let termination = null;
  let cleanupWarning = false;
  const files = new Set();
  try {
    const stored = platform.getStorageSync(storageKey);
    if (stored && isGuid(stored.bindingId) && isGuid(stored.accountUserId) && (stored.tenantId === null || isGuid(stored.tenantId))) metadata = { bindingId: stored.bindingId, accountUserId: stored.accountUserId, tenantId: stored.tenantId };
  } catch (_) { storageWarning = true; }

  const state = () => ({ configured, generation, authenticated: token !== null, context, hasBinding: metadata !== null, busy: busy || refreshing, storageWarning, cleanupWarning, termination });
  const publish = () => changed(state());
  const error = (code, epoch = generation) => Object.assign(new Error(code), { code, generation: epoch });
  const current = (epoch) => { if (epoch !== generation) throw error('STALE_RESPONSE', epoch); };
  const tenantHeader = (tenantId) => tenantId ? { __tenant: tenantId } : {};
  const form = (value) => Object.keys(value).map((key) => `${encodeURIComponent(key)}=${encodeURIComponent(value[key])}`).join('&');

  function releaseFile(filePath) {
    if (!files.has(filePath)) return;
    // shortcut: device cache removal is best effort; shared-device policy still needs D10 acceptance.
    try { platform.getFileSystemManager().unlink({ filePath, success() { files.delete(filePath); }, fail() {} }); } catch (_) { }
  }
  const releaseFiles = () => { for (const path of files) releaseFile(path); };
  function clearAnswerStorage(identity = metadata) {
    const okay = require('./answer-store').clearAccount(platform, config, identity);
    cleanupWarning = cleanupWarning || !okay;
  }
  function denied(status) {
    termination = status === 401 ? 'expired' : 'forbidden';
    generation++; context = null; busy = false; refreshing = false; releaseFiles();
    if (status === 401) token = null;
    publish(); return Object.assign(error(status === 401 ? 'AUTH_REQUIRED' : 'FORBIDDEN'), { status });
  }

  function send(path, options, epoch) {
    if (!configured) return Promise.reject(error('NOT_CONFIGURED', epoch));
    if (!(path.startsWith('/api/') || path === '/connect/token' || path === '/connect/revocat')) return Promise.reject(error('INVALID_PATH', epoch));
    return new Promise((resolve, reject) => platform.request({
      url: config.apiBaseUrl + path,
      method: options.method || 'GET',
      data: options.form ? form(options.data) : options.data,
      header: { 'content-type': options.form ? 'application/x-www-form-urlencoded' : 'application/json', ...tenantHeader(options.tenantId), ...(options.token ? { Authorization: `Bearer ${options.token}` } : {}) },
      timeout: 10000,
      success(response) {
        try {
          current(epoch);
          if (response.statusCode >= 200 && response.statusCode < 300) { resolve(response.data); return; }
          if ((options.method || 'GET') === 'GET' && response.statusCode === 409 && /^\/api\/app\/education-learning-report(?:\?|$|\/(for-school|students|student|point-evidence|trend)\?)/.test(path)) throw Object.assign(error('REPORT_UPDATING', epoch), { status: response.statusCode });
          if (isGrowthRead(path, options.method || 'GET') && response.statusCode === 409) throw Object.assign(error('GROWTH_UPDATING', epoch), { status: response.statusCode });
          if (isGrowthRead(path, options.method || 'GET') && [400, 403].includes(response.statusCode) && response.data?.error &&
              ['KnowPulse:LearningReport:Invalid', 'KnowPulse:LearningReport:Capacity', 'KnowPulse:LearningReport:SourceChanged', 'KnowPulse:LearningReport:OperationConflict', 'KnowPulse:EducationLearning:InvalidRule'].includes(response.data.error.code))
            throw Object.assign(error(response.data.error.code === 'KnowPulse:LearningReport:SourceChanged' ? 'GROWTH_CHANGED' : 'GROWTH_UNAVAILABLE', epoch), { status: response.statusCode });
          if ((options.method || 'GET') === 'GET' && /^\/api\/app\/education-assessment\/my-result-for-school\?/.test(path) && /[?&]expectedResultReleaseId=[0-9a-f-]+(?:&|$)/i.test(path) &&
              [400, 403].includes(response.statusCode) && response.data?.error?.code === 'KnowPulse:LearningReport:SourceChanged') throw Object.assign(error('GROWTH_CHANGED', epoch), { status: response.statusCode });
          // ABP also uses 403 for bounded editing-rule failures; these must not erase an answer draft.
          if (isEditRejection(path, response.statusCode, response.data)) throw Object.assign(error('ASSESSMENT_REJECTED', epoch), { status: response.statusCode });
          if (isStudyRejection(path, response.statusCode, response.data)) throw Object.assign(error('STUDY_REJECTED', epoch), { status: response.statusCode });
          if (isWorkloadRejection(path, response.statusCode, response.data)) throw Object.assign(error('WORKLOAD_REJECTED', epoch), { status: response.statusCode });
          if (isPlanRejection(path, response.statusCode, response.data, options.method || 'GET')) throw Object.assign(error('PLAN_REJECTED', epoch), { status: response.statusCode });
          if (isReminderRejection(path, response.statusCode, response.data, options.method || 'GET')) throw Object.assign(error('REMINDER_REJECTED', epoch), { status: response.statusCode });
          if (isInvitationRejection(path, response.statusCode, response.data, options.method || 'GET')) throw Object.assign(error('INVITATION_REJECTED', epoch), { status: response.statusCode });
          if (isRelationRejection(path, response.statusCode, response.data, options.method || 'GET')) throw Object.assign(error('RELATION_REJECTED', epoch), { status: response.statusCode });
          if (isCorrectionRejection(path, response.statusCode, response.data)) throw Object.assign(error('CORRECTION_REJECTED', epoch), { status: response.statusCode });
          if (isAnalysisRejection(path, response.statusCode, response.data)) throw Object.assign(error('REPORT_UNAVAILABLE', epoch), { status: response.statusCode });
          if (isTeacherNoteRejection(path, response.statusCode, response.data, options.method)) throw Object.assign(error('TEACHER_NOTE_REJECTED', epoch), { status: response.statusCode });
          if (isPracticeRejection(path, response.statusCode, response.data, options.method)) throw Object.assign(error('PRACTICE_REJECTED', epoch), { status: response.statusCode });
          if (isNotebookRejection(path, response.statusCode, response.data)) throw Object.assign(error('NOTE_REJECTED', epoch), { status: response.statusCode });
          if ([400, 403].includes(response.statusCode) && response.data?.error?.code === 'KnowPulse:NotebookSource:Unavailable') throw Object.assign(error('SOURCE_UNAVAILABLE', epoch), { status: response.statusCode });
          if (isCollectionWrite(path, options.method) && (response.statusCode === 409 || [400, 403].includes(response.statusCode) && response.data?.error &&
              ['Invalid', 'Conflict', 'SourceUnavailable'].some((c) => response.data.error.code === 'KnowPulse:QuestionCollection:' + c))) throw Object.assign(error('COLLECTION_REJECTED', epoch), { status: response.statusCode });
          if (options.token && (response.statusCode === 401 || response.statusCode === 403)) {
            reject(denied(response.statusCode)); return;
          }
          reject(Object.assign(error(response.statusCode === 429 ? 'RATE_LIMITED' : options.kind === 'token' ? 'AUTH_FAILED' : 'REQUEST_FAILED', epoch), { status: response.statusCode }));
        } catch (failure) { reject(failure); }
      },
      fail() { reject(error('NETWORK_UNCONFIRMED', epoch)); },
    }));
  }

  function loginCode(epoch) {
    return new Promise((resolve, reject) => platform.login({ timeout: 10000,
      success(result) {
        try { current(epoch); if (typeof result.code !== 'string' || !result.code) throw error('WECHAT_LOGIN_FAILED', epoch); resolve(result.code); }
        catch (failure) { reject(failure); }
      },
      fail() { reject(error('WECHAT_LOGIN_FAILED', epoch)); },
    }));
  }

  function validateContext(value) {
    if (!value || value.accountUserId !== metadata.accountUserId || value.tenantId !== metadata.tenantId || !Array.isArray(value.workspaces) || !Array.isArray(value.grantedPermissions)) throw error('INVALID_RESPONSE');
    if (value.workspaces.some((workspace) => ![1, 2].includes(workspace.role) || !isGuid(workspace.schoolId) || !Array.isArray(workspace.classes))) throw error('INVALID_RESPONSE');
    return value;
  }

  async function authenticate(epoch) {
    const code = await loginCode(epoch);
    const response = await send('/connect/token', { method: 'POST', form: true, kind: 'token', tenantId: metadata.tenantId,
      data: { client_id: config.clientId, grant_type: 'urn:knowpulse:wechat', binding_id: metadata.bindingId, code, scope: 'KnowPulse' } }, epoch);
    current(epoch);
    if (!response || typeof response.access_token !== 'string' || !response.access_token || String(response.token_type).toLowerCase() !== 'bearer' || !Number.isFinite(response.expires_in) || response.expires_in <= 0 || response.expires_in > 300) throw error('INVALID_RESPONSE', epoch);
    token = response.access_token;
    const workspace = await send('/api/app/education-school/my-workspace', { token, tenantId: metadata.tenantId }, epoch);
    current(epoch); context = validateContext(workspace); termination = null; publish();
  }

  async function enter(bindingCode) {
    if (busy) throw error('BUSY');
    if (!configured) throw error('NOT_CONFIGURED');
    const epoch = ++generation;
    busy = true; refreshing = false; workspaceVersion++; token = null; context = null; termination = 'login'; releaseFiles(); publish();
    try {
      if (bindingCode !== undefined) {
        if (typeof bindingCode !== 'string' || !/^[0-9a-f]{32}\.[A-Za-z0-9_-]{43}$/i.test(bindingCode)) throw error('INVALID_BINDING_CODE', epoch);
        const code = await loginCode(epoch);
        const receipt = await send('/api/account/wechat/exchange', { method: 'POST', data: { appKey: config.appKey, bindingCode, code } }, epoch);
        current(epoch);
        if (!receipt || !isGuid(receipt.bindingId) || !isGuid(receipt.accountUserId) || !(receipt.tenantId === null || isGuid(receipt.tenantId))) throw error('INVALID_RESPONSE', epoch);
        if (metadata && (metadata.accountUserId !== receipt.accountUserId || metadata.tenantId !== receipt.tenantId)) clearAnswerStorage();
        metadata = { bindingId: receipt.bindingId, accountUserId: receipt.accountUserId, tenantId: receipt.tenantId };
        try { platform.setStorageSync(storageKey, metadata); storageWarning = false; } catch (_) { storageWarning = true; }
      }
      if (!metadata) throw error('BINDING_REQUIRED', epoch);
      await authenticate(epoch);
    } catch (failure) {
      if (generation === epoch) { token = null; context = null; termination = 'login_failed'; publish(); }
      throw failure;
    } finally { if (generation === epoch) { busy = false; publish(); } }
    current(epoch); return state();
  }

  async function refreshWorkspace() {
    if (!token) throw error('AUTH_REQUIRED');
    const epoch = generation; const accessToken = token;
    const version = ++workspaceVersion;
    context = null; refreshing = true; publish();
    try {
      const result = await send('/api/app/education-school/my-workspace', { token: accessToken, tenantId: metadata.tenantId }, epoch);
      current(epoch); if (version !== workspaceVersion) throw error('STALE_RESPONSE', epoch);
      context = validateContext(result); publish();
    } finally { if (epoch === generation && version === workspaceVersion) { refreshing = false; publish(); } }
    current(epoch); if (version !== workspaceVersion) throw error('STALE_RESPONSE', epoch);
    return state();
  }

  async function request(path, method = 'GET', data) {
    if (!token) throw error('AUTH_REQUIRED');
    const epoch = generation; const accessToken = token;
    const result = await send(path, { method, data, token: accessToken, tenantId: metadata.tenantId }, epoch);
    current(epoch); return result;
  }

  async function logout() {
    const previous = token; const tenantId = metadata && metadata.tenantId; const epoch = ++generation;
    token = null; context = null; busy = false; refreshing = false; termination = 'logout'; releaseFiles(); clearAnswerStorage(); publish();
    if (previous) {
      try { await send('/connect/revocat', { method: 'POST', form: true, tenantId, data: { client_id: config.clientId, token: previous, token_type_hint: 'access_token' } }, epoch); }
      catch (_) { throw error('LOGOUT_UNCONFIRMED', epoch); }
    }
    if (cleanupWarning) throw error('LOCAL_CLEANUP_UNCONFIRMED', epoch);
  }

  async function uploadAnswerImage(attemptId, input, filePath) {
    if (!token) return Promise.reject(error('AUTH_REQUIRED'));
    if (!isGuid(attemptId) || !input || !isGuid(input.operationId) || !isGuid(input.paperQuestionId) || !/^[a-f0-9]{64}$/.test(input.contentHash) ||
        !['jpg', 'png', 'webp'].some((ext) => filePath === require('./answer-store').imagePath(platform, input.operationId, ext))) return Promise.reject(error('INVALID_PATH'));
    const path = `/api/app/education-assessment/upload-answer-image/${attemptId}?OperationId=${input.operationId}&PaperQuestionId=${input.paperQuestionId}&ContentHash=${input.contentHash}`;
    return uploadFile(path, filePath, isEditRejection, 'ASSESSMENT_REJECTED');
  }
  async function uploadCorrectionImage(deliveryId, operationId, filePath) {
    if (!token) throw error('AUTH_REQUIRED');
    if (![deliveryId, operationId].every(isGuid) || !['jpg', 'png', 'webp'].some((ext) => filePath === require('./answer-store').imagePath(platform, operationId, ext))) throw error('INVALID_PATH');
    return uploadFile(`/api/app/education-assessment/upload-my-correction-image?deliveryId=${deliveryId}&operationId=${operationId}`, filePath, isCorrectionRejection, 'CORRECTION_REJECTED');
  }
  async function uploadPracticeImage(id, operationId, filePath) {
    if (!token) throw error('AUTH_REQUIRED');
    if (![id, operationId].every(isGuid) || !['jpg', 'png', 'webp'].some((ext) => filePath === require('./answer-store').imagePath(platform, operationId, ext))) throw error('INVALID_PATH');
    return uploadFile(`/api/app/education-assessment/${id}/upload-my-practice-image/${operationId}`, filePath, isPracticeRejection, 'PRACTICE_REJECTED');
  }
  async function uploadNotebookFile(id, versionId, operationId, filePath, fileName) {
    if (!token) throw error('AUTH_REQUIRED');
    const ext = typeof filePath === 'string' ? filePath.split('.').pop() : '';
    if (![id, versionId, operationId].every(isGuid) || filePath !== require('./answer-store').notebookFilePath(platform, operationId, ext)) throw error('INVALID_PATH');
    require('./notebook-file').name(fileName);
    return uploadFile(`/api/knowledge/notebooks/${id}/versions/${versionId}/attachments/operations/${operationId}`, filePath, isNotebookRejection, 'NOTE_REJECTED', { 'X-KnowPulse-File-Name': encodeURIComponent(fileName) });
  }
  function uploadFile(path, filePath, rejected, rejectionCode, extraHeaders = {}) {
    const epoch = generation; const accessToken = token;
    return new Promise((resolve, reject) => platform.uploadFile({
      url: config.apiBaseUrl + path,
      name: 'file', filePath, timeout: 30000, header: { Authorization: `Bearer ${accessToken}`, ...tenantHeader(metadata.tenantId), ...extraHeaders },
      success(response) {
        try {
          current(epoch);
          let data = null;
          try { if (typeof response.data === 'string') data = JSON.parse(response.data); } catch (_) { }
          if (rejected(path, response.statusCode, data)) throw Object.assign(error(rejectionCode, epoch), { status: response.statusCode });
          if (response.statusCode === 401 || response.statusCode === 403) throw denied(response.statusCode);
          if (response.statusCode < 200 || response.statusCode >= 300) throw Object.assign(error('REQUEST_FAILED', epoch), { status: response.statusCode });
          if (typeof response.data !== 'string') throw error('INVALID_RESPONSE', epoch);
          if (!data) throw error('INVALID_RESPONSE', epoch);
          resolve(data);
        } catch (failure) { reject(failure.code ? failure : error('INVALID_RESPONSE', epoch)); }
      }, fail() { reject(error('NETWORK_UNCONFIRMED', epoch)); },
    }));
  }

  function download(path) {
    if (!token) return Promise.reject(error('AUTH_REQUIRED'));
    const match = typeof path === 'string' && (path.match(/^\/api\/education\/assessment\/attempts\/([0-9a-f-]+)\/images\/([0-9a-f-]+)$/i) ||
      path.match(/^\/api\/education\/study\/recipients\/([0-9a-f-]+)\/materials\/([0-9a-f-]+)\/attachments\/([0-9a-f-]+)$/i) ||
      path.match(/^\/api\/education\/teaching\/activities\/([0-9a-f-]+)\/materials\/([0-9a-f-]+)\/attachments\/([0-9a-f-]+)$/i) ||
      path.match(/^\/api\/education\/corrections\/deliveries\/([0-9a-f-]+)\/images\/([0-9a-f-]+)$/i) ||
      path.match(/^\/api\/education\/practices\/([0-9a-f-]+)\/images\/([0-9a-f-]+)$/i) ||
      path.match(/^\/api\/knowledge\/notebooks\/([0-9a-f-]+)\/versions\/([0-9a-f-]+)\/attachments\/([0-9a-f-]+)$/i));
    if (!match || match.slice(1).some((id) => !isGuid(id))) return Promise.reject(error('INVALID_PATH'));
    const epoch = generation; const accessToken = token;
    return new Promise((resolve, reject) => platform.downloadFile({
      url: config.apiBaseUrl + path, timeout: 10000,
      header: { Authorization: `Bearer ${accessToken}`, ...tenantHeader(metadata.tenantId) },
      success(response) {
        const filePath = typeof response.tempFilePath === 'string' ? response.tempFilePath : null;
        if (filePath) files.add(filePath);
        try {
          current(epoch);
          if (response.statusCode === 401 || response.statusCode === 403) throw denied(response.statusCode);
          if (response.statusCode !== 200 || !filePath) throw error('REQUEST_FAILED', epoch);
          resolve(filePath);
        } catch (failure) { if (filePath) releaseFile(filePath); reject(failure); }
      },
      fail() { reject(error('NETWORK_UNCONFIRMED', epoch)); },
    }));
  }

  return Object.freeze({ getState: state, login: () => enter(), bindAndLogin: (code) => enter(code), refreshWorkspace, request, logout, download, releaseFile, uploadAnswerImage, uploadCorrectionImage, uploadPracticeImage, uploadNotebookFile,
    portalUrl: (path = '/account/wechat-bindings') => originPattern.test(config.portalBaseUrl) && typeof path === 'string' &&
      (path === '/account/wechat-bindings' || path === '/account/assistance' || path === '/education/learning-reports' || path === '/education/notebook' || path === '/education/growth' || (/^\/education\/evidence\//.test(path) && isGuid(path.slice('/education/evidence/'.length))) || (/^\/education\/assessment-grading\//.test(path) && isGuid(path.slice('/education/assessment-grading/'.length))) ||
        (/^\/education\/feedback\?feedbackId=/.test(path) && isGuid(path.slice('/education/feedback?feedbackId='.length))) ||
        (/^\/education\/corrections\/my\?deliveryId=/.test(path) && isGuid(path.slice('/education/corrections/my?deliveryId='.length))) ||
        (/^\/education\/practice\?practiceId=/.test(path) && isGuid(path.slice('/education/practice?practiceId='.length))) || isPlanActivityPath(path) || isTeacherPlanPath(path)) ? config.portalBaseUrl + path : null });
};
