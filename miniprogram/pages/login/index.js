const message = require('../../utils/messages');

Page({
  data: { configured: false, busy: false, hasBinding: false, bindingCode: '', error: '', storageWarning: false },
  onLoad() {
    const app = getApp();
    this.unsubscribe = app.subscribe((state) => this.render(state));
    this.render(app.session.getState());
  },
  onShow() { if (getApp().session.getState().authenticated) wx.redirectTo({ url: '/pages/workspace/index' }); },
  onUnload() { if (this.unsubscribe) this.unsubscribe(); this.data.bindingCode = ''; },
  render(state) {
    const changed = this.generation !== state.generation;
    this.generation = state.generation;
    this.setData({ configured: state.configured, busy: state.busy, hasBinding: state.hasBinding, storageWarning: state.storageWarning,
      cleanupWarning: !!state.cleanupWarning, ...(changed ? { bindingCode: '', error: '' } : {}) });
  },
  codeChanged(event) { this.setData({ bindingCode: event.detail.value }); },
  async login() {
    try { await getApp().session.login(); wx.redirectTo({ url: '/pages/workspace/index' }); }
    catch (error) { if (error.generation === getApp().session.getState().generation) this.setData({ error: message(error) }); }
  },
  async bind() {
    const code = this.data.bindingCode.trim();
    try { await getApp().session.bindAndLogin(code); this.setData({ bindingCode: '' }); wx.redirectTo({ url: '/pages/workspace/index' }); }
    catch (error) { if (error.generation === getApp().session.getState().generation) this.setData({ bindingCode: code, error: message(error) }); }
  },
  copyPortal() {
    const url = getApp().session.portalUrl();
    if (url) wx.setClipboardData({ data: url });
    else this.setData({ error: '当前尚未配置账号网页，请联系学校或平台管理员。' });
  },
  copyAccountHelp() {
    const url = getApp().session.portalUrl('/account/assistance');
    if (url) wx.setClipboardData({ data: url });
    else this.setData({ error: '当前尚未配置账号网页，请联系学校或平台管理员。' });
  },
});
