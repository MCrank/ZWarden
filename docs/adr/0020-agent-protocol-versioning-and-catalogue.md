# 20. The Agent protocol is versioned by a single integer, and F7 ships the envelope and lifecycle messages only

**The Web ↔ Agent protocol (`ZWarden.Contracts`) carries a single monotonic integer
`ProtocolVersion` on every envelope, and a connection is accepted iff the Agent's version falls
within a Web-declared `MinSupported..Current` range, inclusive.** F7 delivers the envelope, the
**closed** `AgentCommand`/`AgentEvent` roots, the five protocol-lifecycle messages (`AgentHello`,
`AgentHeartbeat`, `AgentStateSnapshot`, `OperationProgress`, `OperationCompleted`), one canonical
`ProtocolJson` serializer, and the pure compatibility rule — **nothing else**. Concrete domain
commands and events are added onto the sealed roots by the feature that owns them, when that feature
knows their real shape.

- Status: accepted
- Decided in: #29 (F7 — Agent Protocol Contracts); mini-plan grilling
- Bears on: PRD 16 (Web ↔ Agent protocol), PRD 18 (protocol messages + metadata), PRD 40
  (heartbeats); [`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (Track C);
  [`trust-boundaries.md`](../trust-boundaries.md) §3 and §9 rule 3; ADR 0014 (typed-id pattern)

## Context

F7 defines the vocabulary two separately-deployed programs use to talk: ZWarden.Web (the authority)
and ZWarden.Agent (the host-resident hands). They are updated independently, so a message written by
one build must be read predictably by another, and a mismatch must fail cleanly rather than corrupt
state. PRD 18 already sketches the metadata set — `ProtocolVersion`, `MessageId`, `AgentId`,
`ServerId`, `OperationId`, `Timestamp` — and a representative message list, but leaves two calls
open.

1. **How rich is the version?** The answer shapes F8, F10 (negotiation) and F11 (operations), and is
   costly to revisit once Agents are in the field.
2. **How much of the message catalogue is F7's?** Most of PRD 18's representative messages
   (`RestartServer`, `KickPlayer`, `LogEntry`, `ServerStateChanged`, `HealthChanged`) belong to
   features that do not exist yet. Defining them now is guessing at a wire contract ahead of its
   feature — expensive to unwind — while the ~100K per-feature guardrail argues against a bloated F7.

The load-bearing constraint underneath both: **trust-boundaries §9 rule 3 — no Agent-facing contract
may carry a free-form command, script or shell string.** The vocabulary must be *closed* by
construction, not by review.

## Decision

- **Version = one `int`, range-checked.** `ProtocolVersion.Current` is a single integer (starts at
  `1`) stamped on every `Envelope<TPayload>`. `ProtocolVersionRange(MinSupported, Current)` is the
  band Web accepts; `ProtocolCompatibility.IsCompatible(agentVersion, range)` is a pure predicate
  (`agentVersion >= MinSupported && agentVersion <= Current`), and `Negotiate(...)` returns a
  `ProtocolNegotiationResult` naming which bound failed. **Bump the integer on any change an older
  peer would mis-read; a purely additive change may reuse it**, because the serializer tolerates
  unknown members. F7 owns the rule; F10 calls it at connect time against the `AgentHello` envelope.
- **Catalogue = envelope + closed roots + lifecycle only.** F7 ships `Envelope<T>`, the `abstract`
  `AgentCommand`/`AgentEvent` roots, and the five lifecycle messages as `sealed record` leaves. Every
  leaf declares a stable wire discriminator via `[ProtocolMessage("area.name")]`; `ProtocolJson`
  builds its dispatch registry by scanning the Contracts assembly, so a leaf added by a later feature
  registers automatically without the roots referencing their leaves.
- **Serialization is reflection-based System.Text.Json**, via one canonical `ProtocolJson.Options` —
  matching the existing `TypedIdJsonConverterFactory` (a runtime converter factory), typed ids as
  canonical `<prefix>-<uuid>` strings (PRD 6), enums by name, unknown members skipped. Not
  source-generated.
- **The closed vocabulary is enforced by test.** `ClosedCommandVocabularyTests` reflects over the
  Contracts assembly and fails the build if any exported contract gains a free-form
  command/script/shell string, if a message leaf is not `sealed`, or if a leaf lacks a discriminator
  (trust-boundaries §9 rule 3).

## Alternatives considered

- **SemVer (`major.minor`).** Rejected for v1.0: it buys additive-minor tolerance, but v1.0 is a
  closed, first-party system — the same Web and Agent binaries ship together, and F35 multi-host is
  those binaries on more hosts, not third-party peers. The extra classification surface is cost
  without a consumer. **Not a one-way door:** minor-version tolerance can be layered on later if a
  real mixed-version need appears, since the integer is the low bits of any such scheme.
- **Capability-flag negotiation.** Rejected as overkill for the same reason: per-feature capability
  tokens matter when many independent parties evolve separately; here one authority talks to its own
  Agent.
- **Define the full PRD 18 catalogue now (stubs).** Rejected: a command's shape (a kick's reason and
  duration, a restart's drain behaviour) is knowable only alongside its feature; a guessed wire
  contract is expensive to change once shipped, and it inflates F7 against the guardrail. The sealed
  roots make each later addition safe and cheap, which is the actual goal.
- **Source-generated STJ.** Rejected for now: the typed-id converter is a runtime `JsonConverterFactory`,
  which the source-gen fast path does not support, and no AOT/trim goal exists yet. Revisit only if
  one does.

## Consequences

- **Old Agents are told to update, bluntly.** A single integer means any breaking change forces a
  version bump and excludes older Agents from the range — there is no partial, field-by-field
  tolerance. Accepted for v1.0: first-party co-deployment makes a hard, legible boundary preferable
  to silent partial compatibility.
- **The catalogue looks sparse on purpose.** A reader expecting `RestartServer`/`KickPlayer` in
  `ZWarden.Contracts` today will not find them; they arrive with F15/F19/etc. The pattern for adding
  one (a `sealed record` on a root + a `[ProtocolMessage]` discriminator) is documented on the roots
  and enforced by the vocabulary and registry tests.
- **The closed-vocabulary guarantee is now load-bearing and permanent.** Every future command feature
  inherits it for free, and a regression (a reintroduced free-form command string) is a red build,
  not a review miss.
- **`ZWarden.Contracts` now references `ZWarden.Domain`** for typed ids. Domain is infrastructure-free
  (arch rule 2), so `ZWarden.Agent → ZWarden.Contracts → ZWarden.Domain` keeps the Agent clear of
  persistence, EF and Docker; the reference-direction tests stay green.
- **A new `msg-` typed id** (`MessageId`) joins the closed prefix registry (now 20), giving every
  envelope a canonical, prefixed identity and dedup key rather than a bare GUID.
