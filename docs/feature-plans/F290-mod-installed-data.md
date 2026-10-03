# Feature #290 Mini-Plan — Installed-item data, automatic discovery, pending-change tracking

**Status:** planned. Two PRs (A, B). v1.0, epic [#289](https://github.com/MCrank/ZWarden/issues/289). Blocks #291,
#292 and #275.

**Written against:** issue #290; epic #289 (Variant B, "mod ids come from the Workshop description, then `mod.info`
corrects them", "no full catalogue sync"); [F21](./F21-workshop-and-mod-discovery.md) and
[F22](./F22-mod-management.md), whose "no mod entity, no auto-discover" decisions this plan reverses (ADR 0047);
trust-boundaries §8 (untrusted data never becomes trusted); ADR 0016 (tenant filter + ownership interceptor);
ADR 0018 (services re-check every action).

## Objective

The control plane knows, per server and without anyone clicking Refresh:
- what is **configured** (`WorkshopItems=` / `Mods=`, the desired state);
- what the server **booted with**;
- what is **pending** (config changed since boot, in either direction);
- what is **leftover** (files on disk with no config line and not loaded).

Each Workshop item also carries Steam details (title, preview, size, updated, tags) and two sets of mod ids: the
**guess** parsed from its description and the **truth** read from `mod.info` on disk. Config stays the authority.
The new tables are a cache that config + disk + Steam can rebuild.

## Facts found (2026-10-02)

- **Discovery is already an Operation** (`OperationKind.ModDiscovery`, non-mutating, no lock, no audit; `actor` is
  optional, so a system enqueue works). `IModDiscoveryService` enqueues it after `Permissions.ModView`. The Agent
  (`ModDiscovery.cs`) returns `ModDiscoveryResult(InstalledItems, ConfiguredWorkshopIds, EnabledModIds, Findings)`:
  the Workshop folder on disk plus the two `servertest.ini` lists, but no paths, sizes or timestamps.
- **Ingest:** `AgentHub.OperationCompleted` calls `IModInventoryCache.Record` (a process-local
  `ConcurrentDictionary`, last write wins, ownership-guarded by AgentId). It is lost on a web restart. F22's
  `ServerModManager` returns `SnapshotUnavailable` while it's empty.
- **Only the Refresh button triggers discovery** (`ModsSection.razor:30`).
- **Nothing reacts to completions.** There is no event/handler seam. `AgentHub.OperationCompleted` branches inline
  on the result payload type, and Start/Stop/Restart carry no payload. `OperationStore.CompleteSucceededAsync` has
  the `Operation` (Kind, ServerId) but returns nothing.
- **Kinds that boot or reconfigure PZ:** `StartServer`, `RestartServer` (incl. #114 graceful), `UpdateServer`
  (#273: the Agent restarts internally), `RecreateServer`, `ConfigApply`, `ConfigApplyRaw`.
- **Timing:** Start/Restart complete when Docker returns, before PZ has downloaded new Workshop items. The
  `servertest.ini` lists are already what PZ read at launch, but the disk lags.
- **`WorkshopMetadataClient`** already batches `GetPublishedFileDetails` in one keyless POST. Two gaps: it parses
  no `tags`, and it **silently drops ids past 100** (`MaxIdsPerRequest`) instead of splitting them. It never
  throws (failure → `NotFound`); 30 min / 5 min `IMemoryCache`.
- **No mod-id character validation.** `ServerModManager.ValidateModIds` checks only non-blank and ≤256 chars, and
  `ModInfoReader` only bounds lengths. A mod id containing `;` (from `mod.info` today, or a description guess
  tomorrow) would corrupt `Mods=` when `ModListEditor` joins on `;`.
- **Typed ids `WorkshopItemId` (`wsi-`) and `ModId` (`mod-`) already exist, unused**, reserved for this.
- **Background work:** only `OperationReaperService` / `AgentConnectionSweeperService` (PeriodicTimer). There are no
  `Channel<T>` workers. Outside a request, scopes come from `TenantScopes.CreateTenantScope(tenant)`; the
  architecture test `Only_tenant_scopes_open_a_di_scope` forbids any other `CreateScope`.
- **No architecture test covers untrusted Steam text** today.
- **Fixture items** (the friends' server, from the `prototype/mods-ux` stub data): More Traits 1299328280 (4 ids),
  NeatUI 3508537032, Modern Status 3451167732, KillCount 2553809727, Better Generator Info 3576056135, Share Map
  Notes 3676995511, OSRS XP Bar 3776534799, UCWF 3682045254, **Equipment UI 3682936016 (the ambiguous one)**,
  Common Sense 3750253491.

## Maintainer decisions (2026-10-02, all as recommended)

| # | Decision |
|---|---|
| D1 | **Background queue.** The hub drops a small "server X: reason" note on an in-process `Channel`. A hosted `ModRefreshWorker` runs discovery and the Steam refresh in a tenant scope. The hub stays fast and Steam latency never sits inside an Agent call. A note in flight is lost on a web restart; reconnect discovery covers it. |
| D2 | **Discover now + again later.** At boot completion: discover, which records the **booted-with** lists from `servertest.ini`. Then one follow-up discovery after a delay (`ModRefresh:PostBootRediscoverDelay`, default 3 min) picks up the `mod.info` ids of new downloads. A download that outlasts the delay is caught by the next trigger. |
| D3 | **Mod-id rule: block separators.** A valid PZ mod id is 1–128 printable characters, not starting or ending with whitespace, and containing none of `; , = \ / " ` + control characters. Spaces, apostrophes, dots and dashes are allowed. One `PzModId` value type enforces it everywhere ids enter config: the description guess, `mod.info` ids, and the operator's hand-typed ids. |
| D4 | **Two PRs.** **A:** parser, `PzModId`, tables + migrations + ADR 0047, metadata client fixes. **B:** worker, triggers, reconnect discovery, booted-with snapshot, pending derivation. |

**Defaults (not grilled):**
- **ADR 0047:** "Installed Workshop items are persisted as a rebuildable cache; config stays the desired state". It
  amends the F21/F22 "no mod entity" decision.
- **Discovery also runs when an Agent (re)connects,** once per server it owns, so the data comes back after a web
  restart without a boot.
- **The UI doesn't change.** The Refresh button and F22's panel stay until #292 rewrites them. `IModInventoryCache`
  stays as F22's snapshot; the worker keeps it fed.
- **The description text is not stored.** Only the parsed ids are kept, and they're re-parsed on every refresh, so
  a parser fix applies on the next refresh.

## Design

### Data (PR-A)

**`ServerWorkshopItem`** (`ITenantOwned`, `WorkshopItemId` key, unique on (TenantId, ServerId, WorkshopId)):
- `ServerId`, `WorkshopId` (digits, ≤20)
- Steam: `Title?`, `PreviewUrl?`, `SizeBytes?`, `SteamUpdatedAt?`, `Tags` (bounded list), `MetadataRefreshedAt?`,
  `MetadataFound`
- `GuessedModIds` (validated `PzModId`s from the description) and `ObservedModIds` (from `mod.info`; ids failing
  `PzModId` are dropped and surfaced as a finding, never stored raw)
- `OnDisk` (seen in the last discovery) and `ObservedAt`

**`ServerModState`** (one per server, keyed by `ServerId`, `ITenantOwned`):
- **Configured:** the `WorkshopItems=` / `Mods=` lists as of the last discovery, plus `ConfigObservedAt`.
- **Booted with:** the same two lists as of the last boot, plus `BootedAt` and `BootSnapshotPending`.

Lists are stored as JSON text columns, in config order.

**Row lifecycle:** a `ServerWorkshopItem` exists while its id is configured, on disk, or in the booted-with list.
When none of those holds, the row is deleted. Tag/title/URL lengths reuse `WorkshopMetadataClient`'s bounds. One
migration per provider (`AddServerWorkshopItems`).

### Description id parser (PR-A)

`WorkshopDescriptionModIds.Parse(string? description) → IReadOnlyList<PzModId>` (pure, Application):
1. Strip BBCode tags (`[b]`, `[h1]`, `[url=…]`, `[list]`, `[*]`, ...).
2. Take `Mod ID:` / `ModID:` lines (case-insensitive), splitting a line on `,` / `;` where an author listed several.
3. For the B42 form `1299328280/ToadTraits`, keep the part after the last `/`.
4. Trim, drop blanks and anything `PzModId` rejects, de-duplicate ordinally, keep first-seen order.

**Fixtures:** the real descriptions of the ten items above, captured once from the keyless Steam endpoint into
`tests/.../Fixtures/workshop-descriptions/`, plus a synthetic no-ids description. Expected: More Traits → its 4 ids
(matching disk); single-id items → one; no-ids → empty. For **Equipment UI**, the test pins today's behaviour (all
listed candidates, B41 + B42 + patch). #291 decides how to pick, and `mod.info` corrects it after boot.

### `PzModId` (PR-A)

A value type in Application with `TryCreate(string?, out PzModId)`, following rule D3.

`ModListEditor`'s mod-id verbs take `PzModId` instead of `string`. `ServerModManager.ValidateModIds` and the ingest
of `mod.info` ids go through `TryCreate`. An operator-typed id with a `;` now gets the existing `InvalidModId`
failure rather than a corrupted list.

### Metadata client (PR-A)

- Parse `tags[].tag` (bounded count and length) into `WorkshopItemMetadata.Tags`.
- Split more than 100 ids into sequential batches instead of dropping the extras.
- Existing tests extended: tags, 150 ids → two requests, all returned.

### Worker and triggers (PR-B)

- **`IModRefreshQueue`** (Application): `Enqueue(ModRefreshRequest(TenantId, ServerId, Reason))`, where the reason
  is `Booted`, `ConfigApplied`, `AgentConnected` or `FollowUp`. It is a bounded `Channel` (drop-oldest; duplicates
  for one server coalesce).
- **Hooks:**
  - `OperationStore.CompleteSucceededAsync` gets the completed `Operation` back. `AgentHub` maps its kind:
    Start/Restart/Update/Recreate → `Booted`; ConfigApply/ConfigApplyRaw → `ConfigApplied`.
  - `AgentHub.OnConnectedAsync` → `AgentConnected` for each server the Agent owns.
- **`ModRefreshWorker`** (hosted) for each request:
  1. Opens `TenantScopes.CreateTenantScope(tenant)`.
  2. Enqueues a system `ModDiscovery` operation for the server.
  3. For `Booted`, marks `BootSnapshotPending` with `BootedAt` = completion time, then schedules one `FollowUp`
     after the delay.
- **Ingest** happens where `AgentHub` already records discovery results, through a new `IModStateRecorder`
  (ownership-guarded like `BackupRecorder`):
  1. Update `ServerModState.Configured`. If `BootSnapshotPending` and the result is newer than `BootedAt`, copy the
     configured lists into the booted-with lists and clear the flag.
  2. Upsert/prune `ServerWorkshopItem` rows (`OnDisk`, `ObservedModIds`).
  3. Ask the worker for a metadata refresh of the server's items whose metadata is missing or older than 6 h. That
     is one batched call, then re-parse the guesses.
- **Failures** (Agent offline, server busy, Steam down) log and drop. The next trigger retries. Nothing throws into
  the hub.

### Pending derivation (PR-B)

`ModChangeSet.Derive(ServerModState, items) → per-item and per-mod status` (pure, Application):
- `Active`: configured and booted with.
- `InstallsOnRestart`: configured, not booted with.
- `RemovedOnRestart`: booted with, not configured.
- `Leftover`: on disk, neither configured nor booted with.

The same applies per mod id against `Mods=`. Before a first boot snapshot exists, everything configured counts as
`Active`, so a fresh install of #290 doesn't light up every row. A read service
`IServerModOverview.GetAsync(user, serverId)` (`Permissions.ModView`) returns it for #292.

## Tests

**PR-A**
- Parser fixtures (as listed).
- `PzModId` rule table: `;`, `,`, `=`, `\`, `/`, quotes, control chars, leading or trailing space, 129 chars, an
  apostrophe and a space (allowed).
- `ModListEditor` / `ServerModManager`: a `;` id is rejected.
- Metadata client: tags, batch split.
- Persistence round-trip on SQLite + Postgres (tenant filter, interceptor stamping, unique key).
- **Architecture:**
  - `ModListEditor`'s public mod-id parameters are `PzModId`, not `string` (the guess can't reach config
    unvalidated).
  - No `MarkupString` in `Web/Components/Pages/Servers` files that reference Workshop metadata or
    `ServerWorkshopItem` (Steam text stays data).

**PR-B**
- Worker: each trigger kind enqueues discovery; `Booted` schedules a follow-up (fake `TimeProvider`); coalescing;
  failures swallowed.
- Recorder: the booted-with snapshot is taken only from a result newer than `BootedAt`; prune rules; an ownership
  mismatch is a no-op.
- `ModChangeSet` truth table, including "no snapshot yet".
- **Hub integration** (real host): a Restart completes → discovery op enqueued; the discovery result lands → state
  persisted.
- **The issue's acceptance:** booted with A+B, remove B, restart → B is off the configured/booted set, and its files
  show `Leftover` with no manual Refresh.

## Out of scope

The UI (#292), Install (#291), deleting files (#293), the "Update ready" cadence (#275). Each one consumes this data.

## Result

**PR-A** (`feat/290-mod-installed-data`):
- **`PzModId`** lives in **Domain** (`ZWarden.Domain.Mods`), not Application, so the entities can take it.
  `ModListEditor`'s Enable/Disable/Reorder intents and `ServerModManager` validate through it. An operator-typed id
  with `;` is now `InvalidInput`.
- **`WorkshopDescriptionModIds.Parse`** handles all ten real fixtures. Equipment UI yields
  `EQUIPMENT_UI_B42|EQUIPMENT_UI|equipmentuipatch`.
- **Found while writing the fixtures:** the metadata client cut descriptions off at 4,000 characters, and three of the
  ten items list their `Mod ID:` lines after that (More Traits at ~5,360). The cap is now Steam's own 8,000. Tags are
  parsed, and more than 100 ids go out in sequential batches.
- **`ServerWorkshopItem` / `ServerModState`:** EF primitive collections map to JSON on SQLite and `text[]` on
  Postgres. Migration `AddServerWorkshopItems` for both providers. A metadata lookup that finds nothing is not
  applied, so a Steam outage never wipes known details.
- **`ServerRemoval`** also deletes both tables' rows for a deleted Server. There are no FKs, as #271 D1.
- **Docs:** ADR 0047; CONTEXT.md gains *Installed item / Booted with / Pending / Leftover*.
- **Architecture:** `UntrustedModTextGuardTests` covers `ModListEditor` and `ServerWorkshopItem` taking mod ids as
  `PzModId`, and `MarkupString` only in the authenticator QR seam.
- **Floors:** Arch 80, Domain 389, Infrastructure 538. Web.Tests unchanged (536).
