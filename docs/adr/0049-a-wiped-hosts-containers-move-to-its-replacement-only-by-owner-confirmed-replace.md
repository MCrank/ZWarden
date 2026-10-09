# 49. A wiped Host's containers move to its replacement only through an Owner-confirmed Replace host

When a machine's Agent state is wiped and the machine enrolls again, the new Host's Agent finds PZ containers stamped
with the previous Agent's id. It **reports** them and never operates on them. The Owner can then **replace** the old
Host with the new one, which is the only way an Agent comes to own containers stamped with another Agent's id. The
replacement moves the old Host's Servers and backups to the new Host, revokes and removes the old Host, and records
that the new Host **inherits** the old id. ZWarden holds the inherited ids and sends them to the Agent on every
connect. The Agent never stores them.

- Status: accepted
- Decided in: #368 (grilled 2026-10-09)
- Bears on: ADR [0007](./0007-agent-authentication-enrollment-credential-in-v1-0.md) (credentials; the old one is revoked),
  ADR [0008](./0008-docker-socket-access-via-wollomatic-socket-proxy.md) (the ownership guard is the real control), ADR
  [0016](./0016-tenant-isolation-query-filter-and-default-tenant.md) (same tenant only), ADR
  [0020](./0020-agent-protocol-versioning-and-catalogue.md) (additive protocol), and the prefix registry (new `hr-`)

## Context

ADR 0008 makes the Agent's `ContainerOwnershipGuard` the control over which containers it may touch: a canonical
container whose `io.zwarden.agent-id` label isn't this Agent's id is refused. #365 widened "this Agent's id" to the
enrolled id plus the legacy local id. Wiping `agent_state` (`docker compose down -v`, a lost volume) produces a new
Agent with new ids, so every PZ container on the machine becomes foreign: hidden from discovery, unmanageable, and
bound in the database to a Host whose credential no longer exists. Docker labels can't be changed in place, so the
containers can't simply be re-stamped.

Letting an Agent claim another id is dangerous. A compromised Agent that could claim any id would receive other
Hosts' Servers and the secrets their operations carry.

## Decision

- **Report, never act.** The Agent's snapshot gains an optional list of canonical containers stamped with an id it
  doesn't own (Docker short id, server id, labelled agent id, state; at most 64). The guard is unchanged for them.
  The control plane keeps the latest report per reporting Agent in memory. It is untrusted.
- **Owner-confirmed, never automatic.** `IHostReplacementService.ReplaceAsync(actor, successor, predecessor)`
  requires all of the following:
  - `Tenant.Enrollment.Manage` (Owner only);
  - both Hosts in the actor's tenant;
  - two different Hosts;
  - a trusted successor;
  - an offline predecessor (no live connection);
  - the predecessor's id present in the successor's own latest report, so the Owner can only hand a Host's Servers
    to the machine their containers are actually on.

  A refusal changes nothing.
- **One step.** In one save: the predecessor's Servers and backups move to the successor, ids the predecessor had
  inherited pass on (chains follow the machine), a `HostReplacement` (`hr-`) records successor ← predecessor, the
  default deploy host follows, and the predecessor is revoked and deleted. The step is audited as `Agent.Replaced`.
  Only this service calls `Server.ReassignTo` / `Backup.ReassignTo`; an architecture test pins that.
- **ZWarden holds the inheritance.** The Agent asks for its inherited ids (`InheritedAgentIds`) after `Hello` and
  before its snapshot, and again when told `OwnershipChanged` after a Replace. The answer is for the authenticated
  Agent only. The Agent keeps the ids in memory, and `Owns` includes them. Nothing is written to `agent_state`.
- **Lazy re-stamp.** No restart at replace time. Each container keeps the old label until its next Recreate (config
  apply, game update, branch change), which stamps the successor's id.
- **Unknown ids stay manual.** A reported container whose id is no known Host, or a Host that is still connected, is
  listed read-only on the card with `docker rm -f <id>` guidance. ZWarden never touches it.

## Consequences

- A host-local claim (an operator naming the old id from a shell) was rejected: ZWarden couldn't verify it either,
  and it adds a surface without adding security.
- A compromised successor could forge a report naming an offline Host's id, but it still needs an Owner to confirm
  the replacement in the UI, for that specific pair, while that Host is offline.
- Replacing is irreversible for the predecessor's Agent: if that machine comes back it needs a fresh enrollment.
- The inherited-id list lives until the Host is removed. Removing a Host also deletes the replacements recorded
  against it.
