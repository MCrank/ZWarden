# #157 — sidebar-09 app shell (MainLayout)

Part of the UI redesign epic #156. Replaces the stopgap raw-HTML top-nav in
`MainLayout.razor` with the **Signal** sidebar-09 shell (grouped nav, workspace
switcher, account menu with **Log out** + theme toggle, top bar). Blocks the
per-page tickets #158–#162.

Design reference (throwaway prototype): <https://claude.ai/artifact/KuVuR9PA7Ezs7sbnFoFqgs>

## The load-bearing decision — the shell is **static SSR** (ADR 0040)

sidebar-09's Bb components (`BbSidebar`, `BbDropdownMenu`, the overlay
providers) **require an interactive render mode** — and a static parent cannot
pass `@Body` (a `RenderFragment`) into an interactive island (Microsoft docs:
render-mode boundary rule). ZWarden is deliberately **static-SSR-first** (auth
cookie flow #63; per-request `HttpContext` on /audit /hosts /servers /setup; the
authz-in-circuit concurrency bug behind #154).

Making the shell interactive would put **one Blazor Server circuit on every
authenticated page** — a stateful, sticky, RAM+WebSocket-per-tab cost that does
not scale for the eventual SaaS (hundreds→thousands of concurrent users), where
a stateless chrome scales flat/horizontally. **Decision (user-approved): hand-roll
the shell in static SSR** — `NavLink` + `AuthorizeView` + a static `/logout`
form + a tiny vanilla-JS/CSS layer for menus, mobile drawer and the `.dark`
toggle. Zero chrome circuits; circuits stay reserved for genuinely-live features
(the ServerDetail `Live*` islands). Recorded as an **ADR 0003 exception** in
ADR 0040 (the Bb interactive shell has no SSR-safe fit).

## Scope (this PR: #157 only)

- `MainLayout.razor` — static shell: fixed left sidebar + top bar + `@Body`.
  - **Sidebar header:** workspace switcher (single "ZWarden / Self-hosted" entry;
    structured so a tenant switcher drops in for v1.1 SaaS).
  - **Nav:** *Operate* → Fleet (`/servers`), Hosts (`/hosts`) · *System* →
    Settings (`/settings`), Audit (`/audit`). `NavLink` active state. Visibility
    only — every page re-authorizes server-side (PRD 12). Gating unchanged:
    `Agent.View` → Hosts, `Audit.View` → Audit; **Fleet always shown** (inventory
    self-filters, ADR 0018); Settings shown to all for now (#160 sets real gating).
  - **Footer:** account menu — Account & 2FA (`/account/manage`), Toggle theme,
    **Log out** (static POST `/logout` form + antiforgery — the missing control).
- **Top bar:** route-derived section label (`ShellNavigation.SectionLabelFor`),
  visual-only ⌘K search pill, `Self-hosted` env pill, theme toggle, mobile
  hamburger.
- `ShellIcon.razor` — static inline-SVG icons (Lucide paths; no icon package).
- `wwwroot/js/shell.js` — document-delegated (survives enhanced-nav DOM merges):
  menu open/close (outside-click + Escape), mobile drawer, `.dark` toggle +
  `localStorage`. No-FOUC head script in `App.razor` applies saved theme pre-paint.
- `Styles/shell.css` (`zw-*` classes, imported by `app.tailwind.css`) — the shell
  chrome CSS, a faithful port of the prototype reading the Signal tokens.
- `Settings.razor` — minimal `/settings` placeholder so the nav link isn't dead
  (#160 fleshes it out).
- **Overlay providers NOT added** to the static shell (they need a circuit and
  would be inert) — features add them inside their own interactive islands, as
  today. A deliberate deviation from the ticket's suggestion, per ADR 0040.

## Tests (TDD)

- `ShellNavigationTests` — `SectionLabelFor` pure function (`/servers`,
  `/servers/{id}` → "Fleet"; `/hosts`,`/settings`,`/audit`,`/account/manage`; unknown).
- `AppShellTests` (integration, `ZWardenWebAppFactory`):
  - Owner sees all four nav links + account email + `Self-hosted` pill + a
    `/logout` form.
  - Role-less user sees Fleet + Settings, **not** Hosts/Audit.
  - `POST /logout` → 302 to the login screen (**Log out works**).
- Bump the Web.Tests discovery floor in **both** the csproj and `ci.yml`
  (web-tests-discovery-floor-bump guard).

## Guards to remember

- `npm run build:css` + commit `wwwroot/app.css` (tier1-stale-app-css).
- Web.Tests `--minimum-expected-tests` floor in csproj **and** ci.yml.
- Verify in the real app per the ui-screenshot-verification flow (aspire/run-web),
  both light and dark, plus phone-width drawer.
