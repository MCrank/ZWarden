# Issue #365 Mini-Plan — one operational Agent id (containers, logs and diagnostics carry the enrolled id)

**Status:** one PR (branch `fix/365-operational-agent-id`). Closes #365's id split. Re-enroll recovery (bringing a
previous Agent's containers back under management) is split out to #368, by decision (see D5).

**Written against:** issue #365; F8 (`IAgentIdentity`, the local identity file); F9 / ADR 0007 (the enrolled
`AgentTrustMaterial.AgentId` is assigned by the control plane); F13 / ADR 0008 (`ContainerOwnershipGuard` is the
container-level control); #363's "Removing a host" docs caveat.

## Objective

Once enrolled, an Agent uses **one** id everywhere an operator can see it: the `io.zwarden.agent-id` label it stamps
on new containers, its startup/stop logs and its diagnostics snapshot all carry the AgentId shown on its Hosts card.
Containers already stamped with the Agent's local (F8) id stay owned and manageable, since Docker labels can't be
changed in place (a Recreate re-stamps them).

## Facts found (2026-10-05)

- `IAgentIdentity.AgentId` is the local id from `agent-identity.json`. Read by `PzContainerFactory` (label),
  `ContainerOwnershipGuard` (both `EnsureOwnedByThisAgent` and `TryResolveOwned`, so every verb and discovery),
  `AgentWorker` (start/stop logs) and `AgentDiagnostics` (snapshot).
- The enrolled id lives only in `AgentTrustMaterial`, read from the trust store by the connection, health and
  metrics services. `AgentEnrollmentInitializer` is the one place that learns it: at startup (already enrolled), or
  on success, which can be **after** startup (background retry, #185).
- Containers are only ever created by a control-plane command, so a new container is always created while enrolled.
  Recreate goes through `PzContainerFactory`, so it re-stamps with the current id.
- Both ids are this Agent's own state (`agent_state` volume). Accepting the local id grants nothing a second Agent
  on the same daemon couldn't already label itself with, so it does not weaken ADR 0008's guard.

## Decisions

- **D1 — the identity carries both ids.** `IAgentIdentity` gains `LocalId` (the F8 file id) and `Owns(AgentId)`;
  `AgentId` becomes the operational id: the enrolled id once known, else the local id. `AgentIdentityHolder` keeps
  both in one immutable snapshot swapped atomically (`Set` for the local id at startup, `MarkEnrolled` for the
  enrolled id), so a background enrollment is safe to read from other threads.
- **D2 — enrollment publishes the enrolled id.** `AgentEnrollmentInitializer` calls `MarkEnrolled` when it finds
  existing trust material and when an exchange (inline or background) succeeds.
- **D3 — the guard owns both.** `ContainerOwnershipGuard` checks `_identity.Owns(owner)`: the enrolled id or the
  local id. `PzContainerFactory` stamps `_identity.AgentId`. Diagnostics and worker logs use `AgentId`.
- **D4 — trace both while they differ.** At start, when the enrolled id differs from the local id, the worker logs
  once that the Agent also owns containers stamped with its local id, so old logs stay traceable. The F8 store's
  messages say "local Agent identity".
- **D5 — re-enroll recovery is a separate issue (#368).** A re-enrolled machine's containers carry a previous Agent's id
  and the database still binds their Servers to the old Host. Bringing them back needs a guard crossing plus a
  "move servers to the replacement host" flow and its own threat read. The remote-agent docs caveat points to it.

## Tests (Agent.Tests)

- `AgentIdentityHolder`: before enrollment `AgentId` is the local id; after `MarkEnrolled` it is the enrolled id;
  `Owns` is true for both and false for any other id.
- `ContainerOwnershipGuard`: accepts the enrolled id; still accepts the legacy local id (verb + discovery); refuses
  a third id.
- `PzContainerFactory`: labels the container with the enrolled id once enrolled.
- `AgentDiagnostics`: the snapshot carries the enrolled id.
- `AgentEnrollmentInitializer`: marks the holder enrolled when already enrolled and after a successful (incl.
  background) exchange; leaves it on the local id when refused / un-enrolled.
- `AgentWorker`: logs both ids at start when they differ, only one when not enrolled.
- Re-enroll scenario: a holder enrolled under a new id with a fresh local id refuses a container stamped with the
  previous Agent's ids (documents today's boundary; recovery is the follow-up).
