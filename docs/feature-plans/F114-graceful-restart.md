# Feature #114 Mini-Plan — Graceful Restart (player broadcast + countdown)

**Status:** delivered on branch `feat/f114-graceful-restart` (closes
[#114](https://github.com/MCrank/ZWarden/issues/114)), one commit per slice. PR-A = the broadcast
primitive + Agent-side graceful-restart orchestration + F17 adoption; PR-B = the control-plane wiring,
operator surface, and ADR 0043. Delivered as one branch/PR rather than two. Cross-cutting follow-up surfaced by
**F22 (#44)** — mods load only on boot, so every mod apply ends in a restart and an operator asked
whether active players can be warned first. The answer belongs here because **every** restart path
wants it.

**Written against:** the issue #114; PRD 2.2 (TDD mandatory), PRD 2.3 (supportability — a restart is
an authorized, audited Operation), PRD 18/29/30 (RCON is policy-gated, credential-owned, and its output
untrusted); [ADR 0026](../adr/0026-rcon-foundation-private-transport-and-agent-owned-credential.md)
(the RCON client is Agent-only, the password is Agent-owned — **the broadcast can only originate
Agent-side**); [ADR 0022](../adr/0022-operation-lifecycle-and-per-server-locking.md) (mutating
server-scoped Operation + per-server lock + progress-kept lease); the F15 safe stop (FIFO
`save`→`quit`, `StopTimeoutSeconds`); F17's SteamCMD restart
([ADR 0025](../adr/0025-steamcmd-update-orchestration.md)); F19's mandatory-quoting core
(`PlayerCommandRules`/`PlayerCommandBuilder`); [ADR 0032](../adr/0032-remote-console-runs-policy-gated-rcon-under-an-elevated-permission.md)
(F28 denies `quit` because F15 owns safe stop — graceful restart is the safe complement); and
`docs/research/project-zomboid-runtime.md` §7 (`servermsg "…"` → `Message sent.`, **quoting
mandatory**, ~line 965; one of the 66 dispatchable RCON commands).

## Objective

Warn connected players before a restart takes their server down: broadcast `servermsg "…"` over RCON,
count them down through a **multi-step** descending schedule, then proceed with the existing F15 safe
stop → start. The warning is **best-effort and never blocks the restart** — if RCON is unreachable or
the server is already down, the broadcast is skipped and the restart proceeds regardless.

**Two maintainer forks, both taken as recommended (2026-09-18):**

1. **Cross-cutting default.** Graceful warning + countdown is the **default** behaviour of every
   restart-causing path — F15 restart, F17 update, F22 mod-apply — not an opt-in side action. A single
   shared Agent-side coordinator does the broadcast+countdown; each path calls it. The default schedule
   lives Agent-side (`AgentOptions`) so even automated/param-less restarts warn; the operator can
   override the schedule/message or explicitly **skip** it per action.
2. **Multi-step countdown.** A configurable descending schedule of lead-times (default
   `300 → 60 → 30 → 10` seconds), each firing one `servermsg`, then the stop. Bounded (max lead
   ≤ 15 min, ≤ 8 steps) so it can never hold the per-server lock indefinitely.

The load-bearing realisation, confirmed against the code first: like F22, this is **orchestration over
existing machinery**, not new low-level plumbing. The RCON transport (F18), the mandatory-quoting
pattern (F19), the safe stop (F15), the per-server-locked mutating Operation with a progress-kept lease
(F11/ADR 0022), and the additive-contract discipline (ADR 0020) all already exist. #114 adds one RCON
verb (`servermsg`) and one Agent-side coordinator that sequences broadcast → countdown → the existing
`ContainerRuntime.RestartAsync`.

| Restart path | How it becomes graceful |
|---|---|
| **F15 restart** (`OperationKind.RestartServer`) | `AgentCommandProcessor`'s `RestartServer` case calls `IServerRestartCoordinator.RestartAsync` (broadcast+countdown+`ContainerRuntime.RestartAsync`) instead of the bare runtime verb. |
| **F17 update** (`ServerUpdateRunner`) | The runner calls `IServerRestartCoordinator.WarnAsync` (broadcast+countdown only) immediately before its existing `_runtime.RestartAsync` (`ServerUpdateRunner.cs:73`). |
| **F22 mod-apply** | Already restarts via F15 `RestartServer` — inherits the warning for free. |

## Settled design

### The broadcast primitive (Domain + Agent), reusing F19

- **`BroadcastMessageRules` (`ZWarden.Domain.Servers`, pure, shared Web+Agent).** Mirrors
  `PlayerCommandRules.ValidateReason`: a broadcast message may contain spaces (it is quoted) but is
  rejected if empty, over a bound (`MaxBroadcastMessageLength`, ~200), or contains a `"`, a control
  character, or a non-ASCII character. This is what makes `servermsg` injection-proof by construction
  (research §7 quirk 10). It also owns the **countdown text formatter** — a pure
  `FormatCountdown(secondsRemaining, reason?)` → e.g. `"Server restarting in 5 minutes."` /
  `"Server restarting in 30 seconds — scheduled maintenance."` — so the exact wire text is unit-tested
  with no I/O and both sides agree.
- **`GracefulRestartRules` (`ZWarden.Domain.Servers`, pure).** Validates a countdown schedule:
  lead-times are positive, distinct, sorted descending, ≤ `MaxCountdownSteps` (8), max lead
  ≤ `MaxCountdownSeconds` (900). An **empty** schedule is valid and means *skip the broadcast*.
- **`ServerBroadcastCommandBuilder.ServerMessage(string)` (`ZWarden.Agent.Servers`).** Validates via
  `BroadcastMessageRules` (throwing before a byte reaches RCON), then returns `servermsg "<message>"`
  — the exact `PlayerCommandBuilder` validate-then-quote shape ADR 0026 deferred out of `ZWarden.Rcon`.

### The Agent-side coordinator (the reusable unit)

- **`IServerRestartCoordinator` / `ServerRestartCoordinator` (`ZWarden.Agent.Servers`).** Copies the
  `ConsoleAdministration`/`PlayerAdministration` orchestrator shape (resolve endpoint → build → execute
  → dispose-in-finally):
  - `WarnAsync(ServerId, GracefulRestartPlan? plan, IOperationProgressReporter progress, ct)` —
    resolves the **effective** plan (`plan ?? AgentOptions default`), and, if the schedule is non-empty
    **and** RCON resolves, walks the descending schedule: at each lead-time broadcast the formatted
    `servermsg`, report an `OperationProgress` status line ("restarting in N…"), and delay until the
    next step. **Best-effort:** every `RconException` / `RconResolveStatus.{NoContainer,RconDisabled}`
    is caught, logged, surfaced as a progress line, and swallowed — `WarnAsync` never throws from the
    broadcast half. A **cancellation** (F11 cancel during the window) *does* propagate, aborting before
    any stop. Progress reports during long gaps also **keep the F11 lease alive** (same reason F17
    reports every `UpdatePollInterval`).
  - `RestartAsync(ServerId, GracefulRestartPlan? plan, progress, ct)` = `WarnAsync` then
    `IContainerRuntime.RestartAsync(serverId, ct)` (the F15 safe stop→start). The restart half is
    **not** best-effort — a Docker failure there is a real failed Operation (existing `LifecycleAsync`
    catch semantics).
  - Collaborators: `IRconEndpointResolver`, `IRconConnectionFactory`, `IContainerRuntime`,
    `TimeProvider` (testable delays), `IOptions<AgentOptions>`, `ILogger`.
- **`AgentOptions` default schedule.** New `RestartWarningLeadSeconds` (`[300, 60, 30, 10]`) and
  `RestartWarningMessage` (nullable reason/prefix), validated on bind via `GracefulRestartRules`.

### Contracts (additive, ADR 0020 — version stays 1)

- **`GracefulRestartPlan` record (`ZWarden.Contracts.Protocol.Messages`)** —
  `(IReadOnlyList<int> WarningLeadSeconds, string? Reason = null)`. `null` plan on the command ⇒ Agent
  default; empty `WarningLeadSeconds` ⇒ explicit skip.
- **`RestartServer` gains an optional `GracefulRestartPlan? Plan = null`.** Nullable/defaulted, so the
  wire stays backward-compatible and `ClosedCommandVocabularyTests` is unaffected. `UpdateServer` stays
  payload-free — F17 uses the Agent default via `WarnAsync`.

### Control-plane wiring (Application + Web)

- **`GracefulRestartPayload` (`ZWarden.Application.Servers`)** — the layer-neutral JSON
  (`WarningLeadSeconds`, `Reason`, or a skip marker) written to `Operation.CommandPayload` at enqueue,
  read in `OperationDispatcher.CommandFor(OperationKind.RestartServer, payload)` to build
  `new RestartServer(plan)`. A null payload keeps `new RestartServer()` (Agent default) — so F22's
  existing enqueue path is untouched and still warns.
- **`IServerLifecycle.RestartAsync` gains an optional `GracefulRestartPlan?`** (default `null`);
  `ServerLifecycle` serialises it to the payload when present. All other verbs unchanged.
- **Operator surface (ServerDetail).** The F15 restart control gains an optional message + a
  "warn players" toggle / schedule choice, gated by the existing server-scoped **`Server.Restart`**
  permission (graceful restart *is* a restart — no new permission; consistent with F15).

## Decisions taken without escalation (low-risk, pattern-matching)

- **Reuse `Server.Restart`** — no catalogue change (graceful restart is a restart; F17 update still
  uses `Server.Update`). RCON here is not the F28 arbitrary console, so `Console.Execute` does not
  apply.
- **Broadcast lives in `ZWarden.Domain.Servers` / `ZWarden.Agent.Servers`, not `…Players`** — a
  server-wide `servermsg` is a lifecycle concern, not player administration, though it reuses the F19
  quoting *pattern*.
- **No new `OperationResult` type** — a graceful restart completes exactly as an F15 restart does; the
  countdown is observable through `OperationProgress` status lines (untrusted, escaped at render).
- **One new ADR (0043)** — "Graceful restart is a best-effort Agent-side broadcast+countdown wrapping
  the F15 safe stop, default across restart paths; an RCON failure never blocks the restart." This is a
  cross-cutting, hard-to-rediscover behaviour (like F15/F17/F25 each earned an ADR), so it earns one.

## Dependencies

**F18** (RCON transport + Agent-owned credential, ADR 0026) — the send path. **F19** (mandatory
quoting pattern) — reused. **F15** (safe stop `ContainerRuntime.RestartAsync`) — the stop→start.
**F11** (mutating per-server-locked Operation + progress-kept lease, ADR 0022) — the carrier.
Transitively F17 (`ServerUpdateRunner`) and F22 (mod-apply restart), which adopt it. **No new
third-party dependency; no external network dependency** (RCON is on the private ZWarden network).

## Scope

### PR-A — Broadcast primitive + Agent-side graceful-restart orchestration

*(Domain + Contracts + Agent + tests. No Web/Application/UI, no migration.)*

1. **`BroadcastMessageRules`** (validate + `FormatCountdown`) and **`GracefulRestartRules`** (schedule
   validation) in `ZWarden.Domain.Servers` — pure, fully unit-tested truth tables.
2. **`ServerBroadcastCommandBuilder.ServerMessage`** in `ZWarden.Agent.Servers` — validate-then-quote.
3. **`GracefulRestartPlan`** record + optional `RestartServer.Plan` in `ZWarden.Contracts`.
4. **`IServerRestartCoordinator` / `ServerRestartCoordinator`** — `WarnAsync` (best-effort broadcast +
   countdown, `TimeProvider` delays, progress + lease) and `RestartAsync` (warn then safe restart).
   `AgentOptions` default schedule + bind validation. DI in `HostingExtensions`.
5. **`AgentCommandProcessor`** — `RestartServer` case routes through the coordinator (new ctor
   collaborator + fake + `Processor(...)` default param); pass `progress` in.
6. **F17 adoption** — `ServerUpdateRunner` calls `coordinator.WarnAsync` before its `_runtime.RestartAsync`.
7. **Tests** — builder quoting/injection refusal; rules truth tables (schedule bounds, message
   hygiene, countdown text); coordinator (warnings fired in descending order with correct text;
   best-effort swallow of `RconException`/unreachable → restart still happens; empty schedule skips the
   broadcast; cancellation aborts *before* the stop; progress emitted); processor `RestartServer`
   success + malformed. Bump each affected `--minimum-expected-tests` floor to the exact new count.

### PR-B — Control-plane wiring, operator surface, ADR

8. **`GracefulRestartPayload`** (`ZWarden.Application.Servers`) + `OperationDispatcher.CommandFor`
   mapping for `RestartServer` (null payload ⇒ `new RestartServer()`).
9. **`IServerLifecycle.RestartAsync(… , GracefulRestartPlan? = null)`** + `ServerLifecycle` payload
   write. F22's mod-apply "Apply & restart" passes the default (unchanged call keeps warning).
10. **ServerDetail restart control** — optional message + warn/skip + schedule, `Server.Restart`-gated,
    static-SSR `EditForm` (F19/F21 pattern). Untrusted progress/countdown escaped at render.
11. **ADR 0043.**
12. **Tests** — `OperationDispatcherMapTests` (RestartServer with/without payload), endpoint, Web bUnit
    (control render + authz gate + post handling). Bump `--minimum-expected-tests` in **both** the
    Web.Tests csproj **and** ci.yml's silent-drop guard; rebuild `wwwroot/app.css` if any new Tailwind
    class lands.

## Non-scope

- **Arbitrary RCON console passthrough** — that is F28/F29 (ADR 0032); graceful restart sends only the
  fixed, validated `servermsg`.
- **`quit`/`save` from the control plane** — the shutdown stays the F15 blessed FIFO path; the
  coordinator only *broadcasts*, then calls the existing safe restart.
- **A restart *scheduler*** (cron-like planned restarts) — a later feature; #114 is the warning wrapper
  around a restart an operator (or F17/F22) already triggers.
- **Per-tenant / per-server persisted warning policy** — the default lives in `AgentOptions`; a
  persisted policy entity is out of scope (config-as-truth posture, cf. F22).

## Testing & verification

TDD in slices, each its own commit (test → red → code → green), grouped PR-A then PR-B. Build with the
pinned SDK, then run each affected test project's compiled `.exe` directly (see
`zwarden-local-toolchain`). No new package ⇒ no lock-file regeneration; still verify `--locked-mode`.
New Domain/Agent tests carry their own floors in PR-A; the Web.Tests count changes in PR-B ⇒ bump the
floor in **both** the csproj and ci.yml. This mini-plan is the durable handoff.
