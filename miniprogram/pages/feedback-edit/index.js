const feedback = require('../../utils/feedback');
const { isGuid } = require('../../utils/assessment');
const command = require('../../utils/feedback-command');
const message = require('../../utils/messages');
const ask = (text) => new Promise((resolve) => wx.showModal({ title: '请核对', content: text, success: (answer) => resolve(answer.confirm), fail: () => resolve(false) }));

Page({
  data: { ready: false, busy: false, form: null, target: null, record: null, gates: [], kinds: feedback.kinds, pending: null, canRetry: false,
    preview: null, confirmed: false, reason: '', dirty: false, conflict: false, cloudText: '', error: '', notice: '', privateOpen: false,
    observationText: '', observationDate: '', observationTime: '', observations: [], observationTotal: 0, observationOffset: 0,
    phrases: [], phraseTotal: 0, phraseOffset: 0, phraseText: '', phrase: null, phraseDirty: false, phraseConflict: false, phraseCloudText: '', includeArchived: false },
  onLoad(query) {
    this.id = query.id; this.schoolId = query.schoolId; this.query = query; this.version = 0; this.form = null; this.baseline = ''; this.base = null; this.owner = null; this.writer = null; this.privateReason = '';
    this.observationText = ''; this.observationClock = feedback.localParts(); this.phrase = null; this.phraseText = ''; this.phraseBaseline = ''; this.observations = []; this.phrases = [];
    this.unsubscribe = getApp().subscribe((state) => {
      const changedOwner = this.owner && state.context && feedback.actor(state) !== this.owner;
      if (!state.authenticated || changedOwner) {
        this.version++;
        if (changedOwner || (state.termination !== 'expired' && !(this.reauth && state.termination !== 'logout'))) this.clearPrivate();
        if (!this.disposed) this.setData({ ready: false, busy: false, form: null, target: null, record: null, preview: null, pending: null, cloudText: '', reason: '', confirmed: false,
          privateOpen: false, observations: [], phrases: [], observationText: '', phraseText: '', phrase: null, phraseCloudText: '', error: changedOwner ? '账号已变化，请返回工作区选择本人记录。' : '当前会话不可用。若因到期中断，可重新验证后继续核对本机编辑。' });
      } else if (!state.context || !feedback.allowed(state, 'teacher', this.schoolId) || this.recipient && !feedback.teachingAllowed(state, this.schoolId, this.recipient.teachingAssignmentId)) {
        if (state.termination === 'forbidden' || state.context) this.clearPrivate();
        if (!this.disposed) this.setData({ ready: false, form: null, target: null, record: null, preview: null, cloudText: '', reason: '', confirmed: false,
          privateOpen: false, observations: [], phrases: [], observationText: '', phraseText: '', phrase: null, phraseCloudText: '' });
      }
    });
  },
  onShow() { this.visible = true; return this.load(); },
  onHide() { this.visible = false; this.version++; this.observations = []; this.phrases = []; this.setData({ ready: false, busy: false, form: null, target: null, record: null, preview: null, cloudText: '', reason: '', confirmed: false,
    privateOpen: false, observations: [], phrases: [], observationText: '', phraseText: '', phrase: null, phraseCloudText: '' }); },
  onUnload() { this.disposed = true; this.visible = false; this.version++; this.unsubscribe(); this.clearPrivate(); },
  clearPrivate() { this.form = null; this.base = null; this.baseline = ''; this.preview = null; this.target = null; this.record = null; this.gates = []; this.privateReason = '';
    this.observationText = ''; this.phraseText = ''; this.phraseBaseline = ''; this.phrase = null; this.observations = []; this.phrases = []; if (this.writer) this.writer.dropPayload(); },
  authorized() { return this.visible && !this.disposed && feedback.actor(getApp().session.getState()) === this.owner && feedback.allowed(getApp().session.getState(), 'teacher', this.schoolId) &&
    (!this.recipient || feedback.teachingAllowed(getApp().session.getState(), this.schoolId, this.recipient.teachingAssignmentId)); },
  warning() {
    if (this.data.dirty || this.data.pending || this.privateReason || this.observationText || this.phraseText !== this.phraseBaseline) { if (typeof wx.enableAlertBeforeUnload === 'function') wx.enableAlertBeforeUnload({ message: '有未保存文字或待核对操作；离开可能丢失本次内存编辑。' }); }
    else if (typeof wx.disableAlertBeforeUnload === 'function') wx.disableAlertBeforeUnload();
  },
  sync() {
    if (!this.authorized()) return;
    const state = this.writer ? this.writer.state() : { pending: null, canRetry: false };
    this.setData({ form: this.form ? JSON.parse(JSON.stringify(this.form)) : null, dirty: !!this.form && JSON.stringify(this.form) !== this.baseline,
      pending: state.pending, canRetry: state.canRetry, gates: (this.gates || []).map((g) => ({ ...g, selected: !!this.form && this.form.assessmentTaskIds.includes(g.taskId) })) });
    if (this.data.privateOpen) this.setData({ observationText: this.observationText, observationDate: this.observationClock.date, observationTime: this.observationClock.time,
      phraseText: this.phraseText, phrase: this.phrase, phraseDirty: this.phraseText !== this.phraseBaseline });
    this.warning();
  },
  async load(discard = false) {
    if (!this.visible || this.data.busy || !isGuid(this.id) || !isGuid(this.schoolId)) return;
    const ticket = ++this.version; this.setData({ busy: true, error: '', ready: false, preview: null });
    try {
      await getApp().session.refreshWorkspace();
      const state = getApp().session.getState(); const owner = feedback.actor(state);
      if (!owner || (this.owner && this.owner !== owner) || !feedback.allowed(state, 'teacher', this.schoolId)) throw Object.assign(new Error('FORBIDDEN'), { code: 'FORBIDDEN' });
      this.owner = owner;
      const context = state.context;
      const record = await getApp().session.request(`/api/app/education-feedback/my-workspace/${this.id}`);
      const recipient = record ? { teachingAssignmentId: record.teachingAssignmentId, studentEnrollmentId: record.studentEnrollmentId, schoolMembershipId: record.schoolMembershipId } :
        { teachingAssignmentId: this.query.teachingId, studentEnrollmentId: this.query.enrollmentId, schoolMembershipId: this.query.membershipId };
      if (Object.values(recipient).some((id) => !isGuid(id))) throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' });
      const [target, gates] = await Promise.all([
        getApp().session.request('/api/app/education-feedback/target?' + feedback.query(recipient)),
        getApp().session.request('/api/app/education-feedback/assessment-list?' + feedback.query(recipient)),
      ]);
      feedback.targetContext(target, this.schoolId, recipient);
      if (!feedback.teachingAllowed(state, this.schoolId, recipient.teachingAssignmentId)) throw Object.assign(new Error('FORBIDDEN'), { code: 'FORBIDDEN' });
      if (!this.visible || this.disposed || ticket !== this.version || getApp().session.getState().context !== context || !this.authorized()) return;
      if (!this.writer) this.writer = command(wx, getApp().session, getApp().configuration, this.id, this.schoolId, recipient);
      const content = record && ((record.draft && record.draft.content) || (record.published && record.published.content));
      const dirty = this.form && JSON.stringify(this.form) !== this.baseline;
      if (!dirty || discard) { this.form = feedback.formFrom(content); this.baseline = JSON.stringify(this.form); this.base = { stamp: record && record.concurrencyStamp, revisionId: record && record.currentRevision.id }; }
      if (discard) this.privateReason = '';
      const conflict = this.base.revisionId !== (record && record.currentRevision.id) || this.base.stamp !== (record && record.concurrencyStamp);
      this.target = target; this.record = record; this.recipient = recipient; this.gates = gates; this.preview = null;
      this.setData({ ready: true, target, record: record ? { version: record.currentRevision.revisionNumber, status: feedback.visibility[record.visibility],
        hasPublished: !!record.published || record.visibility === 3, canWithdraw: !!record.published, draftCurrent: record.currentRevision.kind === 0, opened: !!record.publishedVersionOpenedAtUtc } : null,
        conflict, cloudText: conflict && content ? content.text : '', confirmed: false, reason: this.privateReason });
      this.sync();
    } catch (error) { if (this.visible && ticket === this.version) this.setData({ error: message(error) }); }
    finally { if (!this.disposed && ticket === this.version) this.setData({ busy: false }); }
  },
  change(event) {
    if (!this.authorized() || !this.form || this.data.busy || this.data.pending) return;
    const name = event.currentTarget.dataset.field;
    if (!['context', 'text', 'date', 'time', 'endDate', 'endTime', 'kind'].includes(name)) return;
    this.form[name] = name === 'kind' ? Number(event.detail.value) : event.detail.value;
    this.preview = null; this.setData({ preview: null, confirmed: false }); this.sync();
  },
  tasks(event) {
    if (!this.authorized() || !this.form || this.data.busy || this.data.pending) return;
    const ids = event.detail.value;
    if (ids.length > 10 || ids.some((id) => !this.gates.some((g) => g.taskId === id))) { this.setData({ error: '最多关联 10 项当前学生的测评。' }); return; }
    this.form.assessmentTaskIds = ids.slice(); this.preview = null; this.setData({ preview: null, confirmed: false }); this.sync();
  },
  confirm(event) { this.setData({ confirmed: event.detail.value.includes('confirmed') }); },
  reason(event) { if (!this.authorized() || this.data.busy || this.data.pending) return; this.privateReason = event.detail.value; this.setData({ reason: this.privateReason }); this.warning(); },
  async act(action) {
    if (!this.authorized() || this.data.busy) return null;
    const ticket = this.version; this.setData({ busy: true, error: '', notice: '' });
    try { const value = await action(); if (ticket === this.version && this.authorized()) return value; }
    catch (error) { if (ticket === this.version && this.visible) this.setData({ error: error.status === 409 ? '云端版本已变化，未覆盖新版本。请先查询回执，再比较并重新读取。' : message(error) }); }
    finally { if (!this.disposed && ticket === this.version) { this.setData({ busy: false }); this.sync(); } }
    return null;
  },
  async saved(receipt) {
    if (!receipt || !this.authorized()) return;
    if (receipt.phraseId) { this.phrase = null; this.phraseText = ''; this.phraseBaseline = ''; this.setData({ phraseConflict: false, phraseCloudText: '' }); await this.loadPrivate('phrases'); this.sync(); return; }
    if (!receipt.feedbackId) { feedback.observation(receipt, this.schoolId, this.recipient); this.observationText = ''; await this.loadPrivate('observations'); this.sync(); return; }
    this.form = null; this.baseline = ''; await this.load(true);
    if (this.authorized()) this.setData({ notice: '已核对服务器回执：' + receipt.id });
  },
  async save() {
    if (!this.authorized() || !this.data.ready || !this.form || !this.writer || this.data.pending || this.data.conflict) return;
    const receipt = await this.act(() => this.writer.start('draft', { ...this.recipient, concurrencyStamp: this.base.stamp || null, expectedRevisionId: this.base.revisionId || null, content: feedback.content(this.form) }));
    await this.saved(receipt);
  },
  async previewPublish() {
    if (!this.record || !this.writer || this.data.dirty || this.data.pending || this.record.currentRevision.kind !== 0) return;
    const preview = await this.act(() => getApp().session.request(`/api/app/education-feedback/preview-publish/${this.id}`, 'POST'));
    if (!preview) return;
    const record = preview.feedback;
    if (record.id !== this.id || !record.draft || record.draft.id !== this.base.revisionId || record.studentEnrollmentId !== this.recipient.studentEnrollmentId || record.schoolMembershipId !== this.recipient.schoolMembershipId || record.teachingAssignmentId !== this.recipient.teachingAssignmentId) { this.setData({ error: '预览版本已变化，请重新读取并核对草稿。' }); return; }
    this.preview = preview;
    this.setData({ preview: { name: record.studentName, accountName: record.accountName, className: record.className, subjectName: record.subjectName,
      text: record.draft.content.text, context: record.draft.content.context, version: record.draft.revisionNumber,
      waiting: preview.assessments.some((g) => !g.isReviewOpen), assessments: preview.assessments }, confirmed: false });
  },
  async publish() {
    if (!this.authorized() || !this.data.ready || !this.preview || !this.data.record || !this.data.confirmed || this.data.dirty || this.data.pending) return;
    if (this.data.record.hasPublished && !this.data.reason.trim()) { this.setData({ error: '更正或重新发布必须填写原因。' }); return; }
    if (!await ask('确认将预览中的确切版本发送给这一个学生？关联测评未全部开放时，正文仍等待完整讲评。')) return;
    const source = this.preview;
    const receipt = await this.act(() => this.writer.start('publish', { concurrencyStamp: source.feedback.concurrencyStamp, expectedDraftRevisionId: source.feedback.draft.id,
      previewFingerprint: source.previewFingerprint, confirmRecipientAndContent: true, reason: this.data.reason || null }));
    await this.saved(receipt);
  },
  async withdraw() {
    if (!this.authorized() || !this.data.ready || !this.record || !this.record.published || this.data.dirty || this.data.pending) return;
    if (!this.data.reason.trim()) { this.setData({ error: '撤回必须填写原因。' }); return; }
    if (!await ask('撤回后停止后续正文读取，保留历史记录；无法收回已经看到或复制的内容。确定撤回？')) return;
    const receipt = await this.act(() => this.writer.start('withdraw', { concurrencyStamp: this.record.concurrencyStamp, expectedPublishedRevisionId: this.record.published.id, reason: this.data.reason }));
    await this.saved(receipt);
  },
  async recover() {
    const receipt = await this.act(() => this.writer.recover());
    if (receipt) await this.saved(receipt); else if (this.authorized()) this.setData({ notice: '尚未查询到原操作回执，不代表操作一定失败。只可明确重试原请求，或先核对云端。' });
  },
  async retry() { await this.saved(await this.act(() => this.writer.retry())); },
  async reload() {
    if ((this.data.dirty || this.data.pending) && !await ask('将重新读取云端。未保存文字不会自动合并；待确认的操作仍可能已经成功，先查询回执。确定继续？')) return;
    if (!this.data.pending) return this.load(true);
    this.setData({ notice: '请先查询原回执或明确关闭追踪；不会生成新的请求替代旧操作。' });
  },
  async abandon() {
    if (!await ask('只清除本机追踪标识，不撤销服务器操作。原请求可能已成功；将重新核对同一记录，未保存文字可能丢失。确定继续？')) return;
    const kind = this.writer.state().pending?.kind;
    const done = await this.act(async () => { this.writer.abandon(); return true; });
    if (done) { if (['observation', 'phrase'].includes(kind)) { await this.loadPrivate(kind === 'phrase' ? 'phrases' : 'observations'); return; } this.form = null; return this.load(true); }
  },
  async openPrivate() { if (!this.authorized() || this.data.busy) return; this.setData({ privateOpen: true }); this.sync(); await this.loadPrivate('observations'); await this.loadPrivate('phrases'); },
  async loadPrivate(kind, direction = 0) {
    if (!this.authorized() || !this.data.privateOpen || this.data.busy || !['observations', 'phrases'].includes(kind)) return;
    const field = kind === 'observations' ? 'observationOffset' : 'phraseOffset', total = kind === 'observations' ? this.data.observationTotal : this.data.phraseTotal;
    const offset = direction ? this.data[field] + direction * 20 : 0; if (offset < 0 || direction > 0 && offset >= total) return;
    this[kind] = []; this.setData({ [kind]: [] });
    const rows = await this.act(async () => {
      const route = kind === 'observations' ? `/api/app/education-feedback/observations-for-school/${this.schoolId}?${feedback.query(this.recipient)}` :
        `/api/app/education-feedback/phrases?${feedback.phraseScope(this.schoolId, this.recipient.teachingAssignmentId)}&includeArchived=${this.data.includeArchived}`;
      const dto = await getApp().session.request(`${route}&SkipCount=${offset}&MaxResultCount=20`);
      if (!dto || !Array.isArray(dto.items) || dto.items.length > 20) throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' });
      return feedback.page(dto, (row) => kind === 'observations' ? feedback.observation(row, this.schoolId, this.recipient) : feedback.phrase(row));
    });
    if (!rows || !this.authorized()) return;
    this[kind] = rows.items; this.setData({ [kind]: rows.items, [field]: offset, [kind === 'observations' ? 'observationTotal' : 'phraseTotal']: rows.total });
  },
  privatePage(e) { const d = Number(e.currentTarget.dataset.direction); if ([-1, 1].includes(d)) return this.loadPrivate(e.currentTarget.dataset.kind, d); },
  privateChange(e) {
    if (!this.authorized() || this.data.busy || this.data.pending) return;
    const field = e.currentTarget.dataset.field;
    if (field === 'observationText') this.observationText = e.detail.value;
    else if (field === 'phraseText') this.phraseText = e.detail.value;
    else if (field === 'observationDate') this.observationClock.date = e.detail.value;
    else if (field === 'observationTime') this.observationClock.time = e.detail.value;
    this.sync();
  },
  async saveObservation() {
    if (!this.authorized() || !this.data.ready || this.data.pending || !this.writer) return;
    const receipt = await this.act(() => this.writer.start('observation', { ...this.recipient, ...feedback.observationContent(this.observationText, this.observationClock.date, this.observationClock.time) }));
    await this.saved(receipt);
  },
  async selectObservation(e) {
    const row = this.observations.find((r) => r.id === e.currentTarget.dataset.id);
    if (!this.authorized() || this.data.busy || this.data.pending || !this.form || !row) return;
    const ticket = this.version;
    if (!await ask('只把当前选择的这一条观察复制到学生评语编辑区，替换当前正文。请删去内部判断、核对允许公开的内容和关联测评，再保存、预览及确认发布；原观察仍仅本人可见。')) return;
    if (ticket !== this.version || !this.authorized()) return;
    this.form.text = row.text; this.form.selectedObservationId = row.id; this.preview = null; this.setData({ preview: null, confirmed: false }); this.sync();
  },
  async editPhrase(e) {
    if (!this.authorized() || this.data.busy || this.data.pending) return;
    const id = e.currentTarget.dataset.id;
    if (this.phraseText !== this.phraseBaseline && !await ask('放弃未保存的常用语编辑，再读取选中常用语的当前版本？学生评语正文保持当前编辑。')) return;
    if (!isGuid(id)) return;
    const row = await this.act(async () => feedback.phrase(await getApp().session.request(`/api/app/education-feedback/phrase/${id}?${feedback.phraseScope(this.schoolId, this.recipient.teachingAssignmentId)}`), id));
    if (!row || !this.authorized()) return;
    this.phrase = row; this.phraseText = row.text; this.phraseBaseline = row.text; this.setData({ phraseConflict: false, phraseCloudText: '' }); this.sync();
  },
  async newPhrase() {
    if (!this.authorized() || this.data.busy || this.data.pending || this.phraseText !== this.phraseBaseline && !await ask('放弃当前未保存常用语编辑，开始一条新的个人常用语？')) return;
    if (!this.authorized()) return; this.phrase = null; this.phraseText = ''; this.phraseBaseline = ''; this.setData({ phraseConflict: false, phraseCloudText: '' }); this.sync();
  },
  async savePhrase() {
    if (!this.authorized() || !this.data.ready || this.data.pending || this.data.phraseConflict || !this.writer || !this.phraseText.trim() || this.phraseText.length > 4000) return;
    const receipt = await this.act(async () => {
      const phraseId = this.phrase?.id || await feedback.uuid(wx);
      return this.writer.start('phrase', { phraseId, schoolId: this.schoolId, teachingAssignmentId: this.recipient.teachingAssignmentId,
        concurrencyStamp: this.phrase?.stamp || null, expectedRevisionId: this.phrase?.revisionId || null, text: this.phraseText, isArchived: this.phrase?.archived || false });
    });
    await this.saved(receipt);
  },
  async comparePhrase() {
    if (!this.phrase || !this.authorized() || this.data.busy) return;
    const id = this.phrase.id;
    const value = await this.act(async () => feedback.phrase(await getApp().session.request(`/api/app/education-feedback/phrase/${this.phrase.id}?${feedback.phraseScope(this.schoolId, this.recipient.teachingAssignmentId)}`), id));
    if (!value || !this.authorized()) return;
    this.setData({ phraseConflict: value.revisionId !== this.phrase.revisionId || value.stamp !== this.phrase.stamp, phraseCloudText: value.text, notice: '已读取当前云端常用语供比较；本机文字和原基准没有改写。先核对原回执，关闭追踪后可明确重新读取。' });
  },
  async archivePhrase(e) {
    if (!this.authorized() || this.data.busy || this.data.pending || this.phraseText !== this.phraseBaseline) return;
    const row = this.phrases.find((p) => p.id === e.currentTarget.dataset.id); if (!row) return; const ticket = this.version;
    if (!await ask(row.archived ? '恢复这条本人常用语？原已保存的学生评语不随常用语变化。' : '归档后不再出现在常用选择中，保留修订和原学生评语。确定归档？')) return;
    if (!this.authorized() || ticket !== this.version) return;
    await this.saved(await this.act(() => this.writer.start('phrase', { phraseId: row.id, schoolId: this.schoolId, teachingAssignmentId: this.recipient.teachingAssignmentId,
      concurrencyStamp: row.stamp, expectedRevisionId: row.revisionId, text: row.text, isArchived: !row.archived })));
  },
  async usePhrase(e) {
    if (!this.authorized() || this.data.busy || this.data.pending || !this.form) return; const id = e.currentTarget.dataset.id;
    if (!this.phrases.some((p) => p.id === id && !p.archived)) return;
    if (!await ask('读取这条常用语的当前版本，替换当前学生评语正文？请结合这一个学生修改，核对关联测评后再保存、预览并确认发布。')) return;
    const value = await this.act(async () => feedback.phrase(await getApp().session.request(`/api/app/education-feedback/phrase/${id}?${feedback.phraseScope(this.schoolId, this.recipient.teachingAssignmentId)}`), id));
    if (!value || !this.authorized() || !this.form) return;
    if (value.archived) { this.setData({ error: '这条常用语已归档，请刷新列表。' }); return; }
    this.form.text = value.text; this.form.selectedObservationId = null; this.preview = null; this.setData({ preview: null, confirmed: false }); this.sync();
  },
  archived(e) { if (!this.authorized() || this.data.busy || this.data.pending) return; this.setData({ includeArchived: e.detail.value.includes('archived') }); return this.loadPrivate('phrases'); },
  async reauthenticate() {
    if (this.data.busy) return; this.reauth = true;
    try { await getApp().session.login(); if (this.visible) await this.load(); }
    catch (error) { if (this.visible) this.setData({ error: message(error) }); }
    finally { this.reauth = false; }
  },
  continueOnWeb() {
    if (!this.authorized()) return;
    if (!this.record || this.data.dirty || this.data.pending || this.observationText || this.phraseText !== this.phraseBaseline) { this.setData({ error: '请先保存云端草稿、私有观察或常用语并核对回执，再到网页继续；未保存文字不随链接传递。' }); return; }
    const url = getApp().session.portalUrl('/education/feedback?feedbackId=' + this.id);
    if (!url) { this.setData({ error: '尚未配置教师网页地址，请联系平台管理员。' }); return; }
    wx.setClipboardData({ data: url });
  },
});
