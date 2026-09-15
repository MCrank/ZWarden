# 32. The remote console runs arbitrary RCON under an elevated permission, governed by a denylist and audited

ZWarden's remote administrative console (F28) lets an operator run **an arbitrary RCON command line** against a
Server — the Project Zomboid server console, which runs as `admin` (research §7). It is **not** a shell (PRD 18
forbids arbitrary OS command execution outright), and it is **not** an open door: it is gated by the **elevated,
server-scoped `Console.Execute`** permission (held only by Tenant Owner and Administrator), every submission is a
**non-mutating, audited Operation**, and a **command policy denylist** refuses two families before a byte is sent —
**credential minting/exposure** (`adduser`, `setpassword`, `setaccesslevel`, `grantadmin`/`removeadmin`,
`addsteamid`/`removesteamid`, and `changeoption` when the target key is a credential key `RCONPassword`/`Password`/
`DiscordToken`) and **`quit`** (which bypasses F15's safe stop). The policy and the input-safety checks (single
line, printable ASCII, bounded) live in one pure `ConsoleCommandRules` module enforced **on both edges** — the Web
service before enqueue and the Agent before the line is sent. All RCON output is **untrusted** (trust-boundaries.md
§8): bounded at the Agent and escaped at render. The RCON credential is never exposed — the credential-reading
commands are denied, `showoptions` never prints the password (research §7), and the Web tier never holds the secret
(ADR 0026).

- Status: accepted
- Decided in: #48 (F28; mini-plan [`docs/feature-plans/F28-remote-administrative-console.md`](../feature-plans/F28-remote-administrative-console.md); decisions locked via a grilling round, 2026-09-15)
- Bears on: PRD 18 (no arbitrary shell — this is arbitrary *RCON*, a bounded, typed, audited surface, not a shell);
  ADR [0026](./0026-rcon-foundation-private-transport-and-agent-owned-credential.md) (RCON foundation — the Agent
  owns the credential and sends the line as-is; this ADR is what finally *uses* the arbitrary-command path ADR 0026
  deferred); ADR [0018](./0018-zwarden-owned-rbac.md) (`Console.Execute` is a catalogue permission held by few
  roles — how "elevated" is modelled; no catalogue change); ADR
  [0022](./0022-operation-lifecycle-and-per-server-locking.md) (non-mutating, server-scoped — never takes the
  per-server lock); ADR [0019](./0019-audit-is-append-only-tenant-owned-and-binds-the-auth-sink.md) (every command
  and every policy denial is audited — the console's durable history); ADR
  [0020](./0020-agent-protocol-versioning-and-catalogue.md) (`ExecuteConsoleCommand`/`ConsoleCommandResult` are
  additive — protocol stays v1 — and the leaf's line is named `Input`, not `Command`, to keep the closed-vocabulary
  canary green); trust-boundaries.md §8 (all RCON I/O untrusted).

## Context

F18 gave the Agent a private, credential-owning RCON client but **deferred command construction to the caller**;
F19 built typed, quoted player commands on top of it and explicitly left "the arbitrary command box" to F28; F27's
`Console.View` was reserved with a note that F28 would add `Console.Execute`. F28 is that box.

RCON in Project Zomboid is not a restricted surface. `GameServer.rcon(command)` calls the same
`handleServerCommand(command, null)` the stdin console uses, and a `null` connection is granted the default admin
role (research §7, "RCON and the server console are the same surface"). So a console command runs with full
server-admin authority. The command vocabulary (research §7, lines 982–990) includes commands that **mint or
expose credentials** — `adduser`/`setpassword` create and set PZ account passwords, `setaccesslevel`/`grantadmin`
grant privilege, and `changeoption` is *not* restricted to public options, so it can write a new RCON or admin
password into the INI — and `quit`, which stops the server **outside** F15's docker-stop→SIGTERM→FIFO
`save`/`quit` path (the exact ungraceful-shutdown route F15 fixed to avoid world corruption).

Two scope lines from the issue are load-bearing against that surface: **"input safety"** and **"never exposing RCON
credentials."** They make an unrestricted passthrough unacceptable, but PRD 18 forbids only *shell*, not *RCON*, and
a "console" that cannot run arbitrary commands is not a console. The decision is where to draw the line.

## Decision

- **Free-form command box governed by a denylist, not an allowlist.** Any RCON line is accepted **except**:
  - **Credential minting/exposure:** `adduser`, `setpassword`, `setaccesslevel`, `grantadmin`, `removeadmin`,
    `addsteamid`, `removesteamid`, and `changeoption <key> …` where `<key>` (case-insensitive, quotes stripped) is
    `RCONPassword`, `Password`, or `DiscordToken`.
  - **Lifecycle bypass:** `quit`.
  Command names match **case-insensitively** (research §7 quirk 10). A denied command returns a legible reason, is
  **audited `Console.CommandDenied`**, and is **never sent**.
- **Input safety, enforced first:** a submission must be a **single line**, **printable ASCII only** (PZ decodes
  request bodies with the platform default charset — research §7 quirk 11), non-empty, and **≤ 1024 characters**
  (comfortably inside `Operation.MaxCommandPayloadLength` after JSON encoding). A control/newline/non-ASCII byte is
  rejected as `InvalidInput`.
- **One rules module, both edges.** `ConsoleCommandRules.ValidateInput` + `EvaluatePolicy` (pure, in the Domain) run
  at the **Web edge** (`ConsoleCommandService`, before enqueue — fast operator feedback) **and** at the **Agent**
  (`ConsoleAdministration`, before the line is sent — defence in depth, since the line is actually assembled there).
- **Audited, non-mutating Operations.** Each command is an `ExecuteConsoleCommand` Operation: **server-scoped**,
  **non-mutating** (an RCON passthrough already serialized by the Agent's single-socket gate — ADR 0026 — so the DB
  per-server lock would buy nothing and would wrongly collide a console command with an in-flight restart), audited
  `Console.CommandExecuted` on enqueue plus the engine's own `Operation.*` trail. Quoting is the **operator's job**
  (ADR 0026 sends the line as-is); ZWarden validates and policy-checks, then passes through.
- **Elevated permission, no catalogue change.** Execution is gated by `Console.Execute`, already in the closed
  catalogue and held only by Tenant Owner and Administrator; `Console.View` gates *seeing* the console.
- **Untrusted output.** The reply is reassembled by the F18 client, **bounded** at the Agent (64 KiB, with a
  `Truncated` flag — `help`/`showoptions` run to tens of KB), carried verbatim on `ConsoleCommandResult`, held in an
  in-memory ownership-guarded cache for the live pane, and **escaped at render** (§8). An empty reply is a valid
  empty result, not a hang (research §7 quirk 3).
- **History is the audit trail.** Every executed and denied command is audited with actor, Server, and the command
  line (non-secret — credential commands are denied). No separate console-history entity is persisted.

## Alternatives considered

- **Strict allowlist of safe commands.** Safest, but it is not a console: operators hit walls on legitimate
  commands, every new PZ command needs a code change to permit, and it collapses back into F19's typed-action model.
  The denylist inverts the burden to exactly the commands that violate the two scope lines, and it remains available
  as a fallback — `ConsoleCommandRules` is a data-driven rule set, so tightening to an allowlist later is a rules
  edit, not a redesign.
- **Unrestricted passthrough.** Simplest and most "raw," but it lets the console read/set the RCON or admin password
  (violating "never exposing RCON credentials") and `quit` a server outside the safe-stop path (world-corruption
  risk). Rejected on the issue's explicit scope, not taste.
- **Grant `Console.Execute` to the Operator role.** Convenient, but it dilutes "elevated"; operators already
  administer through the typed F15/F17/F19/F22 surfaces. Kept to Owner/Admin.
- **Ephemeral F27-style live channel for the command round-trip.** Lower latency, but it carries no audit or
  idempotency, so auditing would be bolted on separately — more moving parts for a request/response interaction the
  Operation path already models and audits. "Streaming output" here is the live-updating pane over the output cache,
  not a byte stream (RCON is strictly request/response — research §7 quirk 8).
- **A persisted console-history entity.** Real persistence and a migration for a record the audit trail already
  holds authoritatively; a searchable console history is a post-1.1 nicety, not a criterion-9 need.

## Consequences

- The console is genuinely powerful — full server-admin over RCON — and we accept that: it is gated by an elevated
  permission, every command is attributable and audited, and the two families that could exfiltrate a credential or
  corrupt a world are refused by construction on both edges.
- The denylist is a **maintenance surface**: a future PZ build that adds a new credential-minting command must be
  added to `ConsoleCommandRules`. This is the cost of the denylist over an allowlist, knowingly taken; the
  adversarial rules test suite is where a regression shows up.
- The policy blocks `quit` even though `quit` is a real RCON command an operator might expect. This is deliberate —
  shutting a server down has a first-class, world-safe surface (F15) — and it is the one place the console is
  surprising, so it returns an explicit reason pointing the operator at the lifecycle controls.
- The console does **not** reconcile out-of-band config drift for a `changeoption` the operator runs (F20b owns
  config revisions and drift); it runs the command and surfaces the reply. An operator can change a live option from
  the console without a revision being recorded — the same bounded drift F20b already documents.
- Output shown in the live pane is only the **successful** replies (the completion carries `ConsoleCommandResult`
  only on success); a failed command's reason is surfaced through its Operation, not the pane. Acceptable for v1.0.
