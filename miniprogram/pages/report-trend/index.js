const analysis = require('../../utils/analysis');
const trend = require('../../utils/trend');
const readScope = require('../../utils/read-scope');
const guid = require('../../utils/assessment').isGuid;
Page({
  data: { head: null, student: null, rows: [], total: 0, offset: 0, busy: false, error: '' },
  onLoad(q) {
    for (const k of ['schoolId', 'assignmentId', 'id', 'snapshotId', 'studentId']) this[k] = typeof q[k] === 'string' ? q[k].toLowerCase() : '';
    this.offset = 0; this.reader = readScope(this, getApp(), (s) => [this.id, this.snapshotId].every(guid) && (!this.studentId || guid(this.studentId)) && analysis.allowed(s, this.schoolId, this.assignmentId), () => ({ head: null, student: null, rows: [], total: 0 }));
  },
  onShow() { this.reader.show(); return this.load(); },
  onHide() { this.reader.hide(); },
  onUnload() { this.reader.dispose(); },
  load() { return this.reader.run(async () => {
    const session = getApp().session, report = await analysis.read(session, this.schoolId, this.assignmentId, this.id, this.snapshotId);
    const result = trend.project(await session.request(`/api/app/education-learning-report/trend?reportId=${this.id}&snapshotId=${this.snapshotId}&schoolId=${this.schoolId}&teachingAssignmentId=${this.assignmentId}&SkipCount=${this.offset}&MaxResultCount=20${this.studentId ? '&StudentUserId=' + this.studentId : ''}`), report, this.studentId);
    return { report, result };
  }, ({ report, result }) => { const { points, assessments, ...head } = report; this.setData({ head, ...result, offset: this.offset }); }); },
  paginate(e) { const d = Number(e.currentTarget.dataset.direction); if (this.data.busy || ![-1, 1].includes(d) || this.offset + d * 20 < 0 || d === 1 && this.offset + 20 >= this.data.total) return; this.offset += d * 20; return this.load(); },
  basis(e) { const row = this.data.rows.find((r) => r.id === e.currentTarget.dataset.id); if (!this.data.busy && this.studentId && row && analysis.allowed(getApp().session.getState(), this.schoolId, this.assignmentId)) wx.navigateTo({ url: `/pages/report-student/index?schoolId=${this.schoolId}&assignmentId=${this.assignmentId}&id=${this.id}&snapshotId=${this.snapshotId}&studentId=${this.studentId}&taskId=${row.id}` }); },
  back() { if (analysis.allowed(getApp().session.getState(), this.schoolId, this.assignmentId)) wx.navigateTo({ url: `/pages/report/index?schoolId=${this.schoolId}&assignmentId=${this.assignmentId}&id=${this.id}` }); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
