# Feature 7 Mini-Plan — Agent Protocol Contracts

**Status:** ready for implementation. Roadmap issue: [F7 (#29)](https://github.com/MCrank/ZWarden/issues/29). Track C.

**Format:** PRD 60. **Written against:** PRD §16 (Web ↔ Agent protocol), §18 (Agent protocol messages), §40 (heartbeats), Feature 7 (§ "Agent Protocol Contracts"); [`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (Track C, F7); [`trust-boundaries.md`](../trust-boundaries.md) §3 (the Web ↔ Agent boundary) and §9 rule 3 (no free-form command string); and the F1 typed-ID foundation ([ADR 0014](../adr/0014-typed-id-pattern.md)) it consumes. The two load-bearing decisions below were settled in the F7 mini-plan grilling.

## Objective

Deliver the **versioned, strongly-typed protocol vocabulary** that ZWarden.Web and ZWarden.Agent both speak — message **envelopes**, the **closed** command/event hierarchies, the **lifecycle** messages (hello, heartbeat, state snapshot, operation progress/completed), one **canonical serializer**, and the **version-compatibility rule** — living in `ZWarden.Contracts` with no runtime implementation dependency in either direction, so F8 (Agent skeleton) and F10 (SignalR transport) build on a shared contract that cannot carry a free-form command.

## The two settled decisions

1. **Versioning — a single monotonic integer `ProtocolVersion`, checked against a Web-declared range (grilling Q1).** Every envelope carries an `int` protocol version. Web declares a `MinSupported..Current` range; the Agent presents its version in `AgentHello`; a pure `ProtocolCompatibility.IsCompatible` gate accepts iff `agentVersion` is in range and rejects cleanly otherwise. Chosen over SemVer and capability-flags because v1.0 is a **closed, first-party** system — the same Web+Agent binaries ship together, and F35 multi-host is still those binaries on more machines, not third-party peers. Not a one-way door: minor-version tolerance can be added later if a real need appears. Recorded as **ADR 0020**.
2. **Catalogue scope — envelope + lifecycle only; concrete domain commands/events land with their owning feature (grilling Q2).** F7 ships the envelope, the **sealed** `AgentCommand` / `AgentEvent` base hierarchies (the closed-vocabulary guarantee, arch rule 3), and the protocol-lifecycle messages. `RestartServer` (F15), `KickPlayer` (F19), `LogEntry` (F27), `ServerStateChanged`/`HealthChanged` (F16) and the rest are added by their owning feature onto the sealed base, when that feature knows the message's real shape. Chosen because a wire contract guessed ahead of its feature is expensive to unwind, and F7's real job is the envelope + the sealed guarantee that makes every later message drop in safely.

## Dependencies

**F1** (typed IDs) and F0 only. Consumes:

- PRD §18 — the representative messages and the metadata set (`ProtocolVersion`, `MessageId`, `AgentId`, `ServerId`, `OperationId`, `Timestamp`); "contracts shall be strongly typed"; "arbitrary remote shell execution shall not exist".
- PRD §16 / §40 — outbound Agent→Web direction; periodic heartbeat/state carrying the protocol version; an authoritative state snapshot after reconnect.
- **F1 typed IDs** — `AgentId`, `ServerId`, `OperationId` (and their canonical-string JSON via `TypedIdJsonConverterFactory`) from `ZWarden.Domain`. F7 therefore adds a **`ZWarden.Contracts → ZWarden.Domain`** project reference. Domain is infrastructure-free (arch rule 2), so `ZWarden.Agent → ZWarden.Contracts → ZWarden.Domain` stays clean — the Agent gains no persistence, EF or Docker reference.
- `trust-boundaries.md` §3 — the boundary crosses commands from a **closed vocabulary**, each carrying an `OperationId`; heartbeats, state snapshots, operation progress and health flow **upward**; **an arbitrary command string never crosses** (§9 rule 3).

## Scope

1. **Metadata + envelope (PRD 18).** An `Envelope<TPayload>` (generic, strongly typed) carrying the PRD §18 metadata — `ProtocolVersion` (int), `MessageId` (a new `MessageId` typed value over UUIDv7), `Timestamp` (`DateTimeOffset`, UTC) — plus the optional routing ids `AgentId`, `ServerId`, `OperationId` where the payload warrants them. A stable string **`MessageType` discriminator** identifies the payload on the wire so a receiver can deserialize without knowing `TPayload` in advance.
2. **The closed hierarchies (arch rule 3).** Two abstract, closed roots: **`AgentCommand`** (Web→Agent, the mutating vocabulary) and **`AgentEvent`** (Agent→Web reports). Both are `abstract` with an `internal`-guarded/`sealed`-leaf discipline so the set is closed; concrete leaves are `sealed`. **No member on either root, or any leaf, is a free-form command / script / shell / exec string** — enforced by a new architecture test, not merely by review.
3. **Protocol version + compatibility rule (Decision 1).** `ProtocolVersion.Current` (const int, = 1) and a `ProtocolVersionRange(MinSupported, Current)`; a pure `ProtocolCompatibility.IsCompatible(int agentVersion, ProtocolVersionRange range)`; a strongly-typed **`ProtocolNegotiationResult`** (accepted, or rejected with the actionable reason: below-min / above-current). F7 owns the *rule*; F10 performs the *negotiation* at connect time.
4. **Lifecycle messages (Decision 2), the only concrete payloads in F7:**
   - **`AgentHello`** — first message on connect: the Agent's `AgentId`, its `ProtocolVersion`, and the minimal identity/capability preamble F10 needs to negotiate.
   - **`AgentHeartbeat`** — periodic liveness carrying the protocol version and the Agent's coarse health (PRD §40).
   - **`AgentStateSnapshot`** — the authoritative post-reconnect snapshot (PRD §40): the Agent's observed per-Server state as an opaque, closed enum set, never a state Web *inferred* (trust-boundaries §3).
   - **`OperationProgress`** — an `OperationId`, a monotonic progress reading, and an optional untrusted status line.
   - **`OperationCompleted`** — an `OperationId` and a terminal outcome (succeeded / failed with a reason code), the record F11 later turns into an operation transition.
5. **The canonical serializer.** One `ProtocolJson` exposing the single `JsonSerializerOptions` both sides use: reflection-based `System.Text.Json` (matching the repo — the typed-ID converter is a runtime `JsonConverterFactory`; **no source-gen**), registering `TypedIdJsonConverterFactory`, enums-as-strings, UTC round-trip, and **unknown-member tolerance** (additive forward-compatibility). Envelope (de)serialization dispatches on `MessageType`; the concrete-type registry is **open for later features to extend** without the base referencing its leaves.
6. **Compatibility rules, documented and tested.** The additive-vs-breaking rule: additive changes (new optional member, new leaf) may reuse the current `ProtocolVersion`; any change that an old peer would mis-read bumps it. Serialization tests pin both halves — an added unknown field is tolerated; a round-trip is identity.
7. **Serialization + compatibility tests** in a new **`ZWarden.Contracts.Tests`**, and the **closed-vocabulary architecture test** in `ZWarden.ArchitectureTests`.

## Non-scope

- **Transport (F10):** SignalR, the hub, the outbound connection, reconnect, and the *act* of negotiating versions at connect time. F7 provides the messages and the compatibility *function*; F10 calls it.
- **Concrete domain commands/events (their owning features):** `RestartServer`/lifecycle (F15), `KickPlayer`/player events (F19), `LogEntry` (F27), `ServerStateChanged`/`HealthChanged` (F16), workshop/mod commands (F21/F22). F7 fixes the *base* they attach to, not the leaves.
- **Operation semantics (F11):** the operation queue, state machine, idempotency store, per-Server locking. F7 defines the *progress/completed message shape*; F11 gives it behaviour.
- **Agent identity, enrollment, credentials (F8/F9):** F7's `AgentHello` names the `AgentId`; it does not authenticate it.
- **Persistence / EF anything** — Contracts is a pure library; it never references Infrastructure or a provider.

## Domain changes

- **New typed id — `MessageId` (`msg-`)** in `ZWarden.Domain.Ids`, added to the closed prefix registry (the reflection registry test's expected set grows from 19 to 20) and to the `CONTEXT.md` prefix table. A message's identity is a typed UUIDv7 like every other identifier — never a bare GUID on the wire (PRD §6).
- No other domain entity or service — the protocol payloads live in `ZWarden.Contracts`, not Domain.

## Contract changes

- **This feature *is* the Agent contract surface.** After F7, `ZWarden.Contracts` exposes: `Envelope<T>`, the `AgentCommand`/`AgentEvent` closed roots, the five lifecycle messages, `ProtocolVersion`/`ProtocolVersionRange`/`ProtocolCompatibility`/`ProtocolNegotiationResult`, and `ProtocolJson`.
- **The canonical string is the wire form** for every id (typed-ID converter, PRD §6); enums serialize as stable names; timestamps are UTC ISO-8601.
- **The closed-vocabulary invariant is a contract, enforced by test:** no Agent-facing type carries a free-form command/script/shell/exec string (trust-boundaries §9 rule 3).
- **The versioning contract:** `ProtocolVersion.Current = 1`; the additive-vs-breaking rule governs when it bumps.

## Security considerations

- **No arbitrary command can cross the boundary — structurally.** Commands are a closed type hierarchy with no free-form command field, and the architecture test fails the build if one is introduced under any name (PRD §18, trust-boundaries §9 rule 3). `ExecuteShellCommand(string)` cannot exist.
- **Untrusted-data honesty (trust-boundaries §8).** Payload fields that carry Agent/PZ-originated text (a heartbeat/progress status line, snapshot detail) are typed and documented as **untrusted, opaque data** — never a markup or identifier type — so no downstream reader is misled into trusting them. F7 does not sanitize; it labels.
- **State is observed, never inferred (trust-boundaries §3).** `AgentStateSnapshot`/`OperationCompleted` model what the Agent *reports*; the contract carries no field by which Web could assert a transition a command's mere acceptance implied.
- **Idempotency currency present (trust-boundaries §3 / PRD 20).** Operation messages carry an `OperationId`; `MessageId` gives every envelope a dedup key. F7 supplies the identifiers; F11 enforces the semantics.
- **No secret-aware types cross here.** The RCON password (trust-boundaries §5/§9 rule 7) has no home in any Contracts type — it stays inside the Agent.

## Test plan

Written before the code (PRD 2.2), in a new `ZWarden.Contracts.Tests` unless noted.

1. **Envelope round-trips** — every lifecycle message wrapped in `Envelope<T>` serializes and deserializes to an equal value; metadata (`ProtocolVersion`, `MessageId`, `Timestamp`, and the present routing ids) survives; timestamps stay UTC.
2. **Typed ids are canonical on the wire** — `AgentId`/`ServerId`/`OperationId`/`MessageId` serialize to `<prefix>-<uuid>`; a raw GUID string without the prefix fails to deserialize (via the F1 converter).
3. **`MessageType` dispatch** — deserializing an envelope selects the correct concrete payload from the discriminator; an unknown discriminator fails with an actionable `JsonException` naming the offending type.
4. **Forward-compatibility (additive rule)** — JSON with an unknown extra member deserializes without error (tolerated); a round-trip of a known message is identity. Together these pin the additive-vs-breaking contract.
5. **Compatibility gate (pure)** — `IsCompatible` accepts an in-range agent version and rejects below-min and above-current; `ProtocolNegotiationResult` carries the specific reason. Table-driven over the boundary values.
6. **Closed vocabulary — no free-form command string (architecture test, in `ZWarden.ArchitectureTests`).** Reflect over `ZWarden.Contracts`: every concrete `AgentCommand`/`AgentEvent` leaf is `sealed`; no property across the command/event surface is a free-form command/script/shell/exec string. The test names the offending type + member on failure.
7. **Prefix registry still exact** (`ZWarden.Domain.Tests`) — the reflection registry now enumerates exactly 20 ids including `msg-`; the set and `CONTEXT.md` agree.
8. **Enum stability** — the closed state/outcome enums serialize as their names (not ordinals), so an added enum member is additive rather than a silent renumber.

## Implementation slices

- **S1 — `MessageId` + metadata + envelope.** Add the `msg-` typed id (registry test to 20, `CONTEXT.md`); `Envelope<T>` with the PRD §18 metadata; wire `ZWarden.Contracts → ZWarden.Domain`. TDD. *Verify:* tests 1, 2, 7 green for a trivial payload.
- **S2 — The closed roots + `ProtocolJson` + dispatch.** `AgentCommand`/`AgentEvent` sealed hierarchy; `ProtocolJson.Options`; `MessageType` discriminator + the extension-open registry. *Verify:* tests 3, 4, 8; the closed-vocabulary arch test (6) green against an empty-but-sealed hierarchy.
- **S3 — The five lifecycle messages.** `AgentHello`, `AgentHeartbeat`, `AgentStateSnapshot`, `OperationProgress`, `OperationCompleted`, each `sealed`, TDD. *Verify:* tests 1–4 green for all five.
- **S4 — Version + compatibility rule.** `ProtocolVersion`, `ProtocolVersionRange`, `ProtocolCompatibility.IsCompatible`, `ProtocolNegotiationResult`. *Verify:* test 5.
- **S5 — ADR + docs + arch wiring.** ADR 0020; `CONTEXT.md` (Protocol / Envelope-in-the-Agent-sense terms + `msg-`); confirm the closed-vocabulary arch test is in the always-on tier. *Verify:* full offline tier green; arch tests green.

## Diagnostics

- **Negotiation rejection is actionable** — `ProtocolNegotiationResult` states the Agent version, the accepted range, and *which* bound failed, so F10 can log "Agent protocol 0 below minimum 1" rather than a bare mismatch.
- **Deserialization failures name the message** — an unknown `MessageType` or a malformed id throws a `JsonException` naming the discriminator/member, never a bare "invalid JSON".
- **The closed-vocabulary arch test names the offender** — a newly introduced free-form command member fails the build with the type + property, so the security invariant fails loudly at the point of violation.

## Documentation

- **ADR 0020 — Agent protocol versioning and compatibility.** Records Decision 1 (single-int + range over SemVer/capabilities, with the closed-first-party reasoning and the "not a one-way door" note) and Decision 2 (envelope + lifecycle only; leaves grow with their feature). A future reader will otherwise wonder why not SemVer and why the catalogue looks sparse.
- **`CONTEXT.md`** — add **Protocol message** / **Envelope (Agent-protocol sense)** terms if they clarify vocabulary without overlapping the existing security "Envelope"; add `msg-` to the prefix table. (Guard against colliding with the crypto **Envelope** term already defined — name carefully or scope the term.)
- **Code XML docs** on the closed roots explaining how a later feature adds a command/event leaf (declare a `sealed` type, give it a `MessageType`, register it — the arch + registry tests enforce the rules), mirroring the F1 "how to add an id" doc.

## Acceptance criteria

1. `ZWarden.Contracts` exposes `Envelope<T>`, the sealed `AgentCommand`/`AgentEvent` roots, the five lifecycle messages, the version/compatibility types, and `ProtocolJson` — strongly typed, with **no runtime implementation dependency** either way (references only `ZWarden.Domain`).
2. Every message round-trips through `ProtocolJson`; typed ids appear only in canonical `<prefix>-<uuid>` form; unknown members are tolerated (additive forward-compat) and a round-trip is identity.
3. `ProtocolCompatibility.IsCompatible` implements the single-int range rule; `ProtocolNegotiationResult` carries an actionable reason for every rejection.
4. The closed-vocabulary architecture test passes and **fails the build** if any Agent-facing contract gains a free-form command/script/shell string (trust-boundaries §9 rule 3).
5. The prefix registry is exactly 20 ids including `msg-`; `CONTEXT.md` and the registry test agree.
6. ADR 0020 is written; the always-on offline CI tier is green with no warnings.

## Definition of Done

Per PRD 61, the applicable subset: acceptance criteria met; tests authored first as executable specifications; unit + serialization tests pass; **architecture rules pass** (Contracts references only Domain; the closed-vocabulary rule 3 test is green and load-bearing; Domain stays infra-free; Agent gains no persistence via the new transitive reference); error conditions modelled (negotiation rejection and deserialization failures are typed and actionable); diagnostics exist (named negotiation/deserialization/arch failures); ADR 0020 and `CONTEXT.md` updated; CI green; no unresolved warnings. (Migrations, DB tests, authorization, audit, UI are N/A at F7 — a pure contracts library with no persistence, no transport and no user surface.)
