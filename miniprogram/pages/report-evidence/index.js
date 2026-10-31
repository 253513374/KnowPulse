const analysis = require('../../utils/analysis');
const readScope = require('../../utils/read-scope');
const guid = require('../../utils/assessment').isGuid;
Page({
  data: { head: null, person: null, point: null, rows: [], total: 0, offset: 0, taskTitle: '', canReview: false, busy: false, error: '' },
  onLoad(q) {
    for (const k of ['schoolId', 'assignmentId', 'id', 'snapshotId', 'studentId', 'pointId', 'taskId']) this[k] = typeof q[k] === 'string' ? q[k].toLowerCase() : '';
    this.offset = 0; this.reader = readScope(this, getApp(), (s) => [this.id, this.snapshotId, this.studentId, this.pointId].every(guid) && (!this.taskId || guid(this.taskId)) && analysis.allowed(s, this.schoolId, this.assignmentId), () => ({ head: null, person: null, point: null, rows: [], total: 0, taskTitle: '', canReview: false }));
  },
  onShow() { this.reader.show(); return this.load(); },
  onHide() { this.reader.hide(); },
  onUnload() { this.reader.dispose(); },
  load() { return this.reader.run(async () => {
    const session = getApp().session, report = await analysis.read(session, this.schoolId, this.assignmentId, this.id, this.snapshotId);
    const person = analysis.student(await session.request(`/api/app/education-learning-report/student?reportId=${this.id}&snapshotId=${this.snapshotId}&studentUserId=${this.studentId}`), this.studentId, report);
    if (this.taskId && !person.assessments.some((t) => t.id === this.taskId)) throw analysis.error('FORBIDDEN');
    const point = person.points.find((p) => p.id === this.pointId); if (!point) throw analysis.error('FORBIDDEN');
    const rows = analysis.page(await session.request(`/api/app/education-learning-report/point-evidence?reportId=${this.id}&snapshotId=${this.snapshotId}&studentUserId=${this.studentId}&pointRevisionId=${this.pointId}&SkipCount=${this.offset}&MaxResultCount=20${this.taskId ? '&taskId=' + this.taskId : ''}`));
    if (this.taskId && rows.items.some((r) => r.taskId !== this.taskId)) throw analysis.error('REPORT_VERSION_CHANGED');
    return { report, person, point, rows };
  }, ({ report, person, point, rows }) => {
    const { points, assessments, ...head } = report;
    this.setData({ head, person: { name: person.name, account: person.account }, point, rows: rows.items.map((r) => ({ ...analysis.evidence(r), task: report.assessments.find((t) => t.id === r.taskId)?.title || '原测评' })), total: rows.totalCount, offset: this.offset,
      taskTitle: this.taskId ? report.assessments.find((t) => t.id === this.taskId).title : '', canReview: getApp().session.getState().context.grantedPermissions.includes('KnowPulse.Education.AssessmentManage') });
  }); },
  paginate(e) { const d = Number(e.currentTarget.dataset.direction); if (this.data.busy || ![-1, 1].includes(d) || this.offset + d * 20 < 0 || d === 1 && this.offset + 20 >= this.data.total) return; this.offset += d * 20; return this.load(); },
  review(e) { const row = this.data.rows.find((r) => r.id === e.currentTarget.dataset.id), s = getApp().session.getState(); if (this.data.busy || !row || !analysis.allowed(s, this.schoolId, this.assignmentId) || !s.context.grantedPermissions.includes('KnowPulse.Education.AssessmentManage')) return;
    const url = getApp().session.portalUrl(`/education/evidence/${row.gradeId}`); if (url) wx.setClipboardData({ data: url }); else this.setData({ error: '尚未配置教师网页地址。' }); },
  back() { if (analysis.allowed(getApp().session.getState(), this.schoolId, this.assignmentId)) wx.navigateTo({ url: `/pages/report/index?schoolId=${this.schoolId}&assignmentId=${this.assignmentId}&id=${this.id}` }); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
