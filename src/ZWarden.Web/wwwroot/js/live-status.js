// Live fleet status (#253) — static-SSR progressive enhancement for the Fleet board (/servers), which stays static
// (ADR 0046). The page is rendered once, so a stop/restart/boot never showed; this polls the authorized batched
// status endpoint ([data-live-fleet="<url>"]: an array of { id, ... }) and updates each [data-live-row="<id>"]'s
// badge from its own entry, its fact cells ([data-fleet-server="<id>"]: players, CPU, memory, uptime, version —
// #257) and the KPI tiles ([data-kpi-strip]); a row with no entry (the Server left the list) triggers one reload.
// The Server Detail header used to ride this script too (#249); since #299 that page is interactive and its
// circuit polls the header itself.
// Poll-driven, no circuit: the endpoint runs under the operator's own request (tenant + Server.View). Faster while
// a mutating Operation is in flight, slower when idle, and only while the tab is visible. One loop for the whole
// app, so it survives Blazor enhanced navigation (a page without the marker simply isn't polled).
(function () {
  'use strict';
  var doc = document;
  var TONES = ['running', 'stopped', 'busy', 'unhealthy', 'unknown'];
  var BUSY_MS = 2000;
  var IDLE_MS = 5000;
  // The fleet's last answer by Server id: re-applied when the interactive board re-renders its rows (a sort
  // moves them), which would otherwise leave a badge showing another row's status until the next poll.
  var lastFleet = null;
  var lastFleetRoot = null;

  function reloadOnceFor(ids) {
    var key = ids.slice().sort().join(',');
    try {
      if (window.sessionStorage.getItem('zw-fleet-gone') === key) { return false; }
      window.sessionStorage.setItem('zw-fleet-gone', key);
    } catch (e) { return false; /* no storage: never risk a reload loop */ }
    window.location.reload();
    return true;
  }

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
    // One tone class tints the whole chip and its dot (#335, status.css).
    retone(scope.querySelector('[data-status-badge]'), 'zw-status-', s.tone);
    var label = scope.querySelector('[data-status-label]');
    var text = String(s.label || '');
    if (label && label.textContent !== text) { label.textContent = text; }
    // What the in-flight Operation is doing (#254) — Agent text, so textContent only, never markup.
    var detail = scope.querySelector('[data-status-detail]');
    if (detail) {
      var detailText = s.detail ? String(s.detail) : '';
      if (detail.textContent !== detailText) { detail.textContent = detailText; }
      if (detail.hidden !== !s.detail) { detail.hidden = !s.detail; }
    }
  }

  function setBusy(root, busy) {
    var value = busy ? 'true' : 'false';
    if (root.getAttribute('data-status-busy') !== value) { root.setAttribute('data-status-busy', value); }
  }

  // #257: the fleet facts. Every value is Agent-observed data, so it is written with textContent only. Uptime and
  // the players' sample age arrive preformatted (server clock); only the meters and the KPI sums are worked out
  // here, mirroring MeterBar (percent + threshold) and FleetFacts.Kpis.
  var DASH = '—';
  var METER_FILLS = ['bg-meter-nominal', 'bg-meter-watch', 'bg-meter-hot'];

  function setText(el, text) {
    if (el && el.textContent !== text) { el.textContent = text; }
  }

  function setHidden(el, hidden) {
    if (el && el.hidden !== hidden) { el.hidden = hidden; }
  }

  function setAttr(el, name, value) {
    if (!el) { return; }
    if (value === null) {
      if (el.hasAttribute(name)) { el.removeAttribute(name); }
    } else if (el.getAttribute(name) !== value) {
      el.setAttribute(name, value);
    }
  }

  function swapClass(el, all, wanted) {
    if (!el) { return; }
    all.forEach(function (c) {
      if (c !== wanted && el.classList.contains(c)) { el.classList.remove(c); }
    });
    if (!el.classList.contains(wanted)) { el.classList.add(wanted); }
  }

  function applyMeter(cell, value, max) {
    var slot = cell.querySelector('[data-meter-slot]');
    var has = typeof value === 'number' && typeof max === 'number' && max > 0;
    setHidden(slot, !has);
    setHidden(cell.querySelector('[data-meter-empty]'), has);
    var meter = has && slot ? slot.querySelector('[data-meter]') : null;
    if (!meter) { return; }
    var percent = Math.min(100, Math.max(0, value / max * 100));
    var rounded = Math.round(percent);
    var fill = meter.querySelector('[data-meter-fill]');
    if (fill && fill.style.width !== rounded + '%') { fill.style.width = rounded + '%'; }
    swapClass(fill, METER_FILLS, percent < 60 ? 'bg-meter-nominal' : percent <= 85 ? 'bg-meter-watch' : 'bg-meter-hot');
    setText(meter.querySelector('[data-meter-text]'), rounded + '%');
    setAttr(meter, 'aria-valuenow', String(rounded));
    var label = meter.getAttribute('data-meter-label');
    setAttr(meter, 'aria-label', (label ? label + ': ' : '') + rounded + '%');
  }

  function applyFacts(root, byId) {
    root.querySelectorAll('[data-fleet-server]').forEach(function (cell) {
      var s = byId[cell.getAttribute('data-fleet-server')];
      if (!s) { return; }
      switch (cell.getAttribute('data-fleet-cell')) {
        case 'players':
          var known = typeof s.players === 'number';
          // #337: "2 / 16", preformatted by the server (FleetFacts.FormatPlayers); the bare count from an older Web.
          setText(cell, typeof s.playersText === 'string' ? s.playersText : known ? String(s.players) : DASH);
          setAttr(cell, 'title', known && s.playersAge ? String(s.playersAge) : null);
          swapClass(cell, ['text-foreground', 'text-muted-foreground'], known ? 'text-foreground' : 'text-muted-foreground');
          break;
        case 'uptime':
          setText(cell, s.uptime ? String(s.uptime) : DASH);
          break;
        case 'version':
          setText(cell, s.version ? String(s.version) : DASH);
          setAttr(cell, 'title', s.versionTitle ? String(s.versionTitle) : null);
          break;
        case 'cpu':
          applyMeter(cell, s.cpuPercent, 100);
          break;
        case 'memory':
          applyMeter(cell, s.memoryUsedBytes, s.memoryLimitBytes);
          break;
      }
    });
  }

  function kpiTile(strip, key) {
    return strip.querySelector('[data-kpi="' + key + '"]');
  }

  function applyKpis(list) {
    var strip = doc.querySelector('[data-kpi-strip]');
    if (!strip) { return; }
    var running = 0, attention = 0, attentionName = null, players = 0, anyPlayers = false;
    list.forEach(function (e) {
      if (e.running) { running++; }
      if (e.attention) {
        attention++;
        if (attentionName === null) { attentionName = e.name ? String(e.name) : ''; }
      }
      if (typeof e.players === 'number') { players += e.players; anyPlayers = true; }
    });

    var runningTile = kpiTile(strip, 'running');
    if (runningTile) { setText(runningTile.querySelector('[data-kpi-value]'), String(running)); }

    var attentionTile = kpiTile(strip, 'needs-attention');
    if (attentionTile) {
      var value = attentionTile.querySelector('[data-kpi-value]');
      setText(value, String(attention));
      swapClass(value, ['text-status-unhealthy', 'text-foreground'], attention > 0 ? 'text-status-unhealthy' : 'text-foreground');
      setText(attentionTile.querySelector('[data-kpi-sub]'),
        attention === 0 ? 'all healthy' : (attentionName ? attentionName + ' · unhealthy' : 'unhealthy'));
    }

    var playersTile = kpiTile(strip, 'players');
    if (playersTile) { setText(playersTile.querySelector('[data-kpi-value]'), anyPlayers ? String(players) : DASH); }
  }

  function applyFleet(root, byId) {
    var busy = false;
    Object.keys(byId).forEach(function (id) { busy = busy || !!byId[id].busy; });
    var gone = [];
    root.querySelectorAll('[data-live-row]').forEach(function (row) {
      var id = row.getAttribute('data-live-row');
      var s = byId[id];
      if (!s) { gone.push(id); return; }
      if (TONES.indexOf(s.tone) !== -1) { applyBadge(row, s); }
    });
    // A rendered server the batch no longer reports has left the fleet (#271, deleted). Reload so the board, its
    // counts and KPI tiles re-render without it — at most once per set of missing servers, so a render/batch mismatch
    // can never loop.
    if (gone.length && reloadOnceFor(gone)) { return; }
    applyFacts(root, byId);
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
    return doc.querySelector('[data-live-fleet]');
  }

  function urlOf(root) {
    return root.getAttribute('data-live-fleet');
  }

  function schedule(root) {
    var busy = root && root.getAttribute('data-status-busy') === 'true';
    window.setTimeout(tick, busy ? BUSY_MS : IDLE_MS);
  }

  function tick() {
    var root = currentRoot();
    if (!root || doc.visibilityState === 'hidden' || !window.fetch) { schedule(root); return; }
    var url = urlOf(root);
    observeFleet(root);
    window.fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' }, cache: 'no-store' })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (s) {
        // Navigated elsewhere while the request was out? Only apply to the page that asked.
        var current = currentRoot();
        if (!Array.isArray(s) || !current || urlOf(current) !== url) { return; }
        var byId = {};
        s.forEach(function (e) { if (e && typeof e.id === 'string') { byId[e.id] = e; } });
        lastFleet = byId;
        lastFleetRoot = current;
        observeFleet(current);
        applyFleet(current, byId);
        applyKpis(s);
      })
      .catch(function () { /* transient: the next tick tries again */ })
      .then(function () { schedule(currentRoot()); });
  }

  schedule(currentRoot());

  // #170: the Hosts page's card telemetry. Polls [data-live-hosts="<url>"] (an array of { id, ... } for the connected
  // Hosts) and moves each [data-host-telemetry-for="<agentId>"] card's meters and lines in place. The texts and the age
  // arrive preformatted (server clock); everything is Agent-observed data, so textContent only. A Host that connects
  // or disconnects changes the card's layout, so that still shows on the next load.
  var AGE_TONES = ['text-status-busy', 'text-muted-foreground'];

  function applyHosts(root, byId) {
    root.querySelectorAll('[data-host-telemetry-for]').forEach(function (card) {
      var h = byId[card.getAttribute('data-host-telemetry-for')];
      if (!h) { return; }
      card.querySelectorAll('[data-host-cell]').forEach(function (cell) {
        switch (cell.getAttribute('data-host-cell')) {
          case 'cpu':
            applyMeter(cell, h.cpuPercent, 100);
            break;
          case 'memory':
            applyMeter(cell, h.memoryUsedBytes, h.memoryTotalBytes);
            break;
          case 'memory-text':
            setText(cell, h.memoryText ? String(h.memoryText) : DASH);
            break;
          case 'disk':
            applyMeter(cell, h.diskUsedBytes, h.diskTotalBytes);
            break;
          case 'disk-text':
            setText(cell, h.diskText ? String(h.diskText) : DASH);
            break;
          case 'age':
            setText(cell, h.age ? String(h.age) : '');
            swapClass(cell, AGE_TONES, h.stale ? 'text-status-busy' : 'text-muted-foreground');
            break;
        }
      });
    });
  }

  function hostsRoot() {
    return doc.querySelector('[data-live-hosts]');
  }

  function scheduleHosts() {
    window.setTimeout(hostsTick, IDLE_MS);
  }

  function hostsTick() {
    var root = hostsRoot();
    if (!root || doc.visibilityState === 'hidden' || !window.fetch) { scheduleHosts(); return; }
    var url = root.getAttribute('data-live-hosts');
    window.fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' }, cache: 'no-store' })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (list) {
        var current = hostsRoot();
        if (!Array.isArray(list) || !current || current.getAttribute('data-live-hosts') !== url) { return; }
        var byId = {};
        list.forEach(function (e) { if (e && typeof e.id === 'string') { byId[e.id] = e; } });
        applyHosts(current, byId);
      })
      .catch(function () { /* transient: the next tick tries again */ })
      .then(scheduleHosts);
  }

  scheduleHosts();
})();
