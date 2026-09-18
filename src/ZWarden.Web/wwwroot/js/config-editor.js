// Live configuration editor enhancements (#108 / F20c) — static-SSR progressive enhancement.
// The editor renders and applies without a circuit; this only adds client conveniences over the
// server-rendered markup: filter-as-you-type search and a dirty-row highlight. Everything degrades
// gracefully without JS (all rows shown, the form still posts the changed values). Listeners are
// delegated on `document` so they survive Blazor enhanced-navigation DOM merges, exactly like shell.js.
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
      // Open matching sections while searching so hits aren't hidden inside a collapsed group.
      if (q !== '') { sec.open = visible > 0; }
      sec.classList.toggle('zw-cfg-hidden', q !== '' && visible === 0);
    });
  }

  // ---- dirty highlight: compare each control to the original value it was rendered with ----
  function markRow(row) {
    if (!row) { return; }
    var control = row.querySelector('[data-cfg-value]');
    if (!control) { return; }
    var original = row.getAttribute('data-cfg-original');
    var current = control.type === 'checkbox' ? String(control.checked) : control.value;
    row.classList.toggle('zw-cfg-changed', current !== original);
  }

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

  // Initial pass on load / after enhanced navigation so a re-rendered editor reflects any bound state.
  function init() { doc.querySelectorAll('[data-cfg-form]').forEach(markAll); }
  doc.addEventListener('DOMContentLoaded', init);
  doc.addEventListener('enhancedload', init);
})();
