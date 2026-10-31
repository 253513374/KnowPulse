const reminders = require('../../utils/reminder');
const assessment = require('../../utils/assessment');
const { actor } = require('../../utils/feedback');
const readScope = require('../../utils/read-scope');
const message = require('../../utils/messages');
const ask = (content) => new Promise((resolve) => wx.showModal({ title: '核对测评提醒', content, success: (r) => resolve(!!r.confirm), fail: () => resolve(false) }));
Page({
  data: { mode: '', items: [], total: 0, offset: 0, preview: null, pending: null, receipt: null, busy: false, error: '', notice: '' },
  onLoad(q) {
    this.mode = q.mode; this.schoolId = q.schoolId; this.taskId = q.taskId; this.offset = 0; this.epoch = 0;
    this.setData({ mode: this.mode });
    this.reader = readScope(this, getApp(), (s) => assessment.allowed(s, this.mode, this.schoolId) && (this.mode === 'student' || assessment.isGuid(this.taskId)), () => {
      this.epoch++; this.context = null; return { items: [], total: 0, preview: null, receipt: null, pending: null, notice: '' };
    });
  },
  onShow() { this.visible = true; this.reader.show(); return this.load(); }, onHide() { this.visible = false; this.reader.hide(); }, onUnload() { this.onHide(); this.disposed = true; this.reader.dispose(); },
  alive() { const s = getApp().session.getState(); return this.visible && !this.disposed && this.owner === actor(s) && this.context === s.context && this.generation === s.generation && assessment.allowed(s, this.mode, this.schoolId); },
  load() { if (this.writing) return; return this.reader.run(async () => {
    const path = this.mode === 'teacher' ? `reminder-targets?TaskId=${this.taskId}&` : 'my-notices?';
    return reminders.page(await getApp().session.request(`/api/app/education-assessment/${path}SchoolId=${this.schoolId}&SkipCount=${this.offset}&MaxResultCount=20`), this.mode, this.schoolId, this.taskId);
  }, (page) => {
    const s = getApp().session.getState(); this.owner = actor(s); this.context = s.context; this.generation = s.generation;
    if (this.mode === 'teacher') this.writer = reminders.command(wx, getApp().session, getApp().configuration, this.schoolId, this.taskId, () => this.alive());
    this.setData({ ...page, offset: this.offset }); this.sync();
  }); },
  sync() { if (this.alive()) this.setData({ pending: this.writer ? this.writer.state().pending : null }); },
  async choose(e) {
    if (!this.alive() || this.mode !== 'teacher' || this.data.busy || this.data.pending) return;
    const row = this.data.items.find((r) => r.id === e.currentTarget.dataset.id); if (!row) return; const ticket = this.epoch; this.setData({ busy: true, preview: null, error: '' });
    try { const dto = await getApp().session.request(`/api/app/education-assessment/reminder-preview?recipientId=${row.id}&schoolId=${this.schoolId}`); if (this.alive() && ticket === this.epoch) { const p = reminders.preview(dto, this.schoolId, this.taskId); if (p.id !== row.id) throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' }); this.setData({ preview: p }); } }
    catch (e) { if (this.alive() && ticket === this.epoch) this.setData({ error: message(e) }); }
    finally { if (this.alive() && ticket === this.epoch) this.setData({ busy: false }); }
  },
  async send() {
    if (!this.alive() || this.data.busy || this.data.pending || !this.data.preview?.canSend) return;
    const row = this.data.preview, ticket = this.epoch;
    if (!await ask(`向“${row.name}”发送“${row.title}”的站内催办？通知仅提示查看任务，不含成绩或答案；微信未配置，不会投递。`)) return;
    if (this.alive() && ticket === this.epoch && this.data.preview === row && !this.data.pending) return this.act(() => this.writer.start(row));
  },
  async act(work) {
    if (!this.alive() || this.writing || this.data.busy) return; const ticket = this.epoch; this.writing = true; this.setData({ busy: true, error: '' }); let done = null;
    try { const r = await work(); if (this.alive() && ticket === this.epoch) { done = r; this.setData({ notice: r ? '站内提醒已核对；微信未配置，未投递。' : '尚未查到回执，可保留标识后明确重试原操作。' }); } }
    catch (e) { if (this.alive() && ticket === this.epoch) this.setData({ error: e.status === 409 ? '预览或处理状态已变化，请先查回执，再关闭追踪并重新预览。' : message(e) }); }
    finally { this.writing = false; if (this.alive() && ticket === this.epoch) { this.setData({ busy: false }); this.sync(); } }
    if (done && this.alive()) { await this.load(); if (this.alive()) this.setData({ receipt: done }); }
  },
  recover() { return this.act(() => this.writer.recover()); },
  async retry() { if (!this.alive() || this.data.busy) return; const ticket = this.epoch; if (await ask('先查原回执，再以原编号和原预览重试？不会重新选择学生或静默采用新状态。') && this.alive() && ticket === this.epoch) return this.act(() => this.writer.retry()); },
  async abandon() { if (!this.alive() || this.data.busy) return; const ticket = this.epoch; if (await ask('确认已核对云端记录后，仅关闭本机追踪？不会撤销可能已发出的站内提醒。') && this.alive() && ticket === this.epoch) { this.writer.abandon(); return this.load(); } },
  async open(e) {
    if (!this.alive() || this.mode !== 'student' || this.data.busy) return; const row = this.data.items.find((r) => r.id === e.currentTarget.dataset.id); if (!row) return;
    const ticket = this.epoch; this.setData({ busy: true, error: '' });
    try { const dto = await getApp().session.request(`/api/app/education-assessment/${row.id}/read-my-reminder/${this.schoolId}`, 'POST');
      if (this.alive() && ticket === this.epoch) {
        const r = reminders.receipt(dto, this.schoolId, row.taskId, row.recipientId);
        if (r.id !== row.id || r.publicationId !== row.publicationId || r.kind !== row.kind || r.resultReleaseId !== row.resultReleaseId || dto.readAtUtc == null) throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' });
        if (!r.canOpen) { await this.load(); if (this.alive()) this.setData({ notice: '该结果已被替代或重新隐藏，消息已读。请从当前成绩入口明确重选，不会自动换入其他版本。' }); return; }
        wx.navigateTo({ url: r.kind === 2 ? `/pages/assessment-result/index?schoolId=${this.schoolId}&id=${r.recipientId}&taskRevisionId=${r.publicationId}&releaseId=${r.resultReleaseId}` : `/pages/assessment-answer/index?schoolId=${this.schoolId}&id=${r.recipientId}` });
      }
    } catch (e) { if (this.alive() && ticket === this.epoch) this.setData({ error: message(e) }); }
    finally { if (this.alive() && ticket === this.epoch) this.setData({ busy: false }); }
  },
  paginate(e) { const n = Number(e.currentTarget.dataset.direction); if (!this.alive() || this.data.busy || this.data.pending || ![-1, 1].includes(n) || this.offset + n * 20 < 0 || n === 1 && this.offset + 20 >= this.data.total) return; this.offset += n * 20; return this.load(); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
