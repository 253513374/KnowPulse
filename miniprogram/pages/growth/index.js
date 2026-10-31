const growth = require('../../utils/growth');
const { isGuid } = require('../../utils/assessment');
const readScope = require('../../utils/read-scope');
Page({
  data: { head: null, from: '', to: '', points: [], assessments: [], pointTotal: 0, assessmentTotal: 0, pointOffset: 0, assessmentOffset: 0, busy: false, error: '' },
  onLoad(q) {
    this.schoolId = q.schoolId; this.id = q.id; this.incoming = q.from || q.to ? { from: q.from, to: q.to } : null; this.pointOffset = 0; this.assessmentOffset = 0;
    this.reader = readScope(this, getApp(), (s) => isGuid(this.id) && growth.allowed(s, this.schoolId), () => { this.report = null; return { head: null, points: [], assessments: [], pointTotal: 0, assessmentTotal: 0 }; });
  },
  onShow() { this.visible = true; this.reader.show(); return this.load(); },
  onHide() { this.visible = false; this.reader.hide(); },
  onUnload() { this.visible = false; this.reader.dispose(); },
  load(expected) { return this.reader.run(async () => {
    const session = getApp().session; const rows = growth.contexts(await session.request(`/api/app/education-student-growth/my-contexts-for-school/${this.schoolId}`), this.schoolId);
    const context = rows.find((r) => r.id === this.id); if (!context) throw growth.error('GROWTH_CHANGED');
    const now = Date.now(), today = new Date(now).toISOString().slice(0, 10);
    const from = this.data.from || new Date(Date.parse(today + 'T00:00:00Z') - Math.min(29, context.maximumWindowDays - 1) * 86400000).toISOString().slice(0, 10), to = this.data.to || today;
    if (!this.requested) this.requested = this.incoming ? growth.fixedWindow(this.incoming.from, this.incoming.to) : growth.window(from, to, context.maximumWindowDays, now);
    const report = await growth.read(session, this.schoolId, this.id, this.requested, expected);
    return { report, from: this.incoming ? this.requested.startsAtUtc.slice(0, 10) : from, to: this.incoming ? new Date(Date.parse(this.requested.endsAtUtc) - 1).toISOString().slice(0, 10) : to };
  }, ({ report, from, to }) => { this.report = report; const { points, assessments, ...head } = report;
    this.setData({ head, from, to, points: points.slice(this.pointOffset, this.pointOffset + 20), assessments: assessments.slice(this.assessmentOffset, this.assessmentOffset + 20), pointTotal: points.length, assessmentTotal: assessments.length, pointOffset: this.pointOffset, assessmentOffset: this.assessmentOffset }); }); },
  change(e) { const field = e.currentTarget.dataset.field; if (this.data.busy || !['from', 'to'].includes(field)) return;
    this.requested = null; this.incoming = null; this.report = null; this.pointOffset = 0; this.assessmentOffset = 0; this.setData({ [field]: e.detail.value, head: null, points: [], assessments: [], pointTotal: 0, assessmentTotal: 0 }); },
  view() { if (this.data.busy) return; this.requested = null; this.incoming = null; this.pointOffset = 0; this.assessmentOffset = 0; return this.load(); },
  refresh() { this.pointOffset = 0; this.assessmentOffset = 0; return this.load(); },
  paginate(e) { const kind = e.currentTarget.dataset.kind, d = Number(e.currentTarget.dataset.direction), current = this.report; if (this.data.busy || !current || !['point', 'assessment'].includes(kind) || ![-1, 1].includes(d)) return;
    const field = kind + 'Offset', total = this.data[kind + 'Total']; if (this[field] + d * 20 < 0 || d > 0 && this[field] + 20 >= total) return; this[field] += d * 20; return this.load(current.snapshotId); },
  basis(e) { const report = this.report, point = this.data.points.find((p) => p.id === e.currentTarget.dataset.id); if (!this.visible || this.data.busy || !report || !point || !growth.allowed(getApp().session.getState(), this.schoolId)) return;
    wx.navigateTo({ url: `/pages/growth-evidence/index?schoolId=${this.schoolId}&id=${this.id}&snapshotId=${report.snapshotId}&pointId=${point.id}&${growth.query(this.requested)}` }); },
  result(e) { const row = this.data.assessments.find((r) => r.id === e.currentTarget.dataset.id); if (!this.visible || this.data.busy || !row || !growth.allowed(getApp().session.getState(), this.schoolId)) return; wx.navigateTo({ url: growth.resultUrl(row, this.schoolId) }); },
  web() { if (!this.visible || this.data.busy || !growth.allowed(getApp().session.getState(), this.schoolId)) return; const url = getApp().session.portalUrl('/education/growth'); if (url) wx.setClipboardData({ data: url }); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
