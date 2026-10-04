# Issue #336 Mini-Plan — Fleet shows the Host name instead of the raw AgentId

**Status:** one PR (branch `feat/336-fleet-host-names`, closes #336). Part of epic #348 (Fleet polish); unblocks
#340 (hierarchical grid).

**Written against:** issue #336; `Pages/Servers/ServerInventory.razor`; `Pages/Hosts/HostInventory.razor`;
`IAgentInventory` (gated on `Agent.View`); ADR 0018 (fleet self-filters to visible Servers).

## Objective

Under each server name the Fleet board shows `agt-01a0bf47-…`. Show the Host's name there, in the unreachable banner
and in the discovered-container option, using one naming rule shared with the Hosts page.

## Facts found (2026-10-04)

- `FleetRow.Host` is `server.AgentId.ToString()`; the banner prints the raw id of the one offline Agent; the import
  option reads `srv-… (Running on agt-…)`.
- `_hostNames` is only built for callers with `Server.Register` **and** `Agent.View`, Hostname-first — the Hosts page
  is Hostname-first too, though the issue describes Label-first.
- `Agent.View` is held by every built-in role that can see servers except **Moderator**.
- AgentIds are UUIDv7: two Agents enrolled within about a minute share the leading hex, so a prefix-only short id
  can collide; the prototype shows `agt-01a0a270…d26cc` (prefix + tail).

## Decisions (agreed with the maintainer 2026-10-04)

- **D1 — one rule, Label first.** `HostNames.Display(id, label, hostname)` = the operator-set Label, else the
  Agent-reported Hostname, else `HostNames.Short(id)`. The Hosts page switches to it (Label now beats Hostname; the
  "—" fallback becomes the short id). Both strings are untrusted display data (Razor-escaped).
- **D2 — short id.** `agt-` + the first 8 hex + `…` + the last 5 hex (`agt-01a107c8…d8e99`), matching the prototype
  and staying distinct for Agents enrolled together. The full id stays on the Hosts page and in each option's value.
- **D3 — no new authorization.** Names come from `IAgentInventory.ListHostsAsync` only when the caller holds
  `Agent.View`; otherwise every host shows its short id (a Moderator never sees a hostname). The lookup now runs
  for every fleet viewer, not just those who may register.
- **D4 — where.** Fleet row, unreachable banner (single-host text), discovered-container option, and the register
  picker / capacity list (already used the map; they now get the short-id fallback too).
- **D5 — tests.** `HostNames` unit tests (order, blank handling, short id); page tests: hostname shown on the row,
  label beats hostname, short-id fallback, banner names the host, Moderator sees the short id; Hosts page Label-first.

## Not done here

- Grouping servers under host rows — #340.
