const assessment = require('../../utils/assessment');
const readScope = require('../../utils/read-scope');
const message = require('../../utils/messages');
const collection = require('../../utils/collection');
const notebook = require('../../utils/notebook');
const growth = require('../../utils/growth');

Page({
  data: { detail: null, busy: false, error: '', imagePath: '', imageRotation: 0, imageBusy: false },
  onLoad(query) {
    this.id = query.id; this.schoolId = query.schoolId; this.releaseId = query.releaseId || null; this.taskRevisionId = query.taskRevisionId || null; this.questionId = query.questionId || null; this.files = new Set(); this.imageVersion = 0; this.fullResult = null;
    this.reader = readScope(this, getApp(), (state) => assessment.isGuid(this.id) && assessment.allowed(state, 'student', this.schoolId),
      () => { this.fullResult = null; return { detail: null, imagePath: '', imageRotation: 0, imageBusy: false }; }, () => this.clearImages());
  },
  onShow() { this.visible = true; this.reader.show(); return this.load(); },
  onHide() { this.visible = false; this.reader.hide(); },
  onUnload() { this.visible = false; this.fullResult = null; this.reader.dispose(); },
  load() {
    return this.reader.run(async () => {
      if (this.taskRevisionId && !assessment.isGuid(this.taskRevisionId) || this.releaseId && !assessment.isGuid(this.releaseId)) throw Object.assign(new Error('INVALID_PATH'), { code: 'INVALID_PATH' });
      const [task, dto] = await Promise.all([
        getApp().session.request(`/api/app/education-assessment/my-student-task/${this.id}`),
        getApp().session.request(`/api/app/education-assessment/my-result-for-school?recipientId=${this.id}&schoolId=${this.schoolId}${this.taskRevisionId ? '&expectedTaskRevisionId=' + this.taskRevisionId : ''}${this.releaseId ? '&expectedResultReleaseId=' + this.releaseId : ''}`),
      ]);
      if (task.id !== this.id || task.schoolId !== this.schoolId || dto.taskId !== task.taskId) throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' });
      const result = assessment.result(dto, this.id);
      if (this.releaseId && dto.resultReleaseId !== this.releaseId) throw Object.assign(new Error('GROWTH_CHANGED'), { code: 'GROWTH_CHANGED' });
      if (this.questionId && (!assessment.isGuid(this.questionId) || result.visibility === 2 && !result.questions.some((q) => q.id === this.questionId))) throw Object.assign(new Error('COLLECTION_SOURCE_CHANGED'), { code: 'COLLECTION_SOURCE_CHANGED' });
      return { ...result, sourceVersionId: assessment.isGuid(task.taskRevisionId) ? task.taskRevisionId : null, title: task.title, subjectName: task.subjectName, className: task.schoolClassName };
    }, (detail) => { this.fullResult = detail; this.renderQuestion(this.questionId ? detail.questions.findIndex((q) => q.id === this.questionId) : 0); });
  },
  renderQuestion(requested) {
    if (!this.fullResult) return;
    const { questions, ...metadata } = this.fullResult;
    const index = Math.max(0, Math.min(requested, questions.length - 1));
    this.clearImages();
    this.setData({ detail: { ...metadata, canUseNotebook: notebook.allowed(getApp().session.getState()), canUseCollections: collection.allowed(getApp().session.getState(), this.schoolId), questionCount: questions.length, questionIndex: index, questions: questions.slice(index, index + 1) }, imagePath: '', imageBusy: false });
  },
  question(event) {
    if (!this.visible || this.data.busy || this.data.imageBusy || !this.data.detail || !assessment.allowed(getApp().session.getState(), 'student', this.schoolId)) return;
    const direction = Number(event.currentTarget.dataset.direction);
    if (direction === 1 || direction === -1) this.renderQuestion(this.data.detail.questionIndex + direction);
  },
  clearImages() { this.imageVersion++; for (const path of this.files) getApp().session.releaseFile(path); this.files.clear(); },
  closeImage() { this.clearImages(); this.setData({ imagePath: '', imageBusy: false }); },
  async image(event) {
    const detail = this.data.detail; const id = event.currentTarget.dataset.id;
    const image = detail && detail.questions.reduce((all, q) => all.concat(q.images), []).find((item) => item.id === id);
    if (!this.visible || this.data.busy || this.data.imageBusy || !image || !assessment.allowed(getApp().session.getState(), 'student', this.schoolId)) return;
    this.clearImages(); const version = this.imageVersion; const generation = getApp().session.getState().generation;
    this.setData({ imagePath: '', imageBusy: true, error: '' });
    try {
      const path = await getApp().session.download(`/api/education/assessment/attempts/${detail.attemptId}/images/${image.id}`);
      if (!this.visible || version !== this.imageVersion || detail !== this.data.detail || generation !== getApp().session.getState().generation) { getApp().session.releaseFile(path); return; }
      this.files.add(path); this.setData({ imagePath: path, imageRotation: image.rotation });
    } catch (error) {
      if (this.visible && version === this.imageVersion && generation === getApp().session.getState().generation) this.setData({ error: message(error) });
    } finally { if (this.visible && version === this.imageVersion) this.setData({ imageBusy: false }); }
  },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
  growth() {
    if (this.visible && !this.data.busy && growth.allowed(getApp().session.getState(), this.schoolId)) wx.navigateTo({ url: `/pages/growth-contexts/index?schoolId=${this.schoolId}` });
  },
  notebook(e) {
    const id = e.currentTarget.dataset.id;
    if (!this.visible || this.data.busy || this.data.imageBusy || !notebook.allowed(getApp().session.getState()) || this.fullResult?.visibility !== 2 || !assessment.isGuid(this.fullResult.sourceVersionId) || !this.data.detail?.questions.some((q) => q.id === id)) return;
    wx.navigateTo({ url: `/pages/notebooks/index?sourceKind=0&recipientId=${this.id}&componentId=${id}&sourceVersionId=${this.fullResult.sourceVersionId}` });
  },
  collection(e) {
    const id = e.currentTarget.dataset.id;
    if (!this.visible || this.data.busy || this.data.imageBusy || !collection.allowed(getApp().session.getState(), this.schoolId) || this.fullResult?.visibility !== 2 || !this.data.detail?.questions.some((q) => q.id === id)) return;
    wx.navigateTo({ url: `/pages/question-collections/index?schoolId=${this.schoolId}&recipientId=${this.id}&questionId=${id}&releaseId=${this.fullResult.releaseId}` });
  },
  practice(e) {
    const id = e.currentTarget.dataset.id;
    if (!this.visible || this.data.busy || this.data.imageBusy || !collection.allowed(getApp().session.getState(), this.schoolId) || this.fullResult?.visibility !== 2 || !this.data.detail?.questions.some((q) => q.id === id)) return;
    wx.navigateTo({ url: `/pages/practices/index?schoolId=${this.schoolId}&recipientId=${this.id}&questionId=${id}&releaseId=${this.fullResult.releaseId}` });
  },
});
