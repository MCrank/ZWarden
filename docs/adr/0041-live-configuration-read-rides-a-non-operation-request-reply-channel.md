# 41. Live configuration read rides an on-demand, read-only, non-Operation request/reply channel

ZWarden shows an operator the **current values** of a Server's configuration files (F20c) so the editor is no
longer blind. Only the Agent can see the live file — it owns the `/pz/` mount — so the control plane obtains the
current state by **asking the owning Agent to read and parse the file once and send its structured contents back**.
That read is deliberately **not an Operation** (ADR 0022 — no per-server lock, no audit row, no revision) and
**not an `AgentCommand`** (the operation-dispatch vocabulary): it observes, it does not mutate, and nothing it
returns is persisted (ADR 0011). It therefore rides its **own on-demand, read-only hub channel** — a near-exact
sibling of the F27 live-logs channel (ADR 0030), differing only in that a config read is a **correlated
request/reply** (one request, one reassembled reply) rather than a viewer-ref-counted subscription. Because a
parsed file can exceed SignalR's default per-message ceiling, the reply is **chunked** into sequenced sends and
**reassembled, bounded, and transient** on ZWarden.Web.

- Status: accepted
- Decided in: F20c (#108); the third F20 split, after F20a (#40) read/model and F20b (#42) apply/revisions
- Bears on: PRD 32 (structured configuration editing — the live-read half F20a/F20b deferred); PRD 2.3
  (supportability — a blind editor and a parse refusal are operator-facing states); PRD 38 (a config file's bytes
  and its comments are attacker-influenced); ADR
  [0030](./0030-live-logs-stream-on-demand-sanitized-at-source-over-a-non-operation-channel.md) (the non-Operation
  agent→web channel this mirrors); ADR
  [0011](./0011-configuration-revisions-are-value-level-and-fail-closed.md) (why the read is transient, unaudited,
  and comment-agnostic); ADR [0010](./0010-lua-configuration-is-parsed-never-evaluated.md) (the untrusted parse
  stays on the Agent, behind `IPzConfigDocument`); ADR
  [0022](./0022-operation-lifecycle-and-per-server-locking.md) (why a read is not an Operation); ADR
  [0020](./0020-agent-protocol-versioning-and-catalogue.md) (the additive `ServerConfigContent` event, no version
  bump); ADR [0018](./0018-authorization-is-evaluated-server-side-on-every-request.md) (the read authorizes
  server-side, fail-closed); ADR [0016](./0016-tenant-isolation-query-filter-and-default-tenant.md) (the reply is
  accepted only from the Server's owning Agent).

## Context

F20b's editor is blind: the operator types a dotted path, a type, and a value by hand, with no current values in
front of them. F20c makes it a real editor, and the load-bearing prerequisite is a **live, non-mutating read** of
the four config files from the Agent to the control plane. Three forces shape how that read is carried.

**The Operation payload cap cannot carry a file.** `Operation.MaxCommandPayloadLength` is 2 KB and an Operation
*result* is expected to stay small; a parsed `_SandboxVars.lua` (~45 KB of text, plus its structured values and its
harvested comment tooltips) is more than an order of magnitude over. Forcing the read through the Operations engine
would also saddle a read with a per-server lock, an audit row, and a persisted revision it has no business creating.

**No SignalR streaming exists, by choice.** Every agent↔web message today is a discrete `Envelope<T>` hub-method
send; there is no `IAsyncEnumerable`/`ChannelReader` transport anywhere, and F27 established that discrete,
batched sends over the persistent connection are enough. A config read is one request and one (chunked) reply — a
correlated exchange, not a long-lived stream.

**The trust boundary is the Agent.** A config file's bytes are attacker-influenced (PRD 38), and ADR 0010 keeps the
untrusted Lua parse on the Agent, behind `IPzConfigDocument`. The read must not move that parse onto the control
plane, even though F20b gave Infrastructure a reference to `ZWarden.PzConfig`.

## Decision

- **A dedicated, read-only, non-Operation channel, correlated by request.** ZWarden.Web sends
  `RequestServerConfigRead` to the owning Agent's connection, carrying only the Server id, the file, and a
  **correlation id** — three bare string arguments, exactly as F27's `StartServerLogStream` carries a bare id, so
  the request is **transport plumbing, not a protocol message**: it is neither an `AgentCommand` nor an
  `AgentEvent`, and never enters the closed command vocabulary (ADR 0020; the `ClosedCommandVocabularyTests`
  reflect only over `AgentCommand`s). The Agent replies with one or more `ServerConfigContent` sends — an additive
  `AgentEvent` (no version bump, ADR 0020) — each echoing the correlation id.
- **The Agent is the single parser and ships a structured view.** On a request the Agent reads the live file once,
  runs it through the same `IPzConfigParser` seam the F20b writer uses, and ships a **layer-neutral view**: each
  scalar setting as `(path, value, kind, leading-comment?)`, the parse diagnostics, the canonical value-snapshot
  hash (the drift baseline), and the **raw file text** for the advanced raw view. The web tier consumes typed data,
  never Lua — the untrusted parse stays on the Agent (ADR 0010). A file that does not exist, or does not parse, is a
  **first-class status** on the reply (`FileMissing` / `ParseFailed` with the diagnostics), never an exception and
  never a torn connection.
- **Comments cross raw; the schema overlay and tooltip precedence are applied on the control plane.** The Agent
  ships each setting's raw harvested comment (a side-map value, ADR 0011 — never part of the snapshot, diff, or
  drift). ZWarden.Infrastructure — which already references `ZWarden.PzConfig` — overlays the ZWarden schema
  (friendly label, section, widget-relevant type, range, default) and sanitizes the comment
  (`PzCommentSanitizer`), resolving the tooltip by precedence: **schema description if authored → else the
  sanitized file comment → else none**. A key with no schema entry falls into an **"Other"** section with a
  comment-only tooltip and is passed through unvalidated (F20a's unknown-key rule). Sanitizing a comment string is
  not parsing Lua, so it is safe on the control plane; the parse itself is not.
- **The reply is chunked, bounded, and transient.** The Agent serializes the view to canonical JSON and splits it
  into sequenced `ServerConfigContent` chunks under a comfortable per-message size, each carrying the correlation
  id, its index, and the total count. ZWarden.Web reassembles by correlation id into a **bounded** buffer (a capped
  chunk count and total size — a compromised Agent cannot storm the socket), completes the pending request, and
  discards it. Nothing is persisted.
- **The reply is accepted only from the Server's owning Agent.** A pending read records the owning Agent it was
  routed to; a `ServerConfigContent` whose reporting Agent (trusted off the connection principal) is not that Agent,
  or whose correlation id matches no pending read, is dropped (the ownership guard, ADR 0016 / trust-boundaries.md
  §8) — one Agent cannot answer another's read.
- **A missing peer and a silent peer are first-class, not hangs.** If the owning Agent is offline the read returns
  **AgentOffline** at once (the registry has no connection to send on); if it is connected but never replies, the
  pending read **times out** and returns `TimedOut`. Both are operator-facing states, not stack traces (PRD 2.3).
- **Authorized server-side, fail-closed, on the existing permission.** The read resolves the Server through the
  tenant filter and authorizes the server-scoped **`Server.Configuration.Edit`** permission (ADR 0018), matching
  the sibling read seam `IServerConfigurationHistory` (F20b); an unknown Server or an unauthorized caller gets a
  denial outcome, never another tenant's file. The reserved `Server.Configuration.View` permission is left unused
  for a possible v1.1 read-only split.

## Alternatives considered

- **A non-mutating Operation whose result carries the file.** Rejected: it collides with the 2 KB result norm and
  drags a read through the per-server lock, the audit store, and the revision machinery it has no reason to touch
  (ADR 0022 / 0011). A read is not durable work.
- **Introduce real SignalR streaming (`IAsyncEnumerable`/`ChannelReader`).** Rejected: it invents a transport
  pattern the codebase has deliberately avoided, adding reconnect/backpressure surface, and buys nothing over
  chunked discrete sends at this size. A read is a single request/reply, not an open-ended stream.
- **Ship raw bytes and parse on the control plane.** The F20b `Infrastructure → ZWarden.PzConfig` reference means
  Web *could* parse. Rejected on trust posture: it re-runs the size/depth pre-check and an untrusted Lua parse on
  the control plane for no gain and duplicates the Agent's parse (ADR 0010). The Agent stays the single parser.
- **Raise `MaximumReceiveMessageSize` instead of chunking.** Rejected: it raises the ceiling for *every* agent→web
  message, widening the surface a compromised Agent can flood, to solve a problem local to one large reply.
  Chunking bounds the cost where it arises.
- **A new `Server.Configuration.View` permission for the read.** Available as a fallback, but not taken for v1.0:
  the sibling config read (`IServerConfigurationHistory`) already gates on `Server.Configuration.Edit`, and the read
  exists to serve the editor. Splitting a read-only viewer role can come later without a wire change.

## Consequences

- **The editor can show current values without a new transport family or a schema change.** One additive event,
  three transport constants, and a correlated coordinator reuse the F10/F27 machinery; no new entity, typed-id
  prefix, migration, or permission.
- **A config read is transient and unaudited.** It creates no revision, writes no audit row, and holds no lock —
  the audit trail and the revision history remain the write path's story (F20b). An operator viewing config leaves
  no durable trace by design.
- **A large or malicious file is bounded, not unbounded.** F20a's size/depth pre-check already bounds what the
  Agent will parse; the web-side chunk-count and total-size caps bound what a compromised Agent can send. A file
  that trips either surfaces as a read failure, not a hang or an out-of-memory.
- **A read can fail in four operator-facing ways** — the Server is not visible/authorized, the Agent is offline,
  the Agent is silent (timeout), or the file is missing/unparseable — each a rendered state with a reason, never an
  exception. The editor (F20c PR-C) renders them.
- **Comments are locale-generated output that now reach a browser.** They cross raw (bounded), are sanitized of PZ
  UI markup on the control plane, and are rendered as data, never markup — the untrusted-data posture that F20a
  and PRD 38 require.
