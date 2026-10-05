# Issue #363 Mini-Plan — "Remove host" cleans up a retired or duplicate Host

**Status:** one PR (branch `feat/363-remove-host`, closes #363). Part of epic #349 (Hosts polish).

**Written against:** issue #363 (+ its #365 comment); #342's mini-plan (island on the static Hosts page,
`ActionScopeRunner`); #271 (Delete server's `BbAlertDialog`); F9 / ADR 0007 (`IAgentTrustService` authorizes
`Tenant.Enrollment.Manage`, audits, drops a live connection); ADR 0016 (tenant filter).

## Objective

Each Host card gets a **Remove host…** action (Owner-only) that takes the Host out of ZWarden: the Agent record goes,
its live connection is dropped, and the removal is audited. A Host that still has Servers can't be removed. Nothing
happens on the machine itself.

## Facts found (2026-10-05)

- `IAgentTrustService` (Infrastructure `AgentTrustService`) already holds rotate / revoke / disable / enable, each
  behind `Tenant.Enrollment.Manage` (tenant-wide, fail-closed), audited as `Agent.*` with `agent <id>` as the target,
  and `DropLiveConnectionAsync` (`IAgentConnectionRegistry.TryAbort` + an `Agent.CredentialRevokedWhileConnected`
  audit when a live connection was dropped). An Agent outside the tenant reads as "not found" → denied.
- Nothing has a foreign key to `Agents`. `Server`, `Operation` and `Backup` carry an `AgentId`; `Enrollment` carries
  `ConsumedByAgent`. Servers are the only live dependents (Operations/Backups are history; an Operation stuck on
  an unreachable Host is failed by the lease reaper as today).
- The credential verifier finds an Agent by credential hash, so a deleted record means a reconnect is refused.
  `AgentConnectionStateWriter` ignores an unknown AgentId, so the disconnect after the abort is harmless.
- `/hosts` is static; `EnrollHostSheet` (an island, Owner-only) carries the page's only `BbPortalHost`. The portal
  service is circuit-scoped, so every island on the page shares one host — a second host would render every
  overlay twice.

## Decisions

- **D1 — service.** `IAgentTrustService.RemoveAsync(actor, agentId)` → `HostRemovalResult` (`Removed`, or
  `HasServers` with the count). Order: permission check (throws `AuthorizationDeniedException`), find the Agent
  (not in this tenant → denied), count its Servers through the tenant filter (> 0 → `HasServers`, nothing changed),
  then revoke the credential and delete the record in one save, audit `Agent.Removed`, and drop the live connection
  (deleting first means a reconnect racing the abort is already refused). The consumed enrollment stays.
- **D2 — island per card.** `Components/Hosts/RemoveHostDialog.razor`: a small destructive-outline button at the foot
  of the card and a `BbAlertDialog` titled "Remove *host*?". Rendered `InteractiveServer` only when the caller holds
  `Tenant.Enrollment.Manage` (the page already evaluates it for Enroll host). Parameters: `AgentId`, display name,
  the server count the page already knows (Owner sees every Server).
  - Servers > 0: the dialog says "*host* still has N server(s). Delete them first." and only offers Close.
  - Otherwise it lists what happens: the Agent is disconnected and its credential revoked; the Host disappears from
    Hosts and Fleet; nothing on the machine is touched (containers, volumes and PZ data stay); bringing the machine
    back needs a new enrollment token and shows up as a new Host. It does **not** promise the old servers come back
    (#365).
  - Confirm → `ActionScopeRunner` → `RemoveAsync`; `Removed` → `Nav.Refresh()`; `HasServers` (a race) → the blocked
    message; denied → "You are not permitted to remove hosts."
- **D3 — one portal host.** The page renders a single `<BbPortalHost @rendermode="InteractiveServer" />` island when
  the caller may enroll/remove; `EnrollHostSheet` drops its own.
- No confirmation typing (unlike Delete server): removing a Host loses no data and is reversible by re-enrolling.

## Tests

- Infrastructure (`AgentTrustServiceTests`): denied without the permission (nothing deleted, nothing audited);
  blocked with Servers (count returned, Agent kept); removes + audits + aborts + the old credential no longer
  verifies; a live connection's drop is audited; another tenant's Agent is denied and kept.
- bUnit (`RemoveHostDialog`): opens naming the Host; blocked message with no Remove button when it has Servers;
  Remove calls the service and refreshes; denied shows the message.
- Page (`HostInventoryPageTests`): an Owner gets a Remove island per card; an Administrator (Agent.View only) gets
  none.
- Browser smoke (`HostsSmokeTests`): open the dialog on a Host card and cancel.
- Docs: the remote-agent / Hosts docs mention Remove host.
