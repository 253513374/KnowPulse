const inbox = require('../../utils/inbox');
const { actor } = require('../../utils/feedback');
const readScope = require('../../utils/read-scope');
const messages = require('../../utils/messages');
Page({
  data: { sources: [], index: 0, items: [], total: 0, offset: 0, busy: false, error: '', notice: '' },
  onLoad(q) {
    this.schoolId = typeof q.schoolId === 'string' ? q.schoolId.toLowerCase() : ''; this.source = ['0', '1', '2'].includes(q.source) ? Number(q.source) : null; this.offset = 0; this.epoch = 0;
    this.reader = readScope(this, getApp(), (s) => this.source == null ? inbox.enabled(s, this.schoolId) : inbox.allowed(s, this.schoolId, this.source), () => {
      this.epoch++; this.context = null; return { sources: [], items: [], total: 0, notice: '' };
    });
  },
  onShow() { this.visible = true; this.reader.show(); return this.load(); }, onHide() { this.visible = false; this.reader.hide(); }, onUnload() { this.onHide(); this.disposed = true; this.reader.dispose(); },
  alive() { const s = getApp().session.getState(); return this.visible && !this.disposed && this.owner === actor(s) && this.context === s.context && this.generation === s.generation && inbox.allowed(s, this.schoolId, this.source); },
  load() { return this.reader.run(async () => {
    const choices = inbox.sources(await getApp().session.request(`/api/app/education-student-inbox/sources?SchoolId=${this.schoolId}`), getApp().session.getState(), this.schoolId);
    if (this.source == null && choices.length) this.source = choices[0].id;
    const index = choices.findIndex((x) => x.id === this.source);
    if (index < 0) return { sources: choices, index: 0, items: [], total: 0, notice: '当前来源不可用，请明确重新选择可用来源。' };
    const dto = await getApp().session.request(`/api/app/education-student-inbox/my-list?SchoolId=${this.schoolId}&Source=${this.source}&SkipCount=${this.offset}&MaxResultCount=20`);
    return { ...inbox.page(dto, this.schoolId, this.source), sources: choices, index, notice: '' };
  }, (view) => { const s = getApp().session.getState(); this.owner = actor(s); this.context = s.context; this.generation = s.generation; this.setData({ ...view, offset: this.offset }); }); },
  choose(e) { if (this.data.busy) return; const row = this.data.sources[Number(e.detail.value)]; if (!row || !inbox.allowed(getApp().session.getState(), this.schoolId, row.id)) return; this.source = row.id; this.offset = 0; return this.load(); },
  resetSource() { if (this.data.busy) return; this.source = null; this.offset = 0; return this.load(); },
  async open(e) {
    if (!this.alive() || this.data.busy) return; const row = this.data.items.find((x) => x.id === e.currentTarget.dataset.id); if (!row) return;
    const ticket = this.epoch; this.setData({ busy: true, error: '', notice: '' });
    try {
      const dto = await getApp().session.request('/api/app/education-student-inbox/read-my', 'POST', { schoolId: this.schoolId, source: this.source, messageId: row.id });
      if (!this.alive() || ticket !== this.epoch) return;
      const current = inbox.message(dto, this.schoolId, this.source);
      if (current.id !== row.id || current.kind !== row.kind || current.referenceId !== row.referenceId || current.publicationId !== row.publicationId || current.resultReleaseId !== row.resultReleaseId || current.stepId !== row.stepId || !current.isRead) throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' });
      const url = inbox.destination(current);
      if (url) wx.navigateTo({ url }); else { await this.load(); if (this.alive()) this.setData({ notice: '消息已标记已读。对应内容已撤回或变化，请从原入口明确重选；不会打开其他版本。' }); }
    } catch (e) { if (this.alive() && ticket === this.epoch) this.setData({ error: messages(e) }); }
    finally { if (this.alive() && ticket === this.epoch) this.setData({ busy: false }); }
  },
  paginate(e) { const n = Number(e.currentTarget.dataset.direction); if (!this.alive() || this.data.busy || ![-1, 1].includes(n) || this.offset + n * 20 < 0 || n === 1 && this.offset + 20 >= this.data.total) return; this.offset += n * 20; return this.load(); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
