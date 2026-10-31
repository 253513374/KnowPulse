const message = require('../../utils/messages');
const assessment = require('../../utils/assessment');
const feedback = require('../../utils/feedback');
const study = require('../../utils/study');
const plan = require('../../utils/plan');
const teacherPlan = require('../../utils/teacher-plan');
const correction = require('../../utils/correction');
const analysis = require('../../utils/analysis');
const collection = require('../../utils/collection');
const notebook = require('../../utils/notebook');
const growth = require('../../utils/growth');
const inbox = require('../../utils/inbox');

Page({
  data: { authenticated: false, busy: false, roles: [], selectedRole: null, workspaces: [], error: '' },
  onLoad() {
    this.unsubscribe = getApp().subscribe((state) => this.render(state));
    this.render(getApp().session.getState());
  },
  onShow() {
    const state = getApp().session.getState();
    if (!state.authenticated) { wx.reLaunch({ url: '/pages/login/index' }); return; }
    this.refresh();
  },
  onUnload() { if (this.unsubscribe) this.unsubscribe(); },
  render(state) {
    const changed = this.generation !== state.generation;
    this.generation = state.generation;
    if (changed) this.chosenRole = null;
    const available = state.context ? state.context.workspaces : [];
    const values = [...new Set(available.map((item) => item.role))].sort();
    const previous = changed ? null : this.chosenRole ?? this.data.selectedRole;
    const selectedRole = values.includes(previous) ? previous : values.length === 1 ? values[0] : null;
    if (state.context) this.chosenRole = selectedRole;
    this.setData({ authenticated: state.authenticated, busy: state.busy, cleanupWarning: !!state.cleanupWarning, notebookAllowed: notebook.allowed(state),
      roles: values.map((role) => ({ id: role, title: role === 1 ? '教师工作区' : '学生工作区' })), selectedRole,
      workspaces: available.filter((item) => item.role === selectedRole).map((item) => ({ ...item, collectionAllowed: selectedRole === 2 && collection.allowed(state, item.schoolId), analysisAllowed: selectedRole === 1 && analysis.allowed(state, item.schoolId), correctionAllowed: selectedRole === 2 && correction.allowed(state, item.schoolId), teacherPlanAllowed: selectedRole === 1 && teacherPlan.allowed(state, item.schoolId), planAllowed: selectedRole === 2 && plan.allowed(state, item.schoolId), studyAllowed: selectedRole === 2 && study.allowed(state, item.schoolId), assessmentAllowed: assessment.allowed(state, selectedRole === 1 ? 'teacher' : 'student', item.schoolId), feedbackAllowed: feedback.allowed(state, selectedRole === 1 ? 'teacher' : 'student', item.schoolId) })), ...(changed ? { error: '' } : {}) });
  },
  async selectRole(event) {
    const role = Number(event.currentTarget.dataset.role);
    try {
      const state = await getApp().session.refreshWorkspace();
      if (!state.context.workspaces.some((item) => item.role === role)) return;
      this.chosenRole = role;
      this.setData({ selectedRole: role, workspaces: state.context.workspaces.filter((item) => item.role === role).map((item) => ({ ...item, collectionAllowed: role === 2 && collection.allowed(state, item.schoolId), analysisAllowed: role === 1 && analysis.allowed(state, item.schoolId), correctionAllowed: role === 2 && correction.allowed(state, item.schoolId), teacherPlanAllowed: role === 1 && teacherPlan.allowed(state, item.schoolId), planAllowed: role === 2 && plan.allowed(state, item.schoolId), studyAllowed: role === 2 && study.allowed(state, item.schoolId), assessmentAllowed: assessment.allowed(state, role === 1 ? 'teacher' : 'student', item.schoolId), feedbackAllowed: feedback.allowed(state, role === 1 ? 'teacher' : 'student', item.schoolId) })) });
    } catch (error) { this.failure(error); }
  },
  async refresh() {
    try { this.render(await getApp().session.refreshWorkspace()); }
    catch (error) { this.failure(error); }
  },
  invitations() { if (getApp().session.getState().authenticated && !this.data.busy) wx.navigateTo({ url: '/pages/invitations/index' }); },
  accountHelp() { if (this.data.busy) return; const url=getApp().session.portalUrl('/account/assistance'); if(url)wx.setClipboardData({data:url}); else this.setData({error:'当前尚未配置账号网页，请联系学校或平台管理员。'}); },
  async relationRequests(e) { const school=e.currentTarget.dataset.schoolId; try { const s=await getApp().session.refreshWorkspace(); if(this.data.selectedRole===1&&s.context&&s.context.workspaces.some(w=>w.role===1&&w.schoolId===school))wx.navigateTo({url:`/pages/relation-requests/index?schoolId=${school}`}); } catch(error){this.failure(error);} },
  async notebooks() {
    try { const state = await getApp().session.refreshWorkspace(); if (notebook.allowed(state)) wx.navigateTo({ url: '/pages/notebooks/index' }); }
    catch (error) { this.failure(error); }
  },
  async growth(e) {
    const schoolId = e.currentTarget.dataset.schoolId;
    try { const state = await getApp().session.refreshWorkspace(); if (this.data.selectedRole === 2 && growth.allowed(state, schoolId)) wx.navigateTo({ url: `/pages/growth-contexts/index?schoolId=${schoolId}` }); }
    catch (error) { this.failure(error); }
  },
  async logout() {
    try { await getApp().session.logout(); wx.reLaunch({ url: '/pages/login/index' }); }
    catch (error) { this.failure(error); }
  },
  async assessments(event) {
    const schoolId = event.currentTarget.dataset.schoolId; const role = Number(event.currentTarget.dataset.role);
    const mode = role === 1 ? 'teacher' : role === 2 ? 'student' : '';
    try {
      const state = await getApp().session.refreshWorkspace();
      if (!assessment.allowed(state, mode, schoolId)) return;
      wx.navigateTo({ url: `/pages/assessments/index?mode=${mode}&schoolId=${schoolId}` });
    } catch (error) { this.failure(error); }
  },
  async feedback(event) {
    const schoolId = event.currentTarget.dataset.schoolId; const role = Number(event.currentTarget.dataset.role);
    const mode = role === 1 ? 'teacher' : role === 2 ? 'student' : '';
    try { const state = await getApp().session.refreshWorkspace(); if (feedback.allowed(state, mode, schoolId)) wx.navigateTo({ url: `/pages/feedback/index?mode=${mode}&schoolId=${schoolId}` }); }
    catch (error) { this.failure(error); }
  },
  async study(event) {
    const schoolId = event.currentTarget.dataset.schoolId;
    try { const state = await getApp().session.refreshWorkspace(); if (this.data.selectedRole === 2 && study.allowed(state, schoolId)) wx.navigateTo({ url: `/pages/study-tasks/index?schoolId=${schoolId}` }); }
    catch (error) { this.failure(error); }
  },
  async workload(event) {
    const schoolId = event.currentTarget.dataset.schoolId;
    try { const state = await getApp().session.refreshWorkspace(); if (this.data.selectedRole === 2 && study.allowed(state, schoolId)) wx.navigateTo({ url: `/pages/workload/index?schoolId=${schoolId}` }); }
    catch (error) { this.failure(error); }
  },
  async teacherPlans(event) {
    const schoolId = event.currentTarget.dataset.schoolId;
    try { const s = await getApp().session.refreshWorkspace(); if (this.data.selectedRole === 1 && teacherPlan.allowed(s, schoolId)) wx.navigateTo({ url: `/pages/teacher-plans/index?schoolId=${schoolId}` }); }
    catch (error) { this.failure(error); }
  },
  async reminders(event) {
    const schoolId = event.currentTarget.dataset.schoolId;
    try { const s = await getApp().session.refreshWorkspace(); if (this.data.selectedRole === 2 && inbox.enabled(s, schoolId)) wx.navigateTo({ url: `/pages/inbox/index?schoolId=${schoolId}` }); }
    catch (error) { this.failure(error); }
  },
  async plans(event) {
    const schoolId = event.currentTarget.dataset.schoolId;
    try { const s = await getApp().session.refreshWorkspace(); if (this.data.selectedRole === 2 && plan.allowed(s, schoolId)) wx.navigateTo({ url: `/pages/teaching-plans/index?schoolId=${schoolId}` }); }
    catch (error) { this.failure(error); }
  },
  async corrections(event) {
    const schoolId = event.currentTarget.dataset.schoolId;
    try { const s = await getApp().session.refreshWorkspace(); if (this.data.selectedRole === 2 && correction.allowed(s, schoolId)) wx.navigateTo({ url: `/pages/corrections/index?schoolId=${schoolId}` }); }
    catch (error) { this.failure(error); }
  },
  async reports(event) {
    const schoolId = event.currentTarget.dataset.schoolId;
    try { const s = await getApp().session.refreshWorkspace(); if (this.data.selectedRole === 1 && analysis.allowed(s, schoolId)) wx.navigateTo({ url: `/pages/reports/index?schoolId=${schoolId}` }); }
    catch (error) { this.failure(error); }
  },
  async collections(event) {
    const schoolId = event.currentTarget.dataset.schoolId;
    try { const s = await getApp().session.refreshWorkspace(); if (this.data.selectedRole === 2 && collection.allowed(s, schoolId)) wx.navigateTo({ url: `/pages/question-collections/index?schoolId=${schoolId}` }); }
    catch (error) { this.failure(error); }
  },
  async practices(event) {
    const schoolId = event.currentTarget.dataset.schoolId;
    try { const s = await getApp().session.refreshWorkspace(); if (this.data.selectedRole === 2 && collection.allowed(s, schoolId)) wx.navigateTo({ url: `/pages/practices/index?schoolId=${schoolId}` }); }
    catch (error) { this.failure(error); }
  },
  failure(error) { if (error.code !== 'STALE_RESPONSE' && error.generation === getApp().session.getState().generation) this.setData({ error: message(error) }); },
  loginPage() { wx.reLaunch({ url: '/pages/login/index' }); },
});
