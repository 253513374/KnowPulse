const plan = require('../../utils/teacher-plan');
const readScope = require('../../utils/read-scope');
Page({
  data: { choices: [], index: 0, items: [], total: 0, offset: 0, busy: false, error: '', warning: '' },
  onLoad(q) { this.schoolId = typeof q.schoolId === 'string' ? q.schoolId.toLowerCase() : ''; this.assignment = null; this.offset = 0; this.reader = readScope(this, getApp(), (s) => plan.allowed(s, this.schoolId), () => ({ choices: [], items: [], total: 0, warning: '' })); },
  onShow() { this.reader.show(); return this.load(); }, onHide() { this.reader.hide(); }, onUnload() { this.reader.dispose(); },
  load() { return this.reader.run(async () => {
    const choices = plan.assignments(getApp().session.getState(), this.schoolId); if (!this.assignment && choices.length) this.assignment = choices[0].id;
    const index = choices.findIndex((c) => c.id === this.assignment); if (index < 0) return { choices, index: 0, items: [], total: 0, warning: '暂无所选范围的有效任教关系，请重新选择。' };
    const page = plan.page(await getApp().session.request(`/api/app/education-teaching-plan/teacher-plans?SchoolId=${this.schoolId}&TeachingAssignmentId=${this.assignment}&SkipCount=${this.offset}&MaxResultCount=20`));
    return { choices, index, items: page.items.map((r) => plan.card(r, this.schoolId, this.assignment)), total: page.totalCount, warning: '' };
  }, (v) => this.setData({ ...v, offset: this.offset })); },
  choose(e) { if (this.data.busy) return; const c = this.data.choices[Number(e.detail.value)]; if (!c || !plan.allowed(getApp().session.getState(), this.schoolId, c.id)) return; this.assignment = c.id; this.offset = 0; return this.load(); },
  confirmRange() { return this.choose({ detail: { value: this.data.index } }); },
  paginate(e) { const d = Number(e.currentTarget.dataset.direction); if (this.data.busy || ![-1, 1].includes(d) || this.offset + d * 20 < 0 || d === 1 && this.offset + 20 >= this.data.total) return; this.offset += d * 20; return this.load(); },
  open(e) { const r = this.data.items.find((v) => v.id === e.currentTarget.dataset.id); if (!this.data.busy && r && plan.allowed(getApp().session.getState(), this.schoolId, r.assignmentId)) wx.navigateTo({ url: `/pages/teacher-plan/index?schoolId=${this.schoolId}&assignmentId=${r.assignmentId}&id=${r.id}&versionId=${r.versionId}` }); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
