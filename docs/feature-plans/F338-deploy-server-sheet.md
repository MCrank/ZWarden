# Issue #338 Mini-Plan — "Deploy server" sheet with steps

**Status:** one PR (branch `feat/338-deploy-server-sheet`, closes #338). Part of epic #348 (Fleet polish).

**Written against:** issue #338 (decisions 2026-10-04: sheet + steps Basics → Game version → Memory; the verb is
**Deploy**, "registration" stays the domain term); ADR 0046 (interactive rendering, circuit-safe tenant), ADR 0040
(static shell, islands); `ServerInventory.razor` (the #230 inline wizard), `AddModsSheet.razor` (the sheet
language), `ActionScopeRunner`, `InteractivePageHarness`.

## Objective

`/servers` loses the inline "Register a new server" form. A **Deploy server** button in the page header opens a
side sheet that walks Basics → Game version → Memory with Back / Next / Deploy, and `?deploy=1` opens it directly.

## Facts found (2026-10-04)

- Fleet is a static page with one interactive island (`FleetBoard`). ADR 0046 keeps Fleet static; islands are still
  ADR 0040's tool, and since #297 a circuit captures its tenant at start, so an island can do tenant-scoped work
  through `ActionScopeRunner` (every page's circuit runs `TenantCircuitHandler`).
- There's no `BbToastProvider` or `BbPortalHost` on static pages; Server Detail puts its own portal host in the page.
- BlazorBlueprint has no stepper component; `BbSheet` is the shell (as in `AddModsSheet`).
- `IAgentInventory.ListHostsAsync` throws without `Agent.View`. `ServerRegister` without `Agent.View` is possible in
  a custom role. Registration itself checks only that the Agent exists in the tenant.
- `Discovery.KnownAgents()` only knows hosts that sent a snapshot since Web started (the issue's complaint).
- The ten #230/#258 wizard tests post the static form; they're ported to bUnit on the island.

## Decisions

- **D1 — island.** `DeployServerSheet` (`Components/Servers`) renders the header button, the sheet, its own
  `BbPortalHost` and `BbToastProvider`. The Fleet page renders it `InteractiveServer` only when the caller holds
  `Server.Register` (the service re-checks on submit) and passes `OpenOnLoad` from `?deploy=1`. The page itself stays
  static; ADR 0046 is unchanged (an island, not an interactive page). Every service call goes through
  `ActionScopeRunner`; hosts load when the sheet opens, not in the prerender.
- **D2 — reusable shell.** `Components/Ui/StepSheet.razor`: a `BbSheet` with a title, description, a numbered step
  strip, the current step's content, and a footer with Back / Next and a final action (label + busy state). Next
  asks the owner to validate the step (`Func<int, Task<bool>>`). Nothing deploy-specific, so the Enroll host sheet
  (#342) can reuse it.
- **D3 — hosts.** New `IServerInventory.ListDeployHostsAsync(user)`: gated on `Server.Register` (empty when denied),
  every enabled, credentialed Host in the tenant with its live connection state. Label and Hostname are returned
  only to a caller who also holds `Agent.View` (#336 D3); otherwise the picker shows the short id. Connected hosts
  first, then by name; an offline host is listed but disabled ("offline"). The Web adds each host's free memory from
  `IHostCapacityCache` to the option and to the Memory step's capacity list.
- **D4 — per-step validation.** A pure `DeployServerDraft` (`Components/Servers`) holds the typed values and
  validates each step with the existing helpers (`HostPortInput`, `NewServerForm`, `InitialSettingsRules`,
  `ServerBranchView`): Basics needs a connected host and a name; Game version resolves the branch; Memory resolves
  the heap. Next is refused with the step's message shown in the sheet. A failure from the service on Deploy jumps
  back to the step that owns the field (port / settings → Basics, branch → Game version, heap → Memory).
- **D5 — overcommit.** `OverCapacity` keeps the sheet on Memory with the shortfall warning and a `BbCheckbox`
  "Create it anyway"; Deploy re-submits with the acknowledgement. Changing the host or the heap clears the warning.
- **D6 — success.** Close the sheet, toast "*name* is deploying on *host*", and `NavigationManager.Refresh()`: an
  enhanced refresh of the static page, so the grid shows the new row (in its in-progress state) and the island, with
  its toast, survives. Verified in the browser.
- **D7 — import stays.** "Import a discovered container" stays a static section until #339 replaces it with the
  adopt callout.

## Tests

- Infrastructure: `ListDeployHostsAsync` (denied → empty; names withheld without `Agent.View`; disabled/revoked
  hosts left out; connection state).
- bUnit on the real host (`InteractivePageHarness`): open/close, `OpenOnLoad`, Next blocked with a message per step,
  Back keeps values, host labels + offline disabled, and the ported wizard tests (port, invalid port, suggested heap +
  settings with the password encrypted, capacity line, overcommit then acknowledged, warning keeps branch + host,
  hostile setting refused, branch options, pinned branch, custom branch, Build 41 refused).
- Page (HTTP): the inline form is gone, the button renders for an owner, not for a Moderator; `?deploy=1` reaches the
  island.
- Browser smoke: open the sheet from the header, walk the steps, deploy, see the toast and the row, no console
  errors.

## Not done here

- The adopt callout (#339) and the Enroll host sheet (#342, reuses `StepSheet`).
