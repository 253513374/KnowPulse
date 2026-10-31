const plan = require('../../utils/teacher-plan');
const command = require('../../utils/teacher-plan-command');
const readScope = require('../../utils/read-scope');
const { actor } = require('../../utils/feedback');
const { isGuid } = require('../../utils/assessment');
const message = require('../../utils/messages');
const ask = (content) => new Promise((resolve) => wx.showModal({ title: '核对私有跟进', content, success: (r) => resolve(!!r.confirm), fail: () => resolve(false) }));
Page({
  data: { header: null, versions: [], versionIndex: 0, choices: [], selectedIndex: 0, activity: null, note: '', confirm: false, pending: null, canRetry: false, busy: false, error: '', notice: '' },
  onLoad(q) {
    for (const key of ['schoolId', 'assignmentId', 'id', 'versionId']) this[key] = typeof q[key] === 'string' ? q[key].toLowerCase() : '';
    this.epoch = 0; this.visible = false;
    this.reader = readScope(this, getApp(), (s) => plan.allowed(s, this.schoolId, this.assignmentId), () => {
      this.epoch++; this.view = null; this.selected = null; if (this.writer) this.writer.dropPayload();
      return { header: null, versions: [], choices: [], activity: null, note: '', confirm: false, pending: null, canRetry: false, notice: '' };
    }, () => { if (this.writer) this.writer.dropPayload(); });
  },
  onShow() { this.visible = true; this.reader.show(); return this.load(); },
  onHide() { this.visible = false; this.reader.hide(); }, onUnload() { this.onHide(); this.disposed = true; this.reader.dispose(); },
  alive() { const s = getApp().session.getState(); return this.visible && !this.disposed && !!this.view && this.owner === actor(s) && this.context === s.context && this.generation === s.generation && plan.allowed(s, this.schoolId, this.assignmentId); },
  load() {
    if (this.writing) return;
    if (![this.schoolId, this.assignmentId, this.id, this.versionId].every(isGuid)) { this.setData({ error: '方案链接不完整，请回教师方案列表重新选择。' }); return; }
    return this.reader.run(async () => plan.view(await getApp().session.request(`/api/app/education-teaching-plan/activity-progress-for-school?planId=${this.id}&publishedRevisionId=${this.versionId}&schoolId=${this.schoolId}&teachingAssignmentId=${this.assignmentId}`), this.schoolId, this.assignmentId, this.id, this.versionId), (v) => {
      const s = getApp().session.getState(); this.owner = actor(s); this.context = s.context; this.generation = s.generation; this.view = v;
      this.writer = command(wx, getApp().session, getApp().configuration, this.id, this.schoolId, this.assignmentId, () => this.alive());
      this.setData({ header: v.header, versions: v.versions, versionIndex: v.versions.findIndex((x) => x.id === this.versionId), choices: v.activities.map((a) => ({ id: a.id, title: `${a.order}. ${a.title} · ${a.stateLabel}` })) });
      this.selectRow(v.activities.find((a) => a.id === this.selectedId) || v.activities[0]); this.sync();
    });
  },
  async refresh() { if (this.data.busy || this.data.pending) return; const ticket = this.epoch; if (this.data.note && !await ask('丢弃未保存的私有文字并刷新所选版本？')) return; if (ticket === this.epoch && this.visible && !this.disposed) return this.load(); },
  selectRow(row) { this.selected = row; this.selectedId = row?.id; this.setData({ activity: row || null, selectedIndex: row ? this.view.activities.findIndex((a) => a.id === row.id) : 0, note: '', confirm: false }); },
  sync() { if (!this.alive()) return; const s = this.writer.state(); this.setData({ pending: s.pending, canRetry: s.canRetry }); if ((this.data.note || s.pending) && wx.enableAlertBeforeUnload) wx.enableAlertBeforeUnload({ message: '私有文字只在本页内存；离开后待确认操作仍可能已保存，请先查原回执。' }); else if (wx.disableAlertBeforeUnload) wx.disableAlertBeforeUnload(); },
  async choose(e) { const row = this.view?.activities[Number(e.detail.value)]; if (!row || this.data.busy || this.data.pending) return; const ticket = this.epoch; if (this.data.note && !await ask('丢弃本页未保存的私有文字并切换活动？')) return; if (ticket === this.epoch && this.alive()) { this.selectRow(row); this.sync(); } },
  async version(e) { const row = this.view?.versions[Number(e.detail.value)]; if (!row || this.data.busy || this.data.pending) return; const ticket = this.epoch; if (this.data.note && !await ask('丢弃未保存的私有文字并明确切换发布版本？')) return; if (ticket === this.epoch && this.alive()) { this.versionId = row.id; this.selectedId = null; return this.load(); } },
  change(e) { if (!this.alive() || this.data.busy || this.data.pending) return; if (e.currentTarget.dataset.field === 'confirm') this.setData({ confirm: e.detail.value.includes('yes') }); else this.setData({ note: e.detail.value }); this.sync(); },
  async save() {
    if (!this.alive() || this.data.busy || this.data.pending || !this.selected?.canHandle || !this.data.confirm || !this.data.note.trim() || this.data.note.length > 2000) return;
    const row = this.selected, ticket = this.epoch, note = this.data.note;
    if (!await ask('只为这条当前困难保存本人私有跟进。学生只看到处理时间；不发送备注，不改变完成状态或分数。')) return;
    if (!this.alive() || ticket !== this.epoch || this.selected !== row || this.data.pending) return;
    return this.act(() => this.writer.start(row.id, { expectedRevisionId: row.versionId, expectedStudentRevisionId: row.currentStudentId, concurrencyStamp: row.stamp, internalTeacherNote: note, confirmHandlingReceipt: true }));
  },
  async act(work) {
    if (!this.alive() || this.writing) return; const ticket = this.epoch; this.writing = true; this.setData({ busy: true, error: '', notice: '' }); let confirmed = false;
    try { const receipt = await work(); if (this.alive() && ticket === this.epoch) { confirmed = !!receipt; this.setData({ notice: receipt ? '已核对保存记录，正在重读当前进度。' : '尚未查到原回执。保留待确认标识；本页有原请求时可明确重试。' }); } }
    catch (e) { if (this.alive() && ticket === this.epoch) this.setData({ error: e.status === 409 ? '发布版本或当前反馈已变化。先查原回执，再明确关闭追踪并重读；不会覆盖新记录。' : message(e) }); }
    finally { this.writing = false; if (this.visible && !this.disposed) { this.setData({ busy: false }); this.sync(); } }
    if (confirmed && this.alive()) return this.load();
  },
  recover() { return this.act(() => this.writer.recover()); }, retry() { return this.act(() => this.writer.retry()); },
  async abandon() { if (!this.alive() || this.data.busy) return; const ticket = this.epoch; if (!await ask('先核对服务器记录。只关闭本机追踪并重读？不会撤销可能已保存的跟进，未保存文字将丢弃。')) return; if (this.alive() && ticket === this.epoch) { this.writer.abandon(); return this.load(); } },
  web(e) { if (!this.alive() || this.data.busy) return; const action = e.currentTarget.dataset.action === 'execution' ? 'execution' : 'progress'; const url = getApp().session.portalUrl(`/education/teaching-plans/${this.id}/${action}`); if (url) wx.setClipboardData({ data: url }); else this.setData({ error: '尚未配置教师网页地址。' }); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
