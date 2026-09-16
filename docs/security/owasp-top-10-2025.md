# OWASP Top 10:2025 — ZWarden assessment

**Standard verified (PRD 62):** OWASP Top 10:2025, published at <https://owasp.org/Top10/2025/> (checked
2026-09-16). This edition adds **A03 Software Supply Chain Failures** (an expansion of 2021's A06
Vulnerable and Outdated Components) and a new **A10 Mishandling of Exceptional Conditions**, folds SSRF into
A01, and moves Security Misconfiguration up to A02.

**Scope.** ZWarden is a self-hosted control plane (ZWarden.Web) driving outbound Agents that operate Docker
containers and Project Zomboid servers. The load-bearing threat model is [`../trust-boundaries.md`](../trust-boundaries.md);
this maps each 2025 category to ZWarden's exposure, its controls, and the executable evidence. "Evidence" is
a test class or ADR that fails the build / records the decision; the [release gate](../release-gate.md)
indexes which CI job runs each.

| # | Category | ZWarden exposure & controls | Evidence |
| --- | --- | --- | --- |
| **A01** | Broken Access Control (incl. SSRF) | ZWarden-owned RBAC: permission-name policies + resource handlers over a closed catalogue, re-checked server-side (hiding UI is not authz). Tenant isolation is an always-on query filter keyed to the **session** claim, never a request value. Agents enforce a **capability** check (label + assignment), not user authz. SSRF surface is minimal — the Agent dials out; no user-supplied URL fetch. | ADR 0018, 0016; `AuthorizationCompositionTests`, `TenantHintAttackTests`, `TenantIsolationTests`, `TenantFilterGuardTests`, `ContainerOwnershipGuardTests` |
| **A02** | Security Misconfiguration | Fail-closed by default: secret key ring required or the app refuses to start (ADR 0015); host-header allow-list (ADR 0006); Caddy ingress terminates TLS and Web trusts only forwarded headers from it; wollomatic socket-proxy allow-list; images run non-root; RCON port never exposed. Config drift on the shipped Compose/Caddy is guarded. | ADR 0035, 0037, 0008; `ComposeDistributionTests`, `HttpsReferenceDeploymentTests`, `pzserver-image-contract` |
| **A03** | Software Supply Chain Failures *(new)* | Locked-mode restore; NuGet advisory scan (hard blocker on release); trivy dependency + container scans; gitleaks; Dependabot across nuget/actions/docker; CycloneDX SBOM for the solution and per image; **cosign keyless** signatures + SBOM attestations on published images; digest-pinned Compose + `SHA256SUMS`. | ADR 0039, 0013; `SupplyChainCiTests`, `ReleasePipelineTests`; CI `sbom`/`secret-scan`/`dependency-scan`/`container-scan`; `release.yml` |
| **A04** | Cryptographic Failures | Application-layer secret encryption: AES-256-GCM under a per-message HKDF subkey in a versioned envelope; keys are not stored with the data they protect. Password hashing PBKDF2 at a deliberately-set iteration count. RCON password is Agent-owned and never reaches Web. | ADR 0015, 0006, 0026; `SecretHandlingGuardTests`, `ReferenceDirectionTests` (rule 7) |
| **A05** | Injection | The Web→Agent vocabulary is **closed** — no free-form command/script/shell string can exist in any contract. PZ Lua config is **parsed, never evaluated**, behind a seam, with a size + nesting pre-check before the parser. The one arbitrary line (RCON console) is policy-gated and denylisted, not a shell. | ADR 0020, 0010, 0032; `ClosedCommandVocabularyTests`, `PzConfigSeamTests`, `PzConfigPreCheckTests` |
| **A06** | Insecure Design | Seams-first architecture with a written trust-boundaries document driving architecture tests; split authority (Web = desired/authz/audit, Agent = observed); no state inferred from command success; append-only audit. | `../trust-boundaries.md`; ADR 0022, 0023, 0019; `ServerStateInferenceAttackTests`, `ReferenceDirectionTests` |
| **A07** | Authentication Failures | ASP.NET Core Identity with confirmed-email sign-in and MFA; Agents authenticate with a single-use enrollment credential exchanged for a revocable/rotatable per-Agent credential over WSS. *Residual:* Agent possession-of-key (mTLS) is v1.1. | ADR 0006, 0007; `AgentCredentialTheftAttackTests`, `AgentCredentialVerifierTests`, F4 identity tests |
| **A08** | Software or Data Integrity Failures | Signed, SBOM-attested images by digest (cosign keyless); digest-pinned Compose; `SHA256SUMS`. Backups are checksummed; restore verifies SHA-256 before touching the world and refuses a mismatch. Agent protocol is versioned and negotiated. | ADR 0039, 0028, 0029, 0020; `ReleasePipelineTests`, `ServerRestoreRunnerTests` (`DR_*`) |
| **A09** | Security Logging & Alerting Failures | Serilog structured logging; an append-only, tenant-owned **audit** table binding the durable auth-event sink; secrets never logged; OpenTelemetry baseline. | ADR 0021, 0019, 0024; F6 audit tests, `SecretHandlingGuardTests` |
| **A10** | Mishandling of Exceptional Conditions *(new)* | Operations model failure/timeout explicitly and fail closed (a safe stop that times out does not corrupt the world — a latent 10s-timeout bug was fixed in F15); config drift detection fails closed; the support-package pipeline is fail-closed; user-visible errors are actionable and non-disclosing. Heartbeat loss marks state **stale**, never promotes it. | ADR 0022, 0011, 0034, 0023; `ServerStateInferenceAttackTests`, operations/lifecycle tests |

## Overall

Every 2025 category has a named control with executable evidence. The single **accepted residual** touching
this list is under **A07/A03**: Agent authentication rests on a revocable bearer credential in v1.0, with
mTLS + an Agent CA deferred to v1.1 (ADR 0007). It is recorded in the [release gate](../release-gate.md) §7
and named in trust-boundaries §3, and its mitigations (revocation, rotation) are tested.
