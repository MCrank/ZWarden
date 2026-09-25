# 45. Recreate removes a stopped container by its canonical name only

**The socket proxy admits `DELETE` for exactly one path shape — `/containers/srv-<uuid>`, a canonical
ZWarden container addressed by its ServerId name — and the Agent's one remove verb is never forced, never
removes volumes, requires the container to be owned (re-asserted on inspect), stopped, and named by its
ServerId.** This is the removal half of the **Recreate** Operation (#229): safe stop → remove → create from the
same closed template → start, which is how host ports change now and how the memory limit (#230) and the PZ
branch (#258) change later. The world, config and installed PZ build survive because they live in the
ServerId-derived host bind mounts, not in the container.

- Status: accepted
- Decided in: [#229](https://github.com/MCrank/ZWarden/issues/229) (maintainer decision D1, 2026-09-25)
- Bears on: [ADR 0008](./0008-docker-socket-access-via-wollomatic-socket-proxy.md) (amends its allowlist, which
  explicitly denied `DELETE /containers/{id}`), [ADR 0022](./0022-operation-lifecycle-and-per-server-locking.md)
  (Recreate is a mutating per-server Operation), `docs/trust-boundaries.md` §1/§4, F14 D2 (the ServerId, not the
  container id, is the durable key "because the container id changes on recreate")

## Context

Docker cannot change a container's port bindings or memory limit in place in any way the allowlist permits
(`/update` cannot touch port bindings and is denied anyway; `/rename` is denied). Changing either means
creating a new container, and because the container name *is* the ServerId, the old one must go first.
ADR 0008 denied `DELETE /containers/{id}` deliberately: the proxy's value is **bug-containment** — an Agent
bug whose label check matches one container too many becomes an HTTP 403 instead of a destroyed foreign
container. An unscoped `-allowDELETE` would give that up for removal entirely.

wollomatic's allowlist is a per-method regex, **auto-anchored and matched against the path only** — the query
string is excluded (`docs/research/docker-socket-proxy.md` §3). So the proxy can constrain *which path* a
DELETE targets but not its `?force=` / `?v=` flags.

## Decision

1. **Proxy:** `-allowDELETE=(/v1\.[0-9]+)?/containers/srv-[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}`,
   in every allowlist copy (reference compose, remote-agent compose, the dev AppHost) and in the tier-2 drift
   test. A container addressed by its 64-hex id, or any container whose name is not `srv-<uuid>`, is a path miss
   → **403**. The drift test proves all three: canonical name passes, hex id 403, foreign name 403.
2. **Agent (`IDockerEngine.RemoveAsync` / `IContainerRuntime.RemoveAsync(ServerId)`):** resolve the owned
   container, **re-assert ownership on inspect**, refuse unless it is stopped and its inspect name equals the
   ServerId, then `DELETE` **by name** with `force=false`, `v=false`. An architecture test forbids
   `Force = true` / `RemoveVolumes = true` anywhere in `src/` — the flags the proxy cannot see are an Agent-side
   guarantee.
3. **Recreate** (the only caller) additionally refuses a container whose `/pz/data` / `/pz/server` bind sources
   are not the ServerId-derived ones, so an imported container with other mounts is never removed out from
   under its world; and rolls back to the previous spec if the new container cannot be created or started.

## Alternatives considered

- **Unscoped `-allowDELETE=/containers/[a-zA-Z0-9_.-]+`.** Simpler, but rests removal entirely on the Agent's
  label check — exactly the bug class ADR 0008 kept the proxy for. Rejected.
- **Rename aside, then create.** Keeps the old container as a rollback, but needs `/rename` (also denied, and a
  broader verb), and leaves stale containers that still need a delete eventually. Rejected.
- **No Recreate; tell operators to recreate by hand.** Leaves port and memory changes outside ZWarden's audit and
  lock, and invites the world-losing mistakes (`docker rm -v`, wrong mount) the guided path prevents. Rejected.

## Consequences

- The proxy's bug-containment for removal is **narrowed, not lost**: a wrong-container bug can now delete another
  *canonical* container — another ZWarden server on the same daemon, possibly another Agent's — if the Agent's
  ownership re-check also fails. Non-ZWarden containers stay unreachable. Against a *compromised* Agent the
  proxy bought nothing before (ADR 0008: create is already a host escape) and still buys nothing.
- `?force=true` is not blockable at the proxy; its absence is enforced only by the Agent (and its arch test).
  Docker still refuses a non-forced delete of a running container, which is a second, daemon-side check.
- Recreate builds from the *current* closed template, pinned image and Agent memory options, so it also rolls
  a server onto an upgraded PZServer image. That is intended; anything in the container's writable layers or
  tmpfs is lost, which is already true by design (read-only rootfs, ADR 0008 amendment #184).
- The ServerId-named container is now load-bearing for removal: a canonical container not named by its ServerId
  (none are created that way) cannot be removed or recreated.
