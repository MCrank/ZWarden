// The circuit reconnect dialog (#299; Components/Layout/ReconnectModal.razor), after the .NET 10 template. Blazor
// adds the state class to #components-reconnect-modal itself and raises components-reconnect-state-changed; this
// opens and closes the dialog and runs Retry / Resume / Reload. A rejected reconnect (the server no longer knows the
// circuit) reloads the page. Loaded once from App; a page without a circuit never raises the event.
(function () {
  'use strict';
  var modal = document.getElementById('components-reconnect-modal');
  if (!modal) { return; }

  function open() {
    if (!modal.open && typeof modal.showModal === 'function') { modal.showModal(); }
  }

  function close() {
    if (modal.open) { modal.close(); }
  }

  async function retry() {
    document.removeEventListener('visibilitychange', retryWhenVisible);
    try {
      // Rejoin the same circuit; failing that, resume a new one from its persisted state ([PersistentState]).
      if (await Blazor.reconnect()) { return; }
      if (await Blazor.resumeCircuit()) { close(); return; }
      location.reload();
    } catch (e) {
      document.addEventListener('visibilitychange', retryWhenVisible);
    }
  }

  async function resume() {
    try {
      if (!(await Blazor.resumeCircuit())) { location.reload(); }
    } catch (e) {
      location.reload();
    }
  }

  async function retryWhenVisible() {
    if (document.visibilityState === 'visible') { await retry(); }
  }

  modal.addEventListener('components-reconnect-state-changed', function (event) {
    var state = event.detail && event.detail.state;
    if (state === 'show') { open(); }
    else if (state === 'hide') { close(); }
    else if (state === 'failed') { document.addEventListener('visibilitychange', retryWhenVisible); }
    else if (state === 'rejected') { location.reload(); }
  });

  // An alert dialog: Esc must not dismiss it while the page is unusable.
  modal.addEventListener('cancel', function (event) { event.preventDefault(); });

  document.getElementById('components-reconnect-button').addEventListener('click', retry);
  document.getElementById('components-resume-button').addEventListener('click', resume);
  modal.querySelector('[data-reconnect-reload]').addEventListener('click', function () { location.reload(); });
})();
