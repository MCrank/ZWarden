# ZWarden Trust Boundaries

**Status:** accepted. Resolves [Name the system trust boundaries](https://github.com/MCrank/ZWarden/issues/7).

**Scope.** One page, six boundaries. For each: what crosses, what must never cross, and which side is
authoritative. It is deliberately **not** a threat enumeration — STRIDE-style analysis against a system
that does not exist yet is out of scope, and Feature 40 owns the full review. This document exists to be
written into the PRD 15 architecture tests (§8) and to give Feature 40 something specific to attack (§9).

---

## 1. The central claim

**ZWarden.Agent sits inside the host's trust domain.** Docker socket access is effectively root on that
host, and research established that no mature socket proxy can authorize by container label — so nothing
outside ZWarden's own code can enforce "this Agent may touch only its assigned containers".

Therefore: **a compromised Agent means a compromised host**, and the boundary that carries the system's
weight is **ZWarden.Web → ZWarden.Agent** (§3). Every control below is placed on that understanding rather
than on a hope that the Agent can be contained by its runtime.

PRD 27 calls a restricted proxy the "preferred *long-term* architecture". This document treats that as
defence in depth, not as a boundary that exists today.

---

## 2. Browser → ZWarden.Web

**Crosses:** authenticated session cookie; user intent as commands and queries; rendered UI.

**Never crosses:** RCON passwords or any internal credential material (PRD 29); bearer tokens exposed to
browser script without a justified requirement (PRD 11 prefers HttpOnly cookies); a tenant identifier the
browser chose (PRD 7A — tenant context derives from the session, never from a request parameter); raw
secrets of any kind, including in error messages.

**Authoritative:** ZWarden.Web, absolutely. The browser is an untrusted client. Hiding a UI control is not
authorization (PRD 12); every decision is re-made server-side in application logic.

---

## 3. ZWarden.Web → ZWarden.Agent

The load-bearing boundary. Agents connect **outbound** (Agent → Web, WSS/SignalR); no inbound management
port is ever opened on a host (PRD 16, and PRD 64's criterion 14).

**Crosses:** domain-specific commands from a closed vocabulary (PRD 19 — `RestartServer`, `KickPlayer`,
`InstallWorkshopItem`, …), each carrying an `OperationId`; heartbeats, state snapshots, operation progress
and health upward; log and console output on explicit subscription only (PRD 38).

**Never crosses:** an arbitrary command string. `ExecuteShellCommand(string)` does not exist and must not be
reintroduced under any name (PRD 19). Also never crosses: database credentials or connection strings (§7);
another tenant's data (§6); a user's session or identity material.

**Authoritative:** split, and the split is the point.

- **ZWarden.Web** is authoritative for **desired** state, **authorization** and **audit**.
- **ZWarden.Agent** is authoritative for **observed** state.

ZWarden.Web may **never infer a state transition from a command's success**. A start operation that returned
success means the command was accepted, not that the server is running — Feature 16 exists because a running
container does not mean a healthy PZ server. On heartbeat loss, Web keeps the last observed state, marks it
**stale**, and never promotes it back to current.

**What the Agent re-checks.** PRD 12 requires authorization "enforced again at privileged Agent command
boundaries". The Agent has no user database and cannot evaluate tenant RBAC, so what it enforces is a
**capability check, not an authorization check**:

1. the target Server is assigned to this Agent (PRD 25 labels, and its own assignment record);
2. the command is in its allowed vocabulary;
3. operational safety holds — one conflicting mutating operation per Server (PRD 21), and idempotency by
   `OperationId` (PRD 20).

It does **not** re-decide whether the requesting user held the permission. Calling that "authorization"
would be believing in a control that does not exist.

**Current strength of this boundary.** v1.0 authenticates an Agent with a single-use, short-lived enrollment
credential exchanged for a **revocable, rotatable per-Agent credential** over WSS. Mutual TLS and the
certificate lifecycle of PRD 17 are deferred to v1.1 by ADR. Stated plainly: transport security and *server*
authentication come from WSS, but *Agent* authentication rests on a bearer credential, so theft of that
credential on a compromised host is not mitigated by possession-of-key. This is the sharpest known weakness
in the system and is named here on purpose.

---

## 4. ZWarden.Agent → Docker Engine

**Crosses:** a narrow set of container operations against containers bearing ZWarden's canonical labels
(PRD 25): inspect, start, stop, restart, logs, and creation of canonical PZ containers.

**Never crosses:** operations on unlabelled or foreign containers; image or volume administration beyond what
a canonical container needs; anything that would make the Agent a general-purpose Docker administration
interface (PRD 27, explicitly).

**Authoritative:** ZWarden.Agent, by necessity — see §1. Enforcement is **in Agent code**: label validation
plus the assignment record, with a coarse proxy (endpoint and method allowlist) in front of the socket as
defence in depth. The proxy cannot scope by label, so it narrows *what kind* of call is possible, never
*which container* it lands on.

**Consequence to accept:** the label-and-assignment check is the only thing standing between an Agent bug and
any container on the host. It therefore deserves the same test rigour as an authorization check, even though
§3 says it is not one.

---

## 5. ZWarden.Agent → Project Zomboid runtime

**Crosses:** RCON commands over a private network (PRD 29); process supervision via the stdin FIFO (`save`
then `quit`, not SIGTERM); file reads and surgical writes under the canonical `/pz/` layout (PRD 23);
SteamCMD invocations.

**Never crosses, outward:** the RCON password. **The Agent generates and owns it; ZWarden.Web never sees it**
(PRD 29, un-hedged here). Web cannot leak what it never holds, and "the browser never receives RCON
credential material" becomes true by construction rather than by discipline. The accepted cost: if a host is
lost, RCON access to its servers is not recoverable *from the control plane* — an operator with host access
can always read or reset it in the config file.

**Never crosses, inward:** trust. Everything the runtime produces is attacker-influenced — logs, RCON
responses, player names, mod metadata, and the config files themselves once mods write to them.

**Authoritative:** the PZ runtime for what is actually true of the game; the Agent for interpreting it. The
Agent abstracts Steam and PZ filesystem details away from ZWarden.Web (PRD 23) — Web must never be taught the
game's on-disk layout.

---

## 6. Tenant → Tenant

**Crosses:** nothing. Ever.

**Authoritative:** ZWarden.Web, from the authenticated session — never the browser (PRD 7A). ZWarden remains
the authoritative source for membership, ownership and permissions even when an external identity provider
supplies organization context; a ZWarden Tenant id and an Auth0 organization id are never interchangeable
(PRD 63A).

**The filter is always evaluated**, including in a self-hosted installation that contains exactly one tenant,
and **cross-tenant integration tests run in v1.0 against a two-tenant fixture**. A boundary that is never
exercised is not a boundary — and deferring the exercise to v1.1 would defeat the seams-first split the
sequencing spec commits to.

Diagnostics must never combine data from unrelated tenants (PRD 63A), which makes Feature 30's support package
a tenant-scoped artifact, not a system-wide one.

---

## 7. Database reachability

**Reachable by:** ZWarden.Web only, on the `data` network (PRD 26).

**Never reachable by:** ZWarden.Agent, which holds **no database credentials and no connection string** — all
state crosses the protocol of §3. Nor by ZWarden.PZServer, which is kept off that network entirely (PRD 24).

**Authoritative:** ZWarden.Web owns all durable control-plane state. The Agent's own local store (Feature 8)
may hold only what it needs to survive a restart — its identity, its credential, its assignment record, and
in-flight operation bookkeeping. Never user, tenant, permission or audit data.

Encryption keys are not stored in the same database as the values they protect (PRD 10).

---

## 8. Cross-cutting: untrusted data never becomes trusted

PZ-originated data is untrusted **end to end**. No stage marks it clean:

- **The Agent bounds it** — size, nesting depth, and rate — and hands it on. It does not sanitize, because a
  sanitized-and-therefore-trusted label is a lie that every downstream consumer would then rely on.
- **ZWarden.Web stores and broadcasts it as opaque data**, never as markup and never as an identifier.
- **Escaping happens at render.** Player names, log lines and RCON output are data at every hop.

This keeps Feature 30's redaction and Feature 31's delimiters honest: both are handling untrusted input by
definition, not input someone upstream promised was safe.

**The config-file parser rule**, from the Lua research: ZWarden applies its own size cap and nesting-depth
pre-check **before** the parser is handed a file, and nothing on the config path recurses over
attacker-controlled tree depth. Measured: no Lua parser survives hostile nesting — Loretta's process dies at
roughly 1800 levels, MoonSharp's at roughly 3200, `StackOverflowException` cannot be caught, and .NET 10 has
no AppDomain to sacrifice. The parser also never *evaluates* file contents.

---

## 9. The PRD 15 architecture tests this implies

Assertions, not aspirations — each should fail the build if violated:

1. `ZWarden.Agent` does not reference EF Core, the Infrastructure project, or any database provider.
2. `ZWarden.Domain` references no infrastructure framework (EF Core, ASP.NET Core, Docker, SignalR, SteamCMD,
   filesystem implementations) — PRD 15's existing list.
3. No Agent-facing contract carries a free-form command, script or shell string.
4. Every tenant-owned query passes through the tenant filter; no repository exposes an unscoped read.
5. Nothing in `ZWarden.Web` references a Docker client library.
6. Auth0-specific types do not appear in Domain or Application (PRD 11).
7. The RCON password type is unreachable from `ZWarden.Web` — it exists only inside the Agent's assemblies.

## 10. What Feature 40 should attack

The claims most worth trying to break, in order:

1. **Agent credential theft** — §3's named weakness, until mTLS lands in v1.1.
2. **The label-and-assignment check** (§4), which is the only barrier to any container on the host.
3. **The tenant filter** (§6) under a request that supplies its own tenant hint, an unscoped query path, or a
   diagnostics export.
4. **State inference** (§3) — any code path where ZWarden.Web advances state without an Agent report.
5. **The untrusted-data rule** (§8) — any stage that treats PZ-originated data as clean, especially on the
   config-write and support-package paths.
