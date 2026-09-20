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

  // Re-apply the saved light/dark preference to <html>. The server always renders <html class="dark">
  // (dark-first), and enhanced navigation re-merges that attribute on every in-app navigation — so the
  // App.razor pre-paint script (which runs only on a full load) isn't enough; we re-run this on each
  // enhanced load too, or a 'light' preference silently reverts to dark when you click another page.
  function applyTheme() {
    try {
      var t = localStorage.getItem('zw-theme');
      if (t === 'light') { root.classList.remove('dark'); }
      else if (t === 'dark') { root.classList.add('dark'); }
      // No stored preference: keep the server's dark-first default.
    } catch (e) { /* private mode */ }
  }

  // Copy the text content of the element named by [data-shell-copy] (a CSS selector) to the clipboard,
  // with brief "Copied" feedback. Progressive enhancement: where the clipboard API is absent the source
  // text stays selectable, so nothing is lost.
  function flashCopied(btn) {
    var label = btn.querySelector('[data-copy-label]') || btn;
    if (label.getAttribute('data-copy-original') === null) {
      label.setAttribute('data-copy-original', label.textContent);
    }
    label.textContent = btn.getAttribute('data-copy-done') || 'Copied';
    window.clearTimeout(btn._copyTimer);
    btn._copyTimer = window.setTimeout(function () {
      label.textContent = label.getAttribute('data-copy-original');
    }, 1500);
  }

  function copyFrom(btn) {
    var sel = btn.getAttribute('data-shell-copy');
    var el = sel ? doc.querySelector(sel) : null;
    var text = el ? (el.textContent || '').trim() : '';
    if (!text || !navigator.clipboard || !navigator.clipboard.writeText) { return; }
    navigator.clipboard.writeText(text).then(function () { flashCopied(btn); }).catch(function () { /* denied */ });
  }

  // Time-display preference (#211): opt in/out of local-time rendering. The zone id is written raw (IANA ids are
  // cookie-safe) so the server reads it back verbatim; clearing reverts to UTC. Reload so the server re-renders.
  function setTzCookie(value, days) {
    var d = new Date();
    d.setTime(d.getTime() + days * 864e5);
    doc.cookie = 'zw-tz=' + value + '; expires=' + d.toUTCString() + '; path=/; SameSite=Lax';
  }

  function toggleTimeZone(btn) {
    var on = btn.getAttribute('data-tz-on') === 'true';
    if (on) {
      setTzCookie('', -1); // clear the cookie ⇒ back to UTC
    } else {
      var zone = 'UTC';
      try { zone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'; } catch (e) { /* fall back to UTC */ }
      setTzCookie(zone, 365);
    }
    location.reload();
  }

  doc.addEventListener('click', function (e) {
    var t = e.target;
    if (!(t instanceof Element)) { return; }

    var menuTrigger = t.closest('[data-shell-menu-trigger]');
    if (menuTrigger) { e.preventDefault(); toggleMenu(menuTrigger.getAttribute('data-shell-menu-trigger')); return; }

    if (t.closest('[data-shell-theme-toggle]')) { e.preventDefault(); toggleTheme(); return; }

    var copyBtn = t.closest('[data-shell-copy]');
    if (copyBtn) { e.preventDefault(); copyFrom(copyBtn); return; }

    var tzBtn = t.closest('[data-shell-tz-toggle]');
    if (tzBtn) { e.preventDefault(); toggleTimeZone(tzBtn); return; }

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

  // Enhanced navigation re-merges the server's dark-first <html class="dark"> on each in-app navigation,
  // so re-assert the operator's saved theme after every enhanced load (fixes light mode reverting to dark).
  if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
    try { window.Blazor.addEventListener('enhancedload', applyTheme); } catch (e) { /* older runtime */ }
  }
})();
