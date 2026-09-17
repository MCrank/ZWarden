// App-shell behaviour for the static-SSR chrome (#157, ADR 0040): dropdown menus, the mobile drawer,
// and the light/dark toggle. All listeners are delegated on `document`, so they keep working across
// Blazor enhanced-navigation DOM merges (the layout is never re-initialised per page). No Blazor circuit
// is involved — this is plain progressive enhancement over server-rendered markup.
(function () {
  'use strict';
  var doc = document;
  var root = doc.documentElement;

  function menuFor(name) { return doc.querySelector('[data-shell-menu="' + name + '"]'); }

  function syncTrigger(name) {
    var menu = menuFor(name);
    var trigger = doc.querySelector('[data-shell-menu-trigger="' + name + '"]');
    if (trigger) { trigger.setAttribute('aria-expanded', menu && !menu.hidden ? 'true' : 'false'); }
  }

  function closeAllMenus(except) {
    doc.querySelectorAll('[data-shell-menu]').forEach(function (m) {
      if (m !== except) { m.hidden = true; }
    });
    doc.querySelectorAll('[data-shell-menu-trigger]').forEach(function (t) {
      syncTrigger(t.getAttribute('data-shell-menu-trigger'));
    });
  }

  function toggleMenu(name) {
    var menu = menuFor(name);
    if (!menu) { return; }
    var willOpen = menu.hidden;
    closeAllMenus(willOpen ? menu : null);
    menu.hidden = !willOpen;
    syncTrigger(name);
  }

  function setDrawer(open) { root.classList.toggle('zw-nav-open', open); }

  function toggleTheme() {
    var dark = root.classList.toggle('dark');
    try { localStorage.setItem('zw-theme', dark ? 'dark' : 'light'); } catch (e) { /* private mode */ }
  }

  doc.addEventListener('click', function (e) {
    var t = e.target;
    if (!(t instanceof Element)) { return; }

    var menuTrigger = t.closest('[data-shell-menu-trigger]');
    if (menuTrigger) { e.preventDefault(); toggleMenu(menuTrigger.getAttribute('data-shell-menu-trigger')); return; }

    if (t.closest('[data-shell-theme-toggle]')) { e.preventDefault(); toggleTheme(); return; }

    if (t.closest('[data-shell-drawer-toggle]')) {
      e.preventDefault();
      setDrawer(!root.classList.contains('zw-nav-open'));
      return;
    }
    if (t.closest('[data-shell-drawer-close]')) { setDrawer(false); return; }

    // Following a sidebar link: let it navigate, but collapse the mobile drawer and any open menu.
    if (t.closest('[data-shell-nav] a[href]')) { setDrawer(false); closeAllMenus(null); return; }

    // A click inside an open menu (a link or the submit button): let it act; navigation clears the menu.
    if (t.closest('[data-shell-menu]')) { return; }

    // Anything else is an outside click — dismiss open menus.
    closeAllMenus(null);
  });

  doc.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') { closeAllMenus(null); setDrawer(false); }
  });
})();
