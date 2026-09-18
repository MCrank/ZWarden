# 42. Interactive config apply drifts against the operator's live read, and raw whole-file edit stages then rides a small Operation

F20c PR-C gave the operator a live, schema-driven editor pre-filled with a Server's current configuration
values (ADR 0041). PR-D closes the two capabilities F20b and PR-C deferred, and both turn on the same
extension to the apply path:

1. **Interactive drift confirm-and-override.** F20b's apply always drift-checks against the *last recorded
   revision* (ADR 0011). For a live editor that is wrong twice over: it refuses a write whenever the file was
   legitimately edited in-game since ZWarden last wrote (a false positive the operator cannot see the cause
   of), and it never protects against a change made between the operator's *read* and their *apply*. F20c makes
   the editor apply against the **operator's live-read baseline** — the canonical value hash of exactly the
   state they were shown — so the Agent's fail-closed check verifies against what the operator actually saw.
   A refusal is then reconciled by **re-reading the file, refreshing the baseline, and letting the operator
   reapply on top**, the flow F20b could only refuse.

2. **Gated raw whole-file edit.** PR-C shows the raw file text read-only. PR-D lets the operator author the
   whole file as text — the only way to edit the positional spawn files, and an escape hatch for anything the
   structured editor does not model. Arbitrary operator text bypasses the surgical values-only writer and can
   plant a syntax error or a BOM that stops the server on start, so it must still **parse-validate, size/depth
   pre-check, drift-check, and BOM-less atomically write** through the F20b writer's safety envelope — and it
   is gated behind an explicit in-form acknowledgement.

- Status: accepted
- Decided in: F20c (#108) PR-D; extends [ADR 0041](./0041-live-configuration-read-rides-a-non-operation-request-reply-channel.md) (the live read this apply drifts against) and the F20b apply/revision substrate (#42)
- Refines: [ADR 0011](./0011-configuration-revisions-are-value-level-and-fail-closed.md) (the drift baseline can be the operator's live read, not only the last recorded revision; the write still fails closed and is never fail-open)
- Bears on: PRD 32 (structured configuration editing); PRD 2.3 (a drift refusal is an operator-facing state, reconciled interactively, not a stack trace); PRD 38 (operator-authored file bytes are attacker-influenced and the untrusted parse stays on the Agent); ADR [0010](./0010-lua-configuration-is-parsed-never-evaluated.md) (the raw parse stays on the Agent behind `IPzConfigDocument`); ADR [0022](./0022-operation-lifecycle-and-per-server-locking.md) (a raw write is still a mutating Operation, so it keeps the per-server lock); ADR [0020](./0020-agent-protocol-versioning-and-catalogue.md) (`ConfigApplyRaw` is an additive command leaf; the upload channel is transport plumbing, not vocabulary); ADR [0018](./0018-zwarden-owned-rbac.md) (both writes re-authorize `ServerConfigurationEdit` server-side)

## Context

**The read hash and the write baseline are the same fingerprint.** ADR 0041's live read ships
`ConfigReadPayload.BaselineHash` — `PzValueSnapshot.Of(document).Hash` — and F20b's Agent-side drift check
compares `PzValueSnapshot.Of(liveFile).Hash` against the baseline it is handed. They are byte-for-byte the
same canonicalization, so the hash the operator was shown can be fed straight back into the apply as the
expected baseline. The wire (`ConfigApply.BaselineHash`) and the Application payload
(`ConfigApplyPayload.BaselineHash`) already carry a baseline; F20b simply filled it from the last recorded
revision. Nothing new has to cross the boundary for the drift half — only *which* hash the enqueuer chooses.

**A whole file cannot ride an Operation.** `Operation.MaxCommandPayloadLength` is 2 KB; `_SandboxVars.lua`
is ~45 KB (measured, research §2.1). This is the exact wall that forced ADR 0041's read to ride a chunked,
non-Operation channel rather than an Operation result. Raw edit hits it in the write direction: the operator's
text is far too large for a command payload, yet a *write* legitimately wants the per-server lock (ADR 0022),
the audit row (ADR 0019), and the value-level revision (ADR 0011) that an Operation carries and a bare
side-channel does not.

## Decision

### The apply baseline is caller-supplied, and defaults to the recorded revision

`IServerConfigurationEditor.ApplyAsync` takes an additive `expectedBaselineHash`. When supplied it is the
baseline the Agent drift-checks against; when omitted the enqueuer falls back to the last recorded revision's
hash exactly as F20b did (so F22's mod manager and any non-interactive caller are unchanged). The interactive
editor always supplies the **live-read baseline** it showed the operator. The Agent's re-parse-and-compare
stays the single authoritative gate and still **fails closed** on a mismatch — this is not a fail-open
override. "Override" means the operator *re-reads the current file and reapplies on top of it*, so the write
verifies against the live state, never that the drift check is skipped.

### The interactive confirm-and-override is a control-plane pre-check over the live read

An apply is asynchronous (enqueue → Operation → Agent), so the Agent's refusal cannot surface synchronously in
the editor's POST. The editor instead re-reads the live file on the same POST it already performs, compares
its fresh `BaselineHash` to the baseline the operator's form carried, and on a mismatch **does not enqueue**:
it re-renders the editor populated with the current values, carrying the fresh baseline, and shows a drift
banner asking the operator to review and reapply. On a match it enqueues carrying that baseline. The Agent's
own fail-closed check remains the authoritative guard for the residual race between the pre-check read and the
write (and for every non-interactive caller). The pre-check makes drift *interactive*; the Agent makes it
*safe*.

### Raw whole-file edit stages the text, then rides a small Operation

The operator's text is chunked and sent to the owning Agent over a new **`StageServerConfigRawEdit`** channel —
the reverse-direction sibling of ADR 0041's read channel: bare-argument transport plumbing, neither an
`AgentCommand` nor an `AgentEvent`, so the closed-command vocabulary (ADR 0020) is unaffected. The Agent holds
the reassembled text transiently, keyed by a correlation id, in a bounded, expiring buffer (a compromised or
buggy peer cannot flood it). ZWarden.Web then enqueues a **small** `ConfigApplyRaw(File, BaselineHash,
CorrelationId)` Operation — well under the 2 KB cap. When the Agent runs it, it retrieves the staged text,
runs the F20b safety envelope (parse-validate the new text, size/depth pre-check, drift-check the *current*
file against the baseline, BOM-less atomic write), and reports the same `ConfigApplyResult` a surgical apply
reports — so the existing completion path records the revision and audit row with no new machinery. A missing
or expired staging entry fails the Operation cleanly ("content not received"), never a hang.

This keeps every invariant a write owes: the per-server lock (it is a mutating Operation, ADR 0022), the audit
row and value-level revision (ADR 0011/0019), and the untrusted parse on the Agent (ADR 0010). It also keeps
the **closed-command-vocabulary** boundary intact — the arbitrary file text travels on the staging channel and
**never rides an `AgentCommand`**; `ConfigApplyRaw` carries only a file enum, a hash, and a correlation id, so
the trust-boundaries §9 "no free-form command string on a command" guard holds by construction.

### Raw edit is gated in-form, on the existing permission

Raw whole-file edit re-authorizes the same server-scoped `ServerConfigurationEdit` permission as a structured
apply (both are privileged config writes; a separate role split can come later without a wire change). The
extra risk — a syntax error can stop the server on start — is gated by an explicit in-form acknowledgement the
operator must set to submit, alongside the drift and restart-required warnings.

## Alternatives considered

- **Keep drift-checking against the last recorded revision.** Rejected for the interactive editor: it refuses
  legitimate writes after any in-game edit (a false positive with no visible cause) and misses read-to-apply
  races. The recorded-revision baseline remains the correct default for non-interactive callers, which is why
  it stays the fallback.
- **A fail-open "force write" that skips the drift check.** Rejected outright — ADR 0011 fails closed on
  purpose; a forced write silently discards a second author's change. Confirm-and-override reconciles against
  the live file instead of ignoring it.
- **Raw edit over a pure non-Operation write channel (symmetric to the read).** Rejected: it bypasses the
  per-server lock (a raw write could race a structured apply or a lifecycle Operation on the same file) and
  re-implements audit/revision recording outside the Operation completion path. Staging + a small Operation
  gets the large payload *and* keeps the write on the Operations engine.
- **Parse the raw text on the control plane and diff it to a surgical edit list.** Rejected: it runs an
  untrusted Lua parse on the control plane (ADR 0010, the reason the read channel exists) and cannot express a
  comment- or layout-only edit or the positional spawn files.
- **Raise SignalR's `MaximumReceiveMessageSize` to send the file in one message.** Rejected as ADR 0041
  rejected it — it widens the ceiling for every message to solve one large payload; chunking bounds the cost
  where it arises.
- **A distinct `ServerConfigurationRawEdit` permission.** Available, not taken for v1.0: raw edit is the same
  privileged config surface, and the in-form acknowledgement carries the "this is riskier" signal without a
  catalogue and role-seed change. A separation-of-duty split can land later.

## Consequences

- **The blind-editor false-positive drift is gone.** An operator editing values they can see, on a file edited
  in-game since ZWarden last wrote, now applies cleanly; drift is raised only when the file actually changed
  under them, and is reconciled in place by a re-read rather than an opaque failed Operation.
- **A read now happens on every interactive apply** — but the editor's POST already re-read the file to
  re-render, so the drift pre-check reuses that read at no extra round-trip.
- **Raw edit is a new mutating Operation kind (`ConfigApplyRaw`) with a two-part delivery** — a staged upload
  plus a small Operation. The staging buffer is transient and bounded; a lost or expired stage fails the
  Operation with an actionable reason. Its completion reuses `ConfigApplyResult`, so it records a Configuration
  Revision and an audit row exactly like a surgical apply.
- **The closed-command-vocabulary guard still holds.** The whole-file text is transport plumbing on its own
  channel; no `AgentCommand` gains a free-form string, and the arch/contract vocabulary tests pass unchanged.
- **A raw edit can still fail closed on drift or on a syntax error** — both are operator-facing Operation
  failures carrying the Agent-authored reason, never a written-then-broken file (parse-validate precedes the
  atomic write, and the write is BOM-less; ADR 0010/0011).
