const createSession = require('./utils/session');
const configuration = require('./config');

App({
  onLaunch() {
    this.configuration = configuration;
    this.listeners = new Set();
    this.session = createSession(wx, configuration, (state) => this.listeners.forEach((listener) => listener(state)));
  },
  subscribe(listener) { this.listeners.add(listener); return () => this.listeners.delete(listener); },
});
