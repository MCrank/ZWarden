# 14. Typed IDs are hand-written structs over a static-abstract interface, not a source generator

ZWarden's strongly-typed IDs (PRD §8) are **one `readonly record struct` per entity**, each
implementing `ITypedId<TSelf>` — a static-abstract interface supplying the type's `Prefix` and a
`FromGuid` factory — with all behaviour (new / parse / format / validate) living once in a generic
static `TypedId` helper. **No source-generator dependency** (Vogen, StronglyTypedId). The struct
holds a native `Guid` (ADR 0004); the canonical public form is `<prefix>-<uuid>`.

- Status: accepted
- Decided in: [#22](https://github.com/MCrank/ZWarden/issues/22) (the F1 mini-plan grilling)
- Bears on: PRD §6/§7/§8, ADR 0004 (native-UUID storage), and every entity in the system

## Context

Eighteen ID types share one shape: a UUIDv7, a fixed prefix, and identical parse/format/validate/
serialize logic. The only per-type variation is the prefix and the C# type identity that makes
`AgentId` and `UserId` non-interchangeable. Three ways to get there:

1. A **source generator** (Vogen, StronglyTypedId) — writes the structs from an attribute.
2. **Hand-written structs over a shared static-abstract interface** — C# 11+ `static abstract`
   members let one generic helper carry all behaviour while each struct stays a concrete,
   readable type.
3. A single **generic `Id<TMarker>`** phantom-type struct — one implementation, but the generic
   name leaks into every signature.

## Decision

Take option 2. `interface ITypedId<TSelf> where TSelf : ITypedId<TSelf>` declares
`static abstract string Prefix { get; }` and `static abstract TSelf FromGuid(Guid value)`; a
non-generic `ITypedId` marker (exposing `Guid Value` and `string Prefix`) lets the JSON and EF
converter **factories** discover and handle every type without knowing them individually. The
concrete structs (`UserId`, `AgentId`, …) are a few lines each and read naturally in signatures.

## Alternatives considered

- **A source generator.** Rejected for v1.0: it adds a build-time dependency and a layer of
  opacity to the most-referenced types in the domain, against PRD 2.4's simplicity aim, to save
  boilerplate that the static-abstract-interface approach already removes. The seam is not closed
  — if the per-type lines ever become a burden, a generator can produce exactly this shape later.
- **A single generic `Id<TMarker>`.** Rejected on ergonomics: every method signature would read
  `Id<UserMarker>` (or need a `using` alias per type), and the JSON/EF factories gain nothing over
  the interface approach. Concrete named structs are what the rest of the codebase will read.
- **Untyped `Guid` everywhere.** Rejected by PRD §8 outright — the whole point is that an
  `AgentId` cannot be passed where a `UserId` is expected.

## Consequences

- **Adding an entity's ID is a fixed, mechanical recipe**: declare the struct, give it a unique
  prefix, done — and the reflection **registry test** enforces PRD §7's rules (unique, lowercase,
  never reused) at build time, so a mistake is a red build, not a latent collision.
- **Domain stays dependency-free** (BCL only), so `ZWarden.Domain` keeps satisfying architecture
  rule 2 (no infrastructure framework). The EF `ValueConverter` lives in `ZWarden.Infrastructure`
  precisely because it needs EF Core, which Domain may not reference.
- **The pattern needs C# 11+ static abstract interface members** — satisfied on `net10.0` (C# 14),
  and a reason the language-version pin matters (ADR 0002).
- New contributors (human or agent) have one canonical shape to copy, recorded here and in the F1
  mini-plan, rather than inventing per-type parsing.
