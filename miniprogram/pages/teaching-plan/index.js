const plan = require('../../utils/plan');
const study = require('../../utils/study');
const command = require('../../utils/plan-command');
const { actor } = require('../../utils/feedback');
const { isGuid } = require('../../utils/assessment');
const message = require('../../utils/messages');
const ask = (content) => new Promise((resolve) => wx.showModal({ title: '核对方案活动', content, success: (r) => resolve(!!r.confirm), fail: () => resolve(false) }));
const blank = () => ({ text: '', minutes: '', difficulty: 0, share: false, activityOnly: false });
const guidInput = (value) => typeof value === 'string' ? value.toLowerCase() : value;

Page({
  data: { plan: null, content: null, activity: null, choices: [], selectedIndex: 0, history: [], historyTotal: 0, offset: 0,
    form: null, pending: null, canRetry: false, canWrite: false, receipt: null, material: null, imagePath: '', busy: false, error: '', notice: '', difficulties: study.difficulties },
  onLoad(query) {
    this.id = guidInput(query.id); this.versionId = guidInput(query.versionId); this.schoolId = guidInput(query.schoolId); this.stepId = guidInput(query.stepId) || '';
    this.version = 0; this.generation = getApp().session.getState().generation; this.offset = 0; this.files = new Set(); this.form = blank(); this.base = undefined;
    this.context = getApp().session.getState().context;
    this.unsubscribe = getApp().subscribe((s) => {
      const changed = this.generation !== s.generation; this.generation = s.generation;
      const contextChanged = this.context !== s.context; this.context = s.context;
      if (changed || contextChanged || !s.context || !plan.allowed(s, this.schoolId) || this.owner && actor(s) !== this.owner) {
        this.clearView();
        if (changed || !s.authenticated || s.context && !plan.allowed(s, this.schoolId) || this.owner && actor(s) && actor(s) !== this.owner) {
          if (changed) this.version++;
          this.form = blank(); this.base = undefined; if (this.writer) this.writer.dropPayload();
        }
      } else this.sync();
    });
  },
  onShow() { this.visible = true; return this.load(); },
  onHide() { this.visible = false; this.version++; this.clearView(); this.form = blank(); this.base = undefined; if (this.writer) this.writer.dropPayload(); },
  onUnload() { this.onHide(); this.disposed = true; this.unsubscribe(); },
  authorized(write = false) { const s = getApp().session.getState(); return this.visible && !this.disposed && actor(s) === this.owner && plan.allowed(s, this.schoolId, write); },
  clearFiles() { for (const f of this.files) getApp().session.releaseFile(f); this.files.clear(); },
  clearView() {
    this.clearFiles(); this.view = null; this.selected = null; this.rawMaterial = null;
    if (!this.disposed) this.setData({ plan: null, content: null, activity: null, choices: [], history: [], historyTotal: 0, form: null, pending: null, canRetry: false, canWrite: false, receipt: null, material: null, imagePath: '', notice: '', busy: !!this.working });
  },
  sync() {
    if (!this.authorized()) return;
    const state = this.writer ? this.writer.state() : { pending: null, canRetry: false };
    this.setData({ pending: state.pending, canRetry: state.canRetry && this.authorized(true), canWrite: this.authorized(true), form: this.selected ? { ...this.form } : null });
    if (this.form.text || this.form.minutes || state.pending) { if (wx.enableAlertBeforeUnload) wx.enableAlertBeforeUnload({ message: '未上交文字只在本次页面内存，离开会丢失；待确认操作仍可能已成功。' }); }
    else if (wx.disableAlertBeforeUnload) wx.disableAlertBeforeUnload();
  },
  failure(e) { return e.status === 409 ? '方案或活动版本已变化，原输入保留。请先查询原回执，再回列表核对当前版本；不会自动覆盖。' : message(e); },
  async load() {
    if (!this.visible || this.working) return;
    if (![this.id, this.versionId, this.schoolId].every(isGuid) || this.stepId && !isGuid(this.stepId)) { this.setData({ error: '方案或活动链接无法核对，请回列表选择当前版本。' }); return; }
    this.working = true; const ticket = ++this.version; this.clearView(); this.setData({ busy: true, error: '' });
    try {
      const session = getApp().session; await session.refreshWorkspace(); const s = session.getState();
      if (!actor(s) || this.owner && actor(s) !== this.owner || !plan.allowed(s, this.schoolId)) throw Object.assign(new Error('FORBIDDEN'), { code: 'FORBIDDEN' });
      this.owner = actor(s); const context = s.context;
      if (!this.writer) this.writer = command(wx, session, getApp().configuration, this.id, this.schoolId, () => this.authorized());
      this.sync();
      const result = plan.view(await session.request(`/api/app/education-teaching-plan/my-activities-for-school?planId=${this.id}&expectedVersionId=${this.versionId}&schoolId=${this.schoolId}`), this.schoolId, this.id, this.versionId);
      if (ticket !== this.version || !this.authorized() || session.getState().context !== context) return;
      this.view = result;
      this.setData({ plan: result.plan, content: result.content, choices: result.activities.map((a) => ({ id: a.id, title: `${a.order}. ${a.title}` })) });
      const selected = this.stepId ? result.activities.find((a) => a.stepId === this.stepId) : result.activities[0];
      if (this.stepId && !selected) { this.setData({ error: '所选活动当前无法定位。可先查询原回执，再回列表选择当前方案。' }); return; }
      if (selected) this.applySelection(selected);
      this.sync();
    } catch (e) { if (this.visible && ticket === this.version) this.setData({ error: this.failure(e) }); }
    finally { this.working = false; if (!this.disposed) { this.setData({ busy: false }); if (ticket === this.version) this.sync(); } }
    if (this.selected && this.authorized()) return this.history();
  },
  applySelection(row) {
    this.selected = row; this.stepId = row.stepId;
    if (this.base === undefined) this.base = row.currentStudentRevisionId;
    this.setData({ activity: row, selectedIndex: this.view.activities.findIndex((a) => a.id === row.id), history: [], historyTotal: 0, offset: this.offset, conflict: this.base !== row.currentStudentRevisionId, material: null, imagePath: '' });
  },
  async select(event) {
    const row = this.view && this.view.activities[Number(event.detail.value)];
    if (!this.authorized() || this.working || !row || this.data.pending || this.selected && row.id === this.selected.id) return;
    const ticket = this.version;
    if ((this.form.text || this.form.minutes || this.form.share || this.form.activityOnly) && !await ask('切换活动会丢弃本次未上交输入，云端已上交记录保留。确定切换？')) return;
    if (ticket !== this.version || !this.authorized() || this.working || this.data.pending) return;
    this.clearFiles(); this.rawMaterial = null; this.form = blank(); this.base = undefined; this.offset = 0; this.applySelection(row); this.sync(); return this.history();
  },
  async act(work) {
    if (!this.authorized() || this.working) return null;
    this.working = true; const ticket = this.version, context = getApp().session.getState().context; this.setData({ busy: true, error: '', notice: '' });
    try { const result = await work(); if (ticket === this.version && this.authorized() && getApp().session.getState().context === context) return result; }
    catch (e) { if (ticket === this.version && this.authorized() && getApp().session.getState().context === context) this.setData({ error: this.failure(e) }); }
    finally { this.working = false; if (!this.disposed) { this.setData({ busy: false }); if (ticket === this.version) this.sync(); } }
    return null;
  },
  async history() {
    const row = this.selected; if (!row || this.working) return;
    this.setData({ history: [], historyTotal: 0 });
    const data = await this.act(() => getApp().session.request(`/api/app/education-teaching-plan/my-activity-history?activityId=${row.id}&${row.currentStudentRevisionId ? 'expectedStudentRevisionId=' + row.currentStudentRevisionId + '&' : ''}SkipCount=${this.offset}&MaxResultCount=20`));
    if (!data || !this.authorized() || !this.selected || this.selected.id !== row.id) return;
    try {
      if (!Array.isArray(data.items) || !Number.isSafeInteger(data.totalCount) || data.totalCount < 0) throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' });
      this.setData({ history: data.items.map((r) => plan.event(r, row.id)), historyTotal: data.totalCount, offset: this.offset });
    } catch (e) { this.setData({ error: message(e) }); }
  },
  paginate(event) { const d = Number(event.currentTarget.dataset.direction); if (!this.authorized() || this.working || ![-1, 1].includes(d) || this.offset + d * 20 < 0 || d === 1 && this.offset + 20 >= this.data.historyTotal) return; this.offset += d * 20; return this.history(); },
  change(event) {
    if (!this.authorized(true) || this.working || !this.selected || this.data.pending) return;
    const field = event.currentTarget.dataset.field;
    if (['text', 'minutes'].includes(field)) this.form[field] = event.detail.value;
    else if (field === 'difficulty') this.form.difficulty = Number(event.detail.value);
    else if (['share', 'activityOnly'].includes(field)) this.form[field] = event.detail.value.includes('yes');
    this.sync();
  },
  async progress(event) {
    const action = Number(event.currentTarget.dataset.action), row = this.selected;
    if (!this.authorized(true) || this.working || !row || this.data.pending || this.data.conflict || ![0, 1, 2].includes(action) || action === 0 && !row.canStart || action === 1 && !row.canComplete || action === 2 && !row.canReport) return;
    const text = this.form.text.trim() ? this.form.text : null; const minutes = action === 1 && this.form.minutes !== '' ? Number(this.form.minutes) : null;
    if (text && (!this.form.share || text.length > 2000) || action === 0 && row.current && row.current.action === 1 && !text ||
        action === 1 && (!this.form.activityOnly || minutes !== null && (!Number.isInteger(minutes) || minutes < 1 || minutes > 1440)) ||
        action === 2 && (!text || !this.form.share || ![0, 1, 2, 3].includes(this.form.difficulty))) { this.setData({ error: '请核对共享勾选和活动确认；困难或重新开始需要说明，用时可留空或填 1–1440 分钟。' }); return; }
    const input = { expectedStudentRevisionId: this.base, action, difficulty: action === 2 ? this.form.difficulty : null, sharedText: text, reportedMinutes: minutes, confirmSharingWithTeacher: this.form.share, confirmActivityOnly: action === 1 && this.form.activityOnly };
    const ticket = this.version, snapshot = JSON.stringify(this.form), activityId = row.id;
    if (!await ask(`${['记录开始学习', '上交自报完成（不表示已经掌握）', '上交困难反馈'][action]}？${text ? '所填文字会共享给本方案教师。' : '本次没有上交文字。'}`)) return;
    if (ticket !== this.version || !this.authorized(true) || this.working || snapshot !== JSON.stringify(this.form) || this.data.pending || !this.selected || this.selected.id !== activityId) return;
    const receipt = await this.act(() => this.writer.start(activityId, input)); if (receipt) return this.saved(receipt);
  },
  async saved(row) {
    const receipt = plan.event(row, row.activityId); this.form = blank(); this.base = undefined; this.offset = 0;
    await this.load(); if (this.authorized()) this.setData({ receipt });
  },
  async recover() {
    if (!this.writer) return; const receipt = await this.act(() => this.writer.recover());
    if (receipt) return this.saved(receipt);
    if (this.authorized() && !this.data.error) this.setData({ notice: '尚未查到原回执不代表失败。仅可明确重试仍在本页面内存中的同一请求，或核对历史后关闭本机追踪。' });
  },
  async retry() {
    const op = this.data.pending && this.data.pending.operationId;
    if (!op || !this.data.canRetry || !await ask('先查询原回执，仅在未查到时重发同一活动、原修订和原内容。确定重试？')) return;
    if (!this.writer || this.writer.state().pending?.operationId !== op || !this.authorized(true)) return;
    const receipt = await this.act(() => this.writer.retry()); if (receipt) return this.saved(receipt);
  },
  async abandon() {
    const op = this.data.pending && this.data.pending.operationId;
    if (!op || !await ask('仅关闭本机追踪，不撤销可能已成功的云端操作。未上交输入会丢失，请核对原记录。确定继续？')) return;
    if (!this.writer || this.writer.state().pending?.operationId !== op || !this.authorized()) return;
    const done = await this.act(async () => { this.writer.abandon(); return true; }); if (done) { this.form = blank(); this.base = undefined; this.offset = 0; return this.load(); }
  },
  async discard() {
    const ticket = this.version;
    if (this.data.pending || !this.authorized() || !await ask('丢弃本机未上交输入并重新读取这个方案版本？不会覆盖云端记录。')) return;
    if (ticket === this.version && this.authorized() && !this.data.pending) { this.form = blank(); this.base = undefined; this.offset = 0; return this.load(); }
  },
  async material(event) {
    const id = event.currentTarget.dataset.id, activity = this.selected;
    if (!this.authorized(true) || this.working || !activity || !activity.canReadMaterials || !activity.materials.some((m) => m.id === id)) return;
    this.clearFiles(); this.rawMaterial = null; this.setData({ material: null, imagePath: '' });
    const data = await this.act(() => getApp().session.request(`/api/app/education-teaching-plan/my-material?activityId=${activity.id}&materialId=${id}`));
    if (data && this.authorized()) { try { this.rawMaterial = study.material(data, id); this.setData({ material: study.materialPage(this.rawMaterial, 0) }); } catch (e) { this.setData({ error: message(e) }); } }
  },
  materialPage(event) {
    const d = Number(event.currentTarget.dataset.direction); if (!this.authorized() || this.working || !this.rawMaterial || !this.data.material || ![-1, 1].includes(d)) return;
    const index = this.data.material.page - 1 + d; if (index < 0 || index >= this.data.material.pages) return; this.setData({ material: study.materialPage(this.rawMaterial, index) });
  },
  async attachment(event) {
    const id = event.currentTarget.dataset.id, material = this.data.material, activity = this.selected;
    const file = material && material.attachments.find((f) => f.id === id);
    if (!this.authorized(true) || this.working || !file || !file.type || !activity) return;
    const ticket = this.version, context = getApp().session.getState().context;
    const path = await this.act(async () => {
      const result = await getApp().session.download(`/api/education/teaching/activities/${activity.id}/materials/${material.id}/attachments/${id}`);
      if (ticket !== this.version || !this.authorized() || getApp().session.getState().context !== context) { getApp().session.releaseFile(result); return null; }
      this.clearFiles(); this.files.add(result); return result;
    });
    if (!path || !this.authorized()) return;
    if (['jpg', 'jpeg', 'png', 'webp'].includes(file.type)) this.setData({ imagePath: path });
    else { try { wx.openDocument({ filePath: path, fileType: file.type, showMenu: false, fail: () => { if (ticket === this.version && this.authorized()) { this.clearFiles(); this.setData({ error: '当前设备无法预览此附件，请在网页阅读。' }); } } }); } catch (_) { this.clearFiles(); this.setData({ error: '当前设备无法预览此附件，请在网页阅读。' }); } }
  },
  closeImage() { this.clearFiles(); this.setData({ imagePath: '' }); },
  assessment() {
    const f = this.selected && this.selected.assessment;
    if (!this.authorized() || this.working || !f || !f.canOpen || !getApp().session.getState().context.grantedPermissions.includes('KnowPulse.Education.AssessmentParticipate')) return;
    wx.navigateTo({ url: `/pages/assessment-answer/index?schoolId=${this.schoolId}&id=${f.recipientId}` });
  },
  list() { if (plan.allowed(getApp().session.getState(), this.schoolId)) wx.navigateTo({ url: `/pages/teaching-plans/index?schoolId=${this.schoolId}` }); },
  web() { if (!this.authorized()) return; const url = getApp().session.portalUrl(`/education/teaching-plans/${this.id}/activities?versionId=${this.versionId}`); if (url) wx.setClipboardData({ data: url }); else this.setData({ error: '尚未配置网页地址，请联系学校或平台管理员。' }); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
