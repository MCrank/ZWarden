# Feature 21 Mini-Plan — Workshop and Mod Discovery

**Status:** in progress. Branch `feat/f21-workshop-mod-discovery` (closes
[#43](https://github.com/MCrank/ZWarden/issues/43)), one commit per slice across **~2 PRs**
(PR-A contracts + Agent discovery core; PR-B the operator surface). Track D. **PR-A** ([#111](https://github.com/MCrank/ZWarden/pull/111), merged)
(contracts · mod.info reader + workshop path · discovery engine + compat analyzer · Agent dispatch).
**PR-B** ([#112](https://github.com/MCrank/ZWarden/pull/112), open) — operator surface: ModDiscovery
kind + dispatcher map · observed inventory model + ownership-guarded cache · Mod.View enqueue service ·
AgentHub cache-record · ServerDetail Mods card + live inventory island. Closes #43.

**One deviation from the scope below, recorded:** the "Refresh" trigger is a **static-SSR EditForm +
page handler** calling the discovery service — the same mechanism the F19 Players card uses — rather
than a separate JSON `ModEndpoints`. The page form is the surface the UI actually exercises; a parallel
JSON endpoint with no consumer would be dead surface, so it was not added.

**Format:** PRD 60. **Written against:** PRD 34 (mod management — the *discovery* half), PRD 2.2 (TDD
mandatory), PRD 2.3 (supportability — a missing or broken mod is an operator-facing finding, not a
stack trace), PRD 38 (untrusted-data posture — `mod.info` and config lists are attacker-influenced);
Feature 21 in [`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (Track D) and §7 (why F20
splits — F21 needs config *read + model* only, not apply/revisions); the F17 SteamCMD lifecycle and
F20a config read/model it depends on; the observed-delivery pattern of
[ADR 0023](../adr/0023-server-health-model-and-observed-delivery.md) and the F19 player roster it
structurally mirrors; the on-disk Workshop facts in
[`docs/research/project-zomboid-runtime.md`](../research/project-zomboid-runtime.md) §4–§5;
[`trust-boundaries.md`](../trust-boundaries.md) §3 (observed, never inferred) and §8 (untrusted data
never becomes trusted); and [ADR 0018](../adr/0018-zwarden-owned-rbac.md) (ZWarden-owned RBAC,
server-scoped resource authorization).

## Objective

Give ZWarden a **safe, read-only view of the mods a Project Zomboid server actually has** — computed
entirely from **files already on the host**, with **no external network egress** and **no Steam
credentials**. F21 delivers, end to end:

1. **Metadata discovery** — walk the Workshop content subtree the server/SteamCMD populated
   (`<install>/steamapps/workshop/content/108600/<workshopId>/mods/<modFolder>/mod.info`) and parse
   each `mod.info` into its declared Mod id(s) and name.
2. **Workshop → Mod mapping** — the one-to-many relation the CONTEXT.md warns must never be conflated:
   one Workshop item may provide several PZ mods.
3. **Installed-state detection** — reconcile what is *on disk* against what the server config
   *references* (`WorkshopItems=`) and *enables* (`Mods=`), read through F20a's `IPzConfigDocument`
   over `servertest.ini`.
4. **Compatibility diagnostics** (all four, all derivable locally): referenced-but-not-installed,
   enabled-but-missing, installed-but-inactive, and duplicate/conflicting Mod id.

The result is **observed, ephemeral display state** — reported by the Agent, held in an
ownership-guarded in-memory cache, surfaced on ServerDetail, never persisted. Mutation (install /
remove / enable / reorder) is **F22**; human-friendly names, previews and Workshop search are
**[#110](https://github.com/MCrank/ZWarden/issues/110)** (which depends on this). Both are explicitly
out of scope (§ Non-scope), and both are why F21 stays local-disk-only.

## The settled decisions

The scope forks were put to the maintainer before writing.

1. **Discovered state is observed and ephemeral — no persistence, no entity, no migration.** The
   Agent reads disk + config on demand and reports a `ModDiscoveryResult` on `OperationCompleted`;
   ZWarden.Web records the latest-per-Server snapshot in an **ownership-guarded in-memory cache**
   (`IModInventoryCache`), exactly as F19 does for the player roster and F16 for metrics/health
   (ADR 0023 observed delivery). Chosen over persisting `WorkshopItem`/`Mod` rows (the F14
   discovery/reconcile shape): discovery reflects volatile on-disk reality, so persisting it would
   reintroduce the "asserts a state the file never held" drift F20b/ADR 0011 fought, and a persisted
   **desired-state** model earns its place in **F22**, where mutation gives it a reason to exist.
   This keeps F21 inside the read-only scope line and well under the ~100K guardrail.

2. **Native PZ identifiers are carried as bounded, untrusted strings — no typed UUIDs minted for
   observed facts.** A PZ **Mod id** is the `mod.info` `id=` value (a string); a **Workshop id** is
   the Steam numeric id (a string). The typed `ModId` (`mod-`) and `WorkshopItemId` (`wsi-`) UUIDs
   that F1 registered are for ZWarden's own **persisted** aggregates — F22's desired-state entities —
   and are **not** used here. F21 introduces **no new typed-ID prefix and no migration**, mirroring
   F20a's "no new typed ID" reasoning. The native ids and mod names are **untrusted PZ/Workshop
   output** (trust-boundaries §8): length-bounded, carried verbatim, escaped only at render.

3. **All four compatibility checks ship in F21.** Each is a pure function of (a) the on-disk mod set,
   (b) `WorkshopItems=`, and (c) `Mods=`, so all are computable offline with no Steam call:
   - **Referenced-but-not-installed** — a `WorkshopItems=` id has no folder under `content/108600/`.
   - **Enabled-but-missing** — a `Mods=` id is provided by no installed `mod.info` (the server will
     fail to load it).
   - **Installed-but-inactive** — a mod is present on disk but absent from `Mods=` (informational).
   - **Duplicate / conflicting Mod id** — the same Mod id is provided by more than one installed
     Workshop item (a load conflict).

4. **Local disk only — external Workshop metadata/search is [#110](https://github.com/MCrank/ZWarden/issues/110).**
   The keyless `GetPublishedFileDetails` API (research §4) is lookup-by-id, not search; real search
   (`QueryFiles`) needs a publisher key — a *stored secret* that F21's "no credentials" posture
   avoids. Reaching Valve is a distinct trust boundary reviewed on its own issue. F21 therefore emits
   **numeric Workshop ids + `mod.info`-declared names only**; #110 layers titles/previews/search on
   top, and pairs with F22 to deliver "browse and install".

Decisions taken without escalation (low-risk, pattern-matching existing work):

- **Reuse the existing `Mod.View` permission** (F5) and the F14/F19 server-scoped authorization
  pattern (ADR 0018): a server-scoped policy checked at the endpoint with no resource denies outright.
  No new permission.
- **Reuse the `DiscoverMods` command / `ModDiscovery` operation as a non-mutating, per-server
  Operation** carrying no payload — the target Server rides the envelope `ServerId`, the operation is
  its `OperationId` — exactly like `ListPlayers`/`ProbeRconHealth`. `ModDiscoveryResult` is a **new
  additive optional field on `OperationCompleted`** (ADR 0020 additive-compatible; `ProtocolVersion`
  is **not** bumped — every prior additive result field, incl. `Roster` and `Config`, kept
  `Current = 1`). One new `OperationKind.ModDiscovery` (stored by name).
- **A tiny hand-written `mod.info` reader on the Agent**, not Loretta and not F20a's INI reader:
  `mod.info` is a flat UTF-8 `key=value` text file (`name=`, `id=`, `require=`, …), never Lua and not
  one of PZ's four config files. It gets the same **untrusted-input hygiene** F20a's pre-check
  established — a byte-size cap before parsing, bounded field lengths, a malformed line is a skipped
  diagnostic, never a throw. No brace-depth scan is needed (it is not nested).
- **Synthetic fixtures only**, per the F12 rule (no PZ-derived artefact committed) and F20a's stance:
  hand-written `mod.info` files and a synthetic `content/108600/…` tree mirroring the shapes the
  research doc measured. A generated real-tree validation is a **tier-2 / opt-in** follow-up.
- **No new ADR.** Observed/ephemeral delivery is ADR 0023's established pattern; the local-disk-only
  boundary and the #110 split are recorded here and on #110; the native-vs-typed-id choice is a
  conventional realisation of F1/F20a precedent. Nothing here is a hard-to-reverse surprise a future
  reader would need an ADR to find.

## Dependencies

**F17** (issue-declared) — the SteamCMD lifecycle that installs the Workshop subtree F21 reads; F21
adds no code to it, and reuses `IServerInstallPaths`' `<serverId>.server` install-volume convention.
**F20a** (issue-declared) — the `IPzConfigDocument` read seam F21 uses to read `WorkshopItems=` and
`Mods=` from `servertest.ini`. Transitively F0–F2, F7 (contracts), F8/F10/F11 (Agent command
dispatch + Operations engine), F14 (Server inventory), F19 (the roster/cache pattern this mirrors).
**No new third-party dependency. No external network dependency.**

## Scope

### PR-A — Contracts + Agent discovery core

1. **Contracts (`ZWarden.Contracts`).** `DiscoverMods : AgentCommand` (`[ProtocolMessage("mods.discover")]`,
   no payload). New result records: `ModDiscoveryResult` (the discovered Workshop items, the mods each
   provides, the `Mods=`/`WorkshopItems=` config lists as observed, and the compatibility findings);
   `DiscoveredWorkshopItem(string WorkshopId, bool Installed, IReadOnlyList<string> ProvidedModIds)`;
   `DiscoveredMod(string ModId, string? Name, string WorkshopId)`;
   `ModCompatFinding(ModCompatKind Kind, string Subject, string? Detail)` with the four-value
   `ModCompatKind` enum. Add `ModDiscoveryResult? Mods = null` to `OperationCompleted` (additive,
   optional; observed data; ids/names untrusted and bounded). Serialization round-trip tests.
2. **Operation kind + enqueue.** `OperationKind.ModDiscovery` (non-mutating; `IsMutating=false` at
   enqueue, so it never contends the per-server lock — ADR 0022, matching the roster/probe reads).
3. **Agent Workshop path (`IServerInstallPaths`).** Add a `GetWorkshopContentRoot(ServerId)` accessor
   returning `<DataMountRoot>/<serverId>.server/steamapps/workshop/content/108600`, abstracted for
   unit tests.
4. **`mod.info` reader (`ModInfoReader`, Agent, internal).** Size-capped, flat `key=value` parse →
   `{ Id, Name, Requires[] }`; bounded fields; malformed/oversized → skipped with a diagnostic.
5. **Discovery engine (`ModDiscovery` / `IModDiscovery`, Agent).** Walk the Workshop content root →
   for each `<workshopId>/mods/<modFolder>/mod.info` build the Workshop→Mod mapping; read
   `servertest.ini` via `IPzConfigDocument` for `WorkshopItems=`/`Mods=` (semicolon-split, trimmed);
   compute installed-state and the **four** compat findings as a **pure function** of (disk set,
   referenced set, enabled set) so the whole truth table is unit-tested with no filesystem; return a
   `ModDiscoveryResult`. Read-only: no `exec`, no writes, no mutation.
6. **Command dispatch.** `case DiscoverMods:` in `AgentCommandProcessor` → run `ModDiscovery` → report
   `OperationCompleted` with the result (the `ListPlayers` shape).
7. **`ZWarden.Agent.Tests` + `ZWarden.Contracts.Tests`** — `mod.info` reader (well-formed,
   multi-mod-per-item tree, malformed line, oversized, missing `id`), the mapping walk, the pure
   compat function (each finding: present/absent cases, duplicates across two items), the dispatch
   path, and the contract round-trip. All on synthetic fixtures.

### PR-B — Operator surface

8. **Observed model + cache (`ZWarden.Application` / `ZWarden.Infrastructure`).**
   `ModInventory(ServerId, AgentId, …result data…, DateTimeOffset ObservedAt)` and
   `IModInventoryCache` (latest-per-Server, process-local singleton, **ownership-guarded** reads:
   returned only to a caller naming the Server's true owning Agent) — the `PlayerRoster` /
   `IPlayerRosterCache` twin.
9. **Discovery service (`IModDiscoveryService` / impl).** `Mod.View` server-scoped authorization
   (fail-closed, ADR 0018), enqueue the `ModDiscovery` Operation, return the poll handle — the
   `IPlayerManagement.ListPlayersAsync` shape.
10. **Hub record.** `AgentHub` injects `IModInventoryCache` and records `ModDiscoveryResult` from
    `OperationCompleted` against the reporting Agent (the roster-record path).
11. **Endpoints (`ModEndpoints`).** `POST /api/servers/{id}/mods/refresh` → enqueue + poll
    `/api/operations/{id}`; the live UI reads the cache. SameSite=Lax CSRF posture as F19.
12. **ServerDetail "Mods" panel.** A Blazor island under `Mod.View` listing installed Workshop items
    and the mods each provides, installed/enabled state, and the compatibility findings (ids/names
    HTML-escaped at render — untrusted). "Refresh" triggers discovery. BlazorBlueprint `Bb*`
    components (ADR 0003); markup mirrors the Players panel.
13. **`ZWarden.Web.Tests`** — endpoint auth (allowed/denied/unknown-server), ownership-guarded cache
    behaviour, and bUnit render of the panel incl. each finding kind and the empty/never-refreshed
    state. Bump `--minimum-expected-tests` in **both** the Web.Tests csproj **and** ci.yml's
    silent-drop guard to the exact new count (toolchain memory).

## Non-scope

- **Any mutation** — install, remove, enable/disable, reorder, update, config sync, restart
  coordination. That is **F22** (#44).
- **Any external network call** — Workshop titles, preview images, sizes, update-times, and search
  are **#110** (keyless enrichment + the publisher-key search fork). F21 emits numeric Workshop ids
  and `mod.info`-declared names only.
- **Persisted `WorkshopItem`/`Mod` aggregates, the `wsi-`/`mod-` entities, any migration.** Observed
  state is cached, not stored; the desired-state model belongs to F22.
- **Deep compatibility analysis** beyond the four local checks (e.g. inter-mod `require=` graphs, PZ
  build/version gating from `mod.info`) — a possible later refinement, not needed by criterion 7.
- **A committed real-install fixture corpus** — opt-in / tier-2 follow-up, per F20a and the F12 rule.

## Domain / documentation changes

No `ZWarden.Domain` change, no entity, no migration, no new typed-ID prefix or permission. `CONTEXT.md`
gains two observed-side terms if they prove load-bearing during the build — **Mod Inventory** (the
observed, cached, latest-per-Server discovery snapshot behind `IModInventoryCache`) and **Workshop→Mod
mapping** (the one-to-many relation) — kept distinct from the persisted `mod-`/`wsi-` aggregates F22
will introduce.

**No research addendum is needed after all:** `docs/research/project-zomboid-runtime.md` **§6 already
documents** — High confidence, verified by inspecting four real Workshop items — the `mod.info` schema
(`id`/`name`/…), the one-to-many Workshop→Mod mapping, the `<workshopId>/mods/<modFolder>/mod.info`
subtree (and the Build-42 `<modFolder>/42/mod.info` variant), the `WorkshopItems=`/`Mods=` keys, the
semicolon separator, and that Mod ids are case-sensitive and may contain spaces. The implementation
honours all of these: ordinal (case-sensitive) id comparison, whole-value trimming that preserves
internal spaces, and a **B42 `42/mod.info` fallback** when a mod ships no legacy root file. Synthetic
fixtures encode these shapes. This mini-plan is the durable handoff.

## Testing & verification

TDD in slices, each its own commit (test → red → code → green), grouped into PR-A then PR-B as above.
Run as CI does — `DOTNET_ROOT=/c/Users/marco/.dotnet /c/Users/marco/.dotnet/dotnet.exe test -c Release`
per affected test project. No new package ⇒ no lock-file regeneration; still verify `--locked-mode`
is clean. New Contracts/Agent tests carry their own `--minimum-expected-tests` floors; the Web.Tests
count changes in PR-B ⇒ bump the floor in **both** the csproj and ci.yml's silent-drop guard to the
exact count (toolchain memory / web-tests-discovery-floor-bump).
