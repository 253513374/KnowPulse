const analysis = require('../../utils/analysis');
const readScope = require('../../utils/read-scope');
const guid = require('../../utils/assessment').isGuid;
Page({
  data: { head: null, points: [], pointTotal: 0, pointOffset: 0, tasks: [], taskTotal: 0, taskOffset: 0, students: [], total: 0, offset: 0, busy: false, error: '' },
  onLoad(q) {
    for (const k of ['schoolId', 'assignmentId', 'id']) this[k] = typeof q[k] === 'string' ? q[k].toLowerCase() : '';
    this.offset = 0; this.pointOffset = 0; this.taskOffset = 0;
    this.reader = readScope(this, getApp(), (s) => guid(this.id) && analysis.allowed(s, this.schoolId, this.assignmentId), () => { this.full = null; return { head: null, points: [], tasks: [], students: [], pointTotal: 0, taskTotal: 0, total: 0 }; });
  },
  onShow() { this.reader.show(); return this.refresh(); },
  onHide() { this.reader.hide(); },
  onUnload() { this.reader.dispose(); },
  refresh() { this.offset = 0; this.pointOffset = 0; this.taskOffset = 0; return this.load(); },
  load(snapshot) { return this.reader.run(async () => {
    const report = await analysis.read(getApp().session, this.schoolId, this.assignmentId, this.id, snapshot); let rows = { items: [], totalCount: 0 };
    if (report.current) rows = analysis.page(await getApp().session.request(`/api/app/education-learning-report/students?reportId=${this.id}&snapshotId=${report.snapshotId}&SkipCount=${this.offset}&MaxResultCount=20`));
    if (report.current && rows.totalCount !== report.coverage.students) throw analysis.error();
    return { report, rows: rows.items.map(analysis.studentCard), total: rows.totalCount };
  }, ({ report, rows, total }) => {
    this.full = report; const { points, assessments, ...head } = report;
    this.setData({ head, points: points.slice(this.pointOffset, this.pointOffset + 20), pointTotal: points.length, pointOffset: this.pointOffset,
      tasks: assessments.slice(this.taskOffset, this.taskOffset + 20), taskTotal: assessments.length, taskOffset: this.taskOffset, students: rows, total, offset: this.offset });
  }); },
  paginate(e) { const d = Number(e.currentTarget.dataset.direction), kind = e.currentTarget.dataset.kind;
    if (this.data.busy || !this.full?.current || ![-1, 1].includes(d)) return;
    const offset = kind === 'points' ? 'pointOffset' : kind === 'tasks' ? 'taskOffset' : kind === 'students' ? 'offset' : null;
    const total = kind === 'points' ? this.data.pointTotal : kind === 'tasks' ? this.data.taskTotal : this.data.total;
    if (!offset || this[offset] + d * 20 < 0 || d === 1 && this[offset] + 20 >= total) return; const snapshot = this.full.snapshotId; this[offset] += d * 20; return this.load(snapshot);
  },
  student(e) { const row = this.data.students.find((s) => s.id === e.currentTarget.dataset.id); if (!this.data.busy && row && this.full?.current && analysis.allowed(getApp().session.getState(), this.schoolId, this.assignmentId)) wx.navigateTo({ url: `/pages/report-student/index?schoolId=${this.schoolId}&assignmentId=${this.assignmentId}&id=${this.id}&snapshotId=${this.full.snapshotId}&studentId=${row.id}` }); },
  trend() { if (!this.data.busy && this.full?.current && analysis.allowed(getApp().session.getState(), this.schoolId, this.assignmentId)) wx.navigateTo({ url: `/pages/report-trend/index?schoolId=${this.schoolId}&assignmentId=${this.assignmentId}&id=${this.id}&snapshotId=${this.full.snapshotId}` }); },
  web() { if (!analysis.allowed(getApp().session.getState(), this.schoolId, this.assignmentId)) return; const url = getApp().session.portalUrl('/education/learning-reports'); if (url) wx.setClipboardData({ data: url }); else this.setData({ error: '尚未配置教师网页地址。' }); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
