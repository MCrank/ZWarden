# 4. Typed IDs are stored as a native UUID, not as prefixed text

PRD 8 left the storage representation open — "native UUID values" or "textual prefixed IDs",
"depending upon measured database and portability considerations". Measured: **store the native
UUID**. One `HasConversion<TypedId, Guid>` in `OnModelCreating`, no provider branch, no
`HasColumnType`. **The public canonical form stays `srv-<UUIDv7>` at every boundary** — API,
logs, diagnostics — exactly as PRD 8 requires; only the storage cell changes. This closes PRD 8.

- Status: accepted
- Decided in: [#9](https://github.com/MCrank/ZWarden/issues/9), the dual-provider spike ([`spike/ef-core-dual-provider`](https://github.com/MCrank/ZWarden/tree/spike/ef-core-dual-provider), raw output committed under `spike/results/`); background facts from [#4](https://github.com/MCrank/ZWarden/issues/4)
- Versions exercised: EF Core / `.Sqlite` / `.Design` **10.0.12**, `Microsoft.Data.Sqlite` **10.0.12**, `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3**, `dotnet-ef` **10.0.12**, `postgres:18.1-alpine`, `net10.0` / SDK 10.0.302

## Context

PRD 8 asked for the measurement and named no number. Two candidate shapes were plausible —
a native `uuid` column, or the prefixed canonical string in a text column, which has the obvious
virtue of being legible in a database console. Two more were worth pricing out on SQLite: bare
hex, and a 16-byte BLOB.

200,000 UUIDv7 rows per shape, primary-key index on the id column, `VACUUM`/`ANALYZE` before
measuring.

**PostgreSQL 18.1**

| shape | column type | table bytes | **index bytes** | total | time-ordered |
|---|---|---|---|---|---|
| **native** | `uuid` | 10,592,256 | **6,332,416** | **16,924,672** | yes |
| prefixed text | `varchar(40)` | 15,441,920 | 13,697,024 | 29,138,944 | yes |
| prefixed text | `text` | 15,441,920 | 13,697,024 | 29,138,944 | yes |
| bare hex | `varchar(32)` | 13,770,752 | 11,829,248 | 25,600,000 | yes |

The prefixed-text index is **2.16× the native index** and the whole relation is 1.72×.
`varchar(40)` and `text` are byte-identical in storage, so the length facet buys validation and
nothing else.

**Migration portability decides it as firmly as the sizes do.** The four generated DDL scripts
diff to almost nothing: on **SQLite, native and text are byte-identical** — both are `TEXT`, so
the PRD 8 choice is invisible at the schema level there and shows up only in row bytes. On
PostgreSQL the only difference is `uuid` ↔ `character varying(40)` on the five id columns.
Everything else, including the PRD 21 partial unique index, emits verbatim identically on both
providers.

## Alternatives considered

- **Prefixed text (`srv-<uuid>`) in the column.** Its only advantage is legibility in a database
  console. 2.16× the PostgreSQL index is too much to pay for it, on the provider that will hold
  the large installations.
- **16-byte BLOB on SQLite.** Smallest on SQLite by 45% (10,235,904 bytes vs 18,415,616) and
  rejected anyway: it needs a `byte[]` converter and `HasColumnType`, which is precisely the
  provider-specific model configuration PRD 9 discourages, and on PostgreSQL it would become
  `bytea` and lose the native `uuid` type.
- **Bare hex text.** Middle on every axis and legible-but-not-canonical, so it buys neither the
  size win nor the console-readability argument.

## Two measured hazards that come with this choice

These close items the data-layer research ([#4](https://github.com/MCrank/ZWarden/issues/4) §7)
explicitly could not verify.

1. **Microsoft.Data.Sqlite writes `Guid` TEXT in UPPER-CASE.** A lower-case literal matches
   **zero rows**, and one lower-case row destroys UUIDv7 ordering under SQLite's BINARY
   collation:

   ```
   default mapping : typeof=text length=36 value=01A08D6E-C228-784A-93C5-977581C53E5E
     WHERE asText = '<lower-case>' : 0 row(s) matched
     mixed-case ordering           : TIME ORDER BROKEN
   ```

   Uniform upper case sorts correctly, so ordering is safe **as long as only
   Microsoft.Data.Sqlite ever writes**. The consequence lands on Features 29/30 and on support:
   hand-written diagnostic SQL must use upper-case UUID literals on SQLite and lower-case on
   PostgreSQL, or go through EF parameters. Prefer EF parameters; where raw SQL is genuinely
   needed, the case rule is a documented requirement, not folklore.
2. **`SqliteType.Blob` is mixed-endian.** It maps a `Guid` through `Guid.ToByteArray()`, which is
   .NET's mixed-endian layout, not RFC 9562 order — `6E8DA001…` where big-endian would be
   `01A08D6E…`. It is the **one shape in the entire matrix whose `ORDER BY` does not return
   generation order**. Anyone reaching for a BLOB id on SQLite to recover that 45% must write
   their own big-endian converter first.

## Consequences

- **Key generation is application-side**: `Guid.CreateVersion7()` in the entity. The Npgsql
  provider would generate UUIDv7 keys for us and the EF SQLite provider generates nothing, so
  application-side is the only mechanism identical on both. Feature 1 carries it.
- **`Guid.CreateVersion7()` has no documented monotonicity.** Both overloads seed `rand_a`/
  `rand_b` with random data, so two ids generated in the same millisecond have arbitrary relative
  order. Harmless for index locality; **fatal for any test that asserts strict ordering** without
  spacing generation. The spike side-stepped it by spacing ids one millisecond apart, which is a
  test technique, not a property of the system.
- **UUIDv7 is carried by the typed-ID conversion layer, not by the stack.** It is a .NET 9+ BCL
  API, native in Npgsql and PostgreSQL 18, and absent from EF Core itself and from SQLite
  entirely.
- The database-side route (`SetPostgresVersion(18, 0)`, provider translation to `uuidv7()`) is
  **not** used: it has no SQLite equivalent, so it would reintroduce a provider branch to buy
  sub-millisecond monotonicity we do not need.
- EF Core 10 adds no strongly-typed-ID feature; value converters remain the mechanism, and all
  six documented value-conversion limitations survive unchanged. The two that bite: **a converted
  property cannot be spread across multiple columns** (so a composite or prefixed id must be a
  single column, which this decision satisfies), and **converted properties cannot be used as
  parameters in raw SQL APIs**.
