# Reading and writing Project Zomboid's Lua config files

Research resolving [#15](https://github.com/MCrank/ZWarden/issues/15) ("Choose how to read and
write Project Zomboid Lua config files"), a wayfinder research ticket under
[#1](https://github.com/MCrank/ZWarden/issues/1). Researched **2026-09-10**.

- **Scope:** the three Lua server-config files surfaced by [#5](https://github.com/MCrank/ZWarden/issues/5)
  — `<name>_SandboxVars.lua`, `<name>_spawnpoints.lua`, `<name>_spawnregions.lua` — plus the
  option space for reading and writing them from `net10.0`. PRD anchors: **32** (structured
  editing of INI, sandbox, spawn and mod configuration), **33** (every mutation creates a
  revision; "writes shall use safe atomic patterns"), **38** (untrusted-data posture),
  **59 Feature 20** ("INI parsing, supported Lua/config parsing, structured model, validation,
  atomic writes, config revisions").
- **Target build:** **42.20.4** (`version=42.20.4 b0bbce05d5`), the build [#5](https://github.com/MCrank/ZWarden/issues/5)
  verified. Facts about the game were re-verified against that build; nothing #5 settled is
  reopened here.
- **Nature of this document:** facts first, each with a source. Section 7 is the one part that
  is **judgement rather than fact** — it is fenced and labelled as such, because the ticket asks
  for a recommendation. Everything before it is evidence.
- **Volatility warning:** package versions and maintenance signals are point-in-time as of
  2026-09-10. Game behaviour is pinned to 42.20.4 and B43 is expected to change the sandbox
  option set.

---

## How to read this

Confidence labels are per claim group:

- **High** — official/primary source, or verified by direct execution during this research.
- **Medium** — primary source but indirect, or verified at one patch version and assumed stable
  across the patch line.
- **Low** — community/vendor sources only; a lead, not a fact.

Source tiers used:

| Tier | Source | Notes |
| --- | --- | --- |
| **Primary — executed** | `zombie.network.GameServer` from a local **42.20.4** install, run five times during this research against an isolated `-cachedir`, generating and re-reading the real config files. Marked **"verified by execution"**. | The strongest evidence here. The game generated the artefacts; ZWarden did not synthesise them. |
| **Primary — shipped binary** | `projectzomboid.jar` (42.20.4) — the writer's own **format string literals**, class references and method names, read out of the class-file constant pools; and the shipped Lua under `media/lua/` and `media/maps/`. | Reading string constants is not decompilation. See the licence note in §6.6. |
| **Primary — first-party docs** | The Indie Stone's own Javadoc at <https://projectzomboid.com/modding/zombie/SandboxOptions.html> (generator stamp `javadoc (25) on Wed Aug 19 20:42:15 BST 2026`), the PZ EULA / T&Cs, the Modding Policy. | Javadoc is not version-stamped but is dated seven days before 42.20.4 shipped. |
| **Primary — NuGet / repo** | `azuresearch-usnc.nuget.org/query`, `api.nuget.org` registration API, the `.nuspec` and zip entries **inside downloaded `.nupkg` files**, ECMA-335 metadata reflection over the shipped DLLs, and the GitHub API for licence and activity. | Package claims below were read from packages, not from READMEs. |
| **Primary — measured on net10.0** | A throwaway probe (§5) built and run on **.NET SDK 10.0.302 / `net10.0`**. | Marked **"measured"**. |
| **Primary-adjacent — game code** | Third-party Vineflower decompilations of `zombie.*`, version-pinned to 42.20.2 / 42.20.3. | Not Indie Stone-published. Used only for control flow, and only where a shipped-binary literal or an executed observation corroborates it. Same tier #5 used. |
| Lower confidence | `pzwiki.net` (official-ish wiki, 42.20.0-stamped pages), community modding guides. | Always labelled inline. The wiki's file listing is **B41-era and partly stale**; see §2.3. |

---

## 1. The short version

1. All three files are **executed as Lua by the game**, not read by a restricted parser. The
   interpreter is **Kahlua** (`se.krka.kahlua.*`) with a **LuaJ-derived Lua 5.1 compiler**
   (`org.luaj.kahluafork.compiler`, recovered from a live stack trace).
2. The subset the files actually use is **tiny**: `Key = value` fields, one level of nesting,
   scalars and quoted strings. But the two spawn files wrap their table in
   `function SpawnRegions() return { … } end`, so they are not bare data literals.
3. **The server rewrites `<name>.ini` and `<name>_SandboxVars.lua` on every single start**, from
   its in-memory model, regenerating all comments and canonicalising order, indentation and
   number formatting. It does **not** rewrite the two spawn files on start.
4. Therefore **round-tripping `_SandboxVars.lua` is a nicety, not a requirement** — anything
   ZWarden preserved would be destroyed by the next restart anyway. Round-tripping the two
   **spawn** files is worth more, because nothing else normalises them.
5. **A Lua syntax error in `_SandboxVars.lua` is fatal to server start** (`System.exit(1)`).
   That makes PRD 33's atomic-write requirement load-bearing rather than tidy: a torn write
   bricks the server.
6. **A UTF-8 BOM is fatal** — it crashes PZ's lexer, in its *error-reporting* path, with an
   `ArrayIndexOutOfBoundsException`. Line endings, by contrast, are tolerated and normalised.
7. For .NET 10 the option space has exactly one full-fidelity parser (**Loretta**,
   MIT, no native code, byte-exact round-trip **measured** on all five real PZ files) and three
   viable interpreters (**MoonSharp**, **NLua/KeraLua**, **LuaCSharp**). Every interpreter
   executes file contents as code. Loretta does not. **No package does both.**
8. Two defaults are traps, both measured: `new MoonSharp.Interpreter.Script()` hands a script
   live `io`, `os`, `load` and `require`, and Loretta's default `LuaSyntaxOptions` is `All`, the
   most permissive dialect superset rather than PZ's Lua 5.1.
9. **Nothing survives hostile nesting depth**, whatever is chosen: Loretta's process dies at
   ~1800 levels and MoonSharp's at ~3200, and a .NET `StackOverflowException` cannot be caught.
   A size and depth pre-check is ZWarden's job, not the library's.

---

## 2. What the files actually are

**Confidence: High — verified by execution.** The server was run as
`zombie.network.GameServer -cachedir=<scratch> -servername zwarden -nosteam` from the 42.20.4
install; it generated all four files itself. Every excerpt below is from that generated output.

### 2.1 `<name>_SandboxVars.lua`

**Root form is a global assignment, not a `return`.** 45,533 bytes, 1,020 lines, CRLF (on a
Windows host — see §3.4).

```lua
SandboxVars = {
    VERSION = 6,
    -- Changing this also sets the "Population Multiplier" in Advanced Zombie Options. Default = Normal
    -- 1 = Insane
    -- 2 = Very High
    -- 3 = High
    -- 4 = Normal
    -- 5 = Low
    -- 6 = None
    Zombies = 4,
    -- How zombies are distributed across the map. Default = Urban Focused
    -- 1 = Urban Focused
    -- 2 = Uniform
    Distribution = 1,
    -- Controls whether some randomization is applied to zombie distribution.
    ZombieVoronoiNoise = true,
```

…and, for the nested tables:

```lua
    Basement = {
        -- How frequently basements spawn at random locations. Default = Sometimes
        -- 1 = Never
        -- 7 = Always
        SpawnFrequency = 4,
    },
    Map = {
        -- If enabled, a mini-map window will be available.
        AllowMiniMap = false,
        -- If enabled, the world map can be accessed.
        AllowWorldMap = true,
    },
```

…closing:

```lua
        -- Rate at which Glassmaking skill levels up. Min: 0.00 Max: 1000.00 Default: 1.00
        Glassmaking = 1.0,
    },
}
```

Census of the generated file (counted directly):

| Property | Value |
| --- | --- |
| Root | `SandboxVars = { … }` — a **global assignment** |
| Depth-1 keys | **189** (including `VERSION` and the five nested-table openers) |
| Depth-2 keys | **86** |
| Nesting depth | **exactly 2.** Five nested tables: `Basement`, `Map`, `ZombieLore`, `ZombieConfig`, `MultiplierConfig` |
| Comment lines | **738** — machine-generated, `--` line comments only |
| Key form | **bare identifiers only.** Zero bracketed (`["x"]`) keys, zero positional entries |
| Trailing commas | **on every field, including the last one in every table** |
| Indentation | **4 spaces** at depth 1, **8 spaces** at depth 2. No tabs |
| Value types | booleans (45), doubles (99, always emitted as `1.0`, never `1`), integers, double-quoted strings (2) |
| `VERSION` | `6`, a hardcoded literal in the writer (`SANDBOX_VERSION`) |

The comment text is the in-game tooltip, rendered through the translation table, and it carries
UI rich-text markup — one real line from the generated file:

```lua
    -- <BHC> [!] It is recommended that you DO NOT change this. [!] <RGB:1,1,1>   Can be used to adjust the number of rolls made on loot tables when spawning loot. …
    RollsMultiplier = 1.0,
```

**Consequence: comments are not stable content.** They come from
`Translator.getTextOrNull("Sandbox_*_tooltip")` (primary-adjacent), so the same settings under a
different server locale produce a textually different file. Any diff, checksum or revision hash
ZWarden computes over these files must be taken over the **parsed values**, not the bytes.

The writer's own format literals, recovered verbatim from `zombie/SandboxOptions.class` in the
42.20.4 jar (`` marks a runtime-substituted argument in a Java string-concat recipe):

```text
return {
SandboxVars = {
    VERSION = 6,
    -- 
    --   = 
     = ,
     = {
        -- 
         = ,
    },
}
```

Note the `return {` variant: it exists for the *developer/preset* path only, and is never used
for a server file. This matters, and it is a trap — see §2.4.

### 2.2 `<name>_spawnregions.lua` and `<name>_spawnpoints.lua`

**These are not tables. They are function definitions.** The generated files, verbatim
(tabs shown as real tabs):

```lua
function SpawnRegions()
	return {
		{ name = "Muldraugh, KY", file = "media/maps/Muldraugh, KY/spawnpoints.lua" },
		{ name = "West Point, KY", file = "media/maps/West Point, KY/spawnpoints.lua" },
		{ name = "Rosewood, KY", file = "media/maps/Rosewood, KY/spawnpoints.lua" },
		{ name = "Riverside, KY", file = "media/maps/Riverside, KY/spawnpoints.lua" },
		-- Uncomment the line below to add a custom spawnpoint for this server.
--		{ name = "Twiggy's Bar", serverfile = "zwarden_spawnpoints.lua" },
	}
end
```

```lua
function SpawnPoints()
	return {
		unemployed = {
			{ worldX = 40, worldY = 22, posX = 67, posY = 201 }
		}
	}
end
```

| Property | Value |
| --- | --- |
| Root | `function SpawnRegions()` / `function SpawnPoints()` returning a table — **a global function that must be called** |
| Indentation | **tabs** (1–4 deep), not spaces |
| Comments | **present in the shipped default**, including a commented-out template line the operator is invited to uncomment |
| `spawnregions` entries | positional Lua sequence of `{ name = …, file = … }` or `{ name = …, serverfile = … }`; `file` resolves under `media/maps/`, `serverfile` under `Zomboid/Server/` |
| `spawnpoints` entries | keyed by profession, each a sequence of `{ posX, posY, posZ }`; a **legacy** `{ worldX, worldY, posX, posY }` cell-relative form is still accepted on read and normalised to absolute (`cell * 300 + pos`) |
| Profession keys | bare identifiers **unless the name needs quoting** — the writer emits `["park ranger"] = {` for names containing a space or backslash. **Both key forms must be handled** (primary-adjacent: `SpawnRegions.fmtKey`) |
| Trailing commas | on every entry; **no** trailing comma after the closing `\t}` |

Writer literals from `zombie/network/SpawnRegions.class` (42.20.4):

```text
\treturn {
\t\t{ name = , file =  },
\t\t{ name = , serverfile =  },
\t\t\t\t{ posX = , posY = , posZ =  },
```

### 2.3 Two stale wiki facts, corrected

- `pzwiki.net/wiki/Server_settings` (42.20.0-stamped) documents the spawnpoint schema as
  `{ worldX = 40, worldY = 22, posX = 67, posY = 201 }`. The 42.20.4 **writer** emits only
  absolute `{ posX, posY, posZ }`; the reader accepts both. The wiki form is a legacy *input*
  shape, not the current output.
- The wiki's file table describes all four files as "editable with a text editor", which is
  true but incomplete: for `.ini` and `_SandboxVars.lua` those edits are **normalised away on
  the next restart** (§3.1).

### 2.4 What the shipped `media/lua/shared/Sandbox/` files are — and are not

**Confidence: High — read from the 42.20.4 install.** These are easy to mistake for the server
file, and they are a different format:

- `media/lua/shared/Sandbox/SandboxVars.lua` is **four lines** and is the *loader*, not data:

  ```lua
  SandboxVars = require "Sandbox/Apocalypse"

  -- This is needed to add custom sandbox options to the SandboxVars table.
  getSandboxOptions():initSandboxVars()
  ```

- The five presets (`Apocalypse.lua`, `Extinction.lua`, `Outbreak.lua`, `Rising.lua`,
  `SixMonthsLater.lua`) use the **`return { … }`** form, carry **no comments**, have **no
  trailing comma** on the last entry, and spell the version key **`Version = 6`** — lower-case
  `ersion` — while the server file uses **`VERSION`**. The loader does a case-sensitive
  `rawget("VERSION")`, so **a preset file is not a drop-in server file**.
- **The presets are value tables, not a schema.** Types, ranges, defaults, ordering and the
  enum labels live in Java behind `getSandboxOptions()` (five option classes:
  `Boolean/Double/Enum/Integer/String SandboxOption`, plus `StrongEnumSandboxOption<E>`). If
  ZWarden wants a validation schema, the shipped Lua is not where it is.

### 2.5 The map spawnpoints files are real code, and out of reach

**Confidence: High — read from the install.** `media/maps/<map>/spawnpoints.lua` — the files
`spawnregions.lua` points at with `file =` — are not data literals at all:

```lua
function SpawnPoints()
    local poor_houses = {
        { posX = 10770, posY = 10271, posZ = 0 },
        …
    }
    local medium_houses = { … }
    return {
        policeofficer = mergeTable(poor_houses, medium_houses, police_station),
        engineer = mergeTable(medium_houses, rich_houses),
        unemployed = poor_houses,
        …
    }
end
```

They use `local` bindings and call PZ's own global `mergeTable`. Evaluating them faithfully
would require not just a Lua interpreter but PZ's global environment. **ZWarden should not try
to resolve these**: treat a `file =` region as an opaque reference and only structurally edit the
server-owned `serverfile =` form. (Six of the eleven shipped maps have a 7-line stub file; only
Muldraugh, Riverside, Rosewood and West Point have real ones.)

---

## 3. What the game does on read and write

### 3.1 The server rewrites `.ini` and `_SandboxVars.lua` on every start

**Confidence: High — verified by execution, five server runs.**

Measured directly. Starting from the pristine generated `_SandboxVars.lua`, six hand edits were
applied: a `--` line comment, a `--[[ ]]` block comment, an unknown key
`ZWardenUnknownKey = 42`, a moved key (`ZombieMigrate` hoisted to the top), a tab-indented
field, a single-quoted string value, and one genuine value change (`Zombies = 4` → `3`). The
server was then started and reached `*** SERVER STARTED ****`, logging:

```text
LOG  : General  f:0 st:7,165,009,818> writing …\zomboid\Server\zwarden_SandboxVars.lua
```

Diffing the resulting file against the **pristine** generated file gives exactly one hunk:

```diff
10c10
<     Zombies = 4,
---
>     Zombies = 3,
```

Everything else was reverted to canonical form. Specifically:

| Hand edit | Fate |
| --- | --- |
| `-- ZWARDEN PROBE hand comment` | **destroyed** |
| `--[[ ZWARDEN PROBE block comment ]]` | **destroyed** |
| `ZWardenUnknownKey = 42` | **silently dropped**, no warning in `server-console.txt` |
| `ZombieMigrate` moved to the top | **reordered back** to its canonical position (line 24) |
| tab indentation | **normalised** to 4 spaces |
| `LootItemRemovalList = ''` (single quotes) | **normalised** to `""` |
| `Zombies = 3` | **kept** |

The mechanism, from `zombie/network/GameServer` (primary-adjacent control flow, corroborated by
the `saveServerLuaFile` / `loadServerLuaFile` / `Exiting due to errors loading ` literals present
in `GameServer.class` at 42.20.4, and by the observed `writing …` log line): the startup path
calls `loadServerLuaFile`, then `saveServerLuaFile` **unconditionally on both branches** — the
file-exists branch and the file-missing branch.

The `.ini` is rewritten too. Across two consecutive restarts with no operator edits at all, the
file changed each time — and the only difference was inside a **comment**:

```diff
63c63
< # Reset ID determines if the server has undergone a soft-reset. … Default: 473486523
---
> # Reset ID determines if the server has undergone a soft-reset. … Default: 630847557
```

`ResetID=9837773` itself was unchanged. So the INI's comments are regenerated from code on
every start, complete with a freshly randomised `Default:` figure quoted inside the comment
text. A byte-level diff of the INI is therefore **guaranteed** to show spurious changes between
restarts.

### 3.2 The server does **not** rewrite the two spawn files on start

**Confidence: High — verified by execution.** A comment inserted into
`zwarden_spawnregions.lua` **survived** a full successful server start and shutdown:

```diff
2a3
> 		-- ZWARDEN PROBE region comment
```

`zwarden_spawnpoints.lua` was byte-identical before and after. Corroboration: `GameServer.class`
has **zero** constant-pool references to `saveRegionsFile` or `savePointsFile`, and
`zombie/iso/SpawnPoints.class` contains no `FileWriter` at all.

The only writer of these two files is `zombie.network.ServerSettings.saveFiles()` — the
**in-game** host/server-settings editor, which writes all four files together and is
`@UsedFromLua`. That editor is a second author ZWarden does not control.

### 3.3 A syntax error is fatal to server start

**Confidence: High — verified by execution.** An accidental but instructive mutation (a field
left without its separating comma — which is invalid in a Lua table constructor) produced:

```text
SEVERE: Error found in LUA file: …/zwarden_SandboxVars.lua
se.krka.kahlua.vm.KahluaException: zwarden_SandboxVars.lua:186: '}' expected (to close '{' at line 1)
    near `RemoveStoryLoot` at Lua.zwarden_SandboxVars.lua(zwarden_SandboxVars.lua:186).
	zombie.SandboxOptions.readLuaFile(SandboxOptions.java:1591)
	zombie.SandboxOptions.loadServerLuaFile(SandboxOptions.java:1442)
LOG  : General  f:0 st:7,164,937,034> Exiting due to errors loading …\zwarden_SandboxVars.lua
LOG  : General  f:0 st:7,164,937,043> Shutdown handling started
LOG  : Network  f:0 st:7,164,937,045> Server exited
```

**This is the strongest argument in this document for PRD 33's atomic writes.** A partially
written `_SandboxVars.lua` — the ordinary outcome of a crash, a full disk, or a container kill
mid-write — does not degrade the server. It prevents the server from starting, with no
self-healing path, because the load happens before the world is touched.

Two further fatal shapes, from the loader (primary-adjacent, `SandboxOptions.readLuaFile` /
`upgradeLuaTable`, corroborated by the matching literals in the 42.20.4 class file):

- the file parses but never assigns the global `SandboxVars` — e.g. if ZWarden wrote the
  preset-style `return { … }` form — → `System.exit(1)`;
- **any** positional/array entry anywhere in the table → `IllegalStateException("expected a
  String key")` → `System.exit(1)`.

By contrast the **spawn** files fail soft: a load failure logs and falls back to
`getDefaultServerRegions()`. They also require the global to be a *function*; a bare table is
silently ignored.

### 3.4 A UTF-8 BOM is fatal. Line endings are not.

**Confidence: High — verified by execution, bisected over two runs.**

Writing the identical file as **UTF-8 with BOM + LF endings** killed the server:

```text
SEVERE: Error found in LUA file: …/zwarden_SandboxVars.lua
java.lang.ArrayIndexOutOfBoundsException: Index 65022 out of bounds for length 31
    at LexState.token2str(LexState.java:247)
	org.luaj.kahluafork.compiler.LexState.txtToken(LexState.java:262)
	org.luaj.kahluafork.compiler.LexState.lexerror(LexState.java:270)
	org.luaj.kahluafork.compiler.LexState.syntaxerror(LexState.java:285)
	org.luaj.kahluafork.compiler.LexState.prefixexp(LexState.java:1075)
LOG  : General> Exiting due to errors loading …\zwarden_SandboxVars.lua
```

Note what that trace shows: PZ's Lua 5.1 lexer does not skip a BOM (Lua 5.1 never did), *and*
its error-reporting path itself throws while trying to render the offending token. The failure
is unrecoverable and the diagnostic is garbage.

The same file written as **UTF-8 without BOM + LF endings** started cleanly, was rewritten by
the server, and came back **with CRLF** — the host's `System.lineSeparator()`. The value change
carried through (`Zombies = 6`).

**Rules this fixes for a ZWarden writer:** UTF-8, **no BOM**, ever. Line endings do not matter
on input; on output prefer the host's convention, and never treat them as significant on read.

### 3.5 Sandbox settings are not live-reloadable

**Confidence: Medium (primary-adjacent, corroborated by the shipped class's reference set).**
#5 established that `<name>.ini` is live-reloadable via the `reloadoptions` admin command. That
command does **not** extend to sandbox vars: `ReloadOptionsCommand`'s only `zombie/*` references
are `commands/CommandBase`, `core/logger/ZLogger`, `core/raknet/UdpEngine`, `core/znet/SteamUtils`,
`network/GameServer` and `network/ServerOptions` — `SandboxOptions` is **absent**. `changeoption`
is likewise INI-only.

So a sandbox change requires a **restart** — and the restart immediately normalises whatever
ZWarden wrote. This is worth recording in the Feature 20 plan: the operator-visible workflow for
sandbox edits is inherently "edit, then restart", not "edit and apply".

The one other path that writes the file at runtime is
`GameServer.receiveSandboxOptions` — the handler for the in-game admin panel's sandbox packet,
which applies the change and calls `saveServerLuaFile`. **A third party can change these files
under ZWarden's feet**, which bears on PRD 33: a revision chain must be able to detect
out-of-band change (an mtime/parsed-value fingerprint), not assume ZWarden is the sole author.

### 3.6 Therefore: round-tripping is a nicety, atomicity is a requirement

Stated plainly, because the ticket asks:

| File | Does the game normalise it? | Is round-trip fidelity worth anything? |
| --- | --- | --- |
| `<name>.ini` | **Yes, every start** — comments regenerated | **No.** Nothing an operator writes survives one restart |
| `<name>_SandboxVars.lua` | **Yes, every start** — comments, order, indentation, number format, unknown keys | **No** — with one caveat below |
| `<name>_spawnregions.lua` | **No** on start; only the in-game editor rewrites it | **Yes.** Comments and the commented-out template line survive indefinitely, and operators do hand-edit this file |
| `<name>_spawnpoints.lua` | **No** on start | **Yes**, same reasoning |

The caveat on `_SandboxVars.lua`: fidelity still buys something *between* ZWarden's write and
the next restart. A structured edit that rewrites the whole file from a ZWarden-side model risks
dropping any key the model does not know about — a mod-added custom sandbox option, or a new
option from a game patch ZWarden has not been taught. A **surgical** edit that replaces only the
value tokens it means to change cannot lose an option it has never heard of. That is a
correctness argument for a fidelity-preserving parser, not an aesthetic one, and it is
independent of whether the game later reformats the file.

---

## 4. The trust posture: these files are attacker-influenced

**Confidence: High for the mechanism; Medium for the specific mod-write route.**

PRD 38 makes logs untrusted data. The same reasoning applies here, and more strongly, because
these files are *executable*:

1. **The game executes them.** `SandboxOptions.readLuaFile` nils the `SandboxVars` global,
   evicts the path from `LuaManager.loaded`, then calls `LuaManager.RunLua(path)` and reads the
   global back. `SpawnRegions` does the same and then `pcall`s the returned closure. Arbitrary
   Lua planted in any of these files **runs inside the game server at start**, before the world
   loads, with mod-level privilege.
2. **Mods have file-write primitives.** PZ's Kahlua environment registers
   `MathLib, BaseLib, RandomLib, UserdataArray, StringLib, CoroutineLib, OsLib, TableLib,
   LuaCompiler` and nothing else — verified: PZ's shipped `stdlib.lua` never mentions `io`,
   `os.execute`, `dofile`, `loadfile`, `load` or `loadstring`, and Kahlua's stdlib package
   contains `BaseLib, CoroutineLib, OsLib, RandomLib, StringLib, TableLib` and `MathLib` with
   **no `IoLib`**. File I/O therefore goes exclusively through PZ's exposed Java surface — and
   that surface includes writers. `LuaManager$GlobalObject` exposes to Lua, among others,
   `getFileWriter(String,boolean,boolean)`, `getModFileWriter`, `getFileOutput`, `getFileReader`,
   `getFileInput`, `serverFileExists` and `reloadServerLuaFile`, plus a
   `LuaManager$GlobalObject$LuaFileWriter` inner class and path recipes for both the `Server` and
   `Lua` directories.
3. **The generic writers cannot reach these files, but a specific one can.** `getFileWriter` is
   rooted at `Zomboid/<cache>/Lua/`, rejects relative paths, and enforces
   `ALLOWED_FILE_EXTENSIONS = {ini, cfg, txt, log, json}` — the literals are present in
   `LuaManager.class` at 42.20.4, and `.lua` is not among them. But `zombie/network/ServerSettings`
   *is* on the Lua exposer's whitelist, and it carries `saveServerLuaFile`, `saveRegionsFile`,
   `savePointsFile` and `saveSpawnPointsFile`. `SpawnRegions.class` contains **zero** references
   to the `containsDoubleDot` traversal guard that `SandboxOptions.class` does reference. That
   asymmetry means the spawn-file write path is the weak one. **Not fully verified:** whether
   every caller of that path is gated server-side at a higher layer.
4. **Even without a mod, ZWarden's own product surface makes the input untrusted.** PRD 32
   offers "raw text editing … as an advanced capability". The moment an operator can paste text
   into a box, arbitrary bytes reach ZWarden's reader. The reader must be safe against them on
   its own merits, regardless of what mods can do.

**Conclusion for ZWarden: `Zomboid/Server/*.lua` is untrusted input on the read path. Never
evaluate it to read it.**

---

## 5. The .NET 10 option space

### 5.1 Package inventory

Read from NuGet's search and registration APIs and from inside the downloaded `.nupkg` files
(nuspec, zip entries, and ECMA-335 reflection over the shipped assemblies), 2026-09-10.

| Package id (case-exact) | Latest stable | Published | Shipped TFMs | net10.0 | Licence | Native? | Parses to a tree? | Executes? | Preserves comments? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `Loretta.CodeAnalysis.Lua` | **0.2.13** (`0.2.14-nightly.26` newer) | 2025-03-30 (nightly 2026-03-30) | ns2.0, net8.0 | via `net8.0` | **MIT** (SPDX in nuspec) | **No** | **Yes** | **No** | **Yes — full Roslyn trivia** |
| `Loretta.CodeAnalysis.Common` | 0.2.13 | 2025-03-30 | ns2.0, net8.0 | via `net8.0` | MIT (SPDX) | No | (base) | No | (`ToFullString` lives here) |
| `LuaCSharp` | **0.5.6** | **2026-07-29** | ns2.1, net6.0, net8.0, **net10.0** | **direct** | MIT (SPDX) | No | Yes (public AST) | **Yes** | **No — no `Comment` token exists** |
| `NLua` | **1.7.9** | 2026-05-01 | ns2.0, net46, net8.0, net9.0-* | via `net8.0` | MIT (repo file) | via KeraLua | No | **Yes** | No |
| `KeraLua` | **1.4.9** | 2026-01-18 | as NLua | via `net8.0` | MIT (repo file) | **Yes** — Lua 5.4 | No | **Yes** | No |
| `MoonSharp` | **2.0.0 (2016)**; `3.0.0-beta.1` 2026-07-04 | see left | 2.0.0: net35/40, PCL · 3.0.0-b1: net45, **ns2.0** | via `ns2.0` | **BSD-3-Clause** (repo file; **no SPDX on NuGet**) | No | internally; **not publicly** | **Yes** | No |
| `NeoLua` | 1.3.19 | 2025-12-04 | ns2.0, net451, net48, net6.0 | via `net6.0` | Apache-2.0 (SPDX) | No | lexer public, no AST | **Yes** | No |
| `WattleScript.Interpreter` | 1.0.0 | 2022-10-10 | ns2.0/2.1, net5–7 | via `net7.0` | BSD-3-Clause | No | No | **Yes** | No |
| `CXuesong.Luaon` | 0.2.7 | 2023-04-16 | ns1.1, ns2.0, net6.0 | via `net6.0` | Apache-2.0 (SPDX) | reader/writer, no tree | No | No | serializer only |
| `CSLua` | 0.0.8 | 2026-07-30 | **net10.0** only | direct | MIT | No | bytecode front-end | **Yes** | No |
| `AsyncLua` | 0.5.0 | 2026-07-27 | ns2.0 | via `ns2.0` | MIT | No | Yes | **Yes** | No |

**Rejected, with the reason:**

| Package | Reason |
| --- | --- |
| `KopiLua` 1.0.6752.15716 | net461 only (**cannot** be consumed from `net10.0`), last commit 2019, **no LICENSE file in the repo at all**, and its nuspec `licenseUrl` points at *the Wikipedia article about the MIT licence*. Unlicensed in practice |
| `SharpLua` 2.1.1.1 | net40, published 2012, last commit 2013. A genuine loss: it is the only other library found with an exact-round-trip visitor (`SharpLua.Visitors.ExactReconstruction`) |
| `DynamicLua` | net40-Client, 2014; repo carries both an Apache and an MIT file with no statement of which applies |
| `Luau` / `Luau.Native` | repo `nuskey8/luau-dotnet` is **archived**. Also Luau ≠ Lua |
| `Loom.Parser.Lua` 2.0.0 | net10.0-native but 250 downloads, 1 star, and **no licence declared in the nuspec**. Its lexer has a `Comment` kind but comments never reach the AST |
| `LuaTableSerializer` 1.0.2 | **no licence declared**, and `repository.url` is empty. Unattributable |
| `lua` 5.5.1 (94,647 downloads) | **not a .NET package.** `build/native/*` MSVC only, no managed assembly, Windows-only. Easy to pick by mistake from a downloads-sorted search |
| `Lua.NET` 6.0.1 | native, **no linux-arm64** |
| `OpenRA-Eluant` | expects a system `liblua` you provide; a single-game internal fork |

**Package ids that do not exist** (404 on the registration API), recorded so nobody looks again:
`MoonSharp.Interpreter` (that is the *assembly* name; the package is `MoonSharp`),
`Loretta.CodeAnalysis` (the base package is `Loretta.CodeAnalysis.**Common**`), `UniLua`, `LuaN`,
`Sharp.Lua`. `Eluant`'s registration index is empty.

### 5.2 Loretta, in detail

**Confidence: High — verified from the package and measured on net10.0.**

- Three package ids: `Loretta.CodeAnalysis.Common`, `Loretta.CodeAnalysis.Lua`,
  `Loretta.CodeAnalysis.Lua.Experimental` (a minifier and constant folder — **not needed**).
- **Licence MIT**, declared as a real SPDX expression (`<license type="expression">MIT`) inside
  the nuspec, which is stronger evidence than a `licenseUrl`. Source files carry the Roslyn
  header; it is a Roslyn fork. Note: `raw.githubusercontent.com/LorettaDevs/Loretta/main/LICENSE`
  404s — there is no LICENSE file at that path — though the GitHub API reports `spdx_id = MIT`.
- **Zero native binaries.** No `runtimes/` entries in any of the three packages. Fully managed,
  so container base image (glibc vs musl) is a non-issue.
- **net10.0 resolution: verified by restore, not inferred.** The probe's `project.assets.json`
  selects `lib/net8.0/Loretta.CodeAnalysis.Lua.dll` for `net10.0`, with no fallback warning. It
  pulls one extra transitive package, **`Tsu` 2.2.2** (same author) — worth knowing, since PRD 55
  cares about the dependency tree.
- **Maintenance: the real risk.** Repo `LorettaDevs/Loretta` (the old `GGG-KILLER/Loretta`
  redirects there — moved to an org, not abandoned). Not archived. Last commit **2026-03-30**,
  **13 commits in the past 12 months**, 7 open issues, 151 stars. Contributor split
  1,303 / 77 / 11 / 10 / 4 commits — **bus factor 1**. The stable release is 18 months old and
  the `0.2.14-nightly.*` line is a year newer than it, published to NuGet as prereleases with no
  stated stability guarantee.
- **Dialects:** `LuaSyntaxOptions` presets for Lua 5.1–5.4, LuaJIT 2.0/2.1, FiveM, GLua and Luau.
  The **default is `All`**, which is maximally permissive — it would silently accept GLua `!=`
  and Luau type annotations that PZ's Kahlua would reject. **Pin `LuaSyntaxOptions.Lua51`**, which
  matches PZ's Lua 5.1 lineage and turns "would the game accept this?" into a parse-time check
  rather than a runtime surprise.
- **No "lossless"/"full-fidelity" marketing claim exists** in the README or docs — a code search
  for `fidelity` across the repo returns one hit in an unrelated file. What exists instead is
  stronger than a README promise and should be cited in its place: the inherited Roslyn API
  contract on `SyntaxNode.ToFullString()` — *"Returns full string representation of this node
  including its leading and trailing trivia … The length of the returned string is always the
  same as FullSpan.Length"* — plus the fact that Loretta's own test harness **enforces**
  round-trip as a precondition on effectively every parser test:

  ```csharp
  public static async Task<SyntaxTree> ParseWithRoundTripCheckAsync(string text, LuaParseOptions options = null) {
      var tree = await ParseAsync(text, options: options ?? LuaParseOptions.Default);
      var parsedText = await tree.GetRootAsync();
      // we validate the text round trips
      await Assert.That(text).IsEqualTo(parsedText.ToFullString());
      return tree;
  }
  ```

  Trivia is first-class in the syntax kinds — `ShebangTrivia`, `SingleLineCommentTrivia`,
  `MultiLineCommentTrivia`, `WhitespaceTrivia`, `EndOfLineTrivia`, `SkippedTokensTrivia` — and the
  shipped public API carries **277** `With*` methods plus `LuaSyntaxRewriter` and
  `LuaSyntaxWalker` for transformation, and `NormalizeWhitespace` for when reformatting is
  actually wanted.
- **The dialect ladder is worth knowing when pinning.** `Lua52` = `Lua51` **plus** empty
  statements, `goto`, hex escapes, hex float literals, whitespace escapes and nested long
  strings; `Lua53` adds bitwise operators, `\u` escapes and floor division; `Lua54` adds
  `<const>`/`<close>` attributes. Pinning `Lua51` therefore *rejects* syntax PZ's own Kahlua
  would also reject, which is the desired behaviour — ZWarden's accepted language should not be
  wider than the game's.
- **The one practical caveat, same as Roslyn:** trivia on untouched nodes survives, but nodes you
  build with `SyntaxFactory` start with **no trivia** — you must carry it across yourself with
  `WithTriviaFrom` / `WithLeadingTrivia`. Loretta's own tutorial teaches exactly this. Budget for
  it; it is not automatic.

### 5.3 Do the candidates execute file contents as code?

The ticket asks for this explicitly, per candidate. **Confidence: High** — measured for Loretta
and MoonSharp, read from the projects' own source and shipped assemblies for the rest.

| Candidate | Executes file contents as code? |
| --- | --- |
| **`Loretta.CodeAnalysis.Lua`** | **No.** There is no VM, no `eval`, no `Execute`/`DoString` anywhere in the public surface — it lexes, parses, analyses and re-emits. A repo-wide code search for `System.Diagnostics.Process` returns zero hits. Measured: a file containing `os.execute("rm -rf /")` and `io.open("/etc/passwd")` parses to inert syntax nodes with **0 diagnostics and nothing run**, and `while true do end` parses in 5.9 ms and does nothing. **Naming trap:** Loretta has a type called `Loretta.CodeAnalysis.Lua.Script` — it is a static-analysis *scope container* (`RootScope`, `GetScope`, `GetVariable`), not an executor. Do not confuse it with `MoonSharp.Interpreter.Script` |
| **`MoonSharp`** | **Yes**, and **the default constructor is the unsandboxed preset.** `public Script() : this(CoreModules.Preset_Default)`, whose own XML doc reads *"Includes everything except "debug" as now. Beware that using this preset allows scripts unlimited access to the system."* Measured — `new Script()` exposes `io = Table`, `os = Table`, `load`/`loadfile`/`dofile` = `ClrFunction`, `require` = `Function`, and `type(os.getenv)..type(io.open)..type(load)` returns `"function/function/function"`. So `new Script().DoString(File.ReadAllText(path))` — the obvious first line anyone writes — hands an attacker-authored file `os.execute`. `CoreModules.None` **does** close that surface (measured, §5.6), but execution still happens and there is **no instruction budget, no memory cap and no `CancellationToken` anywhere in the interpreter** — the only `CancellationToken` in the repository is in a TypeScript file in the VS Code debug adapter. `Coroutine.AutoYieldCounter` is the sole budget mechanism and its guard is `State != CoroutineState.Main`, so `DoString` gets no budget at all; and it *yields* rather than aborting |
| **`NLua` + `KeraLua`** | **Yes, in native code, and with CLR reflection on by default.** These are P/Invoke bindings over the reference **Lua 5.4 C library**; `KeraLua.NativeMethods` is a `[SuppressUnmanagedCodeSecurity]` block of `[DllImport("lua54")]` declarations. Two compounding problems: (1) `public Lua(bool openLibs = true)` calls `luaL_openlibs`, so the full `io`/`os`/`load`/`require`/`debug` set is **open by default**; (2) `NLua.Lua.Init()` runs **unconditionally** in every constructor and executes a bootstrap chunk ending `luanet.load_assembly('mscorlib')`, with an `__index` metamethod resolving dotted CLR type names on demand. NLua's own README advertises this: *"You can use/instantiate any .NET class without any previous registration or annotation."* That sentence is the finding. Container note: both Linux `.so` files are **glibc** builds (`GLIBC_2.14+` x64, `2.17` arm64) and **will not load on an Alpine/musl image**; no musl variant exists on NuGet |
| **`LuaCSharp`** | **Yes** — `LuaState.Create()` + `DoStringAsync`. It is, to be fair, the **best-engineered** of the three interpreters and the only one that is secure by default: `LuaState.Create()` builds an **empty** environment and nothing is added until you call one of ten individual `Open*Library()` methods; a `CancellationToken` is checked **inside the opcode loop including on backward jumps**, with the project's own test cancelling a 1e9-iteration Lua loop after 100 ms; the parser calls `RuntimeHelpers.TryEnsureSufficientExecutionStack()` plus a `MaxCallCount = 200` limit and degrades to a `LuaCompileException`; interop is a compile-time source generator (`[LuaObject]`/`[LuaMember]`) with no reflection. Its disqualifiers here are different: it is **Lua 5.2**, not 5.1; strings are UTF-16 so `string.len` differs from the game's; `LuaState` is explicitly not thread-safe; it is pre-1.0 (0.5.6); and its lexer has **no `Comment` token at all** (the `SyntaxTokenType` enum's 55 members contain none; `SyntaxToken` carries only `Type/Text/Position`), so round-trip is structurally impossible |
| **`NeoLua`, `WattleScript.Interpreter`, `CSLua`, `AsyncLua`** | **Yes**, all four. None exposes a trivia-carrying AST |
| **`CXuesong.Luaon`** | **No** — it is a serializer/reader pair (`LuaConvert`, `LuaTableTextWriter`, `LuaTableTextReader`, plus a Json.NET-shaped LINQ layer). It does not execute. But it is **dormant since 2023-04-16**, 10 stars, and it would be a second dependency where Loretta can already emit a table constructor |
| **A hand-rolled reader/writer** | **No**, by construction — provided it is a reader and not an evaluator |

### 5.4 What "native" costs, specifically

**Confidence: High** — NVD and lua.org, read per record for the two headline entries.

This is the part of the trust argument that is not a matter of taste. The single most on-point
record, verbatim from NVD:

> **CVE-2022-28805** — "singlevar in **lparser.c** in Lua from (including) 5.4.0 up to (excluding)
> 5.4.4 lacks a certain luaK_exp2anyregup call, leading to a heap-based buffer over-read **that
> might affect a system that compiles untrusted Lua code**." CVSS v3.1 **9.1 CRITICAL**.
> <https://nvd.nist.gov/vuln/detail/CVE-2022-28805>

Read the emphasis: NVD's own threat model is *compiling* untrusted Lua. **With a native
interpreter, "we only parse it, we never run it" is not a memory-safety boundary.** A managed
parser cannot have this class of bug at all.

The class is live, not historical:

> **CVE-2025-49844 ("RediShell")** — Redis advisory, titled *"Lua Use-After-Free may lead to
> remote code execution"*, CWE-416, **CVSS 9.9**. "An authenticated user may use a specially
> crafted Lua script to manipulate the garbage collector, trigger a use-after-free and
> potentially lead to remote code execution."
> <https://github.com/redis/redis/security/advisories/GHSA-4789-qfc9-5f9q>

Thirteen further NVD records touch Lua 5.4.x, including CVE-2020-15889 (9.8), CVE-2022-33099
(7.5, heap overflow in `luaG_runerror`), CVE-2021-44964 (use-after-free) and CVE-2021-45985
(heap over-read).

And the shipped version is behind: at released tag **v1.4.9** (the current NuGet) KeraLua's `lua`
submodule pins **Lua 5.4.8**. `main` was bumped to 5.4.9 on 2026-09-01, *after* that release.
Lua 5.4.8 has four bugs documented on <https://www.lua.org/bugs.html>, one of which is:

> **"Constructors with nils can overflow counters during parsing"** — reported 2025-08-26,
> "existed since 5.4 (at least)", fixed by adding a missing `checklimit(fs, cc.tostore + cc.na +
> cc.nh, INT_MAX/2, "items in a constructor")` **in the table-constructor parser**.

That is a parsing bug in precisely the syntax construct ZWarden would be reading, present in the
currently shipped package. lua.org also notes 5.4.9 "was the last release of Lua 5.4" — a
terminal branch.

**The supply-chain consequence is the quiet one:** NuGet shows no vulnerability banner on
`KeraLua` or `NLua`, and it cannot — NuGet's advisory database indexes managed packages and has
no visibility into a bundled native `lua54` binary. ZWarden would never be told its embedded C
interpreter was out of date. Under PRD 55 that monitoring obligation is permanent and manual.

In fairness, native Lua has two mitigations the managed interpreters lack: a real instruction-count
watchdog (`KeraLua.SetHook` with `LuaHookMask.Count`) and a hard syntactic nesting cap
(`LUAI_MAXCCALLS 200` in `llimits.h`, so deep nesting is a Lua error rather than a crash).
Neither offsets the two paragraphs above.

### 5.5 What a hand-rolled reader/writer would involve

**Confidence: High for the surface it must cover** (derived from the generated files and the
writer's own literals); **Medium** for the effort estimate, which is judgement.

The subset is genuinely small, and that is the case *for* hand-rolling:

- one root form per file (`Name = { … }` for sandbox; `function Name() return { … } end` for the
  two spawn files);
- fields are `identifier = value` and, in spawnpoints only, `["quoted name"] = value`;
- values are: `true` / `false`, an integer, a decimal, a double-quoted string with `\\` and `\"`
  escapes only, or a nested table;
- table members may be keyed **or positional** (spawnregions is a positional sequence);
- separators are `,` (the writer always emits one, including trailing); Lua also permits `;`, and
  a hand-rolled reader must accept it because the file is not always machine-written;
- comments: `--` to end of line, and `--[[ … ]]` / `--[==[ … ]==]` block form — the game's own
  generated `spawnregions.lua` contains a commented-out template line, so comment support is
  mandatory on the read path, not optional;
- depth is 2 in sandbox, 4 in spawnpoints.

What makes it more work than it looks:

1. **Lua's long-bracket forms.** `[[...]]`, `[==[...]==]` for both strings and comments, with
   matching level counts and the leading-newline rule. Skipping these means a valid file
   ZWarden refuses.
2. **Number formats.** Lua 5.1 accepts `0x1A`, `1e-3`, `.5`, `3.`. The game never writes them;
   an operator can.
3. **Escapes.** `\n`, `\t`, `\\`, `\"`, `\'`, `\ddd`. The game emits only two of them.
4. **The two files that are functions.** A pure table-literal reader does not read them at all
   without special-casing the `function X() return` … `end` wrapper.
5. **Error reporting an operator can act on.** Line and column, and the offending token. This is
   most of the work in any hand-rolled parser and is exactly what Loretta gives away.
6. **Hostile input.** Depth limits, size limits, and never recursing without a bound. §5.6 shows
   this is not hypothetical.
7. **Round-trip, if wanted at all.** Preserving comments and layout means building a trivia
   model — which is re-implementing the interesting half of Loretta.

A read-only, non-round-tripping reader for the exact observed subset is a plausible few hundred
lines plus a real test corpus. A round-tripping one is a different project.

### 5.6 Hostile-input behaviour, measured

**Confidence: High — measured** on `net10.0`, SDK 10.0.302.

Loretta 0.2.13, `LuaSyntaxOptions.Lua51`:

```text
  unterminated string          threw=no diagnostics=2 roundtrip=True 100.6ms  first="Unfinished string"
  unterminated table           threw=no diagnostics=1 roundtrip=True  11.8ms  first="} expected"
  unterminated block comment   threw=no diagnostics=2 roundtrip=True   2.1ms  first="Unfinished multi-line comment"
  missing separator            threw=no diagnostics=2 roundtrip=True   9.4ms  first="} expected"
  os.execute call              threw=no diagnostics=0 roundtrip=True   2.9ms
  shell out via io             threw=no diagnostics=0 roundtrip=True   2.5ms
  while true                   threw=no diagnostics=0 roundtrip=True   5.9ms
  100k keys                    threw=no diagnostics=0 roundtrip=True 961.1ms
```

Malformed input **never throws** — it produces diagnostics, and the round-trip still reproduces
the input exactly. Malicious *content* is inert, because nothing is executed.

**But nesting depth kills the process.** Parsing `t = ` followed by *n* nested empty tables:

| Depth | Result |
| --- | --- |
| 100 / 500 / 1000 / 1200 / 1400 / **1600** | parsed, 0 diagnostics, round-trip exact |
| **1800 and above** (tested to 20000) | **process terminated**, exit code `-1073741819` (`0xC0000005`) |

At 1800 the failure surfaces as:

```text
Unhandled exception. System.NullReferenceException: …
   at Loretta…InternalSyntax.SyntaxParser.AddSkippedSyntax(SyntaxToken, GreenNode, Boolean)
   at Loretta…InternalSyntax.LanguageParser.CreateForGlobalFailure[TNode](Int32, TNode)
   at Loretta…InternalSyntax.LanguageParser.ParseWithStackGuard[TNode](Func`2, Func`2)
   at Loretta…InternalSyntax.LanguageParser.ParseCompilationUnit()
```

This is worth stating carefully, because reading Loretta's source alone gives the wrong answer.
Loretta **does** inherit Roslyn's two-part stack guard: `StackGuard.EnsureSufficientExecutionStack`
(probing via `RuntimeHelpers.EnsureSufficientExecutionStack` once depth exceeds 20) at every
recursive parser entry, and a top-level `ParseWithStackGuard` that catches
`InsufficientExecutionStackException` and degrades to an `ERR_InsufficientStack` diagnostic. From
the source, the expected outcome is a diagnostic, not a crash. **Measured, it is a crash** — the
guard fires, and then its *recovery* path (`CreateForGlobalFailure` → `AddSkippedSyntax`) throws
and the process dies. Anyone who reasons about this from the source without running it will get
this wrong.

The same measurement against MoonSharp 3.0.0-beta.1 (`CoreModules.None`), for comparison:

| Depth | Loretta 0.2.13 | MoonSharp 3.0.0-beta.1 |
| --- | --- | --- |
| ≤ 1600 | parsed, 0 diagnostics, byte-exact round-trip | OK |
| 1800 – 2400 | **process terminated**, `0xC0000005` (access violation), after an `NRE` in the guard's recovery path | OK |
| 3200 | process terminated | **process terminated**, `0xC00000FD` = `STATUS_STACK_OVERFLOW`, printing only `Stack overflow.` — no exception, nothing catchable |

MoonSharp has **no** stack guard at all (zero repo-wide hits for `EnsureSufficientExecutionStack`,
no depth counter in `TableConstructor`/`Expression_`), so it simply overflows; Loretta has one
that fires and then fails. Neither survives hostile depth. Only `LuaCSharp` does, by design
(`TryEnsureSufficientExecutionStack` plus a `MaxCallCount = 200` limit → a catchable
`LuaCompileException`) — and native Lua does too, via `LUAI_MAXCCALLS 200`.

**So this is not a reason to pick a different library, because a .NET `StackOverflowException`
cannot be caught at all.** Microsoft's own documentation is unambiguous: *"You can't catch a
`StackOverflowException` object with a `try`/`catch` block, and the corresponding process is
terminated by default"*, and `HandleProcessCorruptedStateExceptions` *"has no effect"*
(<https://learn.microsoft.com/en-us/dotnet/api/system.stackoverflowexception>). The one documented
escape — having the host unload the AppDomain via `ICLRPolicyManager` — **does not exist on .NET
10**, because AppDomains do not. One malformed Lua file would take down the whole control plane,
and every other server's monitoring with it.

The mitigation therefore belongs in ZWarden, not in whichever library is chosen: **before
parsing, cheaply reject input that exceeds a size cap and a nesting-depth cap**, and never
recurse over attacker-controlled tree depth in ZWarden's own tree-to-model conversion — the
parser's stack guard does not extend to ZWarden's walker. PZ's real files are depth 2 and 4; a
cap of 16 is generous by an order of magnitude, and a `FileInfo.Length` check costs nothing.

MoonSharp 3.0.0-beta.1, `CoreModules.None`:

```text
  SandboxVars (global assignment form)  -> keys=189 Zombies=4 MultiplierConfig.Axe=1
  spawnregions (function form)          -> returned Table, entries=4, first.name="Muldraugh, KY"
  os.execute under CoreModules.None     -> ScriptRuntimeException: attempt to index a nil value
  io.open under CoreModules.None        -> ScriptRuntimeException: attempt to index a nil value
  load/loadstring under CoreModules.None-> ScriptRuntimeException: attempt to call a nil value
  require (reach the CLR)               -> ScriptRuntimeException: attempt to call a nil value
  infinite loop, 3s budget              -> STILL RUNNING after 3000ms
  20M-entry table, 5s budget            -> STILL RUNNING/ALLOCATING after 5000ms
  public syntax-tree types              -> public types=159, public Tree/* types=0
```

Read this fairly: **MoonSharp's hard sandbox works** for what it claims. It reads both file
shapes correctly, including calling `SpawnRegions()`, and under `CoreModules.None` the dangerous
globals are genuinely gone. Its problems are that the **default** preset is not that (§5.3), that
**an interpreter has no bounded worst case** — no instruction budget, no memory cap, no
cancellation — and that there is no public AST (`public types=159`, `Tree/*` public types=**0**),
so it can never round-trip.

The preset composition, read from `CoreModules.cs` (identical at v2.0.0 and at master), is worth
recording because the names are misleading:

```text
Preset_HardSandbox = GlobalConsts | TableIterators | String | Table | Basic | Math | Bit32
Preset_SoftSandbox = Preset_HardSandbox | Metatables | ErrorHandling | Coroutine | OS_Time | Dynamic | Json
Preset_Default     = Preset_SoftSandbox | LoadMethods | OS_System | IO
Preset_Complete    = Preset_Default | Debug
```

`LoadMethods` is `load`, `loadsafe`, `loadfile`, `loadfilesafe`, `dofile` **and `require`**;
`OS_System` is everything in `os` except the four time functions; `IO` is the `io` and `file`
packages. So only `None` and `Preset_HardSandbox` are safe for untrusted input — and note that
even `Preset_SoftSandbox` includes MoonSharp's own `Dynamic` runtime-expression-evaluation module,
so "soft sandbox" is not "no dynamic code". MoonSharp's own documentation states the problem in
the same terms this ticket does: *"Unless you control, in some way, the script providers … there
is a fundamental trust problem in loading scripts … at runtime: security"*, and *"Do NOT use
`InteropRegistrationPolicy.Automatic`. Ever."* (<https://www.moonsharp.org/sandbox.html>).

### 5.7 Loretta round-trip and surgical edit, measured

**Confidence: High — measured** against the real generated files and two shipped PZ files.

Parse and re-emit, comparing `ToFullString()` to the input **as bytes**:

```text
=== zwarden_SandboxVars.lua  (45533 bytes)
    [Lua51] parse=105.3ms diagnostics=0 errors=0 roundtrip_chars=True roundtrip_bytes=True
    [Lua52] parse=2.7ms   diagnostics=0 errors=0 roundtrip_chars=True roundtrip_bytes=True
    [All  ] parse=2.5ms   diagnostics=0 errors=0 roundtrip_chars=True roundtrip_bytes=True
    trivia=3328 comments=738 kinds=EndOfLineTrivia,SingleLineCommentTrivia,WhitespaceTrivia

=== zwarden_spawnregions.lua  (520 bytes)
    [Lua51] parse=6.6ms   diagnostics=0 errors=0 roundtrip_chars=True roundtrip_bytes=True
    trivia=49 comments=2

=== zwarden_spawnpoints.lua  (123 bytes)
    [Lua51] diagnostics=0 errors=0 roundtrip_chars=True roundtrip_bytes=True

=== Apocalypse.lua  (7992 bytes)          # shipped media/lua/shared/Sandbox/
    [Lua51] diagnostics=0 errors=0 roundtrip_chars=True roundtrip_bytes=True

=== spawnpoints.lua  (2336 bytes)         # shipped media/maps/Muldraugh, KY/
    [Lua51] diagnostics=0 errors=0 roundtrip_chars=True roundtrip_bytes=True
```

Five for five: **0 diagnostics, byte-exact round-trip**, under `Lua51` and every other preset
tried — including the map spawnpoints file with its `local` bindings and `mergeTable` calls, and
including CRLF endings and the commented-out template line. The 105 ms on the first parse is JIT
warm-up; the same file re-parses in 2.7 ms.

Surgical edit — locate the `Zombies` field in the table constructor, replace only its value
node, carry trivia with `WithTriviaFrom`, re-emit:

```text
  original bytes=45533 emitted bytes=45533
  line count same=True (1021 vs 1021)
  changed lines=1
    L10: "    Zombies = 4,"  ->  "    Zombies = 7,"
  comments before=738 after=738
  CRLF preserved=True lone-LF present=False
```

One line changed. All 738 comments intact. CRLF preserved. This is the behaviour PRD 33's
"previous state / resulting state" revision diff wants: a minimal, reviewable change.

The probe source and its exact output are in §8.

---

## 6. Options, and a recommendation

> **This section is judgement, not evidence.** Everything above is fact with a source;
> everything here is an argument built on it. It is included because the ticket asks for a
> recommendation and because a technology-baseline ADR needs one.

### 6.1 The four real options

**A. Parse-only, full fidelity — `Loretta.CodeAnalysis.Lua`.** Parse to a syntax tree, read
values out of it, apply surgical value edits, re-emit. Never executes anything.
*Costs:* one dependency plus `Tsu`; a bus-factor-1 upstream on an 18-month-old stable release;
a nesting/size pre-check ZWarden must write itself.
*Buys:* no code execution at all; byte-exact fidelity measured on every real file; operator-grade
diagnostics for free; edits that cannot drop an option ZWarden has never heard of; MIT; no native
binaries, so nothing to say about base images.

**B. Embed a managed interpreter — `MoonSharp` (`CoreModules.None`) or `LuaCSharp`.** Evaluate the
file, read the resulting table. Handles the `function SpawnRegions()` shape naturally by calling it.
*Costs:* it **executes attacker-influenced content**. For MoonSharp the hard sandbox does close
the API surface (measured) but the **default constructor does not** (also measured), there is no
instruction budget, no memory cap and no cancellation, and the process dies on hostile nesting;
there is no AST, so write is a separate unsolved problem. Its licence is **BSD-3-Clause**, not
MIT, carrying an attribution obligation on distribution, and clause 3 still contains an unfilled
`{organization}` template placeholder. Its stable release is from **2016**; only
`3.0.0-beta.1` is modern, and correctness fixes landing on master in mid-2026
(`fix: infinite loop when LuaCall pads nil results`) exist only in that unreleased beta.
`LuaCSharp` is the better engineered choice here — secure by default, real cancellation, guarded
parser recursion, net10.0-native, MIT — but it is Lua **5.2** not 5.1, UTF-16 strings change
`string.len` semantics, `LuaState` is not thread-safe, it is pre-1.0, and it still cannot
round-trip.
*Buys:* the only option that evaluates `media/maps/*/spawnpoints.lua` faithfully — which §2.5
argues ZWarden should not attempt anyway.

**C. Native interpreter — `NLua` + `KeraLua`.** Real Lua 5.4 semantics.
*Costs:* everything in B, plus: the stdlib is **open by default**; `NLua.Lua.Init()`
unconditionally installs `luanet.load_assembly`/`import_type`, i.e. **CLR reflection reachable
from an evaluated script with no registration at all**; the C implementation's memory-safety
surface, where NVD's own text for CVE-2022-28805 (9.1) says the threat model is *compiling*
untrusted Lua; native binaries in the container that **will not load on a musl/Alpine base
image**; a shipped Lua 5.4.8 with a documented table-constructor parser bug; and a native
dependency NuGet's advisory feed cannot see. Nothing in PZ's files needs Lua 5.4.
*Buys:* nothing this problem requires.

**D. Hand-rolled reader/writer.** Zero dependencies, zero execution.
*Costs:* §5.5. The read half is a plausible few hundred lines; the round-tripping half is
re-implementing Loretta's trivia model. Every long-bracket, number-format and escape case
ZWarden gets wrong is a config file it wrongly rejects, discovered by an operator, in production.
*Buys:* no supply-chain exposure and no upstream risk — which is a real answer to Loretta's bus
factor.

### 6.2 Recommendation

**Take option A: `Loretta.CodeAnalysis.Lua` 0.2.13 (pinned), `LuaSyntaxOptions.Lua51`, parse-only,
with a ZWarden-side size and nesting-depth pre-check, and surgical value edits rather than
whole-file regeneration.** Write with `File.WriteAllBytes` to a temp file in the same directory
followed by an atomic replace, UTF-8 **without BOM**.

The reasoning is that this problem is not "how do we run Lua", it is "how do we edit a data file
that happens to be Lua syntax, safely, without losing anything we do not understand". Option A is
the only candidate that answers that without executing the file, and the round-trip is not a
promise from a README — it was measured byte-exact on all five real files, and a targeted edit
changed exactly one line out of 1,021 while keeping all 738 comments.

**The trade-off, stated honestly:** ZWarden takes a dependency on a single-maintainer project
whose stable release is 18 months old and which sees roughly a commit a month. That is the same
concentration risk [#2](https://github.com/MCrank/ZWarden/issues/2) flagged for Blazor Blueprint,
and it deserves the same treatment: pin the exact version, keep the parse behind a narrow
ZWarden-owned seam (something like `IPzConfigDocument` — open, read values, set a value, emit
bytes) so the parser is one implementation behind an interface, and keep a real PZ file corpus as
the acceptance test for that seam — generated from a local install rather than committed, for the
reason in §6.5 — so a fork or a replacement can be validated in an afternoon. It is MIT with no
native code, so vendoring is a genuine fallback rather than a threat. The alternative — option
D — trades that risk for a permanent obligation to be right about Lua lexing, which is the worse
bargain while the seam keeps the option open.

**Scope note: this recommendation is about the three Lua files only.** `<name>.ini` is
`# comment` / `KEY=value` with no sections and no continuation lines, and needs nothing more than
a small hand-written reader — Loretta has no business there, and neither does a general INI
library, given the game regenerates the comments anyway (§3.1).

Two secondary calls that follow from the evidence:

- **Do not chase round-trip fidelity on `_SandboxVars.lua` or the `.ini` as a product feature.**
  The game destroys it on the next restart (§3.1). The fidelity is worth having for the
  *correctness* reason in §3.6 — not losing unknown keys — and that is how it should be
  justified in the ADR. For the two **spawn** files fidelity is directly user-visible and worth
  preserving.
- **Treat "the file did not parse" as a first-class operator-facing state**, not an exception.
  PZ itself refuses to start on a syntax error (§3.3), so ZWarden discovering the problem first,
  with a line and column, is a supportability win (PRD 2.3) rather than an edge case.

### 6.3 Not recommended, and why, in one line each

- **MoonSharp / NLua / LuaCSharp / NeoLua / WattleScript / CSLua / AsyncLua:** all execute
  attacker-influenced content; none can round-trip; the one thing an interpreter uniquely buys
  (evaluating map spawnpoints files) is something §2.5 argues against attempting.
- **`NLua` specifically is the worst option on the board** for this input: native
  memory-unsafety *and* stdlib-on-by-default *and* CLR reflection-on-by-default.
- **`CXuesong.Luaon`:** a second dependency, dormant since 2023, for emitting table literals that
  Loretta can already emit.
- **Hand-rolled:** keep as the documented fallback behind the seam, not the first move.

### 6.4 Summary of the trust axis

| | Loretta | LuaCSharp | MoonSharp | NLua + KeraLua |
| --- | --- | --- | --- | --- |
| Executes attacker input | **No** | Yes | Yes | Yes |
| Native code | No | No | No | **Yes** (Lua 5.4.8 as shipped) |
| Default `io`/`os`/`load`/`require` | n/a | **off** (empty env) | **ON** (`Preset_Default`) | **ON** (`openLibs = true`) |
| Default CLR reflection from script | n/a | off (source-gen only) | off (opt-in registration) | **ON** (`luanet.import_type`) |
| Can abort `while true do end` | n/a | **yes** (`CancellationToken`, tested upstream) | **no** | yes (`SetHook` count hook) |
| Hostile nesting | process dies ≥1800 (measured) | `LuaCompileException` | process dies ≥3200 (measured) | Lua error (`LUAI_MAXCCALLS 200`) |
| Memory-safety CVE class | impossible | impossible | impossible | **inherits Lua's C CVEs** |
| Round-trip with comments | **yes** | no | no | no |
| Latest stable | 0.2.13 (2025-03) | 0.5.6 (2026-07) | **2.0.0 (Oct 2016)** | 1.7.9 / 1.4.9 (2026) |

Trust-cost ranking for attacker-influenced Lua data files:
**Loretta (parse-only) ≪ hand-rolled reader ≪ LuaCSharp ≪ MoonSharp ≪ NLua.**

### 6.5 A licence question that must be settled before it becomes a test fixture

**Confidence: High on what the documents say; the question itself is unresolved.**

`servertest_SandboxVars.lua` does **not** ship with the game — it is generated at runtime on the
operator's machine, as this research demonstrated. Whether operator-generated output is a "base
file or content" of Project Zomboid is addressed nowhere in the licence corpus.

The corpus itself is inconsistent. `terms and conditions.txt` in the install root (and
`license/Project Zomboid.txt`, identical) is the permissive 2022 T&C, whose §2.1 grants:

> Change or distribute the base files or contents in any way you like, provided that those
> changes do not result in you making Project Zomboid available to play or download …

But the same install ships `license/PZLicense.txt`, a 613-byte fragment beginning mid-agreement
at the word "Restrictions.", with no parties clause, no grant, no definition of "the Software",
and an unresolved drafting bracket, whose clause (c) prohibits copying or distributing "the
Software" outright and whose clause (a) prohibits reverse engineering and decompilation.
`license/README.txt` enumerates third-party components and never references `PZLicense.txt`.
**Which document controls is not determinable from the text.**

Nothing in the T&Cs, Modding Policy or IP Rights Policy names `media/`, `media/lua/`, or any file
path, and there is no LICENSE, README or copyright header anywhere under `media/lua/` in the
42.20.4 tree.

Two consequences worth acting on:

1. **Prefer generating fixtures over committing them.** This research showed the generation path
   works and is cheap: `zombie.network.GameServer -cachedir=<tmp> -servername <name> -nosteam`
   writes all four files in about three minutes and touches nothing outside the given directory.
   A test that generates its corpus from a local install sidesteps the question entirely. And
   §2.4 notes the shipped Lua **is not the schema** anyway, so committing it buys less than it
   appears to.
2. **`PZLicense.txt` clause (a) also bears on how this document's evidence was gathered.** The
   findings here rest on running the game, on reading string constants out of shipped class files
   (not decompilation), and on shipped Lua text; third-party decompilations were used only for
   control flow and only where a shipped literal or an executed observation corroborates them.
   That is the same tier [#5](https://github.com/MCrank/ZWarden/issues/5) used, and it is recorded
   here so the choice is visible rather than implicit.

This is a report of text, not legal advice. §2.1 twice instructs readers to ask when unsure; a
short mail to `info@theindiestone.com` naming the exact files, with the reply kept, is what the
licence itself points to.

---

## 7. What this changes for the PRD and for #5

**Confirms #5.** Three of the four config files are Lua, only `<name>.ini` is key=value, and
structured editing means a Lua reader/writer rather than an INI parser. All still true.

**Sharpens #5** on three points:

| # | #5 said | 42.20.4 measurement |
| --- | --- | --- |
| 1 | The three Lua files are "Lua with nested tables" | Two of the three are **function definitions** wrapping the table, not bare tables. `_SandboxVars.lua` is a **global assignment**, not a `return`. A table-literal reader alone reads none of them |
| 2 | `<name>.ini` "can be edited while the server is running" and is reloadable via `reloadoptions` | Still true — but the INI is also **rewritten from code on every start**, so operator comments and ordering never survive, and `reloadoptions` does **not** extend to sandbox vars |
| 3 | The wiki documents the spawnpoint schema as `{ worldX, worldY, posX, posY }` | That is a legacy **input** form. The 42.20.4 writer emits `{ posX, posY, posZ }` |

**New constraints on PRD 32 and 33:**

- **PRD 33's "safe atomic patterns" is a hard requirement, not a quality bar.** A torn write to
  `_SandboxVars.lua` prevents server start with `System.exit(1)` (§3.3).
- **PRD 33's revision diff cannot be a byte diff.** Comments are regenerated from the server's
  locale and the INI embeds a freshly randomised number inside a comment on every start (§3.1).
  "Previous state" and "resulting state" must be captured as **parsed values**.
- **PRD 33 must tolerate a second author.** The in-game admin panel and the in-game server-
  settings editor both write these files (§3.2, §3.5). A revision chain that assumes ZWarden is
  the sole writer will silently diverge.
- **PRD 32's "structured editing" for sandbox is edit-then-restart, not edit-and-apply** (§3.5).
- **PRD 32's "raw text editing … as an advanced capability" is a parser attack surface** and is
  a sufficient reason on its own to never evaluate these files (§4).
- **A validation model cannot be derived from the shipped Lua** — types, ranges and defaults are
  Java-side (§2.4). It has to be built from observation, or from the first-party Javadoc, and it
  will need re-verifying at B43.

**Not covered by the map, and arguably a new decision:** the fixture-provenance question in
§6.5 — whether ZWarden commits generated PZ config files as test corpus or generates them at
test time from a local install. It is small, but it touches PRD 22's licensing posture and
PRD 5's testing stack, and #5 already found the EULA closing one redistribution escape clause.

---

## 8. The probe

Throwaway, `net10.0`, SDK 10.0.302, run from a scratch directory and **not** proposed for `src/`.
Packages: `Loretta.CodeAnalysis.Lua` 0.2.13, `MoonSharp` 3.0.0-beta.1, `LuaCSharp` 0.5.6.
Four modes: default (parse + byte round-trip census over real files), `edit` (surgical value
replacement), `depth` (nesting escalation, one process per depth so a crash is observable), and
`interp` (MoonSharp under `CoreModules.None`). Its outputs are quoted verbatim in §5.6 and §5.7.

The config-file corpus it ran against was generated by the game itself:

```text
java -cp ./;projectzomboid.jar zombie.network.GameServer \
     -cachedir=<scratch>/zomboid -servername zwarden -adminpassword <x> -nosteam
```

run from the 42.20.4 install directory. `-cachedir` fully redirects the user-data root, so
nothing outside the scratch directory was touched — confirmed after the runs.

---

## 9. Consolidated list of what could not be verified

Ranked, most load-bearing first.

| # | Item | Why it matters |
| --- | --- | --- |
| 1 | **Line endings on a Linux server host, observed.** CRLF-on-Windows and LF-tolerance-on-read are measured; LF-on-Linux rests on `System.lineSeparator()` in primary-adjacent code | ZWarden runs PZ in a Linux container. Only affects byte-level comparison, which §3.1 already rules out |
| 2 | **Whether a non-English server locale changes the generated comment text.** The mechanism (`Translator.getTextOrNull("Sandbox_*_tooltip")`) is primary-adjacent; the comments-are-generated conclusion is measured | Reinforces "diff parsed values, not bytes". Not verified that the text actually differs |
| 3 | **Whether 42.20.4's sandbox option set and ordering match the 42.20.0 wiki listing.** `VERSION`/`SANDBOX_VERSION` is 6 in both, which strongly implies no schema change, but 42.20.4's ~275 options were not enumerated against the wiki in order | The generated file from this research supersedes the wiki as the reference. **Do not treat the wiki key list as authoritative for 42.20.4** |
| 4 | **Whether a mod can in practice reach `ServerSettings.saveSpawnPointsFile` / the unguarded spawn-file write path.** The primitive exists and lacks the traversal guard its sibling has; not every caller path was traced, and no exploit was attempted | Does not change the "treat as untrusted" conclusion (§4 item 4 stands alone), but it is the difference between "theoretically attacker-influenced" and "demonstrably so" |
| 5 | **The exact Kahlua fork and version PZ ships.** `se.krka.kahlua.*` and `org.luaj.kahluafork.compiler` are verified from the jar and from a live stack trace; the Lua 5.1 lineage is corroborated by a community modding guide and by observed 5.1 behaviour (no BOM skipping), but no version string was recovered, and PZ's Kahlua is explicitly modified | Determines which Loretta dialect preset is exactly right. `Lua51` parsed every real file with 0 diagnostics, so the practical risk is low |
| 6 | **Which licence text controls: `terms and conditions.txt` §2.1 or `license/PZLicense.txt` clause (c).** Not determinable from the documents; "the Software" is undefined in the latter | §6.5. Only The Indie Stone can settle it |
| 7 | **Whether Loretta's `0.2.14-nightly.*` line is release-quality.** A year newer than the stable, published from CI as a prerelease with no stated guarantee | The recommendation pins the stable 0.2.13, which was the version measured |
| 8 | **Whether Loretta round-trips *invalid* input in all cases.** Measured true for four malformed cases (§5.6) via the skipped-tokens-trivia path, but not property-tested over a wide corpus, nor over exotic encodings or mixed CRLF/LF | A raw-edit save path (PRD 32) will hit this. Worth a property test over the real PZ files |
| 9 | **Whether `new NLua.Lua(openLibs: false)` even works.** `Init()` unconditionally runs `DoString(InitLuanet)`, which needs `rawget`/`setmetatable`/`pcall`/`type` — base-library globals that `openLibs: false` never creates, so the opt-out plausibly throws. Untested | If true, NLua has no working way to open a state without the standard library. Moot under the recommendation, but it would be disqualifying |
| 10 | **Whether NLua's `luanet.import_type` actually reaches the CLR end-to-end on .NET 10.** The unconditional `Init()` and the `luanet.load_assembly('mscorlib')` payload are source-verified; the exploit was not run | High severity if true, and NLua's own README asserts the capability as a feature |
| 11 | **Loretta on a musl/Alpine base image.** It ships no native code, so it should be indifferent; not executed on musl | Low risk, but unmeasured |
| 12 | **Superlinear-time / pathological lexing** for Loretta, MoonSharp or LuaCSharp. No fuzzing or complexity data found or produced. Recursive descent with a hand-written lexer is normally O(n) | Lower priority than the depth and size caps §5.6 already mandates |
| 13 | **Exact NuGet package ownership/publish rights** for any package here. Contributor commit counts were read from the GitHub API; NuGet does not expose owners via the search API | Bears on the bus-factor argument in §6.2 |
| 14 | **The exploitability, beyond a crash, of Lua 5.4.8's "Constructors with nils can overflow counters during parsing".** lua.org gives the title, the timeline and the one-line `checklimit` fix but no impact statement | Moot under the recommendation; load-bearing if NLua is ever revisited |

**One source-only inference this research had to overturn.** Reading Loretta's source suggests
that hostile nesting degrades to an `ERR_InsufficientStack` diagnostic — the guard is there, at
every recursive parser entry, with a top-level catch. **Measured, the process dies instead**
(§5.6). Recorded here because it is exactly the kind of claim that survives a documentary review
and fails a spike, which is the case PRD 62's capped code spikes exist to catch.

**Confirmed absences** (searched and genuinely not there, as distinct from the above):

- **No NuGet package both round-trips Lua and executes it.** The two capabilities do not
  co-occur in any package found.
- **MoonSharp has no stack guard whatsoever.** Zero repo-wide hits for
  `EnsureSufficientExecutionStack`, no depth counter in its `TableConstructor` or `Expression_`
  parsers. Confirmed by measurement: the process is killed with `STATUS_STACK_OVERFLOW`.
- **MoonSharp's interpreter accepts no `CancellationToken` anywhere.** The single repo-wide hit
  is in a TypeScript file belonging to the VS Code debug adapter. There is no timeout, no
  instruction limit and no memory cap, and `Thread.Abort` does not exist on .NET — so a runaway
  script can only be contained by a process boundary.
- **There is no in-process mitigation for a stack overflow on .NET 10.** Microsoft documents the
  `ICLRPolicyManager` / AppDomain-unload escape, and **AppDomains do not exist on .NET (Core)**.
- **`MoonSharp.Interpreter.Serialization` is JSON, not Lua** — `JsonTableConverter.TableToJson`
  and friends. `SerializationExtensions` is public but ships no XML documentation for any member,
  so nothing there is a documented Lua-literal emitter.
- **`https://www.moonsharp.org/moonsharp_vs_lua.html` does not exist** (404). The real page is
  `moonluadifferences.html`. Recorded because the dead URL circulates.
- **No published security advisory exists for MoonSharp, NLua, KeraLua, LuaCSharp or Loretta** —
  GitHub Advisory Database returns zero for each, and no NuGet page carries a vulnerability
  banner. Read that as absence of *reports*, not absence of bugs: NuGet's advisory feed has no
  visibility into KeraLua's bundled native `lua54` binary at all.
- **Loretta makes no "lossless" or "full-fidelity" claim** anywhere in its README or docs. The
  capability is real and measured; the marketing claim does not exist and should not be cited.
- **`LuaCSharp` has no comment token whatsoever** — no `Comment` member in `SyntaxTokenType`, no
  trivia type in the assembly. Round-trip is not "unsupported", it is structurally impossible.
- **PZ's Lua environment has no `io` library**, and `os` is limited to `date`/`difftime`/`time`.
  There is no `dofile`, `loadfile`, `load` or `loadstring`, and no `writeFile` / `readFile` /
  `removeFile` global — those three do not exist and should not be designed against.
- **`getFileWriter` cannot write a `.lua` file**: its extension allowlist is
  `{ini, cfg, txt, log, json}`, and it is rooted at `Zomboid/<cache>/Lua/`, never
  `Zomboid/Server/`.
- **`reloadoptions` has no sandbox path** — `SandboxOptions` is absent from
  `ReloadOptionsCommand`'s entire reference set.
- **No LICENSE, README, COPYING or copyright header exists anywhere under `media/lua/`** in the
  42.20.4 install.
- **`servertest_SandboxVars.lua` does not ship with the game.** Nothing matching `*servertest*`
  exists in the install tree; it is generated at runtime.
