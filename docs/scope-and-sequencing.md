# ZWarden v1.0 / v1.1 Scope and Sequencing

**Status:** accepted. Resolves [Draw the v1.0 scope line and sequence the roadmap](https://github.com/MCrank/ZWarden/issues/11).

**Authority.** This document supersedes PRD 59's *slicing and ordering*. It does not change any
product requirement: PRD sections 1, 2.1 and 64 remain frozen, and every technology or
architecture choice it overturns is called out in §10 as an ADR that must be written before
Feature 0 locks package versions.

**Feature identity.** PRD 59's numbers are permanent ids. This document defines an ordering *over*
them rather than a renumbering, so every PRD cross-reference (PRD 22 → F12, PRD 36 → F19,
PRD 32 → F20) keeps working. Splits take letter suffixes (`F20a`); merges keep the surviving id
and record the absorbed one.

---

## 1. The scope test

> **v1.0 is the minimum feature set that satisfies all fifteen PRD 64 success criteria, plus the
> Feature 40 release gate. Nothing else.**

The burden of proof is on inclusion: a feature is in v1.0 only if a named criterion needs it.
PRD 64 is frozen, so this is the only scope test with authority behind it, and it makes the
allocation mechanical rather than argued.

Two consequences worth stating plainly, because both are load-bearing:

- **Criterion 14** — "manage remote servers without opening privileged Agent ports" — is
  Feature 35. **Remote, multi-host Agents are v1.0**, not a follow-on. A localhost-only v1.0 would
  make F35 a retrofit of the exact boundary the Manager/Agent split exists to serve.
- **Five criterion-adjacent features fall out** (§9). Each is defensible on product taste and
  none is needed by a criterion.

## 2. Release definitions

| Release | Definition |
| --- | --- |
| **v1.0** | Self-hosted. Satisfies all fifteen PRD 64 criteria and passes the F40 gate. Tenant-aware throughout, single tenant by default. |
| **v1.1** | Hosted SaaS. Adds *only* what hosting requires: the Auth0 Organizations wiring, tenant administration and invitations, hosted Agent enrollment, and the hosted deployment itself. No second architecture. |
| **post-1.1** | The backlog in §9. Not fog: consciously deferred, with the criterion-based reason recorded. |

The v1.1 rule that makes this work: **the seams ship in v1.0, the hosting ships in v1.1.** Tenant
scope, the identity-provider abstraction, and tenant-scoped authorization are built *and tested*
in v1.0 so that v1.1 is integration work rather than rearchitecture (PRD 7A's own instruction).

## 3. Corrections to PRD 59

PRD 59 was written before the release split and before the Project Zomboid runtime was verified.
Four structural defects and five runtime corrections are fixed here.

### 3.1 Structural defects

1. **F9 depended on F3D, which is v1.1.** As written, v1.0 could not enroll an Agent.
   **Fixed:** F9 now depends on F5 (authorization) and the tenant foundation. Enrollment needs a
   tenant to bind to and an authorization check; it does not need membership management. The
   "single-tenant self-hosted default" bootstrap folds into F3A, where that deliverable already
   lives. F3D stays whole and moves to v1.1.
2. **F3C and F5 were the same feature twice** — both delivered roles, permissions, resource
   authorization, policy handlers and tests. **Fixed:** merged into **F5 — Authorization**. PRD 12A
   defines one permission namespace covering tenant and server scope, so splitting it by scope
   would have split one model across two features and forced the second to rewrite the first's
   policy handlers. `F3C` is retired as an id.
3. **F3B and F4 overlapped, and F4 was gated behind Auth0**, inverting PRD 63's "the initial
   architecture shall not require external identity providers". **Fixed:** **F4 — Identity and
   Authentication** absorbs F3B's local identity and OIDC *abstractions*, with local ASP.NET Core
   Identity landing first and the OIDC seam proven against a test double. `F3B` survives as the
   **Auth0 Organizations integration only**, in v1.1.
4. **Nothing Docker-shaped could start until auth, authz and audit were done**
   (F13 → F11 → F6 → F5 → F4 → F3B → F3A). The riskiest external unknowns sat at the end of the
   longest chain. **Fixed:** F12 moves to second position, immediately after F0, which its own
   declared dependencies already permitted (§5, Track B).

### 3.2 Runtime corrections absorbed from research

From [Verify Project Zomboid server, ports, RCON and SteamCMD facts](https://github.com/MCrank/ZWarden/issues/5)
and [Choose how to read and write Project Zomboid Lua config files](https://github.com/MCrank/ZWarden/issues/15):

1. **The port model shrinks to two UDP ports** (16261, 16262) plus a private 27015/TCP for RCON.
   There is no Steam query port (queries ride 16261 since 41.77) and no per-player ports (retired
   at 41.65, and TCP when they existed). PRD 28's "Steam/query requirements" and "additional
   player ports" are dead concerns. Multi-server-per-host allocation is a **two-port stride**.
   Affects F12, F13, F14 — all *smaller* than planned.
2. **Whitelist addition does not exist as a whitelist operation.** `addusertowhitelist` and
   `addalltowhitelist` are `@DisabledCommand` in the shipped build. F19 is rescoped in §6.
3. **Configuration needs a Lua reader/writer, not an INI parser.** Three of four files are Lua;
   `_SandboxVars.lua` is a global assignment and both spawn files are function definitions. A
   syntax error or a UTF-8 BOM is **fatal to server start**, the server rewrites the INI and
   sandbox file on *every* start, and the in-game admin panels are a second author. This is why
   F20 splits (§7).
4. **SteamCMD-at-runtime is mandatory.** PZ EULA 3.2 closes PRD 22's redistribution escape
   clause, so a clean container always performs a network install on first run. This shapes F12's
   exit condition and F0's CI skeleton (§6, Q9 decision).
5. **Smaller absorptions:** SteamCMD exit codes are undocumented, so F17 parses stdout rather
   than branching on exit codes; `-Dsoftreset` is broken as of 42.20.4; the developers discourage
   SIGTERM in favour of a stdin FIFO with `save` then `quit`, which shapes F12's supervision and
   F15's stop path; and RCON's four behavioural traps (no response terminator, empty results
   sending no packet, a five-connection cap, tick-serialized execution) enlarge F18 without
   moving it.

## 4. Vocabulary

The product has **three** first-class components (PRD 13), and those names are the only component
vocabulary: **ZWarden.Web**, **ZWarden.Agent**, **ZWarden.PZServer**.

- **"Manager" is retired.** All 19 PRD occurrences mean ZWarden.Web. The protocol keeps its
  assembly-neutral home in `ZWarden.Contracts`, so nothing in PRD 14's layout needed the word.
- **"Control plane" is a product descriptor, not a component** — the sense CLAUDE.md uses. It
  stays in prose and never names a component.
- **"Server"** means a Project Zomboid server instance. A machine is a **host**.

The mechanical sweep across the PRD, CLAUDE.md and `docs/agents/*` is tracked as its own ticket
and blocks the Feature 0 mini-plan; the terms themselves are recorded in `CONTEXT.md`.

---

## 5. The tracks

The dependencies form a DAG, not a queue. Five tracks run with the entry conditions below; a
single worker can read §12 for a topological order instead.

```text
Track A  Control plane foundation   F0 → F1 → F2 → F3 → F3A → F4 → F5 → F6
Track B  PZ runtime                 F0 → F12                            (parallel to A from day one)
Track C  Agent plane                F1 → F7 → F8 → F9 → F10 → F11 → F13 (F9 waits on A's F5)
Track D  Server operations          F13+F12 → F14 → F15 → F16 → {F17, F18, F20a} → …
Track E  Operator surface           F16+F18+F27 → F28, F29 → F30
Track F  Deployment and release     F4+F10 → F32 → F33 → F34 → F35 → F40
```

**Track B is the point of the reordering.** F12 declares only F0, so it starts on day one and
proves the three unknowns most likely to invalidate the plan — mandatory SteamCMD-at-runtime, PZ
boot behaviour, the two-UDP-port model — before the auth stack is built on top of assumptions
about them. Its exit condition is verified by hand and by container-level tests, because no Agent
exists yet to drive it. If the EULA/SteamCMD reality breaks F12's shape, it breaks in week one.

## 6. v1.0 features, in sequence

Each entry gives the redrawn dependencies, a scope boundary sharp enough to judge against PRD 59's
~100K guardrail, and what is explicitly **not** in it. Mini-plans (PRD 60) are written
just-in-time by whoever picks the feature up; only F0's is in scope for the current effort.

### Track A — Control plane foundation

**F0 — Repository and Engineering Foundation** · *depends: none*
**In:** solution and project layout (PRD 14), SDK pin, compiler config, analyzers, `.editorconfig`,
warnings-as-errors, TUnit + TUnit.Mocks, test conventions, ADR framework, contribution docs, CI
skeleton — including the **two CI tiers** F12 needs: an always-on offline tier and a
scheduled/opt-in tier permitted to reach the network.
**Not:** any domain code; container scanning, SBOM, signing and provenance (post-1.0, PRD 54/55).
**Note:** `TreatWarningsAsErrors` promotes transitive NuGet advisories to build failures — a
supply-chain break becomes a red build, which is intended but must be a conscious CI policy.

**F1 — Domain Foundation and Typed UUIDv7 IDs** · *depends: F0*
**In:** UUIDv7 generation, prefixed ID spec, typed IDs, parse/format/serialize, validation, the
prefix registry, EF conversion strategy.
**Not:** persistence itself (F2). **Note:** UUIDv7 is a .NET 9+ BCL API but absent from EF Core and
SQLite, so the typed-ID conversion layer carries it; the native-vs-text storage representation is
still open (PRD 8) and is settled by measurement in the persistence spike.

**F2 — Persistence Foundation** · *depends: F1*
**In:** EF Core infrastructure, **both** providers, migrations, initialization, migration execution
strategy, concurrency primitives, integration tests green on SQLite *and* PostgreSQL.
**Not:** provider-specific query features (PRD 9 forbids them without justification).
**Note:** both providers are supported deployment modes in v1.0 — F34 ships both and v1.1 needs
PostgreSQL regardless, so the work is not deferrable, only hideable. If the dual-provider spike
shows SQLite cannot meet PRD 21's per-server locking, the answer flips to PostgreSQL-only with
SQLite demoted to development use; that call belongs to the spike.

**F3 — Security and Cryptography Foundation** · *depends: F2*
**In:** secret abstractions, authenticated encryption, key loading, secure configuration,
secret-aware logging types, redaction primitives, security tests.
**Not:** the reference deployment's concrete secret store (still open, PRD 10).
**Note:** AES-256-GCM's tag-size-less constructors are obsolete, and NIST's 2^32-invocation
key-rotation cap is undocumented by Microsoft — the rotation story must be explicit, not inherited.

**F3A — Tenant Foundation** · *depends: F3*
**In:** Tenant entity, tenant-scoped identifiers, tenant context abstraction, ownership rules,
tenant-aware repositories, tenant isolation tests, **and the single-tenant self-hosted bootstrap**
(absorbed from F3D so that F9 does not wait on v1.1).
**Not:** membership management, invitations, tenant settings UI (F3D, v1.1).
**Note:** the browser never selects a tenant id (PRD 7A); context derives from the session.

**F4 — Identity and Authentication** · *depends: F3A* · *absorbs F3B's abstractions*
**In:** ASP.NET Core Identity, user management, login/logout, password policy, secure cookies,
account recovery, MFA, authentication audit events, **plus the OIDC/identity-provider seam proven
against a test double**.
**Not:** the Auth0 Organizations integration (F3B, v1.1); passkeys.
**Note:** Identity ships PBKDF2-SHA512 at 100k iterations against OWASP's current 220k — the
iteration count is a deliberate configuration, not a default to inherit. Passkeys are GA but are a
primary factor with no built-in 2FA story and attestation unverified by default, so they stay out
of v1.0 and the opt-in schema version is a migration decision, not a feature.

**F5 — Authorization** · *depends: F4* · *absorbs F3C*
**In:** one permission model covering tenant and server scope: permission definitions, roles, role
assignments, tenant-scoped membership checks, resource authorization handlers, the built-in
Moderator role, custom roles, policy handlers, authorization tests.
**Not:** UI-level visibility rules; anything that assumes an external IdP's RBAC.
**Note:** the largest feature in Track A. If its mini-plan breaks the guardrail, split by *slice* —
definitions and handlers, then enforcement surface — never by inventing a tenant/server boundary
PRD 12A does not have. Auth0's RBAC is not on the Free tier, which is another reason ZWarden stays
authoritative for permissions (PRD 7A already requires this).

**F6 — Audit System** · *depends: F5*
**In:** audit entity, writer, append-oriented semantics, correlation ids, filtering, administrative
viewer.
**Not:** notifications or alerting (post-1.1).

### Track B — PZ runtime

**F12 — Canonical PZ Container Foundation** · *depends: F0* · **starts day one, parallel to Track A**
**In:** Dockerfile, base Linux environment, non-root runtime, SteamCMD, canonical filesystem,
persistent volumes, container labels, startup tooling, base health check, **stdin-FIFO supervision
with `save` then `quit` for shutdown** (the developers discourage SIGTERM).
**Not:** Agent-side orchestration (F13); native runtime (post-1.1).
**Exit condition:** a clean container installs and launches a vanilla PZ server reproducibly,
**which always requires a network install on first run** — the EULA leaves no alternative.
**Testing:** two tiers. The always-on CI tier runs offline against **hand-written synthetic
fixtures** and a pre-seeded install layout; a scheduled/opt-in tier performs the real SteamCMD
install and validates against **real files generated from a local install at test time**. No
PZ-derived artefact is committed to the repository — the install ships two conflicting licence
documents, neither naming `media/lua/`, and `servertest_SandboxVars.lua` does not ship at all.

### Track C — Agent plane

**F7 — Contracts** · *depends: F1*
**In:** protocol version, message envelopes, commands, events, heartbeat contract, state snapshot,
operation progress, serialization tests, compatibility rules — in `ZWarden.Contracts`, with no
runtime implementation dependency either way.
**Not:** transport (F10).

**F8 — Agent Runtime Skeleton** · *depends: F7*
**In:** Worker Service, Agent identity, configuration, lifecycle, local persistence where required,
health state, graceful shutdown, diagnostic hooks.
**Not:** Docker (F13), RCON (F18), anything PZ-shaped.

**F9 — Agent Enrollment and Trust** · *depends: F3, F5, F8* · *edge to F3D removed*
**In:** one-time, short-lived enrollment credentials; Agent registration; issuance of a **revocable,
rotatable per-Agent credential**; credential storage; revocation; Agent disablement; trust tests.
**Not:** mutual TLS and the certificate authority — **deferred to v1.1 by ADR** (§10). Criterion 14
is satisfied by the outbound WSS model either way, and PRD 63A's single-use, short-lived enrollment
rule is satisfied either way. The cost is stated in the ADR: transport security and server
authentication come from WSS, but Agent authentication rests on a bearer credential, so credential
theft on a compromised host is not mitigated by possession-of-key the way mTLS would be.
**Note:** this overturns PRD 17, which is permitted with a recorded reason.

**F10 — SignalR Agent Control Plane** · *depends: F9*
**In:** Agent hub, outbound Agent connection, authentication, reconnect, heartbeat, state snapshot,
protocol negotiation, connection monitoring.
**Not:** tenant-scoped hosted connectivity (F10A, v1.1).

**F11 — Durable Operations Engine** · *depends: F6, F10*
**In:** operation entity and queue, state machine, UUIDv7 operation ids, progress reporting,
idempotency, cancellation rules, **per-server locking** (PRD 21), failure semantics.
**Not:** scheduling (post-1.1). **Note:** at the guardrail; per-server locking is the part that
interacts with the SQLite question in F2.

**F13 — Agent Docker Runtime** · *depends: F8, F11, F12*
**In:** Docker API abstraction, container discovery, canonical-label validation,
inspect/start/stop/restart, allowed-container enforcement, Docker health diagnostics,
**two-port-stride allocation** for multi-server hosts.
**Not:** the restricted socket-proxy component itself — still open, and gated on the enforcement
model in [Name the system trust boundaries](https://github.com/MCrank/ZWarden/issues/7). Research
established that no mature component does label-scoped container authorization, so this is
ZWarden's enforcement to design, not a component to adopt.

### Track D — Server operations

**F14 — Server Registration and Inventory** · *depends: F5, F10, F13, F3A*
**In:** Server entity, Agent-to-Server association, discovery, import/register, metadata, dashboard
inventory. **Not:** lifecycle (F15), health beyond last-reported state (F16), config (F20).

**F15 — Basic Server Lifecycle** · *depends: F11, F14*
**In:** start, stop, restart, lifecycle progress, safe timeout handling, audit integration,
authorization, UI. The stop path uses the **stdin FIFO with `save` then `quit`**, not SIGTERM.
**Not:** update (F17), bulk operations (post-1.1).

**F16 — Health and Observability** · *depends: F15*
**In:** hierarchical health model, container/process/startup/network probes, OpenTelemetry
baseline, runtime metrics, UI status visualization; distinguishes stopped, starting, healthy,
degraded, failed. **Not:** Prometheus export as a supported surface — OpenTelemetry core is stable
at 1.18.0 but its Prometheus exporter has never stabilised, so it is not a v1.0 promise.

**F17 — SteamCMD Lifecycle** · *depends: F16*
**In:** install/update/validate state machine, version detection, failure recovery, operation
progress, audit integration. **Parses stdout** — SteamCMD's exit codes are undocumented by Valve.
**Not:** `-Dsoftreset` (broken as of 42.20.4); Workshop content (F21).

**F18 — RCON Foundation** · *depends: F12, F14, F16*
**In:** Source RCON client in `ZWarden.Rcon`, authentication, command/response, timeout, reconnect,
Agent-only secret ownership, RCON health probe. Must handle four measured traps: **no response
terminator**, **empty results send no packet**, a **five-connection cap**, and **tick-serialized
execution**. **Not:** the console UI (F28).

**F19 — Player Management** · *depends: F6, F18* · **rescoped**
**In:** player enumeration, connect/disconnect events where available, kick, ban, unban,
`removeuserfromwhitelist`, and the whitelist **mode** toggle via `changeoption Open false`;
granular permissions, audit records, UI.
**Not:** whitelist *addition*. `addusertowhitelist` and `addalltowhitelist` are `@DisabledCommand`
in the shipped build, and the only working path — `adduser` with a username and password — means
minting PZ account credentials, which drags in password delivery, storage and rotation that nothing
else in v1.0 needs. Criterion 8 is met without it. PRD 36's "whitelist actions where supported"
licenses this reading; the shipped build defines "supported".

**F20a — Configuration Read and Model** · *depends: F14* · **split, see §7**
**In:** the `IPzConfigDocument` seam (open, read values, set a value, emit bytes) over
`Loretta.CodeAnalysis.Lua` 0.2.13 pinned, `LuaSyntaxOptions.Lua51`, **parse-only — the file is
never evaluated**; a ZWarden-side size and nesting-depth pre-check *before* the parser sees the
file; a small hand-written key-equals-value reader for `<name>.ini`; the structured model;
validation; "the file did not parse" as a first-class operator-facing state with line and column.
**Not:** writes, revisions, restore (F20b). **Note:** hand-rolling stays the documented fallback
behind the seam. Validation ranges and defaults cannot be derived from the shipped Lua — they are
Java-side — so the schema is ZWarden's own, maintained by hand.

**F20b — Configuration Apply and Revisions** · *depends: F11, F15, F20a*
**In:** surgical value edits rather than whole-file regeneration; **BOM-less** temp-file plus
atomic-replace writes; config revisions diffed as **parsed values, not bytes**; restore revision;
advanced raw view/edit; **out-of-band change detection** — re-parse and compare against the last
recorded revision before any write, and **fail closed** on a mismatch until the operator confirms.
**Not:** live application of sandbox settings — `reloadoptions` does not cover `SandboxOptions`, so
sandbox editing is inherently edit-then-restart. `<name>.ini` remains live-reloadable.
**Why atomicity is a requirement, not a quality bar:** a Lua syntax error in `_SandboxVars.lua`
makes the server exit with a nonzero status, and a UTF-8 BOM crashes PZ's lexer inside its own
error-reporting path. A torn write bricks the server with no self-healing path. **Why revisions
cannot be byte diffs:** the server rewrites the INI and sandbox file on every start, regenerating
comments from its own locale, and reseeds a randomised number *inside* a comment. **Why drift
detection is mandatory:** the in-game admin panel and server-settings editor both write these files
behind ZWarden's back, so without detection the revision history would assert a previous state the
file never held.

**F21 — Workshop and Mod Discovery** · *depends: F17, F20a*
**In:** Workshop item model, Mod ID model, metadata discovery, Workshop-to-Mod mapping,
installed-state detection, compatibility diagnostics where possible. Anonymous SteamCMD covers
Workshop content, so **no Steam credentials are stored**.
**Not:** mutation (F22).

**F22 — Mod Management** · *depends: F11, F21, F20b*
**In:** install, remove, enable, disable, reorder, update, configuration synchronization, safe
restart coordination. **Not:** profiles (post-1.1).

**F24 — Backup Foundation** · *depends: F11, F14, F20a*
**In:** backup model, local destination, checksums, backup operation, retention metadata,
automatic pre-operation backup API. **Not:** remote destinations, scheduling (post-1.1).

**F25 — Restore** · *depends: F24*
**In:** restore validation, archive safety, staging, protective backup, atomic restore, health
verification, failure handling.

**F27 — Live Logs** · *depends: F16*
**In:** log ingestion, structured log events, SignalR subscriptions, filters, tail, bounded
buffering, output sanitization. Logs are **untrusted input** (PRD 38).
**Not:** log retention or search as a product feature.

### Track E — Operator surface

**F28 — Remote Administrative Console** · *depends: F6, F18, F27*
**In:** console page, RCON command transport, streaming output, command authorization, history,
auditing, input safety — under an elevated permission, and never exposing RCON credentials.
**Not:** arbitrary shell (PRD 18 forbids it outright).

**F29 — Diagnostics Engine** · *depends: F3, F16, F18, F20a, F22, F25, F27*
**In:** health checks plus DB, Docker, Agent, RCON, filesystem, SteamCMD, mod, TLS and
compatibility diagnostics. **Not:** remediation actions. **Note:** ten domains; expected to split
at mini-plan time, and the split is by domain group, which does not change the DAG.

**F30 — Sanitized Support Package** · *depends: F29*
**In:** diagnostic collector, redaction, pseudonymization, secret scanner, manifest, ZIP output,
validation, DiagnosticId. Satisfies criterion 13 on its own.
**Not:** the AI context export (F31, post-1.1).

### Track F — Deployment and release

**F32 — HTTPS Reference Deployment** · *depends: F4, F10*
**In:** Caddy 2.11.4 configuration, HTTPS, HTTP redirect, HTTP-01, WebSocket forwarding,
certificate lifecycle, public and private deployment documentation.

**F33 — First-Run Setup** · *depends: F5, F9, F14, F32*
**In:** first administrator, TLS mode, Agent enrollment, PZ server discovery, server registration,
initial health checks, setup completion state.

**F34 — Complete Docker Compose Distribution** · *depends: F33, and Track D complete*
**In:** reference Compose, volumes, networks, Caddy, the three components, **SQLite mode and
PostgreSQL mode**, secrets bootstrap, health dependencies, upgrade documentation.

**F35 — Remote Agent / Multi-Host Support** · *depends: F34* · **v1.0 by criterion 14**
**In:** remote Agent installation, Agent inventory, host health, server assignment, WAN reconnect
behaviour, host-scoped permissions where appropriate.
**Not:** certificate lifecycle — the credential model from F9 carries this in v1.0, and mTLS plus
the CA arrives in v1.1 (§10).

**F40 — Production Hardening and 1.0 Release Gate** · *depends: all of v1.0*
**In:** the PRD 59 review list, against **OWASP Top 10:2025** (note the new A03 supply chain and
A10 exceptional conditions) and **ASVS 5.0.0**; plus disaster-recovery, migration/upgrade and
fresh-install tests, SBOM and signed artifacts.
**Not:** Feature 40's full STRIDE enumeration — a one-page trust-boundary document is the v1.0
artifact, and it comes from
[Name the system trust boundaries](https://github.com/MCrank/ZWarden/issues/7).

---

## 7. Splits

Splitting happens in the mini-plan (PRD 59 already mandates it) **except** where a split changes
the DAG — that is sequencing, and belongs here.

**One split changes the DAG: F20.** F21 (mod discovery) needs configuration *reading and
modelling*; it does not need revisions, atomic apply, restore or drift detection. Leaving F20
whole would put the entire apply-and-revisions half — the part carrying the atomic-write
requirement and the second-author problem — ahead of mod discovery for no reason. Hence **F20a**
before F21, and **F20b** before F22.

**Two features are flagged as oversized without a split here**, because inventing their boundaries
now would be guessing at mini-plans that are explicitly out of scope:

- **F5 — Authorization.** If its mini-plan breaks the guardrail, split by slice (definitions and
  policy handlers, then enforcement surface). Not by tenant-vs-server scope.
- **F29 — Diagnostics Engine.** Ten diagnostic domains; split by domain group. Either way the
  DAG is unchanged, because every group has the same entry condition.

## 8. v1.1 — the SaaS residue

v1.1 adds only what hosting requires. Every seam it plugs into was built and tested in v1.0.

| Feature | Scope |
| --- | --- |
| **F3B** | Auth0 Organizations integration only: external subject mapping, organization-to-tenant mapping, login context validation. The abstractions it plugs into shipped in v1.0's F4. |
| **F3D** | Tenant administration: tenant creation, settings, membership management, invitations, membership removal, role assignment, tenant security settings, audit integration. |
| **F10A** | SaaS Agent enrollment and tenant-scoped connectivity: Agent-to-tenant binding, tenant-scoped SignalR, hosted enrollment credentials, enrollment audit events. |
| **F33A** | Hosted SaaS deployment: multi-tenant hosted deployment, PostgreSQL deployment, Auth0 configuration, tenant isolation verification, SaaS operational limits, platform administration, tenant-aware diagnostics, hosted HTTPS/WebSocket configuration. |
| **mTLS + Agent CA** | Moved here from F9 by ADR: certificate issuance, renewal, rotation, expiration, revocation, lost-Agent recovery (PRD 17's full list). |

Two standing constraints for v1.1, both already verified: **Auth0 Organizations is Free-tier but
Auth0 RBAC is not**, which is a second reason ZWarden remains authoritative for permissions
(PRD 7A already requires it); and **tenant identifiers and Auth0 organization ids are never
interchangeable** (PRD 63A).

PRD 63A's other mandatory controls — every request resolving a tenant context, every query
tenant-scoped, cross-tenant integration tests — are **v1.0 obligations**, not v1.1 ones. They are
satisfied by F3A and F5 and tested there. That is the whole point of the seams-first split.

## 9. Post-1.1 backlog

Deferred consciously, with the reason recorded. Not fog: each is a known thing we chose not to do.

| Feature | Why it is not v1.0 |
| --- | --- |
| **F23 — Mod Profiles** | Criterion 7 ("manage mods correctly") is satisfied by F21 + F22. |
| **F26 — Automated Backup Scheduling** | Criterion 10 says *create and restore*, not schedule. The one users will notice; F11's operation engine and F24's retention metadata make it a small later add. |
| **F31 — AI Troubleshooting Context** | Criterion 13 is satisfied by F30's sanitized support package. Cheapest of the five to pull forward, since F30 does the redaction work it depends on. |
| **F36 — Multi-Server Operational UX** | Criteria 3 and 4 are satisfied by F14's inventory and F16's health. Mutating bulk operations need safety design first (PRD 59 says so itself). |
| **F37 — Notifications** | No criterion names it. |
| **F38 — Native PZ Runtime** | PRD 59 defers it explicitly, so third-party variability does not dictate the architecture. |
| **F39 — Third-Party Docker Adoption** | Same reasoning; the canonical container stays preferred. |

Supply-chain CI tooling beyond F0's skeleton — container scanning, provenance — also lands later,
**except** SBOM and signed artifacts, which F40 requires as part of the gate.

## 10. ADRs owed before Feature 0 locks versions

These are the decisions in this document that a future reader would otherwise find surprising.
Most fall to [Write the technology-baseline ADRs](https://github.com/MCrank/ZWarden/issues/12).

1. **Agent authentication: enrollment credential in v1.0, mTLS in v1.1.** Overturns PRD 17. Must
   state the residual risk explicitly: credential theft on a compromised host is not mitigated by
   possession-of-key.
2. **SteamCMD-at-runtime is mandatory.** Overturns PRD 22's redistribution escape clause, which
   the PZ EULA closes. Carries the two-CI-tier consequence.
3. **Lua configuration handling.** `Loretta.CodeAnalysis.Lua` pinned, parse-only, behind
   `IPzConfigDocument`, with a ZWarden-side size and nesting pre-check; hand-rolling as the
   documented fallback. From
   [Choose how to read and write Project Zomboid Lua config files](https://github.com/MCrank/ZWarden/issues/15).
4. **Configuration revisions are value-level, not byte-level, and detect out-of-band edits.**
   Refines PRD 33 against measured server behaviour.
5. **Both database providers are supported in v1.0**, with the escape hatch if SQLite cannot meet
   PRD 21's locking.
6. **Identity hardening:** PBKDF2 iteration count set deliberately against OWASP's current figure
   rather than inherited; passkeys deferred and their schema version treated as a migration
   decision.
7. **F19's whitelist rescope**, reinterpreting PRD 36 against the shipped build.
8. **Component vocabulary:** three components, "Manager" retired, "control plane" reserved for the
   product.

The feature merges (F3C into F5, F3B's abstractions into F4) and F9's redrawn dependency need no
separate ADR — **this document is their record**.

## 11. Traceability: PRD 64 criterion to feature

| # | Criterion | Satisfied by |
| --- | --- | --- |
| 1 | Deploy the supported stack with minimal setup | F32, F33, F34 |
| 2 | Securely authenticate | F4 |
| 3 | Manage one or more servers | F14, F15 |
| 4 | Understand server health | F16 |
| 5 | Start, stop, restart servers | F15 |
| 6 | Update Project Zomboid | F17 |
| 7 | Manage mods correctly | F21, F22 (which pulls in F20a/F20b) |
| 8 | Administer players | F19 |
| 9 | Securely use RCON | F18, F28 |
| 10 | Create and restore backups | F24, F25 |
| 11 | Audit administrative activity | F6 |
| 12 | Diagnose common failures without SSH | F27, F29 |
| 13 | Generate safe troubleshooting information | F30 |
| 14 | Manage remote servers without privileged inbound ports | F9, F10, F35 |
| 15 | Operate without understanding the Docker architecture | F12, F13, F33, F34 |

Foundations F0, F1, F2, F3, F3A, F5, F7, F8, F11 carry no criterion of their own; every criterion
above depends on them. **Configuration management enters transitively**: F22 depends on F20b, so
criterion 7 requires it, and criterion 3 would be hollow without it.

## 12. Topological order for a single worker

```text
F0 · F12 · F1 · F2 · F3 · F3A · F4 · F5 · F6 · F7 · F8 · F9 · F10 · F11 · F13
F14 · F15 · F16 · F17 · F18 · F20a · F19 · F20b · F21 · F22 · F24 · F25 · F27
F28 · F29 · F30 · F32 · F33 · F34 · F35 · F40
```

**36 work items**, from PRD 59's 44 features: five merged or moved to v1.1, seven deferred to the
post-1.1 backlog, one split in two. F12 sits second because it can, and because it should.
