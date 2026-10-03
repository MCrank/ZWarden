# 47. Installed Workshop items are a rebuildable cache; config stays the desired state

**The control plane persists what it knows about each Server's mods in two tables, `ServerWorkshopItems` (one row per
Server × Workshop item) and `ServerModStates` (one row per Server). They hold Steam details, the mod ids guessed from
the Workshop description, the mod ids observed in `mod.info`, and the configured and booted-with mod lists. They are a
cache and annotation, never the authority. `WorkshopItems=` / `Mods=` in `servertest.ini` stay the only desired
state, and every row can be rebuilt from config + disk + Steam.** This amends the F21/F22 decision "no mod entity, no
persistence".

- Status: accepted
- Decided in: #290 (epic #289; mini-plan `docs/feature-plans/F290-mod-installed-data.md`)
- Bears on: F21 (mod discovery: in-memory inventory only), F22 decision 1 (config-as-truth, no `wsi-`/`mod-` table),
  ADR 0011 (no drift between a table and the thing it mirrors), ADR 0016 (tenant ownership), trust-boundaries §8

## Context

F21 and F22 deliberately kept mods out of the database. The config file was already durable, revisioned desired
state, and discovery's result lived in a process-local cache that only the Refresh button filled.

The 2026-10-01 live pass showed what that costs. The UI could not tell what was **installed** (configured), **loaded**
(as of the last boot), **pending** (changed since boot) or **leftover** (on disk with no config line). Everything
vanished on a web restart, and there was nowhere to keep per-item Steam details. The Mods redesign (#289) needs all
four states, plus a guessed mod id at install time, and needs them across restarts of either process.

## Decision

- **Two tenant-owned tables.**
  - `ServerWorkshopItem` (`wsi-` id): Workshop id, Steam title / preview / size / updated / tags,
    `GuessedModIds`, `ObservedModIds`, `OnDisk`, and refresh timestamps.
  - `ServerModState` (keyed by `ServerId`): the configured lists, the booted-with lists, `BootedAt` and the snapshot
    flag.
  - Lists are primitive collections: JSON on SQLite, `text[]` on PostgreSQL.
- **Config is still written only through F20b config-apply** (via F22's `ModListEditor`). Nothing reads these tables
  to decide what to write into config.
- **No concurrency token.** Each observation rewrites a row wholesale, so last write wins. A lost race is corrected by
  the next discovery.
- **A row exists while its item is configured, on disk, or in the booted-with snapshot.** It is pruned otherwise.
  Server deletion removes both tables' rows (no FKs, as #271 D1).
- **Untrusted text stays data.** Every mod id stored (guessed or observed) has passed `PzModId`. Steam text is
  bounded at parse and again at the column, and escaped at render.

## Alternatives considered

- **Keep everything in memory (status quo).** Rejected: it loses the booted-with snapshot on a web restart, so
  "pending" would be wrong after every deploy, and it re-hits Steam on every render.
- **Make the table the desired state and generate config from it.** Rejected: that reintroduces the ADR 0011 drift
  problem (operators and PZ itself edit `servertest.ini`) and bypasses F20b's revision, drift-check and audit trail.
- **A full Workshop catalogue sync.** Rejected in #289: Steam has no mod-id field, so a copy of the catalogue adds
  nothing. We store details only for items a Server uses.

## Consequences

- Two more tables and a migration per provider. Rows can be briefly stale (until the next discovery). The UI must
  present them as observations, not facts.
- F21's `IModInventoryCache` remains as F22's in-memory snapshot until #292 retires the old Mods panel. For a while
  there are two readers of discovery results.
- If the tables are lost or wrong, the fix is to delete them and let the next boot or discovery rebuild them. That
  property must be kept: nothing may be stored here that config + disk + Steam cannot reproduce.
