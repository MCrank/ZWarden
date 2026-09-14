# Feature 27 Mini-Plan — Live Logs

**Status:** IN PROGRESS. Roadmap issue: [F27 (#47)](https://github.com/MCrank/ZWarden/issues/47). Track D — the feature that lets an operator *see what a server is doing* without SSH. **Depends on F16 (health & observability), merged.** Unblocks [F28 (#40 remote console)](https://github.com/MCrank/ZWarden/issues/40) and [F29 (#42 diagnostics)](https://github.com/MCrank/ZWarden/issues/42), and — with F29 — satisfies **PRD 64 criterion 12** ("diagnose common failures without SSH").

**Format:** PRD 60. **TDD is mandatory** (PRD 2.2). **Written against:** [`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (F27), ADR [0003](../adr/0003-blazor-blueprint-ui-library.md) (Blueprint wrappers + the 1 Hz interactive-circuit path the live panels use), ADR [0008](../adr/0008-docker-socket-access-via-wollomatic-socket-proxy.md) (the allowlist — **already permits `containers/{id}/logs`; F27 proves streaming through it, it does not amend it**), ADR [0020](../adr/0020-agent-protocol-versioning-and-catalogue.md) (additive-vs-breaking rules for the new event), ADR [0022](../adr/0022-operation-lifecycle-and-per-server-locking.md) (why a log follow is *not* an Operation), ADR [0018](../adr/0018-zwarden-owned-rbac.md) (permission model), ADR [0016](../adr/0016-tenant-isolation-query-filter.md) (logs are tenant-owned; the interactive island reads ownership-guarded caches instead of carrying tenant context), and [`trust-boundaries.md`](../trust-boundaries.md) §3 (observed-never-inferred) / §8 (**Agent log output is untrusted input — PRD 38**). One new ADR lands with the feature: **0030** (live-logs streaming model, sanitization, and the non-Operation subscription channel).

## Objective

Give the operator a **live tail** of a running server's container logs (stdout+stderr) in the browser, with **filters**, **bounded buffering**, and **Agent-side output sanitization** — logs are untrusted input. Streaming is **on-demand**: the Agent follows a server's logs only while an operator is watching, torn down when the last viewer leaves.

## The load-bearing decisions (settled with the maintainer before writing)

- **D-RISK — prove multiplexed `follow=true` through the socket proxy *first*.** The socket-proxy research ([`docs/research/docker-socket-proxy.md`](../research/docker-socket-proxy.md) §9, item 9) names long-lived, multiplexed, hijacked log streaming through wollomatic as **the single most likely place for an unpleasant surprise** in the whole proxy recommendation, and F27's issue says to "prove it early; if it does not hold, the allowlist or the proxy choice is what gives, not the feature." The allowlist **already permits** `GET /containers/{id}/logs` (path-only match, so `?follow=true&stdout=1&stderr=1` is permitted without change — confirmed against `AgentDockerRuntimeTests`), so **no ADR 0008 amendment is needed**. The **first commit of PR-A is a failing integration test** that follows a chatty container's logs with `Follow=true` **through the §3.5 wollomatic allowlist proxy** and asserts live frames arrive over a long-lived connection with stdout/stderr distinguished. If it cannot be made to pass, we stop and revisit the proxy choice — not the feature scope.
- **D-ONDEMAND — a log follow is a subscription, not an Operation and not an AgentCommand.** It is ephemeral, high-churn, read-only, per-viewer transport state — the opposite of a durable Operation (ADR 0022: no per-server lock, no audit, no lifecycle row) and outside the `AgentCommand` vocabulary (which is the operation-dispatch surface). Subscription control rides a **dedicated, transport-level Web→Agent hub channel** — `AgentHubProtocol.StartServerLogStream` / `StopServerLogStream`, carrying just a server id — registered with `connection.On<string>` on the Agent, invoked by Web via `IHubContext<AgentHub>.Clients.Client(connectionId)`. Recorded in **ADR 0030**; a PRD 15 architecture test asserts these are *not* `AgentCommand`s.
- **D-SANITIZE — sanitize on the Agent, render as data on the Web (PRD 38 / trust §8).** Before a line leaves the Agent it is: stripped of ANSI/CSI escape sequences and C0/C1 control characters (tab kept, newline is the frame boundary), **length-capped** (default 2 KiB, marked truncated), and **rate-capped** (a per-server lines/sec ceiling; overflow coalesces into a `Dropped` marker rather than flooding the socket — this is the "bounded" in bounded buffering, enforced at the source). The Web renders every line as **data** (Blazor auto-encoding; never `MarkupString`).
- **D-BUFFER — bounded per-server ring buffer on the Web, ownership-guarded, tail semantics.** A new `IServerLogBuffer` singleton (sibling of `IServerMetricsCache`) holds the last *N* lines per server in a ring buffer (default 2000, evict-oldest). `ReadSince(serverId, owningAgentId, afterSequence)` **enforces the same ownership guard as `ServerMetricsCache.GetLatest`** — a caller gets lines only when it names the Agent that reported them. A new subscriber sees the current buffer (the *tail*) then live deltas by monotonic sequence.
- **D-PERM — reuse `Permissions.ConsoleView`.** Already in the catalogue, `ServerScopable`, reserved-and-unused. Live logs = viewing server console output; F28 later adds `Console.Execute` on the same family. **No catalogue change, no permission ADR, no `PermissionCatalogueTests`/floor churn.**
- **D-UI — a live interactive island, no client-facing hub.** There is no Web→browser SignalR hub and F27 does not add one; the established pattern is *ownership-guarded cache + interactive island polling*. A new `LiveServerLogPanel.razor` (`@rendermode InteractiveServer`) subscribes on init, polls the buffer delta ~1 Hz, appends, and applies UI filters (stdout/stderr toggle + text contains); unsubscribes on dispose. It hangs in a new **Logs `BbCard`** on `ServerDetail`, gated on `Console.View` — architecturally identical to `LiveServerPanel`/`LivePlayerRosterPanel`.
- **D-SPLIT — two PRs.** **PR-A:** the risk gate + contracts + Agent follow/sanitize/subscribe + Web ingest/buffer/coordinator + ADR 0030. **PR-B:** the UI island + `ServerDetail` card (**closes #47**).

## The mechanism, end to end

```
operator opens Logs panel
  → LiveServerLogPanel (island) Subscribe(serverId, agentId)         [Web, ref-counted]
     → first viewer? Coordinator sends StartServerLogStream(serverId)  → owning Agent
        → Agent resolves the OWNED container, starts a Follow task:
           GET /containers/{id}/logs?follow=1&stdout=1&stderr=1&tail=N   (through the proxy)
           → MultiplexedStream frames → split into lines, tag stdout/stderr
           → SANITIZE (strip ANSI/control, cap length, rate-cap→Dropped)
           → batch (flush every ~250 ms or 50 lines)
           → SendServerLogBatchAsync(ServerLogBatch)                    → Web AgentHub
              → ingest guard (agent may only append for a server it owns)
              → IServerLogBuffer.Append (ring buffer, bounded)
  ← island polls ReadSince(cursor) ~1 Hz → appends new lines, applies filters
operator closes panel / navigates away
  → island Dispose → Unsubscribe → last viewer? Coordinator sends StopServerLogStream → Agent cancels the Follow task
```

Reconnect: an Agent reconnect tears its follow tasks down; the Web islands re-subscribe on their next poll cycle, re-issuing Start. An Agent that is offline when a viewer subscribes: the coordinator no-ops the Start (buffer stays empty; the panel shows "awaiting host"), and re-issues on the next subscribe.

## Contracts (all additive — ADR 0020, no version bump)

- `enum LogStream { Stdout, Stderr }` (Contracts).
- `sealed record ServerLogLine(long Sequence, DateTimeOffset Timestamp, LogStream Stream, string Text, bool Truncated)`.
- `[ProtocolMessage("server.log-batch")] sealed record ServerLogBatch(ServerId ServerId, IReadOnlyList<ServerLogLine> Lines, bool Dropped) : AgentEvent` — **realises the `LogEntry → F27` slot reserved on `AgentEvent`**. `Dropped` = the rate cap coalesced lines since the last batch.
- Subscription control is **not** a protocol message (D-ONDEMAND): `AgentHubProtocol.StartServerLogStream` / `StopServerLogStream` string constants only.
- Update `EnvelopeSerializationTests`, `ProtocolCompatibilityTests` (assert `ProtocolVersion.Current` stays **1**), and the closed-vocabulary tests (the batch is an `AgentEvent`; Start/Stop are not `AgentCommand`s).

## Scope, by PR

### PR-A — Streaming core + risk gate (branch `feat/f27-live-logs`)
1. **Risk gate first (integration, `[Category("Networked")]`):** extend `AgentDockerRuntimeTests` (or a sibling) — start a container that emits to stdout+stderr on an interval; through the **§3.5 wollomatic allowlist proxy**, follow its logs with `Follow=true`; assert live frames arrive incrementally over a long-lived stream, both streams are distinguished, and cancellation tears the stream down cleanly. Keep the existing "denied verb → 405" assertion. **This commit must go red→green before the rest of PR-A is written.**
2. **Contracts:** the types above + serialization/additivity/closed-vocabulary tests.
3. **Agent — engine:** `IDockerEngine.FollowLogsAsync(containerId, tailLines, Func<LogFrame> sink | ChannelWriter, ct)` over the `MultiplexedStream` overload (preserves stdout/stderr; the current `IProgress<string>` demux overload flattens them). `DockerDotNetEngine` impl. Non-following `ReadLogsAsync` stays for F17.
4. **Agent — runtime:** `IContainerRuntime.FollowServerLogsAsync(ServerId, ...)` — ownership-scoped discovery (owned container only; foreign/absent ⇒ no stream), delegates to the engine.
5. **Agent — sanitizer:** `LogLineSanitizer` (pure, exhaustively unit-tested): ANSI/CSI + C0/C1 strip, length cap+`Truncated`, rate cap → `Dropped`.
6. **Agent — subscription service:** `ServerLogSubscriptionService` — `Start(serverId)`/`Stop(serverId)`, one follow task + CTS per server, dedupes double-start, batches (flush interval/size), pushes `ServerLogBatch` via a new `SendServerLogBatchAsync` on the connection; guarded by `if (_connection is { State: Connected })`. Wire `connection.On<string>(StartServerLogStream/StopServerLogStream, …)` in `SignalRControlPlaneConnection.BuildConnection`. New `AgentOptions`: `LogTailLines` (200), `LogLineMaxBytes` (2048), `LogMaxLinesPerSecond` (500), `LogBatchFlushMs` (250) — all validated > 0.
7. **Web — buffer:** `IServerLogBuffer` (`Application.Servers`) + `ServerLogBuffer` (`Infrastructure.Servers`) — bounded ring buffer, ownership-guarded `ReadSince`. `ServerLogBufferOptions.MaxLinesPerServer` (2000).
8. **Web — coordinator:** `IServerLogSubscriptionCoordinator` — ref-count per server; first Subscribe ⇒ Start to the owning Agent (via `IAgentConnectionRegistry` + `IHubContext<AgentHub>`), last Unsubscribe ⇒ Stop; Agent-offline is a graceful no-op.
9. **Web — ingest:** `AgentHub.ServerLogBatch(envelope)` → guard (the reporting Agent, from `AgentClaims`, must own the server — foreign/absent ⇒ no-op, trust §8) → `IServerLogBuffer.Append`.
10. **ADR 0030** (streaming model + sanitization + non-Operation channel).

### PR-B — Live logs UI (closes #47)
1. `LiveServerLogPanel.razor` (`InteractiveServer`) — Subscribe/Unsubscribe lifecycle (`IDisposable`), ~1 Hz `ReadSince(cursor)` poll, append + advance cursor, **stdout/stderr filter toggle + text-contains filter** (client-side over the buffer), tail cap in view, lines rendered as **data**. Ids cross the boundary as strings; no tenant context (parent authorizes).
2. `ServerDetail.razor` — a Logs `BbCard` gated `IPermissionChecker.EvaluateAsync(user, Permissions.ConsoleView, serverId)`, embedding the island. Use the `blazorblueprint` MCP to pick a scroll/log region primitive if a fit exists; otherwise a Tailwind mono scroll box.
3. Chores: `npm run build:css` + commit `wwwroot/app.css` ([[tailwind-app-css-rebuild]]); **bump the Web.Tests floor in BOTH the csproj and `ci.yml` `tier1-silent-drop-guard`** ([[web-tests-discovery-floor-bump]]); bUnit `JSRuntimeMode.Loose`.

## Test plan (TDD, per PR)

- **PR-A:** the risk-gate integration test (step 1, first); `LogLineSanitizer` tests (ANSI/control/length/rate→Dropped); `FollowServerLogsAsync` ownership-scoping (fake engine: owned follows, foreign/absent no stream); `ServerLogSubscriptionService` (start/stop/dedupe/unknown-server no-op/teardown-on-cancel); Contracts serialization + additivity + closed-vocabulary; `ServerLogBuffer` (bounded eviction, ownership guard rejects foreign agent, `ReadSince` cursor); coordinator (ref-count start/stop-once, agent-offline graceful); `AgentHub.ServerLogBatch` ingest guard (owning vs foreign/absent no-op).
- **PR-B:** `LiveServerLogPanelTests` (bUnit, loose JSInterop): renders lines encoded-as-data, stdout/stderr + text filters, tail cap, Subscribe/Unsubscribe called; `ServerDetail` Logs-card render + `Console.View` gating test.

## Non-scope

- **Log retention or search as a product feature** (issue non-goal) — the buffer is transient, bounded, in-memory; nothing is persisted to the DB.
- **A client-facing SignalR hub / true server-push to the browser** — the cache + island-poll pattern stands.
- **Always-on streaming** of unwatched servers (D-ONDEMAND rejects it — it maximizes the flagged proxy-streaming surface).
- **Log download / export** and the **sanitized support package** (F30).
- **The RCON console and command execution** (F28 — `Console.Execute`).
- **A multi-server aggregated log view** (post-1.0 operational UX, F36).
- **Widening the ADR 0008 allowlist** — logs are already permitted; no new verb.

## Open confirmations to raise if they bite

- **MultiplexedStream vs the IProgress demux overload under `Follow=true`.** The risk-gate test decides the exact primitive; the plan assumes `MultiplexedStream` for the stdout/stderr discriminator. If the fork's streaming behaviour differs, the sink shape adjusts — the seam (`FollowLogsAsync`) does not.
- **wollomatic `-shutdowngracetime` mid-stream** (research §9 item 9): the risk-gate test should also confirm a clean teardown, not just clean startup.
- **Rate-cap default (500 lines/s).** A world-load burst or a mod spewing errors could exceed it; the `Dropped` marker makes the loss visible rather than silent. Tune if it bites in the real-app check.
