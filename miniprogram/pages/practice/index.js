const practice = require('../../utils/practice');
const createStore = require('../../utils/practice-store');
const createCommand = require('../../utils/practice-command');
const { actor } = require('../../utils/feedback');
const { isGuid } = require('../../utils/assessment');
const message = require('../../utils/messages');
const clone = (v) => JSON.parse(JSON.stringify(v));
const ask = (content) => new Promise((resolve) => wx.showModal({ title: '核对本轮复练', content, success: (r) => resolve(!!r.confirm), fail: () => resolve(false) }));
const choose = () => new Promise((resolve, reject) => wx.chooseMedia({ count: 1, mediaType: ['image'], sourceType: ['album', 'camera'], success: (r) => r.tempFiles?.[0]?.tempFilePath ? resolve(r.tempFiles[0].tempFilePath) : reject(practice.error('IMAGE_UNAVAILABLE')), fail: () => reject(practice.error('IMAGE_UNAVAILABLE')) }));
Page({
  data: { detail: null, form: null, localPreview: null, preview: null, consent: false, share: false, practiceOnly: false, pending: null, receipt: '', busy: false, error: '', notice: '', unpersisted: false, imagePath: '', imageRotation: 0 },
  onLoad(q) {
    this.id = q.id; this.schoolId = q.schoolId; this.version = 0; this.files = new Set(); this.imageVersion = 0;
    const s = getApp().session.getState(); this.generation = s.generation; this.context = s.context;
    this.unsubscribe = getApp().subscribe((s) => {
      const generation = this.generation !== s.generation, context = this.context !== s.context;
      this.generation = s.generation; this.context = s.context;
      const lost = this.owner && actor(s) && this.owner !== actor(s) || s.context && !practice.allowed(s, this.schoolId) || !s.authenticated;
      if (generation || context || lost || !s.context) this.clearView();
      if (generation || lost) { this.version++; this.local = null; this.consent = false; let cleanupFailed = false;
        try { this.store?.clearPrivate(); } catch (_) { cleanupFailed = true; }
        if (!this.disposed) this.setData({ error: '身份或权限已变化，本机复练内容已停止显示，请重新核对本人账号。' + (cleanupFailed ? ' 本机内容清理未确认，共享设备请谨慎。' : '') }); }
    });
  },
  onShow() { this.visible = true; if (!this.receiptOnly) return this.load(false); },
  onHide() { this.visible = false; this.version++; this.clearView(); },
  onUnload() { this.visible = false; this.disposed = true; this.version++; this.unsubscribe(); this.local = null; this.prepared = null; this.clearImages(); },
  authorized() { const s = getApp().session.getState(); return !this.disposed && this.visible && this.owner === actor(s) && practice.allowed(s, this.schoolId); },
  clearImages() { this.imageVersion++; for (const file of this.files) getApp().session.releaseFile(file); this.files.clear(); },
  clearView() { this.detail = null; this.prepared = null; this.clearImages(); if (!this.disposed) this.setData({ detail: null, form: null, localPreview: null, preview: null, pending: null, receipt: '', editing: false, consent: false, share: false, practiceOnly: false, imagePath: '', imageRotation: 0 }); },
  invalidate() { this.prepared = null; this.setData({ preview: null, share: false, practiceOnly: false }); },
  sync() {
    if (!this.authorized() || !this.writer) return;
    const editable = this.detail?.sourceAvailable && !this.detail.submitted && this.local;
    this.setData({ detail: this.detail, form: editable ? practice.form(this.detail.question, this.local) : null, localPreview: !editable && this.local ? clone(this.local) : null,
      consent: !!this.consent, pending: this.writer.state(), editing: !!(editable && this.consent && !this.working && !this.store.pending()), unpersisted: !!this.unpersisted });
    if (wx.enableAlertBeforeUnload && (this.local || this.store.pending())) wx.enableAlertBeforeUnload({ message: '本机复练草稿不等于上交，请核对暂存和原操作回执。' });
    else if (wx.disableAlertBeforeUnload) wx.disableAlertBeforeUnload();
  },
  async run(work) {
    if (!this.authorized() || this.working) return null;
    this.working = true; const ticket = this.version, context = getApp().session.getState().context; this.setData({ busy: true, error: '', notice: '' });
    try { const r = await work(); if (ticket === this.version && this.authorized() && context === getApp().session.getState().context) return r; }
    catch (e) { if (ticket === this.version && this.authorized()) this.setData({ error: e.status === 409 ? '本轮核对条件已变化。请先查询原回执，核对当前上交状态后重新预览；原草稿和操作编号保留。' : message(e) }); }
    finally { this.working = false; if (!this.disposed) { this.setData({ busy: false }); if (ticket === this.version) { try { this.sync(); } catch (e) { this.setData({ error: message(e) }); } } } }
    return null;
  },
  async load(allowPendingBody = true) {
    if (!this.visible || this.working) return;
    if (![this.id, this.schoolId].every(isGuid)) { this.setData({ error: '复练链接无法核对，请回当前学校列表选择。' }); return; }
    this.receiptOnly = false; this.working = true; const ticket = ++this.version; this.clearView(); this.setData({ busy: true, error: '' });
    try {
      const app = getApp(); await app.session.refreshWorkspace(); const s = app.session.getState(), context = s.context;
      if (!actor(s) || this.owner && this.owner !== actor(s) || !practice.allowed(s, this.schoolId)) throw practice.error('FORBIDDEN'); this.owner = actor(s);
      if (!this.store) { this.store = createStore(wx, app.configuration, s.context, this.schoolId, this.id); this.writer = createCommand(wx, app.session, this.store, this.schoolId, this.id, null, () => this.authorized()); }
      if (!allowPendingBody && this.store.pending()) { this.sync(); this.setData({ notice: '存在待确认原操作，请先查回执。明确读取本轮详情时才可能提供当前获准参考反馈。' }); return; }
      const dto = await app.session.request(`/api/app/education-assessment/my-practice-for-school?practiceId=${this.id}&schoolId=${this.schoolId}`);
      if (ticket !== this.version || !this.authorized() || context !== app.session.getState().context) return;
      this.detail = practice.detail(dto, this.schoolId, this.id);
      if (!this.local && !this.detail.submitted) this.local = this.store.draft();
      if (this.local && this.detail.question) this.local = practice.response(this.local, this.detail.question);
      if (this.detail.submitted) { this.local = null; this.store.removeDraft(); this.consent = false; }
      this.sync();
    } catch (e) { if (this.visible && ticket === this.version) this.setData({ error: message(e) }); }
    finally { this.working = false; if (!this.disposed) { this.setData({ busy: false }); if (ticket === this.version) { try { this.sync(); } catch (e) { this.setData({ error: message(e) }); } } } }
  },
  canEdit() { return this.authorized() && !this.working && this.consent && this.detail?.sourceAvailable && !this.detail.submitted && this.local && !this.store.pending(); },
  async allowLocal() {
    if (!this.authorized() || this.working || !this.detail?.sourceAvailable || this.detail.submitted || this.store.pending()) return; const ticket = this.version;
    if (!await ask('允许在本机按账号和本轮复练暂存自写答案、选择、填空、图片引用和原重试请求，后续使用时按24小时有效期清理。未确认原图可能产生本应用副本。本机草稿不是云端笔记，也不是上交；共享设备请谨慎。')) return;
    if (ticket !== this.version || !this.authorized() || this.working) return;
    this.consent = true; this.local ||= practice.empty(this.detail.question.type); this.persist(); this.sync();
  },
  persist() { try { this.store.saveDraft(this.local); this.unpersisted = false; } catch (e) { this.unpersisted = true; this.setData({ error: message(e) + ' 当前输入仍在页面内存，请勿离开。' }); } },
  saveLocal() { if (this.authorized() && this.consent && this.local && !this.working) { this.persist(); this.sync(); } },
  text(e) { if (!this.canEdit() || this.detail.question.type !== 4) return; this.local.text = e.detail.value; this.changed(); },
  choices(e) { if (!this.canEdit() || ![0, 1, 2].includes(this.detail.question.type)) return; this.local.selectedOptionKeys = this.detail.question.type === 1 ? e.detail.value : [e.detail.value]; this.changed(); },
  blank(e) { const id = e.currentTarget.dataset.id; if (!this.canEdit() || this.detail.question.type !== 3 || !this.detail.question.responseFields.some((f) => f.id === id)) return;
    this.local.blanks = this.local.blanks.filter((b) => b.blankId !== id).concat({ blankId: id, text: e.detail.value }); this.changed(); },
  changed() { this.local = practice.response(this.local, this.detail.question); this.invalidate(); this.persist(); this.sync(); },
  async prepare() {
    if (!this.canEdit() || this.unpersisted) return;
    const response = clone(practice.response(this.local, this.detail.question));
    const r = await this.run(async () => { const p = await getApp().session.request(`/api/app/education-assessment/${this.id}/preview-my-practice`, 'POST', { response });
      if (!p || !/^[a-f0-9]{64}$/.test(p.fingerprint) || !Number.isFinite(Date.parse(p.serverNowUtc))) throw practice.error(); return { response, fingerprint: p.fingerprint }; });
    if (r) { this.prepared = r; this.setData({ preview: practice.form(this.detail.question, r.response), share: false, practiceOnly: false }); }
  },
  sharing(e) { if (this.authorized() && !this.working && this.prepared) this.setData({ share: e.detail.value.includes('share'), practiceOnly: e.detail.value.includes('practice') }); },
  async submit() {
    if (!this.canEdit() || this.unpersisted || !this.prepared || !this.data.share || !this.data.practiceOnly) return; const ticket = this.version, prepared = this.prepared;
    if (!await ask('将核对的本轮全部文字、选择、填空和选中图片明确共享给原任务教师？本轮将封存一次；不覆盖首答、正式成绩或独立掌握证据。')) return;
    if (ticket !== this.version || !this.canEdit() || this.prepared !== prepared || !this.data.share || !this.data.practiceOnly) return;
    const r = await this.run(async () => { await this.writer.beginSubmit({ response: prepared.response, preparationFingerprint: prepared.fingerprint, confirmSharingWithTeacher: true, confirmPracticeOnly: true }); return this.writer.send(); });
    if (r) return this.accept(r);
  },
  accept(r) {
    if (!this.authorized()) return; const p = this.store.pending(); if (!p || (r.operationId || r.id) !== p.operationId) return;
    try {
      if (p.kind === 'image') { this.local ||= this.store.draft() || practice.empty(4);
        if (!this.local.images.some((i) => i.attachmentId === r.id)) { if (this.local.images.length >= 5) throw practice.error(); this.local.images.push({ attachmentId: r.id, rotationDegrees: 0 }); }
        this.store.saveDraft(this.local); this.unpersisted = false; this.store.acknowledge(r.id);
        if (this.detail && !this.detail.images.some((i) => i.id === r.id)) this.detail.images.push(r);
        this.invalidate(); this.sync(); this.setData({ notice: '原图已确认，只加入本人本机草稿，尚未共享或上交。' });
      } else { this.store.removeDraft(); this.store.acknowledge(r.operationId); this.local = null; this.consent = false; this.unpersisted = false; this.receiptOnly = true;
        this.clearView(); this.setData({ receipt: r.operationId, notice: '已确认独立上交。回执不含答案或参考评分；可明确读取本轮副本与当前获准反馈。' }); }
    } catch (e) { this.setData({ error: message(e) }); this.sync(); }
  },
  async recover() { if (!this.authorized() || this.working || !this.store.pending()) return; const r = await this.run(() => this.writer.recover()); if (r) return this.accept(r);
    if (this.authorized() && !this.data.error) this.setData({ notice: '尚未查到原回执不表示失败。请核对本轮记录，再明确重试或关闭追踪。' }); },
  async retry() { if (!this.authorized() || this.working || !this.store.pending()) return; const ticket = this.version, p = this.store.pending();
    if (!await ask('先查询原回执，未查到时才同号同原内容重试。丢失正文不能重造，图片须同一原图。确认继续？')) return;
    if (ticket !== this.version || !this.authorized() || this.store.pending()?.operationId !== p.operationId) return;
    const r = await this.run(() => this.writer.retry()); if (r) return this.accept(r); },
  async abandon() { if (!this.authorized() || this.working || !this.store.pending()) return; const ticket = this.version, p = this.store.pending();
    if (!await ask('只关闭本机追踪，不撤销可能已完成的上传或上交，不删除服务器原图。请先核对本轮记录，确定继续？')) return;
    if (ticket === this.version && this.authorized() && this.store.pending()?.operationId === p.operationId) { this.store.acknowledge(p.operationId); this.sync(); } },
  async clearLocal() { if (!this.authorized() || this.working || this.store.pending()) return; const ticket = this.version;
    if (!await ask('清除本轮本机未上交草稿？不影响服务器副本或历史原图。')) return;
    if (ticket === this.version && this.authorized()) { this.store.removeDraft(); this.local = null; this.consent = false; this.unpersisted = false; this.invalidate(); this.sync(); } },
  async addImage() { if (!this.canEdit() || this.detail.question.type !== 4 || this.local.images.length >= 5 || this.unpersisted) return;
    const r = await this.run(async () => { const file = await choose(); if (!this.authorized()) throw practice.error('STALE_RESPONSE'); await this.writer.beginImage(file); return this.writer.send(); }); if (r) return this.accept(r); },
  async reselectImage() { if (!this.authorized() || this.working || this.store.pending()?.kind !== 'image') return;
    const r = await this.run(async () => { const file = await choose(); if (!this.authorized()) throw practice.error('STALE_RESPONSE'); await this.writer.reselect(file); return this.writer.retry(); }); if (r) return this.accept(r); },
  addUploaded(e) { const id = e.currentTarget.dataset.id; if (!this.canEdit() || this.detail.question.type !== 4 || this.local.images.length >= 5 || !this.detail.images.some((i) => i.id === id) || this.local.images.some((i) => i.attachmentId === id)) return;
    this.local.images.push({ attachmentId: id, rotationDegrees: 0 }); this.changed(); },
  editImage(e) { if (!this.canEdit()) return; const images = this.local.images, index = images.findIndex((i) => i.attachmentId === e.currentTarget.dataset.id), action = e.currentTarget.dataset.action; if (index < 0) return;
    if (action === 'rotate') images[index] = { ...images[index], rotationDegrees: (images[index].rotationDegrees + 90) % 360 };
    else if (action === 'remove') images.splice(index, 1); else if (action === 'up' && index > 0) [images[index - 1], images[index]] = [images[index], images[index - 1]];
    else if (action === 'down' && index + 1 < images.length) [images[index + 1], images[index]] = [images[index], images[index + 1]]; else return; this.changed(); },
  async image(e) { const id = e.currentTarget.dataset.id; if (!this.authorized() || this.working || !this.detail?.images.some((i) => i.id === id)) return;
    this.clearImages(); const version = this.imageVersion, epoch = getApp().session.getState().generation;
    const file = await this.run(async () => { const path = await getApp().session.download(`/api/education/practices/${this.id}/images/${id}`);
      if (!this.authorized() || version !== this.imageVersion || epoch !== getApp().session.getState().generation) { getApp().session.releaseFile(path); throw practice.error('STALE_RESPONSE'); } return path; });
    if (!file) return; if (!this.authorized() || version !== this.imageVersion || epoch !== getApp().session.getState().generation) { getApp().session.releaseFile(file); return; }
    this.files.add(file); this.setData({ imagePath: file, imageRotation: (this.local || this.detail.response)?.images.find((i) => i.attachmentId === id)?.rotationDegrees || 0 }); },
  closeImage() { this.clearImages(); this.setData({ imagePath: '' }); },
  original() { if (this.authorized() && !this.working && this.detail) wx.navigateTo({ url: `/pages/assessment-result/index?schoolId=${this.schoolId}&id=${this.detail.recipientId}&questionId=${this.detail.questionId}` }); },
  web() { if (!this.authorized() || this.working || !this.detail) return; const url = getApp().session.portalUrl(`/education/practice?practiceId=${this.id}`);
    if (url) { wx.setClipboardData({ data: url }); this.setData({ notice: '已复制本轮定位地址，网页仍需登录。未上交的本机文字和图片不会自动同步网页，请先核对暂存或上交。' }); } },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
