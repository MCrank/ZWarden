// Live configuration editor enhancements (#108 / F20c) — static-SSR progressive enhancement.
// The editor renders and applies without a circuit; this only adds client conveniences over the
// server-rendered markup: filter-as-you-type search, a dirty-row highlight, posting only the changed
// rows (#224), and a remembered collapse/expand-all (#243). Everything degrades gracefully without JS (all rows shown, the whole form posts and the
// server diffs it). Listeners are delegated on `document` so they survive Blazor enhanced-navigation DOM
// merges, exactly like shell.js.
(function () {
  'use strict';
  var doc = document;

  // ---- search: hide rows (and empty sections) that don't match the query ----
  function applyFilter(form, query) {
    var q = (query || '').trim().toLowerCase();
    form.querySelectorAll('[data-cfg-sec]').forEach(function (sec) {
      var visible = 0;
      sec.querySelectorAll('[data-cfg-row]').forEach(function (row) {
        var hay = row.getAttribute('data-cfg-terms') || '';
        var match = q === '' || hay.indexOf(q) !== -1;
        row.classList.toggle('zw-cfg-hidden', !match);
        if (match) { visible++; }
      });
      // Open matching sections while searching so hits aren't hidden inside a collapsed group; clearing the
      // search returns them to the remembered collapse/expand choice.
      sec.open = q !== '' ? visible > 0 : !sectionsCollapsed();
      sec.classList.toggle('zw-cfg-hidden', q !== '' && visible === 0);
    });
  }

  // ---- collapse / expand all, remembered per browser (#243) ----
  // One choice for every editor (INI, SandboxVars, every server), kept across Apply, reload and tab switches.
  // Applied on load; a section the operator then toggles by hand stays as they set it until the next page.
  var SECTIONS_KEY = 'zw-cfg-sections';

  function sectionsCollapsed() {
    try { return localStorage.getItem(SECTIONS_KEY) === 'collapsed'; } catch (e) { return false; /* private mode */ }
  }

  function setAllSections(form, collapsed) {
    form.querySelectorAll('[data-cfg-sec]').forEach(function (sec) { sec.open = !collapsed; });
  }

  doc.addEventListener('click', function (e) {
    var t = e.target;
    var button = t instanceof Element ? t.closest('[data-cfg-sections]') : null;
    var form = button ? button.closest('[data-cfg-form]') : null;
    if (!form) { return; }
    var collapsed = button.getAttribute('data-cfg-sections') === 'collapse';
    try { localStorage.setItem(SECTIONS_KEY, collapsed ? 'collapsed' : 'expanded'); } catch (err) { /* private mode */ }
    setAllSections(form, collapsed);
  });

  function initSections(form) {
    form.querySelectorAll('[data-cfg-sections-controls]').forEach(function (el) { el.hidden = false; });
    var search = form.querySelector('[data-cfg-search]');
    if (!search || search.value.trim() === '') { setAllSections(form, sectionsCollapsed()); }
  }

  // ---- dirty highlight: compare each control to the original value it was rendered with ----
  function isDirty(row) {
    var control = row.querySelector('[data-cfg-value]');
    if (!control) { return false; }
    var original = row.getAttribute('data-cfg-original') || '';
    var current = control.type === 'checkbox' ? String(control.checked) : control.value;
    return current !== original;
  }

  function markRow(row) {
    if (!row) { return; }
    row.classList.toggle('zw-cfg-changed', isDirty(row));
  }

  // ---- post only the changed rows (#224) ----
  // Each row posts ~5 fields, so a full SandboxVars (~280 settings) posted whole is large and can exceed the
  // server's form limits. On submit, untouched rows are disabled (disabled fields are not posted) and the changed
  // rows are renumbered from 0 — the server binds Rows[] as a list and stops at the first missing index. The
  // server diffs against each row's Original, so the outcome is the same as posting every row; without JS the
  // whole form posts and the page's raised limit covers it.
  var ROW_FIELD = /^_editorForm\.Rows\[\d+\]\./;

  function rowFields(row) {
    return Array.prototype.filter.call(row.querySelectorAll('[name]'), function (el) {
      return ROW_FIELD.test(el.getAttribute('data-cfg-name') || el.name);
    });
  }

  function trimToChanged(form) {
    var next = 0;
    form.querySelectorAll('[data-cfg-row]').forEach(function (row) {
      var dirty = isDirty(row);
      var index = dirty ? next++ : -1;
      rowFields(row).forEach(function (el) {
        if (!el.hasAttribute('data-cfg-name')) { el.setAttribute('data-cfg-name', el.name); }
        if (dirty) {
          el.name = el.getAttribute('data-cfg-name').replace(/^_editorForm\.Rows\[\d+\]/, '_editorForm.Rows[' + index + ']');
        } else {
          el.disabled = true;
        }
      });
    });
  }

  // Undo trimToChanged, e.g. when the browser restores this page from its back/forward cache.
  function restoreRows(form) {
    form.querySelectorAll('[data-cfg-name]').forEach(function (el) {
      el.name = el.getAttribute('data-cfg-name');
      el.removeAttribute('data-cfg-name');
      el.disabled = false;
    });
  }

  // Capture phase, so the fields are trimmed before anything else reads the form.
  doc.addEventListener('submit', function (e) {
    var form = e.target;
    if (form instanceof HTMLFormElement && form.hasAttribute('data-cfg-form')) { trimToChanged(form); }
  }, true);

  window.addEventListener('pageshow', function (e) {
    if (e.persisted) { doc.querySelectorAll('[data-cfg-form]').forEach(restoreRows); }
  });

  function markAll(form) {
    form.querySelectorAll('[data-cfg-row]').forEach(markRow);
  }

  doc.addEventListener('input', function (e) {
    var t = e.target;
    if (!(t instanceof Element)) { return; }

    var search = t.closest('[data-cfg-search]');
    if (search) {
      var searchForm = search.closest('[data-cfg-form]');
      if (searchForm) { applyFilter(searchForm, search.value); }
      return;
    }

    if (t.closest('[data-cfg-value]')) {
      markRow(t.closest('[data-cfg-row]'));
    }
  });

  doc.addEventListener('change', function (e) {
    var t = e.target;
    if (t instanceof Element && t.closest('[data-cfg-value]')) {
      markRow(t.closest('[data-cfg-row]'));
    }
  });

  // ---- refresh while a configuration write is applying (#226) ----
  // The server marks the page while a tracked write is still running; reload (a GET) until it has finished, when
  // the server stops emitting the marker and the editor shows the written values. <noscript> covers the no-JS path.
  var refreshTimer = null;
  function scheduleRefresh() {
    var marker = doc.querySelector('[data-cfg-refresh]');
    if (!marker || refreshTimer !== null) { return; }
    var seconds = parseInt(marker.getAttribute('data-cfg-refresh'), 10) || 2;
    refreshTimer = window.setTimeout(function () { window.location.reload(); }, seconds * 1000);
  }

  // Initial pass on load / after enhanced navigation so a re-rendered editor reflects any bound state.
  function init() {
    doc.querySelectorAll('[data-cfg-form]').forEach(function (form) { markAll(form); initSections(form); });
    scheduleRefresh();
  }
  doc.addEventListener('DOMContentLoaded', init);
  doc.addEventListener('enhancedload', init);
})();
