# Feature 28 Mini-Plan — Remote Administrative Console

**Status:** PLANNED (2026-09-15) — decisions locked as-recommended (2026-09-15) via a grilling round with the
maintainer. Roadmap issue: [F28 (#48)](https://github.com/MCrank/ZWarden/issues/48), **Track E — Operator
surface**. The feature that gives an operator a **free-form RCON command console**: type an admin command,
it runs against the server over F18's Agent-owned RCON path under an **elevated permission**, and the
untrusted output comes back in a live, escaped-at-render console pane — audited, with input safety, and
never exposing the RCON credential. This is the "arbitrary command box" F19 and F27 deliberately deferred
to F28.

**Depends on F6 (audit), F18 (RCON), F27 (Live Logs) — all merged and CLOSED (#28, #39, #47).** Blocks
[F55 (#55)](https://github.com/MCrank/ZWarden/issues/55) per the issue's native dependency edge.

**Format:** PRD 60. **TDD is mandatory** (PRD 2.2). **Written against:**
[`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (F28) and §11 criterion 9 ("securely use RCON");
[`research/project-zomboid-runtime.md`](../research/project-zomboid-runtime.md) **§7** (the RCON dialect,
the full admin-command surface at lines 982–990, the twelve+ quirks, and "RCON and the server console are
the same surface" — a caller with `connection == null` runs as `admin`); [`trust-boundaries.md`](../trust-boundaries.md)
**§5** (the Agent owns the RCON credential; Web never sees it) and **§8** (everything PZ emits over RCON is
untrusted end to end); and PRD **18** (arbitrary *shell* is forbidden — this console is arbitrary *RCON*,
not shell). ADRs it builds on:
[0018](../adr/0018-zwarden-owned-rbac.md) (server-scoped, fail-closed authorization),
[0019](../adr/0019-audit-is-append-only-tenant-owned-and-binds-the-auth-sink.md) (audit),
[0020](../adr/0020-agent-protocol-versioning-and-catalogue.md) (additive-vs-breaking; the closed command
vocabulary), [0022](../adr/0022-operation-lifecycle-and-per-server-locking.md) (operation lifecycle +
per-server lock), [0026](../adr/0026-rcon-foundation-private-transport-and-agent-owned-credential.md) (RCON
foundation — **which defers command construction to the caller**), and
[0030](../adr/0030-live-logs-stream-on-demand-sanitized-at-source-over-a-non-operation-channel.md) (the
live-panel UI pattern F28's output pane reuses). New **ADR 0032** lands with the feature (the console
command-policy boundary).

## Objective

Give operators a fail-closed, audited, **arbitrary-RCON console** over the F18 path. Under the elevated
`Console.Execute` permission (server-scoped), an operator submits a single RCON command line; ZWarden
applies **input safety and a command policy**, dispatches it as an audited Operation, runs it Agent-side
over one `IRconConnection.ExecuteAsync`, and surfaces the raw (untrusted, bounded, escaped-at-render)
output in a live console pane. F28 owns the one thing F19/F27 left open: **executing operator-authored RCON
command text safely**. It slots into the existing backbone (`ServerLifecycle` + `RconHealthProbe` +
`PlayerManagement`) end to end — one new operation kind, additive contracts, an application service, a
per-feature audit-action class, and a Blazor card — reusing the **already-declared** `Console.Execute`
permission and F19's untrusted-output posture. **No new entity, no migration** (`Operation.CommandPayload`
exists; the audit trail is the history).

## The load-bearing decisions (LOCKED as-recommended, 2026-09-15)

- **D-1 — The console is a free-form RCON command box governed by a *denylist*, not an allowlist.
  `[LOCKED]`** PRD 18 forbids arbitrary *shell*; RCON is not shell (though it is full server-admin — §7
  "RCON and the server console are the same surface"). A "console" must run arbitrary commands to be one, so
  F28 accepts any RCON command line **except** a small, security-motivated denylist enforced **server-side
  on both edges** (Web service for fast feedback + Agent for defence in depth, mirroring F19-D3):
  - **Credential minting / exposure** (honours the scope's "never exposing RCON credentials"): `adduser`,
    `setpassword`, `setaccesslevel`, `grantadmin`, `removeadmin`, `addsteamid`, `removesteamid`, and
    `changeoption` **when the target key is a credential key** (`RCONPassword`, `Password`, and the Discord
    token keys — §7: `changeoption` is *not* restricted to `publicOptions`, so it can write a new RCON/admin
    password into the INI).
  - **Lifecycle-invariant bypass**: `quit` — it stops the server outside F15's safe-stop path
    (`docker stop ?t=StopTimeoutSeconds` → SIGTERM → FIFO `save`/`quit`), which is exactly the
    world-corruption path F15 fixed; shutting a server down has a first-class, safe surface (F15).
  A denied command returns a **legible reason** and is **audited as `Denied`**, never sent. Everything else
  (`save`, `servermsg`, `showoptions`, `help`, `players`, `reloadoptions`, weather/debug commands, even the
  F19 `kick`/`ban` verbs — still audited here) runs. **Reversible**: the policy is a small data-driven rule
  set (`ConsoleCommandRules`), not a structural choice; tightening to an allowlist later is a rules edit.
  *Rejected — strict allowlist:* it is not a console (operators hit walls; every new PZ command needs a code
  change) and duplicates F19's typed-action model. *Rejected — unrestricted:* violates the explicit
  "never exposing RCON credentials" scope line and lets the console brick a world via `quit`.
- **D-2 — Console commands are audited *Operations*, server-scoped and *non-mutating*; output rides an
  additive result record + an in-memory cache. `[LOCKED]`** "auditing" is explicit in scope, and F19 already
  proved the shape: each command is an `Operation` (audited via the engine's `Operation.*` trail **plus** a
  feature audit record), dispatched like `KickPlayer`. It is **`IsMutating: false`, server-scoped** — the
  same reasoning as F19-D2/F18: RCON passthroughs are already serialized by `ZWarden.Rcon`'s single-socket
  gate (ADR 0026 D-3), so the DB per-server lock buys nothing, and making a console command mutating would
  make it collide with an in-flight `RestartServer` and fail `ServerBusy` — wrong for a diagnostic console
  you may well want to use *during* other activity. The raw response returns as an **additive optional**
  `ConsoleCommandResult` on `OperationCompleted` (nullable ⇒ additive; `ProtocolVersion.Current` stays 1,
  mirroring `RconHealthResult`/`PlayerActionResult`); the live pane reads it from an **in-memory
  ownership-guarded output cache** (`IConsoleOutputCache`, the F16/F19 `IPlayerRosterCache` pattern —
  transient display data, **no `Operation.ResultPayload` column**). **"Streaming output" = the live-updating
  pane**, not a byte stream: RCON is strictly request/response (§7 quirk 8, tick-serialized), so there is no
  multiplexed stream to follow as in F27; the console pane streams *command→response entries* live via the
  F27 cache+island poll pattern. *Rejected — ephemeral F27-style hub channel:* it carries no
  audit/idempotency, so auditing would be bolted on separately — more moving parts for a request/response
  interaction that the Operation path already models and audits.
- **D-3 — `Console.Execute` stays the "elevated" permission held only by Tenant Owner + Administrator; no
  catalogue and no built-in-role change. `[LOCKED]`** Both `Console.View` and `Console.Execute` already
  exist in the closed catalogue (`Permissions.cs:58–59`, `ServerScopable`); `Console.View` is what F27's log
  panel is gated on. Today `Console.Execute` is granted to nobody except `TenantOwner`/`Administrator` (via
  `Permissions.All`), and `BuiltInRolesTests` **asserts Operator/Moderator do *not* get it** — that is
  precisely how "an elevated permission" (the issue's words) is modelled. F28 keeps it there:
  `Console.View` gates *seeing* the console card, `Console.Execute` gates *running* a command. **No
  `Permissions.cs`, `BuiltInRoles.cs`, `PermissionCatalogueTests`, or `BuiltInRolesTests` change** — the
  closed-catalogue and seed-regression guards stay green untouched. *Rejected — grant Operator:* dilutes
  "elevated" and changes the seeded bundle; operators already administer via the typed F15/F17/F19/F22
  surfaces.
- **D-4 — The audit trail is the command history; no persisted console-history entity. `[LOCKED]`** Every
  executed (and every denied) command is already audited with actor, server, and the command line as
  `Detail` (D-2). The console card shows recent command→output entries from the in-memory `IConsoleOutputCache`
  for the live feel, plus **browser-local input recall** (arrow-up, `localStorage`, per-viewer convenience).
  **No new entity, no migration, no `ResultPayload`.** *Rejected — persisted `ConsoleCommandLog`:* real
  persistence/plumbing/migration for a record audit already holds authoritatively; a searchable console
  history is a post-1.1 nicety, not a criterion-9 need.

## Command surface and policy (from research §7)

| Aspect | Behaviour |
| --- | --- |
| Accepted input | one RCON command line; **ASCII-only** (§7 quirk 11 — request bodies decoded with the platform default charset); **single line** (reject `\n`/`\r`/NUL/other C0–C1 control chars); non-empty; length-bounded (≤ 1024 chars — comfortably inside `Operation.MaxCommandPayloadLength = 2048` after JSON encoding, and inside PZ's usable body) |
| Quoting | **the operator's job** (`IRconConnection.ExecuteAsync` sends text as-is; ADR 0026). ZWarden does **not** re-quote — it validates and policy-checks, then passes through. The help text tells operators to quote multi-word args (§7 quirk 10: `servermsg "My Message"`) |
| Command-name matching | PZ matches command **names** case-insensitively (§7 quirk 10). The policy parses the first token, lower-cases it, and checks the denylist; `changeoption` additionally has its **first argument** (the option key) checked against the credential-key set |
| Denylist (D-1) | `adduser`, `setpassword`, `setaccesslevel`, `grantadmin`, `removeadmin`, `addsteamid`, `removesteamid`, `quit`; and `changeoption <credential-key> …` for `RCONPassword` / `Password` / Discord token keys |
| Output | **untrusted** (§8): reassembled by the F18 client (chunked, no terminator — §7 quirks 1–3), **bounded** (cap stored/rendered output, e.g. 64 KiB, with a `Truncated` flag — `showoptions`/`help` can be tens of KB), **escaped at render** (data, never `MarkupString`). An empty response is a valid empty result, **not a hang** (§7 quirk 3; F18 handles it) |
| RCON disabled / unreachable | maps to a **failed** operation with a legible reason (reuse F18's `RconResolveResult` classification), never a hang (§7 quirk 2/trap 2) |

The console command line is **`IsMutating: false`, server-scoped** (D-2). Because the first token is echoed
verbatim into what PZ runs as `admin`, D-1's policy + D-input-safety are load-bearing and get a dedicated
adversarial test suite written first.

## Scope, by PR

### PR-A — Contracts + Domain policy + Agent execution core (branch `feat/f28-console-contracts-agent`)

The typed command surface, the command policy/input-safety rules, and the Agent-side execution, written
red-green against `FakeRconServer` (the F18 harness). The security core of the feature.

1. **Contracts (additive — ADR 0020, `ProtocolVersion.Current` stays 1):** one
   `sealed record ExecuteConsoleCommand(string Input, ...) : AgentCommand` with
   `[ProtocolMessage("console.execute")]`, auto-registered by `ProtocolJson` reflection (copy
   `KickPlayer`/`ProbeRconHealth`). **The string property is `Input`, NOT `Command`/`CommandLine`/`Cmd`/
   `Exec`/`Script`/… — `ClosedCommandVocabularyTests` (trust-boundaries §9 rule 3) fails the build on any of
   those names.** It is a legitimate typed leaf (an RCON line under an elevated, audited, policy-gated
   surface), not a free-form shell string — assert that guard **and** the closed-vocabulary/additivity
   guards stay green. The target Server rides the envelope `ServerId`; the command carries `OperationId` for
   Agent-side idempotency. Add one **additive optional** result record on `OperationCompleted`:
   `ConsoleCommandResult(string Output, bool Truncated)` (nullable ⇒ additive, mirroring `RconHealthResult`;
   `Output` is untrusted PZ text carried verbatim).
2. **`ConsoleCommandRules`** (`ZWarden.Domain/Console/`, pure, shared by Web + Agent — the F19
   `PlayerCommandRules` pattern): `ValidateInput(string)` → the input-safety checks (ASCII-only, single-line,
   non-empty, length ≤ 1024, no control chars); `EvaluatePolicy(string)` → parses the first token
   (lower-cased) and the `changeoption` option key, returns `Allowed` / `Denied(reason)` against the D-1
   denylist. Data-driven (a `FrozenSet` of denied names + credential keys). Exhaustively unit-tested,
   including case/whitespace variants and `changeoption` credential-key targeting.
3. **`ConsoleAdministration`** (`ZWarden.Agent/Console/`, copy `PlayerAdministration.ExecuteAsync`,
   ~35 lines): re-validate + re-policy-check (defence in depth), resolve the endpoint via the existing
   `IRconEndpointResolver`, `RconConnectionFactory.Create(endpoint)`, `connection.ExecuteAsync(input, ct)`,
   **bound the output** (cap + `Truncated`), map RCON exceptions (disabled/unreachable/timeout) to a failed
   result, and **`finally` dispose** the connection (cap-slot safety, §7 quirk 7). Never interprets the
   response (§8).
4. **Agent dispatch:** one `case ExecuteConsoleCommand` arm in `AgentCommandProcessor.ProcessAsync` (dedupe
   by `OperationId`), following the `PlayerActionAsync` helper shape — resolve serverId from the envelope,
   run `ConsoleAdministration`, carry `ConsoleCommandResult` on the `Completed(...)` builder (add its
   optional result param). A denied/invalid input maps to a **failed** completion with the legible reason
   (belt-and-braces; the Web edge already rejected it), never a hang.
5. **`OperationKind`** — append `ExecuteConsoleCommand` (stored by name; doc-comment `IsMutating: false`,
   server-scoped). No migration (existing string column). Add it to `OperationDispatcher.CommandFor` in
   PR-B, but the kind lands here so the enum is complete.
6. **Tests (`ZWarden.Domain.Tests` / `ZWarden.Agent.Tests` / `ZWarden.Contracts.Tests`):** the **policy +
   input-safety adversarial suite first** — every denied command (incl. case/whitespace variants,
   `changeoption RCONPassword`/`Password`), every input-safety violation (newline/NUL/control, non-ASCII,
   over-length, empty), and representative allowed commands; `ConsoleAdministration` dispatch against
   `FakeRconServer` (applied / empty-result / rcon-disabled / timeout, output-truncation, dedupe on
   redelivery); contract serialization + additivity + **`ClosedCommandVocabularyTests` green (verify the
   `Input` name)**. Bump the affected `--minimum-expected-tests` floors.

### PR-B — Application service, operation wiring, output cache, audit (branch `feat/f28-console-operations`)

The control-plane half: authorize + enqueue + audit + surface output, mirroring `PlayerManagement`.

1. **`ConsoleCommandPayload`** (`ZWarden.Application/Console/`, copy `PlayerCommandPayload`):
   `sealed record ConsoleCommandPayload(string Input)` with `ToJson()`/`FromJson()` — shared by the
   enqueueing service and the Web dispatcher without a Contracts reference.
2. **`OperationDispatcher.CommandFor`** — one arm:
   `OperationKind.ExecuteConsoleCommand => new ExecuteConsoleCommand(Payload(commandPayload).Input)`; covered
   by `OperationDispatcherMapTests`.
3. **`IConsoleCommandService`/`ConsoleCommandService`** (`ZWarden.Application` interface +
   `ZWarden.Infrastructure` impl, copy `PlayerManagement.ActAsync`): resolve the Server through the tenant
   filter (→ `ServerNotFound`), **fail-closed authorize `Permissions.ConsoleExecute` server-scoped inside
   the service** (F14-D3; → `NotAuthorized`), run `ConsoleCommandRules.ValidateInput` + `EvaluatePolicy`
   (edge copy; → `Rejected`/`Denied` with reason), `EnqueueAsync(… IsMutating: false, ServerId …,
   CommandPayload: payload.ToJson())`, and **audit** — `Console.CommandExecuted` on enqueue-success,
   `Console.CommandDenied` on a policy/authorization denial (`Detail` = the command line; carries no secret,
   §7 confirms `showoptions`/RCON never echo the password). Returns a result carrying the operation id.
4. **`IConsoleOutputCache`/`ConsoleOutputCache`** (copy `IPlayerRosterCache`): in-process, **ownership-guarded
   by the reporting Agent** (§8), latest-N entries per (ServerId) for the live pane; `AgentHub.OperationCompleted`
   records a `ConsoleCommandResult` into it (like the roster cache at `AgentHub.cs:344`). No persistence.
5. **`ConsoleAuditActions`** (`ZWarden.Infrastructure/Console/`): `Console.CommandExecuted`,
   `Console.CommandDenied`. Written via `IAuditWriter`; the engine's automatic `Operation.*` trail covers
   dispatch/terminal state.
6. **Endpoints + DI:** `POST /api/servers/{id}/console` (submit a command; `Results.Accepted("/api/operations/{id}")`)
   and `GET /api/servers/{id}/console/output` (read the cache), authenticated at the edge with the
   fail-closed server-scoped `Console.Execute` check in the service. `AddZWardenConsole()` +
   `MapConsoleEndpoints()` in `Program.cs` + a `ConsoleServiceCollectionExtensions`. Not-found/foreign → 404,
   unauthorized → 403, invalid/denied input → 400.
7. **Tests:** `ConsoleCommandService` (not-found/foreign ⇒ ServerNotFound; unauthorized incl.
   server-scoped-with-no-server ⇒ NotAuthorized; invalid/denied ⇒ Rejected + audited Denied; success ⇒
   enqueued + audited Executed); `OperationDispatcherMapTests` for the new arm; `ConsoleOutputCache`
   ownership guard (a foreign agent's completion can't shadow the owner's); endpoint status-code mapping.

### PR-C — Blazor console surface (closes #48; branch `feat/f28-console-ui`)

The operator-facing console card on the **server-detail** page (`/servers/{id}`), **Bb** components, SSR
patterns per [[blueprint-seam-on-ssr-forms]], slotting in right after the F27 **Live logs** card.

1. **Console card** gated `_canExecuteConsole` (`PermissionChecker.EvaluateAsync(user, Permissions.ConsoleExecute,
   serverId)` in `OnInitializedAsync`; the service re-check is the fail-closed authority). Help text notes
   the quoting rule (§7 quirk 10) and that credential/`quit` commands are blocked (D-1).
2. **Command submission:** an SSR `EditForm` (single command line + Run) with `[SupplyParameterFromForm]`,
   calling `IConsoleCommandService`; the outcome surfaces as an operator message pointing at the enqueued
   operation (or the legible denial reason). Browser-local input recall (arrow-up, `localStorage`,
   try/catch-guarded per-viewer convenience).
3. **Live output pane:** an interactive island cloned from `LiveServerLogPanel.razor`
   (`@rendermode InteractiveServer`, `PeriodicTimer` poll of `IConsoleOutputCache`), rendering each
   command→output entry **as data (never `MarkupString`)** — a hostile command echo or hostile server output
   renders as text, not markup (§8). Bounded/truncation indicated.
4. **Tests:** bUnit render/permission-gating (card hidden without `Console.Execute`; untrusted output renders
   escaped; denied-command message shown), loose JSInterop; real-host page/POST integration tests. Per-PR
   chore: bump the Web.Tests floor in **both** the csproj and `ci.yml`'s `tier1-silent-drop-guard`
   ([[web-tests-discovery-floor-bump]]); `npm run build:css` + commit `wwwroot/app.css` if a new utility is
   introduced ([[tailwind-app-css-rebuild]]).

## Non-scope

- **Arbitrary shell / OS command execution** — PRD 18 forbids it outright; F28 is arbitrary *RCON* only.
- **Credential-minting/exposing and lifecycle-bypass commands** — `adduser`, `setpassword`,
  `setaccesslevel`, `grantadmin`/`removeadmin`, `addsteamid`/`removesteamid`, `changeoption <credential-key>`,
  and `quit` are denied by policy (D-1). Shutting a server down is F15's safe surface.
- **A persisted, searchable console history** — D-4; the audit trail is the history for v1.0.
- **Granting `Console.Execute` to Operator/Moderator** — D-3; it stays elevated (Owner/Admin).
- **A byte-level output stream / follow** — D-2; RCON is request/response, and log *following* is F27.
  The console pane "streams" command→response entries via the live cache, not a socket stream.
- **Re-quoting or rewriting operator input** — ADR 0026 sends text as-is; F28 validates + policy-checks +
  passes through. Quoting is documented as the operator's responsibility (§7 quirk 10).
- **Non-ASCII command arguments** — §7 quirk 11; sent commands are ASCII-only.
- **Out-of-band config-drift reconciliation** for `changeoption` writes — F20b owns config revisions/drift;
  F28 runs the command and surfaces the confirmation, and does not reconcile the INI.

## Domain / contract / persistence changes

- **Domain:** one `OperationKind.ExecuteConsoleCommand` (append, stored by name); new `ConsoleCommandRules`
  (pure policy/validation). **No permission change** (D-3) — `Console.Execute` already exists.
- **Contracts (additive, no version bump):** one `ExecuteConsoleCommand` leaf (string field **`Input`**);
  `ConsoleCommandResult(string Output, bool Truncated)` optional on `OperationCompleted`. Assert
  `ProtocolVersion.Current == 1`; the closed-command-vocabulary and free-form-guard tests stay green.
- **Persistence:** **none.** `Operation.CommandPayload` (F19) carries the command line; there is no new
  table and **no migration** (the six-value string `OperationKind` column already stores the new kind; the
  audit trail is the history; output rides the additive result + in-memory cache — no `ResultPayload`).

## Test plan (TDD, per PR)

- **PR-A (offline, `FakeRconServer`):** the **policy + input-safety adversarial suite first** (every denied
  command incl. case/whitespace + `changeoption` credential keys; every input-safety violation);
  `ConsoleAdministration` dispatch (applied / empty-result / rcon-disabled / timeout / output-truncation /
  dedupe); contract serialization + additivity + `ClosedCommandVocabularyTests` (verify `Input`).
- **PR-B:** `ConsoleCommandService` (not-found/foreign / unauthorized incl. no-server / invalid-denied
  audited as Denied / success enqueued+audited); `OperationDispatcherMapTests` for the new arm;
  `ConsoleOutputCache` ownership guard; endpoint status-code mapping.
- **PR-C:** bUnit render/permission-gating (card hidden without `Console.Execute`; untrusted output escaped;
  denial message), loose JSInterop; real-host page/POST integration tests.
- **Integration tier (`[Category("Networked")]`, deferrable to the opt-in tier as in F18/F19):** against a
  real PZ container over the `zwarden` network — run `players`/`showoptions`, confirm parsed-as-text output,
  confirm a denied command is refused, confirm an empty-result command resolves cleanly (§7 quirk 3).

## Diagnostics

- **No arbitrary shell** (PRD 18): the console runs only RCON command text, dispatched over F18's private
  transport; there is no OS-command path.
- **Command policy is fail-closed and enforced twice** (D-1): credential-minting/exposing and lifecycle-bypass
  commands are denied with a legible reason at the Web edge **and** re-checked Agent-side; a denial is
  audited.
- **The RCON credential never leaves the Agent** (§5): Web never sees it; `showoptions` never prints it (§7);
  the credential-write commands are denied, so the console cannot set or read it.
- **Every RCON response is untrusted** (§8): carried verbatim, bounded, and escaped only at render — an
  operator sees hostile output as text, not markup.
- **"No response" is an empty result, not a hang** (§7 quirk 3 / F18 trap 2); RCON-disabled / unreachable /
  cap-exhausted / timeout each surface a distinct reason on the failed operation (F18 classification reused).
- **Every command is audited** (D-2/D-4): `Console.CommandExecuted` / `Console.CommandDenied` plus the
  engine's `Operation.*` trail — the audit log is the authoritative history.

## Documentation

- **ADR 0032** — "The remote console runs arbitrary RCON under an elevated permission, governed by a
  credential/lifecycle denylist, audited, with all output untrusted": why a denylist over an allowlist
  (D-1), why credential and `quit` commands are excluded, why commands are audited non-mutating Operations
  (D-2), and the consequence operators must know (the console is full server-admin — §7).
- `docs/pzserver-architecture.md` — add the remote-console row (arbitrary RCON over the F18 path, elevated
  `Console.Execute`, denylist + input safety, audited, output untrusted).
- `CONTEXT.md` — add "Remote console" / "Console command policy" if the glossary warrants it.
- Cross-reference F28 from the F19 and F27 plans as the now-realized "arbitrary command box".

## Acceptance criteria

1. An operator with `Console.Execute` on a server can submit an arbitrary RCON command and see its output
   in a live pane; the command is dispatched like `RconHealthProbe`/`KickPlayer` on **additive** contracts
   (`ProtocolVersion.Current == 1`), with the string field named `Input` (the closed-vocabulary canary is
   green).
2. **Command policy holds** (D-1): credential-minting/exposing commands and `quit` are denied — with a
   legible reason, audited `Denied`, never sent — enforced at the Web edge and re-checked Agent-side; the
   adversarial policy suite is green.
3. **Input safety holds**: non-ASCII, multi-line, control-char, over-length, and empty input are rejected
   before dispatch.
4. **All RCON output is treated as untrusted** (§8) — carried verbatim, bounded (with a `Truncated` flag),
   escaped at render; an empty response resolves cleanly, not as a hang.
5. Authorization is **fail-closed and server-scoped** (a `Console.Execute` check with no Server denies; a
   foreign Server is `ServerNotFound`); tenant isolation holds; **`Console.Execute` remains elevated**
   (Owner/Admin only) with **no catalogue/built-in-role change** (D-3) — `PermissionCatalogueTests` and
   `BuiltInRolesTests` are untouched and green.
6. **Every executed and denied command is audited** (D-2/D-4); the audit trail is the command history; there
   is **no new entity and no migration**.
7. The RCON credential is never exposed by the console (the credential-write/read commands are denied;
   `showoptions` never prints it; Web never holds the secret).
8. Offline unit tier green (policy + input-safety + dispatch + service + cache + render); ADR 0032 written;
   docs updated; CI green.

## Definition of Done

Per PRD 61: acceptance criteria met; tests authored first (TUnit unit incl. the adversarial policy/input-safety
suite; bUnit UI; a networked integration test for live RCON, deferrable to the opt-in tier as in F18/F19);
the console runs arbitrary RCON but no shell (PRD 18), governed by a fail-closed credential/lifecycle
denylist enforced on both edges (D-1); all RCON I/O untrusted (§8, escape at render, bounded); fail-closed
server-scoped authorization + tenant isolation on every command; `Console.Execute` stays elevated with no
catalogue/role change (D-3); every command audited (D-2/D-4); protocol changes additive
(`ProtocolVersion.Current == 1`, field named `Input`); no new entity and no migration; ADR 0032 written;
docs updated; CI green.
