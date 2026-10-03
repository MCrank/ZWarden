# Feature #291 Mini-Plan — One-click Install (mod ids from the description, verified after boot)

**Status:** planned — maintainer took every recommendation (2026-10-03). Slice 0 spike done: a missing id is skipped,
not fatal. Next: PR-A TDD. v1.0, epic [#289](https://github.com/MCrank/ZWarden/issues/289).
Needs #290 (done: #314 + #316). Blocks #292.

**Written against:** issue #291; epic #289 (Variant B; "ids come from the description, `mod.info` corrects them");
[F22](./F22-mod-management.md) (two-step install, which this plan replaces); [F290](./F290-mod-installed-data.md)
(the installed-item cache, `ModChangeSet`, `PzModId`); [F110](./F110-workshop-metadata-enrichment-and-browse.md)
(preview + search); ADR 0011 (value-level revisions, drift fails closed); ADR 0044 (search key optional); ADR 0047
(the cache is never the authority); trust-boundaries §8.

## Objective

Search → **Install** → restart → the mod is live, in **one** restart for the common case. Install writes
`WorkshopItems=` **and** `Mods=` in a single revisioned, audited config apply. The ids come from the item's
description. After the boot, `mod.info` on disk is compared with what was enabled, and a wrong guess is surfaced
as **Pick parts**. It never leaves the server unbootable.

## Facts found (2026-10-03)

- **One apply can carry both keys.** `ConfigApplyPayload` takes a list of `ConfigApplyEdit`, one whole-value edit
  per key. `ModListEditor.RemoveWorkshopItems` already emits a `WorkshopItems` edit and a `Mods` edit together.
  There is no "add item + enable ids" verb yet.
- **`ServerModManager.ApplyListEditAsync`** gates every verb on `IModInventoryCache.GetLatest` (the F21 in-memory
  snapshot). It returns `SnapshotUnavailable` while that's empty. Since #290 the worker refills it after every boot,
  apply or reconnect.
- **The guess is only computed for tracked rows.** `WorkshopDescriptionModIds.Parse` is called only by
  `ModRefreshProcessor`, for items already configured, on disk or booted with. The preview path
  (`IWorkshopMetadataService.ResolveAsync`, keyless, `Mod.View`) returns the description and tags but no parsed ids.
  Search results (`QueryFiles`, key-only) carry no description, tags or children.
- **No dependency data anywhere.** `WorkshopMetadataClient.ParseItem` doesn't read `children`. Only the collection
  expander reads it.
- **Remove leaves guessed ids behind.** `RemoveWorkshopItems` drops from `Mods=` only ids that the item
  *exclusively* provides according to on-disk `mod.info`. For an item installed but not yet downloaded, it would
  leave the ids we just enabled from the guess.
- **There is no per-mod Undo.** `IServerConfigurationEditor.RestoreAsync` restores a whole file to an earlier revision
  and needs `ServerConfigurationEdit`, not `Mod.*`.
- **The "wrong guess" signal already exists.** F21's compat check has `EnabledButMissing` (an enabled id that no
  `mod.info` provides), and the #290 cache holds `GuessedModIds` vs `ObservedModIds` per item.
- **The B42 `Mods=` format is unverified.** Some hosting guides say B42 wants a `\` before each id
  (`Mods=\ModA;\ModB`). `PzModId` rejects `\`, and ZWarden writes ids bare. Also unverified: what B42 does when
  `Mods=` names an id that isn't on disk (issue step 5).
- **The UI today:**
  - `ModBrowserSection.InstallAsync` calls `AddWorkshopItemAsync` and tells the operator to enable mods later.
  - `ModsSection` has the enable select, Remove, reorder, and "Restart to apply".
  - Both are interactive (`ServerDetail` is `InteractiveServer`, #299).

## Slice 0 spike results (2026-10-03, local `zwarden-pzserver`, PZ **42.21.0**, Workshop item KillCount 2553809727)

| Case | `Mods=` | Boot | Log |
|---|---|---|---|
| 1 | `KillCount` (bare), item **not yet downloaded** | started | `Workshop: … DownloadPending` → `installed to /pz/server/steamapps/workshop/content/108600/2553809727` → `Mod: loading KillCount`, all **in the same boot** |
| 2 | `\KillCount` | started | `loading KillCount` |
| 3 | `KillCount;ZwSpikeNoSuchMod` | started | `WARN : Mod … ZomboidFileSystem.loadModAndRequired> required mod "ZwSpikeNoSuchMod" not found`, then `loading KillCount` |
| 4 | `ZwSpikeNoSuchMod` only | started | the same WARN; nothing loaded |

PZ never rewrote `Mods=` / `WorkshopItems=` (the ini after boot equals what was written).

**What this means for the plan:**
- **The one-restart Install is safe.** PZ downloads `WorkshopItems=` during startup, before it loads mods, so the
  guessed ids load in the same boot. A wrong or missing id is skipped with a WARN; it never blocks boot. D2 stands
  as decided, and the issue's step 5 is answered.
- **Bare ids are correct.** ZWarden keeps writing ids bare, and `PzModId` keeps rejecting `\`.
- **New PR-A task: reading `\`-prefixed ids.** An operator or another tool may hand-write `\ModId`. Agent
  discovery (`ModDiscovery.ReadList`) returns `Mods=` entries verbatim, so `\KillCount` would never match
  `mod.info`'s `KillCount`: F21 reports a false `EnabledButMissing`, and the #290 recorder drops the id. Fix:
  - discovery strips one leading `\` from each `Mods=` entry;
  - the next ZWarden write then normalises the list to bare.
- **A future signal (not in #291):** the WARN line ("required mod … not found") could feed Diagnostics or a log
  probe later.

## Maintainer decisions (2026-10-03, all as recommended)

D1, D2, D5 and D6 were asked; D3 and D4 are defaults, not grilled. D1 runs on a **local** PZ container.

| # | Question | Decision |
|---|---|---|
| D1 | **Slice 0 spike on a real B42 server:** (a) `Mods=` names a missing id; (b) bare ids vs `\`-prefixed ids. | Run it first, on the DMZ box or a local PZ container, before writing any guess to `Mods=`. If a missing id **blocks boot**, the plan changes: Install writes `WorkshopItems=` only, and enabling waits for `mod.info` (two restarts). |
| D2 | **Which ids does Install enable?** | **0 ids:** `WorkshopItems=` only, then **Pick parts** after boot. **1 id:** enable it. **More than 1:** the part picker, with every parsed id pre-ticked, and the operator can untick. Ids are validated by `PzModId` on the server side; the operator may also type an id. |
| D3 | **Wrong guess after boot: surface or auto-fix?** | **Surface only.** `ModChangeSet` gains a per-item **PickParts** flag, with the guessed vs observed ids. Nothing rewrites config by itself, because config is the operator's desired state (ADR 0047). |
| D4 | **Undo** (before a restart) | One verb, `UndoPendingAsync(item)`. It sets that item's `WorkshopItems=` entry and its mod ids back to the **booted-with** snapshot, as one apply. It covers both an undone Install and an undone Remove. It needs `Mod.Remove` to undo an install and `Mod.Install` to undo a remove. |
| D5 | **Dependencies (Steam "required items")** | **Key-only**, as the issue allows. With a search key, the planner calls `IPublishedFileService/GetDetails?includechildren=true` and offers the children ticked by default, all in the same apply. Each child gets the same 0/1/many rule. Without a key, nothing is shown. **Or split it into a follow-up** if the PR runs long (see D6). |
| D6 | **PR split and UI scope** | **PR-A:** the service layer (planner, `InstallWorkshopItemAsync`, the Remove fix, Undo, PickParts), with tests. **PR-B:** a minimal UI in the *existing* sections (browser Install → picker dialog; a Pick parts notice and Undo in the Mods list), plus dependencies. #292 then rebuilds the UI as Variant B on these verbs. |

## Design

### Install planner (Application interface, Infrastructure implementation)

`IModInstallPlanner.PlanAsync(user, server, workshopId) → ModInstallPlan`, requiring `Mod.Install`:
- Resolves keyless metadata (reusing `IWorkshopMetadataService`'s client and cache).
- `CandidateModIds = WorkshopDescriptionModIds.Parse(description)`, plus a `Kind` of `NoIds` / `Single` / `Choose`.
- `AlreadyConfigured`, plus the Build tags (shown as data; #292 decides whether to block Build 41-only items).
- `Dependencies` (D5): the child ids, their titles, and each child's own plan.

The plan is advisory. Install re-validates everything it receives.

### Install verb

`IServerModManager.InstallWorkshopItemAsync(user, server, workshopId, IReadOnlyList<string> modIds)`:
- Requires `Mod.Install` and audit action `Installed`.
- Uses the same `ApplyListEditAsync` pipeline: snapshot, drift baseline, `ServerBusy`.
- Calls a new `ModListEditor.InstallWorkshopItem(configuredWs, enabledMods, workshopId, PzModId[])`. It appends the
  item if it is absent and appends the ids not already enabled, emitting at most one edit per key.
  - It returns `NoChange` only when the item is already configured **and** every id is already enabled.
  - A list of dependency ids goes through the same call (`workshopIds[]`), so one apply covers the item and its
    children.
- It also tracks the `ServerWorkshopItem` row with `GuessedModIds` straight away, so the overview shows
  **Installs on restart** before the next discovery.

### Remove fix

`RemoveWorkshopItems` also drops ids the item is known to provide through its **guessed** ids. That covers items that
haven't been downloaded yet. It keeps the existing rule that an id another configured item provides stays.

### Verify after boot (PickParts)

In `ModChangeSet.Derive`, an item that is configured, booted with and on disk is flagged **PickParts** when either
holds:
- **(a)** an enabled id attributed to it (in its guessed ids) is missing from its observed ids;
- **(b)** none of its observed ids are enabled, which is the no-ids path.

The view carries `Guessed`, `Observed` and the missing ids, so the UI can say "the Workshop page listed X, the
download provides Y". Fixing it means `EnableMods` / `DisableMods` with the real ids, as today.

### Undo

`IServerModManager.UndoPendingAsync(user, server, workshopId)` reads `ServerModState` (booted-with) and the item row.
It computes the edits that make this item's presence and its mod ids (guessed ∪ observed) match booted-with, then
sends one apply. It returns `NoChange` when nothing for this item is pending, and `InvalidInput` before a first boot
snapshot exists.

## Tests (TDD, per slice)

- **Slice 0 (spike):** done, see above.
- **Agent `ModDiscovery`:** `Mods=\A;B` → enabled ids `A`, `B`.
- **`ModListEditor.InstallWorkshopItem`:** add with 0/1/many ids; already configured; ids already enabled; append
  order; dependencies in one apply; one edit per key.
- **`ServerModManager.InstallWorkshopItemAsync`:** permission denied; `;` id rejected (`InvalidInput`); snapshot
  unavailable; one `ConfigApply` with both edits and an audit; row tracked with guesses.
- **Planner:** 0/1/many from the fixtures (More Traits → Choose with 4, single-id → Single, no-ids → NoIds); Steam
  down → plan with `MetadataFound=false` (Install still allowed for `WorkshopItems=` only); dependencies only with a
  key (fake HTTP).
- **Remove:** a not-yet-downloaded item's guessed ids are removed; an id shared with another item stays.
- **`ModChangeSet`:** a PickParts truth table (wrong guess; no ids; correct guess; not yet on disk → no flag).
- **Undo:** an undone install; an undone remove restores the ids in their booted order; nothing pending →
  `NoChange`; no snapshot yet → `InvalidInput`.
- **Acceptance (hub integration, stand-in Agent):**
  - Install a single-id item, restart → Active, with no PickParts.
  - Install with a wrong guess, restart, rediscover → PickParts, with guessed vs observed.
- **Web (PR-B):** the bUnit picker dialog shows for >1 ids and Install sends the chosen subset; the PickParts notice;
  Undo.

## Out of scope

The Variant B UI, sheet and pending bar (#292); deleting files (#293); Update ready (#275); SteamCMD pre-download of
no-id items (the epic defers it); blocking Build 41-only items (#292's UI decision; the planner only exposes the tags).
