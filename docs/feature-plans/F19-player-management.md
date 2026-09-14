# Feature 19 Mini-Plan — Player Management

**Status:** COMPLETE (2026-09-14) — decisions locked as-recommended (2026-09-13). **PR-A DONE** (PR #101:
contracts + Agent quoting/parsing/execution core). **PR-B DONE** (PR #102: `OperationKind`s +
`Operation.CommandPayload` + `PlayerManagement` service + ban registry/ADR 0027 + audit + endpoints). **PR-C
DONE** (enumeration via the ownership-guarded roster cache + `IBanQuery` + the server-detail Players UI). A
real-PZ networked integration test for the live prose-parse is deferred to the opt-in tier (as in F18); the
rich inline console render is F28. Roadmap issue:
[F19 (#41)](https://github.com/MCrank/ZWarden/issues/41). Track D — the feature that lets an operator
**see who is on a server and act on them** (enumerate, kick, ban, unban, remove from the whitelist, and
close/open the server to non-whitelisted players) without SSH, through the RCON voice F18 gave the Agent.
**Depends on F6 (audit) and F18 (RCON) — both merged (#28, #39).** Blocks
[F28 (#48)](https://github.com/MCrank/ZWarden/issues/48) (Remote Administrative Console reuses the
command-quoting + untrusted-output-parsing surface F19 introduces) and
[F53/F55](https://github.com/MCrank/ZWarden/issues/41) per the issue's native dependency edges.

**Format:** PRD 60. **TDD is mandatory** (PRD 2.2). **Written against:**
[`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (F19, rescoped) and §11 criterion 8
("administer players"); [`research/project-zomboid-runtime.md`](../research/project-zomboid-runtime.md)
**§7** (the RCON admin-command surface + response strings, verified against Build 42.20.2 game code — the
load-bearing source, esp. quirk 10 argument quoting, quirk 13 `players` is log-excluded, and the operation
matrix); [`trust-boundaries.md`](../trust-boundaries.md) **§5** (the Agent owns the RCON credential; Web
never sees it) and **§8** (everything PZ emits over RCON is untrusted end to end); ADR
[0012](../adr/0012-whitelist-addition-is-out-of-scope.md) (the whitelist rescope this feature implements),
[0018](../adr/0018-zwarden-owned-rbac.md) (server-scoped authorization, fail-closed),
[0019](../adr/0019-audit-is-append-only-tenant-owned-and-binds-the-auth-sink.md) (audit),
[0020](../adr/0020-agent-protocol-versioning-and-catalogue.md) (additive-vs-breaking),
[0022](../adr/0022-operation-lifecycle-and-per-server-locking.md) (the operation lifecycle + per-server
lock), [0026](../adr/0026-rcon-foundation-private-transport-and-agent-owned-credential.md) (RCON foundation
— **which explicitly defers command construction and quoting to F19**). New **ADR 0027** lands with the
feature (the ban-registry boundary).

## Objective

Give operators a fail-closed, audited **player-management surface** over the F18 RCON path. Enumerate the
live roster; kick; ban and unban by account username; remove a user from the whitelist; and toggle the
whitelist **mode** (`Open`). F19 owns the two things ADR 0026 deferred: **admin-command construction with
mandatory quoting**, and **parsing PZ's untrusted responses** into typed results. It slots into the
existing backbone (`ServerLifecycle` + `RconHealthProbe`) end to end — new operation kinds, additive
contracts, a per-feature audit-action class, an application service, and a Blazor surface — reusing the
**already-declared** `Player.*` permissions and `ply-`/`ban-` typed IDs. It persists a **ban registry** so
the UI can list bans and unban from that list, while being honest (ADR 0027) that the registry records the
bans **ZWarden issued**, not a mirror of PZ's authoritative list.

## The load-bearing decisions (LOCKED as-recommended, 2026-09-13)

- **D-1 — Enumeration is an on-demand poll of `players`; there are no player *events*. `[LOCKED by the
  shipped build]`** The issue asks for "connect/disconnect events where available"; measured against
  Build 42.20.2 they are **not** available — `connections` is `@DisabledCommand` and no `disconnect`
  command exists (research §7; ADR 0012 consequences). So the reserved **"player events → F19"** slot on
  `AgentEvent` **retires for v1.0**: no `AgentEvent` is added. The roster is fetched on demand via
  `players` (quirk 13: `players` is excluded from RCON debug logging, so it can be polled without flooding
  the server log) and returned as an **additive optional result record on `OperationCompleted`**, exactly
  as F18 returns `RconHealthResult`. A live push/roster subscription is out of scope (revisit if a future
  build re-enables connection events).
- **D-2 — Player operations are *non-mutating*, server-scoped — they do not take the per-server lock.
  `[recommend]`** Per ADR 0022 the per-server lock is `Operation.IsMutating`, set at enqueue, **not**
  derived from the kind. Kick/ban/unban/remove/mode are RCON passthroughs already serialized by
  `ZWarden.Rcon`'s single-socket `SemaphoreSlim` gate (ADR 0026 D-3), so the DB per-server lock buys
  nothing — and making them mutating would make a kick collide with an in-flight `RestartServer` and fail
  `ServerBusy`, which is the wrong behaviour (you may well want to kick or close the server *during* other
  activity). They therefore mirror `RconHealthProbe` (`IsMutating: false`, server-scoped). Reversible if a
  future action genuinely needs exclusivity.
- **D-3 — Command construction, argument quoting, and input validation are F19's security core, and live
  Agent-side. `[LOCKED]`** ADR 0026 forbids this in `ZWarden.Rcon` (it sends raw text). A new
  `PlayerCommandBuilder` (in `ZWarden.Agent`, next to the RCON integration) builds each command with
  **mandatory quoting** (quirk 10: PZ's tokenizer `([^\"]\S*|\".*?\")` strips all `"` and splits on
  whitespace, so an unquoted multi-word argument silently reparses). **Input is validated on both sides**
  — at the Web edge (fast operator feedback) and again at the Agent (defence in depth, since a command is
  assembled there): a **username** is rejected if it contains a `"`, whitespace, a newline/control
  character, a leading `-` (would read as a flag), or exceeds a length bound; a **reason** is quoted,
  stripped of `"`/newlines, and length-bounded. Commands are ASCII-only (quirk 11: PZ decodes request
  bodies with the platform default charset). Every response is **untrusted** (§8): the parser matches only
  PZ's known-stable strings and never interprets prose as anything but display text (escaping at render is
  F28). This is F18's "four traps" analogue and gets the same treatment — a dedicated injection/parse test
  suite written first.
- **D-4 — Persist a `BanRecord` registry of bans ZWarden issued; do *not* persist a player roster.
  `[recommend, confirmed 2026-09-13]`** PZ ships **no "list bans" RCON command** (research §7 operation
  matrix), so without our own record the ban list is invisible in the UI and unban is a blind username
  text box. F19 therefore persists a `BanRecord` (`ban-`, `ITenantOwned`, server-scoped) recorded **at enqueue
  from the operator's authorized intent** — the ingest path carries no acting user and persists no per-action
  result (it drops F18's `RconHealthResult` the same way), so reconciling from the completion would be real
  plumbing for a record that is advisory regardless. **ADR 0027** records the honest boundary: the registry is
  "bans ZWarden **issued**", **not** a mirror of PZ's authoritative user store (a ban issued in-game or from
  the console will not appear, and a ban ZWarden issued may not have taken effect) — a bounded drift in the same
  family as ADR 0011/0012. The **Operation and audit trail are authoritative** for whether the RCON command
  actually succeeded; the registry states intent. The **roster is not persisted** — "enumeration" is the live
  poll (D-1); the reserved `ply-` `PlayerRecordId` stays reserved for a later seen-players/history feature.
  Scope of ban targets is **account username only** (`banuser`/`unbanuser`), matching the usernames the
  `players` roster yields; Steam-ID (`banid`) and IP (`banip`) bans are out of scope (they need an input
  the roster does not provide).
- **D-5 — Reuse the existing permission catalogue; no new permissions. `[LOCKED]`** The `Player.*` family
  already exists (`Player.View/Kick/Ban/Unban`, all `ServerScopable`). F19 maps: enumerate → `Player.View`;
  kick → `Player.Kick`; ban → `Player.Ban`; unban → `Player.Unban`; **remove-from-whitelist → `Player.Ban`**
  (it is persistent access denial); **whitelist-mode toggle → `Server.Configuration.Edit`** (it writes the
  `Open` server-config key). No catalogue/`All`/`BuiltInRoles` change and **no ADR for authorization** —
  the closed-catalogue test stays green untouched.

## Command surface (from research §7, verified against Build 42.20.2)

| Action | RCON command (quoted per quirk 10) | OperationKind | Permission | Parse |
| --- | --- | --- | --- | --- |
| Enumerate | `players` | `ListPlayers` | `Player.View` | **Structured** — `Players connected (N): ` then `-<username>` lines, `\n`-separated, trailing separator |
| Kick | `kickuser "<u>" -r "<reason>"` | `KickPlayer` | `Player.Kick` | **Stable strings** — `User X kicked.` / `User X doesn't exist.` / `This user can't be kicked.` |
| Ban | `banuser "<u>" -r "<reason>"` | `BanPlayer` | `Player.Ban` | Prose → coarse outcome + raw detail |
| Unban | `unbanuser "<u>"` | `UnbanPlayer` | `Player.Unban` | Prose → coarse outcome + raw detail |
| Remove from whitelist | `removeuserfromwhitelist "<u>"` | `RemoveFromWhitelist` | `Player.Ban` | Prose → coarse outcome + raw detail |
| Whitelist mode | `changeoption Open <true\|false>` | `SetWhitelistMode` | `Server.Configuration.Edit` | `Option : Open is now : <v>` |

All six are `IsMutating: false`, server-scoped (D-2). Command *names* match case-insensitively; **argument
values do not** (research §7) — usernames are echoed verbatim into the command, which is exactly why D-3's
validation is load-bearing.

## Scope, by PR

### PR-A — Contracts + Agent execution core (branch `feat/f19-player-contracts-agent`)

The typed command surface and the Agent-side builder/parser, written red-green against `FakeRconServer`
(the F18 harness that *is* the executable spec of research §7). The security core of the feature.

1. **Contracts (all additive — ADR 0020, `ProtocolVersion.Current` stays 1):** six `sealed record :
   AgentCommand`, each `[ProtocolMessage("players.<name>")]`, auto-registered by `ProtocolJson` reflection
   (copy `ProbeRconHealth`/`StartServer`): `ListPlayers` (payload-free), `KickPlayer(string Username,
   string? Reason)`, `BanPlayer(string Username, string? Reason)`, `UnbanPlayer(string Username)`,
   `RemoveFromWhitelist(string Username)`, `SetWhitelistMode(bool Open)`. The target Server rides the
   envelope `ServerId`; every command carries `OperationId` for Agent-side idempotency. Two **additive
   optional** result records on `OperationCompleted` (nullable ⇒ additive, mirroring `RconHealthResult`):
   `PlayerRosterResult(int Count, IReadOnlyList<string> Players)` and
   `PlayerActionResult(PlayerActionOutcome Outcome, string? Detail)` with
   `PlayerActionOutcome { Applied, NotFound, Rejected, Unknown }`. Assert additivity + closed-vocabulary in
   `ClosedCommandVocabularyTests`/`ProtocolCompatibilityTests` (the free-form/shell-command guard must stay
   green — these are typed leaves, not a command box).
2. **`PlayerCommandBuilder`** (`ZWarden.Agent`, pure, unit-tested): builds each command string with
   mandatory quoting and the D-3 validation. Rejects an invalid username/reason **before** a byte reaches
   RCON, with a legible reason. ASCII-only. No dependency on the socket — it is a string function, tested
   exhaustively against injection payloads.
3. **`PlayerResponseParser`** (`ZWarden.Agent`, pure, unit-tested): `players` → `PlayerRosterResult`
   (count + usernames, tolerant of the trailing separator, bounded); kick's three stable strings →
   `PlayerActionOutcome`; ban/unban/remove prose → `Applied`/`Unknown` + raw (untrusted, un-interpreted)
   detail; `changeoption Open` → confirm the echoed value. Never interprets response text as a command;
   caps parsed size.
4. **Agent dispatch:** six `case` arms in `AgentCommandProcessor.ProcessAsync` (dedupe by `OperationId`),
   each resolving the endpoint via the existing `IRconEndpointResolver` + `RconConnectionFactory`, sending
   the built command over one `IRconConnection.ExecuteAsync`, parsing the reply, and returning
   `OperationCompleted` carrying the appropriate result record. Follow the `LifecycleAsync` helper shape
   (server target from `envelope.ServerId`). An RCON-disabled/unreachable server maps to a failed
   completion with a legible reason (reusing F18's classification), never a hang (trap 2).
5. **`ZWarden.Agent.Tests` / `ZWarden.Contracts.Tests`:** the injection suite (usernames with quotes,
   spaces, newlines, leading `-`, over-length; reasons with quotes/newlines) written **first**; parser
   cases for every response string above incl. an empty roster and a malformed line; dispatch arms against
   `FakeRconServer` (applied / not-found / rejected / rcon-disabled / timeout, dedupe on redelivery);
   contract serialization + additivity + closed-vocabulary. Bump the affected `--minimum-expected-tests`
   floors.

### PR-B — Application service, operation wiring, ban registry, audit (branch `feat/f19-player-operations`)

The control-plane half: enqueue + authorize + audit, mirroring `ServerLifecycle`, plus the `BanRecord`
persistence and its completion-reconciliation seam.

1. **`OperationKind`** — append `ListPlayers`, `KickPlayer`, `BanPlayer`, `UnbanPlayer`,
   `RemoveFromWhitelist`, `SetWhitelistMode` (stored by name; doc-comment each `IsMutating: false`,
   server-scoped). Add each to `OperationDispatcher.CommandFor` → the matching `AgentCommand` (covered by
   `OperationDispatcherMapTests`).
2. **`IPlayerManagement`/`PlayerManagement`** (copy `ServerLifecycle.RunAsync`): resolve the Server through
   the tenant filter (→ `ServerNotFound`), fail-closed authorize the mapped `Player.*`/`Server.Configuration.Edit`
   permission **server-scoped inside the service** (F14-D3 pattern; → `NotAuthorized`), validate the
   username/reason (D-3, edge copy), `EnqueueAsync(... IsMutating: false, ServerId ...)`, and audit. One
   method per action for the five actions (kick/ban/unban/remove/mode), all **audited**. Enumeration
   (`Player.View`, a read — not audited) lands in **PR-C** with the roster result-surfacing and UI (the
   `ListPlayers` kind + `CommandFor` mapping ship here so the dispatch layer is complete).
3. **Ban registry (D-4, ADR 0027):** `BanRecord` domain entity (`ban-`, `ITenantOwned`: `Id, TenantId,
   ServerId, Username, Reason?, IssuedByUserId, IssuedAt, Status{Active|Lifted}, LiftedByUserId?, LiftedAt?`) +
   `BanRecordConfiguration` (auto-discovered; unique filtered index on `(TenantId, ServerId, Username)
   WHERE Status='Active'`) + a `BanRecordRepository` (`FindActiveAsync`/`ListForServerAsync`). **Dual-provider
   migration** in **both** `ZWarden.Migrations.Postgres` and `.Sqlite` (the `_AddOperations` pair is the
   template) — this same migration adds the `Operations.CommandPayload` column that carries a player command's
   username/reason/flag to the dispatcher (existing kinds are payload-free). The record is written/lifted **at
   enqueue from the operator's authorized intent** in `PlayerManagement` (the ingest path has no acting user or
   per-action result to reconcile from cheaply, and the registry is advisory regardless — ADR 0027); the
   Operation + audit trail are authoritative for effect. The `IBanQuery` read model for listing is PR-C, with
   the UI that renders it.
4. **Audit:** new `PlayerAuditActions` constants (`Player.Kicked`, `Player.Banned`, `Player.Unbanned`,
   `Player.RemovedFromWhitelist`, `Player.WhitelistModeChanged`) written on `Succeeded`/`Failed`/`Denied`
   via `IAuditWriter`, plus the automatic `Operation.*` trail from the engine.
5. **Endpoints + DI:** `/api/servers/{id}/players` (list) and action endpoints under it, authenticated at
   the edge with the fail-closed server-scoped check in the service (F14-D3); `AddZWardenPlayers()` +
   `MapPlayerEndpoints()` registered in `Program.cs` and a `PlayersServiceCollectionExtensions`.
   Not-found/foreign → 404, unauthorized → 403, invalid input → 400.

### PR-C — enumeration + Blazor player-management surface (closes #41; branch `feat/f19-player-ui`)

Operator-facing UI on the **server-detail** page (`/servers/{id}`), **Bb** components, SSR patterns per
[[blueprint-seam-on-ssr-forms]]. **Enumeration is surfaced via an in-memory roster cache, not an operation
result** — the roster is transient display data exactly like F16's metrics/health, so it rides the same
pattern rather than a new `Operation.ResultPayload` column + store/hub/endpoint churn:

1. **Enumeration:** `IPlayerManagement.ListPlayersAsync` (authorize `Player.View`, enqueue `ListPlayers`,
   **not audited** — a read); `IPlayerRosterCache`/`PlayerRosterCache` (in-process, latest-per-Server,
   **ownership-guarded** by the reporting Agent, §8), which `AgentHub.OperationCompleted` records from the
   completion's `Roster`; a `POST /players/refresh` endpoint. A **`LivePlayerRosterPanel`** interactive island
   (mirrors `LiveServerPanel`) reads the cache live and renders count + usernames (render-escaped, §8); a
   **Refresh** SSR button enqueues an enumeration.
2. **Player actions:** one SSR `EditForm` (username + optional reason) with **Kick** / **Ban** / **Unban** /
   **Remove from whitelist** buttons, each gated on its permission; the service is the fail-closed re-check.
3. **Bans panel:** `IBanQuery`/`BanView` lists the Server's `BanRecord`s with a per-row **Unban** (gated
   `Player.Unban`) and an explicit note that the list is bans issued via ZWarden, not PZ's set (ADR 0027).
4. **Whitelist-mode** toggle (`Open` on/off) gated `Server.Configuration.Edit`.
5. Action outcomes surface as an operator message pointing at the enqueued operation (the rich inline console
   render is **F28**); the bans list re-loads after each action so a just-issued/lifted ban shows immediately.
6. Per-PR chore: bump the Web.Tests floor in **both** the csproj and `ci.yml`'s `tier1-silent-drop-guard`
   ([[web-tests-discovery-floor-bump]]); `npm run build:css` + commit `wwwroot/app.css` (the new `border-input`
   utility). bUnit island render tests + real-host page/POST integration tests.

## Non-scope

- **Whitelist *addition*** — `addusertowhitelist`/`addalltowhitelist` are `@DisabledCommand` and `adduser`
  mints PZ credentials; **ADR 0012**, criterion 8 met without it. No affordance is shipped.
- **Steam-ID and IP bans** (`banid`/`banip`/`unbanid`/`unbanip`) — D-4; account-username bans only.
- **`adduser`, `setpassword`, `setaccesslevel`/`grantadmin`, `addsteamid`, `servermsg`** — not player
  *management* in F19's sense; account/role/messaging surfaces are other features or out of scope.
- **A live roster push / connection-event stream** — D-1; the shipped build has no connection events.
- **A persisted player directory / seen-players history** — D-4; `ply-`/`PlayerRecordId` stays reserved.
- **The rich administrative console / arbitrary command box** — F28. F19 ships only these typed actions.
- **Out-of-band config-drift detection for the `Open` toggle** — F20b owns config revisions and drift; F19
  fires the runtime toggle and surfaces the confirmation, and does not reconcile the INI.
- **Reflecting the `Open` state onto the Server read model** — F20a (config read / `showoptions`) owns that.
- **Non-ASCII command arguments** — quirk 11; F19 restricts sent commands to ASCII.

## Domain / contract / persistence changes

- **Domain:** six `OperationKind` values (append, stored by name); new `BanRecord` entity + `BanStatus`
  enum. `PlayerActionOutcome` lives in Contracts. **No permission change** (D-5).
- **Contracts (additive, no version bump):** six `AgentCommand` leaves; `PlayerRosterResult` +
  `PlayerActionResult` optional on `OperationCompleted`. Assert `ProtocolVersion.Current == 1` holds; the
  closed-command-vocabulary guard stays green.
- **Persistence:** a **new `BanRecord` table**, dual-provider migration (Postgres + Sqlite). The six new
  `OperationKind` values need **no** migration (existing string column).

## Test plan (TDD, per PR)

- **PR-A (offline, `FakeRconServer`):** the **injection suite first** (every hostile username/reason);
  `PlayerResponseParser` for every response string incl. empty roster + malformed input; the six dispatch
  arms (applied / not-found / rejected / rcon-disabled / timeout, dedupe on redelivery); contract
  serialization + additivity + closed-vocabulary.
- **PR-B:** `PlayerManagement` per action (not-found/foreign ⇒ ServerNotFound; unauthorized ⇒ NotAuthorized
  incl. the server-scoped-with-no-server deny; invalid input ⇒ rejected; success audits; enumeration
  unaudited); `OperationDispatcherMapTests` for all six; the **completion-reconciliation seam**
  (BanPlayer success ⇒ Active `BanRecord`; UnbanPlayer success ⇒ Lifted; failed op ⇒ no/failed record);
  `BanRecord` mapping + the filtered-unique-active index (both providers); endpoint status-code mapping.
- **PR-C:** bUnit render/permission-gating tests (roster panel, bans panel, mode toggle; each control
  hidden without its permission; untrusted usernames render escaped), loose JSInterop.
- **Integration tier (`[Category("Networked")]`, deferred follow-up allowed as in F18):** against a real
  PZ container over the `zwarden` network — enumerate a known roster, kick/ban/unban a seeded user, and
  confirm the parsed outcomes match live prose. This is where the prose-parse heuristics are validated
  against a live server.

## Diagnostics

- **Command injection is impossible by construction:** usernames/reasons are validated then quoted (D-3);
  a hostile value is rejected with a legible reason, never assembled into a command. Tested adversarially.
- **Every RCON response is untrusted (§8):** parsed only against known-stable strings, never interpreted,
  bounded in size, escaped only at render — an operator sees a hostile username as text, not markup.
- **"No response" is an empty result, not a hang** (F18 trap 2) — an empty roster or a silent action
  resolves cleanly.
- **Failure is legible:** RCON-disabled / unreachable / cap-exhausted / timeout each surface a distinct
  reason on the failed operation, reusing F18's classification.
- **The ban registry is honest about drift (ADR 0027):** the UI states it lists bans issued via ZWarden,
  not PZ's authoritative list.

## Documentation

- **ADR 0027** — "ZWarden's ban registry records the bans it issued, not a mirror of PZ's user store":
  the bounded-drift boundary, why (no list-bans RCON command), and the consequence an operator must know.
- `docs/pzserver-architecture.md` — flip the player-management row to reflect the F19 surface (enumerate +
  kick/ban/unban/whitelist-remove/mode over RCON), and note the ban-registry drift boundary.
- ADR 0012 is now a **realized** feature — cross-reference F19 from it (the rescope it specified is shipped).
- `CONTEXT.md` — add "Ban record" (and confirm "Player roster" is a live poll, not an entity) if the
  glossary warrants it.

## Acceptance criteria

1. An operator with the right `Player.*` permission can **enumerate** a server's live roster and **kick**,
   **ban**, **unban**, and **remove from the whitelist** a user by account username, and **toggle the
   whitelist mode** — each dispatched like `RconHealthProbe`, on **additive** contracts
   (`ProtocolVersion.Current == 1`).
2. **Command injection is prevented:** hostile usernames/reasons are rejected before dispatch; all commands
   are correctly quoted (quirk 10); the adversarial suite is green.
3. **All RCON output is treated as untrusted** — parsed only against known-stable strings, bounded, escaped
   at render (§8).
4. Authorization is **fail-closed and server-scoped** (a `Player.*` check with no Server denies; a foreign
   Server is `ServerNotFound`); tenant isolation holds; the five mutations are **audited**
   (`Succeeded`/`Failed`/`Denied`) and enumeration is not.
5. The **ban registry** lists bans issued via ZWarden and supports unban-from-list; a `BanRecord` is
   written/lifted only from the operation's **terminal result**, never inferred from enqueue; **ADR 0027**
   states the drift boundary.
6. **No new permission** enters the catalogue (D-5); the closed-catalogue test is untouched and green.
7. Offline unit tier green (injection + parse + dispatch + service + reconciliation + render); the
   dual-provider `BanRecord` migration applies on both providers; ADR 0027 written; docs updated.

## Definition of Done

Per PRD 61: acceptance criteria met; tests authored first (TUnit unit incl. the adversarial injection/parse
suite; bUnit UI; a networked integration test for live prose-parse, deferrable to the opt-in tier as in
F18); RCON command construction/quoting/validation owned by F19 and injection-proof; all RCON I/O untrusted
(§8, escape at render); fail-closed server-scoped authorization + tenant isolation on every action; audit on
each mutation's terminal state; the ban registry reconciled only from observed completions (trust-boundaries
§3) with its drift boundary recorded (ADR 0027); protocol changes additive (`ProtocolVersion.Current == 1`);
dual-provider migration for `BanRecord`; no permission-catalogue change; docs + ADR 0027 written; CI green.
