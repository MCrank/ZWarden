# Feature #230 Mini-Plan — New-server wizard: per-server memory, host RAM capacity guard, basic settings

**Status:** PR-A #268 (branch `feat/230-new-server-wizard`) = Contracts + Domain rules + Agent + the Web hub ingest of the
capacity report (so no Agent ever sends to a missing hub method); PR-B (branch `feat/230-new-server-wizard-web`, stacked on
PR-A) = persistence + Web wizard,
closes [#230](https://github.com/MCrank/ZWarden/issues/230). v1.0. Branch/version selection (#258) is a
follow-up sub-issue that adds one field to this wizard.

**Written against:** issue #230; PRD 2.2 (TDD), PRD 21 (one mutating Operation per Server), PRD 25/27 (closed
create template, restricted socket proxy); [ADR 0015](../adr/) (secret protector),
[ADR 0020](../adr/0020-agent-protocol-versioning-and-catalogue.md) (additive contracts, no version bump),
[ADR 0036](../adr/) (static-SSR wizard, not `BbFormWizard`), F229 mini-plan (Recreate primitive),
#198 (heap + overhead = container limit).

## Objective

1. **Choose memory per server** at creation (heap; limit = heap + the Agent's overhead), suggested from the
   expected player count, operator-overridable.
2. **Capacity guard:** the form shows how much host RAM is free for new servers and warns when the new limit
   would exceed it.
3. **Basic settings at creation:** public listing, public name, max players, optional password, welcome
   message, written before PZ's first boot.
4. **Change memory later** through the existing Recreate (#229) — and Recreate must stop silently resetting it.

## Maintainer decisions (2026-09-25, all as recommended)

| # | Decision |
|---|---|
| D1 | **Capacity guard = warn + acknowledge.** Over budget (total − committed − reserve) shows the shortfall and requires an explicit "create anyway" checkbox; the server side enforces the acknowledgement. Docker limits are ceilings, not reservations, so deliberate overcommit is allowed. |
| D2 | **Heap suggestion = 4 GiB + 0.25 GiB per expected player, rounded up to 0.5 GiB** (8 → 6 GiB, 16 → 8 GiB, 32 → 12 GiB). Documented as a starting point; PZ has no fixed per-player number (mods, zombie density, loaded chunks dominate). |
| D3 | **Initial settings are seeded into `servertest.ini` before first boot**, carried in `CreateServer`, written by the Agent the same surgical way F18 seeds RCON (`RconServerConfig.EnsureEnabled`). One Operation, no restart; PZ fills the remaining keys on first boot. |
| D4 | **One static-SSR page with sections** (Basics / Resources / Capacity), one submit, no draft state. Replaces the two-field "Register a new server" section. |

## Settled design (defaults taken without a fork)

- **Found bug, fixed here: Recreate resets memory.** `ServerProvisioner.SpecFor` always uses `AgentOptions`
  heap/limit. So desired heap is persisted as **`Server.HeapSizeBytes`** (null = Agent default) and carried on
  both `CreateServer` and `RecreateServer`. When a Recreate carries no heap, the Agent **preserves the existing
  container's heap** (read from its `ZW_PZ_XMX` env on inspect) rather than falling back to the default.
- **Heap is the only memory input.** Limit = heap + `AgentOptions.MemoryOverheadBytes` (Agent-side, so the
  Agent's overhead policy stays authoritative). Agent validates heap bounds (≥ 1 GiB, whole MiB; the factory
  already enforces limit > heap).
- **Host capacity comes from Docker, not `/proc/meminfo`.** `GET /info` (already allowlisted) returns the
  daemon host's `MemTotal`, which is correct for a native Linux host and a Docker Desktop VM alike, and sidesteps
  cgroup confusion (the Agent's own cgroup doesn't bound its sibling PZ containers). **Committed** = sum of
  `HostConfig.Memory` across **all** containers this Agent owns, stopped included (they'll start again).
- **Transport:** a new additive `HostCapacityReport(TotalMemoryBytes, CommittedMemoryBytes,
  MemoryOverheadBytes, DefaultHeapSizeBytes, ReserveMemoryBytes)` sent on the metrics cadence
  (`MetricsReportInterval`). Web keeps the latest per Agent in memory (like live status) — not persisted. No
  report yet (older Agent / just connected) → the form says capacity is unknown and doesn't block.
- **Reserve** = new `AgentOptions.HostMemoryReserveBytes`, default 2 GiB (host-bound, validated ≥ 0).
- **Password stays out of the stored payload in plaintext:** the Operation's `CommandPayload` holds it encrypted
  with the F3 `ISecretProtector`; the dispatcher decrypts only to build the hub message. Never logged or audited.
- **Settings validation reuses `PzSchema`** (MaxPlayers 1–254, `Public` bool, text keys) plus the INI
  value rules (no newlines/control chars — an INI line injection guard). All fields optional; blank = PZ default.
- **Changing memory later:** Server Detail's "Change host ports" recreate form becomes "Container settings"
  with game port + heap; same `Server.Recreate` permission.
- **Host picker shows hostnames** (from `HostDescriptor`) and each host's free RAM.

## Slices (TDD, one commit each)

**PR-A — Contracts + Domain rules + Agent + hub ingest**
1. Contracts: `CreateServer` += `HeapSizeBytes?`, `InitialSettings?` (`Public?`, `PublicName?`,
   `MaxPlayers?`, `Password?`, `WelcomeMessage?`); `RecreateServer` += `HeapSizeBytes?`; new
   `HostCapacityReport` message. Serialization + catalogue tests.
2. Agent options: `HostMemoryReserveBytes` + validator.
3. Provisioner: spec from requested heap (limit = heap + overhead); Recreate preserves inspected heap when none
   requested. Inspect surfaces env heap + memory limit.
4. INI seeding of initial settings before create (idempotent, surgical, never touches managed keys).
5. Capacity: engine `/info` MemTotal + committed sum; periodic `HostCapacityReport`.
6. Web hub ingest: `AgentHub.HostCapacity` → in-memory `IHostCapacityCache` (+ free/shortfall arithmetic).

**PR-B — persistence + Web wizard**
7. `Server.HeapSizeBytes` + migrations (Sqlite + Postgres).
8. Payload carries heap + protected settings; dispatcher maps to `CreateServer`/`RecreateServer`.
9. Wizard view-model: heap suggestion (`ServerMemoryRules.SuggestHeap`) + capacity verdict from the cache.
10. Wizard section on `/servers` (+ `POST /servers` JSON twin) with acknowledgement enforcement.
11. Server Detail "Container settings" (port + heap).
12. Docs: operator guide section on sizing memory.

## Out of scope

- Branch/version selection (#258). Live host CPU/RAM telemetry on Host cards (#170). Editing basic settings
  after creation (existing config editor covers it).
