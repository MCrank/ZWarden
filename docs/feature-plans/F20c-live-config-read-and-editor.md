# Feature 20c Mini-Plan — Live Configuration Read, Schema Editor, and Tooltips

**Status:** delivered. Track D. The third F20 split — [#108](https://github.com/MCrank/ZWarden/issues/108),
"F20b follow-up: live config read → full schema editor + raw view/edit" (`ready-for-agent`). Delivered
across four PRs, one commit per TDD slice, on branches under `feat/f20c-*`: PR-A library (comment
harvest + schema metadata), PR-B read path (ADR 0041), PR-C schema editor UI, and PR-D drift
confirm-and-override + gated raw whole-file edit (ADR 0042) — this PR closes #108.

**Format:** PRD 60. **Written against:** PRD 32 (structured configuration editing — the *live-read and
present* half F20a/F20b deferred), PRD 2.2 (TDD mandatory), PRD 2.3 (supportability — a blind editor and a
drift refusal are operator-facing states, not stack traces), PRD 38 (untrusted-data posture — a config
file's bytes and its comments are attacker-influenced). Sequencing: this is the F20 split's third leg in
[`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (Track D) / §7. Anchored on
[ADR 0010](../adr/0010-lua-configuration-is-parsed-never-evaluated.md) (parse-only Lua behind
`IPzConfigDocument`; the syntax tree — and thus comment trivia — is retained internally by the F20b edit
backing), [ADR 0011](../adr/0011-configuration-revisions-are-value-level-and-fail-closed.md) (values not
bytes; comments are **not** content and **not** drift; fail-closed re-parse before every write), and
[ADR 0030](../adr/0030-live-logs-stream-on-demand-sanitized-at-source-over-a-non-operation-channel.md)
(the F27 non-Operation, ephemeral, read-only agent→web channel this feature's read path mirrors). Extends
the **F20a** read/model library ([#40](https://github.com/MCrank/ZWarden/issues/40), PR #104) and the
**F20b** apply/revision substrate ([#42](https://github.com/MCrank/ZWarden/issues/42), PRs #105–#109). The
research doc [`docs/research/pz-lua-config.md`](../research/pz-lua-config.md) §2.1 supplies the load-bearing
fact this feature turns into a product surface: the 738 `--` comment blocks in `_SandboxVars.lua` **are**
PZ's in-game tooltips, generated from its translation table, carrying enum labels and Min/Max/Default.

## Objective

Turn ZWarden's configuration editor from **blind** into **live and legible**. Today (F20b PR #109) the
operator must already know the dotted path, the type, and the intended value, and types all three by hand.
F20c delivers, end to end:

1. A **live, non-mutating read** of a Server's four config files from the Agent to the control plane, over
   a channel that can carry a ~45 KB file (the 2 KB `Operation` payload cap cannot).
2. A **schema-driven structured editor pre-filled with the file's current values** — booleans as toggles,
   ranged enums as labelled dropdowns, numbers as bounded sliders/steppers, text as inputs — built from
   F20a's `PzSchema`.
3. **Per-setting tooltips**, sourced **primarily from the file's own `--` comments** (harvested at read
   time, sanitized of PZ's UI rich-text markup) and supplemented by ZWarden-authored schema metadata
   (friendly label, section, an optional override description).
4. An **advanced raw view** of the file text (raw *edit* is a gated later slice — see §Non-scope).
5. An interactive **drift confirm-and-override**: on a fail-closed drift refusal, re-read the live file,
   refresh the baseline, and let the operator reapply on top — the flow F20b could only refuse.

All UI keeps the **Signal** design (the redesigned Server Detail rail) and is gated by the existing
`Permissions.ServerConfigurationEdit`, re-checked server-side on every action (ADR 0018).

**The approved mockup is the UX target:** https://claude.ai/artifact/GVzf1345uaJHGmtwtYRh52 — grouped
sections, current-value pre-fill, ⓘ tooltip (with a "from the file" line showing the raw harvested comment),
search-across-settings, raw view, dirty-state + surgical-apply framing, drift banner, restart-required hint.

## Decisions to settle before writing (maintainer forks)

Four forks change hard-to-reverse structure. Recommendations below; each is a plain-terms breakdown.

### Fork 1 — the read transport (the load-bearing one; needs an ADR)

A ~45 KB file cannot ride an `Operation` (`Operation.MaxCommandPayloadLength = 2048`), and by symmetry an
Operation *result* carrying 45 KB breaks the "results stay bounded" norm. No SignalR streaming
(`IAsyncEnumerable`/`ChannelReader`) exists anywhere in the codebase today — all agent↔web traffic is
discrete `Envelope<T>` hub-method sends.

- **Option A — a new ephemeral, read-only hub-method channel, the F27 pattern (recommended).** Add
  `RequestServerConfigRead` (web→agent) and `ServerConfigContent` (agent→web) to `AgentHubProtocol`,
  exactly as F27 added `StartServerLogStream` / `ServerLogBatch`. Deliberately **not** an `AgentCommand`
  and **not** an `Operation`: no per-server lock, no audit row, no revision — it is a read (ADR 0030's
  reasoning applies unchanged). Chunk the payload into a few sequenced `Envelope<ServerConfigChunk>` sends
  if it exceeds a comfortable single-message size; reassemble on the web side, bounded and transient.
  *Buys:* reuses a proven, reviewed pattern; sidesteps the Operation cap entirely; keeps reads off the
  lock/audit/revision machinery where they do not belong. *Costs:* a second bespoke channel to maintain;
  a correlation id + timeout + "agent offline" path to get right.
- **Option B — introduce real SignalR streaming.** *Costs:* a brand-new transport pattern in a codebase
  that has deliberately avoided one; more surface for reconnect/backpressure bugs. *Buys:* nothing A does
  not, at this size.
- **Option C — a non-mutating Operation whose result carries the file.** *Costs:* collides with the 2 KB
  result norm and the lock/audit/revision conventions Operations carry. Rejected.

**Recommendation: Option A**, recorded as **ADR 0040** ("live configuration read rides an on-demand,
read-only, non-Operation channel"). It is a near-exact sibling of ADR 0030 and should cite it.

### Fork 2 — what crosses the channel

- **Option A — the Agent parses and ships a structured *config view* (recommended):** the agent already
  does `_parser.Open(...)` on the live file in `ServerConfigWriter`. Have the read path parse once and
  return a layer-neutral view — `(path, value, kind, leadingComment?)` per setting, plus the parse
  diagnostics and the canonical-snapshot hash (the drift baseline) — **and** the raw file text for the raw
  view. *Buys:* the untrusted-parse trust boundary stays at the Agent (ADR 0010/§4 of the research doc:
  never parse attacker-influenced bytes where you do not have to); the web tier consumes typed data, not
  Lua. *Costs:* the view DTO must carry comments, which the current parse output does not expose (Fork 3).
- **Option B — ship raw bytes, parse on the web tier.** The `Infrastructure → ZWarden.PzConfig` reference
  F20b added means Web *can* parse. *Costs:* re-runs the size/depth pre-check and an untrusted parse on the
  control plane for no gain, and duplicates the agent's parse. Rejected on trust-posture grounds.

**Recommendation: Option A.** Ship a structured view + the raw text; the agent is the single parser.

### Fork 3 — where comments live in `ZWarden.PzConfig`

Comments exist today only as raw bytes / Loretta trivia; no model, schema, snapshot or diff type carries
them, and `PzDriftCheck` explicitly documents that a regenerated comment is **not** drift.

- **Option A — a side-map from the reader (recommended):** `LuaConfigReader` (which already retains the
  `SyntaxTree`) walks each value node's **leading** trivia and produces a `path → comment-block` map,
  returned alongside the document — **not** a field on `PzTableEntry`/`PzValue`. INI's hand-written reader
  does the same for `# ` lines. *Buys:* the value model, `PzValueSnapshot`, `PzValueDiff` and
  `PzDriftCheck` stay byte-agnostic and untouched — ADR 0011's "comments aren't values, aren't drift"
  holds by construction. *Costs:* one more return channel from the reader.
- **Option B — add a `Comment` field to `PzTableEntry`.** *Costs:* pollutes the value model that snapshot
  and drift walk, and invites a comment to leak into a hash or a diff. Rejected.

**Recommendation: Option A**, plus add `Label`, `Section`, and an optional override `Description` to
`PzSchemaEntry` (today: `Path/Type/Min/Max/Default` only). Tooltip precedence at render: schema
`Description` if authored → else the harvested file comment (sanitized) → else none. Grouping/section and
friendly label are schema-authored; unknown/mod keys fall into an "Other" section with a comment-only
tooltip and pass through unvalidated (F20a's unknown-key rule).

### Fork 4 — raw *edit* scope

Raw **view** is cheap (display the text the read path already returns). Raw **edit** means writing
operator-authored arbitrary text — which bypasses the surgical/values-only writer and can plant a syntax
error or BOM that bricks the server on start (research §3.3–3.4). It must still parse-validate + pre-check +
drift-check + BOM-less atomic-write. **Recommendation: raw *view* ships with the editor (PR-C); raw *edit*
is its own gated slice (PR-D or deferred), because it is a distinct write path with its own failure modes,
not a toggle on the editor.**

## Delivery slices (PRs)

- **PR-A — Library: comment harvest + schema metadata.** Pure `ZWarden.PzConfig`, full TDD, zero wiring.
  `LuaConfigReader`/`IniConfigReader` produce a `path → leading-comment` map; a `PzCommentSanitizer`
  strips PZ UI markup (`<BHC>`, `<RGB:r,g,b>`, `[!]` … ) and collapses the multi-line `-- ` block into
  clean text + parsed enum-label lines (`1 = Insane`) where present; `PzSchemaEntry` gains
  `Label`/`Section`/`Description`; expand the SandboxVars + INI schema toward the mockup's grouped set
  (still a representative subset — filling all ~275 keys stays incremental follow-up). Arch guard unchanged
  (no Loretta on the public surface). Bump the PzConfig.Tests floor.
- **PR-B — Read path: channel + agent reader + application seam.** `ADR 0040`. `AgentHubProtocol`
  `RequestServerConfigRead` / `ServerConfigContent` (+ `ServerConfigChunk` if chunked); an agent-side
  `IServerConfigReader` that reads `/pz/`, parses once, harvests comments, and emits the view + raw text +
  baseline hash; a web-side receiver/coordinator (the `ServerLogSubscriptionCoordinator` sibling) that
  correlates the request, reassembles, times out, and handles agent-offline; an `IServerConfigurationReader`
  Application seam returning layer-neutral `ConfigDocumentView` (sections → settings with value + kind +
  tooltip + schema meta) + `ConfigRawText` + diagnostics; authorize `ServerConfigurationEdit`, fail closed.
  Transient — nothing persisted (ADR 0011). TDD across contracts/agent/infra; bump those floors; regenerate
  lock files if a package moves (none expected).
- **PR-C — Editor UI (the checkpoint PR).** The Signal Server Detail `?section=config` editor rebuilt on
  Bb* components: file tabs, section nav, schema-driven controls pre-filled from the read, ⓘ tooltips,
  search, raw view, dirty-state batching into the existing `IServerConfigurationEditor.ApplyAsync`, and the
  drift **confirm-and-override** flow (re-read → refresh baseline → reapply). **Before committing this PR's
  UI, show the layout inside the real redesigned app** (run-web/aspire/playwright per the maintainer's
  standing checkpoint). Bump the Web.Tests floor in **both** the csproj and the `ci.yml`
  `tier1-silent-drop-guard`; rebuild `app.css` if utility classes change.
- **PR-D — Drift confirm-and-override + gated raw edit (the final leg).** `ADR 0042`. Two capabilities over
  one shared apply-seam extension (`IServerConfigurationEditor.ApplyAsync` gains an additive
  `expectedBaselineHash`; the enqueuer coalesces it over the recorded-revision baseline). **(1) Interactive
  drift confirm-and-override:** the editor applies against the operator's **live-read baseline** (the hash it
  showed), and on a control-plane pre-check drift (the fresh read the POST already performs disagrees with the
  baseline the form carried) it re-renders the current values with a banner and does not enqueue; the Agent's
  fail-closed check (ADR 0011) stays the authoritative residual-race guard. **(2) Gated raw whole-file edit:**
  operator-authored text is chunked to the Agent over a new `StageServerConfigRawEdit` transport channel (the
  reverse-direction sibling of the read channel), then a **small** `ConfigApplyRaw(File, BaselineHash,
  CorrelationId)` Operation is enqueued — so the write keeps the per-server lock (ADR 0022), the audit row, and
  the value-level revision (ADR 0011) despite the ~45 KB payload the 2 KB Operation cap forbids. The Agent runs
  the F20b safety envelope (parse-validate + pre-check + drift-check + BOM-less atomic write) on the staged text
  and reports the same `ConfigApplyResult`. The whole-file text never rides an `AgentCommand`, so the
  closed-command-vocabulary boundary holds. Gated by an explicit in-form acknowledgement on the existing
  `ServerConfigurationEdit` permission. **Show the UI inside the real app before committing** (the standing
  checkpoint). Bump the Web.Tests floor in **both** the csproj and `ci.yml`; rebuild `app.css` if classes
  change. Closes #108.

## Non-scope

- **Persisting live file content or comments** — reads are transient; the control plane still holds only
  value snapshots (ADR 0011). Comments are never hashed, diffed, or stored as revision content.
- **Filling the full ~275-key sandbox schema** — PR-A ships the mechanism + the mockup's grouped subset;
  the rest is mechanical follow-up (one row + one test per key), unknown keys already pass through.
- **Structured spawn-file editing** — `_spawnregions.lua` / `_spawnpoints.lua` are positional/keyed lists,
  not `key = value`; the read path returns their raw text for the raw view, but a structured list editor is
  a later slice (the mockup says as much in-place).
- **Raw *edit*** beyond PR-D's gated write; **evaluating** `media/maps/*/spawnpoints.lua` (research §2.5 —
  out of reach, treated as opaque).
- **Live application of sandbox settings** — unchanged from F20b: sandbox is edit-then-restart
  (`reloadoptions` does not cover `SandboxOptions`); the INI stays live-reloadable. The UI shows the
  restart-required hint.

## Domain / documentation changes

- **New ADR 0040** (Fork 1): live configuration read is an on-demand, read-only, non-Operation channel;
  sibling of ADR 0030, cites ADR 0011 for why the read is transient and unaudited.
- **`scope-and-sequencing.md`**: add the **F20c** entry under §6 / §7 (the F20 split note gains a third
  leg: F20a read/model → F20b apply/revisions → F20c live-read/editor).
- **CONTEXT.md**: two read-side terms if load-bearing during the build — **Configuration View** (the
  transient, structured, tooltip-carrying live read behind `IServerConfigurationReader`) and **Setting
  Tooltip** (the sanitized, comment-or-schema-sourced help string), kept distinct from **Configuration
  Revision** (`cfg-`, F20b) and **Configuration Document** (F20a).
- No new typed-ID prefix, no new entity, no migration (nothing persists), no new permission
  (`ServerConfigurationEdit` gates read and write).

## Testing & verification

TDD in slices, each its own commit (test → red → code → green), run as CI does with the pinned SDK
(`DOTNET_ROOT=/c/Users/marco/.dotnet …/dotnet.exe test -c Release`). Synthetic fixtures only (F12 rule /
ADR 0010 §6.5) — hand-written to the research doc's measured shapes, including a fixture with the
rich-text-markup comment (`<BHC> … <RGB:1,1,1>`) so the sanitizer is tested against the real worst case,
and a mod-added unknown key to prove comment-only, unvalidated pass-through. The read channel gets an
end-to-end request→chunk→reassemble→view test mirroring F27's. The UI is exercised over the real host in
`ZWarden.Web.Tests`. Every changed `--minimum-expected-tests` floor moves with its count; the Web.Tests
floor moves in both the csproj and `ci.yml` ([[web-tests-discovery-floor-bump]]).
