# 11. Configuration revisions are value-level, and drift detection fails closed

PRD 33 requires every meaningful configuration mutation to record a revision with a "previous
state" and a "resulting state". Measured against the shipped server, a **byte-level** diff would
be actively wrong. Revisions are therefore captured as **parsed values, not file bytes**. And
because ZWarden is not the only author of these files, **every write is preceded by a re-parse
and a comparison against the last recorded revision, and a mismatch fails closed** — the write
is refused until the operator confirms.

This refines PRD 33 rather than overturning it: the requirement stands, its representation
changes.

- Status: accepted
- Decided in: [#15](https://github.com/MCrank/ZWarden/issues/15) (measurement), [#11](https://github.com/MCrank/ZWarden/issues/11) §10 item 4 and F20b in `docs/scope-and-sequencing.md`
- Depends on: ADR 10 (parse-only Lua handling behind `IPzConfigDocument`)
- Refines: **PRD 33**

## Context: why bytes are the wrong unit

Three things were measured against Build 42.20.4 by running the server and re-reading its own
output.

1. **The server rewrites `<name>.ini` and `<name>_SandboxVars.lua` on every single start**, from
   its in-memory model. Six hand edits to `_SandboxVars.lua` — a line comment, a block comment,
   an unknown key, a reordered key, tab indentation, a single-quoted string — were **all erased**
   on the next start, leaving the file byte-identical to the pristine generated one except for
   the one value actually changed.
2. **The `.ini` changes between two consecutive no-op restarts.** The only difference was a
   randomised number embedded *inside a comment*. So a byte diff would report a configuration
   change where none occurred, on every restart, forever.
3. **The comments are regenerated from the server's own locale.** They are not content; they are
   output.

A byte-level "previous state / resulting state" would therefore be noisy, locale-dependent, and
wrong in the specific sense that it would assert differences the operator did not make. Parsed
values are the only unit in which the revision history means what PRD 33 says it means.

(The two spawn files are different: the server does **not** rewrite them, and their comments are
real content. ADR 10 covers why they are round-tripped exactly. Revisions over them are still
value-level, because that is what an operator wants to read.)

## Context: why drift detection is mandatory, and why it fails closed

**ZWarden is not the only author.** The in-game admin panel and the in-game server-settings
editor both write these files behind ZWarden's back, and so does the server itself on every
start. Without detection, the revision history would assert a "previous state" that the file
never held — which is worse than having no history, because it is a record that reads as
authoritative and is false.

So before any write: re-parse the file, compare against the last recorded revision, and on a
mismatch **refuse the write** and surface the drift to the operator, who confirms or reconciles.

Failing closed rather than open is the deliberate half of this. Failing open — writing anyway
and recording the surprise — would silently discard whatever the second author did, which on
these files can be a real gameplay change an admin made in-game five minutes earlier. OWASP Top
10:2025's new **A10 Mishandling of Exceptional Conditions** lands exactly here, and the honest
reading of it is that an ambiguous state on a privileged write is refused, not guessed.

## Alternatives considered

- **Byte-level revisions, as PRD 33's "previous state / resulting state" most naturally reads.**
  Rejected on the three measurements above.
- **Hybrid: value-level diff for display, byte snapshot for restore.** Rejected because the byte
  snapshot is not restorable in any meaningful sense — restoring bytes the server will rewrite
  at next start produces a file that differs from what was restored, and the stored bytes may
  carry a comment-embedded random number from a different boot.
- **Detect drift and fail open** (write anyway, record that the file had changed). Rejected: it
  makes ZWarden silently authoritative over a second author it cannot see, on a file where a
  mistake means a changed game world.
- **Lock the files or disable the in-game editors.** Not available; they are the game's own
  surfaces and PZ is the authority on its own runtime.

## Consequences

- **Writes must be atomic and BOM-less** (ADR 10): temp file in the same directory, then atomic
  replace. This is a requirement rather than a quality bar because a torn write or a BOM makes
  the server exit on start with no self-healing path.
- **Operators will see drift prompts**, and they are a real UX cost. Every in-game settings edit
  between two ZWarden writes produces one. That friction is the price of a revision history that
  is true, and Feature 20b should make reconciling cheap rather than trying to make the prompt
  rare.
- **A revision's "previous state" is the last state ZWarden parsed**, not necessarily the state
  at the instant before the write. The re-parse-and-compare step is what makes the difference
  visible; the model does not pretend to observe the file continuously.
- **Sandbox changes are inherently edit-then-restart.** `reloadoptions` does not cover
  `SandboxOptions`, so there is no live-apply path for sandbox settings. `<name>.ini` remains
  live-reloadable.
- **Feature 20 splits because of this ADR's half of the work.** F20a (read, model, validate)
  carries no revisions, no atomic apply, no drift detection; F20b carries all three. Mod
  discovery (F21) needs only F20a, so the split changes the dependency graph and is recorded in
  `docs/scope-and-sequencing.md` §7.
