const feedback = require('../../utils/feedback');
const { isGuid } = require('../../utils/assessment');
const readScope = require('../../utils/read-scope');

Page({
  data: { detail: null, busy: false, error: '' },
  onLoad(query) {
    this.id = query.id; this.versionId = query.versionId; this.schoolId = query.schoolId;
    this.reader = readScope(this, getApp(), (state) => isGuid(this.id) && isGuid(this.versionId) && feedback.allowed(state, 'student', this.schoolId), () => ({ detail: null }));
  },
  onShow() { this.reader.show(); return this.load(); },
  onHide() { this.reader.hide(); },
  onUnload() { this.reader.dispose(); },
  load() {
    return this.reader.run(async () => feedback.studentDetail(await getApp().session.request(`/api/app/education-feedback/open-my-for-school?feedbackId=${this.id}&expectedVersionId=${this.versionId}&schoolId=${this.schoolId}`, 'POST'), this.id, this.versionId, this.schoolId),
      (detail) => this.setData({ detail }));
  },
  assessment(event) {
    const id = event.currentTarget.dataset.id;
    if (!this.data.detail || !this.data.detail.assessments.includes(id) || !feedback.allowed(getApp().session.getState(), 'student', this.schoolId)) return;
    wx.navigateTo({ url: `/pages/assessment-result/index?schoolId=${this.schoolId}&id=${id}` });
  },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
