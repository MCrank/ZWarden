// Live server status (#249 header, #253 fleet) — static-SSR progressive enhancement. The page is rendered once,
// so a stop/restart/boot never showed; this polls an authorized status endpoint and updates the badge (tone +
// label) and the lifecycle buttons' enabled state in place. Two page shapes:
//   [data-live-status="<url>"]  server-detail header — one status object; buttons anywhere on the page.
//   [data-live-fleet="<url>"]   fleet board — an array of { id, ... }; each [data-live-row="<id>"] is updated
//                               from its own entry (buttons scoped to that row), as are its fact cells
//                               ([data-fleet-server="<id>"]: players, CPU, memory, uptime, version — #257) and
//                               the KPI tiles ([data-kpi-strip]); a row with no entry (the Server left the list)
//                               is left as rendered.
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
    // What the in-flight Operation is doing (#254) — Agent text, so textContent only, never markup.
    var detail = scope.querySelector('[data-status-detail]');
    if (detail) {
      var detailText = s.detail ? String(s.detail) : '';
      if (detail.textContent !== detailText) { detail.textContent = detailText; }
      if (detail.hidden !== !s.detail) { detail.hidden = !s.detail; }
    }
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
          setText(cell, known ? String(s.players) : DASH);
          setAttr(cell, 'title', known && s.playersAge ? String(s.playersAge) : null);
          swapClass(cell, ['text-foreground', 'text-muted-foreground'], known ? 'text-foreground' : 'text-muted-foreground');
          break;
        case 'uptime':
          setText(cell, s.uptime ? String(s.uptime) : DASH);
          break;
        case 'version':
          setText(cell, s.version ? String(s.version) : DASH);
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
    root.querySelectorAll('[data-live-row]').forEach(function (row) {
      var s = byId[row.getAttribute('data-live-row')];
      if (s && TONES.indexOf(s.tone) !== -1) {
        applyBadge(row, s);
        applyControls(row, s);
      }
    });
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
          applyKpis(s);
        } else {
          applyHeader(current, s);
        }
      })
      .catch(function () { /* transient: the next tick tries again */ })
      .then(function () { schedule(currentRoot()); });
  }

  schedule(currentRoot());
})();
