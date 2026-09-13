# Feature 18 Mini-Plan — RCON Foundation

**Status:** IN PROGRESS — decisions locked as-recommended (2026-09-13). **PR-A DONE** (PR #98 merged:
`ZWarden.Rcon` client + fake-server harness). **PR-B DONE** (Agent integration: password ownership +
INI config seam, container-IP resolver, `diagnostics.rcon-health` additive contract + probe, the §9
rule-7 architecture test, ADR 0026). **PR-C next** (operator enqueue surface). A real-PZ-container
networked integration test (validating the empty-result quirk against a live server) is deferred to a
follow-up in the opt-in tier. Roadmap issue: [F18 (#39)](https://github.com/MCrank/ZWarden/issues/39).
Track D — the
feature that gives ZWarden a *voice* inside a running Project Zomboid server: a Source RCON client
the Agent owns end to end. **Depends on F12, F14, F16 — all merged.** Unblocks
[F19 (#41)](https://github.com/MCrank/ZWarden/issues/41) — Player Management,
[F28 (#48)](https://github.com/MCrank/ZWarden/issues/48) — Remote Administrative Console, and
[F29 (#49)](https://github.com/MCrank/ZWarden/issues/49) — Diagnostics Engine.

**Format:** PRD 60. **TDD is mandatory** (PRD 2.2). **Written against:**
[`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (F18) and §3 note 5 (the four traps);
[`research/project-zomboid-runtime.md`](../research/project-zomboid-runtime.md) **§7** (RCON dialect,
verified against Build 42.20.2 game code + the archived Valve spec — the load-bearing source);
[`trust-boundaries.md`](../trust-boundaries.md) **§5** (Agent → PZ runtime: the Agent generates and
owns the RCON password, Web never sees it), **§8** (PZ output is untrusted end to end), **§9 test 7**
(the RCON password type is unreachable from `ZWarden.Web`); PRD §§29–30 and Feature 18; ADR
[0008](../adr/0008-docker-socket-access-via-wollomatic-socket-proxy.md) (the allowlist — RCON is a
private network hop, **not** a socket verb), [0020](../adr/0020-agent-protocol-versioning-and-catalogue.md)
(additive-vs-breaking), [0021](../adr/0021-serilog-is-the-logging-stack.md), [0023](../adr/0023-server-health-model-and-observed-delivery.md)
(the closed four-probe health shape). New **ADR 0026** lands with the feature.

## Objective

Give the Agent a correct, hardened, **Agent-owned** Source RCON client (`ZWarden.Rcon`) that can
authenticate to a running PZ server over the **private container network**, execute an admin command,
and read its reply — surviving the four measured protocol traps that make a naïve Source RCON client
hang or misframe against PZ. Ship one **RCON health probe** proving the path end to end. The RCON
password is **generated and owned by the Agent and never reaches `ZWarden.Web`** (PRD 29, un-hedged
by `trust-boundaries.md` §5) — true by construction, enforced by a PRD-15 architecture test.

## The five load-bearing decisions (LOCKED as-recommended, 2026-09-13)

- **D-1 — End-of-response detection is short-chunk + read-until-idle, bounded by an overall
  timeout; never Valve's terminator, never the Koraktor sentinel.** PZ *never emits a
  end-of-response sentinel* (research §7 quirk 2) and *mishandles the Koraktor multi-packet probe*
  (quirk 4), so those two standard tricks are off the table. The client detects the last chunk by
  its body being **shorter than 4086 bytes** (42.20.2 splits at 4086 — quirk 1), and backstops that
  with a **read-until-idle window** (no bytes for *N* ms after the command was accepted ⇒ response
  complete) plus a hard per-command **timeout**. **This is also how trap 2 is survived:** an *empty*
  result sends **no packet at all** (quirk 3), so "nothing arrived within the idle window" is a
  **valid empty result, not a hang and not a fault**. Proposed defaults: idle window **250 ms**,
  per-command timeout **5 s** (each command costs ≥1 tick + up to 50 ms — quirk 8), connect timeout
  **3 s**. *Recommend confirm the idle-window default; it is the one value worth tuning against a
  live server (quirk 3 is Medium-confidence — a direct read of the loop condition, not a capture).*
- **D-2 — The Agent sets the RCON password by a host-side surgical write into `servertest.ini` at
  provision — not via a container env var.** The container scripts set **no** RCON config today
  (`pz-lib.sh`), and an empty `RCONPassword` *silently disables RCON* (research §7). An env var
  (`ZW_PZ_RCON_PASSWORD`) is simpler for first-run bootstrap but **exposes the secret in
  `docker inspect`** (`Config.Env`, which the allowlist permits reading). A host-side surgical write
  keeps the secret out of the container's inspectable env and matches `trust-boundaries.md` §5
  ("surgical writes under the canonical `/pz/` layout") and F17's DataMountRoot ownership precedent.
  The Agent writes `RCONPort=27015` + `RCONPassword=<generated>` into
  `DataMountRoot/<serverId>/data/Server/servertest.ini` **before first launch** (PZ reads an existing
  INI and fills only missing keys, so pre-seeded values survive). **Cost:** reopens F14's
  `ProvisionAsync`; servers provisioned before F18 gain RCON only on re-provision (acceptable pre-1.0,
  as with F17's mounts) — a start-path *ensure* covers them. *Recommend host-side write.*
- **D-3 — One long-lived, pooled TCP connection per server, with a serialized command queue, shared
  by operator commands and the health probe.** PZ caps **5 simultaneous connections** — the 6th is
  *accepted then instantly EOF'd* (quirk 7) — and **serializes every command on the main tick with no
  pipelining** (quirk 8). A single authenticated socket per server (PZ keeps it alive — quirk 5),
  guarded by a `SemaphoreSlim` so commands run one at a time, respects both traps and avoids leaking a
  cap slot (a dead socket holds its slot until the next `accept()`). Reconnect is lazy on the next
  command after a fault, with bounded backoff. *Recommend confirm.*
- **D-4 — RCON health is an on-demand diagnostics command (`diagnostics.rcon-health`), not a fifth
  standing probe in the closed `HealthBreakdown` tuple.** ADR 0023's `HealthBreakdown` is a *closed*
  four-probe shape (Container/Process/Startup/Network); adding a fifth would bump `ProtocolVersion`
  and make the health monitor poll RCON continuously — competing with operators for the 5-slot cap and
  the tick queue. Instead, mirror F13's `ProbeDockerHealth`: a payload-free `AgentCommand` leaf +
  a dispatch arm + an **additive** optional `RconHealthResult` on `OperationCompleted`
  (`ProtocolVersion` stays **1**). Continuous rollup integration is deferred to F29. *Recommend
  confirm.*
- **D-5 — `servertest.ini` is the single source of truth for the password; no parallel Agent secret
  store.** `trust-boundaries.md` §7 enumerates what the Agent's local store may hold (identity,
  credential, assignment, operation bookkeeping) — the RCON password is **not** in that list, and §5
  explicitly says an operator "can always read or reset it in the config file." So the Agent
  **generates the password once at provision** (idempotent — never regenerated on restart; PZ captures
  it `final` at init, quirk in §7, so a mid-run change is inert anyway), writes it to the INI, and
  **re-reads it from the INI** to connect. One source, no drift. The RCON credential *type* still
  lives in `ZWarden.Rcon` (Agent-only assembly) to satisfy §9 test 7. *Recommend INI-as-source-of-truth.*

## Scope, by PR

### PR-A — `ZWarden.Rcon` client library + fake-server harness (branch `feat/f18-rcon-client`)

The pure Source-RCON client, written red-green against a fake PZ RCON server that *is* the executable
spec of research §7. **No dependency on `ZWarden.Agent` or `ZWarden.Contracts`.**

1. **Packet codec** — Source RCON framing (4-byte little-endian size excluding itself; `id` (4),
   `type` (4), NUL-terminated body, trailing empty string = two NULs; max packet 4096). Encode/decode
   with **hardened bounds**: reject a length that is negative, `> 4096`, or `< 10`; read the length
   prefix and body in **loops** (never assume one `read` fills them); cap total accumulated response
   bytes. Constants `SERVERDATA_AUTH=3`, `SERVERDATA_AUTH_RESPONSE=2`, `SERVERDATA_EXECCOMMAND=2`,
   `SERVERDATA_RESPONSE_VALUE=0`.
2. **`RconConnection`** (one per server, `IAsyncDisposable`): connect → `SERVERDATA_AUTH` → expect an
   empty `RESPONSE_VALUE` then `AUTH_RESPONSE` with a **matching id** (mismatched/`-1` id ⇒
   `RconAuthenticationException`, and PZ then closes the socket — fail fast, do **not** retry-hammer
   the cap); `ExecuteAsync(command)` → `EXECCOMMAND` → reassemble `type-0` chunks until a chunk
   `< 4086 B` **or** the idle window elapses (D-1); overall timeout (D-1); **empty result ⇒ empty
   string, not an error** (trap 2); a `SemaphoreSlim(1,1)` serializes commands (trap/quirk 8);
   reconnect-on-fault with bounded backoff; clean close so no cap slot leaks (trap 7).
3. **Types (Agent-only, live here for §9 test 7):** `RconEndpoint(string Host, int Port,
   SecretString Password)`, `RconOptions(TimeSpan IdleWindow, CommandTimeout, ConnectTimeout,
   MaxResponseBytes)`, `RconAuthenticationException`, `RconTimeoutException`. Password carried as
   `SecretString` (from `ZWarden.Domain.Security`) so it never stringifies through logs. Command
   *construction/quoting* is **out of scope** (F19/F28) — the client sends raw command text.
4. **`FakeRconServer`** (test harness, `TcpListener`): reproduces every trap — no terminator; empty
   command ⇒ **no packet**; responses chunked at 4086 B sharing one id/type; **5-connection cap**
   (accept-then-close the 6th); auth failure ⇒ `id = -1` then close; a configurable **tick delay**
   before each response; UTF-8 response bodies. This harness is a deliverable — it is the spec.
5. **New `ZWarden.Rcon.Tests`** (TUnit, mirror `ZWarden.Agent.Tests.csproj`, set
   `--minimum-expected-tests` floor): codec round-trip + bounds; auth success/`-1`-failure; single-
   and multi-chunk responses; **short-chunk vs idle-window** end detection; **empty result ⇒ empty
   string within the idle window** (no hang); command serialization; reconnect after EOF; 5th-vs-6th
   connection behavior; hostile-framing rejection (negative/oversized length).

### PR-B — Agent integration: password ownership, config seam, health probe, contracts (branch `feat/f18-agent-rcon`)

1. **Password ownership + config seam (D-2, D-5):** at `ProvisionAsync`
   (`AgentCommandProcessor`/F14), generate a strong INI-safe password (CSPRNG, constrained alphabet —
   no newline/`=`/shell metacharacters, §8 config-write path is an F40 attack target) **iff absent**,
   and surgically write `RCONPort=27015` + `RCONPassword=` into
   `DataMountRoot/<serverId>/data/Server/servertest.ini` (host-side, no `exec`). A start-path *ensure*
   covers pre-F18 servers. **Never set `-Drconlo`** (it binds RCON to container loopback and would
   lock the Agent out of the bridge — research §7). New `IRconConfigWriter`/`RconIniConfigWriter`
   (pure, unit-tested: seeds keys, preserves other keys, idempotent, INI-safe value).
2. **Reachability (`IRconEndpointResolver`):** resolve the container's IP on the `zwarden` network via
   the **already-allowlisted** `GET /containers/{id}/json` (inspect — no new verb, ADR 0008
   untouched) → `RconEndpoint(<ip>, 27015, <password read from the INI>)`.
3. **Contracts (all additive — ADR 0020, `ProtocolVersion.Current` stays 1):**
   `RconHealthProbe : AgentCommand` `[ProtocolMessage("diagnostics.rcon-health")]` (payload-free,
   auto-registered — copy `ProbeDockerHealth`); optional `RconHealthResult(bool Reachable, bool
   Authenticated, string? Detail)` on `OperationCompleted` (nullable ⇒ additive, mirrors
   `UpdateResult`/`ProvisionResult`). Assert additivity + closed-vocabulary in
   `ClosedCommandVocabularyTests`/`ProtocolCompatibilityTests`.
4. **Dispatch:** `AgentCommandProcessor` `case RconHealthProbe` (dedupe by `OperationId`) → new
   `IRconHealthProbe` (resolve endpoint → connect → auth → a cheap liveness command → classify) →
   `Completed(..., RconHealthResult)`. The probe **distinguishes** healthy / RCON-disabled (empty
   password) / refused-or-cap / auth-mismatch / timeout, each with a legible `Detail`. Unit-tested via
   the `FakeRconServer` (following `AgentCommandProcessorTests` + `FakeContainerRuntime`).
5. **DI wiring** in `HostingExtensions` (`IRconConfigWriter`, `IRconEndpointResolver`,
   `IRconHealthProbe`, an `RconConnection` factory/pool keyed by server).
6. **Architecture test (PRD 15 · `trust-boundaries.md` §9 test 7):** assert `ZWarden.Web`'s reference
   graph does **not** include `ZWarden.Rcon` (nor the RCON password type) — fails the build if
   violated. Add to the existing architecture-tests suite.
7. **ADR 0026** — RCON foundation: private-network transport (never host-published, quirk-aware),
   Agent-owned INI-sourced credential, the four traps + single-connection discipline, on-demand health
   probe as an additive command.

### PR-C — Operator enqueue seam + permission + minimal surface (closes #39; branch `feat/f18-rcon-probe-surface`)

Just enough operator-facing wiring to demonstrate criterion 9 ("securely use RCON") end to end **without
pre-empting the F28 console**.

1. **Application service `IRconHealthCheck`/`RconHealthCheck`** (copy `ServerLifecycle`): resolve
   Server through the tenant filter → authorize a server-scoped permission → `EnqueueAsync(...
   OperationKind.RconHealthProbe ...)` → audit → `ServerBusyException` ⇒ `ServerBusy`.
2. **`OperationKind.RconHealthProbe`** (append; stored by name) + `OperationDispatcher.CommandFor`
   case → `RconHealthProbe` command (covered by `OperationDispatcherMapTests`).
3. **Authorization:** `Permissions.ServerDiagnostics` (server-scopable, `Server.*` pattern) added to
   the closed catalogue + `All`; `BuiltInRoles` grants; extend `PermissionCatalogueTests`.
4. **Audit:** automatic `Operation.*` via the engine; a `ServerAuditActions` intent record.
5. **Minimal UI:** an enqueue endpoint + a "Check RCON" action on the server-detail view surfacing the
   `RconHealthResult` (reachable / authenticated / detail) off the existing operation surface — Bb
   components, SSR patterns per [[blueprint-seam-on-ssr-forms]]. Per-PR chore: if any Web.Tests count
   changes, **bump the floor in BOTH the csproj and `ci.yml` `tier1-silent-drop-guard`**
   ([[web-tests-discovery-floor-bump]]); `npm run build:css` + commit `wwwroot/app.css` if styles change.

## Non-scope

- **The remote administrative console / arbitrary command surface** — F28. F18 sends raw command text
  through the client; it ships no operator command box.
- **Command construction, argument quoting, player/admin command semantics** — F19/F28. (Research §7
  quirk 10: quoting is mandatory — `servermsg "My Message"`; the caller owns it.)
- **Continuous RCON health in the standing `HealthBreakdown` rollup** — kept on-demand (D-4); a later
  feature (F29) may fold it into continuous observation.
- **General `servertest.ini` configuration management** — F20. F18 writes only the two RCON keys.
- **Runtime password rotation** — PZ captures `RCONServer.password` as `final` at init (research §7);
  a change needs a restart, out of scope here.
- **The Koraktor multi-packet sentinel** — unusable against PZ (quirk 4); never attempted.
- **Non-ASCII command arguments** — PZ decodes request bodies with the platform default charset
  (quirk 11); F18 restricts sent commands to ASCII and documents the risk.
- **RCON over the public internet** — RCON is private on the container network, never host-published.

## Domain / contract / persistence changes

- **Domain:** `OperationKind.RconHealthProbe` (append); `Permissions.ServerDiagnostics` (closed
  catalogue). No new entity.
- **Contracts (additive, no version bump):** `RconHealthProbe` command; optional `RconHealthResult`
  on `OperationCompleted`. Assert `ProtocolVersion.Current == 1` holds.
- **Persistence:** **none** — `RconHealthProbe` is a new enum value in the existing Operations string
  column; no migration. (The password lives in the INI on the bind mount, not the DB.)

## Test plan (TDD, per PR)

- **PR-A (`ZWarden.Rcon.Tests`, offline, `FakeRconServer`):** every trap and framing case above,
  written **before** the client. The fake server is authored first as the executable spec.
- **PR-B:** `RconIniConfigWriter` (seed, preserve, idempotent, INI-safe, empty-password rejected);
  `IRconEndpointResolver` (IP from a fake inspect payload); `AgentCommandProcessor` RCON arm against
  `FakeRconServer` (healthy / disabled / refused / auth-mismatch / timeout, dedupe on redelivery);
  Contracts serialization + additivity + closed-vocabulary; the **architecture test** (Web ⊅
  `ZWarden.Rcon`).
- **PR-C:** `RconHealthCheck` (not-found/foreign ⇒ ServerNotFound, unauthorized, ServerBusy, success
  audits); `OperationDispatcherMapTests`; `PermissionCatalogueTests`; enqueue-endpoint +
  server-detail render tests (bUnit, loose JSInterop).
- **Integration tier (`ZWarden.IntegrationTests`, `[Category("Networked")]`):** a real PZ container
  (Testcontainers, `zwarden` network) with a seeded RCON password — connect, authenticate, run a
  command, read the reply, and confirm an empty-result command returns empty (not a hang). This is the
  one place trap 2's Medium-confidence read is validated against a live server.

## Diagnostics

- **RCON failure is legible, not a hang:** the probe reports *why* — disabled (empty password),
  refused-or-cap-exhausted, auth-mismatch (`id = -1`), or timeout — each a distinct `Detail`.
- **"No response" is correctly an empty result**, surfaced as empty, never as a fault or a stall
  (trap 2) — the single most consequential quirk for a control plane.
- **Cap-slot safety:** connections are pooled and cleanly closed; a fault reconnects lazily without
  leaking one of PZ's five slots (trap 7).
- All RCON output is logged as **untrusted data** (§8) — never interpolated, escaped only at render.

## Documentation

- `src/ZWarden.PZServer/README.md` — the RCON `27015/tcp`-private note updated to record that the
  **Agent** seeds `RCONPort`/`RCONPassword` into `servertest.ini` (host-side) and owns the credential.
- `docs/pzserver-architecture.md` — flip the "RCON console F18/F28 — not built" row to reflect the
  F18 foundation; note the private-network transport and Agent-owned credential.
- **ADR 0026** as above. Note in ADR 0008's consequences that F18 reaches RCON as a private
  **network** hop (no new socket verb; inspect is already allowlisted).
- `trust-boundaries.md` §9 test 7 is now a realized, enforced architecture test.

## Acceptance criteria

1. `ZWarden.Rcon` connects over the **private container network** to `:27015`, authenticates
   (type 3 → 2, id match), executes a command (type 2 → type-0 chunks), and reads the reply — **all
   four traps handled**: no-terminator (short-chunk + idle window), empty-result (empty string, no
   hang), 5-connection cap (single pooled connection, clean close), tick-serialization (serialized
   queue, timeout accounts for the tick).
2. The Agent **generates and owns** the RCON password (INI-sourced on its own bind mount);
   `ZWarden.Web` never holds it, and the **architecture test fails the build** if Web's graph
   references `ZWarden.Rcon` or the RCON password type (`trust-boundaries.md` §9 test 7).
3. The **RCON health probe** reports reachable/authenticated with a legible detail, dispatched like
   `ProbeDockerHealth`, on an **additive** contract (`ProtocolVersion.Current == 1`).
4. Timeout, empty-result, and reconnect are exercised; a lost socket recovers on the next command
   **without leaking a cap slot**.
5. All RCON output is treated as **untrusted** (bounded framing, no interpolation, escape at render —
   §8).
6. Offline unit tier (all traps, via `FakeRconServer`) is green; the **networked** integration tier
   (real PZ container over `zwarden`) proves the path and validates the empty-result read.
7. **ADR 0026** written; README + architecture doc updated.

## Definition of Done

Per PRD 61: acceptance criteria met; tests authored first (TUnit unit + networked integration); the
RCON password never crosses to Web (enforced by the architecture test) and never stringifies through
logs (`SecretString`); fail-closed server-scoped authorization + tenant isolation on the operator
surface; audit on enqueue/terminal; diagnostics (legible failure classes, empty-≠-hang, cap-slot
safety) exist; failure modes modelled (disabled, refused, cap-exhausted, auth-mismatch, timeout,
empty, hostile framing); no persistence migration required; protocol changes additive
(`ProtocolVersion.Current == 1`); ADR 0026 written; docs updated; CI green (offline tier + the
networked integration tier exercising a real RCON round-trip); `trust-boundaries.md` §5/§8 reviewed
(all RCON I/O is untrusted; the config-write path is INI-safe).
