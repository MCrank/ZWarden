# Feature #275 Mini-Plan — Show when a Workshop mod update is ready

**Status:** implemented — PR-A #327 (Contracts + Agent, floors Agent 685 / Contracts 162); PR-B (control plane + Web, closes #275; floors Domain 396 / Infra 670 / Web 560). Awaiting the DMZ live pass. D1–D10 accepted in grilling (2026-10-03). Slice 0 spike done. v1.0, epic
[#289](https://github.com/MCrank/ZWarden/issues/289). The game half (a new build on the server's branch) was split
out to [#326](https://github.com/MCrank/ZWarden/issues/326) (v1.1).

**Written against:**
- issue #275 (rewritten 2026-10-03)
- [F290](./F290-mod-installed-data.md): `ServerWorkshopItem`, `ModChangeSet`, `ModRefreshProcessor`
- [F292](./F292-mods-variant-b-ui.md): `ModStatusChip`, `PendingChangesBar`, `ModsTable`
- [F293](./F293-delete-workshop-downloads.md): Workshop content is on the install volume; `.acf` keeps stale entries
- F110: `WorkshopMetadataClient`
- #273: a restart pulls Workshop updates
- `SteamAppManifest`: the existing `.acf` reader for `buildid`

## Objective

A Workshop mod updates. Players' clients auto-update, the running server doesn't, and joins fail PZ's Lua checksum
check until someone restarts it. ZWarden shows which items have a newer version on Steam than the copy on the
server's disk, plus a "Restart to apply" path (the #114 graceful restart, which pulls the updates, #273).

## Slice 0 spike results (2026-10-03, local `zwarden-pzserver:f293b`, PZ 42.21, the cached F293 install volume)

| Step | Result |
|---|---|
| Read `/pz/server/steamapps/workshop/appworkshop_108600.acf` | Valve KeyValues. `WorkshopItemsInstalled.<id>` has `size`, `timeupdated` (Unix seconds), `manifest`. `WorkshopItemDetails.<id>` adds `timetouched`, `latest_timeupdated`, `latest_manifest`. Mode 775 owned 10000:10000, so the Agent (group 10000) can read it. |
| Compare with keyless `GetPublishedFileDetails` | `timeupdated` **equals** Steam's `time_updated` exactly for both items (1687374018, 1789036314). |
| Backdated KillCount's `timeupdated` to 1700000000, then booted | PZ logs `Workshop: Installed status but timeUpdated doesn't match!!!` → `DownloadPending` → re-download. **PZ applies the same rule we will.** |
| `.acf` after that boot | `timeupdated` rewritten to Steam's 1789036314. The file tracks what's on disk. |
| 2392709985: an `.acf` entry with **no folder** (left behind by the F293 delete) | Stale `.acf` entries exist: the Agent must report `timeupdated` only for items whose folder it found. That boot re-downloaded it (it was in `WorkshopItems=`). |

**Conclusion:** use the `.acf` (D2). No boot-time (`StartedAt`) fallback is needed.

## Decisions (accepted 2026-10-03)

- **D1 — Scope.** Mod half only. The game half moved to #326 (v1.1), with its design left open. Auto-restart on mod
  updates is out of scope, a v1.1 follow-up.
- **D2 — On-disk source = `appworkshop_108600.acf`, `WorkshopItemsInstalled.<id>.timeupdated`.**
  - Agent discovery reads it (a pure KeyValues text function next to `SteamAppManifest`, unit-tested on text) and
    sets an additive `DiscoveredWorkshopItem.InstalledUpdatedAt` (`DateTimeOffset?`, UTC from Unix seconds).
  - It is set only for items whose folder exists. A missing or unparseable file, a missing entry, or `0` gives `null`.
  - The field is additive-optional, so `ProtocolVersion` stays 1 (ADR 0020).
- **D3 — The rule.**
  - **Update ready** ⇔ the item is in `WorkshopItems=` **and** `SteamUpdatedAt > InstalledUpdatedAt`. Both values come
    from Steam's clock, so there's no skew.
  - Either value `null` ⇒ no chip.
  - Unused files (`Leftover`) never qualify, because boot doesn't pull them.
- **D4 — Steam refresh cadence.**
  - A new `PeriodicTimer` hosted service (`OperationReaperService` pattern) runs **hourly**. It queues `RefreshMetadata`
    with a 1 h max age for every server that has items.
  - Opening the Mods page queues one when the server's newest `MetadataRefreshedAt` is older than **30 min**.
  - Both pass `bypassCache` to `WorkshopMetadataClient`, so the 30-min `IMemoryCache` doesn't hide the data's age.
  - On 403 or 429 the client backs off: it skips calls for the rest of the hour and logs once.
- **D5 — Where it shows.**
  - The Mods row chip.
  - A separate line on the restart bar.
  - A fleet-board hint ("N mod updates").
  - No rail badge.
- **D6 — Updates are separate from pending changes.** `PendingChanges` doesn't count them. The bar shows its own line,
  "N mod updates ready", and uses the same single "Restart to apply" button.
- **D7 — Stopped servers.**
  - The row chip reads **"Updates on start"** when the server isn't Running, and **"Update ready"** when it is.
  - The bar line and the fleet hint appear only while Running.
- **D8 — Chip precedence.**
  - The order is Removed on restart > Installs on restart > Pick parts > **Update ready** > Active, so the update chip
    only ever replaces Active.
  - Implemented as a `bool UpdateReady` on `ModItemView`, not a new `ModChangeStatus`, because `Status` is the
    boot-diff axis.
- **D9 — PR split.**
  - **PR-A:** Contracts and Agent, plus a docs fix.
  - **PR-B:** control plane and Web, stacked on PR-A (like #324/#325).
  - No new ADR, since the Docker and Steam trust surface is unchanged.
- **D10 — Housekeeping in PR-A.** ADR 0028:42 still says the `/pz/data/workshop` symlink points into the runtime
  tmpfs. Since #293 it points into `/pz/server`. Fix the line.

## Slices (TDD)

**PR-A — `feat/275-mod-update-ready` (Contracts + Agent)**
1. Slice 0 spike (done, above).
2. `SteamWorkshopManifest.ParseInstalledTimeUpdated(string)` (a small KeyValues tokenizer, max depth 16) returns `IReadOnlyDictionary<string, DateTimeOffset>`.
   - It reads only the `WorkshopItemsInstalled` block, with numeric ids and positive values.
   - It is bounded (ignores absurd sizes) and returns empty on garbage.
   - Tests use the spike's real text, plus missing-block, `0`, duplicate and malformed cases.
3. `IServerInstallPaths.ReadWorkshopInstalledTimes` (4 MB cap). `ModDiscovery` reads it once per discovery (resilient: an IO error
   gives `null`s) and sets `InstalledUpdatedAt` only on walked folders.
4. Contracts: additive `InstalledUpdatedAt` on `DiscoveredWorkshopItem`, plus a round-trip test. (The Application twin
   `InstalledWorkshopItem` and the hub mapping move to PR-B, where they are consumed.)
5. ADR 0028 line fix. Bump the test floors in the csproj **and** `ci.yml`.

**PR-B — `feat/275-mod-update-ready-web` (control plane + Web)**
1. Application twin `InstalledWorkshopItem.InstalledUpdatedAt` + hub mapping. Domain: `ServerWorkshopItem.InstalledUpdatedAt`, set by `ObserveDisk` and cleared when off disk. EF migration.
2. `ModStateRecorder` passes it through. `ModItemView.UpdateReady` and `ServerModOverview.UpdatesReady` count (D3).
3. `WorkshopMetadataClient`: `bypassCache` plus a 403/429 back-off. `ModRefreshProcessor` takes a max-age override.
4. `ModUpdateCheckService` (hourly `PeriodicTimer`, system scope, per-tenant enqueue). The Mods page queues an
   open-time refresh when the data is older than 30 min.
5. Web:
   - `ModStatusChip` gets the Update ready / Updates on start chip (D7, D8).
   - `PendingChangesBar` gets the updates line (D6).
   - The fleet `FleetRow` and `/status` payload get a mod-update count, with the hint shown while Running.
   - Rebuild Tailwind if there are new classes.
6. Floors (csproj + `ci.yml`), and a screenshot pass via aspire / playwright-cli.

## Acceptance

- A configured item whose Steam `time_updated` is newer than its `.acf` `timeupdated` shows **Update ready** while the
  server runs, and **Updates on start** when it's stopped. After a restart the chip clears, once post-boot discovery
  reports the rewritten `.acf`.
- The restart bar shows "N mod updates ready · Restart to apply", separate from pending changes. The fleet board shows
  "N mod updates" for a Running server.
- An idle, never-restarted server still picks up a new Steam version within about an hour, or on Mods-page open when
  its data is older than 30 min.
- A stale `.acf` entry with no folder, a missing `.acf`, and Unused-files items never show the chip.
- DMZ live pass: backdate one item's `.acf` `timeupdated` (or wait for a real mod update), re-scan, see the chip,
  Restart to apply, and see it clear.

## Out of scope

- The game half: #326 (v1.1).
- Auto-restart when mods update (v1.1 follow-up).
- A manual "Check for updates" button.
- A rail badge.
