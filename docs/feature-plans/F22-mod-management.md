# Feature 22 Mini-Plan — Mod Management

**Status:** in progress. Branch `feat/f22-mod-management` (closes
[#44](https://github.com/MCrank/ZWarden/issues/44)), one commit per slice across **~2 PRs**
(PR-A the Application mod-management core; PR-B the operator mutation surface). Track D.

**Format:** PRD 60. **Written against:** PRD 34 (mod management — the *mutation* half, the
counterpart to F21's discovery half), PRD 2.2 (TDD mandatory), PRD 2.3 (supportability — a mod
change is an authorized, audited, revisioned Operation, never a silent file poke), PRD 30/38
(config apply and untrusted-data posture); Feature 22 in
[`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (Track D); the
[F21 discovery mini-plan](./F21-workshop-and-mod-discovery.md) whose observed inventory F22
mutates; the [F20b apply/revisions mini-plan](./F20b-configuration-apply-and-revisions.md) and
[ADR 0011](../adr/0011-configuration-revisions-are-value-level-and-fail-closed.md) (value-level,
fail-closed config apply — the write path F22 reuses); [ADR 0025](../adr/0025-steamcmd-update-orchestration.md)
(the F17 update Operation F22 triggers to download Workshop content); the F15 safe restart
(FIFO `save`→`quit`, never SIGTERM); [ADR 0022](../adr/0022-operation-lifecycle-and-per-server-locking.md)
(mutating server-scoped Operations + per-server lock); [ADR 0018](../adr/0018-zwarden-owned-rbac.md)
(server-scoped `Mod.*` authorization); [ADR 0019](../adr/0019-audit-is-append-only-tenant-owned-and-binds-the-auth-sink.md)
(audit); and [`trust-boundaries.md`](../trust-boundaries.md) §3/§8 (observed-never-inferred; Workshop
ids and mod ids stay untrusted, bounded, escaped at render).

## Objective

Give an operator the **mutation** half of mod management — install, remove, enable, disable,
reorder, update, configuration synchronization, and safe restart coordination — as **authorized,
audited, revisioned Operations** built entirely on machinery already shipped. F21 gave the safe
read-only view; F22 lets the operator act on it.

The load-bearing realisation, confirmed against the code before writing this plan: **a PZ server's
mod set _is_ its `WorkshopItems=` / `Mods=` config, and that config is already durable, revisioned
desired-state** (F20b) that F21 already **observes** (the `ModDiscoveryResult` reports
`ConfiguredWorkshopIds` and `EnabledModIds` in file order). So every F22 verb reduces to composing
existing Operations:

| Verb | Mechanism (all reuse — no new low-level plumbing) |
|---|---|
| **enable / disable** | add/remove the mod id in `Mods=` → recompute the value → **F20b config-apply Operation** |
| **reorder** | reorder `Mods=` (load order is significant) → recompute → F20b config-apply |
| **remove** | drop the id from `WorkshopItems=` **and** `Mods=` → recompute both → F20b config-apply |
| **install** (add) | add the id to `WorkshopItems=` → F20b config-apply → **F17 `UpdateServer`** downloads content → F21 discovery reveals the provided mod id(s) → operator enables them (`Mods=`) |
| **update** | **F17 `UpdateServer`** Operation (SteamCMD re-validates all Workshop content, ADR 0025) |
| **config synchronization** | reconcile `WorkshopItems=` ↔ `Mods=` ↔ disk using **F21's four compat findings** |
| **safe restart coordination** | **F15** safe restart (FIFO `save`→`quit` + start) — mods load only on boot |

**Current-list source:** the F21 `IModInventoryCache` (the observed `WorkshopItems=`/`Mods=` in file
order). **Drift guard, free:** F20b's per-file baseline-hash check refuses the apply if the file
changed underneath, so a stale cache **fails closed** instead of clobbering an out-of-band edit.

## The settled decisions

The scope forks were put to the maintainer before writing; all four took the recommended option.

1. **Config-as-desired-state — no new entity, no migration, no `wsi-`/`mod-` table.** `servertest.ini`'s
   `WorkshopItems=`/`Mods=` **is** the desired state; F20b already revisions it and F21 observes it.
   F22 adds a mod-management **service + UI**, nothing persisted of its own. Chosen over the persisted
   `ServerMod` desired-set F21's plan anticipated: a parallel table reintroduces exactly the
   file-vs-table drift [ADR 0011](../adr/0011-configuration-revisions-are-value-level-and-fail-closed.md)
   and F20b fought, for **no v1.0 payoff** (#110's Workshop metadata is ephemeral and deferred). The
   `wsi-`/`mod-` prefixes F1 registered stay unminted until a feature gives file-independent mod state a
   reason to exist (post-1.0). This keeps F22 well under the ~100K guardrail.

2. **Install is two-step and operator-driven — no saga engine.** Enabling a mod needs its **mod id**,
   which exists only after the Workshop item downloads and its `mod.info` is read (F21). So install is
   inherently two phases, surfaced honestly: **(1)** "Add & download" — add the Workshop id to
   `WorkshopItems=` (config-apply), run the F17 update to fetch content, restart; **(2)** once F21
   discovery reveals the provided mod id(s), the operator ticks which to **enable** (`Mods=`
   config-apply), restart. Each phase is one visible Operation. A one-click "install & enable" saga
   (auto-discover → auto-enable-all → restart) is deferred: it needs orchestration state, an
   auto-enable policy (one Workshop item can ship several mods), and mid-saga rollback — and it leans
   on the persisted entity decision 1 declined.

3. **Config writes are immediate and individually revisioned; the disruptive _restart_ is the batched,
   explicitly-confirmed step.** Mods load only on boot and the devs discourage SIGTERM (F15). Rather
   than restart on every toggle, each mod edit applies as its **own** small config-apply Operation
   (cheap, non-disruptive — the running server keeps the old mods in memory until it reboots, and each
   change is its own auditable revision with an independent drift check). The **one** disruptive action
   — the F15 safe restart that actually loads the changes — is gated behind a single explicit
   "Apply & restart" confirmation ("the server will restart"). A **stopped** server needs no restart:
   changes apply on next start. The panel shows a persistent "restart required to apply" banner
   whenever the config has been edited. This is the practical static-SSR realisation of "batch changes,
   one confirmed restart".

4. **Reuse F20b `ApplyAsync`'s Operation machinery — zero new Agent/Contracts surface for mod edits.**
   A mod change is a recompute-the-whole-list-value config edit
   (`new ConfigApplyEdit("Mods", ConfigValueKind.Text, "Mod1;Mod2;Mod3")`,
   `new ConfigApplyEdit("WorkshopItems", …, "12345;67890")` against `servertest.ini`), which the F20b
   Agent writer already applies byte-preservingly, drift-checked, atomically — and records a
   Configuration Revision on completion. F22 therefore ships **no new `ZWarden.Contracts` message, no
   new Agent handler, no new `OperationKind` for edits**; the update path is the existing F17
   `UpdateServer`, the restart path the existing F15. Chosen over a dedicated mod-aware Agent command:
   more protocol surface, and it would bypass the revision/audit/drift trail F20b gives for free.

Decisions taken without escalation (low-risk, pattern-matching existing work):

- **Reuse the existing `Mod.*` permissions (F5) — no new permission.** The catalogue already carries
  `Mod.Install`, `Mod.Remove`, `Mod.Update`, `Mod.View`. Map: **add Workshop id / enable / reorder →
  `Mod.Install`** (they add to or arrange the active set), **remove Workshop id / disable →
  `Mod.Remove`**, **update → `Mod.Update`**. F22 authorizes the right `Mod.*` at its own service
  boundary (fail-closed, ADR 0018, the F21/F19 server-scoped pattern), so a Mod-Manager role acts
  without needing `Server.Configuration.Edit`.
- **Reuse the F20b config-apply Operation enqueue, funnelled through one place.** The mod manager
  authorizes `Mod.*`, computes the edits, captures the drift baseline
  (`IConfigurationRevisionRepository.FindLatest`), and enqueues the same mutating config-apply
  Operation via `IOperationCoordinator` — the shared path `ServerConfigurationEditor.EnqueueAsync`
  already implements. Preferred: **extract that enqueue into a small internal Application seam** both
  the config editor and the mod manager call, so there is one enqueue implementation; fallback if that
  refactor grows risky: a thin sibling enqueue in the mod manager. Either way the Agent write, the
  drift check, and the recorded Configuration Revision are unchanged.
- **A `Mod.*` audit action alongside the config revision.** New `ModAuditActions`
  (`Mod.Installed`/`Mod.Removed`/`Mod.Enabled`/`Mod.Disabled`/`Mod.Reordered`/`Mod.Updated`), mirroring
  `ConfigurationAuditActions`. The Operation still records the underlying Configuration Revision (the
  exact file change), so a mod edit leaves a **dual** trail: an audit entry naming the mod intent and a
  revision naming the file bytes.
- **Mutation requires a fresh Mod Inventory snapshot.** The recompute needs the current
  `WorkshopItems=`/`Mods=`; the manager reads them from the F21 cache and returns a typed
  `SnapshotUnavailable` failure when absent, so the panel prompts "refresh discovery first" rather than
  computing against a guess. (The panel refreshes discovery before offering edits.)
- **Untrusted-input hygiene carries over from F21.** Operator-supplied Workshop ids are validated as
  bounded numeric strings before entering a list; discovered mod ids are compared **ordinally**
  (case-sensitive, spaces preserved — research §5/§6) and HTML-escaped at render.
- **No new ADR.** Every F22 decision is either the conservative default (config-as-truth follows
  ADR 0011's own logic; reuse-F20b/F17/F15 is composition) or a scope choice recorded here. Nothing is
  a hard-to-reverse surprise a future reader needs an ADR to find. *(Candidate worth a sentence in
  CONTEXT.md: "F22 mints no desired-state entity — the config file is the desired state", so a reader
  expecting mod tables finds the reasoning.)*

## Dependencies

**F20b** (#42, issue-declared) — the config-apply Operation, drift check, and Configuration Revision
trail F22's every edit rides. **F21** (#43, issue-declared) — the observed `IModInventoryCache`
(current lists + Workshop→Mod mapping + compat findings) F22 reads and mutates. **F11** (#33,
issue-declared) — the mutating per-server-locked Operation lifecycle (ADR 0022). Transitively **F17**
(the `UpdateServer` download Operation), **F15** (safe restart), **F14** (Server inventory + tenant
resolution), F0–F10. **No new third-party dependency. No external network dependency.** The
player-broadcast-before-restart idea raised during grilling is **[#114](https://github.com/MCrank/ZWarden/issues/114)**
(a cross-cutting graceful-restart feature adopted by all restart paths), deliberately **not** in F22.

## Scope

### PR-A — Application mod-management core

*(No `ZWarden.Contracts`, `ZWarden.Agent`, or `ZWarden.Domain` change; no migration.)*

1. **`ModListEditor` (pure, `ZWarden.Application/Mods`).** Given an observed list (in file order) and an
   intent — `EnableMods(ids)`, `DisableMods(ids)`, `ReorderMods(orderedIds)`, `AddWorkshopItem(id)`,
   `RemoveWorkshopItems(ids)` — return the recomputed semicolon-joined value string(s). Order-preserving,
   ordinal dedupe, no-op detection (returns "unchanged" so the caller can skip a pointless Operation).
   Pure function of (current list, intent): the whole truth table is unit-tested with no I/O.
2. **`IServerModManager` + typed results (`ZWarden.Application/Mods`).** The operator entry point:
   `EnableModsAsync`/`DisableModsAsync`/`ReorderModsAsync`/`AddWorkshopItemAsync`/
   `RemoveWorkshopItemsAsync`/`UpdateModsAsync`. Fail-closed: resolve the Server through the tenant
   filter, authorize the mapped `Mod.*`, read the current lists from `IModInventoryCache`
   (`SnapshotUnavailable` when absent), recompute via `ModListEditor` (`NoChange` short-circuits),
   enqueue the config-apply Operation, audit with the `Mod.*` action. `UpdateModsAsync` authorizes
   `Mod.Update` and enqueues the existing F17 `UpdateServer` Operation.
3. **Enqueue reuse + `ModAuditActions` (`ZWarden.Infrastructure/Mods` + `Configuration`).** Extract the
   F20b `EnqueueAsync` (baseline capture → mutating config-apply Operation → audit) into a shared
   internal seam both editors call; add `ModAuditActions`. `ServerModManager` impl wires
   `IModInventoryCache`, `IConfigurationRevisionRepository`, the shared enqueue, and `IAuditWriter`.
   DI registration in the Infrastructure module.
4. **Tests (`ZWarden.Application.Tests` / `ZWarden.Infrastructure.Tests`).** `ModListEditor` truth table
   (enable/disable/reorder/add/remove, dedupe, order preservation, no-op); `ServerModManager` authz per
   verb (allowed / denied / grant-on-a-different-server); `SnapshotUnavailable`; ownership; the mapped
   audit action recorded; drift-refusal surfaced from the reused enqueue. Bump each affected
   `--minimum-expected-tests` floor to the exact new count.

### PR-B — Operator mutation surface

5. **ServerDetail "Mods" panel — mutation controls.** Extend the F21 panel/island: per-mod
   **enable/disable** toggles and **reorder** (up/down over `Mods=`); per-Workshop-item **remove**; an
   **add-by-Workshop-id** input (validated numeric); an **update** action; and a single
   **"Apply & restart"** action with an explicit "the server will restart" confirmation. Static-SSR
   `EditForm` + page handler (the F19/F21 pattern — no dead JSON endpoint). Controls are individually
   gated by the mapped `Mod.*` policy. `BbNativeSelect` needs an explicit full-path `Name` under SSR
   (#84). Bb* components (ADR 0003).
6. **Restart coordination in the panel.** Each edit posts its own config-apply Operation (immediate,
   non-disruptive); a persistent **"restart required to apply"** banner shows whenever the config has
   been edited; "Apply & restart" additionally enqueues the F15 safe restart. A **stopped** server
   shows "changes apply on next start" and offers no restart.
7. **Untrusted rendering.** Workshop ids and mod ids/names are HTML-escaped at render (F21 posture);
   operator-entered Workshop ids validated before submission.
8. **Tests (`ZWarden.Web.Tests`).** bUnit render of each control and its authz-gated visibility;
   add/enable/disable/reorder/remove/update post handling; "Apply & restart" confirmation path;
   stopped-server and stale/absent-snapshot states; the restart-required banner. Bump
   `--minimum-expected-tests` in **both** the Web.Tests csproj **and** ci.yml's silent-drop guard to the
   exact new count (toolchain memory).

## Non-scope

- **Workshop titles, previews, sizes, update-times, and search** — that is
  **[#110](https://github.com/MCrank/ZWarden/issues/110)** (keyless enrichment + the publisher-key
  search fork), a distinct trust boundary (outbound to Valve) reviewed on its own and **not v1.0**.
  F22 renders numeric Workshop ids and `mod.info`-declared names only; the panel is laid out so a
  name/preview column can slot in later without rework.
- **A player-broadcast / countdown before restart** — **[#114](https://github.com/MCrank/ZWarden/issues/114)**,
  a cross-cutting graceful-restart feature adopted by F15/F17/F22 alike. F22 uses the plain F15 safe
  restart and inherits the broadcast for free when #114 lands.
- **A persisted `wsi-`/`mod-` desired-set entity or migration** (decision 1); **a one-click
  install-&-enable saga** (decision 2); **mod profiles** (post-1.1, PRD/roadmap); **arbitrary RCON
  console passthrough** (F28/F29).
- **Deep compatibility analysis** beyond F21's four local checks (inter-mod `require=` graphs, PZ
  build/version gating) — a later refinement, not needed by criterion 7.

## Domain / documentation changes

No `ZWarden.Domain` entity, no migration, no new typed-ID prefix, no new permission. New code is
`ZWarden.Application/Mods` (`ModListEditor`, `IServerModManager` + results) and
`ZWarden.Infrastructure/Mods` (`ServerModManager`, `ModAuditActions`), plus the extracted shared
config-apply enqueue seam and the ServerDetail panel additions. `CONTEXT.md` gains one sentence — **the
config file is the mod desired state; F22 mints no mod entity** — so a reader expecting mod tables finds
the reasoning (decision 1). This mini-plan is the durable handoff.

## Testing & verification

TDD in slices, each its own commit (test → red → code → green), grouped PR-A then PR-B. Run as CI does
— `DOTNET_ROOT=/c/Users/marco/.dotnet /c/Users/marco/.dotnet/dotnet.exe test -c Release` per affected
test project. No new package ⇒ no lock-file regeneration; still verify `--locked-mode` is clean. New
Application/Infrastructure tests carry their own `--minimum-expected-tests` floors; the Web.Tests count
changes in PR-B ⇒ bump the floor in **both** the csproj and ci.yml's silent-drop guard to the exact
count (toolchain memory / web-tests-discovery-floor-bump). Rebuild `wwwroot/app.css` and commit it if
any new Tailwind class lands in the panel markup (tailwind-app-css-rebuild guard).
