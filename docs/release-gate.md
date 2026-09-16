# ZWarden v1.0 Release Gate

**Status:** the F40 gate ([#55](https://github.com/MCrank/ZWarden/issues/55)). This is the documented
security-and-reliability gate PRD 59 requires as the v1.0 exit condition. It is deliberately **executable**:
almost every line below is enforced by a CI job or a test that fails the build when the control regresses
(ADR 0039). Prose that is not machine-checked is called out as such.

**How to read it.** Each control names the artifact that enforces it. "CI" means a job in
`.github/workflows/ci.yml` that runs on every PR; "release" means a job in `.github/workflows/release.yml`
that runs on a version tag; a test name is a class in the offline test tiers. The threat model is
[`docs/trust-boundaries.md`](./trust-boundaries.md) (§9 architecture assertions, §10 ranked attacks); the
assessments are [`docs/security/owasp-top-10-2025.md`](./security/owasp-top-10-2025.md) and
[`docs/security/asvs-5.0.0.md`](./security/asvs-5.0.0.md).

## 1. Threat-model controls (trust-boundaries §10)

| Attack | Control | Enforced by |
| --- | --- | --- |
| §10 #1 Agent-credential theft | Revocable + rotatable per-Agent credential; a stale one is refused at the hub handshake | `AgentCredentialTheftAttackTests`, `AgentCredentialVerifierTests`, `AgentTrustServiceTests`; ADR 0007 |
| §10 #2 Label/assignment bypass | Agent refuses foreign/unlabelled containers and unassigned servers | `ContainerOwnershipGuardTests`, `CanonicalContainerRecognizerTests`, `AgentDockerRuntimeTests` (networked); ADR 0008 |
| §10 #3 Tenant-filter bypass | Tenant derives only from the session claim; always-on query filter; no unscoped read | `TenantHintAttackTests`, `TenantIsolationTests`, `TenantFilterGuardTests`, `PostgresTenantTests` (networked); ADR 0016 |
| §10 #4 State inference | Web never advances server state from command success — only from an observed report | `ServerStateInferenceAttackTests`, `ServerStateReconcilerTests`; ADR 0023 |
| §10 #5 Untrusted data trusted | PZ data bounded at source, stored opaque, escaped at render; config pre-check before parse | `LivePlayerRosterPanelTests`/`LiveConsoleOutputPanelTests`/`LiveServerLogPanelTests`, `PzConfigPreCheckTests`, `SupportPackageRedactorTests`, `SecretScannerTests`; ADR 0010, 0034 |

## 2. Architecture assertions (trust-boundaries §9)

| Rule | Enforced by |
| --- | --- |
| 1 Agent has no persistence/DB reach · 2 Domain has no infra · 5 Web has no Docker client · 6 no IdP types in Domain/Application | `ReferenceDirectionTests` |
| 3 No free-form command/script/shell in any Agent contract | `ClosedCommandVocabularyTests` |
| 4 No unscoped tenant read (no `IgnoreQueryFilters` in `src`) | `TenantFilterGuardTests` (+ model-level `TenantFilterModelGuardTests`) |
| 7 The RCON password type is unreachable from Web | `ReferenceDirectionTests`, `SecretHandlingGuardTests`; ADR 0026 |
| Lua parser seam (no Loretta type crosses `IPzConfigDocument`) | `PzConfigSeamTests`; ADR 0010 |
| Support-package pipeline references no ZIP/HTTP assembly | `SupportPackagePurityTests`; ADR 0034 |

## 3. Supply chain (PRD 55, OWASP A03)

| Control | Enforced by |
| --- | --- |
| Deterministic restore (locked mode) | `dotnet restore --locked-mode` on every CI tier |
| NuGet advisory scan | CI `nuget-advisory-promotion` (warning on PR, ADR 0013) → **hard blocker** on the release path (`advisory-gate`, `PromoteNuGetAudit=true`) |
| Dependency vulnerability scan | CI `dependency-scan` (trivy fs, HIGH/CRITICAL, ignore-unfixed) |
| Secret scan | CI `secret-scan` (gitleaks, full history) |
| Container vulnerability scan | CI `container-scan` (trivy image on the PZServer image) |
| Dependency currency | `.github/dependabot.yml` (nuget, github-actions, docker) |
| SBOM | CI `sbom` (CycloneDX, solution) + per-image CycloneDX SBOMs attested on release |
| Controls cannot silently disappear | `SupplyChainCiTests`, `ReleasePipelineTests` |

## 4. Signed release artifacts (PRD 55, OWASP A08)

| Control | Enforced by |
| --- | --- |
| Images published by digest to GHCR | release `build-sign-publish` |
| Keyless signatures (cosign / GitHub OIDC) | release `cosign sign` per image |
| SBOM attestation per image | release `cosign attest --type cyclonedx` |
| Digest-pinned Compose + `SHA256SUMS` + `image-digests.txt` | release GitHub Release assets |
| Consumer verification documented | [`docs/deployment/compose-reference.md`](./deployment/compose-reference.md) → "Running signed release images" |

## 5. Reliability drills (PRD 59)

| Drill | Enforced by |
| --- | --- |
| Disaster recovery (backup → destroy → restore, byte-for-byte; corrupted archive refused) | `ServerRestoreRunnerTests` (`DR_*`); ADR 0028, 0029 |
| Migration/upgrade (both providers no model drift; full SQLite chain from empty; rollback + forward) | `MigrationDriftTests`; ADR 0005 |
| Migration applies on a live Postgres server | `PostgresTenantTests` et al. (networked) |
| Fresh install (empty DB migrates on boot → `/healthz` → `/setup` gate) | `FreshInstallDrillTests`, `SetupFlowTests`; ADR 0036 |
| Container fresh install (SQLite mode boots non-root, serves `/healthz`) | `ComposeDistributionSmokeTests` (networked); ADR 0037 |

## 6. Standing engineering gates (PRD 56, 61)

| Control | Enforced by |
| --- | --- |
| Warnings-as-errors, nullable, analyzers | `dotnet build -c Release` (CI `tier1-offline`) |
| Tests cannot be silently dropped | per-assembly `--minimum-expected-tests` floors + CI `tier1-silent-drop-guard` |
| Authorization enforced + actions audited | F5 (`AuthorizationCompositionTests`), F6 audit table; ADR 0018, 0019 |
| Secrets never logged; encrypted at rest | ADR 0015; `SecretHandlingGuardTests`, `RedactionTests` |
| HTTPS ingress, proxy trust | Caddy reference (`HttpsReferenceDeploymentTests`, `CaddyReferenceDeploymentTests`); ADR 0035 |

## 7. Named residual risks (accepted for v1.0)

These are deliberate, recorded gaps — not oversights. Each has an owner and a horizon.

- **Agent-credential theft on a compromised host** (trust-boundaries §3). v1.0 authenticates Agents with a
  revocable bearer credential over WSS; possession-of-key (mTLS + Agent CA) is deferred to **v1.1** (ADR 0007,
  scope-and-sequencing §10). Mitigations tested: revocation and rotation are refused at the hub.
- **Container base-image CVEs.** The PZServer image applies Debian security updates at build (`apt-get upgrade`);
  Dependabot bumps bases and the CI container-scan blocks fixable HIGH/CRITICAL. Unfixable CVEs are surfaced,
  not blocked (`ignore-unfixed`), and reviewed per release.
- **Live-PZ networked RCON integration** is deferred (F18) — the RCON client and the four traps are unit-tested;
  an end-to-end test against a real PZ server is a post-1.0 follow-up.
- **Trivy/scanner findings outside a PR's diff** can redden CI (a newly published CVE). This is intended: the
  gate reflects the current threat surface, not just the diff.

## 8. Manual pre-release checklist (not machine-enforced)

Run these by hand before cutting a release tag; they complement, not replace, the automated gates above.

1. Review any open `dependency-scan` / `container-scan` waivers and confirm each is still justified.
2. Confirm the trust-boundaries document still matches the architecture (no new boundary crossed).
3. Skim the two assessment docs for any control whose evidence has moved.
4. On a scratch host, run the disaster-recovery drill against a real backup (the automated drill proves the
   mechanism; a manual run proves the operator runbook).

## 9. Cutting a release

1. Ensure `development` is green (all CI gates above pass).
2. Tag `vX.Y.Z` and push it — `release.yml` runs the hard advisory gate, builds, publishes, signs, attests,
   and cuts the GitHub Release with the digest-pinned Compose and `SHA256SUMS`.
3. Verify the published images per `docs/deployment/compose-reference.md` before announcing.
