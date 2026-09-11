# 10. PZ Lua config is parsed and never evaluated: Loretta 0.2.13 behind `IPzConfigDocument`

Three of Project Zomboid's four server configuration files are **Lua**, not INI. ZWarden reads
and edits them with **`Loretta.CodeAnalysis.Lua` 0.2.13**, pinned, in **`LuaSyntaxOptions.Lua51`**
mode, **parse-only — the file is never evaluated** — behind a narrow ZWarden-owned
**`IPzConfigDocument`** seam, with a **ZWarden-side size and nesting-depth pre-check applied
before the parser ever sees the file**. Edits are **surgical value replacements**, not whole-file
regeneration, written **BOM-less** to a temp file in the same directory and atomically replaced.
**Hand-rolling a reader/writer stays the documented fallback behind the seam.** `<name>.ini`
gets a small hand-written key-equals-value reader and nothing more.

- Status: accepted
- Decided in: [#15](https://github.com/MCrank/ZWarden/issues/15); full evidence in [`docs/research/pz-lua-config.md`](../research/pz-lua-config.md). Verified against **Build 42.20.4** by running `zombie.network.GameServer` five times against an isolated `-cachedir` and re-reading its own output.
- Sharpens: **PRD 32** (three of four files are Lua, not INI)

## Context

The PRD assumed INI parsing. The file set is right and the format is not:
`<name>.ini` is key=value, but `<name>_SandboxVars.lua`, `<name>_spawnpoints.lua` and
`<name>_spawnregions.lua` are Lua with nested tables, **executed as Lua by the game** — the
interpreter is Kahlua with a LuaJ-derived Lua 5.1 compiler.

Two shapes contradict the obvious working assumption and would defeat a table-literal reader
outright: **`_SandboxVars.lua` is a global assignment** (`SandboxVars = { … }`), not a `return`,
and **both spawn files are function definitions** (`function SpawnRegions() return { … } end`).

The real question is therefore not "how do we run Lua". It is: **how do we edit a data file that
happens to be Lua syntax, without losing anything we do not understand?**

Two measured constraints make getting this wrong expensive rather than annoying:

- **A Lua syntax error in `_SandboxVars.lua` is fatal to server start** (`Exiting due to errors
  loading …` → `System.exit(1)`).
- **A UTF-8 BOM is fatal**, and worse than fatal-with-a-message: it crashes PZ's lexer inside its
  own error-reporting path with an `ArrayIndexOutOfBoundsException`.

A torn or BOM-prefixed write bricks the server with no self-healing path. That is why atomic,
BOM-less writing is a requirement here and not a quality bar.

## Why parse-only, and why that eliminated most of the field

Every other viable candidate on NuGet **executes** the file. The survey, in one line each:

| Candidate | Executes file contents? |
|---|---|
| **`Loretta.CodeAnalysis.Lua` 0.2.13** | **No.** No VM, no eval, no `DoString` in the public surface. Measured: a file containing `os.execute("rm -rf /")` and `io.open("/etc/passwd")` parses to inert nodes, 0 diagnostics, nothing run. MIT, **no native binaries**, resolves on `net10.0` via its `net8.0` asset |
| `MoonSharp` 3.0.0-beta.1 | **Yes** — and `new Script()` is the **unsandboxed** preset, whose own XML doc says it "allows scripts unlimited access to the system". No instruction budget, no memory cap, no `CancellationToken`, no public AST. Last *stable* release 2016 |
| `NLua` 1.7.9 + `KeraLua` 1.4.9 | **Yes, in native code**, stdlib and CLR reflection both on by default. Inherits Lua's C memory-safety surface — NVD's own text for CVE-2022-28805 (9.1) describes the threat model as "a system that compiles untrusted Lua code", so "we only parse it" is not a safety boundary with native Lua. Its Linux `.so` files are glibc builds and will not load on Alpine/musl |
| `LuaCSharp` 0.5.6 | **Yes**, though it is the best-engineered and the only one secure by default. Disqualified separately: Lua **5.2**, UTF-16 string semantics, pre-1.0, and its lexer has **no `Comment` token at all**, so round-trip is structurally impossible |
| `NeoLua`, `WattleScript`, `CSLua`, `AsyncLua` | **Yes**, all four; none exposes a trivia-carrying AST |
| A hand-rolled reader/writer | **No**, by construction |

**No package on NuGet both round-trips Lua and executes it.** Loretta is the only candidate that
answers the actual question.

Round-tripping is not a README promise here — it was measured **byte-exact on all five real PZ
files** (both generated spawn files, the generated `_SandboxVars.lua`, the shipped
`Apocalypse.lua`, and the shipped `media/maps/Muldraugh, KY/spawnpoints.lua` with its `local`
bindings and `mergeTable` calls), 0 diagnostics under `Lua51`, ~2.7 ms for a 45 KB file. A
targeted value edit changed **exactly one line out of 1,021**, keeping all 738 comments and CRLF
intact.

### Why surgical edits rather than regenerating from a ZWarden model

Fidelity is mostly a nicety — the server rewrites the INI and sandbox file from its own in-memory
model on **every single start**, so anything ZWarden preserved there is destroyed at the next
restart anyway (see ADR 11). But one part of it is a **correctness** argument, not an aesthetic
one: a surgical edit that replaces only the value tokens it means to change **cannot silently
drop a mod-added custom sandbox option or a key from a game patch ZWarden has not been taught**.
Regenerating the whole file from a ZWarden-side model can, and would do it quietly.

For the two spawn files, fidelity is a hard requirement: the server does **not** rewrite them,
operators do hand-edit them, and the game's own generated file ships a commented-out template
line — so comments there are real content.

## Alternatives considered

- **Hand-roll a Lua reader/writer.** Not chosen, but **kept as the documented fallback behind
  the seam** — it is the only other option that does not execute the file. It trades Loretta's
  single-maintainer risk for a permanent obligation to be right about Lua long-brackets, number
  formats and escapes. That is the worse bargain *while the seam keeps the option open*, which
  is precisely why the seam is not optional.
- **An embedded interpreter, sandboxed** (`CoreModules.None` on MoonSharp, `LuaState.Create()`
  on LuaCSharp). Rejected: it still executes attacker-influenced content, and none of them
  round-trips, so writes would mean regeneration.
- **Regex or line-based editing.** Rejected: `_SandboxVars.lua` has exactly two levels of
  nesting, five nested tables and 738 machine-generated comments; the spawn files are function
  definitions. Line editing survives until the first mod-added key.
- Rejected on licence or liveness grounds, recorded so they are not re-proposed: `KopiLua`
  (net461, no LICENSE file, `licenseUrl` points at a Wikipedia article), `LuaTableSerializer`
  and `Loom.Parser.Lua` (no licence declared), `SharpLua` (net40/2012 — a shame; it has the only
  other exact-round-trip visitor found), `Luau`/`Luau.Native` (repo archived), and the `lua`
  5.5.1 package, which is a native C++ MSBuild package with **no managed assembly** and an easy
  mistake from a downloads-sorted search.

## Two library defaults that are traps

- **Loretta's default `LuaSyntaxOptions` is `All`** — the most permissive dialect superset, wider
  than the game's own language. `Lua51` must be set explicitly.
- **`new MoonSharp.Interpreter.Script()` is the unsandboxed preset.** Recorded in case anyone
  reaches for it later for an unrelated reason.

## The pre-check is ZWarden's job, not the library's

**Nothing survives hostile nesting depth.** Measured: Loretta's *process* dies at roughly 1,800
levels (`0xC0000005`, after its stack guard's own *recovery* path throws) and MoonSharp's at
roughly 3,200 (`STATUS_STACK_OVERFLOW`, no exception at all). A .NET `StackOverflowException`
cannot be caught, and the documented `ICLRPolicyManager`/AppDomain escape is unavailable because
**AppDomains do not exist on .NET 10**. The process is simply gone.

This also **overturns a source-only inference**: reading Loretta's source suggests deep nesting
degrades to an `ERR_InsufficientStack` diagnostic. Measured, it does not — exactly the kind of
claim that survives a documentary review and fails a spike.

So the **size cap and nesting-depth pre-check belong in ZWarden, applied before the parser sees
the file**, and there is a standing prohibition on recursing over attacker-controlled tree depth
anywhere on the config path. PZ's real files are depth 2 and 4; a cap of 16 is generous. These
files are attacker-influenced: mods write to them, and so do the in-game admin panel and
server-settings editor. This is the config-path instance of the general rule in
`docs/trust-boundaries.md` §8 and §5, and it is the kind of rule PRD 15's architecture tests can
be written against.

## Consequences

- **`Loretta.CodeAnalysis.Lua` is a third single-maintainer dependency** (1,303 of ~1,405 commits
  from one person, 13 commits in the past 12 months, a stable release 18 months old with a
  nightly line a year ahead of it) alongside TUnit (ADR 2) and Blazor Blueprint (ADR 3). Same
  mitigation: pin the version, keep it behind a narrow seam, hold a generated real-file corpus as
  that seam's acceptance test. MIT with no native code, so **vendoring is a real fallback**.
- **`IPzConfigDocument` is a Feature 20a deliverable and a rule, not a convenience.** Open, read
  values, set a value, emit bytes — and nothing library-shaped crosses it.
- **"The file did not parse" is a first-class operator-facing state**, with line and column. It
  is a thing operators will hit, because mods and the in-game editors write these files.
- **Validation ranges and defaults cannot be derived from the shipped Lua** — they are Java-side.
  ZWarden's config schema is its own, maintained by hand. That is ongoing manual work, and it is
  a cost of this decision.
- **`reloadoptions` does not extend to sandbox vars** (`SandboxOptions` is absent from
  `ReloadOptionsCommand`'s entire reference set), so sandbox editing is inherently
  edit-then-restart. `<name>.ini` remains live-reloadable.
- Line endings are tolerated by the game and normalised to the host separator, so they are not
  something ZWarden must preserve — but the BOM absolutely is something ZWarden must never write.
