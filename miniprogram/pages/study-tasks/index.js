const study = require('../../utils/study');
const readScope = require('../../utils/read-scope');
const { isGuid } = require('../../utils/assessment');

Page({
  data: { items: [], total: 0, offset: 0, busy: false, error: '' },
  onLoad(query) {
    this.schoolId = query.schoolId; this.offset = 0;
    this.reader = readScope(this, getApp(), (s) => study.allowed(s, this.schoolId), () => ({ items: [], total: 0 }));
  },
  onShow() { this.reader.show(); return this.load(); },
  onHide() { this.reader.hide(); },
  onUnload() { this.reader.dispose(); },
  load() { return this.reader.run(async () => {
    const row = await getApp().session.request(`/api/app/education-study-task/my-list?schoolId=${this.schoolId}&SkipCount=${this.offset}&MaxResultCount=20`);
    if (!row || !Array.isArray(row.items) || !Number.isSafeInteger(row.totalCount) || row.totalCount < 0) throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' });
    return { items: row.items.map((r) => study.task(r, this.schoolId)), total: row.totalCount };
  }, (value) => this.setData({ ...value, offset: this.offset })); },
  paginate(event) {
    const d = Number(event.currentTarget.dataset.direction);
    if (this.data.busy || ![-1, 1].includes(d) || this.offset + d * 20 < 0 || d === 1 && this.offset + 20 >= this.data.total) return;
    this.offset += d * 20; return this.load();
  },
  open(event) {
    const id = event.currentTarget.dataset.id;
    if (!this.data.busy && isGuid(id) && this.data.items.some((r) => r.id === id) && study.allowed(getApp().session.getState(), this.schoolId))
      wx.navigateTo({ url: `/pages/study-task/index?schoolId=${this.schoolId}&id=${id}` });
  },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
