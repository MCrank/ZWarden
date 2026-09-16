# OWASP ASVS 5.0.0 — ZWarden assessment

**Standard verified (PRD 62):** OWASP Application Security Verification Standard **5.0.0**, released May 2025
(<https://github.com/OWASP/ASVS/tree/v5.0.0>, checked 2026-09-16). 5.0.0 is a restructure — 17 chapters
(V1–V17), 345 requirements, every requirement renumbered.

**Target level.** ZWarden targets **ASVS Level 2** for v1.0 — the standard's bar for an application that
handles sensitive data and privileged operations, which a control plane with root-equivalent host reach is.
This is a **self-assessment against the chapter intents**, not a certified line-by-line audit; the evidence
column points at the ADRs and tests that carry each chapter. Chapters that do not apply to v1.0 say so.

| Chapter | Relevance & status | Evidence |
| --- | --- | --- |
| **V1 Encoding & Sanitization** | Met. PZ-origin data is untrusted end to end; encoding happens at render (Blazor), never trusted upstream; config is parsed not evaluated. | ADR 0010; `Live*PanelTests` (hostile-input escaping), `PzConfigPreCheckTests` |
| **V2 Validation & Business Logic** | Met. Closed command vocabulary; per-server operation locking and idempotency by `OperationId`; drift detection fails closed. | ADR 0020, 0022, 0011; `ClosedCommandVocabularyTests`, operation-lock tests |
| **V3 Web Frontend Security** | Met (L2). Caddy sets transport/security headers; HttpOnly session cookies; SSR forms with anti-forgery; no bearer token exposed to script. | ADR 0035, 0006; `blueprint-seam` SSR form tests, `SetupFlowTests` |
| **V4 API & Web Service** | Met. The only external service surface is the Agent SignalR hub, authenticated by the "Agent" scheme before any hub method; a closed message vocabulary; versioned protocol. | ADR 0007, 0020; `AgentHubIntegrationTests`, `AgentCredentialTheftAttackTests` |
| **V5 File Handling** | Met. Backup/restore archives refuse symlinks, path traversal, and unsupported entry types; restore stages then atomically swaps; config files are size/nesting pre-checked. | ADR 0028, 0029, 0010; `RestoreArchiveExtractorTests`, `ServerRestoreRunnerTests`, `PzConfigPreCheckTests` |
| **V6 Authentication** | Met (L2). ASP.NET Core Identity, confirmed-email sign-in, MFA/TOTP, PBKDF2 tuned to OWASP guidance. Agent auth is a revocable/rotatable credential (mTLS → v1.1). | ADR 0006, 0007; F4 identity tests, `AgentCredentialVerifierTests` |
| **V7 Session Management** | Met. Session-cookie identity; tenant context derives from the session, never a request value; sign-out and MFA flows tested. | ADR 0016; `SessionTenantContextTests`, `TenantHintAttackTests`, F4 tests |
| **V8 Authorization** | Met (L2). ZWarden-owned RBAC re-enforced server-side; tenant filter always on; the Agent re-checks capability (label + assignment) at the privileged boundary. | ADR 0018, 0016; `AuthorizationCompositionTests`, `TenantIsolationTests`, `ContainerOwnershipGuardTests` |
| **V9 Self-contained Tokens** | Partial / mostly N/A in v1.0. No JWTs are issued to browsers (cookie sessions). The enrollment credential is a single-use opaque token, not a self-contained one. | ADR 0007 |
| **V10 OAuth & OIDC** | N/A in v1.0 — external IdP (Auth0) integration is v1.1 (F3B), behind an already-proven seam. | ADR 0017; scope-and-sequencing §8 |
| **V11 Cryptography** | Met. AES-256-GCM + per-message HKDF subkeys, versioned envelope, keys separated from data; modern hashing; no home-rolled primitives outside the confined protection code. | ADR 0015; `SecretHandlingGuardTests` |
| **V12 Secure Communication** | Met. Caddy TLS ingress (HTTP→HTTPS redirect, ACME); Agents connect outbound over WSS; RCON on a private network, never host-published. | ADR 0035, 0026; `HttpsReferenceDeploymentTests`, `CaddyReferenceDeploymentTests` |
| **V13 Configuration** | Met. Fail-closed secret ring; non-root images; socket-proxy allow-list; reference Compose (SQLite + Postgres) with secrets bootstrap; digest-pinned signed images on release. | ADR 0037, 0008, 0039; `ComposeDistributionTests`, `SupplyChainCiTests`, `ReleasePipelineTests` |
| **V14 Data Protection** | Met. Secrets encrypted at rest; RCON password Agent-owned and unreachable from Web; the support package is a fail-closed redact-and-scan pipeline; tenant data never crosses tenants. | ADR 0015, 0026, 0034; `SupportPackageRedactorTests`, `SecretScannerTests`, `ReferenceDirectionTests` (rule 7) |
| **V15 Secure Coding & Architecture** | Met. Warnings-as-errors + analyzers; enforced reference-direction and seam architecture tests; SBOM + dependency/secret/container scanning; Dependabot. | ADR 0013, 0039; `ReferenceDirectionTests`, `ClosedCommandVocabularyTests`, `SupplyChainCiTests` |
| **V16 Security Logging & Error Handling** | Met. Serilog; append-only tenant-owned audit binding the auth sink; no secrets logged; errors actionable and non-disclosing; OTel baseline; failure/timeout modeled and fail-closed. | ADR 0021, 0019, 0024, 0022; F6 audit tests, `SecretHandlingGuardTests` |
| **V17 WebRTC** | N/A — ZWarden uses no WebRTC. | — |

## Overall

At the chapter-intent level ZWarden meets ASVS 5.0.0 **Level 2** for every applicable chapter, with V10
(OAuth/OIDC) and V17 (WebRTC) not applicable to v1.0 and V9 (self-contained tokens) largely not exercised.
The one carried weakness — Agent possession-of-key under **V6** — is the mTLS deferral recorded in the
[release gate](../release-gate.md) §7 and ADR 0007. A certified line-by-line audit against all 345
requirements is a candidate for a later release; this self-assessment satisfies the v1.0 gate.
