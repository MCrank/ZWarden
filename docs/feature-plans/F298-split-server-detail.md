# Feature #298 Mini-Plan — Split ServerDetail.razor into per-section components (no behaviour change)

**Status:** one PR (branch `feat/298-split-server-detail`, closes #298). v1.0, epic [#294](https://github.com/MCrank/ZWarden/issues/294).

**Written against:** issue #298;
[ADR 0046](../adr/0046-pages-may-opt-into-interactive-rendering-the-shell-stays-static.md) (decision Q10),
[ADR 0040](../adr/0040-the-app-shell-is-static-ssr-chrome.md) (static-SSR forms), ADR 0018 (fail-closed per-server gates).

## Objective

`Components/Pages/Servers/ServerDetail.razor` is 3,304 lines: one page, 17 static `EditForm`s, nine rail sections.
#299 makes the page interactive and rebuilds sections one at a time, which needs small, reviewable units. This PR
cuts the page into one component per section **without changing any markup or behaviour**.

## Facts found (2026-10-01)

- Every section is an `@if (_activeSection == "...")` block; only the active one renders. Posts go back to the same
  URL (including `?section=`), so the posting form is always rendered when its handler is dispatched.
- Static SSR dispatches a form submit only after the page has rendered to quiescence. A child component's
  `[SupplyParameterFromForm]` and `OnInitializedAsync` therefore run before its handler, just as they do on the page today.
- The posted field names are the model **property** names (`_actionForm.Username`, ...). The real-host tests post them,
  so each section keeps its form properties' names exactly.
- The coupling between sections is small:
  - **Overview → header:** Recreate and Delete set the header lifecycle alert and re-read the header status. The
    Recreate button and the Delete dialog read `_status` (`Busy`, `Label`).
  - **Overview:** reads `_lastUpdate`, which `RefreshStatusAsync` computes.
  - **Mods + Mod Browser:** both use the mod inventory, `ModManagePermissions` and `LoadModManagementAsync`.
  - **Shared helpers:** `CurrentUserAsync` (used by every handler), `FormatSize` (Backups + Mod Browser),
    `CountdownLeads` (the header graceful restart + Overview recreate/delete).
- Today the page loads bans, backups and the mod inventory on **every** request, whatever section is open.

## Maintainer decisions (2026-10-01, all as recommended)

| # | Decision |
|---|---|
| D1 | **Data loads move into the sections.** The parent keeps every permission evaluation: the rail gating, `ResolveSection`'s fail-closed fallback, and the flags passed down. Each section loads its own data in `OnInitializedAsync`. |
| D2 | **Overview → header via `EventCallback`.** `OnLifecycleResult(command, result)`: the parent sets the alert and re-reads status. Header state (`Status`, `LastUpdate`) is passed into Overview as parameters. |
| D3 | **`ServerSectionBase`** (an abstract `ComponentBase`) holds `[Parameter] Server`, the `AuthState` cascade and `CurrentUserAsync()`. The pure helpers go in a static `ServerDetailFormat`. |
| D4 | **Verification:** a scratch golden-HTML capture of every section (GET + representative posts) runs before the refactor and is diffed after each extraction, with tokens and ids normalized. Before/after screenshots go on the PR. Nothing extra is committed. |

## Design

- The sections go in `Components/Pages/Servers/Sections/`: `OverviewSection`, `PlayersSection`, `ConsoleSection`,
  `LogsSection`, `DiagnosticsSection`, `ConfigSection`, `ModsSection`, `ModBrowserSection` and `BackupsSection`. None
  has a `@page`.
- **`ServerDetail` keeps:**
  - the route and the query parameters it owns (`Section`; `FormRejected`/`op`/`file` move to Config);
  - the server load and the permission flags;
  - the header: lifecycle and graceful-restart forms, the lifecycle alert, the last failure;
  - the rail, `ResolveSection`, and section switching.
- **Each section:**
  - takes the resolved `Server` plus its permission flags as parameters;
  - owns its `[SupplyParameterFromForm]` models (same `FormName`s, same property names), handlers, messages and
    nested model types.
- **Mod inventory sharing:** `ModsSection` and `ModBrowserSection` each call one shared loader. That keeps the
  sections independent: the two never render together, and #292 merges them anyway.
- **The `#224` `RequestFormLimits` attribute stays on the page**, since it's endpoint metadata.

## Slices (one commit each)

1. A scratch golden capture against the current page (not committed).
2. `ServerSectionBase` + `ServerDetailFormat`.
3. Extract the sections one by one (Logs, Console, Diagnostics, Players, Backups, Mods, Mod Browser, Config,
   Overview), re-running the real-host tests and the golden diff after each one.
4. Before/after screenshots; the PR.

## Acceptance

- The real-host tests are green with no assertion changes, and the Web.Tests count is unchanged.
- The golden diff is empty.
- `ServerDetail.razor` is under about 500 lines.
