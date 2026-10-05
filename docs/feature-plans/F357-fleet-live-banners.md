# Issue #357 Mini-Plan — the Fleet's unreachable banner, Hosts tile and adopt banner stay current

**Status:** one PR (branch `feat/357-fleet-live-banners`, closes #357). Part of epic #348 (Fleet polish).

**Written against:** issue #357; #253/#257 (`live-status.js` polls `/api/servers/status`); #336 (`HostNames`, D3: no
new authorization); #339 (`UnmanagedBanner` island, tenant-scoped discovery); #340/#342 (Hosts with no Servers count
for an `Agent.View` caller); #342 (`EnrollHostSheet`'s `PeriodicTimer` + `ActionScopeRunner` poll); ADR 0018, 0046.

## Objective

`/servers` keeps the "ZWarden.Agent on *host* is unreachable" banner, the **Hosts** tile (online / total, and the
"N hosts" in the subtitle) and the adopt banner up to date without a reload, so a `web` restart that the Agent
recovers from clears itself, and containers reported on reconnect get the adopt banner.

## Facts found (2026-10-05)

- The page works out `_hostsTotal` / `_offlineHosts` once: the distinct owning Agents of the visible Servers, plus
  every Host for an `Agent.View` caller (#342), "online" per `IAgentConnectionRegistry`. Names: `HostNames.Display`
  with `Agent.View`, else `HostNames.ShortId`.
- `/api/servers/status` returns a JSON **array** of per-Server entries; `live-status.js` and its tests rely on that.
- `IServerInventory.ListAllDiscoveredUnregisteredAsync` takes no user and checks no permission (the page, the Deploy
  sheet and first-run setup gate it). The adopt banner is rendered only when the count is above 0, so it can't appear
  later on its own.
- Browser tests share one host per session (`PerTestSession`); other tests leave Servers on never-connected Agents, so
  the banner usually lists several Agents there.

## Decisions

- **D1 — one projection for the hosts.** `Components/Servers/FleetHosts.cs`: `FleetHosts.Summarize(servers,
  knownHosts, nameOf, isConnected)` → `FleetHostsSummary(Total, Online, Unreachable names)`. The page and the new
  endpoint both use it, so the first render and the poll agree.
- **D2 — a sibling endpoint, not a new shape for the status poll.** `GET /api/fleet/hosts` (authenticated, no-store)
  → `{ total, online, unreachable: ["nsfw-01", …] }`. It derives the Hosts the same way the page does: visible Servers,
  plus every Host for an `Agent.View` caller; names only with `Agent.View`. Keeping `/api/servers/status` an array means
  nothing that reads it changes. `live-status.js` polls it on its own idle loop (5 s, visible tab only), from
  `[data-live-fleet-hosts]` on the KPI strip.
- **D3 — the banner is always in the page.** The unreachable banner is rendered `hidden` when nothing is unreachable,
  with a one-host variant (`[data-degraded-one]` + `[data-degraded-host]`) and a many-hosts variant
  (`[data-degraded-many]` + `[data-degraded-count]`). The wording stays in Razor; the script only toggles the
  variants and writes names and numbers with `textContent`. The many-hosts variant gets a `title` listing the names
  (a hover answers "which ones?"; browser tests rely on it too). The Hosts tile's value (`[data-hosts-online]`,
  `[data-hosts-total]`), its sub line and the subtitle's host count (`[data-fleet-hosts-count]`) update the same way.
- **D4 — the adopt banner refreshes itself.** New `IServerInventory.ListUnmanagedHostsAsync(user)` →
  `UnmanagedHostCount(AgentId, Label, Hostname, Count)` per Host: empty without `Server.Register` (fail-closed), named
  only with `Agent.View`, this tenant only (built on `ListAllDiscoveredUnregisteredAsync`). The page uses it for the
  first render. For a `Server.Register` caller it always renders the `UnmanagedBanner` island, which shows nothing at
  zero. Once interactive, the island re-reads every 5 s through `ActionScopeRunner` (a `PeriodicTimer` on
  `TimeProvider`, as `EnrollHostSheet` does) and re-renders only when something changed. A new `Hosts` parameter value
  (the enhanced refresh after an adopt) wins over its own last read. The poll interval is a parameter so tests can
  shorten it.
- No new authorization: the endpoint uses the inventory's `Server.View` filter and the `Agent.View` check the page
  already makes; the adopt list re-checks `Server.Register`. Host names stay untrusted data (`textContent` / Razor).

## Tests

- Infrastructure (`ServerInventoryTests`): `ListUnmanagedHostsAsync` counts per Host and leaves out registered and
  foreign containers; returns nothing without `Server.Register`; names only with `Agent.View`.
- Web (`FleetHostsTests`): the summary counts the visible fleet + known Hosts, names the unreachable ones.
- Web endpoint: `/api/fleet/hosts` is no-store; it reports total/online/unreachable names; a Moderator sees short ids,
  not hostnames; anonymous is refused; a caller who can't view anything gets 0.
- Page: the banner is in the page but hidden when every Host is connected; the strip names the endpoint; a
  `Server.Register` caller with nothing discovered gets the island but no `data-unmanaged-banner`.
- bUnit (`UnmanagedBanner`): starts empty, shows the banner after a container is discovered (short poll), hides it
  again once adopted.
- Browser (`FleetSmokeTests`): a Server on a Host that isn't connected shows that Host in the banner; connecting the
  Agent clears it from the banner without a reload.
