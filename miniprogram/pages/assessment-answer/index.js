const assessment = require('../../utils/assessment');
const answer = require('../../utils/answer');
const { actor } = require('../../utils/feedback');
const createStore = require('../../utils/answer-store');
const createCommand = require('../../utils/answer-command');
const images = require('../../utils/answer-image');
const message = require('../../utils/messages');
const error = (code) => Object.assign(new Error(code), { code });
const ask = (content) => new Promise((resolve) => wx.showModal({ title: '请核对', content, success: (r) => resolve(r.confirm), fail: () => resolve(false) }));
const pick = () => new Promise((resolve, reject) => wx.chooseMedia({ count: 1, mediaType: ['image'], sourceType: ['album', 'camera'], sizeType: ['original'],
  success: (r) => r.tempFiles && r.tempFiles.length === 1 ? resolve(r.tempFiles[0].tempFilePath) : reject(error('IMAGE_UNAVAILABLE')), fail: () => reject(error('IMAGE_UNAVAILABLE')) }));

Page({
  data: { ready: false, busy: false, task: null, question: null, index: 0, count: 0, pending: null, consent: false, remaining: '—', canEdit: false,
    draftCount: 0, unpersisted: false, showCloud: false, review: null, reviewOffset: 0, confirmed: false, savedOnly: false, submission: null,
    error: '', notice: '', imagePath: '', imageRotation: 0 },
  onLoad(query) {
    this.id = query.id; this.schoolId = query.schoolId; this.owner = null; this.version = 0; this.index = 0; this.drafts = {}; this.consent = false; this.files = new Set(); this.unpersistedIds = new Set(); this.unpersisted = false;
    this.unsubscribe = getApp().subscribe((state) => {
      const changed = this.owner && state.context && actor(state) !== this.owner;
      const forbidden = state.termination === 'forbidden' || state.context && !assessment.allowed(state, 'student', this.schoolId);
      if (!state.authenticated || changed || forbidden) {
        this.version++;
        if (changed || forbidden || state.termination !== 'expired' && !this.reauth) this.clearPrivate();
        this.hideViews();
        if (!this.disposed) this.setData({ busy: false, error: '当前身份不可用。到期可重新验证同一账号；其他身份不能恢复本人的草稿。' });
      } else if (!state.context) this.hideViews();
    });
  },
  onShow() { this.visible = true; return this.load(); },
  onHide() { this.visible = false; this.version++; this.hideViews(); if (this.timer) clearInterval(this.timer); this.timer = null; },
  onUnload() { this.disposed = true; this.visible = false; this.version++; this.unsubscribe(); if (this.timer) clearInterval(this.timer); this.clearPrivate(); },
  hideViews() { this.clearImages(); if (!this.disposed) this.setData({ ready: false, task: null, question: null, review: null, submission: null, canEdit: false, imagePath: '' }); },
  clearPrivate() { this.task = null; this.attempt = null; this.drafts = {}; this.review = null; this.submission = null; this.consent = false; this.unpersistedIds.clear(); this.unpersisted = false; this.clearImages(); },
  authorized() { const state = getApp().session.getState(); return this.visible && !this.disposed && this.owner === actor(state) && assessment.allowed(state, 'student', this.schoolId); },
  async run(action) {
    if (!this.visible || this.disposed || this.data.busy) return;
    const ticket = this.version; this.setData({ busy: true, canEdit: false, error: '', notice: '' });
    try { await action(); }
    catch (failure) { if (this.visible && ticket === this.version) this.setData({ error: failure.status === 409 ? '云端记录已变化，请核对原操作回执和本题云端版本；未覆盖新答案。' : message(failure) }); }
    finally { if (!this.disposed && ticket === this.version) { this.setData({ busy: false }); this.render(); } }
  },
  load() { return this.run(() => this.readCloud(true)); },
  async readCloud(refreshIdentity = false) {
    if (!assessment.isGuid(this.id) || !assessment.isGuid(this.schoolId)) throw error('INVALID_RESPONSE');
    const ticket = this.version; const session = getApp().session;
    if (refreshIdentity) await session.refreshWorkspace();
    const state = session.getState(); const owner = actor(state); const context = state.context;
    if (!owner || this.owner && this.owner !== owner || !assessment.allowed(state, 'student', this.schoolId)) throw error('FORBIDDEN');
    this.owner = owner;
    const task = await session.request('/api/app/education-assessment/my-student-task/' + this.id);
    if (!task || task.id !== this.id || task.schoolId !== this.schoolId || !assessment.isGuid(task.taskRevisionId)) throw error('INVALID_RESPONSE');
    let current = null, policy = null, submission = null;
    if (task.currentAttemptId) {
      const rows = await Promise.all([session.request('/api/app/education-assessment/attempt/' + task.currentAttemptId),
        session.request('/api/app/education-assessment/answer-image-policy/' + task.currentAttemptId), session.request('/api/app/education-assessment/submission/' + task.currentAttemptId)]);
      current = answer.attempt(rows[0], task); policy = rows[1]; submission = rows[2];
      if (!policy || !Number.isSafeInteger(policy.maxFileBytes) || policy.maxFileBytes < 1 || !Number.isSafeInteger(policy.maxImagesPerResponse) || policy.maxImagesPerResponse < 1 || !Array.isArray(policy.allowedContentTypes)) throw error('INVALID_RESPONSE');
      if (submission && (submission.id !== current.submissionId || submission.attemptId !== current.id || ![0, 1].includes(submission.cause))) throw error('INVALID_RESPONSE');
    }
    if (!this.authorized() || ticket !== this.version || session.getState().context !== context) throw error('STALE_RESPONSE');
    if (this.attempt && current && this.attempt.id !== current.id) throw error('INVALID_RESPONSE');
    if (!this.store) this.store = createStore(wx, getApp().configuration, state.context, this.id);
    if (!this.writer) this.writer = createCommand(wx, session, this.store, this.id, this.schoolId, () => this.authorized());
    const drafts = {};
    if (current) {
      const restored = this.store.drafts(current);
      for (const question of current.questions) {
        const local = this.drafts[question.id] || restored.values[question.id];
        if (local) drafts[question.id] = answer.draft(local, question, current);
        if (local && JSON.stringify(local) !== JSON.stringify(restored.values[question.id])) this.unpersistedIds.add(question.id); else this.unpersistedIds.delete(question.id);
      }
      if (restored.expired) this.setData({ notice: `${restored.expired} 份本机草稿已超过 24 小时逻辑保留期，云端确认答案仍保留。` });
    }
    this.task = task; this.attempt = current; this.policy = policy; this.drafts = drafts; this.submission = submission;
    this.unpersisted = this.unpersistedIds.size > 0;
    this.clock = { server: Date.parse(current ? current.serverNowUtc : task.serverNowUtc), local: Date.now() };
    this.invalidateReview(); this.setData({ ready: true }); this.render();
    if (!this.timer) this.timer = setInterval(() => this.tick(), 1000);
  },
  invalidateReview() { this.review = null; this.setData({ review: null, confirmed: false, savedOnly: false, reviewOffset: 0 }); },
  seconds() { return this.attempt && this.clock ? Math.max(0, Math.ceil((Date.parse(this.attempt.expiresAtUtc) - this.clock.server - (Date.now() - this.clock.local)) / 1000)) : 0; },
  editable() {
    try { return this.authorized() && this.data.ready && this.attempt && this.attempt.status === 0 && !this.attempt.submissionId && !this.data.busy && !this.store.pending() && this.consent && this.seconds() > 0 && !this.data.showCloud; }
    catch (failure) { if (!this.disposed) this.setData({ canEdit: false, error: message(failure) }); return false; }
  },
  tick() {
    if (!this.authorized() || !this.data.ready) return;
    const seconds = this.seconds();
    this.setData({ remaining: this.attempt ? `${Math.floor(seconds / 60)} 分 ${seconds % 60} 秒（显示估计，以服务器截止为准）` : '尚未开始', canEdit: !!this.editable() });
  },
  render() {
    if (!this.authorized() || !this.task || !this.data.ready) return;
    let pending;
    try { pending = this.writer.state(); }
    catch (failure) { this.setData({ ready: false, canEdit: false, error: message(failure) }); return; }
    let question = null;
    if (this.attempt) {
      this.index = Math.max(0, Math.min(this.index, this.attempt.questions.length - 1));
      const id = this.attempt.questions[this.index].id;
      const local = answer.projected(this.attempt, id, this.drafts);
      const shown = this.data.showCloud && local.conflict ? answer.projected(this.attempt, id, {}) : local;
      question = { id, type: shown.type, title: shown.title, stem: shown.stem, score: shown.score, options: shown.options, fields: shown.fields,
        text: shown.value.text || '', images: shown.value.images, dirty: local.dirty, conflict: local.conflict, savedAt: assessment.time(shown.savedAtUtc) };
    }
    this.setData({ task: { title: this.task.title, instructions: this.task.instructions, subject: this.task.subjectName, className: this.task.schoolClassName,
      startsAt: assessment.time(this.task.startsAtUtc), endsAt: assessment.time(this.task.endsAtUtc), duration: this.task.durationMinutes, canStart: this.task.canStart && !this.task.currentAttemptId },
      question, index: this.index, count: this.attempt ? this.attempt.questions.length : this.task.questionCount, consent: this.consent,
      imagePolicy: this.policy ? { bytes: this.policy.maxFileBytes, megabytes: (this.policy.maxFileBytes / 1048576).toFixed(1), count: this.policy.maxImagesPerResponse,
        types: this.policy.allowedContentTypes.map((type) => ({ 'image/jpeg': 'JPEG', 'image/png': 'PNG', 'image/webp': 'WebP' }[type] || type)).join('、') } : null,
      draftCount: Object.keys(this.drafts).length, unpersisted: this.unpersisted, pending: pending ? { ...pending, label: { start: '开始测评', response: '保存本题', image: '上传图片', submit: '交卷' }[pending.kind] } : null, submission: this.submission ? { id: this.submission.id,
        cause: this.submission.cause === 1 ? '截止封存' : '手动提交', effectiveAt: assessment.time(this.submission.effectiveAtUtc), sealedAt: assessment.time(this.submission.sealedAtUtc) } : null });
    this.tick();
    if (Object.keys(this.drafts).length || this.store.pending()) { if (typeof wx.enableAlertBeforeUnload === 'function') wx.enableAlertBeforeUnload({ message: '有未保存草稿或待核对操作，请先保存或查询回执。' }); }
    else if (typeof wx.disableAlertBeforeUnload === 'function') wx.disableAlertBeforeUnload();
  },
  async allowStorage() {
    if (!this.authorized() || this.data.busy) return;
    if (await ask('本机将保留未保存文字、选择、填空和待确认请求；图片复制到本应用专用路径。逻辑保留 24 小时，关闭时不保证物理擦除，共享设备须谨慎。退出尝试清除正文，清理失败会提示。只有服务器确认的答案可跨设备恢复。是否允许本机暂存后继续编辑？')) {
      if (this.authorized()) { this.consent = true; this.render(); }
    }
  },
  start() { return this.run(async () => {
    if (!this.authorized() || !this.task || this.task.currentAttemptId || !this.task.canStart || this.store.pending()) return;
    if (!await ask(`确认开始本测评？时限 ${this.task.durationMinutes} 分钟，开始后由服务器计时，退出不会暂停。`)) return;
    await this.writer.begin('start', {}); await this.accept(await this.writer.send());
  }); },
  question(event) {
    const direction = Number(event.currentTarget.dataset.direction);
    if (!this.authorized() || this.data.busy || !this.attempt || ![1, -1].includes(direction)) return;
    this.clearImages(); this.index = Math.max(0, Math.min(this.index + direction, this.attempt.questions.length - 1)); this.setData({ showCloud: false }); this.render();
  },
  change(event) {
    if (!this.editable()) return;
    const question = this.attempt.questions[this.index]; const local = answer.clone(answer.edit(this.attempt, question.id, this.drafts));
    const field = event.currentTarget.dataset.field;
    if (field === 'choice' && question.type < 3) local.value.selectedOptionKeys = question.type === 1 ? event.detail.value.slice() : event.detail.value ? [event.detail.value] : [];
    else if (field === 'text' && question.type === 4) local.value.text = event.detail.value;
    else if (field === 'blank' && question.type === 3 && question.responseFields.some((f) => f.id === event.currentTarget.dataset.id)) {
      const id = event.currentTarget.dataset.id; const existing = local.value.blanks.find((b) => b.blankId === id);
      if (existing) existing.text = event.detail.value; else local.value.blanks.push({ blankId: id, text: event.detail.value });
    } else return;
    this.keepDraft(local, question);
  },
  keepDraft(local, question) {
    try { local.value = answer.response(local.value, question); this.drafts[question.id] = local; this.unpersistedIds.add(question.id); this.invalidateReview(); this.store.saveDraft(local); this.unpersistedIds.delete(question.id); this.setData({ error: '' }); }
    catch (failure) { this.setData({ error: message(failure) }); }
    this.unpersisted = this.unpersistedIds.size > 0;
    this.render();
  },
  clearChoices() {
    if (!this.editable()) return;
    const question = this.attempt.questions[this.index]; if (question.type > 2) return;
    const local = answer.clone(answer.edit(this.attempt, question.id, this.drafts)); local.value.selectedOptionKeys = []; this.keepDraft(local, question);
  },
  toggleCloud() { if (!this.authorized() || this.data.busy) return; this.setData({ showCloud: !this.data.showCloud }); this.render(); },
  chooseLocal() { return this.run(async () => {
    if (!this.authorized() || !this.attempt || this.store.pending()) return;
    const question = this.attempt.questions[this.index]; const local = this.drafts[question.id]; if (!local) return;
    if (!await ask('确认已比较当前云端答案，并选择保留本机答案作为后续保存内容？这只更新本机基准，不会立即覆盖云端。')) return;
    if (!this.authorized()) return;
    local.baseRevisionId = answer.headId(this.attempt, question.id); this.store.saveDraft(local); this.invalidateReview(); this.setData({ showCloud: false });
  }); },
  discard() { return this.run(async () => {
    if (!this.authorized() || !this.attempt || this.store.pending()) return;
    const question = this.attempt.questions[this.index];
    if (!await ask('确认丢弃本题本机草稿，重新使用当前已读取的云端答案？不会删除云端历史或原图。')) return;
    if (!this.authorized()) return;
    this.store.removeDraft(this.attempt.id, question.id); delete this.drafts[question.id]; this.unpersistedIds.delete(question.id); this.unpersisted = this.unpersistedIds.size > 0; this.invalidateReview(); this.setData({ showCloud: false });
  }); },
  async saveOne(questionId) {
    if (!this.authorized() || !this.consent || this.store.pending()) throw error('PENDING_OPERATION');
    await this.readCloud(); const local = this.drafts[questionId]; if (!local) return;
    if (answer.conflict(this.attempt, local)) throw error('ANSWER_CONFLICT');
    const question = this.attempt.questions.find((q) => q.id === questionId);
    await this.writer.begin('response', { paperQuestionId: questionId, paperSnapshotVersion: this.attempt.paperSnapshotVersion,
      expectedResponseSetVersion: this.attempt.responseSetVersion, response: answer.response(local.value, question) }, { attemptId: this.attempt.id, questionId });
    await this.accept(await this.writer.send());
  },
  save() { const id = this.data.question && this.data.question.id; return this.run(async () => { if (id) await this.saveOne(id); }); },
  saveAll() { return this.run(async () => {
    if (!this.attempt) return;
    const ids = this.attempt.questions.filter((q) => this.drafts[q.id]).map((q) => q.id);
    for (const id of ids) await this.saveOne(id);
    await this.prepareInner();
  }); },
  async accept(receipt) {
    if (!receipt || !this.authorized()) return;
    const pending = this.store.pending(); if (!pending) throw error('PENDING_REQUIRED');
    await this.readCloud();
    if (pending.kind === 'response') {
      const local = this.drafts[pending.questionId]; const question = this.attempt.questions.find((q) => q.id === pending.questionId);
      if (local && answer.headId(this.attempt, question.id) === receipt.id && JSON.stringify(answer.response(local.value, question)) === JSON.stringify(answer.response(receipt.response, question))) {
        this.store.removeDraft(this.attempt.id, question.id); delete this.drafts[question.id]; this.unpersistedIds.delete(question.id); this.unpersisted = this.unpersistedIds.size > 0;
      }
    } else if (pending.kind === 'image') {
      const question = this.attempt.questions.find((q) => q.id === pending.questionId); if (!question || question.type !== 4) throw error('INVALID_RESPONSE');
      const local = answer.clone(answer.edit(this.attempt, question.id, this.drafts));
      if (!local.value.images.some((i) => i.attachmentId === receipt.id)) {
        const image = { attachmentId: receipt.id, rotationDegrees: 0 };
        if (pending.replaces) { const position = local.value.images.findIndex((i) => i.attachmentId === pending.replaces); if (position < 0) throw error('ANSWER_CONFLICT'); local.value.images[position] = image; }
        else { if (local.value.images.length >= this.policy.maxImagesPerResponse) throw error('IMAGE_INVALID'); local.value.images.push(image); }
      }
      local.value = answer.response(local.value, question); this.store.saveDraft(local); this.drafts[question.id] = local; this.unpersistedIds.delete(question.id); this.unpersisted = this.unpersistedIds.size > 0; this.store.removeImage(pending);
    } else if (pending.kind === 'start' && (!this.attempt || this.attempt.id !== receipt.id) || pending.kind === 'submit' && (!this.submission || this.submission.id !== receipt.id)) throw error('INVALID_RESPONSE');
    this.writer.acknowledge(pending.operationId); this.invalidateReview();
    this.setData({ notice: pending.kind === 'image' ? '图片上传已确认，只加入本机草稿；还须保存本题才能进入服务器答案。' : `已核对云端回执：${receipt.id}${pending.kind === 'submit' && receipt.id !== pending.operationId ? '（已有其他封存操作，未创建新的交卷）' : ''}` });
  },
  recover() { return this.run(async () => { const receipt = await this.writer.recover(); if (receipt) await this.accept(receipt); else this.setData({ notice: '未查询到已完成回执，不表示请求一定失败；可明确重试原操作，或先核对云端。' }); }); },
  retry() { return this.run(async () => { if (!this.consent) throw error('LOCAL_CONSENT_REQUIRED'); await this.accept(await this.writer.retry(this.policy)); }); },
  abandon() { return this.run(async () => {
    if (!await ask('仅关闭本机追踪并清理本次图片复制，不撤销服务器操作。原操作可能已成功，请先核对原回执。确认关闭后重新读取同一答卷？')) return;
    if (!this.authorized()) return;
    this.writer.abandon(); await this.readCloud();
  }); },
  addImage(event) { return this.run(async () => {
    if (!this.authorized() || !this.consent || !this.attempt || this.attempt.status !== 0 || this.seconds() <= 0 || this.store.pending()) return;
    const question = this.attempt.questions[this.index]; if (question.type !== 4) return;
    const replaces = event.currentTarget.dataset.id || null; const value = answer.projected(this.attempt, question.id, this.drafts).value;
    if (replaces && !value.images.some((i) => i.attachmentId === replaces) || !replaces && value.images.length >= this.policy.maxImagesPerResponse) throw error('IMAGE_INVALID');
    const ticket = this.version; const source = await pick(); const image = await images.inspect(wx, source, this.policy);
    if (!this.authorized() || ticket !== this.version) throw error('STALE_RESPONSE');
    const pending = await this.writer.begin('image', { paperQuestionId: question.id, contentHash: image.contentHash }, { attemptId: this.attempt.id, questionId: question.id, replaces, image });
    await images.retain(wx, source, pending.operationId, image, this.policy); this.store.imageRetained(pending.operationId);
    await this.accept(await this.writer.send(this.policy));
  }); },
  reselectImage() { return this.run(async () => {
    const pending = this.store.pending(); if (!pending || pending.kind !== 'image' || !this.consent) return;
    const receipt = await this.writer.recover(); if (receipt) { await this.accept(receipt); return; }
    const ticket = this.version; const source = await pick();
    if (!this.authorized() || ticket !== this.version) throw error('STALE_RESPONSE');
    await images.retain(wx, source, pending.operationId, pending.image, this.policy); this.store.imageRetained(pending.operationId);
    this.setData({ notice: '原图字节已核对并保留，可明确重试同一上传；尚未记为已上传或已保存答案。' });
  }); },
  imageAction(event) {
    if (!this.editable()) return;
    const question = this.attempt.questions[this.index]; const local = answer.clone(answer.edit(this.attempt, question.id, this.drafts));
    const id = event.currentTarget.dataset.id; const index = local.value.images.findIndex((i) => i.attachmentId === id); if (index < 0) return;
    const action = event.currentTarget.dataset.action;
    if (action === 'rotate') local.value.images[index].rotationDegrees = (local.value.images[index].rotationDegrees + 90) % 360;
    else if (action === 'remove') local.value.images.splice(index, 1);
    else if (action === 'up' && index > 0) [local.value.images[index - 1], local.value.images[index]] = [local.value.images[index], local.value.images[index - 1]];
    else if (action === 'down' && index + 1 < local.value.images.length) [local.value.images[index + 1], local.value.images[index]] = [local.value.images[index], local.value.images[index + 1]];
    else return;
    this.keepDraft(local, question);
  },
  async prepareInner() {
    if (!this.authorized() || !this.attempt || this.store.pending()) throw error('PENDING_OPERATION');
    await this.readCloud();
    const dto = await getApp().session.request('/api/app/education-assessment/preview-submission/' + this.attempt.id, 'POST');
    const preview = answer.preview(dto, this.attempt);
    if (!this.authorized()) return;
    this.review = { ...preview, paperSnapshotVersion: this.attempt.paperSnapshotVersion, responseSetVersion: this.attempt.responseSetVersion,
      exclusions: this.attempt.questions.filter((q) => this.drafts[q.id]).map((q) => ({ id: q.id, order: q.displayOrder, title: q.title, images: this.drafts[q.id].value.images.length })) };
    this.setData({ confirmed: false, savedOnly: false, reviewOffset: 0 }); this.renderReview();
  },
  prepare() { return this.run(() => this.prepareInner()); },
  renderReview() {
    if (!this.authorized() || !this.review) return;
    const r = this.review; const offset = this.data.reviewOffset;
    this.setData({ review: { canSubmit: r.canSubmit, total: r.questions.length, questions: r.questions.slice(offset, offset + 20),
      localExclusions: r.exclusions.slice(offset, offset + 20), exclusionTotal: r.exclusions.length, unlinked: r.unlinkedImages.slice(offset, offset + 20), unlinkedTotal: r.unlinkedImages.length,
      rowsTotal: Math.max(r.questions.length, r.exclusions.length, r.unlinkedImages.length), incomplete: r.questions.filter((q) => q.isUnanswered || q.unansweredBlankCount > 0).length,
      hasExclusions: !!r.exclusions.length || !!r.unlinkedImages.length || r.questions.some((q) => q.isUnanswered || q.unansweredBlankCount > 0) } });
  },
  reviewPage(event) {
    if (!this.authorized() || this.data.busy || !this.review) return;
    const direction = Number(event.currentTarget.dataset.direction); if (![1, -1].includes(direction)) return;
    const offset = this.data.reviewOffset + direction * 20;
    if (offset < 0 || offset >= this.data.review.rowsTotal) return; this.setData({ reviewOffset: offset }); this.renderReview();
  },
  confirm(event) { if (this.authorized() && !this.data.busy) this.setData({ confirmed: event.detail.value.includes('confirmed'), savedOnly: event.detail.value.includes('savedOnly') }); },
  submit() { return this.run(async () => {
    const review = this.review;
    if (!this.authorized() || !this.consent || !review || !review.canSubmit || !this.data.confirmed || this.store.pending() || this.seconds() <= 0 || this.data.review.hasExclusions && !this.data.savedOnly) return;
    if (!await ask(`确认封存当前服务器已保存的答案？本机 ${review.exclusions.length} 题草稿、${review.unlinkedImages.length} 个未关联上传及 ${this.data.review.incomplete} 题未完整作答均按核对清单排除；提交后不能继续修改原答卷。`)) return;
    if (!this.authorized() || this.review !== review) return;
    await this.writer.begin('submit', { paperSnapshotVersion: review.paperSnapshotVersion, expectedResponseSetVersion: review.responseSetVersion,
      confirmSavedOnly: this.data.review.hasExclusions, expectedPreparationFingerprint: review.fingerprint }, { attemptId: this.attempt.id });
    await this.accept(await this.writer.send());
  }); },
  clearImages() { this.imageVersion = (this.imageVersion || 0) + 1; if (this.files) { for (const path of this.files) getApp().session.releaseFile(path); this.files.clear(); } },
  closeImage() { this.clearImages(); this.setData({ imagePath: '' }); },
  showImage(event) { return this.run(async () => {
    const id = event.currentTarget.dataset.id; const image = this.data.question && this.data.question.images.find((i) => i.attachmentId === id);
    if (!this.authorized() || !image) return;
    this.clearImages(); const ticket = this.imageVersion;
    const filePath = await getApp().session.download(`/api/education/assessment/attempts/${this.attempt.id}/images/${id}`);
    if (!this.authorized() || ticket !== this.imageVersion) { getApp().session.releaseFile(filePath); return; }
    this.files.add(filePath); this.setData({ imagePath: filePath, imageRotation: image.rotationDegrees });
  }); },
  async reauthenticate() {
    if (this.data.busy) return; this.reauth = true;
    try { await getApp().session.login(); if (this.visible) await this.load(); }
    catch (failure) { if (this.visible) this.setData({ error: message(failure) }); }
    finally { this.reauth = false; }
  },
  results() { if (this.authorized()) wx.navigateTo({ url: `/pages/assessment-result/index?schoolId=${this.schoolId}&id=${this.id}` }); },
});
