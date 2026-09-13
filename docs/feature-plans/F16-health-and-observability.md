# Feature 16 Mini-Plan — Health and Observability

**Status:** IN PROGRESS. Roadmap issue: [F16 (#37)](https://github.com/MCrank/ZWarden/issues/37); folds in [#88 (MeterBar)](https://github.com/MCrank/ZWarden/issues/88). Track D — the feature that makes a running Server *observable*; **depends on F15 (basic lifecycle), merged**. Unblocks F17 (SteamCMD lifecycle), F18 (RCON), F27 (live logs), and the operator surface (F28/29/30).

**Format:** PRD 60. **TDD is mandatory** (PRD 2.2). **Written against:** [`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (F16), ADR [0003](../adr/0003-blazor-blueprint-ui-library.md) (Blueprint wrapper seam + the live 1 Hz circuit path this UI finally exercises), ADR [0008](../adr/0008-docker-socket-access-via-wollomatic-socket-proxy.md) (the allowlist — **amended by this feature**, see D-METRICS), ADR [0020](../adr/0020-agent-protocol-versioning-and-catalogue.md) (additive-vs-breaking protocol rules for the new events), ADR [0022](../adr/0022-operation-lifecycle-and-per-server-locking.md) (read-only/host-level operations never contend), ADR [0016](../adr/0016-tenant-isolation-query-filter.md) (health is tenant-owned), and [`trust-boundaries.md`](../trust-boundaries.md) §3 (observed-never-inferred) / §8 (untrusted Agent output). Two new ADRs land with the feature: **0023** (health model + delivery, PR-A) and **0024** (OpenTelemetry baseline, PR-B).

## Objective

Turn "last-reported run-state" into a real, **hierarchical health model** with **runtime metrics** and a **live** operator view. F16 must distinguish the five operator-facing states the issue names — **stopped, starting, healthy, degraded, failed** — from container / process / startup / network probes rolled up on the Agent, deliver them observed-never-inferred, expose CPU/memory/disk meters, and stand up an OpenTelemetry baseline (no Prometheus). It also builds **MeterBar** (#88), the second and last of the Feature-0 owned wrapper components, alongside its first real consumer.

## The four load-bearing decisions (settled with the maintainer before writing)

- **D-METRICS — real CPU/memory means amending the ADR 0008 allowlist.** `docker stats` (`GET /containers/{id}/stats`) is **not** among the ten allowlisted verbs, so resource meters require an **eleventh entry**. Decision: **amend ADR 0008** to add read-only, **non-streaming** `GET /containers/{id}/stats?stream=false` (a read verb, no mutation, no exec) and update both the wollomatic allowlist config and the allowlist **drift test** (F13). Disk usage is read from the **host bind-mount path** directly (the Agent owns `DataMountRoot`) — no Docker. **Player-count is not available in F16** — it needs RCON, which is F18 and *depends on F16*; the player meter is deferred and its wire field ships nullable.
- **D-OTEL — a real baseline: SDK + Meter/tracing + opt-in OTLP.** Add the OpenTelemetry SDK to ZWarden.Web and ZWarden.Agent; define a control-plane `Meter` (operation outcomes, connected Agents, health transitions, probe/sample durations) and an `ActivitySource` over the operation dispatch→execute→ingest path; ship an **OTLP exporter that is opt-in via config** (no endpoint ⇒ off), with the console exporter in Development. **No Prometheus exporter** (its OTel exporter has never stabilised — the issue's explicit non-goal). New **ADR 0024**.
- **D-UI — MeterBar + a live per-server health panel; the fleet grid stays static.** Build MeterBar (#88); ship a **live, interactive-circuit** per-server health/detail view (the ADR 0003 "1 Hz push" path, finally exercised) with the meters and the hierarchical breakdown. The `/servers` fleet table stays **static SSR** (adds a health column); the full virtualized live `BbDataGrid` grid remains F36.
- **D-SPLIT — three PRs.** A: health model + probes + delivery. B: runtime metrics + OpenTelemetry baseline. C: MeterBar + the live health UI. #37 and #88 close on **PR-C**.

## The health model (D-HEALTH — the design crux)

Health is a **rollup**, not a synonym for run-state. `ServerRunState` (lifecycle: Unknown/Stopped/Starting/Running/Stopping/Failed) stays exactly as F14 defined it. A new **`ServerHealth`** (Contracts) carries the operator taxonomy — **`Stopped, Starting, Healthy, Degraded, Failed`** — the issue's five, and only those. "Not heard from" is **not** an enum member: it is the **staleness** of `LastHealthReportedAt` (the `Server` doc already frames the timestamp's age as the first-class signal) combined with run-state `Unknown`, rendered by the UI.

The rollup is a **pure domain function** (`ServerHealthEvaluator`) over four probe inputs the Agent gathers, so it is unit-tested exhaustively with no Docker:

1. **Container probe** — is the container running? From `docker inspect` `State.Status` (already an allowlisted verb; we project the field). Not running ⇒ `Stopped` (or `Failed` if it exited non-zero / `OOMKilled`).
2. **Process probe** — is the GameServer JVM alive and past its own HEALTHCHECK? From inspect `State.Health.Status` — the PZServer image's `HEALTHCHECK` already `pgrep`s the JVM, so **the Agent needs no `exec`** (which the allowlist denies). `starting` ⇒ `Starting`; `unhealthy` ⇒ `Degraded`/`Failed`.
3. **Startup probe** — inside vs. past `--start-period` (world load). `State.Health.Status == starting` within the window ⇒ `Starting`, not `Failed`.
4. **Network probe** — are the published game/query UDP ports reachable on the host? A running, process-healthy container whose ports are unreachable ⇒ `Degraded`.

Rollup precedence (worst-wins among running): `Failed > Degraded > Starting > Healthy`; not-running ⇒ `Stopped`/`Failed`. The evaluator returns a `ServerHealth` **and** a structured `HealthBreakdown` (the four component verdicts + a human reason) so the UI can explain *why* degraded.

**Delivery (observed-never-inferred, trust §3):** health rides the **existing push path**, enriched — never a Web-side inference from a command's success.

- `AgentStateSnapshot.ServerState` gains an **optional** `ServerHealth? Health` (nullable ⇒ additive, no protocol bump per ADR 0020; `null` = an older Agent / not yet computed).
- Two reserved events are realised: **`ServerStateChanged`** `(ServerId, ServerRunState)` and **`HealthChanged`** `(ServerId, ServerHealth, string Reason, HealthBreakdown Breakdown)` — emitted by an Agent **`ServerHealthMonitor`** loop only on a *transition*, so quiescent fleets are quiet.
- Web ingests both via new `AgentHub` receivers → `IServerStateReconciler` (tenant-scoped; a report for a foreign/absent Server is a no-op, trust §8). `Server` gains `LastHealth` (`ServerHealth?`) + `LastHealthReportedAt` and `RecordObservedHealth(...)`; a health transition writes an **audit event** (`Server.HealthChanged`, ADR 0019 append-only).

## Runtime metrics (PR-B)

Metrics are **high-churn and transient** — they are **not** persisted per sample (no DB write storm). The Agent's `ServerHealthMonitor` also samples metrics on an interval (`AgentOptions.MetricsSampleSeconds`, default 15) and emits a new **`ServerMetricsReport`** event carrying a `ServerMetricsSample` per Server: `CpuPercent`, `MemoryUsedBytes`, `MemoryLimitBytes`, `DiskUsedBytes?`, `DiskCapacityBytes?`, `PlayerCount?` (null in F16), `SampledAt`. Web holds only the **latest** sample per Server in an in-memory `IServerMetricsCache` (sibling of `IServerDiscoveryCache`), pushed to any subscribed live circuit. CPU% and memory come from the newly-allowed `stats?stream=false`; disk from the bind-mount path; player-count is null until F18.

## Scope, by PR

### PR-A — Health model + probes + delivery (branch `feat/f16-health-model-and-probes`)
1. **Contracts:** `ServerHealth` enum; `HealthBreakdown` + component verdict types; enrich `ServerState` with `ServerHealth? Health = null` (additive); new events `ServerStateChanged`, `HealthChanged` (`AgentEvent`, `[ProtocolMessage("server.state-changed" | "server.health-changed")]`); update `ClosedCommandVocabularyTests`/`EnvelopeSerializationTests`/`ProtocolCompatibilityTests` (additive ⇒ no version bump — assert it).
2. **Domain:** pure `ServerHealthEvaluator` (rollup + breakdown, exhaustively tested); `Server.LastHealth`/`LastHealthReportedAt` + `RecordObservedHealth`.
3. **Infrastructure:** EF mapping + `AddServerHealth` migration (both providers); `IServerStateReconciler` persists health from snapshot + `HealthChanged`; `ServerAuditActions.HealthChanged`.
4. **Agent:** project `State.Health`/`RestartCount`/`OOMKilled` on `EngineContainer` (inspect mapping only — no new verb); container/process/startup/network probe gatherers; `ServerHealthMonitor` hosted loop → evaluate → emit `ServerStateChanged`/`HealthChanged` on transition; network probe is a host-side UDP reachability check.
5. **Web:** `AgentHub` receivers for the two events → reconciler; ADR **0023** (health model + delivery).

### PR-B — Runtime metrics + OpenTelemetry baseline
1. **ADR 0008 amendment** (eleventh entry: read-only `stats?stream=false`) + wollomatic config + allowlist **drift test** update; ADR **0024** (OTel baseline).
2. **Agent:** `IDockerEngine.StatsAsync` (non-streaming) + `DockerDotNetEngine` impl; disk read from bind-mount; extend `ServerHealthMonitor` to sample + emit `ServerMetricsReport`; `AgentOptions.MetricsSampleSeconds` (default 15, validated > 0).
3. **Contracts:** `ServerMetricsReport` + `ServerMetricsSample`.
4. **Web:** `IServerMetricsCache` + `AgentHub` receiver; expose latest sample to the UI layer.
5. **OTel:** SDK packages (`Directory.Packages.props`); `Meter` + `ActivitySource` registered in Web + Agent; instrument the operations/health/metrics paths; opt-in OTLP exporter + console-in-Development; wire the Agent's Serilog and Web's MEL to emit under the shared resource attributes.

### PR-C — MeterBar + live health UI (closes #37, #88)
1. **MeterBar** (`Components/Ui/MeterBar.razor`) — per #88: squared, fixed-width, `--meter-*` ramp via literal Tailwind classes (`bg-meter-track|nominal|watch|hot`), Chivo-Mono `tabular-nums` right-aligned percentage, `role="meter"` + `aria-value*`; clamp 0–100; bands `<60 nominal`, `60–85 watch`, `>85 hot`. bUnit test per band (the `StatusBadgeTests` template).
2. **Live per-server health panel** — an **interactive-circuit** view (server-detail) subscribing to `IServerMetricsCache` + health push, rendering the hierarchical `HealthBreakdown` + CPU/memory/disk MeterBars; updates on new samples via the circuit (ADR 0003's 1 Hz path).
3. **`/servers`** gains a static health column (a health `StatusBadge`-style pill) beside run-state; links to the detail panel.
4. Per-PR chores: `npm run build:css` + commit `wwwroot/app.css`; **bump the Web.Tests floor in BOTH the csproj and `ci.yml` `tier1-silent-drop-guard`** ([[web-tests-discovery-floor-bump]]); bUnit `JSRuntimeMode.Loose` if MeterBar composes Blueprint primitives.

## Non-scope

- **Prometheus export** (issue non-goal). **Player-count metric** (needs F18 RCON). **The virtualized live `BbDataGrid` fleet grid** (F36). **Log ingestion / live logs** (F27). **Alerting/notifications** on health transitions (later; F16 audits the transition, it does not notify). **Widening the allowlist beyond the one read-only stats entry.** **Persisting metric time-series / history** (transient latest-sample only).

## Protocol / contract changes (all additive — ADR 0020, no version bump)

- New enum `ServerHealth`; new records `HealthBreakdown`(+ component verdicts), `ServerStateChanged`, `HealthChanged`, `ServerMetricsReport`(+ `ServerMetricsSample`); one optional member on `ServerState`. Assert additivity in `ProtocolCompatibilityTests` (`ProtocolVersion.Current` stays 1).

## Test plan (TDD, per PR)

- **PR-A:** `ServerHealthEvaluatorTests` (every probe-input combination → expected `ServerHealth` + breakdown); `Server` health-mutator tests; reconciler tests (snapshot health + `HealthChanged`, foreign/absent no-op); `EngineContainer` inspect-projection tests (fake engine); `ServerHealthMonitor` transition-only emission tests; Contracts serialization + additivity tests; hub-receiver tests.
- **PR-B:** stats-read mapping tests (fake engine); disk-read tests; metrics-cache tests; allowlist drift test (through the wollomatic proxy, integration tier); OTel registration/exporter-opt-in tests.
- **PR-C:** `MeterBarTests` (per-band literal classes + a11y attributes, bUnit); live-panel render tests (loose JSInterop); `/servers` health-column render test.

## Open confirmations to raise if they bite

- **Network probe of a UDP port** is best-effort (UDP has no handshake) — treat "no ICMP-unreachable within timeout" as reachable; document the limitation rather than over-claim. If it proves flaky it degrades to inspect-only health (still ships the other three probes).
- **OTLP default endpoint:** none (off) unless `OTEL_EXPORTER_OTLP_ENDPOINT` / config is set — confirm no accidental egress.
