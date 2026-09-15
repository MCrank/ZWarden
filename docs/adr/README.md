# Architecture Decision Records

Every decision that a future reader would otherwise find surprising lives here as a numbered,
immutable record. ADRs are **append-only**: to change a decision, write a new ADR that supersedes
the old one and mark the old one superseded — never rewrite history.

An ADR is worth writing only when all three hold (see [`docs/agents/domain.md`](../agents/domain.md)
and the domain-modeling skill): the decision is **hard to reverse**, **surprising without context**,
and **the result of a real trade-off**. If any is missing, record it in the relevant mini-plan
instead. New ADRs take the next number and follow [`TEMPLATE.md`](./TEMPLATE.md).

`CONTEXT.md` (repo root) is the glossary — terms only. ADRs are the decisions.

## Index

| # | Decision | Decided in |
| --- | --- | --- |
| [0001](./0001-three-components-and-retired-vocabulary.md) | Three components, and the words we stopped using | #11, #16 |
| [0002](./0002-test-stack-and-ci-tiering.md) | Test stack: TUnit, TUnit.Mocks, bUnit in reflection mode, tiered by assembly | #2, #3, #8 |
| [0003](./0003-blazor-blueprint-ui-library.md) | Blazor Blueprint 3.16.0 as the UI component library | #10 |
| [0004](./0004-typed-ids-are-stored-as-native-uuid.md) | Typed IDs are stored as a native UUID, not as prefixed text | #4 |
| [0005](./0005-both-database-providers-ship-in-v1-0.md) | Both database providers ship in v1.0, under five SQLite operating conditions | #4, #11 |
| [0006](./0006-identity-hardening-and-deferred-passkeys.md) | Identity hardening: PBKDF2 iteration count configured, passkeys deferred | #11 |
| [0007](./0007-agent-authentication-enrollment-credential-in-v1-0.md) | Agent authentication is a bearer credential in v1.0; mTLS moves to v1.1 | #11 |
| [0008](./0008-docker-socket-access-via-wollomatic-socket-proxy.md) | Docker socket access: `wollomatic/socket-proxy` behind a ten-entry allowlist | #17 |
| [0009](./0009-steamcmd-at-runtime-is-mandatory.md) | SteamCMD at runtime is mandatory, not preferred | #5 |
| [0010](./0010-lua-configuration-is-parsed-never-evaluated.md) | PZ Lua config is parsed and never evaluated: Loretta behind `IPzConfigDocument` | #15 |
| [0011](./0011-configuration-revisions-are-value-level-and-fail-closed.md) | Configuration revisions are value-level, and drift detection fails closed | #11, #15 |
| [0012](./0012-whitelist-addition-is-out-of-scope.md) | Player management ships without whitelist *addition* | #5, #11 |
| [0013](./0013-feature-0-build-and-ci-policy.md) | Feature 0 build-and-CI policy: warnings-as-errors, and NuGet advisories | #14 |
| [0014](./0014-typed-id-pattern.md) | Typed IDs are hand-written structs over a static-abstract interface | #22 |
| [0015](./0015-application-layer-secret-encryption.md) | Secret encryption: AES-256-GCM under a per-message HKDF subkey, in a versioned envelope | #24 |
| [0016](./0016-tenant-isolation-query-filter-and-default-tenant.md) | Tenant isolation: an always-on EF query filter over an ambient tenant context, with a fixed default tenant | #25 |
| [0017](./0017-external-identity-provider-seam.md) | External identity-provider seam: an Application abstraction proven against a test double, concrete provider deferred to F3B | #26 |
| [0018](./0018-zwarden-owned-rbac.md) | ZWarden-owned RBAC: permission-name policies + resource handlers, over a closed catalogue | #27 |
| [0019](./0019-audit-is-append-only-tenant-owned-and-binds-the-auth-sink.md) | Audit is an append-only, tenant-owned table, and F6 binds the durable authentication-event sink | #28 |
| [0020](./0020-agent-protocol-versioning-and-catalogue.md) | Agent protocol: a single integer version + range, and F7 ships the envelope and lifecycle messages only | #29 |
| [0021](./0021-serilog-is-the-logging-stack.md) | Serilog is ZWarden's logging stack, introduced by the Agent runtime (F8) | #30 |
| [0022](./0022-operation-lifecycle-and-per-server-locking.md) | The Operation lifecycle, its failure/timeout semantics, and the realized per-server lock | #33 |
| [0023](./0023-server-health-model-and-observed-delivery.md) | The hierarchical server-health model (five states, four probes) and its observed delivery | #37 |
| [0024](./0024-opentelemetry-observability-baseline.md) | The OpenTelemetry observability baseline: SDK, opt-in OTLP, no Prometheus | #37 |
| [0025](./0025-steamcmd-update-orchestration.md) | SteamCMD updates driven through the container (no exec): control-file + restart + log-parse, persistent install bind + tmpfs runtime | #38 |
| [0026](./0026-rcon-foundation-private-transport-and-agent-owned-credential.md) | RCON foundation: private transport, never host-published, Agent-owned credential | #39 |
| [0027](./0027-ban-registry-records-zwarden-issued-bans-not-a-mirror.md) | The ban registry records ZWarden-issued bans (advisory intent), not a mirror of PZ's user store | #41 |
| [0028](./0028-backup-archive-contract-and-local-destination.md) | A backup is a checksummed tar.gz of the world tree, written host-side to a configurable local BackupRoot | #45 |
| [0029](./0029-restore-verifies-stages-and-swaps-atomically-with-an-inline-protective-backup.md) | A restore verifies the archive, stages it, and swaps it in atomically after an inline protective backup | #46 |
| [0030](./0030-live-logs-stream-on-demand-sanitized-at-source-over-a-non-operation-channel.md) | Live logs stream on demand, are sanitized at the source, and ride a non-Operation subscription channel | #47 |
| [0031](./0031-aspire-is-dev-test-orchestration-only.md) | Aspire is dev/test orchestration only and does not govern production | #123 |
| [0032](./0032-remote-console-runs-policy-gated-rcon-under-an-elevated-permission.md) | The remote console runs arbitrary RCON under an elevated permission, governed by a denylist and audited | #48 |
| [0033](./0033-diagnostics-is-an-aggregating-read-only-transient-sweep-over-ten-domains.md) | Diagnostics is an aggregating, read-only, transient sweep over ten domains producing one untrusted report | #49 |
| [0034](./0034-the-sanitized-support-package-is-a-transient-fail-closed-pipeline.md) | The sanitized support package is a transient, fail-closed collect→sanitize→redact→scan→validate→package pipeline | #50 |

When you add an ADR, add its row here in the same commit.
