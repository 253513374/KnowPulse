const correction = require('../../utils/correction');
const readScope = require('../../utils/read-scope');
Page({
  data: { items: [], total: 0, offset: 0, busy: false, error: '' },
  onLoad(query) { this.schoolId = typeof query.schoolId === 'string' ? query.schoolId.toLowerCase() : ''; this.offset = 0; this.reader = readScope(this, getApp(), (s) => correction.allowed(s, this.schoolId), () => ({ items: [], total: 0 })); },
  onShow() { this.reader.show(); return this.load(); },
  onHide() { this.reader.hide(); },
  onUnload() { this.reader.dispose(); },
  load() { return this.reader.run(async () => {
    const result = await getApp().session.request(`/api/app/education-assessment/my-correction-list?schoolId=${this.schoolId}&SkipCount=${this.offset}&MaxResultCount=20`);
    if (!result || !Number.isSafeInteger(result.totalCount) || result.totalCount < 0 || !Array.isArray(result.items)) throw correction.error();
    return { items: result.items.map((r) => correction.card(r, this.schoolId)), total: result.totalCount };
  }, (r) => this.setData({ ...r, offset: this.offset })); },
  paginate(e) { const d = Number(e.currentTarget.dataset.direction); if (this.data.busy || ![-1, 1].includes(d) || this.offset + d * 20 < 0 || d === 1 && this.offset + 20 >= this.data.total) return; this.offset += d * 20; return this.load(); },
  open(e) { const row = this.data.items.find((r) => r.id === e.currentTarget.dataset.id); if (!this.data.busy && row && correction.allowed(getApp().session.getState(), this.schoolId)) wx.navigateTo({ url: `/pages/correction/index?schoolId=${this.schoolId}&id=${row.id}` }); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
