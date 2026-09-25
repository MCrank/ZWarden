// Live server status (#249 header, #253 fleet) — static-SSR progressive enhancement. The page is rendered once,
// so a stop/restart/boot never showed; this polls an authorized status endpoint and updates the badge (tone +
// label) and the lifecycle buttons' enabled state in place. Two page shapes:
//   [data-live-status="<url>"]  server-detail header — one status object; buttons anywhere on the page.
//   [data-live-fleet="<url>"]   fleet board — an array of { id, ... }; each [data-live-row="<id>"] is updated
//                               from its own entry (buttons scoped to that row); a row with no entry (the
//                               Server left the list) is left as rendered.
// Poll-driven, no circuit: the endpoint runs under the operator's own request (tenant + Server.View), which a
// circuit lacks. Faster while a mutating Operation is in flight, slower when idle, and only while the tab is
// visible. One loop for the whole app, so it survives Blazor enhanced navigation (a page without a marker simply
// isn't polled). Without JS the page stays as rendered and the service still re-checks every submit.
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
  // The fleet's last answer by Server id: re-applied when the interactive board re-renders its rows (a sort
  // moves them), which would otherwise leave a badge showing another row's status until the next poll.
  var lastFleet = null;
  var lastFleetRoot = null;

  // Every write is skipped when already current, so re-applying is idempotent and the fleet's mutation
  // observer settles instead of re-triggering itself.
  function retone(el, prefix, tone) {
    if (!el) { return; }
    TONES.forEach(function (t) {
      if (t !== tone && el.classList.contains(prefix + t)) { el.classList.remove(prefix + t); }
    });
    if (!el.classList.contains(prefix + tone)) { el.classList.add(prefix + tone); }
  }

  function applyBadge(scope, s) {
    retone(scope.querySelector('[data-status-badge]'), 'border-status-', s.tone);
    retone(scope.querySelector('[data-status-dot]'), 'bg-status-', s.tone);
    var label = scope.querySelector('[data-status-label]');
    var text = String(s.label || '');
    if (label && label.textContent !== text) { label.textContent = text; }
  }

  function applyControls(scope, s) {
    Object.keys(CONTROLS).forEach(function (action) {
      scope.querySelectorAll('[data-action="' + action + '"]').forEach(function (b) {
        var disabled = !s[CONTROLS[action]];
        if (b.disabled !== disabled) { b.disabled = disabled; }
      });
    });
  }

  function setBusy(root, busy) {
    var value = busy ? 'true' : 'false';
    if (root.getAttribute('data-status-busy') !== value) { root.setAttribute('data-status-busy', value); }
  }

  function applyHeader(root, s) {
    if (!s || TONES.indexOf(s.tone) === -1) { return; }
    applyBadge(root, s);
    setBusy(root, s.busy);
    applyControls(doc, s);
  }

  function applyFleet(root, byId) {
    var busy = false;
    Object.keys(byId).forEach(function (id) { busy = busy || !!byId[id].busy; });
    root.querySelectorAll('[data-live-row]').forEach(function (row) {
      var s = byId[row.getAttribute('data-live-row')];
      if (s && TONES.indexOf(s.tone) !== -1) {
        applyBadge(row, s);
        applyControls(row, s);
      }
    });
    setBusy(root, busy);
  }

  function observeFleet(root) {
    if (root.__zwLiveObserved || !window.MutationObserver) { return; }
    root.__zwLiveObserved = true;
    new window.MutationObserver(function () {
      if (lastFleet && lastFleetRoot === root && root.isConnected) { applyFleet(root, lastFleet); }
    }).observe(root, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ['class', 'data-live-row'] });
  }

  function currentRoot() {
    return doc.querySelector('[data-live-status]') || doc.querySelector('[data-live-fleet]');
  }

  function urlOf(root) {
    return root.getAttribute('data-live-status') || root.getAttribute('data-live-fleet');
  }

  function schedule(root) {
    var busy = root && root.getAttribute('data-status-busy') === 'true';
    window.setTimeout(tick, busy ? BUSY_MS : IDLE_MS);
  }

  function tick() {
    var root = currentRoot();
    if (!root || doc.visibilityState === 'hidden' || !window.fetch) { schedule(root); return; }
    var url = urlOf(root);
    var fleet = root.hasAttribute('data-live-fleet');
    if (fleet) { observeFleet(root); } else { lastFleet = null; lastFleetRoot = null; }
    window.fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' }, cache: 'no-store' })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (s) {
        // Navigated elsewhere while the request was out? Only apply to the page that asked.
        var current = currentRoot();
        if (!s || !current || urlOf(current) !== url) { return; }
        if (fleet) {
          if (!Array.isArray(s)) { return; }
          var byId = {};
          s.forEach(function (e) { if (e && typeof e.id === 'string') { byId[e.id] = e; } });
          lastFleet = byId;
          lastFleetRoot = current;
          observeFleet(current);
          applyFleet(current, byId);
        } else {
          applyHeader(current, s);
        }
      })
      .catch(function () { /* transient: the next tick tries again */ })
      .then(function () { schedule(currentRoot()); });
  }

  schedule(currentRoot());
})();
