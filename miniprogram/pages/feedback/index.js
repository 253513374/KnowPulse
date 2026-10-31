const feedback = require('../../utils/feedback');
const { isGuid, time } = require('../../utils/assessment');
const readScope = require('../../utils/read-scope');
const message = require('../../utils/messages');

Page({
  data: { mode: '', teachings: [], teachingIndex: 0, teachingId: '', saved: [], students: [], notices: [], total: 0, studentTotal: 0, noticeTotal: 0,
    offset: 0, studentOffset: 0, noticeOffset: 0, busy: false, error: '' },
  onLoad(query) {
    this.mode = query.mode; this.schoolId = query.schoolId; this.offset = 0; this.studentOffset = 0; this.noticeOffset = 0; this.teachingId = '';
    this.setData({ mode: this.mode });
    this.reader = readScope(this, getApp(), (state) => feedback.allowed(state, this.mode, this.schoolId), () => ({ saved: [], students: [], notices: [], total: 0, studentTotal: 0, noticeTotal: 0 }));
  },
  onShow() { this.reader.show(); return this.load(); },
  onHide() { this.reader.hide(); },
  onUnload() { this.reader.dispose(); },
  load() {
    return this.reader.run(async () => {
      const session = getApp().session;
      if (this.mode === 'teacher') {
        const teachings = feedback.teachingList(await session.request('/api/app/education-feedback/teaching-list'), session.getState(), this.schoolId);
        if (!teachings.some((t) => t.id === this.teachingId)) this.teachingId = teachings.length ? teachings[0].id : '';
        if (!this.teachingId) return { teachings, saved: [], students: [], total: 0, studentTotal: 0, notices: [], noticeTotal: 0 };
        const [saved, students] = await Promise.all([
          session.request(`/api/app/education-feedback?teachingAssignmentId=${this.teachingId}&SkipCount=${this.offset}&MaxResultCount=20`),
          session.request(`/api/app/education-feedback/students/${this.teachingId}?SkipCount=${this.studentOffset}&MaxResultCount=20`),
        ]);
        const history = feedback.page(saved, feedback.teacherCard);
        const roster = feedback.page(students, (s) => {
          if (!isGuid(s.studentEnrollmentId) || !isGuid(s.schoolMembershipId)) throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' });
          return { enrollmentId: s.studentEnrollmentId, membershipId: s.schoolMembershipId, name: s.studentName, accountName: s.accountName };
        });
        return { teachings, saved: history.items, total: history.total, students: roster.items, studentTotal: roster.total, notices: [], noticeTotal: 0 };
      }
      const [rows, notices] = await Promise.all([
        session.request(`/api/app/education-feedback/my-list?schoolId=${this.schoolId}&SkipCount=${this.offset}&MaxResultCount=20`),
        session.request(`/api/app/education-feedback/my-notices?schoolId=${this.schoolId}&SkipCount=${this.noticeOffset}&MaxResultCount=20`),
      ]);
      const cards = feedback.page(rows, (row) => feedback.studentCard(row, this.schoolId));
      const hints = feedback.page(notices, (n) => ({ id: n.id, feedbackId: n.feedbackId, current: n.isCurrentVersion, read: n.isRead, withdrawn: n.kind === 2, time: time(n.recordedAtUtc) }));
      return { teachings: [], saved: cards.items, total: cards.total, students: [], studentTotal: 0, notices: hints.items, noticeTotal: hints.total };
    }, (value) => this.setData({ ...value, teachingId: this.teachingId, teachingIndex: Math.max(0, value.teachings.findIndex((t) => t.id === this.teachingId)),
      offset: this.offset, studentOffset: this.studentOffset, noticeOffset: this.noticeOffset }));
  },
  chooseTeaching(event) {
    const teaching = this.data.teachings[Number(event.detail.value)]; if (!teaching || this.data.busy) return;
    this.teachingId = teaching.id; this.offset = 0; this.studentOffset = 0; return this.load();
  },
  paginate(event) {
    if (this.data.busy) return;
    const group = event.currentTarget.dataset.group; const direction = Number(event.currentTarget.dataset.direction);
    const key = group === 'students' ? 'studentOffset' : group === 'notices' ? 'noticeOffset' : 'offset';
    const total = group === 'students' ? this.data.studentTotal : group === 'notices' ? this.data.noticeTotal : this.data.total;
    if ((direction !== 1 && direction !== -1) || this[key] + direction * 20 < 0 || (direction === 1 && this[key] + 20 >= total)) return;
    this[key] += direction * 20; return this.load();
  },
  open(event) {
    const id = event.currentTarget.dataset.id; const versionId = event.currentTarget.dataset.version;
    if (!isGuid(id) || !feedback.allowed(getApp().session.getState(), this.mode, this.schoolId)) return;
    if (this.mode === 'student' && !isGuid(versionId)) return;
    wx.navigateTo({ url: `/pages/${this.mode === 'teacher' ? 'feedback-edit' : 'feedback-view'}/index?schoolId=${this.schoolId}&id=${id}` + (this.mode === 'student' ? '&versionId=' + versionId : '') });
  },
  async create(event) {
    const target = this.data.students.find((s) => s.enrollmentId === event.currentTarget.dataset.id);
    const owner = feedback.actor(getApp().session.getState());
    if (!target || !feedback.allowed(getApp().session.getState(), 'teacher', this.schoolId)) return;
    try {
      const id = await feedback.uuid(wx);
      if (owner !== feedback.actor(getApp().session.getState())) return;
      wx.navigateTo({ url: `/pages/feedback-edit/index?schoolId=${this.schoolId}&id=${id}&teachingId=${this.teachingId}&enrollmentId=${target.enrollmentId}&membershipId=${target.membershipId}` });
    } catch (error) { this.setData({ error: message(error) }); }
  },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
