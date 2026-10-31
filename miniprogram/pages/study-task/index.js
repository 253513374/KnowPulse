const study = require('../../utils/study');
const notebook = require('../../utils/notebook');
const command = require('../../utils/study-command');
const { actor } = require('../../utils/feedback');
const { isGuid } = require('../../utils/assessment');
const message = require('../../utils/messages');
const ask = (content) => new Promise((resolve) => wx.showModal({ title: '核对学习记录', content, success: (r) => resolve(!!r.confirm), fail: () => resolve(false) }));
const blank = () => ({ text: '', minutes: '', difficulty: 0, share: false, activityOnly: false });

Page({
  data: { detail: null, material: null, history: [], historyTotal: 0, offset: 0, busy: false, error: '', notice: '',
    form: null, pending: null, canRetry: false, imagePath: '', difficulties: study.difficulties },
  onLoad(query) {
    this.id = query.id; this.schoolId = query.schoolId; this.publicationId = query.publicationId || null; this.materialId = query.materialId || null; this.version = 0; this.offset = 0; this.files = new Set();
    this.generation = getApp().session.getState().generation;
    this.form = blank(); this.base = undefined;
    this.unsubscribe = getApp().subscribe((s) => {
      const changed = this.generation !== s.generation; this.generation = s.generation;
      if (changed || !s.context || !study.allowed(s, this.schoolId) || this.owner && actor(s) !== this.owner) {
        this.clearView();
        if (changed || !s.authenticated || s.context && !study.allowed(s, this.schoolId) || this.owner && actor(s) && actor(s) !== this.owner) {
          if (changed) this.version++;
          this.form = blank(); this.base = undefined; if (this.writer) this.writer.dropPayload();
        }
      }
    });
  },
  onShow() { this.visible = true; return this.load(); },
  onHide() { this.visible = false; this.version++; this.clearView(); this.form = blank(); this.base = undefined; if (this.writer) this.writer.dropPayload(); },
  onUnload() { this.onHide(); this.disposed = true; this.unsubscribe(); },
  authorized() { const s = getApp().session.getState(); return this.visible && !this.disposed && actor(s) === this.owner && study.allowed(s, this.schoolId); },
  clearFiles() { for (const file of this.files) getApp().session.releaseFile(file); this.files.clear(); },
  clearView() { this.clearFiles(); this.rawMaterial = null; if (!this.disposed) this.setData({ detail: null, material: null, history: [], historyTotal: 0, imagePath: '', form: null, pending: null, canRetry: false, busy: !!this.working, notice: '' }); },
  sync() {
    if (!this.authorized()) return;
    const state = this.writer ? this.writer.state() : { pending: null, canRetry: false };
    this.setData({ ...state, form: { ...this.form } });
    if (this.form.text || state.pending) { if (wx.enableAlertBeforeUnload) wx.enableAlertBeforeUnload({ message: '未上交文字只在本次页面内存中，离开会丢失；待确认操作仍可能已经成功。' }); }
    else if (wx.disableAlertBeforeUnload) wx.disableAlertBeforeUnload();
  },
  async load() {
    if (!this.visible || this.working || !isGuid(this.id) || !isGuid(this.schoolId)) return;
    this.working = true; const ticket = ++this.version; this.clearView(); this.setData({ busy: true, error: '' });
    try {
      const session = getApp().session; await session.refreshWorkspace(); const state = session.getState();
      if (!actor(state) || this.owner && actor(state) !== this.owner || !study.allowed(state, this.schoolId)) throw Object.assign(new Error('FORBIDDEN'), { code: 'FORBIDDEN' });
      this.owner = actor(state); const context = state.context;
      if (this.publicationId && !isGuid(this.publicationId) || this.materialId && !isGuid(this.materialId)) throw Object.assign(new Error('INVALID_PATH'), { code: 'INVALID_PATH' });
      const raw = await session.request(`/api/app/education-study-task/open-my-for-school?recipientId=${this.id}&schoolId=${this.schoolId}${this.publicationId ? '&expectedPublicationId=' + this.publicationId : ''}`, 'POST');
      const detail = study.task(raw, this.schoolId, true); if (detail.id !== this.id || this.materialId && !detail.materials.some((m) => m.id === this.materialId)) throw Object.assign(new Error('SOURCE_UNAVAILABLE'), { code: 'SOURCE_UNAVAILABLE' });
      const head = detail.currentStudentRevisionId;
      const rows = await session.request(`/api/app/education-study-task/my-history?recipientId=${this.id}&${head ? 'expectedStudentRevisionId=' + head + '&' : ''}SkipCount=${this.offset}&MaxResultCount=20`);
      if (!rows || !Array.isArray(rows.items) || !Number.isSafeInteger(rows.totalCount) || rows.totalCount < 0) throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' });
      const history = rows.items.map((r) => study.event(r, this.id));
      if (ticket !== this.version || !this.authorized() || session.getState().context !== context) return;
      if (!this.writer) this.writer = command(wx, session, getApp().configuration, this.id, this.schoolId, () => this.authorized());
      if (this.base === undefined) this.base = head;
      this.setData({ detail, canUseNotebook: notebook.allowed(state), history, historyTotal: rows.totalCount, offset: this.offset, conflict: this.base !== head }); this.sync();
    } catch (e) { if (this.visible && ticket === this.version) this.setData({ error: message(e) }); }
    finally { this.working = false; if (!this.disposed) this.setData({ busy: false }); }
  },
  change(event) {
    if (!this.authorized() || this.data.busy || this.data.pending || !this.data.detail) return;
    const field = event.currentTarget.dataset.field;
    if (['text', 'minutes'].includes(field)) this.form[field] = event.detail.value;
    else if (field === 'difficulty') this.form[field] = Number(event.detail.value);
    else if (['share', 'activityOnly'].includes(field)) this.form[field] = event.detail.value.includes('yes');
    this.sync();
  },
  async act(work) {
    if (!this.authorized() || this.working) return null;
    this.working = true; const ticket = this.version; const context = getApp().session.getState().context; this.setData({ busy: true, error: '', notice: '' });
    try { const result = await work(); if (ticket === this.version && this.authorized() && getApp().session.getState().context === context) return result; }
    catch (e) { if (ticket === this.version && this.authorized() && getApp().session.getState().context === context) this.setData({ error: message(e) }); }
    finally { this.working = false; if (!this.disposed) { this.setData({ busy: false }); if (ticket === this.version) this.sync(); } }
    return null;
  },
  async progress(event) {
    const action = Number(event.currentTarget.dataset.action); const d = this.data.detail;
    if (!this.authorized() || this.data.busy || !d || this.data.pending || this.data.conflict || ![0, 1, 2].includes(action) ||
        action === 0 && !d.canStart || action === 1 && !d.canComplete || action === 2 && !d.canReport) return;
    const text = this.form.text.trim() || null;
    const minutes = action === 1 && this.form.minutes !== '' ? Number(this.form.minutes) : null;
    if (text && !this.form.share || action === 2 && (!text || !this.form.share) || action === 0 && d.state === 3 && !text ||
        action === 1 && (!this.form.activityOnly || minutes !== null && (!Number.isInteger(minutes) || minutes < 1 || minutes > 1440))) {
      this.setData({ error: '请核对上交勾选、困难说明或重新开始原因；完成须确认仅为学习活动，用时可留空或填 1–1440 分钟。' }); return;
    }
    const input = { expectedStudentRevisionId: this.base, action, difficulty: action === 2 ? this.form.difficulty : null,
      sharedText: text, reportedMinutes: minutes, confirmSharingWithTeacher: this.form.share, confirmActivityOnly: action === 1 && this.form.activityOnly };
    const ticket = this.version; const snapshot = JSON.stringify(this.form);
    if (!await ask(`${['记录开始学习', '记录自报完成（不表示已经掌握）', '上交困难反馈'][action]}？${text ? '填写的文字会共享给本任务教师。' : '本次没有上交文字。'}`)) return;
    if (ticket !== this.version || !this.authorized() || snapshot !== JSON.stringify(this.form) || this.data.pending || this.data.busy) return;
    const receipt = await this.act(() => this.writer.start(input)); if (receipt) return this.saved();
  },
  saved() { this.form = blank(); this.base = undefined; this.offset = 0; return this.load(); },
  async recover() {
    if (!this.writer) return;
    const row = await this.act(() => this.writer.recover());
    if (row) return this.saved();
    if (this.authorized() && !this.data.error) this.setData({ notice: '未查到原回执不代表失败；可明确重试仍在本页面内存中的原请求，或先核对历史记录。' });
  },
  async retry() {
    if (!this.writer || !this.data.canRetry || !await ask('将先查询原回执；仅在未查到时重发同一操作、同一版本和同一内容。确认重试？')) return;
    const row = await this.act(() => this.writer.retry()); if (row) return this.saved();
  },
  async abandon() {
    if (!this.writer || !await ask('只关闭本机追踪，不撤销服务器操作。原请求可能已成功；请核对历史，未上交文字会丢失。确定继续？')) return;
    const done = await this.act(async () => { this.writer.abandon(); return true; }); if (done) return this.saved();
  },
  async discard() {
    if (this.data.pending || !this.authorized() || !await ask('丢弃本页面未上交文字并重新读取当前版本？不会覆盖云端记录。')) return;
    if (this.authorized() && !this.data.pending) return this.saved();
  },
  paginate(event) {
    const d = Number(event.currentTarget.dataset.direction);
    if (this.data.busy || ![-1, 1].includes(d) || this.offset + d * 20 < 0 || d === 1 && this.offset + 20 >= this.data.historyTotal) return;
    this.offset += d * 20; return this.load();
  },
  async material(event) {
    const id = event.currentTarget.dataset.id;
    if (!this.data.detail || !this.data.detail.materials.some((m) => m.id === id)) return;
    this.clearFiles(); this.rawMaterial = null; this.setData({ material: null, imagePath: '' });
    const row = await this.act(() => getApp().session.request(`/api/app/education-study-task/my-material?recipientId=${this.id}&materialId=${id}`));
    if (row && this.authorized()) {
      try { this.clearFiles(); this.rawMaterial = study.material(row, id); this.setData({ material: study.materialPage(this.rawMaterial, 0), imagePath: '' }); }
      catch (e) { this.setData({ material: null, error: message(e) }); }
    }
  },
  materialPage(event) {
    const d = Number(event.currentTarget.dataset.direction); if (!this.authorized() || this.working || !this.rawMaterial || !this.data.material || ![-1, 1].includes(d)) return;
    const index = this.data.material.page - 1 + d; if (index < 0 || index >= this.data.material.pages) return;
    this.setData({ material: study.materialPage(this.rawMaterial, index) });
  },
  async attachment(event) {
    const id = event.currentTarget.dataset.id; const material = this.data.material;
    const file = material && material.attachments.find((a) => a.id === id);
    if (!this.authorized() || !file || !file.type) return;
    const ticket = this.version; const context = getApp().session.getState().context;
    const path = await this.act(async () => {
      const result = await getApp().session.download(`/api/education/study/recipients/${this.id}/materials/${material.id}/attachments/${id}`);
      if (ticket !== this.version || !this.authorized() || getApp().session.getState().context !== context) { getApp().session.releaseFile(result); return null; }
      this.clearFiles(); this.files.add(result); return result;
    });
    if (!path || !this.authorized()) return;
    if (['jpg', 'jpeg', 'png', 'webp'].includes(file.type)) this.setData({ imagePath: path });
    else {
      try { wx.openDocument({ filePath: path, fileType: file.type, showMenu: false, fail: () => { if (ticket === this.version && this.authorized()) { this.clearFiles(); this.setData({ error: '当前设备无法预览此附件，请在网页阅读。' }); } } }); }
      catch (_) { this.clearFiles(); this.setData({ error: '当前设备无法预览此附件，请在网页阅读。' }); }
    }
  },
  closeImage() { this.clearFiles(); this.setData({ imagePath: '' }); },
  notebook(e) {
    const id = e.currentTarget.dataset.id;
    if (!this.authorized() || this.data.busy || !notebook.allowed(getApp().session.getState()) || !this.data.detail?.materials.some((m) => m.id === id)) return;
    wx.navigateTo({ url: `/pages/notebooks/index?sourceKind=1&recipientId=${this.id}&componentId=${id}&sourceVersionId=${this.data.detail.publicationId}` });
  },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
