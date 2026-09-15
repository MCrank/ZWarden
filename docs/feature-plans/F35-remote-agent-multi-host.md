# Feature 35 Mini-Plan — Remote Agent / Multi-Host Support

**Status:** DONE (2026-09-15) — PR-A ([#142](https://github.com/MCrank/ZWarden/pull/142)) MERGED, PR-B open.
Load-bearing decisions **LOCKED as-recommended with the maintainer (2026-09-15)**: D-1 Agent reports host
metadata; D-2 defer host-narrowing (gate on tenant-wide `Agent.*`); D-3 standalone remote-agent compose +
docs; D-4 assignment = host-pick at register (no reassignment).
Roadmap issue: [F35 (#54)](https://github.com/MCrank/ZWarden/issues/54), **Track F — Deployment and
release**. F35 is the feature that turns the already-built outbound-WSS control plane into a **legible,
operable multi-host fleet**: a remote operator installs a ZWarden.Agent on a *second* host, sees every host
in an inventory with its health, and assigns servers to a chosen host — with **no privileged inbound port on
any host** (PRD §11 **criterion 14**, the v1.0 gate this feature closes).

**Depends on [F34 (#53)](https://github.com/MCrank/ZWarden/issues/53) — DONE** (the reference Compose
distribution a remote Agent enrols against). **Blocks [F40 (#55)](https://github.com/MCrank/ZWarden/issues/55)**
(1.0 release gate). **Format:** PRD 60. **TDD is mandatory** (PRD 2.2).

## The load-bearing context: the transport is already built

The subsystems criterion 14 names — outbound WSS with **no inbound Agent port**, single-use enrollment, a
**revocable/rotatable per-Agent credential**, reconnect, heartbeat, staleness — all shipped in **F9 + F10**
and are agent-keyed end to end. Concretely, **already present and correct**, F35 only *verifies* these:

- **No privileged inbound ports.** The Agent dials out to `wss://<domain>/agent/hub`; only Caddy publishes
  80/443 (ADR 0035 / 0037). Nothing to build.
- **WAN reconnect.** `SignalRControlPlaneConnection` uses `WithAutomaticReconnect(CappedBackoffRetryPolicy)`
  — exponential backoff **capped at 30s, retrying forever** (SignalR's default gives up ~30s). On every
  reconnect it re-sends `AgentHello` + `AgentStateSnapshot`. The server-side `AgentConnectionSweeper` marks a
  silent Agent `Disconnected` after `StaleAfter`. This *is* WAN reconnect behaviour.
- **Server assignment.** `ServerInventory.razor` already has a **Host picker** (populated from
  `IServerDiscoveryCache.KnownAgents()`) on both the register and import forms; `Server.AgentId` is set once
  from it. Multi-agent assignment already works.
- **Health/discovery are per-Agent.** `IServerHealthCache`, `IServerMetricsCache`, `IServerDiscoveryCache`,
  `IServerLogBuffer` are all keyed and ownership-guarded by `AgentId`.

So F35 is **not** a new transport or a new domain engine. It is the **operator surface + install path +
host-legibility** the single-node build never needed.

## What is genuinely missing (this is F35)

1. **Hosts are anonymous.** The `Agent` record holds trust + observed-connection state only — **no host
   metadata**. A fleet renders as bare `agt-019c…` ids; an operator cannot tell one host from another.
2. **No Hosts inventory / host-health surface.** Agent management (list/rotate/revoke/disable/enable) is
   **JSON-endpoint-only** (`/api/agents…`); the sole Agent UI is the first-run `EnrollAgent.razor` wizard
   step. There is no operator page that lists hosts with `ConnectionState` / `LastSeenAt` /
   `LastProtocolVersion`.
3. **No remote-install artifact.** F34's compose ships **one co-located** Agent. There is no one-command way
   to stand up an Agent **on a different host** pointed at the public control plane, nor docs for it.

## The load-bearing decisions (LOCKED with the maintainer, 2026-09-15)

- **D-1 — The Agent self-reports host metadata.** `AgentHello` carries a small **host descriptor**
  (hostname, agent version, OS platform); the `Agent` record persists it and the inventory shows it, so a
  multi-host fleet is legible. Self-reported facts are **observed, not trusted** (trust-boundaries.md §3) —
  display-only, never an authorization input. *(Chosen over relying on the enrollment Label alone: a wall of
  `agt-…` rows is unreadable, and a hostname is the one fact that reliably tells hosts apart.)*
- **D-2 — Host-scoped permissions are deferred; the surface is gated on the existing tenant-wide
  `Agent.View` / `Agent.Manage`.** No Host-narrowable permission scope in v1.0. This honours the issue's own
  "where appropriate" hedge: host-narrowing mirrors Server-scoping onto a Host resource, which only earns its
  cost in the **multi-operator / hosted** setting that is v1.1 (F10A/F33A). *(Chosen over building
  `HostScopable` now: a large RBAC change for a self-hosted single-tenant release whose operators are already
  tenant-wide.)*
- **D-3 — Remote installation is a standalone Compose artifact + a deployment guide.** A small
  `deploy/compose/remote-agent/` stack (**Agent + wollomatic only**, outbound `wss://<domain>`, its own
  `.env` + enrollment token + persisted trust/identity volume) reusing the **F34 Agent image unchanged**,
  plus `docs/deployment/remote-agent.md`. No new runtime code; a drift guard pins its wollomatic allowlist to
  the F13/F34 copy. *(Chosen over docs-only: a copy-pasteable one-command install is the criterion-14
  operator experience; docs alone leave the operator hand-assembling a compose file.)*
- **D-4 — "Server assignment" = choosing the host at register time, which already works; `Server.AgentId`
  stays immutable.** No move-between-hosts reassignment in v1.0 — that contradicts the explicit
  scope-and-sequencing.md invariant ("a Server is not moved between hosts") and the entity doc, and would
  need an ADR overturn. F35's only assignment work is **legibility**: the Host picker and the Server "Host"
  column show the host **Label/hostname**, not a raw `agt-` id. *(Reassignment stays a post-v1.0 item.)*

## PR split

Two PRs, mirroring F34's shape.

### PR-A — Host metadata + Hosts inventory & health surface *(this branch)*
- **Contract (D-1):** extend `AgentHello` with an optional `HostDescriptor(Hostname, AgentVersion,
  OsPlatform)`. Additive, back-compatible on the wire (an older Agent omits it → nulls); protocol version
  handling per ADR 0020. Serialization round-trip tests.
- **Domain:** `Agent` gains `Hostname` / `AgentVersion` / `OsPlatform` (nullable, observed) and a
  `RecordHostDescriptor(...)` mutator called from the Hello path. Persisted via a new EF migration on **both**
  `ZWarden.Migrations.Sqlite` and `ZWarden.Migrations.Postgres`.
- **Hello path:** `AgentHub.Hello` / `IAgentConnectionStateWriter.MarkConnectedAsync` record the descriptor
  alongside the negotiated protocol version. Agent side fills the descriptor from
  `Environment.MachineName` / assembly version / `RuntimeInformation.OSDescription`.
- **Read model:** extend `AgentSummary` with `ConnectionState`, `LastSeenAt`, `LastProtocolVersion`,
  `Hostname`, `AgentVersion`, `OsPlatform`; the live registry supplies "connected now" (a persisted
  `Connected` that the registry doesn't know is shown as last-known).
- **UI (D-2 gate):** a new **`/hosts`** operator page (`Agent.View`) — a `BbDataGrid`/Bb list of hosts with
  label, hostname, agent version, OS, connection state, last-seen (relative), protocol version, enabled;
  per-host **Manage** actions (rotate / revoke / disable / enable) gated `Agent.Manage`, wired to the
  existing `IAgentTrustService`. Follows the SSR-form + Blueprint patterns (blueprint-seam-ssr-forms).
- **Assignment legibility (D-4):** the register/import Host picker and the ServerInventory "Host" column
  render the host **Label/hostname**, not the raw `agt-` id.
- **Nav:** a "Hosts" entry under the existing operator navigation.

### PR-B — Remote-agent install + WAN verification + ADR
- **Remote install (D-3):** `deploy/compose/remote-agent/` (`compose.yaml` = Agent + wollomatic, `.env.example`,
  `.gitignore`, `bootstrap-secrets.{sh,ps1}` for the remote host's key ring), reusing the F34 Agent image; a
  `ComposeDistribution`-style **drift test** pins the wollomatic allowlist verbatim to the F13/F34 copy.
- **Docs:** `docs/deployment/remote-agent.md` — install an Agent on a second host, enrol via a wizard/minted
  token, verify it appears in `/hosts`; the private-CA-trust note for `tls internal` mode (as F34's guide).
- **WAN reconnect (verify + document):** confirm capped-backoff-forever + snapshot-on-reconnect + sweeper
  `StaleAfter` behave over a simulated WAN drop (test), and document the knobs; make the reconnect cap /
  `StaleAfter` configurable **only if** the maintainer wants tuning — default behaviour already correct.
- **ADR 0038** — *Remote agents are the same outbound-WSS Agent on another host; hosts self-describe;
  host-scoped permissions deferred to v1.1.* Records D-1..D-4.

## Out of scope / explicitly deferred
- **Host-narrowed permissions** (D-2) — v1.1 (F10A/F33A).
- **Server reassignment between hosts** (D-4) — `Server.AgentId` immutable in v1.0.
- **mTLS + Agent CA** — v1.1 by ADR (§10); F35 rests on F9's bearer credential over WSS.
- **Multi-Web-instance / SignalR backplane** — the in-memory registry is single-instance by ADR 0005; scale-out
  is F10A (v1.1). F35 targets one Web instance with many Agents.
- **New transport, heartbeat, or reconnect engine** — already shipped in F9/F10; F35 verifies, does not rebuild.

## Progress
- [x] PR-A ([#142](https://github.com/MCrank/ZWarden/pull/142), MERGED): host descriptor contract + entity +
  migration (both providers) + Hello wiring + `IAgentInventory`/`HostSummary` + `/hosts` page + nav
- [x] PR-B: standalone `deploy/compose/remote-agent/` (Agent + wollomatic, outbound wss, own enrollment/trust
  volume) + `bootstrap-env.{sh,ps1}` + `RemoteAgentDistributionTests` allowlist drift guard;
  `docs/deployment/remote-agent.md`; WAN reconnect extracted to a public, tested `CappedBackoffRetryPolicy`
  (retry-forever, capped-30s); **ADR 0038** + README index (also backfilled the missing 0037 row).

## Verification
- Unit/arch/bUnit tests green (TDD); `AgentHello` round-trips with and without the descriptor.
- `/hosts` renders a multi-host fleet with health; Manage actions honour `Agent.Manage`; page honours
  `Agent.View`.
- Remote-agent compose stands up an Agent on a separate host that enrols and appears in `/hosts` over
  outbound WSS only (no inbound port). Allowlist drift test green.
- Web.Tests discovery floors bumped in both the csproj and ci.yml (web-tests-discovery-floor-bump).
- `npm run build:css` + commit `wwwroot/app.css` if any new Tailwind class lands (tailwind-app-css-rebuild).
