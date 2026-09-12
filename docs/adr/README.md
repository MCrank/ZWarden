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

When you add an ADR, add its row here in the same commit.
