const assessment = require('../../utils/assessment');
const readScope = require('../../utils/read-scope');

Page({
  data: { title: '', mode: '', items: [], total: 0, offset: 0, pageSize: 20, busy: false, error: '' },
  onLoad(query) {
    this.mode = query.mode; this.schoolId = query.schoolId; this.offset = 0;
    this.setData({ mode: this.mode, title: this.mode === 'teacher' ? '测评任务与评分进度' : '测评作答、成绩与回顾' });
    this.reader = readScope(this, getApp(), (state) => assessment.allowed(state, this.mode, this.schoolId), () => ({ items: [], total: 0 }));
  },
  onShow() { this.reader.show(); return this.load(); },
  onHide() { this.reader.hide(); },
  onUnload() { this.reader.dispose(); },
  load() {
    return this.reader.run(async () => {
      const endpoint = this.mode === 'teacher' ? 'my-list' : 'my-student-list';
      const path = `/api/app/education-assessment/${endpoint}?SchoolId=${encodeURIComponent(this.schoolId)}&SkipCount=${this.offset}&MaxResultCount=20` + (this.mode === 'teacher' ? '&IsPublished=true' : '');
      return assessment.taskPage(await getApp().session.request(path), this.mode, this.schoolId);
    }, (page) => this.setData({ items: page.items, total: page.total, offset: this.offset }));
  },
  previous() { if (!this.data.busy && this.offset >= 20) { this.offset -= 20; return this.load(); } },
  next() { if (!this.data.busy && this.offset + 20 < this.data.total) { this.offset += 20; return this.load(); } },
  open(event) {
    const id = event.currentTarget.dataset.id;
    if (!assessment.isGuid(id) || !assessment.allowed(getApp().session.getState(), this.mode, this.schoolId)) return;
    wx.navigateTo({ url: `/pages/${this.mode === 'teacher' ? 'assessment-progress' : 'assessment-result'}/index?schoolId=${this.schoolId}&id=${id}` });
  },
  answer(event) {
    const id = event.currentTarget.dataset.id;
    if (!assessment.isGuid(id) || this.data.busy || !assessment.allowed(getApp().session.getState(), 'student', this.schoolId)) return;
    wx.navigateTo({ url: `/pages/assessment-answer/index?schoolId=${this.schoolId}&id=${id}` });
  },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
