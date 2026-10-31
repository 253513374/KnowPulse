const plan = require('../../utils/plan');
const readScope = require('../../utils/read-scope');

Page({
  data: { items: [], total: 0, offset: 0, busy: false, error: '' },
  onLoad(query) { this.schoolId = typeof query.schoolId === 'string' ? query.schoolId.toLowerCase() : query.schoolId; this.offset = 0; this.reader = readScope(this, getApp(), (s) => plan.allowed(s, this.schoolId), () => ({ items: [], total: 0 })); },
  onShow() { this.reader.show(); return this.load(); },
  onHide() { this.reader.hide(); },
  onUnload() { this.reader.dispose(); },
  load() { return this.reader.run(async () => {
    const row = await getApp().session.request(`/api/app/education-teaching-plan/my-list?schoolId=${this.schoolId}&SkipCount=${this.offset}&MaxResultCount=20`);
    if (!row || !Number.isSafeInteger(row.totalCount) || row.totalCount < 0 || !Array.isArray(row.items)) throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' });
    return { items: row.items.map((r) => plan.card(r, this.schoolId)), total: row.totalCount };
  }, (r) => this.setData({ ...r, offset: this.offset })); },
  paginate(e) { const d = Number(e.currentTarget.dataset.direction); if (this.data.busy || ![-1, 1].includes(d) || this.offset + d * 20 < 0 || d === 1 && this.offset + 20 >= this.data.total) return; this.offset += d * 20; return this.load(); },
  open(e) { const row = this.data.items.find((r) => r.id === e.currentTarget.dataset.id); if (!this.data.busy && row && plan.allowed(getApp().session.getState(), this.schoolId)) wx.navigateTo({ url: `/pages/teaching-plan/index?schoolId=${this.schoolId}&id=${row.id}&versionId=${row.versionId}` }); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
