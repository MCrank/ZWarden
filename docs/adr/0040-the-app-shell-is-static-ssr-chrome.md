# 40. The app shell is static-SSR chrome, not an interactive island

**The sidebar-09 app shell (`MainLayout.razor`) is hand-rolled as static
server-rendered chrome** — `NavLink` for nav, `AuthorizeView` for visibility, a
static `/logout` form, and a small vanilla-JS/CSS layer for the menus, mobile
drawer and dark-mode toggle. It uses **no interactive BlazorBlueprint
components**, so it opens **no Blazor Server circuit**. This is a deliberate,
documented exception to ADR 0003 ("prefer `Bb*` unless there is no SSR-safe fit").

- Status: accepted
- Decided in: #157 (UI redesign epic #156); user-approved after a scale analysis
- Bears on: ADR 0003 (Blueprint wrapper seam), PRD 12 (server-side authz), #63
  (static auth pages), #154 (authz-in-circuit concurrency)

## Context

sidebar-09 is built from `BbSidebar`, `BbDropdownMenu` and the overlay providers,
all of which **require an interactive render mode** (BlazorBlueprint setup docs).
Two hard facts make dropping them into `MainLayout` impossible without changing
the whole app's render posture:

1. A statically-rendered parent **cannot pass a `RenderFragment` (child content)
   to an interactive child** — so an interactive shell island cannot wrap the
   static `@Body` (Microsoft "Blazor render modes": render-mode boundary rule).
2. `MainLayout` itself **cannot be made interactive** — its `Body` parameter is a
   `RenderFragment`. Interactivity for the layout is all-or-nothing: it requires a
   **global** interactive render mode on `Routes`/`HeadOutlet`.

ZWarden is static-SSR-first on purpose: the sign-in/out cookie flow needs a real
HTTP response (#63); /audit, /hosts, /servers and /setup render per request
against a live `HttpContext`; and authorization run inside a circuit is where the
DbContext-concurrency bug behind #154 lived. Going globally interactive would
undo all of that.

The remaining option — interactive *chrome islands* beside a static `@Body` —
still means **one Blazor Server circuit on every authenticated page** (a single
interactive component on a page opens the circuit). A circuit is stateful: it
holds the page's component tree in server RAM and a SignalR WebSocket for as long
as the tab is open, is pinned to one instance (sticky load-balancing), and is
dropped on every deploy. ZWarden is self-hosted today but is intended to become a
hosted SaaS; at hundreds–thousands of concurrent operators, paying a stateful
circuit on every page — including pages that are just static reading — is the
expensive, hard-to-scale shape. Stateless SSR chrome scales flat and horizontally.

## Decision

Render the shell as **static SSR chrome**:

- Nav uses `NavLink` (active state) inside `AuthorizeView` (visibility only —
  enforcement stays server-side per page, PRD 12). Gating is unchanged from the
  stopgap nav: `Agent.View` → Hosts, `Audit.View` → Audit, **Fleet always shown**.
- **Log out** is a static `<form method="post" action="/logout">` with an
  antiforgery token (the existing endpoint), not a C# click handler.
- Menus (workspace switcher, account menu), the mobile drawer, and the
  light/dark toggle are driven by one small `wwwroot/js/shell.js` using
  **document-level delegated listeners** (so they survive enhanced-navigation DOM
  merges) plus CSS. The `.dark` class on `<html>` is applied pre-paint by a
  no-FOUC head script and persisted to `localStorage`; ZWarden's Signal tokens in
  `zwarden.css` remain the single source of truth (Blueprint's own ThemeService /
  `themes.css` palette system is **not** adopted, to avoid overriding Signal).
- **Overlay providers (`BbPortalHost`/`BbToastProvider`/`BbDialogProvider`) are
  not placed in the shell** — they need a circuit and would be inert here.
  Interactive features add them inside their own `@rendermode` islands, as today.

Interactive render modes remain available and are used exactly where
interactivity is genuinely needed — the ServerDetail `Live*` panels — as opt-in
islands.

## Alternatives considered

- **Global interactivity** (`@rendermode="InteractiveServer"` on `Routes`/
  `HeadOutlet`): the only way to use the vanilla `BbSidebarProvider`/`BbSidebarInset`
  pattern cleanly. Rejected: breaks the static auth flow (#63) and every
  per-request `HttpContext` page, and pushes all authorization into circuits
  (#154 territory). Highest blast radius.
- **Interactive chrome islands + static `@Body`**: keeps most pages static but
  adds a circuit to *every* authenticated page for the chrome alone — the stateful,
  sticky, per-tab cost that does not scale for SaaS. Rejected on the scale
  analysis, not on taste. Remains available if the product later grows enough
  rich interactivity to justify a stateful tier — this ADR would then be revisited.
- **Adopt Blueprint's ThemeService/`themes.css`** for the toggle: idiomatic, but
  its OKLCH base/primary palette system would fight the bespoke Signal tokens.
  Rejected; a minimal `.dark` toggle keeps Signal authoritative.

## Consequences

- The shell is the one place in the UI that uses raw HTML + a little JS instead of
  `Bb*` components. That is a real inconsistency with ADR 0003; it is confined to
  chrome that has no SSR-safe Bb equivalent, and is the price of a stateless,
  scale-safe app shell. Feature surfaces keep using `Bb*`.
- Menu/drawer/theme accessibility and behaviour are our responsibility (ARIA,
  outside-click, Escape, `prefers-reduced-motion`) rather than inherited from
  Blueprint.
- No circuit is opened just to view a page; the existing `Live*` islands are
  unaffected. Horizontal scaling and rolling deploys stay simple for the whole
  static surface.
- If a future feature needs a shell-level overlay (e.g. a global command palette
  or toast), it must bring its own interactive island + providers rather than
  relying on the shell.
