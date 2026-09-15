# Feature 40 Mini-Plan — Production Hardening and 1.0 Release Gate

**Status:** IN PROGRESS (2026-09-15) — PR-A open. Load-bearing decision **LOCKED with the maintainer
(2026-09-15):** the signed-release pipeline goes **full** — build and publish the three images to
`ghcr.io/MCrank`, sign them with **cosign keyless (GitHub OIDC)**, attach SBOM attestations and
`SHA256SUMS`, and pin digests in the reference Compose (slice C).
Roadmap issue: [F40 (#55)](https://github.com/MCrank/ZWarden/issues/55), **Track F — Deployment and
release**. **Format:** PRD 60. **TDD is mandatory** (PRD 2.2). **Pre-implementation verification gate**
(PRD 62) applies to the OWASP Top 10 and ASVS assessments and is run at the head of slice E.

**Depends on: all of v1.0.** Expressed in the tracker as native dependency edges to the four DAG leaves
— [F19 (#41)](https://github.com/MCrank/ZWarden/issues/41),
[F28 (#48)](https://github.com/MCrank/ZWarden/issues/48),
[F30 (#50)](https://github.com/MCrank/ZWarden/issues/50),
[F35 (#54)](https://github.com/MCrank/ZWarden/issues/54) — **all DONE.** Every other v1.0 feature is a
transitive prerequisite of one of those leaves, so F40 is unblocked.

## The load-bearing context: F40 is a gate, not a capability

The scope test (scope-and-sequencing §1) ends *"…Feature 40 release gate. Nothing else."* F40 adds no new
user-facing capability. Its exit condition (PRD 59) is *"Release satisfies documented security and
reliability gates"* — so the **primary deliverable is the documented gate itself**, expressed as executable
CI checks and a release checklist, together with the evidence that it is met.

Two things are already in place and de-risk this feature substantially:

- **`docs/trust-boundaries.md` is accepted** (resolves wayfinder #7). It hands F40 a ready-made attack list
  (its §10) and seven architecture-test assertions (its §9). The full STRIDE enumeration is **explicitly out
  of scope** — the one-page trust-boundary document is the v1.0 threat-model artifact.
- **CI already carries a supply-chain skeleton** (ADR 0013): locked-mode restore on every tier, a
  `nuget-advisory-promotion` job (today warning-only on PR/main, filing a tracking issue on the scheduled
  cadence), and the PZServer image-contract assertions. F40 **promotes** the advisory gate to a hard blocker
  on the release path and **adds** the parts the skeleton always deferred: SBOM, scanning, signing.

So the work is: prove the boundaries hold with executable attacks, close the supply-chain gaps, prove we can
recover and upgrade, and write the assessments that map each control to its evidence.

## Objective

Produce the documented, **executable** v1.0 security-and-reliability release gate and the evidence it is
satisfied: a threat-model attack suite exercising every claim in trust-boundaries §10; SBOM generation,
dependency/secret/container scanning, and signed+published release artifacts; disaster-recovery,
migration/upgrade and fresh-install tests; and the OWASP Top 10:2025 + ASVS 5.0.0 assessments, a
documentation review, and a release-gate checklist that closes #55 when green.

## Dependencies

All of v1.0 (leaves F19/F28/F30/F35, DONE). Reuses, without re-implementing: F24/F25 (backup/restore) for
the DR drill; F34's compose + tier-2 smoke and F33's first-run gate for fresh-install; the EF migrations
(SQLite + Postgres) for the migration/upgrade test; F30's redaction + F29 diagnostics for evidence;
`docs/trust-boundaries.md` §9/§10 for the security spine; the existing `ZWarden.ArchitectureTests` suite and
the `nuget-advisory-promotion` CI job.

## Scope

The PRD 59 review list, delivered as five independently verifiable slices (below). Each review item is either
(a) encoded as an executable test/CI check, or (b) written as an assessment that cites the executable
evidence. Concretely in scope:

- Threat-model / auth / authz / encryption / secrets / Docker-privilege / backup-restore reviews, encoded as
  a **pen-style behavioral attack suite** against trust-boundaries §10 plus completion of the §9 assertions.
- Dependency scan (advisory gate → hard), secret scan (gitleaks), container scan (trivy), `dependabot.yml`.
- **SBOM** (CycloneDX for the .NET graph; syft for the images) and **signed, published release artifacts**
  (cosign keyless on tag; `SHA256SUMS`; image digests pinned in compose) — the full option the maintainer
  chose.
- Fuzz/input testing where useful — targeted at the untrusted-data path (config pre-check, RCON/log/player
  parsing), reusing the existing bounds logic.
- Disaster-recovery test, migration/upgrade test, fresh-install test.
- OWASP Top 10:2025 assessment (incl. the new A03 supply-chain and A10 exceptional-conditions categories),
  ASVS 5.0.0 assessment, documentation review, and the release-gate checklist/runbook.

## Non-scope

- **Full STRIDE enumeration** — the accepted `docs/trust-boundaries.md` is the v1.0 threat model.
- **Active network penetration testing of a live stack** — the §10 attack list is encoded as deterministic
  integration/architecture tests plus a documented **manual** pen-test checklist; CI does not stand up a live
  target and fire probes at it (non-deterministic, disproportionate for a self-hosted OSS gate).
- **mTLS / Agent CA** and the certificate lifecycle — deferred to v1.1 by ADR (scope-and-sequencing §10);
  F40 *names* Agent-credential theft as the sharpest residual risk (trust-boundaries §3/§10) but does not fix
  it.
- Any new operator capability. Findings that reveal a **real** defect become fix commits inside the relevant
  slice; net-new features do not.

## Domain changes

None expected. F40 is verification and release engineering; it adds tests, CI, docs, and one ADR, not domain
types. If a review surfaces a genuine modeling gap, it is fixed under its owning feature's vocabulary, not
invented here.

## Contract changes

None to the Agent/SignalR/API/persistence contracts. New non-runtime contracts only: the CI workflow
surface (SBOM/scan/sign jobs), the release-artifact layout (image tags, digests, `SHA256SUMS`, SBOM
attestations), and `dependabot.yml`.

## Security considerations

The whole feature is the security consideration. The controls F40 must *prove*, drawn from
trust-boundaries §10, in priority order:

1. **Agent-credential theft** (§3 named weakness) — prove the credential is revocable/rotatable and that a
   revoked credential is rejected; document the residual risk (no possession-of-key until mTLS/v1.1).
2. **The label-and-assignment check** (§4) — prove an Agent refuses operations on unlabelled/foreign
   containers and on servers not in its assignment record.
3. **The tenant filter** (§6) — prove a request supplying its own tenant hint cannot cross tenants; prove no
   unscoped read path exists; prove diagnostics/support-package are tenant-scoped. Cross-tenant tests run
   against a two-tenant fixture (trust-boundaries §6 requires this in v1.0).
4. **State inference** (§3) — prove no Web code path advances server state from command success without an
   Agent report; heartbeat loss marks state stale, never current.
5. **Untrusted-data end-to-end** (§8) — prove no stage marks PZ-originated data trusted; config pre-check
   caps size/nesting before the parser; render-time escaping; the support-package redaction holds under
   hostile input.

Plus the standing rules: no secrets logged; user-visible errors actionable without disclosure; supply-chain
integrity (locked restore, advisories, SBOM, signatures).

## Test plan

TDD throughout — each attack test is written to **fail if the control were absent** and pass because the
control exists (for controls already shipped, the test is the executable proof; a genuine gap turns red first
and is fixed to green).

- **Behavioral attack suite** (`ZWarden.IntegrationTests`, new `Security/` area): credential-revocation
  rejection; capability-check refusals (foreign/unlabelled container, unassigned server); tenant-hint
  crossing attempts against a two-tenant fixture; unscoped-read absence; state-inference path absence;
  untrusted-data handling (oversized/deeply-nested config, hostile player/log/RCON strings) end to end.
- **Architecture assertions** (`ZWarden.ArchitectureTests`): confirm trust-boundaries §9 rules 1–7 are all
  present (rules 1/2/5/6 exist; assert 3 free-form-command-absence, 4 tenant-filter, 7 RCON-password-
  unreachable are covered — add any that are not).
- **Fuzz/input tests**: property/boundary tests on the config pre-check and the RCON/log/player parsers.
- **DR test**: backup → destroy world state → restore → verify integrity (SHA + world tree), refuse-if-
  running, inline protective backup (reuses F24/F25).
- **Migration/upgrade test**: EF migrate to head and one step back on **both** SQLite and Postgres; apply the
  full migration chain from empty; assert no pending model changes.
- **Fresh-install test**: compose up from clean (reuse F34 tier-2 smoke) reaching `/healthz`, then the F33
  first-run gate; SQLite mode and Postgres overlay.
- **Supply-chain CI checks**: advisory-promotion green as a hard gate; SBOM produced and non-empty; trivy
  finds no un-waivered HIGH/CRITICAL; gitleaks clean; on tag, images signed and `cosign verify` succeeds.

## Implementation slices

Each slice is its own PR and is independently verifiable (PRD 60). Reviews that find a real defect fix it
inside the owning slice.

- **PR-A — Threat-model attack suite + mini-plan + ADR 0039 (release gate).** This document; the §10
  behavioral attack suite; completion/confirmation of the §9 architecture assertions; the fuzz/input tests.
  The security spine, TDD red→green.
- **PR-B — Supply-chain CI.** SBOM (CycloneDX/.NET + syft/images), `dependabot.yml`, gitleaks secret scan,
  trivy container scan, advisory-promotion promoted to a hard gate on the release path.
- **PR-C — Signed, published release artifacts.** `release.yml` on version tags: build + push the three
  images to `ghcr.io/MCrank`, cosign keyless signing (GH OIDC), SBOM attestation, `SHA256SUMS`, digest
  pinning in the reference Compose. (The outward-facing slice; publishes public images.)
- **PR-D — DR / migration-upgrade / fresh-install tests.** The three reliability drills above.
- **PR-E — Assessments + doc review + gate checklist.** Run the PRD 62 verification gate for OWASP Top
  10:2025 and ASVS 5.0.0 against authoritative sources, then the two assessment docs (each control → cited
  evidence), the documentation review, and `docs/release-gate.md` (the checklist/runbook). Closes #55 when
  every gate is green.

## Diagnostics

Failure of any gate is a red CI job with an actionable message (matching the existing tier guards). The
release-gate checklist (`docs/release-gate.md`) is the human-readable index from each gate to the job/test
that enforces it and the assessment that documents it, so a maintainer can see at a glance which gate failed
and why. SBOM and scan reports upload as CI artifacts for post-hoc inspection.

## Documentation

- This mini-plan; **ADR 0039** (release gate & supply-chain: SBOM/signing/scanning decisions and the
  advisory-gate promotion).
- `docs/release-gate.md` (checklist/runbook, PR-E); the OWASP Top 10:2025 and ASVS 5.0.0 assessment docs
  (PR-E); the documentation review updates.
- Deployment docs updated for verifying signatures and reading the SBOM (PR-C).

## Acceptance criteria

1. The §10 attack suite exists and is green; each attack demonstrably fails without its control.
2. trust-boundaries §9 rules 1–7 are all asserted by the architecture suite.
3. SBOM is generated for the .NET graph and each image; dependency/secret/container scans run in CI; the
   NuGet advisory gate is a hard blocker on the release path.
4. On a version tag, the three images are published to `ghcr.io/MCrank`, signed with cosign, carry SBOM
   attestations, and `cosign verify` + `SHA256SUMS` validation succeed; compose pins digests.
5. DR, migration/upgrade (both providers), and fresh-install (both DB modes) tests pass.
6. OWASP Top 10:2025 and ASVS 5.0.0 assessments exist, each mapping controls to cited executable evidence,
   preceded by the PRD 62 verification of the current standards.
7. `docs/release-gate.md` exists and every gate on it is green; documentation review complete.

## Definition of Done

PRD 61 satisfied for each slice: acceptance criteria met; tests authored as executable specifications; unit/
integration/security/architecture tests pass; SQLite and Postgres tests pass where applicable; no secrets
logged; diagnostics exist; migrations complete; documentation and applicable ADRs updated; CI green with no
unresolved warnings; threat considerations reviewed; user-visible errors actionable without disclosure. #55
closes when the release-gate checklist is fully green.
