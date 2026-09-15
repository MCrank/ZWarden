# 38. A remote Agent is the same outbound-WSS Agent on another host; Hosts self-describe; host-scoped permissions wait for v1.1

**Multi-Host support adds no new component and no new transport.** A remote Agent is the **same
ZWarden.Agent** (F8) run on a second host from a standalone `deploy/compose/remote-agent/` stack (Agent +
wollomatic only), reaching an **existing** control plane **outbound over `wss://<domain>/agent/hub`** with
**no privileged inbound port on the Agent host** — exactly the F9/F10 model, now made deployable and legible
for a fleet. To tell Hosts apart, the Agent **self-reports a Host descriptor** (hostname, agent version, OS
platform) on `AgentHello`, which the control plane records and shows in a new `/hosts` inventory. Host
metadata and the inventory are **observed, display-only** (trust-boundaries.md §3), never an authorization
input. **Host-scoped permission narrowing is deferred to v1.1**: the surface is gated on the existing
tenant-wide `Agent.View` / `Agent.Manage`. A Server's Host is **still set once and never moved** in v1.0.

- Status: accepted
- Decided in: #54 (F35 — Remote Agent / Multi-Host Support); mini-plan
  `docs/feature-plans/F35-remote-agent-multi-host.md`; decisions D-1..D-4 locked with the maintainer 2026-09-15
- Bears on: PRD §11 criterion 14 (manage remote servers without privileged inbound ports — satisfied by
  F9 + F10 + F35). Builds on ADR 0007 (outbound-WSS Agent auth; the bearer credential carries v1.0),
  ADR 0037 (the reference Compose distribution the remote Agent enrols against; its Agent image is reused
  verbatim), ADR 0008 (the wollomatic allowlist, shipped identically here), ADR 0035 (Caddy ingress),
  ADR 0018 (ZWarden-owned RBAC; the tenant-wide/Server-scopable split). Defers the mTLS + Agent CA work to
  v1.1 (scope-and-sequencing §10).

## Context

By F35 the parts criterion 14 names were already built and tested: the Agent dials **outbound** to
`wss://<domain>/agent/hub` (F10) authenticated by a **single-use enrollment** that mints a **revocable,
rotatable per-Agent credential** (F9, ADR 0007); the connection **reconnects forever with a capped backoff**;
a heartbeat and a server-side staleness sweeper track liveness; and the discovery/health caches are keyed per
Agent. The F34 reference stack, however, ships **one co-located Agent**, and three things were missing for a
genuine multi-Host fleet:

1. **Hosts were anonymous.** The `Agent` record held trust + observed-connection state only. A fleet rendered
   as a wall of `agt-019c…` ids an operator could not tell apart.
2. **There was no operator surface.** Agent management existed as JSON endpoints; the only Agent UI was the
   first-run enrollment wizard. Nothing listed Hosts with their connection state and last-seen.
3. **There was no remote-install path.** No one-command way to stand an Agent up on a *different* host
   pointed at the public control plane, and no documentation for it.

Two forces shaped the decision. First, **self-reported facts cannot be trusted** — an Agent could claim any
hostname — so host metadata must be display-only and never gate anything. Second, **host-scoped permission
narrowing** (limiting a grant to one Host, mirroring Server-scoping) earns its cost only with multiple
operators over multiple Hosts, which is the **hosted/multi-operator** setting that is already v1.1 (F10A/F33A);
building it for a self-hosted single-tenant release would be RBAC weight with no v1.0 payer.

## Decision

- **D-1 — Hosts self-describe.** `AgentHello` carries an optional `HostDescriptor(Hostname, AgentVersion,
  OsPlatform)`, additive and back-compatible on the wire (an Agent that predates F35 sends none). The `Agent`
  record persists it (`Hostname`/`AgentVersion`/`OsPlatform`, nullable) via `RecordHostDescriptor`, recorded on
  the connect handshake. It is **observed, display-only** and never an authorization input. The Agent fills it
  from `Environment.MachineName` / assembly version / `RuntimeInformation`, each coalesced to a non-blank
  fallback so it can never fail the handshake.
- **D-2 — Host-scoped narrowing is deferred; gate on tenant-wide `Agent.*`.** The new read-only `/hosts`
  inventory (`IAgentInventory` → `HostSummary`) is gated on **`Agent.View`**; host trust-management stays on
  the existing `IAgentTrustService`. No `HostScopable` permission scope in v1.0.
- **D-3 — Remote install is a standalone Compose stack + a guide.** `deploy/compose/remote-agent/` runs the
  Agent + wollomatic only, outbound `wss://<domain>`, with its own `.env` + one-time enrollment secret and a
  persisted identity/trust volume, reusing the F34 Agent image. The **wollomatic allowlist is shipped verbatim**
  and pinned by an offline drift test. `docs/deployment/remote-agent.md` documents enrol → verify in `/hosts`.
- **D-4 — "Server assignment" is host-pick at register; no reassignment.** Assigning a Server to a Host is
  choosing the target Agent when registering, which already works. `Server.AgentId` **remains immutable** — a
  Server is not moved between Hosts in v1.0.

## Alternatives considered

- **Rely on the enrollment Label alone, no self-reported metadata.** Rejected: a Label is optional and
  operator-set; a hostname is the one fact that reliably distinguishes Hosts, and self-reporting it costs only
  an additive `AgentHello` field.
- **Build `HostScopable` permission narrowing now.** Rejected for v1.0 (kept as the v1.1 path): it mirrors
  Server-scoping onto a Host resource — a handler, assignment shape and UI — for a payoff only multi-operator
  hosted installs see, and those are v1.1 anyway.
- **Docs-only remote install.** Rejected: a copy-pasteable one-command stack is the criterion-14 operator
  experience; a prose-only guide leaves the operator hand-assembling a Compose file and its allowlist (which
  would then drift from F13/F34).
- **A dedicated Host entity.** Rejected: an Agent already *is* a Host (CONTEXT.md). A parallel entity would
  duplicate identity and trust for no behaviour F35 needs.
- **Make `Server.AgentId` mutable to move Servers between Hosts.** Rejected: contradicts the explicit
  scope-and-sequencing invariant and would need reconcile/discovery rework; deferred past v1.0.
- **mTLS + Agent CA for remote hosts.** Rejected for v1.0 by scope-and-sequencing §10: transport security and
  server authentication come from WSS; the residual risk (credential theft on a compromised host is not
  mitigated by possession-of-key) is the one ADR 0007 already records. mTLS is v1.1.

## Consequences

- **Host trust rests on a bearer credential over WSS, not possession-of-key** (inherited from ADR 0007): a
  remote host's stolen credential is usable until revoked. Revocation is immediate (the live connection is
  aborted), and rotation is one operator action — but the residual risk stands until mTLS lands in v1.1.
- **The wollomatic allowlist now lives in three places** (F13 tests, the F34 compose, and the remote-agent
  compose). Two offline drift guards (`ComposeDistributionTests`, `RemoteAgentDistributionTests`) pin the
  copies verbatim, so they cannot silently diverge — but an intentional change must edit all three.
- **Self-reported Host metadata is spoofable.** It is display-only by construction; operators must not read it
  as proof of which physical host they are looking at when trust matters — the `agt-` id and the credential are
  the real identity.
- **A remote host in Private (`tls internal`) control-plane mode must trust Caddy's CA** in the Agent
  container for the outbound wss handshake — the same step F32/F34 already document, now called out in the
  remote-agent guide. Public mode works out of the box.
- **The in-memory connection registry is still single-Web-instance** (ADR 0005). Many Agents against one Web
  instance is fully supported; scaling Web horizontally (a SignalR backplane) remains F10A/v1.1.
