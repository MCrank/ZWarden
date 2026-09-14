# Feature 20b Mini-Plan — Configuration Apply and Revisions

**Status:** in progress. Track D. Delivered across **~4 PRs** — part of [#42](https://github.com/MCrank/ZWarden/issues/42),
one commit per TDD slice. **PR 1 = the library write-half** ([PR #105](https://github.com/MCrank/ZWarden/pull/105),
**merged**). **PR 2 = persistence + drift-comparison** (in review, branch
`feat/f20b-config-revisions-persistence`): the `ConfigurationRevision` `cfg-` aggregate + its own
`PzConfigFile` domain identity (Domain references nothing, so it cannot store PzConfig's `PzConfigKind`;
the two are mapped in PR 3's apply path), the EF mapping + tenant-scoped repository + dual-provider
`AddConfigurationRevisions` migration, and `PzDriftCheck` — the pure, fail-closed value-level drift check
the Agent runs before every write. Repository DI registration is deferred to PR 3 with its consumer. PRs
3–4 wire the agent apply path and the UI.

**Format:** PRD 60. **Written against:** PRD 32 (structured configuration editing — the *apply* half),
PRD 33 (every meaningful mutation records a revision with a previous/resulting state), PRD 2.2 (TDD
mandatory), PRD 2.3 (supportability — a refused write and a drift are operator-facing states, not stack
traces), PRD 38 (untrusted-data posture); Feature 20b in
[`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (Track D) and §7 (why F20 splits — F20b
carries revisions/apply/drift); [ADR 0010](../adr/0010-lua-configuration-is-parsed-never-evaluated.md)
(surgical value replacement, BOM-less atomic write, `IPzConfigDocument` supports set/emit, Loretta
behind the seam) and
[ADR 0011](../adr/0011-configuration-revisions-are-value-level-and-fail-closed.md) (revisions are
parsed values not bytes; drift detection re-parses before every write and **fails closed**); the
research doc [`docs/research/pz-lua-config.md`](../research/pz-lua-config.md) (the measured byte-exact
round-trip and the rewrite/reseed facts); [`trust-boundaries.md`](../trust-boundaries.md) §5 and §8;
the **F20a** read/model library ([#40](https://github.com/MCrank/ZWarden/issues/40), PR #104) this
extends; and F11 (operations), F15 (lifecycle) as the apply-path precedents PRs 2–4 reuse.

## Objective

Give ZWarden the **apply** half of structured configuration: **surgical, byte-preserving, BOM-less
value edits** to a Server's four config files; a **Configuration Revision** history captured as parsed
values (not bytes); **restore** of a prior revision; an advanced raw view/edit; and **out-of-band
drift detection** that re-parses the on-disk file before every write and **fails closed** on a
mismatch. The whole feature is sequenced across four PRs; the durable design decisions for all four
are recorded here.

## The settled decisions

Three design forks were put to the maintainer before writing; all three took the recommended option.
A fourth decision is settled by ADR 0011, not open.

1. **Emit engine: retain the Loretta tree internally (recommended, chosen).** F20a's reader parses
   with Loretta then **discards the syntax tree**, keeping only a detached, immutable `PzTable`
   ([`LuaConfigReader.cs`](../../src/ZWarden.PzConfig/Internal/LuaConfigReader.cs) returns
   `PzConfigDocument(kind, table)` and drops `tree`/`root`). Byte-preserving surgical emit is therefore
   **impossible against today's model** — there is no map from a `PzValue` back to a source token. The
   write half **retains the Loretta `SyntaxTree` inside an internal edit backing**; `SetValue`
   re-navigates the syntax tree by the same dotted path, replaces the **single** value node while
   carrying its original leading/trailing trivia (`ReplaceNode` + `WithTriviaFrom`), and `Emit`
   returns `root.ToFullString()` as **BOM-less UTF-8**. This leans on the *measured* byte-exact
   round-trip that justified choosing Loretta (ADR 0010: one value edit changed exactly one line of
   1,021, all 738 comments intact). Loretta stays **strictly `internal`** — the `ReferenceDirectionTests`
   guard bars Loretta only from the *public* surface, which this respects. Chosen over recording source
   byte-spans on ZWarden's own model and splicing bytes by hand, which would re-implement the
   trivia/quoting/escape boundaries Loretta already provides — more bespoke code and more fidelity-bug
   surface, against an ADR whose whole point is "don't hand-roll Lua."

2. **Revisions store a full canonical value snapshot (recommended, chosen).** Each Configuration
   Revision persists the **whole file's parsed values** as a canonical, order-normalized serialization,
   plus a derived value-level diff for display. **Restore** re-applies a snapshot's values as surgical
   edits (never a byte restore — ADR 0011 rejects that explicitly). The **drift baseline** shipped to
   the Agent is a **stable hash of the canonical snapshot**: the Agent re-parses the on-disk file,
   canonicalizes, hashes, and a mismatch fails the write closed. Files are ~45 KB, so a full snapshot
   per revision is a non-issue; chosen over delta-only revisions, whose restore and drift baseline must
   replay a whole delta chain and where one bad link breaks reconstruction.

3. **Delivery: the library write-half is PR 1 of ~4 (recommended, chosen).** PR 1 is **pure
   `ZWarden.PzConfig`**, full TDD, **zero wiring**: extend the reader to retain the edit backing, add
   `SetValue`/`Emit`, the value-level diff, the canonical snapshot + hash, and restore-as-edits. PRs
   2–4 (below) add persistence, the agent apply path, and the UI. Mirrors the multi-PR delivery of F16
   and F19 and keeps each PR reviewable under the ~100 K guardrail.

4. **Apply + drift run Agent-side (settled by ADR 0011, not a fork).** ADR 0011 requires "before any
   write: re-parse the file, compare against the last recorded revision, and on a mismatch refuse the
   write." Only the Agent can read the live on-disk file, and a Web-side read-then-write has a TOCTOU
   race against the very second-author scenario (in-game admin panel / settings editor) the ADR
   targets. So the apply Operation carries the intended edits plus the baseline hash; the Agent reads →
   parses → drift-checks → surgically writes → BOM-less atomic-replaces → reports the result. This is
   PR 3.

Decisions taken without escalation (low-risk, pattern-matching existing work):

- **The write half replaces existing scalar values only.** Adding or removing a key is **out of scope**
  for the surgical writer: operators change values, and PZ owns the key set (it regenerates the INI and
  sandbox file on every start — ADR 0011). Restore therefore applies **value** differences; a
  structural add/remove between revisions is reported as a first-class "cannot be restored surgically"
  result, not silently regenerated (ADR 0010 forbids regeneration). Inserting new fields is a possible
  later extension, deliberately not in the first cut.
- **No new typed ID struct.** `ConfigurationRevisionId` with prefix `cfg-` **already exists** in
  `TypedIds.cs` (and in the CONTEXT.md registry table); PR 2 only registers it where the
  `PrefixRegistryTests` expect the closed set. PR 1 (library) persists nothing and adds no prefix.
- **No new permission.** `Permissions.ServerConfigurationEdit` already exists and already gates the
  whitelist-mode section on `ServerDetail`; it is the natural gate for the config-apply UI (PR 4).
- **No new ADR.** ADR 0010 and 0011 already record every hard-to-reverse decision on this path
  (surgical/BOM-less/atomic, value-level revisions, fail-closed drift). "Retain the tree internally"
  is a conventional realization of ADR 0010's set/emit mandate and lives in this mini-plan. This plan
  *does* supersede one F20a **implementation note** — `PzConfigDocument`'s "no parser type is retained"
  doc-comment — which PR 1 updates; the tier-safety claim beside it stands (Loretta is MIT, parse-only,
  no native code).
- **Synthetic fixtures only**, per the F12 rule and ADR 0010 §6.5 — hand-written to the shapes the
  research doc measured, as in F20a. No PZ-derived artefact is committed.

## Delivery slices (PRs)

- **PR 1 — Library write-half (this session).** `ZWarden.PzConfig` only. Scope below.
- **PR 2 — Persistence.** `ConfigurationRevision` entity (`cfg-`, `ITenantOwned`, `IVersioned`) +
  `IEntityTypeConfiguration` + dual-provider `AddConfigurationRevisions` migration + repository +
  register `cfg-` in the prefix registry; the value-level drift-comparison domain logic.
- **PR 3 — Agent apply + drift.** New `ConfigApply` `AgentCommand` + `OperationKind` + dispatcher map +
  `AgentCommandProcessor` arm + a config-writer collaborator reading/writing the `/pz/` mount (atomic
  temp-file + replace, BOM-less), re-parse-and-drift-check fail-closed, `ConfigApplyResult` on
  `OperationCompleted`; the enqueue service; e2e.
- **PR 4 — UI.** `ServerDetail` configuration section: structured editor (schema-driven), advanced raw
  view/edit, revision history, restore, and the drift-confirmation prompt — gated by
  `ServerConfigurationEdit`.

## Scope — PR 1 (library write-half)

1. **Edit seam + result type.** Extend the F20a seam with the write half ADR 0010 named ("set a value,
   emit bytes"): `TrySetValue(string path, PzValue newValue)` returning a first-class
   **`PzConfigEditResult`** (`Ok`, or a diagnostic: path not found, path resolves to a table,
   document not editable) and `byte[] Emit()`. A document opened from bytes is edit-capable (carries a
   backing); a document built directly from a `PzTable` (the F20a test ctor) reports **not editable**
   rather than throwing.
2. **Lua edit backing (`LuaEditBacking`, internal).** Retains the Loretta `SyntaxTree` + parse options.
   `SetValue` navigates the `TableConstructorExpressionSyntax` root by dotted path, replaces the leaf
   value node with a freshly-parsed literal carrying the old node's trivia, re-derives the `PzTable`
   model from the new tree. Handles the three roots (`SandboxVars = { }`, `function SpawnRegions/
   SpawnPoints() return { } end`), booleans, integers/doubles (lexeme-faithful), negative numbers, and
   **re-quoted** strings (Loretta unescapes on read, so a new string is deliberately re-escaped as a
   double-quoted Lua literal). `Emit` = `ToFullString()`, BOM-less UTF-8.
3. **INI edit backing (`IniEditBacking`, internal).** Retains the original lines + a key→line map;
   `SetValue` replaces the text after `=` on the matching line, preserving key, spacing, trailing
   comment and EOL (the `RconServerConfig` line-edit precedent, generalized). `Emit` = BOM-less UTF-8.
4. **BOM-less guarantee.** `PzText` gains an `EncodeUtf8` that never writes a BOM; a test asserts no
   `EF BB BF` prefix on any emitted bytes, including when the input carried one (F20a strips it on read;
   F20b must not restore it — the BOM is fatal to PZ's lexer).
5. **Value-level diff (`PzValueDiff`).** `Compare(before, after)` → ordered
   `PzConfigChange { string Path, PzConfigChangeKind Kind (Added/Removed/Changed), PzValue? Before,
   PzValue? After }`. **Order-insensitive** over named keys (a reorder is not a change — ADR 0011
   measured the server erasing reorders), **order-sensitive** over positional entries. Scalar equality:
   booleans by value, strings ordinal, numbers by (value, integer-ness).
6. **Canonical snapshot + hash (`PzValueSnapshot`).** Deterministic serialization of a document's value
   tree: named entries **sorted by key** (order-normalized), positional entries kept in order; a stable
   **SHA-256** over it. A reorder yields the same hash; any value change yields a different one. This is
   the unit PR 2 persists and PR 3 hashes for the drift baseline.
7. **Restore plan (`PzRestore`).** `PlanEdits(target snapshot, current document)` → the `SetValue`
   edits that make the current file's values match the target's, plus a report of keys present in one
   and not the other (structural, **not** surgically restorable). Non-destructive; produces a plan the
   caller applies.
8. **Tests + arch guard.** `ZWarden.PzConfig.Tests` covers every slice on synthetic fixtures (INI +
   three Lua shapes; byte-exact-except-one-value; comment/trivia fidelity; re-quoting; negative
   numbers; path-not-found and path-is-table as first-class results; BOM never written; diff
   order-insensitivity; snapshot reorder-stability; restore value-vs-structural). `ArchitectureTests`
   re-asserts no Loretta type on the public surface (the retained tree is internal). Update the
   per-csproj `--minimum-expected-tests` floor to the new count.

## Non-scope — PR 1

- **Persistence, the `cfg-` entity/migration, drift-comparison domain logic (PR 2).**
- **The `ConfigApply` operation, the agent file round-trip, atomic temp-file+replace on disk, the
  fail-closed re-parse against a live file (PR 3).** PR 1's BOM-less `Emit` produces the bytes; *who
  writes them atomically to the `/pz/` mount* is PR 3.
- **Any UI — structured editor, raw view/edit, revision history, restore button, drift prompt (PR 4).**
- **Adding/removing keys via the surgical writer** (values only, per the settled decisions).
- **Live-reload semantics** (`reloadoptions` for INI vs sandbox edit-then-restart) — an apply-side,
  PR 3/PR 4 concern.
- **The full ~275-key sandbox schema** — unchanged from F20a; still incremental follow-up.

## Domain / documentation changes — PR 1

No `ZWarden.Domain` change and no migration in PR 1 (those are PR 2). The only doc change is updating
`PzConfigDocument`'s "no parser type is retained" note to reflect the retained internal backing, with
the tier-safety rationale unchanged. **Configuration Revision** (CONTEXT.md) is realized in PR 2; this
mini-plan is the durable handoff for all four PRs.

## Testing & verification

TDD in slices, each its own commit (test → red → code → green). Run as CI does —
`DOTNET_ROOT=/c/Users/marco/.dotnet /c/Users/marco/.dotnet/dotnet.exe test --project
tests/ZWarden.PzConfig.Tests/ZWarden.PzConfig.Tests.csproj -c Release`. No new package (Loretta is
already pinned from F20a), so no lock-file regeneration is required for PR 1. The new
`--minimum-expected-tests` floor moves with the test count; no Web.Tests change, so the ci.yml
silent-drop guard is untouched.
