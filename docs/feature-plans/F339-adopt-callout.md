# Issue #339 Mini-Plan — Adopt discovered containers via a callout

**Status:** one PR (branch `feat/339-adopt-callout`, closes #339). Part of epic #348 (Fleet polish). Builds on #338.

**Written against:** issue #339 (decision 2026-10-04: a callout only when something is discovered; **Adopt** opens the
Deploy sheet on an "Adopt existing" tab); `F338-deploy-server-sheet.md`; `ServerInventory.razor` (the inline Import
form), `DeployServerSheet`, `StepSheet`, `IServerInventory.ImportAsync` / `ListAllDiscoveredUnregisteredAsync`.

## Objective

The inline "Import a discovered container" form leaves `/servers`. When a Host reports a ZWarden-labelled container
with no Server record, a small banner says so ("1 unmanaged server found on nsfw-host") with an **Adopt** button that
opens the Deploy sheet on **Adopt existing**: pick the container (host + run state), name it, Adopt.

## Facts found (2026-10-04)

- `ListAllDiscoveredUnregisteredAsync` walks **every** Agent in the in-memory discovery cache, which isn't tenant-scoped;
  only the registered-id exclusion is tenant-filtered. `ImportAsync` re-checks that the Agent is in the tenant, so a
  foreign container can be listed but not adopted. Listing it is still a leak once hosting is multi-tenant.
- The page renders the banner below the header; the Deploy sheet is an island in the header. Interactive roots on one
  page share one circuit, and so one DI scope.
- `/setup/servers` (first-run) also lists discovered containers through the same call.

## Decisions

- **D1 — tenant-scoped listing.** `ListAllDiscoveredUnregisteredAsync` keeps only Agents in the current tenant (the
  tenant-filtered Agent list), so the banner and the tab never show another tenant's containers. Setup benefits too.
- **D2 — banner island.** `UnmanagedBanner` is a small interactive island rendered (only for `Server.Register`, only
  when something is discovered) under the header with per-host counts from the page: one host → "N unmanaged
  server(s) found on *host*"; several → "N unmanaged servers found on K hosts". Host names follow #336 (short id
  without `Agent.View`). **Adopt** asks the Deploy sheet to open through a circuit-scoped `DeploySheetRequests`
  service (both islands share the circuit), so no page reload. `?adopt=1` deep-links to the same tab (like `?deploy=1`).
- **D3 — the tab.** `StepSheet` gains an optional `Lead` slot above the step strip, and hides the strip when there's
  one step. The Deploy sheet shows `BbTabs` "New server" / "Adopt existing" in that slot **only when something is
  discovered**; Adopt existing is a one-step sheet whose final action is **Adopt**. It lists the containers as a
  `BbRadioGroup` (run state + host; a short server id only to tell apart several on one host), a name field, and
  calls `ImportAsync` through `ActionScopeRunner`. One container → preselected.
- **D4 — success.** As for Deploy: close, toast "*name* is now managed", and refresh the page so the row appears and the
  banner clears (a `?deploy`/`?adopt` deep link is dropped on the way, so a reload doesn't reopen the sheet).
- **D5 — setup page stays.** `/setup/servers` is untouched (its own first-run flow).

## Tests

- Infra: the discovered listing leaves out an Agent of another tenant.
- bUnit: the tab is absent with nothing discovered; banner → sheet opens on Adopt existing; the list shows host + run
  state without raw ids; Adopt needs a choice and a name; Adopt imports (ported from the static Import POST test) and
  toasts; `?adopt=1` opens on the tab.
- Page: banner with per-host counts when something is discovered, none otherwise or without `Server.Register`; the
  inline Import form is gone.
- Browser smoke: banner → Adopt → the row appears and the banner is gone.
