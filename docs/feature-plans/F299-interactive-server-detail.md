# Feature #299 Mini-Plan — Make Server Detail + Settings interactive

**Status:** three PRs (A → B → C); C closes #299. PR-A (branch `feat/299a-playwright-tier`): the Playwright tier, 11 tests green. v1.0, epic
[#294](https://github.com/MCrank/ZWarden/issues/294). Blocks Mods #292.

**Written against:** issue #299;
[ADR 0046](../adr/0046-pages-may-opt-into-interactive-rendering-the-shell-stays-static.md) (Q2/Q4/Q5/Q7 amendment/Q9/Q12),
[ADR 0040](../adr/0040-the-app-shell-is-static-ssr-chrome.md) (the shell stays static), ADR 0018 (services re-check
every action), [F298](./F298-split-server-detail.md) (the section split this builds on), F297 (`ActionScopeRunner`,
fail-closed circuit tenant, session revalidation).

## Objective

Server Detail (`/servers/{id}`) and Settings (`/settings`) declare `@rendermode InteractiveServer`. Forms become circuit
handlers; the static-only JavaScript (`dialog.js`, `config-editor.js`, Server Detail's use of `live-status.js`) and
the form-limit plumbing go away on these pages. Operators see the same behaviour without full-page reloads. A
Playwright smoke tier in CI catches circuit-only failures.

## Facts found (2026-10-01)

- **Tests.** The page coverage is all real-host (`ZWardenWebAppFactory`, GET + scrape antiforgery + POST
  `_handler=`). About 40 tests post forms: `ServerDetailPageTests` (66 tests, ~25 posts), `ServerDetailRailTests`
  (3 lifecycle posts), `ServerDetailModBrowserTests` (~6), `SettingsPageTests` (~2). There are no bUnit tests of the
  page, its sections or Settings; bUnit covers only the `Live*` panels. `ParseHiddenInputs` / `SignedIn*Async` are
  copied into ~15 files.
- **Playwright:** none. `ci.yml`'s `e2e-stub` job only echoes a message. `Microsoft.AspNetCore.Mvc.Testing` 10.0.12
  has `WebApplicationFactory.UseKestrel()` + `StartServer()`, so the existing factory can serve a real socket.
- **`FormLimitGuardMiddleware`** applies to any antiforgery-validated form POST (no path list). Its only consumers
  are Server Detail (`[RequestFormLimits(ValueCountLimit = 6144)]`) and Config (`?formRejected=`). Its only test
  is the oversized-post test in `ServerDetailPageTests`.
- **Architecture tests in force:** no `ZWardenDbContext`/`IDbContextFactory` under `Web/Components`; only
  `TenantScopes` opens a DI scope. Nothing checks `@rendermode`.
- **Sections inject Application services directly** (`IServerLifecycle`, `IPlayerManagement`, `IServerBackup`, ...).
  In a circuit those resolve from the tab-lifetime scope, so each call must go through `ActionScopeRunner` (#297
  Q7 amendment). The `Live*` panels inject only singleton caches.
- **Six sections host `@rendermode="InteractiveServer"` islands**; the panels take ids as strings because they crossed
  the static → interactive boundary.
- **`live-status.js`:** `[data-live-status]` is Server Detail only; Fleet uses `[data-live-fleet]` and keeps the
  script. The header's last-failure dismissal is remembered in `localStorage` by operation id.
- **No BlazorBlueprint overlay provider is mounted anywhere.** BB 4.1.0 (`BlazorBlueprint.Components` + `.Primitives`).
- **The config editor shows secret values** (`Password`, `RCONPassword`). That's why the draft stays server-side (D1).
- **.NET 10 circuit persistence:** `[PersistentState]` is persisted when a circuit is disconnected or evicted,
  into `MemoryCache` by default (1,000 circuits / 2 h), or a distributed `HybridCache` if one is configured. A process
  kill loses the in-memory store. The server-triggered pause on shutdown (`RequestCircuitPauseAsync`) arrives in .NET 11.

## Maintainer decisions (2026-10-01, all as recommended)

| # | Decision |
|---|---|
| D1 | **The draft is `[PersistentState]` only.** The unsaved config-editor edits survive a dropped connection and circuit eviction while the web process lives. **Acceptance is amended:** from "kill the web process mid-edit" to "drop the connection mid-edit (and let the circuit be evicted), then reconnect". Surviving a process kill comes with SaaS Redis `HybridCache` / .NET 11 pause. Secrets never go to browser storage. |
| D2 | **The form-POST real-host tests become bUnit tests on the real service graph.** Each section renders in bUnit with `ZWardenWebAppFactory`'s services as the fallback provider, driven by clicks/submits. The existing assertions (operation enqueued, audit row, message shown) carry over. GET-only real-host tests stay, through prerendering. |
| D3 | **Playwright harness:** a new `tests/ZWarden.Web.BrowserTests` (TUnit + Microsoft.Playwright, Chromium) hosts the app through `ZWardenWebAppFactory.UseKestrel()` on a seeded SQLite database with no Agent. It replaces the `e2e-stub` CI job. |
| D4 | **Three PRs.** **A:** the Playwright tier against today's static page (a safety net that must stay green through B). **B:** Server Detail interactive end to end. **C:** Settings + the reconnect overlay + docs. |

**Defaults (not grilled):**
- Prerendering stays on. The page's first-load data (server, permission flags, header status) crosses to the circuit
  with `[PersistentState]`, so it isn't loaded twice.
- Every section's service call goes through `ActionScopeRunner`.
- The header status is polled in the circuit (2 s busy / 5 s idle).
- Failure dismissal stays in `localStorage` behind a small JS-interop module.
- `?section=` / `?file=` stay bookmarkable as in-circuit navigation.

## Design

### PR-A — Playwright smoke tier (static page baseline)

- `tests/ZWarden.Web.BrowserTests`: TUnit, `Microsoft.Playwright`. A shared host fixture holds a
  `ZWardenWebAppFactory` started on Kestrel (random port, SQLite file), seeds the admin and one demo server, and logs
  in once (saved storage state).
- `BrowserSmoke` helper: opens a page, records `console` errors + `pageerror` + failed `_blazor` frames, and fails the
  test if any appeared.
- One test per section: Overview, Players, Console, Logs, Config, Mods, Mod Browser, Backups, Diagnostics, plus
  Settings. Each opens the section and does one representative action (e.g. Start → "enqueued" alert; Config → edit a
  value and see the dirty mark; Diagnostics → Gather). Assertions use the existing `data-*` hooks, so the same tests
  hold after B.
- CI: the `e2e` job replaces `e2e-stub`: build, `playwright.ps1 install --with-deps chromium`, run. Discovery-floor
  bump per the Web.Tests convention.

### PR-B — Server Detail interactive

- `ServerDetail.razor`: `@rendermode InteractiveServer`. Drop `[RequestFormLimits]`, `FormName`s and
  `[SupplyParameterFromForm]`. The lifecycle buttons become `OnClick` handlers. `[PersistentState]` holds the
  first-load view.
- A header **status poller**: a component-owned `PeriodicTimer` that reads `IOperationStore` through `ActionScopeRunner`,
  re-tones the badge/buttons and shows/hides the last failure. It is disposed with the page.
- **Sections:** each handler goes through `ActionScopeRunner`. `EditForm`s keep `Model` + `OnValidSubmit`, with no
  `FormName`. The nested `Live*` panels drop `@rendermode` and take typed ids.
- **Portal host:** `BbPortalHost` (and the dialog provider BB 4.1 needs) is mounted inside `ServerDetail`. The Delete
  confirm becomes `BbAlertDialog`, with the typed-name gate in circuit state; the service still re-checks.
- **Config editor:** circuit state replaces `config-editor.js`: search filter, dirty mark, changed-rows-only apply,
  collapse/expand-all (the choice stays in `localStorage` via interop), and refresh-while-applying as a circuit timer.
  `[PersistentState]` keeps the draft (D1). It fixes #308 (no-rows NRE) along the way.
- `FormLimitGuardMiddleware` stays registered for the remaining static forms. Its Server Detail test is replaced by a
  middleware unit test.
- **Tests:** a `SectionHarness` (bUnit `TestContext` + fallback to the factory's services + an auth/tenant cascade).
  The ~38 Server Detail POST tests move onto it. The rail/GET tests stay real-host, and markup assertions are updated
  where markup changed.
- **Removed:** `dialog.js`, `config-editor.js` (if nothing else loads them), and the `[data-live-status]` branch of
  `live-status.js`.

### PR-C — Settings, reconnect overlay, docs

- `Settings.razor` interactive; the Workshop key save/clear become handlers through `ActionScopeRunner`. The time-zone
  toggle keeps `shell.js` (shell behaviour).
- The reconnect UI: a styled `ReconnectModal` (the .NET 10 template's `components-reconnect-*` states) with Signal tokens,
  in light + dark.
- Docs: `ui-components.md` (the static-page gotchas no longer apply to Server Detail), ADR 0046 note on D1, CONTEXT.md
  if terms change.

## Slices

**A:** (1) the project + host fixture + one Overview smoke test, red→green; (2) the remaining section smoke tests;
(3) the CI job.

**B:** (1) `SectionHarness` + migrate one section's POST tests, red against the static section; (2) the page goes
interactive + the header poller; (3..n) one section per commit: tests migrated → section converted → green; (n+1)
the portal + `BbAlertDialog` delete; (n+2) the config editor in circuit state + the draft; (n+3) delete the dead JS;
Playwright green.

**C:** (1) Settings; (2) the reconnect overlay; (3) docs.

## Acceptance

- Every section works in a real browser (the Playwright tier is green in CI, with no console errors).
- The real-host tests are updated where markup changed; the migrated bUnit tests keep their original assertions.
- Operators see identical behaviour, minus the full-page reloads.
- The config-editor draft survives a dropped connection and reconnect (D1).
- The user live-tests each PR branch on DMZ before merging.
