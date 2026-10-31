const growth = require('../../utils/growth');
const readScope = require('../../utils/read-scope');
Page({
  data: { rows: [], total: 0, offset: 0, busy: false, error: '' },
  onLoad(q) {
    this.schoolId = q.schoolId; this.offset = 0;
    this.reader = readScope(this, getApp(), (s) => growth.allowed(s, this.schoolId), () => { this.rows = []; return { rows: [], total: 0 }; });
  },
  onShow() { this.visible = true; this.reader.show(); return this.load(); },
  onHide() { this.visible = false; this.reader.hide(); },
  onUnload() { this.visible = false; this.reader.dispose(); },
  load() { return this.reader.run(async () => growth.contexts(await getApp().session.request(`/api/app/education-student-growth/my-contexts-for-school/${this.schoolId}`), this.schoolId),
    (rows) => { this.rows = rows; this.setData({ rows: rows.slice(this.offset, this.offset + 20), total: rows.length, offset: this.offset }); }); },
  paginate(e) { const d = Number(e.currentTarget.dataset.direction); if (this.data.busy || ![-1, 1].includes(d) || this.offset + d * 20 < 0 || d > 0 && this.offset + 20 >= this.data.total) return; this.offset += d * 20; return this.load(); },
  open(e) { const row = this.rows.find((r) => r.id === e.currentTarget.dataset.id); if (!this.visible || this.data.busy || !row || !growth.allowed(getApp().session.getState(), this.schoolId)) return;
    wx.navigateTo({ url: `/pages/growth/index?schoolId=${this.schoolId}&id=${row.id}` }); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
