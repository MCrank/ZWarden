# Feature 8 Mini-Plan — Agent Runtime Skeleton

**Status:** ready for implementation. Roadmap issue: [F8 (#30)](https://github.com/MCrank/ZWarden/issues/30). Track C.

**Format:** PRD 60. **Written against:** PRD §14 (component layout), §16 (Web ↔ Agent boundary), §18/§40 (Agent messages, heartbeat) for the *shape* F8 must be ready to speak; Feature 8 (§ "Agent Runtime Skeleton"); [`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (Track C, F8) and §5 (the DAG: F7 → **F8** → F9 → F10); [`trust-boundaries.md`](../trust-boundaries.md) §9 rule 1 (a compromised Agent must not reach the database); the F7 protocol contracts ([ADR 0020](../adr/0020-agent-protocol-versioning-and-catalogue.md)) it consumes; and the F1 typed-ID foundation ([ADR 0014](../adr/0014-typed-id-pattern.md)). One load-bearing decision (the logging stack) was settled with the maintainer before writing.

## Objective

Turn `ZWarden.Agent` from F0's build-clean placeholder (`Host.CreateApplicationBuilder(args).Build().Run()`) into a **real, long-running Worker Service host** with a **stable self-identity**, **strongly-typed validated configuration**, a **hosted lifecycle**, an **in-process health-state model** (reusing F7's `AgentHealthStatus`), **graceful shutdown**, and **diagnostic hooks** — the foundation F9 (enrollment/trust) and F10 (SignalR transport) attach to — while adding **no** transport, **no** database persistence, **no** Docker and **no** RCON.

## The settled decision

**Logging stack — Serilog, adopted as ZWarden's logging stack, recorded as ADR 0021 (maintainer decision).** F8 is the first feature whose real job is host diagnostics, and the repo has no logging framework yet (Web included, on default `Microsoft.Extensions.Logging`). The Agent adopts **Serilog** via `Serilog.Extensions.Hosting`, with the **two-stage bootstrap** pattern (a bootstrap `Log.Logger` around host construction, then the host-integrated logger from configuration), **Console + rolling File** sinks, `FromLogContext` enrichment, and configuration-driven levels. ADR 0021 records this as the standard the Web side migrates to later — Serilog is introduced here, not forked here. Chosen over default MEL because structured, sink-configurable diagnostics are exactly F8's deliverable and the maintainer's stated preference; the cost knowingly taken is that Web's MEL→Serilog migration becomes a tracked follow-up rather than shipping atomically.

## Dependencies

**F7** (contracts), **F1** (typed ids), **F0** (host + build policy). Consumes:

- **F7 `ZWarden.Contracts`** — `AgentHealthStatus` (the coarse self-health the health-state model holds and F10 later puts on a heartbeat), `ProtocolVersion.Current` (the protocol the Agent declares in its startup banner), and, transitively, the `AgentId`/`ServerId` typed ids. The `ZWarden.Agent → ZWarden.Contracts → ZWarden.Domain` chain already exists; F8 adds no new project reference except the Serilog packages and the new test project.
- **F1 `AgentId`** (`agt-`) — the Agent's self-identity, `AgentId.New()` on first run, `AgentId.Parse` on reload.
- `trust-boundaries.md` **§9 rule 1** — the Agent references neither Infrastructure, EF Core, nor a provider. This makes F8's "local persistence where required" **file-based, not a database** — enforced by the existing `Agent_does_not_reference_persistence` architecture test, which stays green.

## Scope

1. **The Worker host (PRD §14).** Replace the top-level `Program.cs` with a configured host: Serilog two-stage bootstrap in a `try/catch/finally` with `Log.CloseAndFlush()`; `UseSerilog` reading from configuration; options binding + validation with `ValidateOnStart`; DI registration of the identity, health-state and diagnostics services; and **one `BackgroundService` — `AgentWorker`** that owns the Agent's lifecycle loop. With no transport yet, the loop is a **liveness/health tick shell** (it maintains health state and honours cancellation); F10 replaces the shell's body with the outbound connection.
2. **Agent identity + local persistence (file-based).** An `IAgentIdentity` exposing the current `AgentId`, backed by an `IAgentIdentityStore`: on startup, read the id from the **identity file**; if absent or unreadable-as-new, generate `AgentId.New()` and persist it **atomically** (temp-file + atomic replace, **BOM-less UTF-8**, canonical `agt-<uuid>` string). The identity file is the Agent's only F8 persistence need; it is *self-identity*, not *trust* — F9 binds it to an enrollment credential and the control plane.
3. **Configuration — `AgentOptions`, strongly typed and validated.** `IValidateOptions` + DataAnnotations, `ValidateOnStart` (fail fast): `IdentityFilePath` (defaults under a well-known per-OS local data directory), `ControlPlaneUri` (declared now for F10 — validated as an absolute `https`/`wss` URI, **not yet connected to**), `HeartbeatInterval` and `HealthReportInterval` (bounded `TimeSpan`s), and `ShutdownTimeout`. Invalid configuration stops the host at startup with an actionable message.
4. **Health-state model (reuses F7 `AgentHealthStatus`).** An in-process `IAgentHealthState` holding the current `AgentHealthStatus` — `Healthy` / `Degraded` / `Unhealthy` — settable by components with a reason and readable by diagnostics/F10. Transitions are logged. **Deliberately a plain in-process service, not the ASP.NET Core HealthChecks framework** — a Worker has no HTTP endpoint, and pulling in web hosting for a status enum would be scope the skeleton does not need (noted in Non-scope).
5. **Lifecycle + graceful shutdown.** `IHostApplicationLifetime` hooks; the worker observes the stopping token, drains cooperatively within `ShutdownTimeout`, and logs an ordered start/stop. A **structured startup banner**: Agent id, assembly/informational version, the `ProtocolVersion.Current` the Agent speaks, and the resolved configuration summary **with no secret rendered** (there is no secret in F8 yet — the discipline is established for F9's credential).
6. **Diagnostic hooks.** The structured start/stop banner; health-transition logging; and a small `IAgentDiagnostics` seam that reports the Agent's current identity, health, protocol version and uptime — the surface F9/F10/F16 attach richer diagnostics to. Minimal by intent.
7. **Tests** — a new **`ZWarden.Agent.Tests`** (offline tier) and one architecture-test addition.

## Non-scope

- **Transport / SignalR (F10):** the hub client, the outbound WSS connection, reconnect, protocol negotiation *at connect time*, and the *act of sending* heartbeats/state. F8 models health and owns the interval config; F10 puts them on the wire.
- **Enrollment, credentials, trust (F9):** F8 gives the Agent a *self-identity*; it does not enrol it, store a credential, or make it trusted. No secret-aware type appears in F8.
- **Database persistence (trust-boundaries §9 rule 1):** no EF, no provider, no `ZWarden.Infrastructure` reference. Local persistence is a single file.
- **Docker (F13), RCON (F18), operations engine (F11), anything PZ-shaped.** The `ZWarden.Agent → ZWarden.Rcon` reference from F0 scaffolding stays but is **unused** by F8.
- **ASP.NET Core HealthChecks / an HTTP status endpoint.** Considered and deferred — the Agent is a Worker with no inbound surface.

## Domain changes

**None.** `AgentId` (`agt-`) already exists in `ZWarden.Domain.Ids`; F8 adds no entity, no id, no prefix. The registry stays at 20.

## Contract changes

**None.** F8 *consumes* `ZWarden.Contracts` (`AgentHealthStatus`, `ProtocolVersion`); it adds nothing to the wire. The protocol surface is frozen as F7 left it until F9/F10/F16 add their leaves.

## Package changes

Add to central management (`Directory.Packages.props`), exact pins verified against restore (lock file on):

- `Serilog.Extensions.Hosting`
- `Serilog.Settings.Configuration`
- `Serilog.Sinks.Console`
- `Serilog.Sinks.File`
- `Microsoft.Extensions.Options.DataAnnotations` (for `AddOptionsWithValidateOnStart` + DataAnnotations validation), if not already in the shared framework for the Worker SDK.

`ZWarden.Agent.csproj` gains the Serilog `PackageReference`s (no `Version`, per ADR 0002). No package is added to any other runtime project in F8.

## Security considerations

- **The Agent still cannot reach the database (trust-boundaries §9 rule 1).** File-based identity persistence keeps it that way; the existing arch test stays load-bearing and green.
- **No secret is logged, and the redaction discipline is established before F9 needs it.** The config-summary banner renders `ControlPlaneUri` and paths but is structured so F9's credential drops into a **secret-aware type that never stringifies** (F3's primitives), and Serilog's destructuring is configured not to serialise such types. F8 has no secret yet; it sets the pattern.
- **The identity file is self-identity, not a credential.** An attacker reading it learns the Agent's `agt-` id, which is not a secret (it appears on the wire in `AgentHello`). Trust rests on F9's credential, not on the id's confidentiality — stated so a future reader does not over-protect the id or under-protect the F9 credential.
- **Configuration is validated fail-closed at startup.** A malformed `ControlPlaneUri` or a non-absolute path stops the host rather than starting an Agent in an ambiguous state.

## Test plan

Written before the code (PRD 2.2), in `ZWarden.Agent.Tests` unless noted. Offline tier.

1. **Identity — first run generates and persists.** With no identity file, the store generates a valid `AgentId`, writes it, and a re-read returns the *same* id (stability across restart).
2. **Identity — reload is identity-preserving.** A pre-seeded valid file is loaded verbatim; the id is not regenerated. A malformed file is a *typed, actionable* failure (named path + reason), never a silent new identity that would orphan an enrolment.
3. **Identity — the write is atomic and BOM-less.** The persisted bytes have no UTF-8 BOM and are the canonical `agt-<uuid>`; the write goes via a temp file + replace (no torn file observable). (Assert on bytes + that no stray temp file remains.)
4. **Configuration — valid binds, invalid fails at startup.** A valid `AgentOptions` binds; a non-absolute `ControlPlaneUri`, a negative/zero interval, or an empty identity path fails `ValidateOnStart` with a message naming the offending option. Table-driven.
5. **Health state — transitions and reads.** Default is `Healthy`; setting `Degraded`/`Unhealthy` with a reason is observable to a reader; a transition is logged. Enum values come from F7 `AgentHealthStatus` (no Agent-local duplicate enum).
6. **Lifecycle — clean start and graceful stop.** The `AgentWorker` starts, then a `StopAsync`/cancellation drains within `ShutdownTimeout` and completes without throwing; cancellation is observed cooperatively (no `OperationCanceledException` escaping the host).
7. **Diagnostics — banner content.** The startup diagnostic reports the Agent id and `ProtocolVersion.Current`, and the config summary contains **no secret token** (guard by asserting a seeded secret-shaped value never appears — pattern for F9).
8. **Architecture — the Agent takes no transport/persistence dependency yet** (`ZWarden.ArchitectureTests`). Extend the reference-direction guards: the Agent references no SignalR client package and (already covered) no persistence — so F8 cannot accidentally pull F9/F10 forward. The existing `Agent_does_not_reference_persistence` stays green.

## Implementation slices

- **S1 — Serilog bootstrap + host shell + ADR 0021.** Add the Serilog packages; rewrite `Program.cs` to the two-stage pattern (`try/catch/finally`, `Log.CloseAndFlush`), `UseSerilog` from config; register an empty DI surface; write ADR 0021 and add its index row. *Verify:* host builds, starts and stops clean; offline tier green. TDD from test 6's skeleton.
- **S2 — `AgentOptions` + validation.** Bind + `ValidateOnStart` + DataAnnotations/`IValidateOptions`. *Verify:* test 4.
- **S3 — Identity store (file-based, atomic).** `IAgentIdentity` / `IAgentIdentityStore` + the atomic BOM-less writer + load/generate. *Verify:* tests 1, 2, 3.
- **S4 — Health-state model.** `IAgentHealthState` over F7 `AgentHealthStatus`, transition logging. *Verify:* test 5.
- **S5 — `AgentWorker` lifecycle + diagnostics banner.** The `BackgroundService` loop shell, lifetime hooks, graceful drain, the startup/shutdown banner and `IAgentDiagnostics`. *Verify:* tests 6, 7.
- **S6 — Architecture guard + docs.** The transport-dependency guard (test 8); `appsettings.json` documented defaults; confirm `--minimum-expected-tests` floor set on the new test project. *Verify:* full offline tier + arch tests green, no warnings.

## Diagnostics

- **Startup banner is a single structured event** naming the Agent id, version, protocol version and the resolved (secret-free) config — so an operator reading the Agent's first log line knows *which* Agent, *what* it speaks, and *where* it will connect once F10 lands.
- **Config validation failure is actionable** — it names the offending option and the rule it broke, at startup, before any work begins.
- **Identity load failure is typed and named** — a malformed identity file fails with the path and reason, never a silent new identity.
- **Health transitions are logged with a reason** — a move to `Degraded`/`Unhealthy` records *why*, the record F16 later enriches and F10 puts on a heartbeat.

## Documentation

- **ADR 0021 — Serilog as ZWarden's logging stack.** Records the decision, the two-stage bootstrap pattern, the Console + File sink baseline, the configuration-driven levels, and the redaction/destructuring stance (secret-aware types never serialise). States the knowingly-accepted cost: Web's MEL→Serilog migration is a tracked follow-up. Alternatives: default MEL (rejected — the maintainer's preference and F8's diagnostics job favour structured-first); Serilog Agent-only with no ADR (rejected — forks the stack with no recorded rationale).
- **`appsettings.json` / `appsettings.Development.json`** — documented Serilog + `AgentOptions` defaults, with comments marking `ControlPlaneUri` as declared-for-F10.
- **No `CONTEXT.md` change** — F8 introduces no new domain term (identity, health, configuration are runtime mechanics, not glossary vocabulary). If "Agent runtime health" proves worth a term when F16 lands, that is F16's call.

## Acceptance criteria

1. `ZWarden.Agent` runs as a Worker Service: it builds, starts, logs a structured startup banner via **Serilog**, runs an `AgentWorker`, and shuts down **gracefully** within `ShutdownTimeout` on Ctrl-C / `StopAsync`.
2. The Agent has a **stable self-identity**: generated once on first run, persisted **atomically and BOM-less** to a local file, and reloaded unchanged across restarts; a malformed file fails typed rather than minting a new id.
3. `AgentOptions` binds from configuration and **fails closed at startup** on invalid values, naming the offending option.
4. An in-process **health-state model** over F7 `AgentHealthStatus` is settable, readable and logs transitions — no Agent-local duplicate of the enum.
5. The Agent references **no persistence, no SignalR/transport, no Docker** — the reference-direction architecture tests pass and fail the build if F9/F10/F13 dependencies are pulled forward.
6. **ADR 0021** is written and indexed; the always-on offline CI tier is green with no warnings; the new test project's silent-drop floor is set.

## Definition of Done

Per PRD 61, the applicable subset: acceptance criteria met; tests authored first as executable specifications; unit + lifecycle tests pass; **architecture rules pass** (Agent references only Contracts/Rcon and, transitively, Domain — no Infrastructure, EF, provider, SignalR or Docker; Domain stays infra-free); error conditions modelled (config validation and identity-load failures are typed and actionable); diagnostics exist (named startup banner, config-failure, identity-failure and health-transition events); ADR 0021 written and indexed; `appsettings` documented; CI green; no unresolved warnings. (Migrations, DB tests, authorization, audit and UI are **N/A at F8** — a Worker Service skeleton with file-only local state, no transport, no persistence and no user surface. Enrollment, transport and Docker are explicitly later features.)
