# Feature 2 Mini-Plan — Persistence Foundation

**Status:** ready for implementation. Roadmap issue: [F2 (#23)](https://github.com/MCrank/ZWarden/issues/23). Track A.

**Format:** PRD 60. **Written against:** PRD §9 (Database Architecture), §21 (Server Operation Concurrency), ADR [0004](../adr/0004-typed-ids-are-stored-as-native-uuid.md) (native-UUID storage), ADR [0005](../adr/0005-both-database-providers-ship-in-v1-0.md) (both providers, the five SQLite conditions), the architecture rules ([`trust-boundaries.md`](../trust-boundaries.md) §7/§9), and F1's typed IDs + converters. The five decisions below were settled in the F2 grilling.

## Objective

Deliver the persistence foundation: **one EF Core model that runs unchanged on SQLite and PostgreSQL**, with the typed-ID conversion applied automatically, a portable optimistic-concurrency token, the five SQLite operating conditions enforced, a per-provider migration strategy that applies on startup, and a dual-provider integration test suite — so later features add entities via configuration and get both providers, concurrency and migrations for free.

## Dependencies

- **F1** — the typed IDs and `TypedIdValueConverters.For<T>()` (applied here by convention).
- **F0** — the CI tiers and the shared PostgreSQL Testcontainer fixture.
- ADR 0004/0005 — native-UUID storage; `Guid Version` + `.IsConcurrencyToken()` + assign-on-write (not `IsRowVersion`); the five conditions; the lock-is-a-row design (F11 builds the engine).

## Scope

1. **The `ZWardenDbContext` (Q4)** — extensible: it discovers `IEntityTypeConfiguration` implementations so later features register their entities without editing the context. No production entities yet.
2. **Provider selection (Q1)** — config-driven (`ZW_DB_PROVIDER` = `sqlite` | `postgres`, plus a connection string). A `DbContextOptions` factory wires the chosen provider and its `MigrationsAssembly`.
3. **SQLite connection hardening (Q1, ADR 0005 condition 1)** — a connection interceptor issuing `PRAGMA journal_mode=WAL; busy_timeout=5000; foreign_keys=ON; synchronous=NORMAL;` on open, and setting **`CommandTimeout=30`** (the value F0 carried here; never 0 — condition 2). `synchronous=FULL` is documented as the durability lever, deferred to F40.
4. **Typed-ID convention (Q2)** — a model convention that reflects over each entity's `ITypedId` properties and applies `TypedIdValueConverters.For<T>()`, so ADR 0004's `HasConversion` is automatic and never hand-written per property.
5. **Optimistic concurrency (Q2, ADR 0005)** — an `IVersioned { Guid Version }` marker (in `ZWarden.Domain`, EF-free) applied as `.IsConcurrencyToken()`, and a **`SaveChanges` interceptor** that stamps a fresh `Guid` on every insert and update (identical on both providers).
6. **Migration strategy + execution (Q3)** — per-provider migration sets (`Migrations/Sqlite`, `Migrations/Postgres`) selected at runtime; a **migrate-on-startup runner** (idempotent, opt-out flag); `dotnet ef` drives generation in dev.
7. **Dual-provider tests (Q5)** — a shared abstract `CorePersistenceSuite` (+ concurrency-conflict suite) in a test-support library; a **SQLite concrete assembly in the offline tier** (every PR) and a **PostgreSQL concrete assembly in the networked tier** (scheduled, reusing F0's shared Testcontainer). No provider branch in the tests.
8. **The deferred-transaction guard (Q1, ADR 0005 condition 3)** — an architecture test forbidding `BeginTransaction(deferred:)` / raw-ADO transactions near the context (the one route to SQLite's unreachable `SQLITE_BUSY_SNAPSHOT` trap).

## Non-scope

- **Production entities** — `Tenant` (F3A), `User` (F4), `Server` (F14), … F2 proves the infrastructure with a **test-only entity**; the first production migration lands with F3A.
- **The Operations engine and the per-Server lock table** (F11) — F2 delivers the reusable concurrency *token*; F11 builds the lock row and the partial unique index on it.
- **The `CommandTimeout` number** — fixed at 30 here (F0's decision); not re-opened.
- **`synchronous=FULL` / crash-durability final call** — deferred to F40 (ADR 0005's open item).
- **Provider-specific query features** (PRD 9) — forbidden without justification.
- **The insert-conflict inner-exception provider switch** — F2's shared suite asserts `DbUpdateException`/`DbUpdateConcurrencyException` and nothing provider-specific; the one switch (SqliteException 1555 vs PostgresException 23505) is F11's when it inspects the inner exception.
- **Scale-out** — one writing process (ADR 0005 condition 4); multi-writer is a PostgreSQL-only v1.1 concern.

## Domain changes

- **`IVersioned { Guid Version }`** in `ZWarden.Domain` (a pure marker; the stamping lives in Infrastructure so Domain stays EF-free — arch rule 2).
- **Glossary:** add **Optimistic concurrency** (a `Guid Version` token stamped on every write; a stale write raises a conflict) if not already implied. No implementation detail.
- No entities; the model is empty of production types.

## Contract changes

- **The persistence extension point:** later features contribute `IEntityTypeConfiguration<TEntity>`; the context discovers them. The typed-ID and concurrency conventions apply automatically.
- **The concurrency contract:** any `IVersioned` entity gets an assign-on-write `Guid Version` and optimistic-concurrency checking; a stale update throws `DbUpdateConcurrencyException` on both providers.

## Security considerations

- **Database reachability** (trust-boundaries §7): only `ZWarden.Web` reaches the database; `ZWarden.Agent` holds no connection string. F2 does not open that boundary (it is a deployment/network control, F34); it must not create a path that leaks credentials.
- **No secrets committed** — connection strings come from configuration/secrets, never source; tests use ephemeral SQLite files and the Testcontainer.
- **SQLite on shared volumes is unsafe** (ADR 0005) — documented; F34's packaging must keep the file on a local volume.
- **The deferred-transaction arch test** is a security-adjacent correctness guard: it prevents re-introducing the one code path that breaks SQLite's locking under concurrency.

## Test plan

Written before the code (PRD 2.2). Unit tests in the offline tier; integration split by provider (Q5).

1. **Provider selection** — the options factory wires SQLite for `sqlite` and Npgsql for `postgres`; an unknown value fails fast.
2. **SQLite hardening** — after opening a connection through the interceptor, `PRAGMA journal_mode` is `wal`, `foreign_keys` is on, `busy_timeout` is 5000, and `CommandTimeout` is 30. (offline)
3. **Version stamping** — inserting an `IVersioned` entity assigns a non-empty `Version`; updating it changes `Version`. (offline, in-memory/SQLite)
4. **Typed-ID convention** — a test entity with a typed-ID key round-trips through a real SQLite database as the native representation and reads back equal. (offline)
5. **Optimistic concurrency conflict** — two contexts load the same row; the second `SaveChanges` after the first committed throws `DbUpdateConcurrencyException`. Runs on **both** providers via the shared suite.
6. **Migration applies** — the migrate-on-startup runner brings an empty database to the current schema on **both** providers, idempotently (running twice is a no-op).
7. **`CorePersistenceSuite`** — insert / query / update / delete of the test entity, green on SQLite (offline) and PostgreSQL (networked), with no provider branch.
8. **Deferred-transaction guard** — the architecture test fails the build if `BeginTransaction(deferred:` or a raw-ADO transaction appears in the persistence code.

## Implementation slices

- **S1 — Provider config + DbContext skeleton + conventions.** `ZWardenDbContext`, the options factory, `IEntityTypeConfiguration` discovery, the typed-ID convention. *Verify:* tests 1, 4 (SQLite).
- **S2 — Interceptors.** The SQLite connection interceptor (pragmas + CommandTimeout) and the version-stamping `SaveChanges` interceptor; `IVersioned`. *Verify:* tests 2, 3.
- **S3 — Migration strategy + runner.** Per-provider migration folders, the runtime `MigrationsAssembly` selection, the idempotent migrate-on-startup runner. *Verify:* test 6 (both providers, in the integration tiers).
- **S4 — Test-support library + abstract suites + test entity.** `CorePersistenceSuite` and the concurrency-conflict suite over a test-only `IVersioned` entity with a typed-ID key. *Verify:* compiles; suites are provider-agnostic.
- **S5 — SQLite integration (offline tier).** A concrete SQLite assembly running the suites against a temp-file database. *Verify:* tests 5, 7 on SQLite, in the offline CI tier.
- **S6 — PostgreSQL integration (networked tier) + CI.** A concrete assembly reusing F0's shared Testcontainer; wire it into the scheduled networked tier. *Verify:* tests 5, 7 on PostgreSQL.
- **S7 — The deferred-transaction arch test.** *Verify:* test 8 red when a deferred transaction is introduced.
- **S8 — Docs.** `CONTRIBUTING`/`CONTEXT` notes: the provider config, the SQLite conditions, how a feature adds an entity, and `dotnet ef` per-provider migration generation.

## Diagnostics

- **Concurrency conflicts** surface as `DbUpdateConcurrencyException` — an actionable, provider-neutral signal the operations engine (F11) turns into a retry/lost-update decision.
- **Migration failures** stop startup with the failing migration named; the runner logs the applied set.
- **SQLite misconfiguration** is catchable: a probe query for `PRAGMA journal_mode` in diagnostics (F29 later) reveals a database opened without the interceptor.
- **The deferred-transaction trap** cannot be reached silently — the arch test makes it a build error.

## Documentation

- `CONTRIBUTING.md` — the `ZW_DB_PROVIDER` config, the five SQLite conditions in one place, the entity-configuration recipe, and `dotnet ef migrations add … --output-dir Migrations/<Provider>` per provider.
- `CONTEXT.md` — the **Optimistic concurrency** term if it sharpens the model.
- **No new ADR** — ADR 0004 and 0005 already record the load-bearing decisions; F2 is implementation within them. The migration-strategy/test-topology choices are recorded here.

## Acceptance criteria

1. `ZWardenDbContext` builds on both providers from config; an unknown provider fails fast.
2. Every SQLite connection is opened WAL + `busy_timeout=5000` + `foreign_keys=ON` + `synchronous=NORMAL`, `CommandTimeout=30`.
3. Typed-ID properties persist as the native representation and round-trip, with no hand-written `HasConversion`.
4. `IVersioned.Version` is stamped on insert and update; a stale update throws `DbUpdateConcurrencyException` on **both** providers.
5. The migrate-on-startup runner is idempotent on both providers.
6. `CorePersistenceSuite` is green on SQLite in the offline tier (every PR) and on PostgreSQL in the networked tier (scheduled); no provider branch in the tests.
7. `ZWarden.Domain` references no EF Core; the deferred-transaction arch test passes and would fail on a violation; the offline tier is green.

## Definition of Done

Per PRD 61, the applicable subset: acceptance criteria met; tests authored first; unit + SQLite integration tests pass in the offline tier and PostgreSQL in the networked tier; **architecture rules pass** (Domain EF-free; no deferred transaction); **SQLite and PostgreSQL tests pass**; error/concurrency conditions modelled and diagnosable; migrations complete and idempotent; documentation updated; CI green; no unresolved warnings. (Authorization, audit, and production entities are N/A at F2 — it is infrastructure proven against a test entity.)
