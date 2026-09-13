# ZWarden.PZServer

The canonical managed Project Zomboid runtime container (`CONTEXT.md` → **ZWarden.PZServer**),
delivered by **Feature 12** ([mini-plan](../../docs/feature-plans/F12-canonical-pz-container-foundation.md)).
This is a container build context, not a .NET assembly — it is not part of `ZWarden.slnx`.

**No Project Zomboid artefact is committed here** (ADR 0009). The image ships SteamCMD; the ~6.72 GiB
server is installed at **runtime on first run** via anonymous SteamCMD. The build needs general
network (Debian packages + Valve's SteamCMD tarball) but never reaches Steam.

## Filesystem (`/pz`)

`-cachedir` relocates PZ's user data onto the persistent `/pz/data` volume, so the logical contract
(PRD 23) and PZ's own on-disk names sit together:

| Logical (`/pz`) | Holds | PZ's own name under it |
| --- | --- | --- |
| `server/` | the SteamCMD install of app 380870 | — |
| `runtime/` | SteamCMD, the Steam root, the `zomboid.control` FIFO | `steamapps/…` |
| `data/` (volume) | config, saves, logs, player db | `Server/`, `Saves/`, `Logs/`, `db/` |
| `data/workshop` | symlink → the Workshop cache under the Steam root | `steamapps/workshop/content/108600/` |

## Environment

| Variable | Default | Meaning |
| --- | --- | --- |
| `ZW_PZ_BETA` | *(empty)* | Steam branch; empty = `public` (42.20.x). e.g. `legacy41`, `42.19`. |
| `ZW_PZ_XMS` / `ZW_PZ_XMX` | `4g` / `4g` | JVM heap (the shipped 16 GB default is overridden). |
| `ZW_PZ_STOP_GRACE` | `30` | Seconds between `save` and `quit` on stop. Match with `docker stop -t`. |
| `ZW_PZ_SERVERNAME` | `servertest` | The `-servername` config set. |

## Ports

`16261/udp` (game + Steam queries) and `16262/udp` (direct connection) are exposed. RCON `27015/tcp`
is **private** and owned by the Agent (F18, PRD 29) — it is deliberately not exposed.

## Build and run by hand

```bash
docker build -t zwarden-pzserver src/ZWarden.PZServer

# First run installs the server from Steam (network) - expect a multi-minute cold start.
docker run -d --name pz -p 16261:16261/udp -p 16262:16262/udp \
  -v pzdata:/pz/data -e ZW_PZ_XMX=6g zwarden-pzserver

docker inspect --format '{{.State.Health.Status}}' pz   # -> healthy once the JVM is up
docker stop -t 60 pz                                     # graceful: save, grace, quit
```

## Stop path

`docker stop` sends SIGTERM; the developers discourage a bare SIGTERM to the JVM. The entrypoint
traps it and performs PZ's blessed sequence — `save`, wait `ZW_PZ_STOP_GRACE`, `quit` — over the
stdin FIFO. The base `HEALTHCHECK` is shallow (the `GameServer` JVM is alive); Feature 16 layers the
hierarchical health model on top.

When ZWarden drives this (Feature 15), the Agent issues that `docker stop`/`docker restart` with
`WaitBeforeKillSeconds = Agent:StopTimeoutSeconds` (default **120**), sized above `ZW_PZ_STOP_GRACE`
so the FIFO `save`→`quit` finishes before Docker's SIGKILL — the socket allowlist (ADR 0008) permits
`stop`/`restart` but not `exec`/`attach`, so the trap, not a direct FIFO write, is how the blessed
shutdown is reached. Operators with very large worlds should raise both grace and stop timeout together.

## Update path (Feature 17)

An **update** re-runs the same anonymous `app_update 380870 validate` as the first-run install. The
socket allowlist (ADR 0008) denies `exec`/`attach`, so the Agent cannot run SteamCMD in a live
container; instead it drops a control-file into the writable data volume and restarts:

1. The Agent writes `/pz/data/.zwarden-update-requested` containing the **OperationId**, then issues
   `docker restart` (which runs the blessed `save`→`quit` stop first).
2. On reboot the entrypoint sees the request and runs `app_update … validate` **past** the install
   marker, bracketing the SteamCMD output with `steamcmd update session <OperationId> begin` … `end
   (success|failure)` so the Agent can bound its `docker logs` parse to this update.
3. The control-file is **always cleared** afterwards (success or failure) so a persisted request can
   never drive a restart loop.
4. A **failed** update is non-fatal — unlike a first-run install it does not `exit 1`; the existing
   install is intact, so the server boots on it and the Operation reports the failure from the log.

The Agent-side orchestration (persistent install/Steam-root mounts, log-parsed progress, version
detection) lands with F17's later PRs; this container-side contract is the half that lives here.

## Tests

`tests/pzserver/*.bats` unit-test the entrypoint shell functions against synthetic fixtures with no
network (`bats tests/pzserver`). The real SteamCMD install + boot runs on a scheduled CI tier.
