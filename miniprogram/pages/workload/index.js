const workload = require('../../utils/workload');
const readScope = require('../../utils/read-scope');
const message = require('../../utils/messages');

Page({
  data: { from: '', to: '', days: [], items: [], total: 0, offset: 0, focusedDay: '', knownMinutes: null, unestimated: 0, omitted: '', partial: false, calculatedAt: '', busy: false, error: '', notice: '' },
  onLoad(query) {
    this.schoolId = query.schoolId; this.offset = 0; this.focusedDay = '';
    const now = new Date(); this.setData({ from: now.toISOString().slice(0, 10), to: new Date(now.getTime() + 14 * 86400000).toISOString().slice(0, 10) });
    this.reader = readScope(this, getApp(), (s) => workload.allowed(s, this.schoolId), () => this.clear());
  },
  clear() { this.result = null; return { days: [], items: [], total: 0, knownMinutes: null, unestimated: 0, omitted: '', partial: false, calculatedAt: '', notice: '' }; },
  onShow() { this.visible = true; this.reader.show(); return this.load(); },
  onHide() { this.visible = false; this.reader.hide(); },
  onUnload() { this.visible = false; this.reader.dispose(); this.result = null; },
  change(event) {
    if (this.data.busy || !['from', 'to'].includes(event.currentTarget.dataset.field)) return;
    this.setData({ ...this.clear(), [event.currentTarget.dataset.field]: event.detail.value, error: '', focusedDay: '', offset: 0 }); this.focusedDay = ''; this.offset = 0;
  },
  load() {
    return this.reader.run(async () => {
      const requested = workload.window(this.data.from, this.data.to);
      const query = `StartsAtUtc=${encodeURIComponent(requested.startsAtUtc)}&EndsAtUtc=${encodeURIComponent(requested.endsAtUtc)}&schoolId=${this.schoolId}`;
      return workload.project(await getApp().session.request('/api/app/education-teaching-plan/my-learning-workload?' + query), this.schoolId, requested);
    }, (result) => { this.result = result; this.offset = 0; this.focusedDay = ''; this.render(); });
  },
  render() {
    if (!this.visible || !this.result || !workload.allowed(getApp().session.getState(), this.schoolId)) return;
    const result = this.result; const rows = result.items.filter((r) => !this.focusedDay || r.dueDate === this.focusedDay);
    this.setData({ days: result.days, items: rows.slice(this.offset, this.offset + 20), total: rows.length, offset: this.offset, focusedDay: this.focusedDay,
      knownMinutes: result.knownMinutes, unestimated: result.unestimated, omitted: result.omitted, partial: result.partial, calculatedAt: result.calculatedAt });
  },
  focus(event) {
    if (this.data.busy || !this.result) return;
    const day = event.currentTarget.dataset.day;
    if (day && !this.result.days.some((d) => d.date === day)) return;
    this.focusedDay = day || ''; this.offset = 0; this.render();
  },
  paginate(event) {
    const d = Number(event.currentTarget.dataset.direction);
    if (this.data.busy || ![-1, 1].includes(d) || this.offset + d * 20 < 0 || d === 1 && this.offset + 20 >= this.data.total) return;
    this.offset += d * 20; this.render();
  },
  open(event) {
    if (!this.visible || this.data.busy || !this.result) return;
    const row = this.data.items.find((r) => r.key === event.currentTarget.dataset.key);
    const state = getApp().session.getState(); const target = workload.target(row, this.schoolId, state);
    if (!target) { this.setData({ error: message({ code: 'FORBIDDEN' }) }); return; }
    wx.navigateTo({ url: target.url });
  },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
