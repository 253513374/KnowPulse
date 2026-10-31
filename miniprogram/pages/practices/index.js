const practice = require('../../utils/practice');
const createStore = require('../../utils/practice-store');
const createCommand = require('../../utils/practice-command');
const readScope = require('../../utils/read-scope');
const { isGuid } = require('../../utils/assessment');
const { actor } = require('../../utils/feedback');
const answerStore = require('../../utils/answer-store');
const message = require('../../utils/messages');
const ask = (content) => new Promise((resolve) => wx.showModal({ title: '核对同题复练', content, success: (r) => resolve(!!r.confirm), fail: () => resolve(false) }));
Page({
  data: { rows: [], total: 0, offset: 0, target: false, pending: null, pendingStarts: [], pendingTotal: 0, pendingOffset: 0, busy: false, error: '', notice: '' },
  onLoad(q) {
    this.schoolId = q.schoolId; this.recipientId = q.recipientId || null; this.questionId = q.questionId || null; this.releaseId = q.releaseId || null; this.offset = this.pendingOffset = 0;
    this.reader = readScope(this, getApp(), (s) => practice.allowed(s, this.schoolId), () => { this.rows = []; this.refs = []; return { rows: [], target: false, pending: null, pendingStarts: [] }; });
  },
  onShow() { this.visible = true; this.reader.show(); return this.load(); },
  onHide() { this.visible = false; this.reader.hide(); },
  onUnload() { this.visible = false; this.reader.dispose(); },
  load() { return this.reader.run(async () => {
    const target = [this.recipientId, this.questionId, this.releaseId]; if (target.some((v) => v != null) && !target.every(isGuid)) throw practice.error('INVALID_PATH');
    const app = getApp(), s = app.session.getState();
    if (this.recipientId) { this.store = createStore.start(wx, app.configuration, s.context, this.schoolId, this.recipientId, this.questionId);
      this.writer = createCommand(wx, app.session, this.store, this.schoolId, null, this.recipientId, () => this.visible); }
    const dto = await app.session.request(`/api/app/education-assessment/my-practice-list?SchoolId=${this.schoolId}&SkipCount=${this.offset}&MaxResultCount=20${this.recipientId ? `&RecipientId=${this.recipientId}&PaperQuestionId=${this.questionId}` : ''}`);
    if (!dto || !Array.isArray(dto.items) || dto.items.length > 20 || !Number.isSafeInteger(dto.totalCount) || dto.totalCount < 0) throw practice.error();
    const rows = dto.items.map((r) => practice.card(r, this.schoolId));
    const prefix = answerStore.prefix(app.configuration, s.context) + `practice-start:${this.schoolId}:`, refs = [];
    for (const key of wx.getStorageInfoSync().keys.filter((k) => typeof k === 'string' && k.startsWith(prefix) && k.endsWith(':pending'))) {
      const raw = wx.getStorageSync(key); if (!raw || !isGuid(raw.recipientId) || !isGuid(raw.input?.paperQuestionId) || key !== `${prefix}${raw.recipientId}:${raw.input.paperQuestionId}:pending`) throw practice.error('LOCAL_STATE_INVALID');
      const p = createStore.start(wx, app.configuration, s.context, this.schoolId, raw.recipientId, raw.input.paperQuestionId).pending();
      refs.push({ id: p.operationId, recipientId: raw.recipientId, questionId: raw.input.paperQuestionId, releaseId: raw.input.expectedResultReleaseId });
    }
    return { rows, total: dto.totalCount, refs };
  }, (r) => { this.rows = r.rows; this.refs = r.refs; if (this.pendingOffset >= r.refs.length) this.pendingOffset = 0;
    this.setData({ rows: r.rows, total: r.total, offset: this.offset, target: !!this.recipientId, pending: this.recipientId ? this.writer.state() : null,
      pendingStarts: r.refs.slice(this.pendingOffset, this.pendingOffset + 20), pendingTotal: r.refs.length, pendingOffset: this.pendingOffset }); }); },
  async start() {
    if (!this.visible || this.data.busy || !this.data.target || this.data.pending || !practice.allowed(getApp().session.getState(), this.schoolId)) return;
    const owner = actor(getApp().session.getState());
    if (!await ask('为已核对讲评的这道题开始一轮独立复练？开始不共享草稿、不改变首答或正式成绩，也不增加独立掌握证据。')) return;
    if (!this.visible || owner !== actor(getApp().session.getState())) return;
    return this.action(async () => { await this.writer.beginStart(this.questionId, this.releaseId); return this.writer.send(); });
  },
  action(work) { return this.reader.run(work, (r) => { if (r) { this.store.acknowledge(r.operationId); wx.navigateTo({ url: `/pages/practice/index?schoolId=${this.schoolId}&id=${r.practiceId}` }); }
    else this.setData({ pending: this.writer.state(), notice: '尚未查到原开始回执，不表示失败。可明确重试原请求；不会自动换到新讲评。' }); }).finally(() => {
      if (this.visible && this.store && practice.allowed(getApp().session.getState(), this.schoolId)) {
        try { this.setData({ pending: this.writer.state() }); } catch (e) { this.setData({ error: message(e) }); }
      }
    }); },
  recover() { if (this.visible && !this.data.busy && this.writer && this.store.pending()) return this.action(() => this.writer.recover()); },
  async retry() { if (!this.visible || this.data.busy || !this.writer || !this.store.pending()) return; const owner = actor(getApp().session.getState()), op = this.store.pending().operationId;
    if (!await ask('先查询原开始回执，未查到时按同一编号和原讲评版本重试。确认继续？')) return;
    if (this.visible && owner === actor(getApp().session.getState()) && this.store.pending()?.operationId === op) return this.action(() => this.writer.retry()); },
  async abandon() { if (!this.visible || this.data.busy || !this.store?.pending()) return; const owner = actor(getApp().session.getState()), p = this.store.pending();
    if (!await ask('只关闭本机追踪，不撤销可能已开始的复练。请先核对已有轮次，确定继续？')) return;
    if (this.visible && owner === actor(getApp().session.getState()) && this.store.pending()?.operationId === p.operationId) { this.store.acknowledge(p.operationId); return this.load(); } },
  open(e) { const row = this.rows.find((r) => r.id === e.currentTarget.dataset.id); if (this.visible && !this.data.busy && row && practice.allowed(getApp().session.getState(), this.schoolId)) wx.navigateTo({ url: `/pages/practice/index?schoolId=${this.schoolId}&id=${row.id}` }); },
  manage(e) { const r = this.refs.find((r) => r.id === e.currentTarget.dataset.id); if (this.visible && !this.data.busy && r && practice.allowed(getApp().session.getState(), this.schoolId)) wx.navigateTo({ url: `/pages/practices/index?schoolId=${this.schoolId}&recipientId=${r.recipientId}&questionId=${r.questionId}&releaseId=${r.releaseId}` }); },
  page(e) { if (this.data.busy) return; const d = Number(e.currentTarget.dataset.direction), pending = e.currentTarget.dataset.kind === 'pending', field = pending ? 'pendingOffset' : 'offset', total = pending ? this.data.pendingTotal : this.data.total;
    if (![-1, 1].includes(d) || this[field] + d * 20 < 0 || d > 0 && this[field] + 20 >= total) return; this[field] += d * 20; return this.load(); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
