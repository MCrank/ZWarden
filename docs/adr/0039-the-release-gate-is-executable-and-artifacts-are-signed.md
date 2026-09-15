# 39. The 1.0 release gate is executable, and release artifacts are signed

**The v1.0 release gate is a set of executable CI checks and one documented checklist, not a manual
audit.** Every PRD 59 review item is either encoded as a test/CI job or written as an assessment that
cites that executable evidence. Supply-chain integrity is completed to its full shape: an **SBOM** is
generated for the .NET graph and every image, dependency/secret/container scanning runs in CI, the NuGet
advisory gate is **promoted to a hard blocker on the release path**, and on a version tag the three images
are **published to `ghcr.io/MCrank` and signed with cosign keyless (GitHub OIDC)**, carrying SBOM
attestations, `SHA256SUMS`, and digests pinned in the reference Compose.

- Status: accepted
- Decided in: [#55](https://github.com/MCrank/ZWarden/issues/55) (F40), with the maintainer's
  signed-release decision locked 2026-09-15; builds on ADR 0013 (F0 build-and-CI policy) and
  `docs/trust-boundaries.md` (wayfinder #7)
- Bears on: PRD 55 (supply-chain), PRD 59 (F40 review list), PRD 61 (definition of done), PRD 62
  (verification gate); ADR 0013, 0037 (compose distribution)

## Context

F40 is the 1.0 release gate. Its exit condition (PRD 59) is *"Release satisfies documented security and
reliability gates"* — the deliverable is the gate, not a feature. A gate that lives only in a reviewer's
head rots the moment the next commit lands; a gate that lives in CI fails the build when a boundary is
crossed. The trust-boundaries document (accepted, resolves wayfinder #7) already hands F40 its threat
model: seven architecture assertions (§9) and a ranked attack list (§10), with the full STRIDE enumeration
explicitly out of scope. ADR 0013 already established the supply-chain skeleton — locked-mode restore and a
`nuget-advisory-promotion` job that is warning-only on PR/main and files a tracking issue on the scheduled
cadence — and deferred SBOM, scanning, and signing to "a later feature." F40 is that feature.

Two forces shape the decision. First, TDD is mandatory (PRD 2.2), which means the threat-model review is
naturally expressed as attacks that must fail. Second, the reference distribution (ADR 0037) ships images a
self-hoster pulls and runs as effectively root on their host (trust-boundaries §1); those users deserve a
way to verify what they run came from this repository unmodified.

## Decision

1. **Encode, don't audit.** The threat-model / auth / authz / encryption / secrets / Docker-privilege /
   backup-restore reviews are delivered as an executable pen-style attack suite exercising
   trust-boundaries §10, plus completion of the §9 architecture assertions (notably rule 3 — no
   Agent-facing contract carries a free-form command, script, or shell string, asserted by reflection over
   every `AgentCommand` so it "cannot be reintroduced under any name"). The remaining reviews are written
   assessments (OWASP Top 10:2025, ASVS 5.0.0) that map each control to the test/ADR that proves it.

2. **Complete the supply chain.** Generate an SBOM (CycloneDX for the .NET graph; syft for images). Run
   secret scanning (gitleaks) and container scanning (trivy) in CI. Add `dependabot.yml`. **Promote the
   NuGet advisory gate to a hard blocker on the release path** (it stays warning-only on ordinary PRs per
   ADR 0013, so day-to-day work is not blocked by a freshly-published advisory).

3. **Sign and publish, in full.** On a version tag, `release.yml` builds the three images, pushes them to
   `ghcr.io/MCrank`, signs them with **cosign keyless** via GitHub OIDC (no long-lived keys), attaches SBOM
   attestations, and produces `SHA256SUMS`. The reference Compose pins images by `@sha256:` digest. Consumer
   verification (`cosign verify`, checksum validation) is documented in the deployment guide.

4. **Prove recovery, from clean.** Disaster-recovery (backup→destroy→restore, reusing F24/F25),
   migration/upgrade (EF up and one step back on both SQLite and Postgres, plus the full chain from empty),
   and fresh-install (compose up to `/healthz` then the F33 first-run gate, both DB modes) are reproducible
   tests.

## Alternatives considered

- **A manual security audit / external pen-test as the gate.** Rejected: not repeatable, not TDD, and it
  ages out on the next commit. A live-fire pen-test against a running stack in CI was also rejected —
  non-deterministic and disproportionate for a self-hosted OSS project; the §10 attack list is encoded as
  deterministic tests plus a documented *manual* pen-test checklist instead.
- **Full STRIDE enumeration.** Out of scope by scope-and-sequencing §6; the one-page trust-boundaries
  document is the v1.0 threat model.
- **Checksums-only, defer SBOM and signing.** The lighter "where practical" reading of PRD 55. Rejected by
  the maintainer in favour of the full pipeline: images that run as root on a stranger's host warrant
  provenance now, not in v1.1.
- **cosign with a long-lived key pair.** Rejected in favour of keyless OIDC: no key to store, rotate, or
  leak; the signature's identity is the GitHub workflow itself.
- **Blocking every PR on the advisory gate.** Rejected: a newly-published advisory would redden unrelated
  work. The gate is hard only on the release path; ordinary PRs keep ADR 0013's warning behaviour.

## Consequences

- **Public images.** Signed images are published to `ghcr.io/MCrank` and become publicly pullable. This is
  intended — it is what "signed release artifacts" means for a self-hosted product — but it is an
  outward-facing commitment: once a tag's images are public and signed, they are part of the supply chain
  others trust.
- **A tag is now a heavier event.** Tagging a release runs build+push+sign+attest for three images. This is
  accepted; releases are infrequent and the provenance is the point.
- **The gate can go red for reasons outside a PR's diff** — a newly-published advisory, a new trivy finding.
  On ordinary PRs these stay warnings; on the release path they block, which is the correct place to be
  strict. Waivers for scanner findings are explicit and reviewed, never silent suppression.
- **The residual risk is named, not fixed.** Agent-credential theft on a compromised host (trust-boundaries
  §3) remains the sharpest weakness until mTLS lands in v1.1. F40 documents and tests around it (revocation,
  rotation) but does not close it, and the release-gate checklist records that plainly.
- **The gate is now maintainable.** A future contributor sees exactly which check enforces which control via
  `docs/release-gate.md`, and a crossed boundary fails the build rather than waiting for a human to notice.
