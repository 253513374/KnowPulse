const note = require('../../utils/notebook');
const store = require('../../utils/notebook-store');
const readScope = require('../../utils/read-scope');
const { isGuid } = require('../../utils/assessment');
Page({
  data: { rows: [], total: 0, offset: 0, pending: [], pendingTotal: 0, pendingOffset: 0, keyword: '', subject: '', year: '', tag: '', busy: false, error: '' },
  onLoad(q) {
    this.offset = 0; this.pendingOffset = 0; this.target = '';
    if (q.sourceKind != null) {
      if (!['0', '1'].includes(q.sourceKind) || ![q.recipientId, q.componentId].every(isGuid) || q.sourceVersionId != null && !isGuid(q.sourceVersionId)) this.invalid = true;
      else this.target = `&sourceKind=${q.sourceKind}&recipientId=${q.recipientId}&componentId=${q.componentId}${q.sourceVersionId ? '&sourceVersionId=' + q.sourceVersionId : ''}`;
    }
    this.reader = readScope(this, getApp(), note.allowed, () => { this.rows = []; this.refs = []; return { rows: [], pending: [] }; });
  },
  onShow() { this.visible = true; this.reader.show(); return this.load(); },
  onHide() { this.visible = false; this.reader.hide(); },
  onUnload() { this.visible = false; this.reader.dispose(); },
  load() { return this.reader.run(async () => {
    if (this.invalid || this.data.year && (!/^\d{1,4}$/.test(this.data.year) || Number(this.data.year) < 1)) throw note.error('NOTE_INVALID');
    const state = getApp().session.getState(), owner = state.context.accountUserId;
    const query = `SkipCount=${this.offset}&MaxResultCount=20&Keyword=${encodeURIComponent(this.data.keyword)}&SubjectName=${encodeURIComponent(this.data.subject)}&Tag=${encodeURIComponent(this.data.tag)}${this.data.year ? '&MaterialYear=' + this.data.year : ''}`;
    const rows = note.page(await getApp().session.request('/api/app/personal-notebook?' + query), (r) => note.card(r, owner));
    return { rows, refs: store.pending(wx, getApp().configuration, state.context) };
  }, (v) => { this.rows = v.rows.items; this.refs = v.refs; this.setData({ rows: this.rows, total: v.rows.total, offset: this.offset,
    pending: v.refs.slice(this.pendingOffset, this.pendingOffset + 20), pendingTotal: v.refs.length, pendingOffset: this.pendingOffset }); }); },
  filter(e) { const field = e.currentTarget.dataset.field; if (!this.data.busy && ['keyword', 'subject', 'year', 'tag'].includes(field)) this.setData({ [field]: e.detail.value }); },
  search() { this.offset = 0; return this.load(); },
  page(e) { if (this.data.busy) return; const d = Number(e.currentTarget.dataset.direction), p = e.currentTarget.dataset.kind === 'pending'; if (![-1, 1].includes(d)) return;
    const field = p ? 'pendingOffset' : 'offset', total = p ? this.data.pendingTotal : this.data.total;
    if (this[field] + d * 20 < 0 || d > 0 && this[field] + 20 >= total) return; this[field] += d * 20; return this.load(); },
  open(e) { if (!this.visible || this.data.busy || !note.allowed(getApp().session.getState())) return;
    const id = e?.currentTarget?.dataset?.id; if (id && !this.rows.some((r) => r.id === id)) return;
    wx.navigateTo({ url: '/pages/notebook/index?' + (id ? 'id=' + id : 'new=1') + this.target }); },
  recover(e) { if (!this.visible || this.data.busy || !note.allowed(getApp().session.getState())) return;
    const r = this.refs.find((v) => v.operationId === e.currentTarget.dataset.id); if (r) wx.navigateTo({ url: '/pages/notebook/index?' + (r.noteId ? 'id=' + r.noteId : 'new=1') }); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
