const analysis = require('../../utils/analysis');
const readScope = require('../../utils/read-scope');
Page({
  data: { choices: [], index: 0, selected: '', items: [], total: 0, offset: 0, busy: false, error: '', warning: '' },
  onLoad(q) { this.schoolId = typeof q.schoolId === 'string' ? q.schoolId.toLowerCase() : ''; this.assignment = null; this.offset = 0; this.reader = readScope(this, getApp(), (s) => analysis.allowed(s, this.schoolId), () => ({ choices: [], items: [], total: 0, selected: '', warning: '' })); },
  onShow() { this.reader.show(); return this.load(); },
  onHide() { this.reader.hide(); },
  onUnload() { this.reader.dispose(); },
  load() { return this.reader.run(async () => {
    const choices = analysis.assignments(getApp().session.getState(), this.schoolId); if (!this.assignment && choices.length) this.assignment = choices[0].id;
    const index = choices.findIndex((c) => c.id === this.assignment);
    if (index < 0) return { choices, index: 0, selected: '', items: [], total: 0, warning: choices.length ? '原选择已失效，请重新选择任教范围。' : '暂无当前有效任教关系。' };
    const page = analysis.page(await getApp().session.request(`/api/app/education-learning-report?teachingAssignmentId=${this.assignment}&schoolId=${this.schoolId}&SkipCount=${this.offset}&MaxResultCount=20`));
    return { choices, index, selected: choices[index].label, items: page.items.map((r) => analysis.list(r, this.schoolId, this.assignment)), total: page.totalCount, warning: '' };
  }, (row) => this.setData({ ...row, offset: this.offset })); },
  choose(e) { if (this.data.busy) return; const c = this.data.choices[Number(e.detail.value)]; if (!c || !analysis.allowed(getApp().session.getState(), this.schoolId, c.id)) return; this.assignment = c.id; this.offset = 0; return this.load(); },
  paginate(e) { const d = Number(e.currentTarget.dataset.direction); if (this.data.busy || ![-1, 1].includes(d) || this.offset + d * 20 < 0 || d === 1 && this.offset + 20 >= this.data.total) return; this.offset += d * 20; return this.load(); },
  open(e) { const row = this.data.items.find((r) => r.id === e.currentTarget.dataset.id); if (!this.data.busy && row && analysis.allowed(getApp().session.getState(), this.schoolId, row.assignmentId)) wx.navigateTo({ url: `/pages/report/index?schoolId=${this.schoolId}&assignmentId=${row.assignmentId}&id=${row.id}` }); },
  web() { if (!analysis.allowed(getApp().session.getState(), this.schoolId)) return; const url = getApp().session.portalUrl('/education/learning-reports'); if (url) wx.setClipboardData({ data: url }); else this.setData({ error: '尚未配置教师网页地址。' }); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
