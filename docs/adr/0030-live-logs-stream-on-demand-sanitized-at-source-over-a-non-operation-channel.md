# 30. Live logs stream on demand, are sanitized at the source, and ride a non-Operation subscription channel

ZWarden shows an operator a **live tail** of a Server's container logs (F27) without SSH. The Agent follows the
owned container's combined stdout/stderr through the Docker Engine's `GET /containers/{id}/logs?follow=1` — an
**already-allowlisted read verb** (ADR 0008), so streaming needs **no allowlist change**, only proof it survives the
socket proxy. Streaming is **on demand**: the Agent follows a Server's logs **only while an operator is watching**,
started on the first viewer and stopped on the last. Each line is **sanitized on the Agent** before it goes on the
wire (logs are untrusted input, PRD 38 / trust-boundaries.md §8), **batched and rate-capped** to bound the stream at
the source, and held on ZWarden.Web in a **bounded, per-(Server, reporting-Agent) in-memory tail** that the
interactive log panel polls. A live-log subscription is deliberately **neither an Operation** (ADR 0022 — no
per-server lock, no audit, no lifecycle row) **nor an `AgentCommand`** (the operation-dispatch vocabulary): it is
ephemeral, read-only, per-viewer transport control, so it rides its own hub channel.

- Status: accepted
- Decided in: F27 (#47)
- Bears on: PRD 38 (logs are untrusted input); Criterion 12 (diagnose common failures without SSH — with F29); ADR
  [0008](./0008-docker-socket-access-via-wollomatic-socket-proxy.md) (`containers/{id}/logs` is allowlisted; this
  feature proves `follow=1` streaming rides it and adds no verb); ADR
  [0022](./0022-operation-lifecycle-and-per-server-locking.md) (why a follow is not an Operation); ADR
  [0020](./0020-agent-protocol-versioning-and-catalogue.md) (the additive `ServerLogBatch` event, no version bump);
  ADR [0023](./0023-server-health-model-and-observed-delivery.md) / F16 (the cache-plus-interactive-island live-UI
  pattern this reuses); ADR [0016](./0016-tenant-isolation-query-filter-and-default-tenant.md) (the panel reads an
  ownership-guarded cache instead of carrying tenant context into the circuit).

## Context

The socket-proxy research ([`../research/docker-socket-proxy.md`](../research/docker-socket-proxy.md) §9 item 9)
named **long-lived, multiplexed, hijacked log streaming through wollomatic** as the single most likely place for an
unpleasant surprise behind the whole proxy recommendation — it had only ever been tested as one non-following
request. F27's scope line says to prove it early: *if it does not hold, the allowlist or the proxy choice is what
gives, not the feature.* So the shape of everything below waited on that proof.

Two further forces shaped the design. Logs are **untrusted** (PRD 38): a container's output can carry ANSI escape
sequences, control bytes, and arbitrary volume, and it reaches a browser. And the live-UI pattern already exists
(F16): an Agent pushes to an ownership-guarded process-local cache, and an interactive Blazor island polls it — there
is no Web→browser hub, and F27 had no reason to invent one.

The open question the decisions turn on: does the Agent follow **every** running Server continuously, or only those
an operator is actively watching? Continuous following is the simplest fit for the F16 push pattern, but it holds a
long-lived multiplexed proxy stream per running Server at all times — maximizing exactly the surface the research
flagged, and spending bandwidth on Servers nobody is looking at.

## Decision

- **Prove the proxy first.** A tier-2 integration test follows a chatty container's stdout **and** stderr with
  `follow=1` **through the §3.5 wollomatic allowlist**, asserts frames arrive incrementally over a long-lived
  connection with the two streams distinguished, and tears the stream down cleanly on cancel. It passed against
  Engine 29.2.1 — the risk is retired, and the allowlist is unchanged (the `logs` path already matches; the proxy
  does not constrain the `?follow=1` query string).
- **On-demand follow, ref-counted by viewers.** The Agent follows a Server only while watched. ZWarden.Web ref-counts
  the live viewers of each Server; the first viewer triggers `StartServerLogStream` to the owning Agent, the last
  triggers `StopServerLogStream`. Opening a follow backfills the last *N* lines (`tail`) then streams live.
- **The subscription channel is transport plumbing, not the protocol vocabulary.** `StartServerLogStream` /
  `StopServerLogStream` are hub method names carrying only a Server id — **not** `AgentCommand`s (which flow through
  the operation dispatcher and the closed vocabulary) and **not** Operations (no lock, no audit, no persisted row). A
  PRD 15 architecture test keeps them out of the command vocabulary. The upward `ServerLogBatch` is an additive
  `AgentEvent` (no version bump, ADR 0020), realising the reserved `LogEntry → F27` slot.
- **Sanitize at the source.** Before a line leaves the Agent it is stripped of ANSI/CSI/OSC escape sequences and
  C0/C1 control characters (tab kept), length-capped (marked truncated), and **rate-capped** — lines beyond a
  per-Server per-second ceiling are coalesced away and the batch flagged `Dropped`, so a flooding server cannot storm
  the socket and the loss is visible rather than silent. ZWarden.Web still renders every line as **data**, never
  markup — defense in depth, not a substitute for output encoding.

  > **Amendment ([#232](https://github.com/MCrank/ZWarden/issues/232)):** the rate cap bounds lines, not bytes. A PZ
  > start/stop burst (≈125 lines per 250 ms flush, with HTML-sensitive characters escaped to six bytes each)
  > serialized past SignalR's default 32 KB receive limit, and the hub closed the Agent's connection on it, every
  > 1–2 s while the Logs page was open. Each flush is now split into `ServerLogBatch` messages under
  > `AgentHubProtocol.StreamedMessageBudgetBytes` (24 KB), with `Dropped` on the first part only. The Agent hub also
  > sets an explicit, bounded `MaximumReceiveMessageSize` (`AgentHubProtocol.MaxReceiveMessageBytes`, 1 MB) as a
  > backstop, and both sides log an abnormal close at Warning.
- **Bounded, ownership-partitioned tail on the Web.** Lines land in a process-local ring buffer keyed by **both** the
  Server and the reporting Agent, capped per partition (the "bounded buffering" the feature requires). Partitioning
  by owner is the ownership guard: a batch forged by one Agent for a Server owned by another lands in its own
  partition and never surfaces to a reader naming the true owner — and, unlike the F16 metrics cache's last-writer
  model, cannot even shadow the owner's lines. Nothing is persisted.
- **Live panel reuses the F16 island pattern.** An interactive `@rendermode InteractiveServer` panel subscribes on
  open, polls the buffer by a monotonic sequence cursor, filters stdout/stderr and text client-side, and unsubscribes
  on close. It carries no tenant context (a circuit lacks one); the static parent page authorizes on `Console.View`.
- **Reuse `Console.View`.** Viewing live logs is gated by the existing (reserved, unused) `Console.View` permission —
  no new permission, no catalogue change. F28 later adds `Console.Execute` on the same family.

## Alternatives considered

- **Always-follow every running Server (the pure F16 pattern).** Rejected: it holds a long-lived multiplexed proxy
  stream per running Server whether or not anyone is watching — the exact surface the research flagged — and wastes
  bandwidth. On-demand confines the risky, resource-holding path to actual demand, which is also why the subscription
  language ("SignalR subscriptions") is in the scope line at all.
- **Model start/stop as `AgentCommand`s dispatched through the Operations engine.** Rejected: a follow is not durable
  work — it has no outcome, no progress, no per-server lock, and auditing every panel open/close would flood the
  append-only store. Forcing it through the operation lifecycle would add a persisted row and a lock per viewer for
  ephemeral streaming. A dedicated transport channel matches what it is.
- **A new Web→browser SignalR hub for true server-push.** Rejected: no client hub exists, and the cache-plus-island
  poll already delivers live updates at the cadence logs need. A second hub is architecture F27 does not require.
- **Sanitize on the Web at render time only.** Rejected as insufficient alone: bounding volume (the rate cap) must
  happen at the source to protect the socket, and stripping control sequences before they cross the wire is cheaper
  and defense-in-depth. Render-time data-encoding stays, on top.
- **A new `Server.Logs.View` permission.** Rejected for v1.0: `Console.View` already means "view server console
  output," is unused, and avoids a catalogue-and-ADR change. Splitting logs from the RCON console can come later if a
  real need appears.

## Consequences

- **The proxy recommendation's sharpest unverified claim is now tested** and pinned by a build-failing integration
  test, so drift between what the Agent streams and what the deployment allows is caught.
- **A Server's logs stream only while watched.** Unwatched Servers hold no follow; a watched Server whose Agent is
  offline simply shows no lines until it reconnects and the panel re-subscribes. A Server that is not yet started (or
  restarts) is retried, so a watched Server picks up when it comes up.
- **Logs are transient and bounded.** Nothing is persisted; the tail is a fixed number of lines per Server, and the
  rate cap makes a flooding server visible (`Dropped`) rather than unbounded. Log retention or search as a product
  feature is explicitly out of scope.
- **Dropped lines are a real, if rare, loss.** A burst above the per-second cap is coalesced away; the operator sees
  a "lines dropped" indicator, not the lines. The default is generous; it is tunable per Agent.
- **F28 (remote console) and F29 (diagnostics) build on this.** The console reuses the streaming-output surface; the
  diagnostics engine consumes the live-log capability. Both are unblocked.
