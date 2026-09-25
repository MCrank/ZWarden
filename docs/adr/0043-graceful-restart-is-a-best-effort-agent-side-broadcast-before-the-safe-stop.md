# 43. Graceful restart is a best-effort, Agent-side broadcast+countdown that wraps the F15 safe stop

Before a restart-causing Operation takes a Server down, the Agent **broadcasts a `servermsg` countdown to
connected players over its owned RCON connection, then runs the existing F15 safe stop→start**. The
broadcast is **best-effort and never blocks the restart**: if RCON is unreachable, disabled, or a send
fails, the warning is skipped or logged and the restart proceeds anyway. It is the **default** for every
restart path — F15 restart, F17 update, F22 mod-apply — driven by one Agent-side
`IServerRestartCoordinator`; the countdown is a bounded, strictly-descending schedule (default
`300 → 60 → 30 → 10` s) that an operator may override, replace with a custom message, or skip.

- Status: accepted
- Decided in: #114 (surfaced planning F22 #44; two maintainer forks — cross-cutting default + multi-step
  countdown — taken as recommended)
- Bears on: PRD 18 (RCON is not a shell), PRD 29/30 (credential ownership, untrusted output); ADR
  [0026](./0026-rcon-foundation-private-transport-and-agent-owned-credential.md) (RCON is Agent-only,
  the password never reaches Web), ADR
  [0022](./0022-operation-lifecycle-and-per-server-locking.md) (mutating per-server lock + progress-kept
  lease), ADR [0032](./0032-remote-console-runs-policy-gated-rcon-under-an-elevated-permission.md) (F28
  denies `quit`; graceful restart is the safe complement), ADR
  [0020](./0020-agent-protocol-versioning-and-catalogue.md) (additive, no version bump)

## Context

Mods and config load only on boot, so mod management (F22) and updates (F17) end in a restart, and an
operator asked whether active players can be warned first. PZ exposes exactly the primitive needed:
`servermsg "…"` broadcasts to all players and returns `Message sent.`, with **quoting mandatory**
(`docs/research/project-zomboid-runtime.md` §7). Three facts shape the design:

- **RCON is Agent-only (ADR 0026).** The RCON client and the password live in `ZWarden.Rcon`, which
  `ZWarden.Web` cannot reference; the password is generated and owned by the Agent. So the broadcast
  **can only originate Agent-side** — the control plane cannot send it.
- **The safe stop already exists (F15).** A restart is `docker restart` with `StopTimeoutSeconds` (120),
  which converts SIGTERM to the image's blessed FIFO `save`→`quit`. Graceful restart must *wrap* that,
  not reinvent it, and must never send `quit` itself (that is F15's job; F28 denies it for the same
  reason).
- **The restart is a mutating, per-server-locked Operation (ADR 0022)** whose lease (5 min) is extended
  by each progress report. A multi-minute countdown must therefore report progress to stay alive, and
  must be bounded so it cannot pin the lock indefinitely.

## Decision

- **One Agent-side coordinator.** `IServerRestartCoordinator.WarnAsync` broadcasts the countdown;
  `RestartAsync` = `WarnAsync` then `IContainerRuntime.RestartAsync` (the F15 safe stop→start). The
  `RestartServer` command handler calls `RestartAsync`; F17's `ServerUpdateRunner` calls `WarnAsync`
  before its own restart; F22 inherits through F15. The broadcast reuses F19's mandatory-quoting pattern
  (`BroadcastMessageRules` in Domain, `ServerBroadcastCommandBuilder` in the Agent) so a `servermsg`
  cannot be broken out of its quotes.
- **Best-effort, never blocks.** RCON unreachable/disabled up front ⇒ the whole countdown is skipped and
  the restart runs immediately (there is nobody to warn and no reason to wait). A per-broadcast
  `RconException` is logged and swallowed; the countdown and restart continue. Only a **cancellation**
  (F11 cancel during the countdown) aborts — before any stop.
- **Bounded countdown.** `GracefulRestartRules`: a schedule is strictly descending, every lead-time
  positive, at most **8** steps, starting no later than **900 s**. An **empty** schedule means "skip the
  broadcast". The default (`AgentOptions.RestartWarningLeadSeconds = [300, 60, 30, 10]`, validated on
  bind) applies when a command carries no plan, so even automated restarts warn. Long gaps are waited in
  heartbeat-sized chunks that report progress, keeping the ADR 0022 lease alive.

  > **Amendment ([#254](https://github.com/MCrank/ZWarden/issues/254)):**
  > - **The default countdown is skipped on an empty server.** When a command carries **no plan** (the header
  >   Restart, an F17 update, an F22 mod apply), the Agent first asks PZ `players`. It skips the countdown only
  >   when the reply positively reads `Players connected (0)`. Warning nobody only delays the restart by the
  >   whole schedule.
  > - **The check fails safe.** A failed query, or a reply without PZ's header, keeps the countdown.
  > - **An explicit plan always warns.** The "Restart with a countdown" panel is the operator asking for it.
  > - **The countdown is visible.** The Operation's progress (`Restarting in N seconds.`, or `No players online;
  >   restarting without a countdown.`) is shown beside the server header's RESTARTING badge (#249), so a running
  >   countdown no longer looks stuck.
- **Additive wire (ADR 0020).** `RestartServer` gains an optional `GracefulRestartPlan? Plan`; the
  protocol version stays 1. The operator's chosen schedule/message rides the Operation's
  `CommandPayload` (`GracefulRestartPayload`), mapped to the wire plan by `OperationDispatcher.CommandFor`.
- **No new permission.** A graceful restart is a restart, gated by the existing server-scoped
  `Server.Restart` (F17 update still uses `Server.Update`). This is not the F28 arbitrary console, so
  `Console.Execute` does not apply.

## Alternatives considered

- **Control-plane orchestration (Web sequences broadcast → wait → restart).** Rejected: ADR 0026 keeps
  RCON Agent-only, so the broadcast is an Agent command regardless; Web-side timers fight the static-SSR
  posture, and coordinating the lock/lease across several Operations is far messier than one locked
  Operation that owns its countdown.
- **A separate `GracefulRestartServer` OperationKind / opt-in action.** Rejected as the primary shape:
  the issue wants every restart path to warn. Folding the plan into the existing `RestartServer` (with a
  null-plan default) makes F17/F22 inherit it for free; the plain header button already warns.
- **A single warning instead of a countdown.** Rejected per the maintainer fork — a descending schedule
  is what players expect and the issue explicitly raised repeated warnings.
- **Blocking the restart when RCON is down.** Rejected outright — the restart is the operator's intent;
  a courtesy warning must never gate it.

## Consequences

- Every restart now carries a short delay by default (up to the first lead-time, 5 min) before the
  server goes down. An operator who wants it *now* uses the "skip warning" control or an empty schedule;
  a stopped/unreachable server skips the wait automatically.
- The countdown holds the per-server lock for its duration. This is deliberate — a second mutation
  should not race a restart — and is bounded by `GracefulRestartRules` so it cannot pin the lock beyond
  15 minutes.
- There are now two default schedules a reader could confuse: the authoritative Agent default
  (`AgentOptions`, used by a plain restart) and the Web configure-form's default it sends explicitly.
  They are intentionally the same value; an admin who customizes the Agent default changes only the
  plain-restart path until the form is likewise updated.
- The broadcast text is Agent-composed and printable-ASCII by construction, so it never carries an
  em-dash or other non-ASCII that PZ's tokenizer would mangle; operator-supplied messages are validated
  to the same rule on the Web edge and re-validated on the Agent.
