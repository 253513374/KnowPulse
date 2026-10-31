const growth = require('../../utils/growth');
const { isGuid } = require('../../utils/assessment');
const readScope = require('../../utils/read-scope');
Page({
  data: { head: null, point: null, rows: [], total: 0, restricted: false, offset: 0, busy: false, error: '' },
  onLoad(q) {
    for (const key of ['schoolId', 'id', 'snapshotId', 'pointId', 'from', 'to']) this[key] = q[key]; this.offset = 0;
    this.reader = readScope(this, getApp(), (s) => [this.id, this.snapshotId, this.pointId].every(isGuid) && growth.allowed(s, this.schoolId), () => ({ head: null, point: null, rows: [], total: 0, restricted: false }));
  },
  onShow() { this.visible = true; this.reader.show(); return this.load(); },
  onHide() { this.visible = false; this.reader.hide(); },
  onUnload() { this.visible = false; this.reader.dispose(); },
  load() { return this.reader.run(async () => {
    const session = getApp().session, requested = growth.fixedWindow(this.from, this.to);
    const report = await growth.read(session, this.schoolId, this.id, requested, this.snapshotId), point = report.points.find((r) => r.id === this.pointId); if (!point) throw growth.error('GROWTH_CHANGED');
    const rows = growth.evidence(await session.request(`/api/app/education-student-growth/my-point-evidence-for-school?contextId=${this.id}&schoolId=${this.schoolId}&snapshotId=${this.snapshotId}&pointId=${this.pointId}&SkipCount=${this.offset}&MaxResultCount=20`), this.schoolId, report);
    return { report, point, rows };
  }, ({ report, point, rows }) => this.setData({ head: { context: report.context, snapshotId: report.snapshotId, from: report.from, to: report.to, calculated: report.calculated }, point, rows: rows.items, total: rows.total, restricted: rows.restricted, offset: this.offset })); },
  paginate(e) { const d = Number(e.currentTarget.dataset.direction); if (this.data.busy || ![-1, 1].includes(d) || this.offset + d * 20 < 0 || d > 0 && this.offset + 20 >= this.data.total) return; this.offset += d * 20; return this.load(); },
  result(e) { const row = this.data.rows.find((r) => r.id === e.currentTarget.dataset.id); if (!this.visible || this.data.busy || !row || !growth.allowed(getApp().session.getState(), this.schoolId)) return; wx.navigateTo({ url: growth.resultUrl(row, this.schoolId) }); },
  back() { if (!this.visible || !isGuid(this.id) || !growth.allowed(getApp().session.getState(), this.schoolId)) return; wx.navigateTo({ url: `/pages/growth/index?schoolId=${this.schoolId}&id=${this.id}` }); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
