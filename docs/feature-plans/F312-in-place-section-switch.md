# Issue #312 Mini-Plan — Switch Server Detail sections and config files in the circuit (no prerender)

**Status:** one PR (branch `feat/312-in-place-section-switch`, closes #312). Follow-up from #299.

**Written against:** issue #312;
[ADR 0046](../adr/0046-pages-may-opt-into-interactive-rendering-the-shell-stays-static.md) (interactive pages, static
router), [ADR 0040](../adr/0040-the-app-shell-is-static-ssr-chrome.md), ADR 0041 (the Agent request/reply channel),
`docs/feature-plans/F299-interactive-server-detail.md` ("Not done here").

## Objective

A rail click (`?section=`) or a config file tab (`?file=`) on Server Detail is an enhanced navigation today: the server
prerenders the whole page for the new URL (the `ServerDetailQuery` load, ~20 permission checks, the header and the
target section's own load, including a live config read from the Agent), throws it away, and the circuit then does the
section's load again. Switch in the circuit instead: no document request, one Agent read per config file switch, the URL
still bookmarkable, back/forward still working.

## Facts found (2026-10-04, `blazor.web.js` 10.0.12)

- Without an interactive router, `NavigationManager.NavigateTo` in a circuit is also an enhanced navigation
  (`navigateTo` → `lt`, the enhanced programmatic handler). .NET 10 has no supported "navigate an interactive page
  without a prerender" when the router is static.
- Blazor's enhanced link handler is a bubble-phase `click` listener on `document`; it skips an event whose default is
  prevented. Its `popstate` listener (on `window`, non-capture) does an enhanced load of `location.href` for any entry
  that isn't a hash-only change.
- An enhanced navigation tells every circuit's `NavigationManager` about the new URL as it starts (`LocationChanged`),
  before the fetch.
- `unsaved-guard.js` (#331) already ignores links to the same Server page, in a window capture listener.

## Decisions

- **D1 — a small JS module owns in-place navigation.** `wwwroot/js/in-place-nav.js`, attached by the page once its
  circuit renders:
  - a window capture `click` listener takes a plain left click on any link to **this Server page** (same path, any
    query), prevents the default, `history.pushState`s the URL and hands it to the page; modifier clicks, other targets
    and other pages are left alone (new tab, the shell's links, the unsaved guard);
  - a window capture `popstate` listener does the same for back/forward between entries of this page and stops
    Blazor's own handler (`stopImmediatePropagation`), so back/forward also switches without a prerender;
  - if the circuit can't take the call, the page reloads (the URL is already right).

  Chosen over `@onclick:preventDefault` on each link: that breaks Ctrl/middle-click and needs wiring in every section,
  while one listener covers every same-page link (rail, file tabs, a section's link to another section). Before the
  circuit is up, the links still work as enhanced navigation (the fallback).
- **D2 — the page owns its location.** The page drops `[SupplyParameterFromQuery]` and keeps the current URL itself,
  parsed by a small pure type, `ServerDetailLocation` (section, file, op, add). The URL changes from three places: the
  circuit's start (`NavigationManager.Uri`), a real navigation (`NavigationManager.LocationChanged`, the enhanced-nav
  fallback), and an in-place one (D1). The circuit's `NavigationManager` doesn't see a `pushState`, so it can't stay the
  source of truth. The page checks an in-place URL is on its own path; every query value stays as untrusted as before
  (the section resolves fail-closed, the file and op parse fail-closed).
- **D3 — sections navigate in place too.** The Config section's own moves to another file ("Load history", a by-path
  edit to another file) go through a cascaded `ServerDetailNavigator` instead of `NavigationManager.NavigateTo`, so they
  don't prerender either.
- **D4 — tests.**
  - Unit: `ServerDetailLocation` parsing.
  - bUnit: an in-place move switches the section, and a file switch does exactly one config read; a real navigation
    (the fallback) still switches.
  - Browser: the existing rail/tab test also asserts no request for the page's URL; a new test covers back/forward
    (no request, right section) and a reload of the pushed URL (bookmarkable).

## Slices

1. `ServerDetailLocation` + the page reading its location (D2), behaviour unchanged (enhanced nav still switches).
2. `in-place-nav.js` + the page's `[JSInvokable]` handler + `ServerDetailNavigator` (D1, D3); bUnit and browser tests.
3. Docs: `ui-components.md` (in-place navigation on interactive pages), this plan's result.

## Not in scope

- Scroll position on a switch (an in-place switch keeps it, as a tab switch should).
- Other interactive pages (Settings has no query-driven tabs).
