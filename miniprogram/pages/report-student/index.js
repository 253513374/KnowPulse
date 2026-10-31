const analysis = require('../../utils/analysis');
const readScope = require('../../utils/read-scope');
const guid = require('../../utils/assessment').isGuid;
Page({
  data: { head: null, person: null, points: [], total: 0, offset: 0, tasks: [], taskTotal: 0, taskOffset: 0, taskTitle: '', busy: false, error: '' },
  onLoad(q) {
    for (const k of ['schoolId', 'assignmentId', 'id', 'snapshotId', 'studentId', 'taskId']) this[k] = typeof q[k] === 'string' ? q[k].toLowerCase() : '';
    this.offset = 0; this.taskOffset = 0;
    this.reader = readScope(this, getApp(), (s) => [this.id, this.snapshotId, this.studentId].every(guid) && (!this.taskId || guid(this.taskId)) && analysis.allowed(s, this.schoolId, this.assignmentId), () => { this.full = null; return { head: null, person: null, points: [], total: 0, tasks: [], taskTotal: 0, taskTitle: '' }; });
  },
  onShow() { this.reader.show(); return this.load(); },
  onHide() { this.reader.hide(); },
  onUnload() { this.reader.dispose(); },
  load() { const cached = this.full; return this.reader.run(async () => {
    const report = await analysis.read(getApp().session, this.schoolId, this.assignmentId, this.id, this.snapshotId);
    const person = cached || analysis.student(await getApp().session.request(`/api/app/education-learning-report/student?reportId=${this.id}&snapshotId=${this.snapshotId}&studentUserId=${this.studentId}`), this.studentId, report);
    if (this.taskId && !person.assessments.some((t) => t.id === this.taskId)) throw analysis.error('FORBIDDEN');
    return { report, person };
  }, ({ report, person }) => {
    this.full = person; const { points, assessments, ...metadata } = person; const { points: cp, assessments: ct, ...head } = report;
    const tasks = this.taskId ? assessments.filter((t) => t.id === this.taskId) : assessments;
    this.setData({ head, person: metadata, points: points.slice(this.offset, this.offset + 20), total: points.length, offset: this.offset, tasks: tasks.slice(this.taskOffset, this.taskOffset + 20), taskTotal: tasks.length, taskOffset: this.taskOffset, taskTitle: this.taskId ? tasks[0].title : '' });
  }); },
  paginate(e) { const d = Number(e.currentTarget.dataset.direction), kind = e.currentTarget.dataset.kind; if (this.data.busy || !this.full || ![-1, 1].includes(d) || !['points', 'tasks'].includes(kind)) return;
    const offset = kind === 'points' ? 'offset' : 'taskOffset', total = kind === 'points' ? this.data.total : this.data.taskTotal; if (this[offset] + d * 20 < 0 || d === 1 && this[offset] + 20 >= total) return; this[offset] += d * 20; return this.load(); },
  point(e) { const row = this.data.points.find((p) => p.id === e.currentTarget.dataset.id); if (!this.data.busy && row && this.full && analysis.allowed(getApp().session.getState(), this.schoolId, this.assignmentId)) wx.navigateTo({ url: `/pages/report-evidence/index?schoolId=${this.schoolId}&assignmentId=${this.assignmentId}&id=${this.id}&snapshotId=${this.snapshotId}&studentId=${this.studentId}&pointId=${row.id}${this.taskId ? '&taskId=' + this.taskId : ''}` }); },
  trend() { if (!this.data.busy && this.full && analysis.allowed(getApp().session.getState(), this.schoolId, this.assignmentId)) wx.navigateTo({ url: `/pages/report-trend/index?schoolId=${this.schoolId}&assignmentId=${this.assignmentId}&id=${this.id}&snapshotId=${this.snapshotId}&studentId=${this.studentId}` }); },
  back() { if (analysis.allowed(getApp().session.getState(), this.schoolId, this.assignmentId)) wx.navigateTo({ url: `/pages/report/index?schoolId=${this.schoolId}&assignmentId=${this.assignmentId}&id=${this.id}` }); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
