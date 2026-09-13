# 23. The hierarchical server-health model and its observed delivery

A Server's **health** is a five-state rollup — **`Stopped`, `Starting`, `Healthy`, `Degraded`, `Failed`**
— computed by the **Agent** from a fixed hierarchy of probes (**container → process → startup → network**)
and reported to ZWarden.Web as **observed** state, never inferred (trust-boundaries.md §3). It is a
**distinct axis from `ServerRunState`** (the coarse lifecycle position): a running Server is `Healthy`,
`Degraded` or `Failed` depending on the probes. **"Not heard from" is not a health value** — it is the
staleness of `Server.LastHealthReportedAt`, rendered by the UI. The rollup is a **pure function** of the
probe facts (`ServerHealthEvaluator`), so its whole truth table is unit-tested with no Docker; health
transitions ride the existing heartbeat/snapshot push, enriched, plus two reserved transition events, and
are **not audit events**.

- Status: accepted
- Decided in: [#37](https://github.com/MCrank/ZWarden/issues/37) (Feature 16), building on the seams reserved for it
- Bears on: PRD 40 (Agent liveness + reporting), [ADR-0020](./0020-agent-protocol-versioning-and-catalogue.md) (the additive contract changes), [ADR-0022](./0022-operation-lifecycle-and-per-server-locking.md) (probes are read-only/host-level, never contend), [ADR-0016](./0016-tenant-isolation-query-filter-and-default-tenant.md) (health is tenant-owned), [ADR-0008](./0008-docker-socket-access-via-wollomatic-socket-proxy.md) (probes read only allowlisted `inspect`; no `exec`), [ADR-0019](./0019-audit-is-append-only-tenant-owned-and-binds-the-auth-sink.md) (why health is *not* audited)

## Context

F16 must "distinguish stopped, starting, healthy, degraded, failed" from "container/process/startup/network
probes" (scope-and-sequencing §6). Two facts shape the realization:

1. **Health is not run-state.** The existing `ServerRunState` (F14) is a coarse lifecycle position
   (Unknown/Stopped/Starting/Running/Stopping/Failed). The issue's taxonomy overlaps it but is a different
   question — *"is a running server actually well?"* — so it needs its own type, not more run-state values.
2. **The Agent can read process health without `exec`.** The wollomatic allowlist (ADR-0008) denies
   `/containers/{id}/exec`, so the Agent cannot run `pgrep` inside a container. But the PZServer image (F12)
   already declares a `HEALTHCHECK` whose verdict surfaces on `docker inspect` at `State.Health.Status` — an
   allowlisted read. So the process and startup probes are reads of inspect, not in-container execution.

## Decision

- **`ServerHealth { Stopped, Starting, Healthy, Degraded, Failed }`** — the five states, and only these — as a
  wire enum (ZWarden.Contracts) with a Domain mirror and a `WireServerHealth` map in ZWarden.Web, exactly the
  wire↔domain split `ServerRunState` uses. No `Unknown` member: staleness carries that.
- **The rollup is a pure function** `ServerHealthEvaluator.Evaluate(HealthProbeFacts)` on the Agent, producing
  the `ServerHealth`, the coarse `ServerRunState`, a four-probe `HealthBreakdown` (each a `ProbeCheck` with a
  `ProbeStatus` of Pass/Warn/Fail/Skipped and an untrusted detail), and a short reason. The hierarchy:
  - **Container** — `State.Status`: not running ⇒ `Stopped` (clean exit / created) or `Failed` (non-zero exit,
    OOM, `dead`).
  - **Process** — `State.Health.Status`: `unhealthy` ⇒ `Failed`; `starting` ⇒ `Starting`.
  - **Startup** — the same `starting` window keeps a booting Server `Starting`, not `Failed`.
  - **Network** — a best-effort host-side UDP reachability probe of the published game port: a running,
    process-healthy Server whose port is unreachable ⇒ `Degraded`. The probe returns `true`/`false`/`null` and
    **only `false` degrades** — an uncertain probe (`null`) never downgrades health.
- **Delivery is observed, over the existing push path, enriched:**
  - `AgentStateSnapshot.ServerState` gains an **optional** `ServerHealth? Health` (additive, ADR-0020 — no
    version bump); the Agent now sends a **populated** snapshot on (re)connect (it previously sent an empty one).
  - Two reserved `AgentEvent`s are realized — **`ServerStateChanged`** and **`HealthChanged`** (carrying the
    reason + breakdown) — emitted by an Agent `ServerHealthMonitor` **only on a transition**, so a steady fleet
    is quiet. ZWarden.Web ingests them through `IServerStateReconciler`, which is **ownership-guarded**: an
    Agent may only move the state of a Server it owns (trust-boundaries.md §8).
  - `Server` persists `LastHealth` + `LastHealthReportedAt` (nullable; `AddServerHealth` migration, both
    providers).
- **Health transitions are not audit events.** See Alternatives.

## Alternatives considered

- **Fold health into `ServerRunState` (more enum values).** Rejected: it conflates two questions and would
  make `StatusBadge` and every run-state consumer ambiguous. Health is a second axis.
- **Poll health with on-demand diagnostic Operations** (the Ping/DockerHealth pattern). Rejected as the
  *continuous* mechanism: health is a standing property of every Server, so a per-server push is the right
  shape; the operations engine stays for *actions*. On-demand deep probing can still be added as an Operation.
- **Audit each health transition** (`Server.HealthChanged`, append-only). **Rejected.** Audit events are
  actor-attributed administration/security occurrences (CONTEXT.md, ADR-0019); a rollup flapping
  healthy↔degraded is high-churn *observed telemetry* with no operator actor, and writing each to the
  append-only store would flood it. The operator *actions* around health (start/stop) are already audited
  (F15). A durable transition trail, if ever wanted, is a separate store, not the audit log.
- **A ZWarden-computed startup window** (compare `StartedAt` to the image `--start-period`). Rejected as
  redundant: the image's own HEALTHCHECK already reports `starting` during that window, so reading
  `State.Health.Status` needs no clock.

## Consequences

- **Network health is best-effort and honest about it.** UDP has no handshake; the probe degrades a Server
  only on a definitive "port closed" signal and reports `null` (which does not degrade) on any uncertainty.
  If it proves noisy in the field it can be disabled without touching the other three probes — the evaluator
  already treats `null` as "not probed". Real reachability accuracy is a known soft spot.
- **The Agent now reports real state.** Populating the connect snapshot (it was empty since F10) means Web's
  reconciler finally sees observed run-state and health at connect, not just at provision time.
- **Metrics are deliberately out of this ADR.** CPU/memory need `docker stats`, outside the current allowlist;
  that (an ADR-0008 amendment) and the OpenTelemetry baseline land in F16 PR-B, keyed off ADR-0024.
- **A new wire enum + two events widen the protocol surface**, but additively — `ProtocolVersion.Current`
  stays 1, and the compatibility tests assert it.
