const assessment = require('../../utils/assessment');
const readScope = require('../../utils/read-scope');

Page({
  data: { detail: null, busy: false, error: '', notice: '' },
  onLoad(query) {
    this.id = query.id; this.schoolId = query.schoolId;
    this.reader = readScope(this, getApp(), (state) => assessment.isGuid(this.id) && assessment.allowed(state, 'teacher', this.schoolId), () => ({ detail: null, notice: '' }));
  },
  onShow() { this.visible = true; this.reader.show(); return this.load(); },
  onHide() { this.visible = false; this.reader.hide(); },
  onUnload() { this.visible = false; this.reader.dispose(); },
  load() {
    return this.reader.run(async () => {
      const [task, dto] = await Promise.all([
        getApp().session.request(`/api/app/education-assessment/${this.id}`),
        getApp().session.request(`/api/app/education-assessment/grading-overview/${this.id}`),
      ]);
      const view = assessment.overview(dto, this.id); const revision = task.currentRevision;
      if (task.id !== this.id || !revision || revision.schoolId !== this.schoolId || revision.id !== view.publishedRevisionId || task.publishedRevisionId !== view.publishedRevisionId)
        throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' });
      return { ...view, title: revision.title, className: revision.schoolClassName, subjectName: revision.curriculum.subjectName, updatedAt: assessment.time(task.updatedAtUtc) };
    }, (detail) => this.setData({ detail }));
  },
  continueOnWeb() {
    if (!this.data.detail || !assessment.allowed(getApp().session.getState(), 'teacher', this.schoolId)) return;
    const url = getApp().session.portalUrl('/education/assessment-grading/' + this.id);
    if (!url) { this.setData({ notice: '尚未配置教师网页地址，请联系平台管理员。' }); return; }
    const generation = getApp().session.getState().generation;
    wx.setClipboardData({ data: url, success: () => {
      if (this.visible && this.data.detail && generation === getApp().session.getState().generation)
        this.setData({ notice: '任务网页地址已复制。请在网页正常登录后继续批改、封卷与统一发布；链接不携带登录凭据。' });
    } });
  },
  reminders() {
    if (this.visible && this.data.detail && assessment.allowed(getApp().session.getState(), 'teacher', this.schoolId))
      wx.navigateTo({ url: `/pages/assessment-reminders/index?mode=teacher&schoolId=${this.schoolId}&taskId=${this.id}` });
  },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
