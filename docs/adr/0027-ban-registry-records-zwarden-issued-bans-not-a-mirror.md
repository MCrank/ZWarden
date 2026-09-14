# 27. ZWarden's ban registry records the bans it issued, not a mirror of PZ's user store

ZWarden persists a **ban registry** (`BanRecord`, `ban-`) so an operator can see a Server's bans and unban
from that list, because **Project Zomboid ships no "list bans" RCON command** (`docs/research/project-zomboid-runtime.md`
§7). The registry records **the account-username bans ZWarden issued through the control plane** — the
acting user, the username, an optional reason, and whether it has since been lifted here — **recorded at the
moment the ban is authorized and enqueued, as advisory intent.** It is deliberately **not** a mirror of PZ's
authoritative user store: a ban issued in-game or from the server console never appears, and a ban ZWarden
issued may not have taken effect. **The Operation and the audit trail — not the registry — are authoritative
for whether the RCON command actually succeeded.**

- Status: accepted
- Decided in: F19 (#41)
- Bears on: PRD 36 (player management), criterion 8; ADR [0012](./0012-whitelist-addition-is-out-of-scope.md)
  (the F19 rescope), ADR [0011](./0011-configuration-revisions-are-value-level-and-fail-closed.md) (the same
  class of bounded, detect-don't-pretend drift), ADR [0022](./0022-operation-lifecycle-and-per-server-locking.md)
  (player actions are non-mutating Operations), ADR [0026](./0026-rcon-foundation-private-transport-and-agent-owned-credential.md)
  (the RCON path the ban travels)

## Context

An operator who bans a player needs to see and reverse that later. PZ's admin surface can `banuser` and
`unbanuser`, but **there is no command that lists the current bans** (research §7 operation matrix), so the
control plane cannot read PZ's ban set back. Without ZWarden keeping its own record, the ban list would be
invisible in the UI and "unban" would be a blind username text box.

Two further measured facts shape the design:

- **The registry cannot be authoritative.** PZ's ban set is edited by paths ZWarden does not see — the
  in-game admin panel and the server console both `banuser`/`unbanuser` behind ZWarden's back — exactly the
  class of drift ADR 0011 exists to contain for configuration. Any ZWarden-side record is therefore a record
  of *what ZWarden did*, not of *what PZ currently holds*.
- **The completion path has neither the actor nor a usable result.** A player action runs as a non-mutating
  Operation (ADR 0022); its terminal `OperationCompleted` is ingested by an Agent-driven path that carries
  no acting user and does not persist the per-action result (the same path drops F18's `RconHealthResult`).
  Reconciling a registry *from the completion* would mean threading the actor and the parsed outcome through
  the operation store — real plumbing — to make a record that is advisory either way.

## Decision

- **Persist `BanRecord`** (`ban-`, `ITenantOwned`, one per Server): the username, an optional reason, the
  issuing user, the issue time, and a `BanStatus` of `Active`/`Lifted` with the lifting user and time.
- **Record at enqueue, from the operator's authorized intent.** `PlayerManagement.BanAsync`, having resolved
  the Server, authorized `Player.Ban`, and validated the username, writes an `Active` record as it enqueues
  the ban Operation; `UnbanAsync` lifts the matching active record as it enqueues the unban. The record is
  **not** marked from the Agent's completion.
- **Account usernames only.** `banuser`/`unbanuser` — matching the usernames the `players` roster yields.
  Steam-ID (`banid`) and IP (`banip`) bans are out of scope (they need an identifier the roster does not
  provide); the registry keys on username.
- **A partial unique index** on `(TenantId, ServerId, Username)` filtered to `Status = 'Active'` admits at
  most one active ban per user per Server (re-banning keeps the one record), while lifted history is retained.
  The filter emits verbatim-identically on SQLite and PostgreSQL (ADR 0005).
- **The Operation and audit are authoritative for effect.** A ban that PZ reports as `User X doesn't exist.`
  fails its Operation (the Agent maps a non-`Applied` outcome to a failed completion, F19 PR-A) and is
  audited; the operator sees that. The registry states intent; the Operation states outcome.

## Alternatives considered

- **Reconcile the registry from the Operation's terminal result.** Rejected: the ingest path carries no
  acting user and persists no per-action result, so this needs new plumbing through the operation store — to
  produce a record that is advisory regardless, because PZ's set drifts out of band anyway. The honest,
  cheaper design records intent and points at the Operation/audit for effect. Revisit if a future need makes
  per-action results first-class on the Operation.
- **Write PZ's ban file (`bans.ini`/the account DB) directly to read the true set.** Rejected: it makes
  ZWarden a second author of PZ's own store while the server runs — the exact problem ADR 0011 contains — with
  no documented file contract and no drift story.
- **Do not persist anything; treat the audit log as the ban history.** Rejected on product value: the audit
  log is append-only and not a queryable "who is currently banned" list, so unban would stay a blind text box.
- **Mint the record only for bans confirmed applied.** Rejected: "confirmed" is exactly the completion-path
  reconciliation above, and PZ's out-of-band edits mean even a confirmed record can be wrong the next minute.

## Consequences

- **The registry can be wrong, in known ways, and that is accepted.** A ban ZWarden issued against a
  mistyped or absent username leaves an `Active` record even though the Operation failed — the operator can
  remove it, and the failed Operation is visible. A ban or unban done in-game never appears or disappears.
  The UI must **say** the list is "bans issued through ZWarden," not "everyone PZ has banned."
- **Unban is usable:** the operator picks from the recorded active bans rather than retyping a username.
- **Revisit triggers:** a future PZ build that adds a list-bans command would let the registry be reconciled
  against the real set (turning it authoritative); and if another feature makes per-action Operation results
  first-class, ban confirmation could move to completion. Neither is needed for v1.0.
