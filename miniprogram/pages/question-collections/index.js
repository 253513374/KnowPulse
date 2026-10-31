const collection = require('../../utils/collection');
const readScope = require('../../utils/read-scope');
const { isGuid } = require('../../utils/assessment');
const { actor } = require('../../utils/feedback');
const ask = (content) => new Promise((resolve) => wx.showModal({ title: '请核对', content, success: (r) => resolve(r.confirm), fail: () => resolve(false) }));
Page({
  data: { rows: [], total: 0, offset: 0, target: null, pending: [], pendingTotal: 0, pendingOffset: 0, busy: false, error: '', notice: '' },
  onLoad(q) {
    this.schoolId = q.schoolId; this.recipientId = q.recipientId || null; this.questionId = q.questionId || null; this.releaseId = q.releaseId || null;
    this.offset = 0; this.pendingOffset = 0; this.allPending = [];
    this.reader = readScope(this, getApp(), (s) => collection.allowed(s, this.schoolId), () => { this.rows = []; this.allPending = []; return { rows: [], pending: [], target: null }; });
  },
  onShow() { this.visible = true; this.reader.show(); return this.load(); },
  onHide() { this.visible = false; this.reader.hide(); },
  onUnload() { this.visible = false; this.reader.dispose(); },
  load() { return this.reader.run(async () => {
    if (this.recipientId !== null && (![this.recipientId, this.questionId].every(isGuid) || this.releaseId !== null && !isGuid(this.releaseId))) throw Object.assign(new Error('INVALID_PATH'), { code: 'INVALID_PATH' });
    const session = getApp().session; const refs = collection.pending(wx, getApp().configuration, session, this.schoolId);
    const dto = await session.request(`/api/app/education-assessment/my-collections?SchoolId=${this.schoolId}&SkipCount=${this.offset}&MaxResultCount=20`);
    if (!dto || !Number.isSafeInteger(dto.totalCount) || dto.totalCount < 0 || !Array.isArray(dto.items) || dto.items.length > 20) throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' });
    const rows = dto.items.map((r) => collection.row(r, this.schoolId)); if (rows.some((r) => !r.collected)) throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' });
    const target = this.recipientId ? collection.context(await session.request(`/api/app/education-assessment/my-collection?schoolId=${this.schoolId}&recipientId=${this.recipientId}&paperQuestionId=${this.questionId}`), this.schoolId, this.recipientId, this.questionId, this.releaseId) : null;
    return { rows, total: dto.totalCount, target, refs };
  }, (value) => { this.rows = value.rows; this.allPending = value.refs; this.setData({ rows: value.rows, total: value.total, offset: this.offset, target: value.target,
    pending: value.refs.slice(this.pendingOffset, this.pendingOffset + 20).map((r) => ({ operationId: r.input.operationId, wanted: r.input.isCollected })), pendingTotal: value.refs.length, pendingOffset: this.pendingOffset }); }); },
  page(e) { if (this.data.busy) return; const d = Number(e.currentTarget.dataset.direction); if (![-1, 1].includes(d)) return;
    const pending = e.currentTarget.dataset.kind === 'pending', field = pending ? 'pendingOffset' : 'offset', total = pending ? this.data.pendingTotal : this.data.total;
    if (this[field] + d * 20 < 0 || d > 0 && this[field] + 20 >= total) return; this[field] += d * 20; return this.load(); },
  async set(e) {
    if (this.data.busy || !collection.allowed(getApp().session.getState(), this.schoolId)) return;
    const id = e.currentTarget.dataset.id, item = id ? this.rows.find((r) => r.id === id) : this.data.target?.current;
    const target = id ? { recipientId: item?.recipientId, questionId: item?.questionId, current: item, canCollect: false } : this.data.target;
    if (!target || ![target.recipientId, target.questionId].every(isGuid)) return; const wanted = !target.current?.collected;
    const owner = actor(getApp().session.getState());
    if (wanted && !target.canCollect) return;
    if (!await ask(wanted ? '收藏只保留此题的引用，不复制题干、答案或图片。以后回顾仍须当前完整讲评和收藏授权；不改变首答、成绩或掌握度。确认收藏？' : '确认取消这道题的收藏？保留原操作记录，不删除答卷、成绩或复练。')) return;
    if (actor(getApp().session.getState()) !== owner || !this.visible) return;
    await this.action(target.recipientId, target.questionId, (w) => w.start(wanted, target.current, target.releaseId));
  },
  async pendingAction(e) {
    if (this.data.busy) return; const ref = this.allPending.find((r) => r.input.operationId === e.currentTarget.dataset.id); if (!ref) return;
    const kind = e.currentTarget.dataset.kind;
    const owner = actor(getApp().session.getState());
    if (!['recover', 'retry', 'abandon'].includes(kind)) return;
    if (kind === 'abandon' && !await ask('只关闭本机追踪，不撤销可能已成功的收藏操作。会重新读取当前状态；确定继续？')) return;
    if (actor(getApp().session.getState()) !== owner || !this.visible) return;
    await this.action(ref.recipientId, ref.questionId, (w) => kind === 'abandon' ? (w.abandon(), true) : kind === 'retry' ? w.retry() : w.recover());
  },
  async action(recipient, question, run) {
    const owner = actor(getApp().session.getState()); this.setData({ notice: '' });
    await this.reader.run(async () => {
      if (actor(getApp().session.getState()) !== owner) throw Object.assign(new Error('STALE_RESPONSE'), { code: 'STALE_RESPONSE' });
      const writer = collection.writer(wx, getApp().session, getApp().configuration, this.schoolId, recipient, question, () => this.visible);
      return run(writer);
    }, (receipt) => this.setData({ notice: receipt ? '已核对该操作，正在重新读取当前收藏状态；旧回执不会回滚较新状态。' : '尚未查询到原回执，不代表失败。可明确重试原请求或先核对当前状态。' }));
    const failure = this.data.error;
    if (this.visible) { await this.load(); if (failure) this.setData({ error: failure }); }
  },
  open(e) {
    const row = this.rows.find((r) => r.id === e.currentTarget.dataset.id);
    if (!this.visible || this.data.busy || !row || !collection.allowed(getApp().session.getState(), this.schoolId)) return;
    wx.navigateTo({ url: `/pages/assessment-result/index?id=${row.recipientId}&schoolId=${this.schoolId}&questionId=${row.questionId}` });
  },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
