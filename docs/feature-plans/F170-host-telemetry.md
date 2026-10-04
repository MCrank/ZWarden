# Issue #170 Mini-Plan — Host CPU / memory / free-disk telemetry

**Status:** one PR (branch `feat/170-host-telemetry`, closes #170). Part of epic #349 (Hosts polish); unblocks #340
(the Fleet hierarchy's host rollup rows read the same cache).

**Written against:** issue #170; ADR 0020 (additive protocol changes); trust-boundaries.md §3/§8; `HostCapacityReport`
(#230), `HostCapacityReader`, `ServerMetricsMonitor`, `IHostCapacityCache`, `HostInventory.razor`, `MeterBar`,
`live-status.js`.

## Objective

Each connected Host card shows real CPU %, memory used / total and free / total disk for the PZ data volume, kept
current without a reload, with an age line that turns into a stale warning when reports stop.

## Facts found (2026-10-04)

- The Agent already sends `HostCapacityReport` (memory budget, #230) every metrics tick (15 s), even with no servers.
  The hub stamps it with the connection's AgentId and keeps the latest one per Agent in `IHostCapacityCache`.
- The Agent runs in a Linux container. `/proc/stat` and `/proc/meminfo` are not namespaced, so they show the whole
  host (unless lxcfs is installed). No new mount or privilege is needed.
- The PZ data root (`/srv/zwarden/pz-data`) is a host bind mount. `DriveInfo(path)` calls `statvfs` on Unix, which
  reports the host filesystem that holds it. Note: the existing `ServerDiskUsageReader.CapacityBytes` uses the path
  root (`/`), which is the container overlay, not the bind mount.
- The Docker root (`/var/lib/docker`) is not visible to the Agent, and Docker's `/info` has no free-space figure.
- The Hosts page is static SSR. The Fleet meters update through `live-status.js`, which polls a batched,
  authorized JSON endpoint and moves `MeterBar`s in place (`data-meter*` hooks).

## Decisions

- **D1 — wire: extend `HostCapacityReport`** with trailing nullable members (ADR 0020, no protocol bump):
  `CpuPercent`, `MemoryUsedBytes`, `DiskFreeBytes`, `DiskTotalBytes`. It already has the right cadence, the
  right key and the host total RAM (`TotalMemoryBytes` is the memory denominator). A separate message would copy all
  of that. An older Agent leaves them null, and the page shows `—`.
- **D2 — Agent `HostVitalsReader`** (`IHostVitalsReader`, singleton), composed into `HostCapacityReader`:
  - CPU: host-wide busy % from the aggregate `cpu` line of `/proc/stat`, as the change since the previous read. The
    first read and a counter reset give `null`.
  - Memory used: `MemTotal − MemAvailable` from `/proc/meminfo`.
  - Disk: `DriveInfo(DataMountRoot)` → `AvailableFreeSpace` / `TotalSize`, the space the Agent itself can write.
  - Every read is fail-soft per figure: no `/proc` (Windows dev box), an unreadable file or a missing directory gives
    `null` for that figure, never an exception. The `/proc` root is a constructor seam so tests use a temp folder.
  - **Docker root: not done.** The Agent can't see it without a new mount; the PZ data volume holds the worlds,
    backups and installs that actually grow.
- **D3 — Web cache + ownership.** `HostCapacity` gains trailing `HostVitals? Vitals = null` (Application mirror,
  no Contracts dependency). It goes in the existing `IHostCapacityCache`, keyed by the **authenticated connection's**
  AgentId and never by the payload, so an Agent can only ever write its own row. Readers only look up AgentIds
  already returned by the tenant-filtered, `Agent.View`-gated `IAgentInventory.ListHostsAsync`. The hub treats the
  values as untrusted: a CPU outside 0–100, a negative byte count, used memory above total, or free disk above total
  drops that figure (or pair), not the whole report.
- **D4 — view model `HostTelemetry`** (`Components/Hosts/HostTelemetry.cs`, pure). It takes the cached report, the
  connected flag and the current time, and returns the CPU %, memory used/total, disk free/total, the preformatted
  texts (`12.3 GiB / 31.2 GiB`, `212.0 GiB free of 480.0 GiB`), an age (`updated 5s ago`) and `Stale` when the
  report is older than 60 s (four missed 15 s ticks). An unreachable host has no telemetry (as today).
- **D5 — render.** The card's telemetry block shows CPU (meter), Memory (meter plus a used/total line) and
  **Disk** (a used-share meter, so it warms as the volume fills, plus an `x free of y` line), then the age line. A
  stale report keeps its values, with the age line in the busy tone: `stale · as of 3 min ago`. A missing figure
  shows `—` in the meter slot and hides its text line (the slot is kept, as on Fleet). Changed after the Aspire
  screenshot: the row was first labelled "Disk free" over a used-share meter (it read as "63 % free"), and an empty
  figure showed `—` twice.
- **D5b — panel layout (user pick "B" from the mockups, 2026-10-04).** Each metric sits in its own shaded panel
  (`bg-muted`, `rounded-md`) with a Lucide icon (`cpu`, `memory-stick`, `hard-drive` via `ShellIcon`; meta rows keep
  no icons) and a two-sided detail line: `4 cores | load 0.12 / 0.20 / 0.18`, `9.3 GiB available | 6.2 / 15.5 GiB`,
  `191.7 GiB used | 34.0 GiB free of 225.7 GiB`. A detail line with nothing on either side is hidden. For this the
  report gains `CpuCores` (the `cpuN` lines of `/proc/stat`) and `LoadAverage1/5/15` (`/proc/loadavg`), also trailing
  and nullable; ingest drops a core count outside 1–4096 and all three loads if any is negative or not a number.
- **D6 — live.** New `GET /api/hosts/telemetry` (`Agent.View` policy, tenant-filtered through
  `IAgentInventory.ListHostsAsync`, `no-store`), using the same `HostTelemetry` projection. The page root carries
  `data-live-hosts="/api/hosts/telemetry"`. `live-status.js` gains a hosts branch that reuses `applyMeter`/`setText` to
  update each `[data-host-telemetry-for="<agentId>"]` cell (textContent only). Idle cadence (5 s) and only while the
  tab is visible. Connect and disconnect changes still need a reload (the card layout changes). That is the #357
  pattern, not in scope here.
- **D7 — tests.** Contracts: round-trip with vitals, and an older report leaves them null. Agent: vitals reader (CPU
  delta, first-read null, meminfo used, missing `/proc` gives nulls, disk for an existing dir and a missing one);
  capacity reader carries vitals. Web: hub ingest (keyed by connection, bounds drop bad figures); `HostTelemetry`
  (format, stale, unreachable, older Agent); the page renders the meters and disk row; the endpoint is `Agent.View`
  gated and returns only the tenant's hosts.

## Not done here

- Docker-root disk (D2). The Fleet hierarchy rollup rows (#340 consumes `IHostCapacityCache`). Live reshaping when a
  host connects or disconnects (#357).
