const message = require('./messages');

// Read pages keep no business cache and discard results after leaving or changing identity.
module.exports = function readScope(page, app, allowed, clear, release = () => {}) {
  let visible = false;
  let disposed = false;
  let version = 0;
  let busy = false;
  let generation = app.session.getState().generation;
  const reset = () => { release(); if (!disposed) page.setData(clear()); };
  const unsubscribe = app.subscribe((state) => {
    if (generation !== state.generation || !state.authenticated) {
      generation = state.generation; version++; busy = false;
      reset(); if (!disposed) page.setData({ busy: false, error: message({ code: state.authenticated ? 'FORBIDDEN' : 'AUTH_REQUIRED' }) });
    } else if (!state.context || !allowed(state)) reset();
  });
  const current = (ticket, epoch, context) => !disposed && visible && ticket === version &&
    app.session.getState().generation === epoch && app.session.getState().context === context &&
    app.session.getState().authenticated && allowed(app.session.getState());

  return {
    show() { visible = true; reset(); },
    hide() { visible = false; version++; busy = false; reset(); if (!disposed) page.setData({ busy: false }); },
    dispose() { disposed = true; visible = false; version++; unsubscribe(); release(); },
    async run(read, apply) {
      if (disposed || !visible || busy) return;
      const ticket = ++version;
      const epoch = app.session.getState().generation;
      busy = true; reset(); page.setData({ busy: true, error: '' });
      try {
        await app.session.refreshWorkspace();
        const state = app.session.getState();
        if (disposed || !visible || ticket !== version || epoch !== state.generation) return;
        if (!state.authenticated || !allowed(state)) throw Object.assign(new Error('FORBIDDEN'), { code: 'FORBIDDEN', generation: epoch });
        const context = state.context;
        const result = await read();
        if (current(ticket, epoch, context)) apply(result);
      } catch (error) {
        if (!disposed && visible && ticket === version && epoch === app.session.getState().generation)
          page.setData({ error: message(error) });
      } finally {
        if (!disposed && ticket === version) { busy = false; page.setData({ busy: false }); }
      }
    },
  };
};
