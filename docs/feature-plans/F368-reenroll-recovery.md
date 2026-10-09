# Issue #368 Mini-Plan — Re-enroll recovery: "Replace host" brings a wiped machine's PZ containers back

**Status:** one PR (branch `feat/368-reenroll-recovery`, closes #368). Last open v1.0 issue.

**Written against:** issue #368 and its grilling comment (2026-10-09); #365 / F365 (`IAgentIdentity.Owns`, the
operational id); #363 / F363 (`AgentTrustService.RemoveAsync`, `RemoveHostDialog`, one `BbPortalHost` per page);
ADR 0007 (Agent credentials), ADR 0008 (the ownership guard is the real control), ADR 0020 (additive protocol).

## Objective

Wiping a machine's `agent_state` and enrolling it again creates a new Host whose Agent can't touch the old PZ
containers, because they are stamped with the old id. After this change:

- The new Agent **reports** those containers without operating on them.
- The Owner sees them on the new Host's card and clicks **Replace *old host*…**.
- That one audited step moves the old Host's Servers and their Backups to the new Host, revokes and removes the old
  Host, and tells the new Agent it now owns the old id. The Servers are managed again without a restart.

## Facts found (2026-10-09)

**Agent side**
- `ContainerRuntime.ListManagedAsync` filters every canonical container through
  `ContainerOwnershipGuard.TryResolveOwned`. A foreign-stamped container is dropped silently, so the control plane
  never learns it exists.
- `IAgentIdentity.Owns(id)` currently covers two ids: the enrolled id and the local id.
- `SignalRControlPlaneConnection.HelloAndSnapshotAsync` sends `Hello` and then the `AgentStateSnapshot`, on every
  (re)connect.
- Server→Agent pushes are named client methods: `ReceiveCommand`, `StartServerLogStream`, and others.

**Control-plane side**
- `AgentHub.StateSnapshot` → `ServerStateReconciler.ReconcileAsync` updates only Servers whose `AgentId` is the
  reporting Agent's, then records `IServerDiscoveryCache` (the in-memory, untrusted import picker).
- `Server.AgentId` is `init`, so no code path moves a Server today.
- `Backup.AgentId` is `init`, and backup delete and restore are dispatched to `backup.AgentId`
  (`ServerBackup`, `ServerRestore`). A moved Server's old Backups must move with it, or they're unreachable.
- `ControlPlaneSettings.DefaultDeployHost` may point at the old Host. #347 shows a warning once that Host is gone.

**Hosts page and removal**
- `AgentTrustService.RemoveAsync` (Tenant.Enrollment.Manage):
  - checks for Servers;
  - revokes and deletes the Agent;
  - writes the `Agent.Removed` audit;
  - drops the live connection.
- `/hosts` is static, and the Owner's islands share one `BbPortalHost`.

## Decisions

**D1 — Agent reports foreign containers.**
- Add `IContainerRuntime.ListForeignAsync`. It returns canonical containers whose labelled agent id this Agent
  doesn't own, as Docker short id, `ServerId`, labelled `AgentId` and state.
- Add `ContainerOwnershipGuard.TryResolveForeign` as the non-throwing sibling of `TryResolveOwned`. Nothing else
  changes in the guard.
- `AgentStateSnapshot` gains an optional, additive `Foreign` list of
  `ForeignContainer(ServerId, AgentId LabelledAgentId, ServerRunState RunState, string ContainerId)`, capped at 64
  entries. `null` from an older Agent.
- The Agent never issues a Docker verb against these containers.

**D2 — Control plane caches the report.**
- `IForeignContainerCache`: in memory, per Agent, replaced on each snapshot.
- It is untrusted: used to *offer* Replace and to *list* unmanaged containers, and re-checked server-side by
  Replace.

**D3 — Inherited ids are stored by ZWarden.**
- New entity `HostReplacement` (`hr-` id, tenant-owned): `SuccessorId`, `PredecessorId`, `ReplacedAt`,
  `ReplacedBy`.
- An Agent's inherited ids are the `PredecessorId`s where `SuccessorId` is that Agent.
- **Chain:** when C replaces B, rows whose successor is B are re-pointed to C, and a B→C row is added. C then
  inherits A and B.
- `RemoveAsync` of a Host (no Servers) deletes the rows where it is the successor.
- Migrations: `AddHostReplacements` for SQLite and Postgres.

**D4 — Domain rebinds.**
- `Server.AgentId` and `Backup.AgentId` become `private set`.
- Each gets `ReassignTo(AgentId)`, called only by the replacement service. An architecture test pins that.

**D5 — `IHostReplacementService.ReplaceAsync(actor, successorId, predecessorId)` → `HostReplacementResult`.**

Checks, in this order:
1. Tenant.Enrollment.Manage, else `AuthorizationDeniedException`.
2. Both Agents are in the tenant (not found → denied).
3. The successor and predecessor are different.
4. The successor is trusted.
5. The predecessor is **offline**: the registry has no live connection and `ConnectionState` is not Connected.
   Otherwise the result is `PredecessorOnline`.
6. The predecessor's id is in the successor's latest foreign report. Otherwise the result is `NotReported`.

Then in **one save**:
- move the predecessor's Servers and Backups;
- re-point and add the `HostReplacement` rows;
- re-point `DefaultDeployHost` if it was the predecessor;
- revoke and delete the predecessor.

After the save:
- Audit `Agent.Replaced`, target `agent <successor>`, detail
  `replaced <predecessor name> (agent <id>); N server(s), M backup(s) moved`.
- Drop any predecessor connection (defensive).
- Clear the successor's foreign cache.
- Push `OwnershipChanged` to the successor.

**D6 — Wire.**
- Hub method `InheritedAgentIds`: the Agent invokes it after `Hello` and before the snapshot. It returns the inherited
  ids for the **authenticated** Agent only, never a payload-named one.
- Client method `OwnershipChanged`: the Agent re-fetches its inherited ids and re-sends its snapshot. The reconciler
  then picks up the moved Servers.
- `AgentIdentityHolder.SetInherited(ids)`, and `Owns` includes them. They are held in memory only and never written
  to `agent_state`.
- A Web that predates this has no `InheritedAgentIds`: the Agent catches the `HubException` and uses an empty list.

**D7 — Hosts card UI.** The card shows the following when its Host's foreign report is non-empty.

- **Replace candidates:** a reported labelled id that is an **offline** Host in the tenant. The card reads
  "*N* container(s) from *old host* are on this machine". For the Owner it adds a `ReplaceHostDialog` island
  (`BbAlertDialog`) with these points:
  - Servers and backups move here.
  - *old host* is removed and its credential revoked.
  - Containers keep running; they pick up this Host's id the next time they're recreated.
  - Not reversible.
- **Read-only list:** the remaining entries, either an unknown id or a Host that is online. The card reads
  "*N* unmanaged ZWarden container(s)". Each row shows the short id, server id and state, and a copyable
  `docker rm -f <id>`. ZWarden never touches them.
- **Result handling:** `Replaced` → `Nav.Refresh()`. `PredecessorOnline` and `NotReported` show a message. Denied
  shows "You are not permitted to replace hosts."

**D8 — ADR 0049.** Crossing the ownership guard is allowed only through D5: Owner-confirmed, same tenant,
predecessor offline, predecessor id reported by the successor's own containers, inheritance held by the control plane.

**Docs.**
- `docs/deployment/remote-agent.md`: replace the "Removing a host" caveat with "Replacing a host after a wipe".
- `CONTEXT.md`: add `hr-` to the prefix table.

## Tests

**Agent**
- `ContainerRuntime.ListForeignAsync` returns only canonical foreign containers: owned, non-canonical and inherited
  ones are excluded.
- `AgentIdentityHolder` owns its inherited ids after `SetInherited`, and they are replaced on the next set.
- The snapshot carries `Foreign`, and a 65th entry is dropped.

**Domain**
- `Server.ReassignTo` and `Backup.ReassignTo`.

**Infrastructure** (`HostReplacementServiceTests`)
- Denied without the permission.
- `NotReported`, `PredecessorOnline`, self, and the other-tenant case, each leaving nothing changed.
- `Replaced` moves Servers and Backups, removes the predecessor (its credential no longer verifies), re-points the
  default deploy host, audits, and pushes `OwnershipChanged`.
- The chain A→B→C leaves C inheriting both.
- `RemoveAsync` cleans up the rows where the removed Host is the successor.
- Migration test.

**Web**
- Hub: `InheritedAgentIds` returns only the caller's ids.
- The snapshot records the foreign cache.
- bUnit `ReplaceHostDialog`.
- Hosts page: the candidate and unmanaged sections; Owner vs Administrator.

**Architecture**
- Only `HostReplacementService` calls `ReassignTo`.

**Live (DMZ)**
1. Deploy a server on host X.
2. `docker compose down -v` the Agent and enroll it again as Y.
3. Y's card offers Replace X.
4. Confirm: the server is managed under Y without a restart, its backups restore, and X is gone.
5. Recreate re-stamps the container with Y's id.
