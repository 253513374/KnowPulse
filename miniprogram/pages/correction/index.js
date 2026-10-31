const correction = require('../../utils/correction');
const createStore = require('../../utils/correction-store');
const createCommand = require('../../utils/correction-command');
const { actor } = require('../../utils/feedback');
const { isGuid } = require('../../utils/assessment');
const message = require('../../utils/messages');
const blank = () => ({ originalThinking: '', currentSolution: '', checkingStrategy: '', images: [] });
const ask = (content) => new Promise((resolve) => wx.showModal({ title: '核对订正', content, success: (r) => resolve(!!r.confirm), fail: () => resolve(false) }));
const choose = () => new Promise((resolve, reject) => wx.chooseMedia({ count: 1, mediaType: ['image'], sourceType: ['album', 'camera'], success: (r) => r.tempFiles?.[0]?.tempFilePath ? resolve(r.tempFiles[0].tempFilePath) : reject(correction.error('IMAGE_UNAVAILABLE')), fail: () => reject(correction.error('IMAGE_UNAVAILABLE')) }));
Page({
  data: { detail: null, form: null, cloud: null, preview: null, history: [], historyTotal: 0, historyOffset: 0, historyIndex: 0, historyCount: 0,
    pending: null, conflict: false, consent: false, share: false, busy: false, error: '', notice: '', imagePath: '', imageRotation: 0, unpersisted: false },
  onLoad(query) {
    this.id = typeof query.id === 'string' ? query.id.toLowerCase() : ''; this.schoolId = typeof query.schoolId === 'string' ? query.schoolId.toLowerCase() : '';
    const state = getApp().session.getState(); this.generation = state.generation; this.context = state.context; this.version = 0; this.files = new Set(); this.offset = 0; this.consent = false;
    this.unsubscribe = getApp().subscribe((s) => {
      const generation = this.generation !== s.generation, context = this.context !== s.context; this.generation = s.generation; this.context = s.context;
      const lost = this.owner && actor(s) && this.owner !== actor(s) || s.context && !correction.allowed(s, this.schoolId) || !s.authenticated;
      if (generation || context || lost || !s.context) this.clearView();
      if (generation || lost) { this.version++; this.local = null; this.consent = false; if (!this.disposed) this.setData({ error: '当前身份或权限已变化，请重新核对本人账号；未上交本机内容已停止显示。' }); try { if (this.store) this.store.clearPrivate(); } catch (_) { if (!this.disposed) this.setData({ error: message(correction.error('LOCAL_CLEANUP_UNCONFIRMED')) }); } }
    });
  },
  onShow() { this.visible = true; return this.load(); },
  onHide() { this.visible = false; this.version++; this.clearView(); this.local = null; this.consent = false; },
  onUnload() { this.onHide(); this.disposed = true; this.unsubscribe(); },
  authorized() { const s = getApp().session.getState(); return this.visible && !this.disposed && actor(s) === this.owner && correction.allowed(s, this.schoolId); },
  clearImages() { for (const p of this.files) getApp().session.releaseFile(p); this.files.clear(); },
  clearView() { this.detail = null; this.prepared = null; this.historyRows = []; this.clearImages(); if (!this.disposed) this.setData({ detail: null, form: null, cloud: null, preview: null, history: [], historyTotal: 0, pending: null, consent: false, share: false, imagePath: '', imageRotation: 0 }); },
  sync() {
    if (!this.authorized() || !this.writer) return;
    this.setData({ pending: this.writer.state(), consent: this.consent, form: this.local ? this.local.content : null, conflict: !!this.local && this.local.base !== this.detail?.currentId, unpersisted: !!this.unpersisted });
    if (this.local || this.store.pending()) { if (wx.enableAlertBeforeUnload) wx.enableAlertBeforeUnload({ message: '本机订正草稿不等于上交；请核对暂存和待确认操作。' }); }
    else if (wx.disableAlertBeforeUnload) wx.disableAlertBeforeUnload();
  },
  failure(e) { return e.status === 409 ? '云端上交版本已变化。本机草稿保留，请查询原回执并比较云端副本，再明确选择基准。' : message(e); },
  async run(work) {
    if (!this.authorized() || this.working) return null; this.working = true; const ticket = this.version, context = getApp().session.getState().context; this.setData({ busy: true, error: '', notice: '' });
    try { const row = await work(); if (ticket === this.version && this.authorized() && context === getApp().session.getState().context) return row; }
    catch (e) { if (ticket === this.version && this.authorized()) this.setData({ error: this.failure(e) }); }
    finally { this.working = false; if (!this.disposed) { this.setData({ busy: false }); if (ticket === this.version) { try { this.sync(); } catch (e) { this.setData({ error: message(e) }); } } } }
    return null;
  },
  async load() {
    if (!this.visible || this.working) return; if (![this.id, this.schoolId].every(isGuid)) { this.setData({ error: '订正链接无法核对，请回列表重新选择。' }); return; }
    this.working = true; const ticket = ++this.version; this.clearView(); this.setData({ busy: true, error: '' });
    try {
      const session = getApp().session; await session.refreshWorkspace(); const s = session.getState(), context = s.context;
      if (!actor(s) || this.owner && this.owner !== actor(s) || !correction.allowed(s, this.schoolId)) throw correction.error('FORBIDDEN'); this.owner = actor(s);
      if (!this.store) { this.store = createStore(wx, getApp().configuration, s.context, this.schoolId, this.id); this.writer = createCommand(wx, session, this.store, this.id, this.schoolId, () => this.authorized()); }
      this.sync(); const dto = await session.request(`/api/app/education-assessment/my-correction-for-school?deliveryId=${this.id}&schoolId=${this.schoolId}`);
      if (ticket !== this.version || !this.authorized() || context !== session.getState().context) return;
      const detail = correction.card(dto, this.schoolId, true); if (detail.id !== this.id) throw correction.error(); this.detail = detail;
      if (!this.local) this.local = this.store.draft();
      this.setData({ detail, cloud: detail.current }); this.sync();
    } catch (e) { if (this.visible && ticket === this.version) this.setData({ error: this.failure(e) }); }
    finally { this.working = false; if (!this.disposed) { this.setData({ busy: false }); if (ticket === this.version) { try { this.sync(); } catch (e) { this.setData({ error: message(e) }); } } } }
    if (this.detail && this.authorized()) return this.history();
  },
  async allowLocal() {
    if (!this.authorized() || this.working || !this.detail) return; const ticket = this.version;
    if (!await ask('允许在本机按账号和订正任务暂存原思路、解法、检查策略和操作请求，后续使用时按 24 小时有效期清理。未确认原图也可能产生本应用副本。共享设备请谨慎；本机暂存不等于云端上交。')) return;
    if (ticket !== this.version || !this.authorized() || this.working) return;
    this.consent = true; if (!this.local) this.local = { base: this.detail.currentId, content: blank() }; this.persist(); this.sync();
  },
  persist() { try { this.store.saveDraft(this.local); this.unpersisted = false; } catch (e) { this.unpersisted = true; this.setData({ error: message(e) + ' 当前输入仍在本页面内存，请勿离开。' }); } },
  saveLocal() { if (this.authorized() && this.consent && this.local && !this.working) { this.persist(); this.sync(); } },
  canEdit() { return this.authorized() && this.consent && this.detail?.canSubmit && this.local && !this.working && !this.store.pending(); },
  invalidate() { this.prepared = null; this.setData({ preview: null, share: false }); },
  change(e) { const k = e.currentTarget.dataset.field; if (!this.canEdit() || !['originalThinking', 'currentSolution', 'checkingStrategy'].includes(k)) return; this.local.content = { ...this.local.content, [k]: e.detail.value }; this.invalidate(); this.persist(); this.sync(); },
  async rebase() {
    if (!this.authorized() || this.working || !this.local || this.store.pending() || !this.consent) return; const ticket = this.version, head = this.detail.currentId;
    if (!await ask('已比较本机草稿与云端副本？保留本机文字和图片，明确改用当前云端副本作为下一次上交基准。不会覆盖首答。')) return;
    if (ticket !== this.version || !this.authorized() || this.working || head !== this.detail.currentId || this.store.pending()) return;
    this.local.base = head; this.invalidate(); this.persist(); this.sync();
  },
  async preview() {
    if (!this.canEdit() || this.unpersisted || this.local.base !== this.detail.currentId) return;
    const row = await this.run(async () => {
      const content = correction.content(this.local.content, true), base = this.local.base;
      const result = await getApp().session.request(`/api/app/education-assessment/preview-my-correction/${this.id}`, 'POST', { expectedSubmissionId: base, content });
      if (result.deliveryId !== this.id || (result.expectedSubmissionId || null) !== base || !/^[a-f0-9]{64}$/.test(result.fingerprint) || JSON.stringify(correction.content(result.content, true)) !== JSON.stringify(content) || !Array.isArray(result.images) || result.images.length !== content.images.length) throw correction.error();
      for (const image of result.images) if (!content.images.some((i) => i.attachmentId === image.id) || !image.isUploaded || !/^[a-f0-9]{64}$/.test(image.contentHash)) throw correction.error();
      return { content, expectedSubmissionId: base, fingerprint: result.fingerprint };
    });
    if (row) { this.prepared = row; this.setData({ preview: row.content, share: false }); }
  },
  sharing(e) { if (this.authorized() && !this.working && this.prepared) this.setData({ share: e.detail.value.includes('yes') }); },
  async submit() {
    if (!this.canEdit() || !this.prepared || !this.data.share || this.unpersisted || this.local.base !== this.detail.currentId) return;
    const prepared = this.prepared, ticket = this.version;
    if (!await ask('将上方核对的全部订正文字和选中图片明确共享给本任务教师？这会创建独立上交版本，不改变首答或正式成绩。')) return;
    if (ticket !== this.version || !this.canEdit() || prepared !== this.prepared || !this.data.share) return;
    const row = await this.run(async () => { await this.writer.beginSubmission({ expectedSubmissionId: prepared.expectedSubmissionId, content: prepared.content, previewFingerprint: prepared.fingerprint, confirmSharingWithTeacher: true }); return this.writer.send(); });
    if (row) return this.accept(row);
  },
  async accept(row) {
    if (!this.authorized()) return; const p = this.store.pending(); if (!p || row.id !== p.operationId) return;
    try {
      if (p.kind === 'image') {
        if (!this.local) this.local = this.store.draft() || { base: this.detail?.currentId || null, content: blank() };
        if (!this.local.content.images.some((i) => i.attachmentId === row.id)) {
          if (this.local.content.images.length >= 5) throw correction.error('CORRECTION_INVALID');
          this.local.content.images.push({ attachmentId: row.id, rotationDegrees: 0 });
        }
        this.store.saveDraft(this.local); this.unpersisted = false; this.store.acknowledge(row.id); this.invalidate(); this.sync(); this.setData({ notice: '图片仅进入本机订正草稿，尚未上交。' });
      } else { correction.submission(row, this.id); this.store.removeDraft(); this.store.acknowledge(row.id); this.local = null; this.unpersisted = false; this.consent = false; this.offset = 0; await this.load(); if (this.authorized()) this.setData({ notice: '已确认独立订正上交，请核对云端记录及教师复核。' }); }
    } catch (e) { this.setData({ error: message(e) }); this.sync(); }
  },
  async recover() { if (!this.writer || !this.store.pending()) return; const row = await this.run(() => this.writer.recover()); if (row) return this.accept(row); if (this.authorized() && !this.data.error) this.setData({ notice: '尚未查到回执不代表失败；请核对历史，再明确重试同一请求或关闭本机追踪。' }); },
  async retry() {
    if (!this.authorized() || this.working || !this.consent || !this.store.pending()) return; const p = this.store.pending(), ticket = this.version;
    if (!await ask('先查询原回执，未查到时才重发同一编号和原内容。丢失的正文不能重建，图片必须为同一原图。确定继续？')) return;
    if (ticket !== this.version || !this.authorized() || this.working || this.store.pending()?.operationId !== p.operationId) return;
    const row = await this.run(() => this.writer.retry()); if (row) return this.accept(row);
  },
  async abandon() {
    if (!this.authorized() || this.working || !this.store.pending()) return; const p = this.store.pending(), ticket = this.version;
    if (!await ask('仅关闭本机追踪，不撤销可能已成功的云端操作，也不删除历史原图。请先核对上交记录。确定继续？')) return;
    if (ticket !== this.version || !this.authorized() || this.working || this.store.pending()?.operationId !== p.operationId) return;
    await this.run(async () => { this.store.acknowledge(p.operationId); return true; }); this.sync();
  },
  async clearLocal() {
    if (!this.authorized() || this.working || this.store.pending()) return; const ticket = this.version;
    if (!await ask('清除本机未上交草稿？云端上交与原答卷保留。')) return;
    if (ticket !== this.version || !this.authorized() || this.working || this.store.pending()) return;
    const done = await this.run(async () => { this.store.removeDraft(); return true; }); if (done) { this.local = null; this.consent = false; this.unpersisted = false; this.invalidate(); this.sync(); }
  },
  async upload() {
    if (!this.canEdit() || this.local.content.images.length >= 5) return;
    const row = await this.run(async () => { const file = await choose(); if (!this.authorized()) throw correction.error('STALE_RESPONSE'); await this.writer.beginImage(file); return this.writer.send(); }); if (row) return this.accept(row);
  },
  async reselect() {
    if (!this.authorized() || this.working || !this.consent || this.store.pending()?.kind !== 'image') return;
    const row = await this.run(async () => { const recovered = await this.writer.recover(); if (recovered) return recovered; const file = await choose(); if (!this.authorized()) throw correction.error('STALE_RESPONSE'); await this.writer.reselect(file); return this.writer.retry(); }); if (row) return this.accept(row);
  },
  editImage(e) {
    if (!this.canEdit()) return; const id = e.currentTarget.dataset.id, action = e.currentTarget.dataset.action, list = this.local.content.images, index = list.findIndex((i) => i.attachmentId === id); if (index < 0) return;
    if (action === 'rotate') list[index] = { ...list[index], rotationDegrees: (list[index].rotationDegrees + 90) % 360 };
    else if (action === 'remove') list.splice(index, 1);
    else if (action === 'up' && index > 0) [list[index - 1], list[index]] = [list[index], list[index - 1]]; else return;
    this.invalidate(); this.persist(); this.sync();
  },
  async image(e) {
    const id = e.currentTarget.dataset.id; let reference = this.local?.content.images.find((i) => i.attachmentId === id) || this.detail?.current?.content.images.find((i) => i.attachmentId === id);
    if (!reference) reference = (this.historyRows || []).flatMap((h) => h.submission.content.images).find((i) => i.attachmentId === id);
    if (!reference || !this.authorized() || this.working) return; const rotation = reference.rotationDegrees, ticket = this.version, context = getApp().session.getState().context;
    const file = await this.run(async () => { const p = await getApp().session.download(`/api/education/corrections/deliveries/${this.id}/images/${id}`); if (ticket !== this.version || !this.authorized() || context !== getApp().session.getState().context) { getApp().session.releaseFile(p); return null; } this.clearImages(); this.files.add(p); return p; });
    if (file) this.setData({ imagePath: file, imageRotation: rotation });
  },
  closeImage() { this.clearImages(); this.setData({ imagePath: '' }); },
  async history() {
    if (!this.detail) return; const head = this.detail.currentId;
    const result = await this.run(() => getApp().session.request(`/api/app/education-assessment/my-correction-history?deliveryId=${this.id}&${head ? 'expectedSubmissionId=' + head + '&' : ''}SkipCount=${this.offset}&MaxResultCount=20`));
    if (!result) return;
    try {
      if (!Array.isArray(result.items) || !Number.isSafeInteger(result.totalCount) || result.totalCount < 0) throw correction.error();
      this.historyRows = result.items.map((h) => { const s = correction.submission(h.submission, this.id); return { id: s.id, submission: s, reviews: [5, 6, 7].includes(this.detail.availability) ? [] : (h.reviews || []).map((r) => correction.review(r, this.id, s.id)) }; });
      this.setData({ historyTotal: result.totalCount, historyOffset: this.offset, historyCount: this.historyRows.length }); this.showHistory(0);
    } catch (e) { this.setData({ error: message(e) }); }
  },
  showHistory(i) { if (!this.authorized() || !Number.isInteger(i) || i < 0 || i >= Math.max(1, this.historyRows.length)) return; this.setData({ history: this.historyRows.slice(i, i + 1), historyIndex: i }); },
  historySelect(e) { if (!this.working) this.showHistory(this.data.historyIndex + Number(e.currentTarget.dataset.direction)); },
  paginate(e) { const d = Number(e.currentTarget.dataset.direction); if (!this.authorized() || this.working || ![-1, 1].includes(d) || this.offset + d * 20 < 0 || d === 1 && this.offset + 20 >= this.data.historyTotal) return; this.offset += d * 20; return this.history(); },
  original() { if (this.authorized() && this.detail?.question && !this.working) wx.navigateTo({ url: `/pages/assessment-result/index?schoolId=${this.schoolId}&id=${this.detail.recipientId}` }); },
  web() { if (!this.authorized()) return; const url = getApp().session.portalUrl(`/education/corrections/my?deliveryId=${this.id}`); if (url) wx.setClipboardData({ data: url }); else this.setData({ error: '尚未配置网页地址。' }); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
