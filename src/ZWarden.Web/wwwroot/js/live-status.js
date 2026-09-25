// Live server-detail header status (#249) — static-SSR progressive enhancement. The header is rendered once
// with the page, so a stop/restart/boot never showed; this polls the authorized status endpoint named by
// [data-live-status] and updates the badge (tone + label) and the lifecycle buttons' enabled state in place.
// Poll-driven, no circuit: the endpoint runs under the operator's own request (tenant + Server.View), which a
// circuit lacks. Faster while a mutating Operation is in flight, slower when idle, and only while the tab is
// visible. One loop for the whole app, so it survives Blazor enhanced navigation (a page without the marker
// simply isn't polled). Without JS the header stays as rendered and the service still re-checks every submit.
(function () {
  'use strict';
  var doc = document;
  var TONES = ['running', 'stopped', 'busy', 'unhealthy', 'unknown'];
  var BUSY_MS = 2000;
  var IDLE_MS = 5000;
  // Which status flag enables which lifecycle control (header buttons + the graceful-restart panel).
  var CONTROLS = {
    'server-start': 'canStart',
    'server-stop': 'canStop',
    'server-restart': 'canRestart',
    'graceful-restart': 'canRestart'
  };

  function retone(el, prefix, tone) {
    if (!el) { return; }
    TONES.forEach(function (t) { el.classList.remove(prefix + t); });
    el.classList.add(prefix + tone);
  }

  function apply(root, s) {
    if (TONES.indexOf(s.tone) === -1) { return; }
    var badge = root.querySelector('[data-status-badge]');
    retone(badge, 'border-status-', s.tone);
    retone(root.querySelector('[data-status-dot]'), 'bg-status-', s.tone);
    var label = root.querySelector('[data-status-label]');
    if (label) { label.textContent = String(s.label || ''); }
    // What the in-flight Operation is doing (#254) — Agent text, so textContent only, never markup.
    var detail = root.querySelector('[data-status-detail]');
    if (detail) {
      detail.textContent = s.detail ? String(s.detail) : '';
      detail.hidden = !s.detail;
    }
    root.setAttribute('data-status-busy', s.busy ? 'true' : 'false');
    Object.keys(CONTROLS).forEach(function (action) {
      doc.querySelectorAll('[data-action="' + action + '"]').forEach(function (b) { b.disabled = !s[CONTROLS[action]]; });
    });
  }

  function schedule(root) {
    var busy = root && root.getAttribute('data-status-busy') === 'true';
    window.setTimeout(tick, busy ? BUSY_MS : IDLE_MS);
  }

  function tick() {
    var root = doc.querySelector('[data-live-status]');
    if (!root || doc.visibilityState === 'hidden' || !window.fetch) { schedule(root); return; }
    var url = root.getAttribute('data-live-status');
    window.fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' }, cache: 'no-store' })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (s) {
        // Navigated elsewhere while the request was out? Only apply to the page that asked.
        var current = doc.querySelector('[data-live-status]');
        if (s && current && current.getAttribute('data-live-status') === url) { apply(current, s); }
      })
      .catch(function () { /* transient: the next tick tries again */ })
      .then(function () { schedule(doc.querySelector('[data-live-status]')); });
  }

  schedule(doc.querySelector('[data-live-status]'));
})();
