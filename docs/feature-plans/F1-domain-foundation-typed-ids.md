# Feature 1 Mini-Plan — Domain Foundation and Typed UUIDv7 IDs

**Status:** ready for implementation. Roadmap issue: [F1 (#22)](https://github.com/MCrank/ZWarden/issues/22). Track A.

**Format:** PRD 60. **Written against:** PRD §6 (Persistent Identifier Standard), §7/§7A (Canonical Prefixes), §8 (ID Representation), ADR [0004](../adr/0004-typed-ids-are-stored-as-native-uuid.md) (native-UUID storage) and [0014](../adr/0014-typed-id-pattern.md) (the ID pattern), the architecture rules ([`trust-boundaries.md`](../trust-boundaries.md) §9). The five decisions below were settled in the F1 mini-plan grilling.

## Objective

Deliver the domain's **strongly-typed, prefixed UUIDv7 identifier** foundation: a set of mutually non-interchangeable ID value types (`UserId` ≠ `AgentId`), each carrying a UUIDv7 and a canonical `<prefix>-<uuid>` public form, with generation, parsing, formatting, validation, JSON serialization, an enforced prefix registry, and the EF conversion strategy — so every later feature builds entities on IDs that cannot be confused and never leak a raw GUID.

## Dependencies

**F0** only. Consumes:

- PRD §6 — UUIDv7, `<prefix>-<uuid>`, raw UUIDs never exposed to users/logs/APIs/diagnostics.
- PRD §7/§7A — the 19 canonical prefixes (18 in §7 + `ten-` in §7A).
- PRD §8 — strongly typed, non-interchangeable, prefix-validated parse; native-UUID **or** text storage (settled by ADR 0004: native).
- ADR 0004 — store the native `uuid`; app-side `Guid.CreateVersion7()`; **no documented monotonicity**; converted properties are single-column and can't be raw-SQL parameters.

## Scope

1. **The ID pattern (Q1, ADR 0014)** — a static-abstract interface `ITypedId<TSelf>` supplying each type's `Prefix` and a `FromGuid` factory, plus a non-generic `ITypedId` marker so converter *factories* can discover every type. All behaviour lives once in a generic static helper (`TypedId`), keyed on the interface.
2. **The 19 ID types (Q2)** — one `readonly record struct` per canonical prefix: `TenantId (ten-)`, `UserId (usr-)`, `RoleId (rol-)`, `AgentId (agt-)`, `ServerId (srv-)`, `OperationId (op-)`, `AuditEventId (aud-)`, `BackupId (bkp-)`, `DiagnosticPackageId (diag-)`, `EnrollmentId (enr-)`, `ConfigurationRevisionId (cfg-)`, `ModId (mod-)`, `WorkshopItemId (wsi-)`, `ModProfileId (mdp-)`, `BanRecordId (ban-)`, `PlayerRecordId (ply-)`, `PermissionAssignmentId (prm-)`, `CertificateRecordId (crt-)`, `NotificationId (ntf-)`. (That is the 18 of §7 + `ten-` = 19 — the registry test is the guard on the exact set.)
3. **Generation (Q3)** — `New()` → `Guid.CreateVersion7()`. `default(T)` (Guid.Empty) is the "unset" sentinel; `New()` never returns it; `IsEmpty` exposed.
4. **Parse / format / validate (Q4)** — canonical `ToString()` = lowercase `<prefix>-<uuid>` (hyphenated "D"); `Parse` strict on prefix (throws `FormatException` naming expected vs actual) and requires a well-formed UUID; `TryParse(out)`; case-insensitive on the UUID; no hard v7-on-parse.
5. **Prefix registry (Q2)** — a reflection-based test enumerating every `ITypedId` implementation and asserting the PRD §7 rules: prefixes unique, lowercase-ASCII, non-empty, never reused. The canonical set is also documented in `CONTEXT.md`.
6. **Serialization (Q5)** — a `System.Text.Json` `JsonConverterFactory` in **`ZWarden.Domain`** (BCL only), emitting/reading the canonical string; the raw Guid is never surfaced.
7. **EF conversion strategy (Q5)** — an EF `ValueConverter<TId, Guid>` factory in **`ZWarden.Infrastructure`** (which gains the **EF Core 10** package here); F1 delivers the converters + their tests, F2 applies them in `OnModelCreating` (ADR 0004).
8. **Comprehensive tests** — in `ZWarden.Domain.Tests` (types, parse, json, registry) and a new `ZWarden.Infrastructure.Tests` (the EF converter round-trip, no database needed).

## Non-scope

- **Persistence** (F2): the DbContext, `OnModelCreating`, migrations, provider wiring, the two-provider integration tests. F1 provides the converter *type*; F2 *uses* it.
- **The entities themselves**: `Tenant` (F3A), `User` (F4), `Server` (F14), … — F1 defines their *IDs*, not the entities.
- **Storage-representation measurement** — closed by ADR 0004 (native UUID); F1 does not re-open it.
- **The Sqlite upper-case / BLOB-endianness hazards** (ADR 0004) — those are F2/raw-SQL storage concerns; F1's canonical form is lowercase and provider-agnostic.
- **Domain services, aggregates, invariants beyond IDs** — later features.

## Domain changes

- **New value types:** the 19 typed IDs (immutable `readonly record struct`, structural equality, single Guid field) and the `ITypedId` / `ITypedId<TSelf>` contracts, in `ZWarden.Domain.Ids`.
- **Glossary (`CONTEXT.md`):** add **Typed ID** (a prefix-scoped UUIDv7, non-interchangeable across entity types, canonical form `<prefix>-<uuid>`, raw UUID never exposed) and the **prefix registry** table. No implementation detail in the glossary.
- **Invariants:** an ID's prefix is fixed by its type; two types are never assignment- or parse-compatible; `New()` is a v7, non-empty UUID.

## Contract changes

- **The canonical string `<prefix>-<uuid>` is the serialization contract** at every boundary (JSON via the STJ factory; a raw GUID never appears — PRD §6).
- **The EF conversion contract:** `TId ↔ Guid` (native `uuid` column, ADR 0004), delivered as a reusable converter in Infrastructure; F2's `OnModelCreating` is the only place it is applied.
- No API/SignalR/Agent contract types yet (those features add them, using these IDs).

## Security considerations

- **Type confusion is a build-time impossibility, not a runtime check** — `AgentId` and `UserId` are distinct types, so an authorization or ownership check can't be handed the wrong kind of ID by accident (PRD §8's explicit goal).
- **Raw UUIDs never surface** (PRD §6): the STJ converter emits the prefixed canonical form, so IDs in APIs, logs and diagnostics carry their entity type and never a bare GUID. This also aids the F30 support-package redaction later.
- **IDs are identifiers, not secrets** — no cryptographic strength is claimed; UUIDv7 is time-ordered and partially guessable, which is fine for internal identity and is never used as a bearer token.
- **Parse is fail-closed**: an unexpected prefix or malformed UUID throws rather than silently coercing.

## Test plan

Written before the code (PRD 2.2), in `ZWarden.Domain.Tests` unless noted.

1. **New() yields a distinct, non-empty, version-7 UUID** each call; `default(T).IsEmpty` is true.
2. **Round-trip** `Parse(id.ToString()) == id`; `ToString()` is lowercase `<prefix>-<uuid>`.
3. **Prefix is enforced**: `AgentId.Parse("usr-<uuid>")` throws `FormatException`; the message names expected vs actual prefix.
4. **Malformed UUID** throws; **`TryParse`** returns false without throwing; case-insensitive UUID parse succeeds and normalises to lowercase.
5. **Types are not interchangeable** — asserted at compile time (a `#error`-free build with no cross-assignment) and by distinct `ToString()` prefixes.
6. **Prefix registry** (reflection): every `ITypedId` has a unique, lowercase-ASCII, non-empty prefix; the set equals the documented 19.
7. **JSON** (STJ): an object with typed-ID properties serialises to canonical strings and round-trips; a raw GUID string without the prefix fails to deserialize.
8. **EF converter** (`ZWarden.Infrastructure.Tests`, no DB): `ConvertToProvider(id)` is the underlying `Guid`; `ConvertFromProvider(guid)` reconstructs the ID; round-trip is identity.
9. **Monotonicity caveat**: ordering tests space generation ≥1 ms via a documented helper; a test asserts that two same-instant IDs are *not* required to be ordered (documents the trap).

## Implementation slices

- **S1 — The pattern + one type.** `ITypedId` / `ITypedId<TSelf>`, the generic `TypedId` helper (new/parse/tryparse/format/validate), and `AgentId` as the first concrete type, TDD. *Verify:* tests 1–5 green for `AgentId`.
- **S2 — All 19 types + the registry guard.** The remaining structs; the reflection registry test. *Verify:* test 6 green; the set is exactly 19.
- **S3 — JSON serialization.** The STJ `JsonConverterFactory` in Domain. *Verify:* test 7.
- **S4 — EF conversion strategy.** Add EF Core 10 to `ZWarden.Infrastructure`; the `ValueConverter` factory; `ZWarden.Infrastructure.Tests`. *Verify:* test 8; arch tests still green (Domain EF-free; Agent references neither).
- **S5 — Glossary + ADR.** `CONTEXT.md` terms + prefix table; ADR 0014 (the pattern). *Verify:* registry test and docs agree on the 19.

## Diagnostics

- **Parse failures are actionable**: the `FormatException` states the expected prefix and what was supplied, and whether the prefix or the UUID was wrong — never a bare "invalid format".
- **The registry test names the offending prefix** (duplicate / non-lowercase / missing) so a mis-declared new ID type fails the build with a specific reason.
- **The monotonicity trap is documented at the point of use** (a comment + the test helper), so a future ordering test doesn't flake mysteriously.

## Documentation

- `CONTEXT.md` — the **Typed ID** term and the prefix-registry table.
- **ADR 0014** — the typed-ID pattern (static-abstract interface + generic helper, no source generator), recorded because it is load-bearing across every entity and a future reader will wonder why not Vogen/a generator.
- Code XML docs on `ITypedId<TSelf>` and `TypedId` describing how to add a new ID type (declare the struct, the prefix, register — the registry test enforces the rules).

## Acceptance criteria

1. All 19 typed IDs exist, each a `readonly record struct` with its canonical prefix; `New()` is a non-empty v7 UUID.
2. `ToString()` is lowercase `<prefix>-<uuid>`; `Parse`/`TryParse` round-trip and reject the wrong prefix or a malformed UUID; a raw GUID never appears at a boundary.
3. The reflection registry test passes and enforces unique/lowercase/non-empty/never-reused across exactly the documented set.
4. STJ serialises and deserialises typed IDs as canonical strings.
5. The EF `ValueConverter` factory in `ZWarden.Infrastructure` round-trips `TId ↔ Guid`; `ZWarden.Domain` references no EF Core (arch rule 2 green); `ZWarden.Agent` references neither Infrastructure nor EF Core (arch rule 1 green).
6. `CONTEXT.md` and ADR 0014 are updated; the offline tier is green.

## Definition of Done

Per PRD 61, the applicable subset: acceptance criteria met; tests authored first as executable specifications; unit tests pass; **architecture rules pass** (Domain EF-free); error conditions modelled (parse is fail-closed, actionable); diagnostics exist (named parse and registry failures); documentation and ADR updated; CI green; no unresolved warnings. (Migrations, DB tests, authorization, audit are N/A at F1 — no persistence or entities yet; the EF converter is tested without a database.)
