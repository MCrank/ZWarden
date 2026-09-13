# 26. RCON is reached over the private container network with an Agent-owned, INI-sourced credential

ZWarden speaks to a running Project Zomboid server over **Source RCON**, and F18 fixes how. **The RCON
client lives in `ZWarden.Rcon`, an Agent-only assembly Web cannot reference; the Agent reaches the
listener over the private ZWarden Docker network (port 27015, never host-published); the password is
generated and owned by the Agent, written into and read back from the server's `servertest.ini` on the
Agent-owned data mount, and never crosses to `ZWarden.Web`.** The client survives PZ's four measured
protocol traps by design, and RCON health is an on-demand, additive diagnostics command — not a fifth
standing health probe.

- Status: accepted
- Decided in: #39 (F18; five load-bearing decisions D-1…D-5 settled before implementation)
- Bears on: PRD 29 (RCON credential ownership), PRD 30; trust-boundaries.md §5, §8, §9 rule 7; ADR
  [0008](./0008-docker-socket-access-via-wollomatic-socket-proxy.md) (the allowlist), ADR
  [0020](./0020-agent-protocol-versioning-and-catalogue.md) (additive change), ADR
  [0023](./0023-server-health-model-and-observed-delivery.md) (the closed health shape)

## Context

PZ's RCON is Source RCON reimplemented in Java, and its behaviour was measured against Build 42.20.2
(`docs/research/project-zomboid-runtime.md` §7). Four traps make a naïve client wrong: it emits **no
end-of-response terminator**; an **empty result sends no packet at all**; the server **caps connections
at five** (the sixth is accepted then instantly closed); and it **serialises every command on the main
tick** with no pipelining. RCON is also security-relevant on two sides: the password is a credential that
must never reach the browser (PRD 29; trust-boundaries.md §5, un-hedged), and everything the runtime
emits over RCON is untrusted (§8). Finally, RCON is deliberately never host-published — the container
publishes only its two UDP ports (F12/F13) — so the listener is reachable only from inside the ZWarden
network.

## Decision

- **Transport.** The Agent connects to the container's address on the **ZWarden network** at TCP
  **27015**, resolved from Docker inspect (`GET /containers/{id}/json` — already allowlisted; **no new
  socket verb**, ADR 0008 untouched). RCON is never host-published, and the container is never started
  with `-Drconlo` (which would bind the listener to container loopback and lock the Agent out).
- **Credential ownership.** The Agent **generates a strong, INI-safe password** (CSPRNG, `[A-Za-z0-9]`,
  32 chars) **once at provision** and writes `RCONPort=27015` + `RCONPassword=` into
  `<DataMountRoot>/<serverId>/Server/servertest.ini` **host-side** before the container first launches
  (PZ reads an existing INI and fills only the keys it is missing, so the seed survives). The INI is the
  **single source of truth**: the Agent re-reads the password from it to authenticate. Seeding is
  **idempotent** — an existing password is kept, never regenerated (PZ captures it as `final` at init,
  so a mid-life change is inert anyway).
- **Assembly boundary.** The RCON client and the `RconEndpoint`/`SecretString` credential type live only
  in `ZWarden.Rcon`, referenced by `ZWarden.Agent` alone. A PRD-15 architecture test fails the build if
  `ZWarden.Web`'s transitive reference closure ever includes `ZWarden.Rcon` (trust-boundaries.md §9
  rule 7).
- **The four traps.** End-of-response is detected by a chunk shorter than PZ's 4086-byte split, backed by
  a **read-until-idle** window (default 250 ms) under an overall command timeout (default 5 s); an empty
  result resolves to an empty string within that window — never a hang, never a fault. One **long-lived
  pooled socket per server** with commands **serialised** under a gate respects the five-connection cap
  and tick-serialisation; a dropped socket reconnects lazily, only when found dead **before** a command
  is sent, so a command is never silently run twice. An **EOF is a lost connection, not an empty
  result**.
- **Health probe.** RCON health is an **on-demand** `diagnostics.rcon-health` `AgentCommand` (per-server;
  the target rides the envelope), reporting an **additive** optional `RconHealthResult`
  (Reachable/Authenticated/Detail) on `OperationCompleted`. `ProtocolVersion.Current` stays **1**. It is
  **not** a fifth probe in ADR 0023's closed `HealthBreakdown` tuple.

## Alternatives considered

- **Deliver the password over the control plane, or store it server-side (encrypted with
  `ISecretProtector`).** Rejected: PRD 29 and trust-boundaries.md §5 require the Agent to own it and Web
  never to see it. Keeping it Agent-local makes "the browser never receives RCON credential material"
  true by construction rather than by discipline. The accepted cost is stated below.
- **Set the password via a container env var (`ZW_PZ_RCON_PASSWORD`).** Simpler for first-run bootstrap,
  but it **exposes the secret in `docker inspect`** (`Config.Env`, which the allowlist permits reading).
  The host-side surgical INI write (trust-boundaries.md §5 permits "surgical writes under `/pz/`") keeps
  it out of the inspectable env.
- **A separate Agent-local secret store for the password (mirroring the enrollment trust store).**
  Rejected as a second source of truth that can drift; trust-boundaries.md §7 does not enumerate the RCON
  password among the Agent's local-store contents, and §5 already says an operator can read or reset it in
  the config file. The INI is authoritative.
- **A fifth standing RCON probe folded into `HealthBreakdown`.** Rejected: it would bump the protocol
  version and make the health monitor poll RCON continuously, competing with operators for the five-slot
  cap and the tick queue. On-demand keeps RCON quiet until asked.
- **Valve's end-of-response sentinel / the Koraktor multi-packet probe.** Unusable — PZ emits no
  terminator and mishandles the Koraktor probe (§7 quirks 2, 4).

## Consequences

- Servers provisioned before F18 gain RCON only on **re-provision** (they were created without the seed);
  acceptable pre-1.0, and a start-path ensure can cover them later.
- **If an Agent host is lost, RCON access to its servers is not recoverable from the control plane** —
  the credential lived only on that host. An operator with host access can always read or reset it in the
  config file. This cost is accepted deliberately (trust-boundaries.md §5).
- The end-of-response idle window is a tunable that trades latency against the risk of clipping a very
  slow first response; 250 ms sits well above PZ's tick + 50 ms poll. The empty-result quirk is
  Medium-confidence (a read of the loop, not a packet capture), so the real behaviour is validated in the
  networked integration tier against a live server.
- All RCON output is treated as untrusted end to end (§8): the client bounds frame sizes and never
  interprets response text; escaping happens at render (F28).
- Command construction and quoting (§7 quirk 10) are **not** in `ZWarden.Rcon` — it sends raw command
  text; the admin command surface and its quoting are F19/F28.
