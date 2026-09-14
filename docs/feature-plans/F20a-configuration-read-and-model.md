# Feature 20a Mini-Plan — Configuration Read and Model

**Status:** in review. Delivered as [PR #104](https://github.com/MCrank/ZWarden/pull/104) (closes
[#40](https://github.com/MCrank/ZWarden/issues/40)), one commit per slice on branch
`feat/f20a-config-read-model`. Track D.

**Format:** PRD 60. **Written against:** PRD 32 (structured configuration editing — the *read and
model* half), PRD 2.2 (TDD mandatory), PRD 2.3 (supportability — "the file did not parse" is an
operator-facing state, not a stack trace), PRD 38 (untrusted-data posture); Feature 20a in
[`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (Track D) and §7 (why F20 splits — F21
needs read+model only); [ADR 0010](../adr/0010-lua-configuration-is-parsed-never-evaluated.md)
(Loretta 0.2.13, parse-only, behind `IPzConfigDocument`, ZWarden-side pre-check) and
[ADR 0011](../adr/0011-configuration-revisions-are-value-level-and-fail-closed.md) (why the unit
is parsed values, not bytes — the read side of which F20a supplies); the research doc
[`docs/research/pz-lua-config.md`](../research/pz-lua-config.md) (the measured file shapes and the
depth/size facts); [`trust-boundaries.md`](../trust-boundaries.md) §5 (Agent ↔ PZ runtime, config
files are attacker-influenced) and §8 (untrusted data never becomes trusted — the config-parser
pre-check rule and the no-recursion-over-attacker-depth rule); the F14 Server inventory it depends
on; and the `ZWarden.Rcon` leaf-project precedent (F18) it structurally mirrors.

## Objective

Give ZWarden a **safe, structured, read-only view** of a Project Zomboid server's four config
files — `<name>.ini`, `<name>_SandboxVars.lua`, `<name>_spawnregions.lua`, `<name>_spawnpoints.lua`
— behind the narrow `IPzConfigDocument` seam ADR 0010 mandates. F20a delivers, end to end for the
**read half**: a ZWarden-side **size + nesting-depth pre-check** that runs *before* any parser sees
the bytes; a small hand-written **key=value reader** for the INI; a **Loretta-backed** (parse-only,
`Lua51`) reader for the three Lua shapes; a **structured value model** that preserves order and
unknown keys; a **hand-maintained validation schema** applied over that model; and **"the file did
not parse" as a first-class result with line and column**. Writes, revisions, atomic apply, drift
detection and restore are **F20b** — explicitly out of scope here (§7 is why).

## The settled decisions

The three design forks were put to the maintainer before writing; all three took the recommended
option.

1. **Value tree + schema overlay, not a strongly-typed model.** The parsed **value tree**
   (`PzTable` of ordered `PzValue` entries, keys and positions preserved) is the source of truth;
   a **separate, hand-maintained validation schema** (per-key type/range/default) is applied *over*
   it. Unknown / mod-added / new-patch keys **pass through untouched and unvalidated** — this is the
   ADR 0010 correctness requirement ("a surgical read/edit must never silently drop a key ZWarden
   has not been taught"), realised in the model layer. Adding a validated key later is one schema
   entry, no model change. Chosen over ~275 hand-typed C# properties (large, patch-fragile, and
   has nowhere to put unknown keys) and over "value tree, defer validation" (the issue lists
   validation as in-scope).

2. **Schema framework + a representative, well-tested subset now.** F20a ships the schema
   *mechanism* and the *structural* rules (types, nesting shape, `VERSION`, nested-table
   membership, INI key set) plus a representative slice (~20–30 keys across all five sandbox nested
   tables, plus the common INI keys). Keys with no schema entry are **preserved and reported as
   `Info` ("no schema entry"), never as an error**. Filling out the remaining ~250 sandbox keys is
   cheap, mechanical follow-up — each a table row + a test — and does not bloat this PR toward the
   ~100K guardrail. The validation ranges are ZWarden's own (Java-side, not derivable from the Lua)
   and this is stated as ongoing manual work, per ADR 0010.

3. **New `ZWarden.PzConfig` leaf project, mirroring `ZWarden.Rcon`.** A project that isolates the
   single-maintainer Loretta dependency behind `IPzConfigDocument`, **references only
   `ZWarden.Domain`**, and exposes **no Loretta type in its public surface** — enforced by a new
   architecture test. Unlike Rcon it is **not** tier-restricted (both Web and Agent legitimately
   model config): Loretta is parse-only, MIT, no native code, so there is no credential-leak reason
   to keep it out of the web tier. Keeps ADR 0010's vendoring fallback real (narrow seam + a
   fixture corpus as the acceptance test). Chosen over folding into `ZWarden.Application` (which
   would drag Loretta into Application's closure and diverge from the precedent).

Decisions taken without escalation (low-risk, pattern-matching existing work):

- **No new typed ID and no new permission in F20a.** The `cfg-` prefix belongs to the
  **Configuration Revision** entity (CONTEXT.md), which is F20b. F20a is a pure parse/model library
  with no persisted aggregate, so it introduces no entity, no migration, and no prefix — the
  `PrefixRegistryTests` stay unchanged.
- **F20a does not read from a live server or wire to the Agent.** The seam turns **bytes + a file
  kind** into a model; *who supplies the bytes* (the Agent from the `/pz/` bind mount, a fixture in
  a test) is the caller's concern and arrives with F20b's operation path. F20a depends on F14 only
  because configuration is conceptually a Server's, and F20b will persist revisions against a
  Server — the read/model library itself needs no Server wiring, and adding a UI/agent round-trip
  now would pull in F10/F11/F15, which F20a explicitly does not depend on.
- **Synthetic fixtures only.** Per the F12 testing rule ("no PZ-derived artefact is committed to
  the repository") and ADR 0010 §6.5 (the licence question is unresolved; generation is preferred
  over committing), all test fixtures are **hand-written** to mirror the exact shapes the research
  doc measured (global-assignment sandbox root, `function …() return {…} end` spawn files, quoted
  profession keys, the legacy `{worldX,worldY,…}` spawnpoint cell, trailing commas, CRLF/tabs). The
  real-install corpus that ADR 0010 §6.5 describes is a **tier-2 / opt-in** concern (a generated,
  never-committed corpus), left as a follow-up; F20a's offline tier stands on synthetic fixtures.
- **No new ADR.** ADR 0010 and 0011 already record every hard-to-reverse, surprising decision on
  this path (parse-only, the pre-check being ZWarden's job, value-level not byte-level). F20a's
  remaining choices are conventional realisations of them and live in this mini-plan.

## Dependencies

**F14** (issue-declared) — the Server inventory the configuration conceptually belongs to; F20a
adds no code to it. Transitively F0–F2 (build/CI, typed IDs, persistence) via `ZWarden.Domain`.
New third-party dependency: **`Loretta.CodeAnalysis.Lua` 0.2.13**, pinned centrally, its only
consumer `ZWarden.PzConfig`, never surfaced across the seam (ADR 0010).

## Scope

1. **`ZWarden.PzConfig` project + model.** The value tree: `PzValue` (a closed hierarchy —
   `PzBoolean`, `PzNumber` (raw lexeme preserved alongside the parsed value), `PzString`,
   `PzTable`), `PzTable` as an **ordered** list of `PzTableEntry { PzKey? Key, PzValue Value }`
   (null key = positional/sequence entry; a present key is an identifier or a quoted name — both
   forms carried). `PzConfigKind` (`Ini`, `SandboxVars`, `SpawnRegions`, `SpawnPoints`).
   `PzSourcePosition { int Line, int Column }` (1-based). `PzConfigDiagnostic { Severity, Code,
   Message, Position? }`. `PzConfigDocument : IPzConfigDocument` (Kind + Root `PzTable` +
   value-read accessors). Read-value accessors (`TryGet(path)`, enumerate) only — `set`/`emit` are
   declared as the F20b half and left unimplemented behind the seam.
2. **Pre-check (`PzConfigPreCheck`).** Runs on raw bytes **before** any parser. A byte-length cap
   and a **brace-nesting-depth cap (16)** measured by a small hand-written scanner that skips `--`
   line and `--[[ ]]` long comments and quoted/long-bracket strings (over-counting is safe — it
   only rejects earlier). Returns a fatal diagnostic (`config.too-large` / `config.too-deep`)
   rather than letting Loretta near a file that would kill the process (research §5: ~1800 levels →
   uncatchable `StackOverflowException`, no AppDomain escape on .NET 10). INI skips the depth scan.
3. **INI reader (`IniConfigReader`).** Hand-written `# comment` / `KEY=value`, no sections, no
   continuations (research §7 scope note). Preserves order and unknown keys; a malformed line is a
   diagnostic with line/column, not a throw.
4. **Lua reader (`LuaConfigReader`, Loretta, internal).** `LuaSyntaxOptions.Lua51` set explicitly
   (the default `All` is a trap — ADR 0010), **parse-only**. Handles the three roots: `SandboxVars
   = { … }` (global assignment), `function SpawnRegions() return { … } end` and `function
   SpawnPoints() return { … } end` (function-def wrappers). Quoted profession keys (`["park
   ranger"]`) and bare identifiers both map to `PzKey`. A Loretta parse diagnostic becomes a
   `PzConfigDiagnostic` with **line and column** and a non-parsed result. The AST→model walk is
   **iterative (explicit stack), never recursing over attacker-controlled depth** (trust-boundaries
   §8); the depth is already ≤16 from the pre-check, and the walker enforces it again as
   belt-and-suspenders.
5. **The seam + parser (`IPzConfigParser` → `PzConfigReadResult`).** `Open(kind, bytes)` runs
   pre-check → reader → returns a result that is **either** a parsed `PzConfigDocument` **or** a
   not-parsed state carrying the fatal diagnostics (line/column). This is the "did not parse is a
   first-class state" requirement.
6. **Validation schema (`PzConfigSchema` + `IPzConfigValidator`).** A hand-maintained table keyed
   by kind + dotted key path, each entry giving expected type and (where numeric) an inclusive
   range and default. `Validate(document)` walks the model **iteratively** and returns diagnostics:
   `Error` for wrong type / out-of-range on a **known** key; `Info` for a key with no schema entry
   (preserved, not dropped); structural errors (missing `VERSION`, a scalar where a nested table is
   required). Non-destructive — validation never mutates the model.
7. **`ZWarden.PzConfig.Tests`** — model, pre-check (hostile size/depth, comment/string false-brace
   cases), INI reader, each Lua shape, parse-failure line/column, the iterative walker's
   depth-guard, and the schema (each representative key: in-range ok, out-of-range error, wrong
   type error, unknown key info). All on synthetic fixtures.
8. **`ZWarden.ArchitectureTests`** — a new `ReferenceDirectionTests` rule: `ZWarden.PzConfig`
   references only `ZWarden.Domain` (no persistence, no Docker, no web), and **no `Loretta` type
   appears in its public API surface** (the seam holds — reflection over exported types).

## Non-scope

- **Writes, surgical value edits, atomic BOM-less writes, `emit bytes` (F20b, ADR 0010/0011).**
  The seam declares them; F20a does not implement them.
- **Configuration revisions, the `cfg-` entity, diffing parsed values, restore (F20b).**
- **Out-of-band drift detection and fail-closed re-parse-before-write (F20b, ADR 0011).**
- **Reading config from a live server / the Agent file round-trip / any UI (F20b + F11/F15/F16).**
  F20a is a library exercised by unit tests, not wired into a page or an operation.
- **A committed real-install fixture corpus (ADR 0010 §6.5).** Left as an opt-in / tier-2
  follow-up; F20a's offline tier uses synthetic fixtures.
- **The full ~275-key sandbox schema.** F20a ships the mechanism + a representative subset;
  the rest is incremental follow-up.
- **Live-reload semantics (`reloadoptions` vs. sandbox edit-then-restart).** That is apply-side
  behaviour (F20b); F20a neither applies nor restarts.

## Domain / documentation changes

No `ZWarden.Domain` change, no entity, no migration, no new typed-ID prefix or permission.
`CONTEXT.md` gains two read-side terms if they prove load-bearing during the build —
**Configuration Document** (the parsed, structured, in-memory view behind `IPzConfigDocument`) and
**Parse Failure** (the first-class "did not parse" state with line and column) — kept distinct from
**Configuration Revision** (`cfg-`, F20b). This mini-plan is the durable handoff.

## Testing & verification

TDD in slices, each its own commit (test → red → code → green): (1) model, (2) pre-check,
(3) INI reader, (4) Lua reader + walker, (5) seam/parser + parse-failure state, (6) validation
schema, (7) architecture guard. Run as CI does —
`DOTNET_ROOT=/c/Users/marco/.dotnet /c/Users/marco/.dotnet/dotnet.exe test --project
tests/ZWarden.PzConfig.Tests/ZWarden.PzConfig.Tests.csproj -c Release`. New project ⇒ new central
Loretta pin ⇒ regenerate lock files with the **pinned** SDK and verify `--locked-mode` is clean
(toolchain memory). The new test csproj carries its own `--minimum-expected-tests` floor; no
Web.Tests count changes, so the ci.yml silent-drop guard is untouched.
