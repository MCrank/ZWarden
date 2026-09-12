# Feature 11 Mini-Plan — Durable Operations Engine

**Status:** in progress. Branch `feat/f11-durable-operations-engine`, **two PRs** closing
[F11 (#33)](https://github.com/MCrank/ZWarden/issues/33) — PR-A engine core, **PR-B** Web/Agent
dispatch + `Diagnostics.Ping` + end-to-end (closes #33). Track C. F6 ([#28](https://github.com/MCrank/ZWarden/issues/28))
and F10 ([#32](https://github.com/MCrank/ZWarden/issues/32)) are merged, so F11 is unblocked; it blocks
F13 ([#57](https://github.com/MCrank/ZWarden/issues/57)/#… Docker runtime), F15, F20b, F22, F24 —
five features build on this engine.

**Format:** PRD 60. **Written against:** PRD 20 (durable, idempotent operations), PRD 21 (one
conflicting mutating Operation per Server), PRD 2.2 (TDD), PRD 15 (architecture tests); Feature 11
(§ "Durable Operations Engine"); [`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (Track C,
F11) and §5/§12 (the DAG: F10 → **F11** → F13/F15/F20b/F22/F24); [ADR-0005](../adr/0005-both-database-providers-ship-in-v1-0.md)
(the spike that proved and prescribed the per-server lock, and deferred three items to this feature);
[ADR-0022](../adr/0022-operation-lifecycle-and-per-server-locking.md) (this feature's lifecycle,
timeout and cancellation contract); [ADR-0020](../adr/0020-agent-protocol-versioning-and-catalogue.md)
(the command/event contract the engine drives); [ADR-0019](../adr/0019-audit-is-append-only-tenant-owned-and-binds-the-auth-sink.md)
(audit); [ADR-0016](../adr/0016-tenant-isolation-query-filter-and-default-tenant.md) (tenant
ownership); [ADR-0014](../adr/0014-typed-id-pattern.md)/[ADR-0004](../adr/0004-typed-ids-are-stored-as-native-uuid.md)
(typed UUIDv7 ids); [`trust-boundaries.md`](../trust-boundaries.md) §3 (Web ↔ Agent, untrusted
Agent-reported data), §6 (tenant isolation), §9 rule 3 (no free-form command on an Agent-facing
contract). Four load-bearing decisions were settled with the maintainer before writing (below).

## Objective

Build the **durable engine that runs every mutating action against a Server as a tracked, resumable
Operation** rather than a fire-and-forget call. ZWarden.Web **enqueues** an Operation, **serializes**
it against a per-Server lock (PRD 21), **dispatches** the underlying `AgentCommand` down the F10
connection, **ingests** the Agent's `OperationProgress`/`OperationCompleted` events, drives the
Operation through its **state machine** (ADR-0022), **audits** every transition (F6), and survives a
Web restart and message redelivery (PRD 20 idempotency). Per-Server serialization is realized as the
**partial unique index** ADR-0005 spike-proved, and a **lease + reaper** releases the lock when an
Agent dies mid-work. F11 ships one real, non-mutating `Diagnostics.Ping` command so the whole loop is
exercisable in production the day it merges; the real mutating commands land with their owning
features (F13/F15). This is the machinery five later features ride on, so the lifecycle contract
(ADR-0022) matters more than any single operation.

## The settled decisions

1. **Per-server locking is the Operation row itself — a partial unique index, not a separate lock
   table (maintainer decision).** `UX_Operations_ActiveMutating_PerServer ON Operations(ServerId)
   WHERE IsMutating AND State IN ('Pending','Running')` — ADR-0005's verbatim-identical DDL. Acquire
   is the `INSERT` of the Operation (a conflicting mutating op throws `DbUpdateException` → typed
   "server busy"); release is the Operation reaching a terminal state (no delete). Chosen over a
   dedicated `ServerOperationLock` table because the lock **is** the operation — one atomic write, a
   DB-enforced invariant, and no orphan-lock bug class — and because a partial index already keeps
   the hot set tiny over a growing history table. The dedicated table stays a documented fallback if
   a lock-without-an-operation need ever arises (better modelled as Server state, F14+). Scales
   per-`ServerId`, which is the axis a v1.1 hosted deployment grows along. Full rationale + the
   scale analysis in ADR-0022.
2. **Ship a real, permanent `Diagnostics.Ping` `AgentCommand` (maintainer decision).** The engine
   must dispatch *something* and ingest the two Agent events to prove itself, and no mutating command
   exists until F13/F15. `Diagnostics.Ping` is a genuine troubleshooting primitive (CONTEXT.md:
   monitoring and troubleshooting), gives F11 a live operator-runnable Operation on merge, and proves
   the real discriminator/serialization/versioning round-trip — which a **test-only fake** cannot,
   because the polymorphic discriminator and the closed-vocabulary guard scan the Contracts assembly,
   not test assemblies. Ping is **non-mutating**, so it does not claim the per-server lock; the lock
   is proven with mutating **test** Operations and goes live the instant F13's first mutating command
   ships. This is the only new leaf in the closed command vocabulary.
3. **Delivered as two PRs on one branch, TDD slices (maintainer decision).** F11 sits at the ~100K
   guardrail, so the split is planned up front rather than discovered mid-build (as F9 split).
   **PR-A — engine core:** the `Operation` aggregate + state machine, persistence + both provider
   migrations, the per-server lock, the enqueue/idempotency/cancellation application seams, the lease
   + reaper — all testable with fakes, **no Web or Agent wiring**. **PR-B — dispatch + ping + e2e:**
   the Web outbound dispatch via `IHubContext<AgentHub>`, the hub receivers for
   `OperationProgress`/`OperationCompleted`, the `Diagnostics.Ping` command + Agent handler, and the
   end-to-end test; **PR-B closes #33.** Each PR is a sequence of red→green TDD slices/commits.
4. **A new ADR (0022) records the durable lifecycle contract (maintainer decision).** ADR-0005 proved
   the *lock*; the *lifecycle* — state machine, lease/reaper, the SQLite `CommandTimeout` value
   (30 s), and the cancellation contract — is foundational for five downstream features and
   hard-to-reverse, so it earns a durable decision record rather than living only in this feature
   plan. ADR-0005's three deferred items (CommandTimeout, the deferred-transaction arch test,
   lock-then-commit-then-work) are closed there.

Decisions taken without escalation (low-risk, pattern-matching existing work), recorded here as the
durable handoff:

- **`IsMutating` is set at enqueue from the command's declared mutating-ness, not derived from a
  closed `OperationKind` enum.** This keeps the lock testable before any mutating command exists
  (ADR-0022 alternatives). Read-only Operations (`Diagnostics.Ping`) sit outside the index and never
  contend.
- **Lease expiry, not a distinct `TimedOut` state.** A reaper fails an over-lease `Running` Operation
  with a non-secret reason; downstream features match on `Failed` (ADR-0022).
- **The concurrency token is the existing `IVersioned.Version`** (portable `Guid`,
  `.IsConcurrencyToken()`) — the takeover mechanism ADR-0005 mandates. No `IsRowVersion`, no `xmin`.
- **Agent-reported `OperationProgress.StatusLine` is untrusted display text** (trust-boundaries §3):
  stored, length-bounded, never interpreted, never used in a control-flow decision.
- **The `OperationReaper` is a hosted service** on the F8/F10 hosted-service pattern; it runs on
  ZWarden.Web (the single writing process, ADR-0005 condition 4).
- **No new permission for F11's own machinery.** Enqueue is an application seam later features call
  under *their* permission; the one operator-facing surface F11 adds — issuing a `Diagnostics.Ping`
  and reading an Operation's state — is gated by an existing permission (no new catalogue entry, so
  `PermissionCatalogueTests`/`BuiltInRolesTests` stay green). Revisit only if review finds no existing
  permission fits.

## Dependencies

**F6 + F10** (issue-declared), plus F1/F2/F3/F3A/F5/F7/F8 transitively. Consumes:

- **F10 connection** — `IAgentConnectionRegistry.GetConnectionId(agentId)` to resolve the live
  connection (the seam F10 built "for F11 to dispatch through"), then
  `IHubContext<AgentHub>.Clients.Client(connectionId).SendAsync(...)` an `Envelope<AgentCommand>`.
  F11 **adds** the outbound send (greenfield — F10 was inbound-only) and the hub receivers for the
  two operation events. A command for an offline Agent leaves the Operation `Pending` until the Agent
  reconnects (queue), bounded by an enqueue timeout.
- **F7 contracts** — `Envelope<T>` (with its pre-built `OperationId?` routing field),
  `AgentCommand`/`AgentEvent` roots, the pre-built `OperationProgress`/`OperationCompleted` events
  and `OperationOutcome` enum, `ProtocolJson`, `ProtocolCompatibility`. F11 adds one `AgentCommand`
  leaf (`Diagnostics.Ping`) — the closed-vocabulary/discriminator guards gain exactly one expected
  entry.
- **F6 audit** — every lifecycle transition writes an `AuditEntry` via `IAuditWriter`
  (`Operation.Enqueued`, `Operation.Started`, `Operation.Succeeded`, `Operation.Failed`,
  `Operation.Cancelled`), correlation id from `ICorrelationContext`, tenant + occurred-at stamped by
  the interceptor. No secret in any record.
- **F3A tenant** — `Operation` is `ITenantOwned`; reads go through a `TenantScopedRepository<Operation>`
  (never `IgnoreQueryFilters`). Agent-originated `OperationProgress`/`OperationCompleted` arrive with
  **no browser session**, so ingest runs under `ClaimsPrincipalTenantContext`'s default-tenant
  fallback (the documented F9/F10 reliance) and resolves the Operation by `OperationId`, re-checking
  its tenant against the resolved record before mutating.
- **F2 persistence** — `Operation` is `ITenantOwned` + `IVersioned`, so the model-wide conventions
  (typed-id converters, `Version` concurrency token, tenant filter) apply with only an
  `IEntityTypeConfiguration`; a paired **Sqlite + Postgres** migration adds the table + the partial
  index. The engine runs at SQLite `CommandTimeout = 30 s` (ADR-0005 condition 2).
- **F1 ids** — `OperationId` (`op-`) already exists; no new prefix (registry stays at 20).

## Scope

1. **Domain — the `Operation` aggregate** (`ZWarden.Domain/Operations/`). `Operation : ITenantOwned,
   IVersioned` with `OperationId Id`, `ServerId ServerId`, `OperationKind Kind` (enum, stored by
   name — `DiagnosticsPing` for now), `bool IsMutating`, `OperationState State` (enum:
   `Pending`/`Running`/`Cancelling`/`Succeeded`/`Failed`/`Cancelled`), `string IdempotencyKey`,
   `int PercentComplete`, `string? StatusLine`, `string? FailureReason`, timestamps (`EnqueuedAt`,
   `StartedAt?`, `CompletedAt?`, `LeaseExpiresAt?`, `LastProgressAt?`). Static factory `Enqueue(...)`
   (tenant left for the interceptor); invariant-guarded mutators taking `DateTimeOffset now`:
   `MarkDispatched(lease, now)`, `ReportProgress(pct, statusLine, lease, now)`, `Succeed(now)`,
   `Fail(reason, now)`, `RequestCancel(now)`, `Cancel(now)`. Illegal transitions throw a typed
   `InvalidOperationStateTransition`. No I/O.
2. **Application — the engine seams.** `IOperationCoordinator` (`EnqueueAsync(request, ct)` →
   idempotent create + lock acquire, returns the Operation or the existing one on a duplicate key;
   `RequestCancellationAsync(operationId, actor, ct)`); an `IOperationDispatcher` seam (Web
   implements it in PR-B) the coordinator calls to hand a dispatchable Operation to the transport;
   `IOperationStore`/`TenantScopedRepository<Operation>` for reads and the ingest path
   (`ApplyProgressAsync`, `ApplyCompletionAsync` keyed by `OperationId`). `OperationAuditActions`
   constants. A typed `ServerBusyException`/result for the lock conflict.
3. **Infrastructure — persistence, lock, reaper.** `OperationConfiguration : IEntityTypeConfiguration
   <Operation>` (`ToTable`, enums `.HasConversion<string>().HasMaxLength(32)`, the partial unique
   index via `HasIndex(o => o.ServerId).IsUnique().HasFilter("...")` with the ADR-0005 predicate, the
   `(TenantId, IdempotencyKey)` unique index); the coordinator implementation (INSERT-to-acquire with
   the **one** provider-specific inner-exception switch → `ServerBusyException`); `OperationReaper`
   hosted service (fails over-lease `Running`/`Cancelling` via optimistic `Version`); the paired
   `Sqlite` + `Postgres` migration; `AddZWardenOperations()` DI (after `AddZWardenEnrollment`),
   registering `TimeProvider`, the coordinator, store, reaper, options (lease duration, reaper
   interval, enqueue timeout, SQLite `CommandTimeout = 30 s`).
4. **Contracts — one command leaf (PR-B).** `[ProtocolMessage("diagnostics.ping")] sealed record
   PingAgent() : AgentCommand`. The closed-vocabulary, discriminator-uniqueness and message-sealed
   guards gain one expected entry. No free-form command; no other contract change (the operation
   events + envelope field are pre-built).
5. **Web — dispatch + ingest + the operator surface (PR-B).** `IOperationDispatcher` implementation:
   resolve `connectionId` from `IAgentConnectionRegistry`, `SendAsync` the `Envelope<AgentCommand>`
   (carrying `OperationId`), mark the Operation `Running` with a lease; on no live connection, leave
   `Pending`. `AgentHub` receivers `OperationProgress(Envelope<OperationProgress>)` and
   `OperationCompleted(Envelope<OperationCompleted>)` → `IOperationStore.ApplyProgressAsync` /
   `ApplyCompletionAsync` (default-tenant fallback, resolve by `OperationId`, untrusted `StatusLine`).
   A minimal operator-facing entry point to issue a `Diagnostics.Ping` against a Server and read the
   Operation's state (gated by an existing permission). DI wiring.
6. **Agent — the ping handler (PR-B).** On receiving `Envelope<PingAgent>`, the Agent replies
   `OperationProgress`(optional) then `OperationCompleted(Succeeded)` on the same `OperationId`,
   deduping a redelivered `OperationId` (execution idempotency, PRD 20). No Docker, no persistence
   (trust-boundaries §9 rule 1 — Agent stays file + BCL + transport).
7. **Tests** — the state machine + invariants; enqueue idempotency; the **per-server lock** (two
   mutating test Operations, same Server → second is "server busy"; different Servers → both
   succeed; read-only → no contention) on **both providers**; the concurrency-token takeover; the
   reaper; the audit trail; the discriminator round-trip of `PingAgent`; the dispatch (offline vs
   online Agent); the ingest of both events (including untrusted `StatusLine`); the Agent ping
   handler + replay dedupe; the full **enqueue → dispatch → progress → complete** end-to-end; and the
   architecture guards (deferred-transaction, tenant filter, closed vocabulary, Agent boundary).

## Non-scope

- **The real mutating commands (F13 `RestartServer`, F15 lifecycle, F20b `ApplyConfig`, F22 mods,
  F24 backup).** F11 ships only `Diagnostics.Ping`; every mutating command lands with its owning
  feature and flips `IsMutating` on, at which point the already-proven per-server lock goes live.
- **Scheduling / recurring operations (post-1.1, F26).** F11 runs an Operation now; it does not
  time-trigger one.
- **The rich operator operations UI / history viewer (F14/F16).** F11 adds only the minimal surface
  to issue a ping and read a state; the dashboard, filtering and history views are later.
- **Operations-table retention / partitioning.** The table grows unboundedly; the lock check never
  scans it (partial index). Retention is a hosted-scale concern deferred to F34-era packaging
  (ADR-0022).
- **`synchronous=FULL` durability tuning.** ADR-0005's open crash-durability edge; belongs to the
  packaging feature, not the engine.
- **Multi-instance dispatch routing.** Single Web instance in v1.0 (ADR-0005 condition 4); a command
  dispatched from the wrong instance under a future scale-out needs the v1.1 backplane behind the
  same `IAgentConnectionRegistry` seam (documented in F10's plan). The persisted Operation state is
  correct cross-instance regardless.

## Domain changes

New `Operations/` folder: the `Operation` aggregate, `OperationState`, `OperationKind` enums, the
`InvalidOperationStateTransition` guard type. **No new typed ID** (`OperationId`/`op-` exists; prefix
registry stays at 20; `PrefixRegistryTests` unchanged). **No new permission**
(`PermissionCatalogueTests`/`BuiltInRolesTests` unchanged). Domain-test floor bumped for the new
aggregate.

## Contract changes

One `AgentCommand` leaf — `[ProtocolMessage("diagnostics.ping")] PingAgent` (PR-B) — the first
concrete command in the closed vocabulary; the closed-vocabulary, discriminator-uniqueness and
sealed-message guards gain exactly one expected entry. The pre-built `OperationProgress`,
`OperationCompleted`, `OperationOutcome` and the envelope `OperationId?` field are otherwise
untouched. No free-form command string (trust-boundaries §9 rule 3).

## Package changes

None expected — persistence, SignalR server and hosted services are all in the existing framework /
already-referenced packages. Any incidental add is pinned in `Directory.Packages.props` on the
**pinned SDK (10.0.401)** with lock files regenerated and committed (toolchain note; watch the
`packages.lock` AspNetCore-assets drift).

## Security considerations

- **Tenant isolation holds on both paths.** Operator-initiated enqueue runs under the session tenant
  through the query filter; Agent-initiated ingest runs under the default-tenant fallback (no browser
  session) and re-checks the resolved Operation's tenant before mutating — never `IgnoreQueryFilters`
  (trust-boundaries §6).
- **Agent-reported data is untrusted** (trust-boundaries §3). `OperationProgress.StatusLine` and
  `OperationCompleted.FailureReason` are stored length-bounded, never interpreted, never drive
  control flow; `PercentComplete` is clamped `0..100`. A compromised Agent can lie about *its own*
  operation's progress but cannot reach another tenant's Operation (resolved + tenant-checked by id)
  or the database beyond the ingest seam (rule 1).
- **The lock is fail-safe.** The per-server invariant is DB-enforced (PRD 21); a lease + reaper
  guarantees a dead Agent's lock is released, so a Server can never be wedged permanently by a lost
  Operation. Cancellation is honest (cooperative), never a false claim of stopping work.
- **The SQLite trap stays unreachable** — the deferred-transaction arch test (ADR-0005 condition 3)
  fails the build if any engine code reaches `BeginTransaction(deferred: true)` or raw ADO; the
  engine claims-commits-then-works, holding no transaction across Agent work (condition 5).
- **The closed vocabulary is preserved** — one sealed, discriminated `PingAgent` leaf, no free-form
  command; the guard gains one expected entry and stays otherwise green.
- **Full audit trail** — every transition is an `Operation.*` Audit event with a correlation id and
  no secret, so an operator reconstructs an Operation's whole life in the F6 viewer.

## Test plan

Written before the code (PRD 2.2). Assemblies noted; offline tier unless marked networked.

1. **Domain — the state machine** (`ZWarden.Domain.Tests`). Every legal transition sets the right
   state + timestamps; every illegal one throws `InvalidOperationStateTransition`; terminal states
   are immutable; `ReportProgress` clamps and extends the lease; `RequestCancel` on `Pending` → the
   cancel path, on `Running` → `Cancelling`.
2. **Enqueue idempotency** (`ZWarden.Infrastructure.Tests`). A second enqueue with the same
   `(TenantId, IdempotencyKey)` returns the **existing** Operation, creates no second row.
3. **Per-server lock** (SQLite: `ZWarden.Infrastructure.Tests`; **Postgres**:
   `ZWarden.IntegrationTests`, networked). Two mutating Operations for the same Server → the second
   is `ServerBusyException`; for different Servers → both acquire; any number of read-only Operations
   on one Server → all acquire. Asserts `DbUpdateException` only (the inner-exception switch is tested
   once, per provider). No provider branch in the suite body.
4. **Concurrency-token takeover** (`ZWarden.IntegrationTests`, networked + SQLite). Two contexts, a
   stale transition → `DbUpdateConcurrencyException`; the reaper's optimistic update wins against a
   fresh read.
5. **Reaper** (`ZWarden.Infrastructure.Tests`). A `Running` Operation past `LeaseExpiresAt` → `Failed`
   with the lease-expiry reason and the per-server slot freed; a within-lease one is untouched; a
   `Cancelling` past lease → `Failed`.
6. **Audit trail** (`ZWarden.Infrastructure.Tests`). Enqueue/start/succeed/fail/cancel each write the
   matching `Operation.*` Audit event with correlation id and no secret.
7. **`PingAgent` discriminator round-trip** (`ZWarden.Contracts.Tests`). Serializes/deserializes
   through `ProtocolJson` with the `diagnostics.ping` discriminator inside an `Envelope` carrying an
   `OperationId`; the closed-vocabulary/uniqueness guards see exactly one command leaf.
8. **Dispatch** (`ZWarden.Web.Tests`). An online Agent → the command is sent to the resolved
   `connectionId` and the Operation goes `Running` with a lease; an offline Agent → the Operation
   stays `Pending`, nothing sent.
9. **Ingest** (`ZWarden.Web.Tests`). `OperationProgress` advances `PercentComplete`/`StatusLine`
   (clamped, untrusted) + extends the lease; `OperationCompleted(Succeeded)` → `Succeeded`,
   `(Failed)` → `Failed` with the reason; an event for an unknown/mismatched-tenant `OperationId` is
   rejected without mutating.
10. **Agent ping handler + replay** (`ZWarden.Agent.Tests`). On `Envelope<PingAgent>` the Agent
    replies `OperationCompleted(Succeeded)` on the same `OperationId`; a redelivered `OperationId` is
    deduped (runs once).
11. **End-to-end** (`ZWarden.IntegrationTests` or a Web+Agent seam test). Operator issues a
    `Diagnostics.Ping` against a Server whose Agent is connected → an `op-…` is created, dispatched,
    the Agent replies, the Operation reaches `Succeeded`, and the audit trail shows the full
    lifecycle.
12. **Architecture** (`ZWarden.ArchitectureTests`). `DeferredTransactionGuardTests` extended to the
    operations engine (no `deferred: true`, no raw-ADO transaction); tenant filter present on
    `Operation` (no `IgnoreQueryFilters`); closed command vocabulary = one leaf; Agent references no
    Infrastructure/EF/Docker; prefix registry (20) and permission catalogue unchanged.

## Implementation slices

Two PRs on branch `feat/f11-durable-operations-engine`:

**PR-A — engine core** (no Web/Agent wiring; everything testable with fakes):

- **S0 — mini-plan + ADR 0022.** This document + `docs/adr/0022-…`. *(this commit)*
- **S1 — Domain.** The `Operation` aggregate, `OperationState`/`OperationKind`, the transition guard.
  *Verify:* test 1. Domain.Tests floor bumped.
- **S2 — Application seams.** `IOperationCoordinator`, `IOperationDispatcher`, `IOperationStore`,
  `OperationAuditActions`, `ServerBusyException`, the enqueue/cancel request/ingest DTOs. *Verify:*
  compile + seam unit tests.
- **S3 — Persistence + lock + migration.** `OperationConfiguration` (partial index + idempotency
  index), the coordinator implementation (INSERT-to-acquire + the one inner-exception switch), the
  paired `Sqlite` + `Postgres` migration (built, never `--no-build`), `AddZWardenOperations()` +
  options (incl. SQLite `CommandTimeout = 30 s`). *Verify:* tests 2, 3, 4 (SQLite offline; Postgres
  on the networked tier before merge).
- **S4 — Reaper + audit + arch guard.** `OperationReaper` hosted service; the `Operation.*` audit
  writes; extend `DeferredTransactionGuardTests` to the engine. *Verify:* tests 5, 6, 12 (the engine
  subset). **→ open PR-A.**

**PR-B — dispatch + ping + end-to-end** (closes #33):

- **S5 — Contracts + Web dispatch/ingest.** `PingAgent` leaf; `IOperationDispatcher` Web impl
  (registry + `IHubContext<AgentHub>`); hub receivers for the two operation events; DI/wiring.
  *Verify:* tests 7, 8, 9.
- **S6 — Agent handler + operator surface + e2e + docs.** The Agent `PingAgent` handler + replay
  dedupe; the minimal operator entry point to issue a ping and read state; the end-to-end test;
  CONTRIBUTING + this plan's status + the progress memory; the full arch guard. *Verify:* tests 10,
  11, 12; full offline tier + arch green; floors set on every touched project. **→ open PR-B, close
  #33.**

## Diagnostics

- **Every lifecycle transition is audited** (`Operation.Enqueued/Started/Succeeded/Failed/Cancelled`)
  with a correlation id and no secret, so an operator reconstructs any Operation's whole life in the
  F6 viewer.
- **A "server busy" refusal is a typed result**, not a swallowed exception — the operator sees that a
  conflicting Operation already holds the Server, not a generic failure.
- **A lease expiry names why** (`"lease expired — agent did not report completion"`), pointing at a
  dead/unresponsive Agent rather than the operation itself.
- **`Diagnostics.Ping` is itself a diagnostic** — the first tool to answer "is this Agent actually
  round-tripping commands right now?", feeding F16 health and support packages.
- **Structured engine logging** (Serilog, ADR-0021) at each transition and on dispatch/reap, never
  logging Agent-supplied `StatusLine` as trusted.

## Documentation

- **`CONTEXT.md`** — the **Operation** term is already defined; add a clarification only if review
  finds `OperationState`/`IsMutating`/lease terms ambiguous. No prefix-table change.
- **CONTRIBUTING** — an "Operations Engine (Feature 11)" section: the state machine, the per-server
  lock, idempotency keys, the reaper, and `Diagnostics.Ping` as the smoke-test operation.
- **ADR-0022** — the durable lifecycle/lock/timeout/cancellation record (this commit); ADR-0005's
  three deferred items are closed there.

## Acceptance criteria

1. A mutating Operation against a Server **serializes** — a second conflicting mutating Operation is
   refused with a typed "server busy" while the first is `Pending`/`Running`; different Servers and
   read-only Operations never contend; the invariant is DB-enforced on **both** providers (PRD 21).
2. An Operation is **durable and idempotent** — it survives a Web restart, a duplicate enqueue
   (same idempotency key) returns the existing Operation, and a redelivered command runs once on the
   Agent (PRD 20).
3. An Operation moves through the **ADR-0022 state machine** — `Pending → Running → (Succeeded |
   Failed | Cancelled)`, `Cancelling` transient — driven by dispatch and the Agent's
   `OperationProgress`/`OperationCompleted`; illegal transitions are impossible; terminal states are
   immutable.
4. A **dead Agent never wedges a Server** — a `Running` Operation past its lease is reaped to
   `Failed` and the per-server lock is released.
5. **Cancellation** takes a `Pending` Operation to `Cancelled` immediately and a `Running` one
   through `Cancelling` cooperatively; every transition is **audited** with no secret.
6. `Diagnostics.Ping` runs **end-to-end** against a connected Agent (enqueue → dispatch → reply →
   `Succeeded`); the closed command vocabulary is exactly one sealed leaf, **no free-form command**;
   both provider migrations exist and the model matches; the deferred-transaction arch guard covers
   the engine; the offline tier is green with no warnings (Postgres verified on the networked tier
   before merge); the closed-set guards (prefix registry at 20, permission catalogue, built-in roles)
   are unchanged and green.

## Definition of Done

Per PRD 61, the applicable subset: acceptance criteria met; tests authored first as executable
specifications; state-machine + idempotency + per-server-lock (both providers) + concurrency-takeover
+ reaper + audit + `PingAgent` round-trip + dispatch + ingest + Agent-handler + end-to-end tests pass;
**architecture rules pass** (deferred-transaction guard covers the engine; tenant filter on
`Operation`, no `IgnoreQueryFilters`; closed vocabulary = one leaf; Agent = Contracts + transport +
BCL, no Infrastructure/EF/Docker); every lifecycle transition **audited** with no secret; error
conditions modelled and typed ("server busy", lease-expiry reason, unknown-Operation ingest refused);
diagnostics exist (the `Operation.*` audit events + structured engine logging); both provider
migrations generated on the pinned SDK with lock files committed; ADR-0022 + CONTRIBUTING documented;
CI green; no unresolved warnings; **both PRs merged and #33 closed on PR-B**.
