# 34. The sanitized support package is a transient, fail-closed collect→sanitize→redact→scan→validate→package pipeline

The support package (F30) is produced by a **deterministic, fail-closed pipeline** —
**Collect → Sanitize → Redact → Secret-scan → Validate → Package** (PRD 51) — over the transient F29
`DiagnosticReport`. Its security-critical core (sanitize, key-based redaction, PII pseudonymization, and the
last-line secret scan) is a **pure, I/O-free** library in `ZWarden.Diagnostics`; the ZIP and the HTTP download are
a thin Web-side shell. The scanner is a **backstop that fails closed**: on a detection, package generation is
**aborted and nothing is emitted** rather than knowingly shipping prohibited material. A package is **transient**
(generated on demand, streamed, gone); `DiagnosticId` is a **correlation id** (the seeded `DiagnosticPackageId`,
`diag-`), stamped into the manifest and the audit event — not a persisted entity key.

- Status: accepted
- Decided in: #50 (F30 — Sanitized Support Package); mini-plan `docs/feature-plans/F30-sanitized-support-package.md`
- Bears on: PRD 49 (`DiagnosticId` as a correlation id alongside `OperationId`/`AgentId`/`ServerId`), PRD 51
  (the pipeline and the "fail rather than knowingly emit" rule), PRD 53 (privacy-aware, pseudonymized output),
  scope-and-sequencing §6/§11 criterion 13; builds on ADR 0018 (fail-closed authorization), ADR 0019 (the export
  is an audited action), ADR 0028 (the per-artifact SHA-256 checksum posture), and ADR 0033 (the transient,
  read-only, **untrusted** report F30 collects). Feeds F31 (post-1.1 AI context), which inherits the untrusted-data
  and sanitization guarantees.

## Context

F29 produces a transient `DiagnosticReport` whose every `Detail` is untrusted (trust-boundaries §8). Criterion 13
asks an operator to "generate safe troubleshooting information" they can hand to a maintainer or an AI harness. The
report cannot be shipped as-is: it can carry a hostile mod name, a config value, an RCON error, a certificate
subject, an IP address, or a player name — and, if a probe or an operator ever put one there, a secret. PRD 51
fixes the shape of the answer (Collect → Sanitize → Redact → Secret-scan → Validate → Package) and one hard rule:
if secret scanning detects prohibited material, **generation shall fail rather than knowingly emit it**. PRD 53
adds that unnecessary operational PII must be pseudonymized, consistently, so relationships stay legible. The
forces: the redaction/scan logic must never regress (it is the difference between a safe bundle and a leak), so it
must be exhaustively testable; and a package is something an operator produces once and sends, so persisting it
would add a second at-rest home for still-sensitive data and a migration, for no exit-condition benefit.

## Decision

1. **A pure, I/O-free pipeline core.** The stages — `SupportPackageSanitizer` (strip ANSI/CSI/OSC + C0/C1 control
   bytes, the F27 `LogLineSanitizer` posture), `SupportPackageRedactor` (mask values behind a sensitive key, reusing
   the F3 `Redaction` vocabulary), `Pseudonymizer` (PII → `<HOST-n>`/`<PLAYER-n>`/`<PRIVATE-IP-n>`/`<PUBLIC-IP-n>`),
   `SecretScanner`, `PackageValidator`, and the sequencing `SupportPackageBuilder` — live in `ZWarden.Diagnostics`
   and reference no filesystem, ZIP, or HTTP assembly (an architecture test proves the assembly pulls in neither
   `System.IO.Compression` nor `System.Net.Http`). JSON serialization and SHA-256 hashing are in-memory. The
   Web-side collector, ZIP writer, endpoint, and audit are the shell (F30 PR-B).
2. **The secret scan is a last-line gate and it fails closed.** Redaction (key-based) and pseudonymization (PII)
   remove the *expected* sensitive material first; the `SecretScanner` then runs over the **fully-sanitized**
   payload as a content-based backstop for a secret with no key or in an unexpected place (a PEM private key, a
   JWT, a cloud key, URL-embedded credentials, an inline `password=…`, or a high-entropy blob). On a detection the
   builder returns `SecretDetected` and emits **nothing**; the Web shell audits the abort and returns a problem
   response naming the detector — **never** the value. The scanner is deliberately conservative: a false positive
   fails a package, which PRD 51 explicitly prefers over emitting a secret.
3. **Pseudonyms are consistent within one package, and only within it.** A per-package, in-memory, first-seen map
   assigns the numbered tokens so a maintainer can follow which host talked to which player *inside the bundle*
   (PRD 53). The map is **not** persisted and **not** stable across packages — a cross-package-stable pseudonym is
   itself a re-identification key and would need durable storage.
4. **Transient, no entity, no migration.** A package is generated on demand, streamed as a ZIP download, and gone
   — like the F29 report and the F28 console. The only durable record is the `Diagnostics.Export` audit event
   (who, when, the `DiagnosticId`, the worst status, the byte size, the scan verdict). `DiagnosticId` reuses the
   already-seeded `DiagnosticPackageId` (`diag-`) as a correlation id, not an entity key.
5. **Fail-closed, tenant/server-scoped authorization; the already-seeded permission.** The Web-side collector
   authorizes the tenant-wide `Diagnostics.Export` before any content is gathered (ADR 0018); a targeted server is
   resolved through the tenant filter (foreign/unknown ⇒ 404). No permission catalogue or built-in-role change.
6. **Per-document integrity in the manifest.** The manifest carries the `DiagnosticId`, schema version, generation
   time, environment facts, scope, worst status, a per-document SHA-256 (ADR 0028's checksum posture, per entry),
   and the clean-scan attestation — statuses, sizes, and hashes only, never a credential.

## Alternatives considered

- **Build the whole thing Web-side around `ZipArchive`.** Maximum reuse of the packaging APIs, but it buries the
  redaction/scan logic — the part that must never regress — behind file and stream I/O, making the fail-closed
  path awkward to test exhaustively. The gate is the feature; it belongs in a pure, heavily-tested core. Not taken.
- **Scan, then redact the hits inline and emit anyway (best-effort).** Friendlier (always produces a package), but
  PRD 51 is explicit ("fail rather than knowingly emit"), and masking a *late* detection silently hides that an
  upstream redaction gap existed and risks shipping an incompletely-masked secret. Not taken; a detection aborts.
- **Persist a `SupportPackage` entity for history and re-download.** Gives a stable, listable history, but adds a
  Domain entity, a Postgres/Sqlite migration, a retention/cleanup concern, and a second at-rest home for a
  sanitized-but-still-sensitive artifact — for a bundle produced once and sent. History is not in the exit
  condition. Not taken; the package is transient.
- **Cross-package-stable pseudonyms.** Would let a maintainer correlate across separate exports, but a durable
  pseudonym map is a re-identification key and needs persistence. Not taken; per-package only.

## Consequences

- There is one pure, exhaustively-tested pipeline that turns the untrusted F29 report into a safe, portable bundle;
  F31 (AI context) builds on the same sanitized output and inherits the guarantees.
- A new secret shape means a new detector in `SecretScanner` (+ its positive/negative tests) — not a change to the
  collector, the packager, or the endpoint.
- Because the scanner fails closed and conservative, a package can legitimately be *refused*; the operator sees a
  non-leaking "export blocked" message and the abort is audited. This is the intended safe behaviour, not a bug.
- The pure core performs no I/O, so the ZIP write and the streaming download (the only real I/O) are isolated in
  the Web shell and covered by the writer/endpoint tests, while the security logic is covered by fast unit tests.
