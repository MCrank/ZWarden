# Issue #337 Mini-Plan — Players column shows current / max

**Status:** one PR (branch `feat/337-players-of-max`, closes #337). Part of epic #348 (Fleet polish); the Players
part of #169.

**Written against:** issue #337 (decision: the Agent reads `MaxPlayers` from `servertest.ini`); ADR 0020 (additive
protocol changes); `ServerMetricsSampler`, `ServerMetricsSample`, `FleetFacts`, `ServerEndpoints` status poll,
`live-status.js`.

## Objective

The Fleet Players cell reads `2 / 16`: the connected count over the server's configured `MaxPlayers`, kept current by
the existing metrics report and status poll.

## Facts found (2026-10-04)

- The metrics sample already carries the #257/#262 fleet facts as trailing nullable members; one more is additive
  (ADR 0020), and `MetricsMessagesTests` shows the older-sample pattern.
- The live ini lives at `<DataMountRoot>/<serverId>/Server/servertest.ini`; `ServerModConfigReader` reads it through
  `IPzConfigParser`. INI values parse as `PzString`.
- `InitialSettingsRules` already bounds `MaxPlayers` to 1–254. PZ writes `MaxPlayers=32` by default.
- `FleetFacts.For` feeds both the first render and `/api/servers/status`; the poll sends uptime and the sample age
  preformatted, and `live-status.js` writes the Players cell from `players`.
- The player count is only sampled while running, so a stopped server shows `—` today.

## Decisions

- **D1 — Agent reader.** `ServerMaxPlayersReader` (`IServerMaxPlayers`) reads `MaxPlayers` from the live ini,
  cached per Server by the file's last-write time and length, so a tick re-parses only after an edit. No ini yet →
  `null` (unknown until first boot); a key that is missing → 32 (PZ's default); unparseable or outside 1–254 →
  `null`. Read for every owned container, running or not, so a stopped server keeps its denominator, and a config
  edit shows on the next sample (before a restart applies it — accepted in the issue).
- **D2 — wire + cache.** `ServerMetricsSample.MaxPlayers` and `ServerMetrics.MaxPlayers` (trailing `int? = null`).
  The hub drops a value outside 1–254 (untrusted input).
- **D3 — facts.** `FleetServerFacts.MaxPlayers` from the cached sample (config, so it's shown even while the Agent is
  offline). A **stopped** server on a connected Agent counts as `0` players. `FleetFacts.FormatPlayers(players, max)`:
  `2 / 16`, `— / 16` when the count is unknown, the count alone (or `—`) with no max.
- **D4 — render + poll.** `FleetRow.MaxPlayers`; the board renders `FormatPlayers`. The status poll adds
  `maxPlayers` and a preformatted `playersText`; `live-status.js` writes `playersText` (fallback: the bare count).
  The cell stays muted while the count is unknown.
- **D5 — KPI capacity: not done.** The prototype's tile says "across the fleet"; a capacity line adds JS + noise for
  little value. Can follow later.
- **D6 — tests.** Agent: reader (value, default, absent, malformed, out-of-range, cache until the file changes) and
  the sampler (stopped container carries max). Contracts: round-trip + older-sample nulls. Web: `FleetFacts`
  (format matrix, stopped → 0), hub ingest bounds, status poll `playersText`/`maxPlayers`, fleet page cell.

## Not done here

- The KPI capacity line (D5); the server detail page's player panel (#169's other parts).
