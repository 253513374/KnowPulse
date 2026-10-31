const note = require('../../utils/notebook');
const createStore = require('../../utils/notebook-store');
const command = require('../../utils/notebook-command');
const { actor } = require('../../utils/feedback');
const { isGuid } = require('../../utils/assessment');
const message = require('../../utils/messages');
const ask = (content) => new Promise((resolve) => wx.showModal({ title: '请核对私有笔记', content, success: (r) => resolve(!!r.confirm), fail: () => resolve(false) }));
Page({
  data: { metadata: null, current: null, versions: [], versionTotal: 0, versionOffset: 0, attachments: [], attachmentTotal: 0, attachmentOffset: 0,
    sources: [], sourceTotal: 0, sourceOffset: 0, target: null, form: null, local: false, conflict: false, pending: null, busy: false, error: '', notice: '', imagePath: '' },
  onLoad(q) {
    this.id = q.id || null; this.target = q.sourceKind != null ? { kind: Number(q.sourceKind), recipientId: q.recipientId, componentId: q.componentId, sourceVersionId: q.sourceVersionId || null } : null;
    this.invalid = this.id !== null && !isGuid(this.id) || this.target && (![0, 1].includes(this.target.kind) || ![this.target.recipientId, this.target.componentId].every(isGuid) || this.target.sourceVersionId != null && !isGuid(this.target.sourceVersionId));
    this.setData({ hasSourceTarget: !!this.target });
    this.version = 0; this.versionOffset = 0; this.attachmentOffset = 0; this.sourceOffset = 0; this.files = new Set(); this.generation = getApp().session.getState().generation;
    this.unsubscribe = getApp().subscribe((s) => {
      const changed = this.generation !== s.generation; this.generation = s.generation;
      if (changed || !s.context || !note.allowed(s) || this.owner && actor(s) !== this.owner) this.clearView();
      if (changed || !s.authenticated || s.context && (!note.allowed(s) || this.owner && actor(s) !== this.owner)) {
        this.version++; this.form = null; this.tagsInput = null; this.base = null; this.local = false;
        try { if (this.store) this.store.clearPrivate(); } catch (_) { this.setData({ error: message(note.error('LOCAL_CLEANUP_UNCONFIRMED')) }); }
        this.writer = null; this.store = null;
      }
    });
  },
  onShow() { this.visible = true; return this.load(); },
  onHide() { this.visible = false; this.version++; this.clearView(); this.form = null; this.tagsInput = null; this.base = null; this.local = false; this.writer = null; this.store = null; },
  onUnload() { this.onHide(); this.disposed = true; this.unsubscribe(); },
  authorized() { return this.visible && !this.disposed && actor(getApp().session.getState()) === this.owner && note.allowed(getApp().session.getState()); },
  clearFiles() { for (const path of this.files) getApp().session.releaseFile(path); this.files.clear(); },
  clearView() { this.clearFiles(); this.rows = []; this.refs = []; if (!this.disposed) this.setData({ metadata: null, current: null, versions: [], attachments: [], sources: [], target: null, form: null, pending: null, imagePath: '' }); },
  sync() {
    if (!this.authorized()) return;
    let pending;
    try { pending = this.writer.state(); }
    catch (e) { this.setData({ error: message(e), pending: this.data.pending || { kind: 'unknown', operationId: '', canRetry: false } }); return; }
    this.setData({ form: this.form ? { ...this.form, tagsText: this.tagsInput ?? this.form.customTags.join('，'), yearText: this.form.materialYear == null ? '' : String(this.form.materialYear) } : null,
      local: !!this.local, pending, conflict: !!this.id && !!this.base && (this.base.versionId !== this.data.metadata?.latestVersionId || this.base.stamp !== this.data.metadata?.concurrencyStamp) });
  },
  async load() {
    if (!this.visible || this.working) return; this.working = true; const ticket = ++this.version; this.clearView(); this.setData({ busy: true, error: '' });
    try {
      if (this.invalid) throw note.error('INVALID_PATH');
      const session = getApp().session; await session.refreshWorkspace(); const s = session.getState(), owner = actor(s), context = s.context;
      if (!note.allowed(s) || this.owner && this.owner !== owner) throw note.error('FORBIDDEN'); this.owner = owner;
      if (!this.store) { this.store = createStore(wx, getApp().configuration, s.context, this.id); this.writer = command(wx, session, this.store, this.id, () => this.authorized()); }
      const p = this.store.pending();
      if (p) { const r = await this.writer.recover(); if (r && ticket === this.version && this.authorized()) { if (!this.accept(r, p) || !this.visible) return; } }
      if (ticket !== this.version || !this.authorized() || session.getState().context !== context) return;
      let metadata = null, current = null, versions = { items: [], total: 0 }, attachments = { items: [], total: 0 }, sources = { items: [], stamp: '' };
      if (this.id) {
        metadata = note.metadata(await session.request('/api/app/personal-notebook/' + this.id), s.context.accountUserId, this.id);
        const v = this.selectedVersion || metadata.latestVersionId;
        versions = note.page(await session.request(`/api/app/personal-notebook/${this.id}/versions?SkipCount=${this.versionOffset}&MaxResultCount=20`), (r) => note.version(r, this.id));
        if (v) {
          current = note.version(await session.request(`/api/app/personal-notebook/${this.id}/version/${v}`), this.id, true);
          if (current.id !== v) throw note.error();
          attachments = note.page(await session.request(`/api/app/personal-notebook/${this.id}/attachments/${v}?SkipCount=${this.attachmentOffset}&MaxResultCount=20`), (r) => note.attachment(r, v));
          sources = note.sources(await session.request(`/api/app/education-notebook-source/${this.id}/sources/${v}`), v);
        }
      }
      if (ticket !== this.version || !this.authorized() || session.getState().context !== context) return;
      this.rows = versions.items; this.refs = sources.items; this.sourceStamp = sources.stamp;
      this.setData({ metadata, current, versions: versions.items, versionTotal: versions.total, versionOffset: this.versionOffset, attachments: attachments.items, attachmentTotal: attachments.total, attachmentOffset: this.attachmentOffset,
        sources: sources.items.slice(this.sourceOffset, this.sourceOffset + 20), sourceTotal: sources.items.length, sourceOffset: this.sourceOffset });
      if (!this.form) { const draft = this.store.draft(); if (draft) { this.form = note.content(draft.form, true); this.base = draft.base; this.local = true; } }
      this.sync();
    } catch (e) { if (ticket === this.version && this.visible) this.setData({ error: message(e) }); }
    finally { this.working = false; if (!this.disposed) this.setData({ busy: false }); }
  },
  async act(work) {
    if (!this.authorized() || this.working) return null; this.working = true; const ticket = this.version; this.setData({ busy: true, error: '', notice: '' });
    try { const r = await work(); if (ticket === this.version && this.authorized()) return r; }
    catch (e) { if (ticket === this.version && this.authorized()) this.setData({ error: message(e) }); }
    finally { this.working = false; if (!this.disposed) { this.setData({ busy: false }); if (ticket === this.version) this.sync(); } } return null;
  },
  async allowLocal() {
    if (!this.authorized() || this.data.busy || this.data.pending) return; const owner = this.owner;
    if (this.data.metadata?.latestVersionId && this.data.current?.id !== this.data.metadata.latestVersionId) { this.setData({ error: '请先查看云端最新版本再开始编辑；历史正文可明确复制到编辑区。' }); return; }
    if (!await ask('允许在本机暂存本人文字草稿、原请求和所选附件，最长 24 小时。退出账号会尝试清理，操作编号仍保留供本人核对回执。共享设备请谨慎使用。')) return;
    if (!this.authorized() || owner !== this.owner) return;
    this.local = true; this.form = this.form || (this.data.metadata ? note.content({ ...this.data.metadata, contentText: this.data.current?.id === this.data.metadata.latestVersionId ? this.data.current.contentText : '' }) : note.blank());
    this.base = this.base || { versionId: this.data.metadata?.latestVersionId || null, stamp: this.data.metadata?.concurrencyStamp || null };
    this.sync();
  },
  change(e) {
    if (!this.authorized() || !this.local || !this.form || this.data.busy || this.data.pending) return;
    const field = e.currentTarget.dataset.field, value = e.detail.value;
    if (['title', 'summary', 'contentText', 'subjectName'].includes(field)) this.form[field] = value;
    else if (field === 'tags') {
      const tags = value.split(/[,，]/).map((v) => v.trim()).filter(Boolean);
      if (tags.length > 20 || tags.some((t) => t.length > 64)) { this.setData({ error: message(note.error('NOTE_INVALID')) }); return; }
      this.tagsInput = value; this.form.customTags = tags;
    }
    else if (field === 'year') {
      if (value !== '' && (!/^\d{1,4}$/.test(value) || Number(value) < 1)) { this.setData({ error: message(note.error('NOTE_INVALID')) }); return; }
      this.form.materialYear = value === '' ? null : Number(value);
    }
    else return;
    try { this.store.saveDraft({ form: this.form, base: this.base }); } catch (e) { this.setData({ error: message(e) }); } this.sync();
  },
  async save() {
    if (!this.local || !this.form || this.data.pending || this.data.conflict) return;
    const r = await this.act(async () => { const c = note.content(this.form); await this.writer.begin(this.id ? 'save' : 'create', { ...c, ...(this.id ? { expectedVersionId: this.base.versionId, concurrencyStamp: this.base.stamp } : {}) }, null, null, this.id ? null : this.target); return this.writer.send(); });
    if (r && this.authorized() && this.accept(r) && this.visible) await this.load();
  },
  accept(r, expected) {
    let p;
    try {
      p = expected || this.store.pending(); if (!p) throw note.error('PENDING_REQUIRED');
      this.store.acknowledge(p.operationId);
      if (['create', 'save', 'delete'].includes(p.kind)) this.store.removeDraft();
    } catch (e) { this.setData({ error: message(e), notice: '云端已返回原回执，本机清理或读取未确认。请保留原操作标识，恢复存储后再次核对；不会重新创建。' }); return false; }
    if (p.kind === 'create') {
      this.form = null; this.visible = false; const t = p.sourceTarget || this.target;
      wx.redirectTo({ url: '/pages/notebook/index?id=' + r.noteId + (t ? `&sourceKind=${t.kind}&recipientId=${t.recipientId}&componentId=${t.componentId}${t.sourceVersionId ? '&sourceVersionId=' + t.sourceVersionId : ''}` : '') }); return true;
    }
    if (p.kind === 'delete') { this.form = null; this.visible = false; wx.redirectTo({ url: '/pages/notebooks/index' }); return true; }
    if (p.kind === 'save') { this.form = null; this.tagsInput = null; this.base = null; this.local = false; this.selectedVersion = null; }
    this.setData({ notice: '已核对原操作；正在读取云端当前记录，旧回执不会覆盖较新版本。' });
    return true;
  },
  async recover(e) {
    const retry = e?.currentTarget?.dataset?.kind === 'retry'; let p;
    if (retry && !await ask('先查询原回执；仍未找到时，仅重发同一编号和原始内容。不会用当前编辑内容替换原请求。')) return;
    const r = await this.act(() => { p = this.store.pending(); if (!p) throw note.error('PENDING_REQUIRED'); return retry ? this.writer.retry() : this.writer.recover(); });
    if (r && this.authorized()) { if (this.accept(r, p) && this.visible) await this.load(); }
    else if (this.authorized() && !this.data.error) this.setData({ notice: '尚未找到原回执，不代表失败。请核对云端记录后再决定明确重试或关闭本机追踪。' });
  },
  async abandon() {
    if (!this.data.pending || !await ask('仅关闭本机追踪，不撤销可能已成功的云端操作。原草稿保留；请先核对云端当前版本，确认继续？')) return;
    await this.act(async () => { this.store.acknowledge(this.store.pending().operationId); return true; }); if (this.authorized()) await this.load();
  },
  async rebase() {
    if (!this.local || !this.form || !this.data.metadata || this.data.pending || !await ask('已比较云端最新版本与本机草稿。保留本机文字，改以当前云端版本为保存基础；下次保存生成新版本，不改写历史。')) return;
    await this.act(async () => { this.base = { versionId: this.data.metadata.latestVersionId, stamp: this.data.metadata.concurrencyStamp }; this.store.saveDraft({ form: this.form, base: this.base }); return true; });
  },
  selectVersion(e) { const id = e.currentTarget.dataset.id; if (!this.authorized() || this.data.busy || !this.rows.some((v) => v.id === id)) return; this.selectedVersion = id; this.attachmentOffset = 0; this.sourceOffset = 0; return this.load(); },
  latest() { if (!this.authorized() || this.data.busy) return; this.selectedVersion = null; this.attachmentOffset = 0; this.sourceOffset = 0; return this.load(); },
  page(e) { if (this.data.busy) return; const kind = e.currentTarget.dataset.kind, d = Number(e.currentTarget.dataset.direction); if (!['version', 'attachment', 'source'].includes(kind) || ![-1, 1].includes(d)) return;
    const field = kind + 'Offset', total = this.data[kind + 'Total']; if (this[field] + d * 20 < 0 || d > 0 && this[field] + 20 >= total) return; this[field] += d * 20; return this.load(); },
  async copyVersion() {
    if (!this.local || !this.form || !this.data.current || this.data.pending || !await ask('将当前查看的历史正文复制到本机编辑区，替换本机未保存文字；保存时仍生成新版本。')) return;
    await this.act(async () => { this.form.contentText = this.data.current.contentText; this.store.saveDraft({ form: this.form, base: this.base }); return true; });
  },
  async remove() {
    if (!this.id || !this.data.metadata || this.data.pending || !await ask('确认删除这篇本人笔记？此操作不删除已上交答卷、成绩或来源资料；历史操作回执仍可追溯。')) return;
    const r = await this.act(async () => { await this.writer.begin('delete', { concurrencyStamp: this.data.metadata.concurrencyStamp }); return this.writer.send(); });
    if (r && this.authorized()) this.accept(r);
  },
  async chooseFile(e) {
    const reselect = e.currentTarget.dataset.kind === 'reselect';
    if (!this.local && !reselect || !this.id || !this.data.current || this.data.current.status !== 0 || this.data.pending && !reselect || reselect && (this.data.pending?.kind !== 'notebook-file' || !this.data.pending.canRetry) || this.data.busy || !this.authorized()) return;
    const current = this.data.current;
    await this.act(async () => {
      const p = this.store.pending(); const photo = e.currentTarget.dataset.kind === 'photo' || reselect && p?.kind === 'notebook-file' && p.file.contentType.startsWith('image/');
      if (reselect && (p?.kind !== 'notebook-file' || !p.payload)) throw note.error('RETRY_BODY_LOST');
      const r = await new Promise((resolve, reject) => { const options = { count: 1, success: resolve, fail: () => reject(note.error('FILE_UNAVAILABLE')) };
        if (photo) wx.chooseMedia({ ...options, mediaType: ['image'], sizeType: ['original'] }); else wx.chooseMessageFile({ ...options, type: 'file' }); });
      if (!this.authorized() || this.data.current !== current) throw note.error('STALE_RESPONSE');
      const f = r.tempFiles?.[0]; if (!f) throw note.error('FILE_UNAVAILABLE');
      let filename = f.name, path = f.path;
      if (photo) { path = f.tempFilePath; const image = await require('../../utils/answer-image').inspect(wx, path, { maxFileBytes: 20 * 1024 * 1024, allowedContentTypes: ['image/jpeg', 'image/png', 'image/webp'] }); filename = '笔记图片.' + image.extension; }
      if (reselect) { await this.writer.reselect(path); this.setData({ notice: '已核对原文件内容。请查询回执或明确重试原操作。' }); return null; }
      await this.writer.beginFile(path, filename, current.id); return this.writer.send();
    }).then(async (r) => { if (r && this.authorized() && this.accept(r)) await this.load(); });
  },
  async attachment(e) {
    const f = this.data.attachments.find((v) => v.id === e.currentTarget.dataset.id), current = this.data.current; if (!f || !f.type || !current) return;
    const path = await this.act(async () => { const file = await getApp().session.download(`/api/knowledge/notebooks/${this.id}/versions/${current.id}/attachments/${f.id}`);
      if (!this.authorized() || this.data.current !== current) { getApp().session.releaseFile(file); return null; } this.clearFiles(); this.files.add(file); return file; });
    if (!path || !this.authorized()) return;
    if (['jpg', 'jpeg', 'png', 'webp'].includes(f.type)) this.setData({ imagePath: path });
    else wx.openDocument({ filePath: path, fileType: f.type, showMenu: false, fail: () => { if (this.authorized()) { this.clearFiles(); this.setData({ error: '当前设备无法预览，请在网页查看附件。' }); } } });
  },
  closeImage() { this.clearFiles(); this.setData({ imagePath: '' }); },
  async previewSource() {
    if (!this.target || !this.data.current || this.data.pending) return;
    const r = await this.act(() => getApp().session.request('/api/app/education-notebook-source/preview-source', 'POST', this.target));
    if (r && this.authorized()) { try { const available = r.available && (!this.target.sourceVersionId || r.source?.sourceVersionId === this.target.sourceVersionId);
      this.preview = available ? note.pointer(r.source) : null; this.setData({ target: available ? { available: true, title: r.title } : { available: false } }); } catch (e) { this.setData({ error: message(e) }); } }
  },
  async addSource() {
    if (!this.preview || !this.data.target?.available || !this.data.current || this.data.pending || !await ask('只保存原题或材料的引用，不复制正文或授予权限。以后打开仍须当前来源权限；笔记仍为本人私有。')) return;
    const r = await this.act(async () => { await this.writer.begin('source-add', { noteVersionId: this.data.current.id, concurrencyStamp: this.sourceStamp, source: this.preview }, this.data.current.id); return this.writer.send(); });
    if (r && this.authorized() && this.accept(r)) await this.load();
  },
  async removeSource(e) {
    const source = this.refs.find((v) => v.id === e.currentTarget.dataset.id); if (!source || source.removed || this.data.pending || !await ask('移除此版本的来源引用？不会删除笔记正文、原始资料或已上交记录。')) return;
    const r = await this.act(async () => { await this.writer.begin('source-remove', { noteVersionId: this.data.current.id, concurrencyStamp: this.sourceStamp }, this.data.current.id, source.id); return this.writer.send(); });
    if (r && this.authorized() && this.accept(r)) await this.load();
  },
  openSource(e) { const s = this.refs.find((v) => v.id === e.currentTarget.dataset.id); if (!this.authorized() || this.data.busy || !s?.available) return; wx.navigateTo({ url: note.sourceUrl(s) }); },
  web() { if (!this.authorized() || this.data.busy) return; const url = getApp().session.portalUrl('/education/notebook'); if (url) wx.setClipboardData({ data: url, success: () => { if (this.authorized()) this.setData({ notice: '已复制笔记网页地址。网页需独立登录；本机未保存文字不会自动同步。' }); } }); },
  login() { wx.reLaunch({ url: '/pages/login/index' }); },
});
