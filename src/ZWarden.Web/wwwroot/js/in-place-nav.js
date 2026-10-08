// #312: switches Server Detail's rail sections and config files (and, #344, Settings' sections) in the circuit. The router is static (ADR 0046), so a
// click on one of the page's own links would otherwise be an enhanced navigation: the server prerenders the whole page
// for the new URL (the Server, every permission check, the section's load, a live config read from the Agent), throws
// it away, and the circuit then loads the section again. Here a plain click on a link to this Server page, and a
// back/forward between its entries, update the address bar and hand the URL to the page instead. Anything else (a
// modifier click, another page, the page before its circuit is up) is left to the browser and Blazor.
// Loaded before blazor.web.js: Blazor's own popstate listener is on window too, and listeners on the same target run in
// the order they were added, so this one has to be added first to stop it.
(function () {
  'use strict';
  // { path, subpaths, page, instance } while a page's circuit is up: Server Detail, or Settings (#344), which also claims
  // the paths below its own. page is the .NET component.
  var current = null;

  function isThisPage(url) {
    if (current === null || url.origin !== location.origin) {
      return false;
    }

    var path = url.pathname.replace(/\/$/, '').toLowerCase();
    return path === current.path || (current.subpaths && path.indexOf(current.path + '/') === 0);
  }

  // Hands the URL to the page. If the circuit can't take it (gone, or not this page after all), the address bar already
  // shows the URL, so a reload lands on it.
  function show(url) {
    current.page.invokeMethodAsync('NavigatedInPlace', url).then(
      function (shown) { if (!shown) { location.reload(); } },
      function () { location.reload(); });
  }

  function push(url) {
    if (url !== location.href) {
      history.pushState(null, '', url);
    }
  }

  window.addEventListener('click', function (event) {
    if (!current || event.defaultPrevented || event.button !== 0
      || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
      return;
    }

    var link = event.target instanceof Element ? event.target.closest('a[href]') : null;
    if (!link || (link.target && link.target !== '_self') || link.hasAttribute('download')) {
      return;
    }

    var url = new URL(link.href, document.baseURI);
    if (!isThisPage(url)) {
      return;
    }

    event.preventDefault(); // Blazor's enhanced navigation skips a click whose default is prevented.
    push(url.href);
    show(url.href);
  }, true);

  // Back/forward between two entries of this page: Blazor's own listener would load the page again.
  window.addEventListener('popstate', function (event) {
    if (isThisPage(new URL(location.href))) {
      event.stopImmediatePropagation();
      show(location.href);
    }
  }, true);

  window.zwInPlaceNav = {
    // Takes over the links of the page at `path` for `page` (a DotNetObjectReference); `instance` names this page so a
    // later detach of an older one leaves it alone. `options.subpaths` also claims the paths below `path`
    // (/settings/security for /settings).
    attach: function (path, page, instance, options) {
      current = { path: path.toLowerCase(), subpaths: !!(options && options.subpaths), page: page, instance: instance };
    },
    detach: function (instance) {
      if (current && current.instance === instance) {
        current = null;
      }
    },
    // Puts `url` (absolute, on this page) in the address bar as a new history entry, as a link click would.
    push: push,
  };
})();
