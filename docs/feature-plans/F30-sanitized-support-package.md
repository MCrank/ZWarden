# Feature 30 Mini-Plan — Sanitized Support Package

**Status:** PLANNED (2026-09-15) — the load-bearing decisions locked as-recommended (2026-09-15) with the
maintainer. Roadmap issue: [F30 (#50)](https://github.com/MCrank/ZWarden/issues/50), **Track E — Operator
surface**. The feature that lets an operator **export a portable, sanitized troubleshooting bundle** — collect the
F29 diagnostic report, strip control bytes, redact secrets, pseudonymize operational PII, scan for anything the
first passes missed, validate, and package into a ZIP — with **automated secret-leak prevention** that *fails the
generation rather than knowingly emit prohibited material*. Satisfies §11 **criterion 13** ("generate safe
troubleshooting information") on its own.

**Depends on [F29 (#49)](https://github.com/MCrank/ZWarden/issues/49) — DONE and CLOSED** (PR #132/#133/#134
merged). Blocks [#55](https://github.com/MCrank/ZWarden/issues/55) per the issue's native dependency edge; feeds
F31 (post-1.1 AI context export), which inherits F30's untrusted-data and sanitization guarantees.

**Format:** PRD 60. **TDD is mandatory** (PRD 2.2). **Written against:**
[`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (F30) and §11 criterion 13; PRD **49** (`DiagnosticId`
as a correlation id, alongside `OperationId`/`AgentId`/`ServerId`), **51** (the Collect → Sanitize → Redact →
Secret-scan → Validate → Package pipeline, and the fail-closed rule), **52** (F31 marks collected runtime data as
untrusted — F30 carries the guarantee forward) and **53** (privacy-aware, pseudonymized diagnostics);
[`trust-boundaries.md`](../trust-boundaries.md) **§8** (everything a Server, its mods, its config, and its
runtime emit is untrusted end to end). ADRs it builds on:
[0018](../adr/0018-zwarden-owned-rbac.md) (fail-closed, tenant-scoped authorization),
[0019](../adr/0019-audit-is-append-only-tenant-owned-and-binds-the-auth-sink.md) (the export is an audited
administrative action),
[0028](../adr/0028-backup-archive-contract-and-local-destination.md) (the atomic-write + per-artifact SHA-256
archive posture the ZIP mirrors), and
[0033](../adr/0033-diagnostics-is-an-aggregating-read-only-transient-sweep-over-ten-domains.md) (the transient,
read-only, **untrusted** `DiagnosticReport` F30 collects). New **ADR 0034** lands with the feature (the
sanitized-support-package pipeline and its fail-closed secret gate).

## Objective

Give an operator with the tenant-wide **`Diagnostics.Export`** permission a one-action way to produce a portable
support bundle they can safely hand to a maintainer or an AI harness. The bundle is built by a deterministic,
**fail-closed** pipeline (PRD 51):

```text
Collect → Sanitize → Redact → Secret scan → Validate → Package
```

- **Collect** — assemble the F29 `DiagnosticReport` (the tenant-wide host report, or a targeted per-server report)
  plus non-secret **environment facts** (ZWarden build version, DB provider, OS/runtime). Reuses `IDiagnosticsService`;
  no new collection seam.
- **Sanitize** — strip ANSI/CSI/OSC escapes and C0/C1 control bytes from every untrusted string (the
  `LogLineSanitizer` posture); detail is already length-bounded by F29 (`DiagnosticCheck.MaxDetailLength`).
- **Redact** — mask sensitive **keys** (`password`, `token`, `rcon`, `connectionstring`, …) with the existing
  `ZWarden.Domain.Security.Redaction` primitives.
- **Pseudonymize** — replace operational PII with **consistent, per-package** tokens (`<HOST-1>`, `<PLAYER-2>`,
  `<PRIVATE-IP-1>`, `<PUBLIC-IP-1>`; PRD 53), preserving relationships *within one package*.
- **Secret scan** — a last-line, content-based detector (private-key PEM blocks, JWTs, bearer tokens, cloud-key
  shapes, connection strings, high-entropy blobs) over the **fully-sanitized** payload. **A hit fails the
  generation** — the package is never emitted (PRD 51).
- **Validate** — the assembled documents are schema-shaped and the secret scan is clean.
- **Package** — write the documents into a **ZIP** with a `manifest.json` carrying the `DiagnosticId`, schema
  version, generation time, environment facts, a per-entry SHA-256, and a secret-scan attestation. Stream it as a
  download.

F30 **collects and sanitizes only — it never diagnoses or remediates** (F29 owns the report; remediation is out of
scope everywhere). It reuses the **already-seeded** tenant-wide `Diagnostics.Export` permission and the
`ZWarden.Diagnostics` project. A package is **transient** — generated on demand, streamed, and gone; **no entity,
no migration** (D-2). `DiagnosticId` is a **correlation id** (Guid-backed typed id, ADR 0014), stamped into the
manifest and the audit event — not a persisted entity key.

## The load-bearing decisions (LOCKED as-recommended, 2026-09-15)

- **D-1 — F30 is a pure sanitization *pipeline* over the F29 report, plus a thin Web-side collector/packager. The
  security-critical core has no I/O. `[LOCKED]`** The pipeline stages (`Sanitizer`, `Redactor` over
  `Redaction`, `Pseudonymizer`, `SecretScanner`, `PackageValidator`, the manifest/report models, `DiagnosticId`,
  and the `ISupportPackageBuilder` that sequences them) live in **`ZWarden.Diagnostics`** and reference only
  `Application`/`Domain` — **no filesystem, no ZIP, no HTTP** — so every stage is exhaustively unit-testable with
  plain strings. The **collector** (an adapter over `IDiagnosticsService` + an environment-facts probe) and the
  **ZIP packager** (`System.IO.Compression`) and the **endpoint** live Web-side (`ZWarden.Web/Diagnostics/`).
  *Rejected — build the whole thing Web-side around `ZipArchive`:* it would bury the redaction/scan logic (the
  part that must never regress) behind I/O and make it awkward to test the failure path exhaustively. The fail-closed
  gate is the feature; it belongs in a pure, heavily-tested core.
- **D-2 — A support package is transient and on-demand; no persisted package, no history, no migration.
  `[LOCKED]`** Generate → stream the ZIP as a download → gone, exactly like the F29 report and the F28 console
  output. The exit condition — "a useful support package can be generated with automated secret-leak prevention" —
  is satisfied by on-demand generation. `DiagnosticId` is a **generated correlation id** stamped into
  `manifest.json` and the `Diagnostics.Export` audit event (who exported, when, the `DiagnosticId`, the worst
  status, byte size, and the scan verdict — never the untrusted contents), matching PRD 49's treatment of it
  alongside `OperationId`/`AgentId`/`ServerId`. *Rejected — persist a `SupportPackage` entity for history +
  re-download:* it adds a Domain entity, a Postgres/Sqlite migration, a file-retention/cleanup concern, and a
  second place a sanitized-but-still-sensitive artifact lives at rest — for a bundle the operator generates and
  sends once. History is not in the exit condition.
- **D-3 — The secret scan is a *last-line* gate over the fully-sanitized payload, and it fails closed. A detection
  aborts generation; the bundle is never written. `[LOCKED]`** Redaction (key-based) and pseudonymization (PII)
  run first and remove the *expected* sensitive material; the scanner then runs over the resulting bytes as a
  content-based backstop for anything they missed (a secret in an unexpected place, a value that isn't behind a
  sensitive key). On a hit, `ISupportPackageBuilder` returns a typed `SecretDetected` failure, the endpoint returns
  a problem response (not a partial ZIP), and the failure is **audited**. The scanner is deliberately
  **conservative — a false positive fails the package** (the safe direction; PRD 51 says fail rather than emit);
  this is documented as accepted. *Rejected — scan and then redact the hits inline (best-effort emit):* PRD 51 is
  explicit ("**fail** rather than knowingly emit"); silently masking a late detection risks shipping an
  incompletely-masked secret and hides that the redaction upstream had a gap.
- **D-4 — Pseudonyms are consistent *within one package* and are not stable across packages. `[LOCKED]`** A
  per-package, in-memory, first-seen map assigns `<HOST-1>`, `<HOST-2>`, `<PLAYER-1>`, `<PRIVATE-IP-1>`,
  `<PUBLIC-IP-1>` (IPs classified RFC1918-private vs public) so a maintainer can follow "which host talked to which
  player" *inside the bundle* (PRD 53). It is **not** persisted and **not** stable run-to-run, because a
  cross-package-stable pseudonym is itself a re-identification key and would require storing the mapping at rest.
  *Rejected — a durable pseudonym table:* re-identification vector + persistence, for a benefit (correlating across
  separate exports) no requirement asks for.

## Pipeline map (stage → home → input → output → fail mode)

| Stage | Home | Input | Output | Fails the package? |
| --- | --- | --- | --- | --- |
| **Collect** | Web (`SupportPackageCollector` over `IDiagnosticsService` + env probe) | `UserId`, optional `ServerId` | raw `SupportPackageContent` (report + env facts) | on `NotAuthorized` (403) — no collection |
| **Sanitize** | `ZWarden.Diagnostics` (`SupportPackageSanitizer`) | every untrusted string (check `Detail`, env values) | control-stripped, bounded strings | no (transforms) |
| **Redact** | `ZWarden.Diagnostics` (`SupportPackageRedactor` → `Redaction`) | sanitized fields | sensitive-key values masked `***` | no (transforms) |
| **Pseudonymize** | `ZWarden.Diagnostics` (`Pseudonymizer`) | redacted text | PII → `<HOST-n>`/`<PLAYER-n>`/`<*-IP-n>` | no (transforms) |
| **Secret scan** | `ZWarden.Diagnostics` (`SecretScanner`) | the fully-sanitized payload | clean, or the first detection | **YES — `SecretDetected` ⇒ abort, audit, no emit (D-3)** |
| **Validate** | `ZWarden.Diagnostics` (`PackageValidator`) | assembled documents | shaped + scan-clean | **YES — malformed ⇒ abort** |
| **Package** | Web (`ZipSupportPackageWriter`) | validated documents | `.zip` (manifest + diagnostics.json), per-entry SHA-256 | on I/O error |

Every string that originates from a Server, its mods, its config, its certificate, or its RCON is **untrusted**
(§8) and flows through Sanitize → Redact → Pseudonymize → Secret-scan before it can reach the ZIP. ZWarden-authored
`Summary` text and environment facts are safe but pass through the same pipeline uniformly (defence in depth; the
scanner still runs over them).

## Package contents

```text
support-package-<DiagnosticId>.zip
 ├─ manifest.json      DiagnosticId, schemaVersion, generatedAtUtc, zwardenVersion, dbProvider,
 │                     os/runtime, scope (tenant | server:<pseudonymized-id>), worstStatus,
 │                     entries[{ name, sha256, bytes }], secretScan: { ran:true, clean:true }
 └─ diagnostics.json   the sanitized DiagnosticReport (every domain: status, ZWarden summary,
                       sanitized+redacted+pseudonymized detail) + environment facts
```

Markdown rendering, an AI-context document, and the prompt-injection delimiters are **F31** (PRD 52), not F30. F30
emits JSON inside a ZIP with a machine-readable manifest.

## Scope, by PR (TDD, security-critical core first)

### PR-A — The sanitization pipeline (branch `feat/f30-support-package-pipeline`)

The pure, I/O-free core — the part that must never regress — shipped and exhaustively tested before any packaging
or UI exists.

1. **`DiagnosticId`** — a Guid-backed typed id in `ZWarden.Domain.Ids` (ADR 0014 pattern), documented as a
   **correlation id**, not an entity key (nothing persists it). `New()` mints one; `ToString()` is the manifest/
   audit form.
2. **Models (`ZWarden.Diagnostics/SupportPackage/`):** `SupportPackageContent` (the collected, not-yet-sanitized
   report + `EnvironmentFacts`), `SupportPackageManifest`, `SupportPackageDocument` (a named byte payload with its
   SHA-256), and the typed `SupportPackageResult { Succeeded, Package?, Failure? }` with
   `SupportPackageFailure { NotAuthorized, SecretDetected, InvalidContent }`.
3. **Stages (pure, `internal` where possible, each a small deep unit):** `SupportPackageSanitizer` (control-strip,
   reusing the `LogLineSanitizer` approach — extract the shared strip logic rather than copy it),
   `SupportPackageRedactor` (over `Redaction.RedactValueFor`/`IsSensitiveKey`), `Pseudonymizer` (first-seen
   per-package map; host/player/IP classifiers, RFC1918 split), `SecretScanner` (the detector set below),
   `PackageValidator`.
4. **`ISupportPackageBuilder` / `SupportPackageBuilder`** — sequences Collect-output → Sanitize → Redact →
   Pseudonymize → **Secret-scan (fail-closed)** → Validate, returning the built (still un-zipped)
   `SupportPackageDocument`s + manifest, or the typed failure. **No I/O, no ZIP** here.
5. **ADR 0034** (the pipeline + the fail-closed gate) lands here.
6. **Tests (`ZWarden.Diagnostics.Tests`):** per-stage units — Sanitize (ANSI/CSI/OSC/C0/C1 stripped; tab kept;
   already-bounded detail unchanged); Redact (each sensitive-key token masked; benign passthrough); Pseudonymize
   (**consistency within a package**; distinct entities get distinct tokens; RFC1918 vs public split; idempotent on
   an already-tokenized value); **SecretScanner (each detector positive/negative; a planted RCON password / DB
   connection string / PEM block / JWT is caught)**; Builder end-to-end (clean content ⇒ success with a complete
   manifest; **planted secret ⇒ `SecretDetected`, no documents returned**; unauthorized never reaches a stage).
   Bump the `ZWarden.Diagnostics.Tests` `--minimum-expected-tests` floor (csproj + the `offline`/`silent-drop-guard`
   ci.yml wiring — [[web-tests-discovery-floor-bump]] applies to this project's floor too).

### PR-B — Collector, packager, endpoint, UI (closes #50; branch `feat/f30-support-package-surface`)

The thin Web-side shell around the PR-A core.

1. **`SupportPackageCollector` (Web/Infrastructure):** fail-closed authorize **`Diagnostics.Export`** (tenant-wide,
   ADR 0018), run `IDiagnosticsService.RunAsync` (or `RunForServerAsync` for a targeted server), gather
   `EnvironmentFacts` (assembly version, `DbProvider`, `RuntimeInformation`), and hand a `SupportPackageContent` to
   the builder.
2. **`ZipSupportPackageWriter` (Web):** write the builder's documents into a ZIP (`System.IO.Compression`,
   deterministic entry order), computing each entry's SHA-256 into the manifest — the F24/ADR 0028 checksum posture
   (mirror `TarGzBackupArchiver`'s hashing, not its tar format). Returns bytes + total size; the endpoint streams it.
3. **Endpoint (`DiagnosticsEndpoints`):** `POST /api/diagnostics/support-package` (tenant host report) and
   `POST /api/servers/{id}/diagnostics/support-package` (per-server; Server resolved through the tenant filter,
   foreign/unknown ⇒ 404), both behind `.RequireAuthorization(Diagnostics.Export)`. Success ⇒
   `application/zip` with a `Content-Disposition` download filename `support-package-<DiagnosticId>.zip`.
   `SecretDetected`/`InvalidContent` ⇒ a `Results.Problem` (409/422) that names the failure **without** the
   offending content; `NotAuthorized` ⇒ 403.
4. **Audit:** a new `DiagnosticsAuditActions.Export = "Diagnostics.Export"`; write **Succeeded** (DiagnosticId,
   worst status, byte size, `secretScanClean=true`) and **Failed** (DiagnosticId, reason `SecretDetected` — never
   the detected value) events (ADR 0019).
5. **UI (Blazor, Bb components, [[prefer-blueprint-over-raw-html]] / [[blueprint-seam-on-ssr-forms]]):** an
   **Export support package** action on the diagnostics surface (the F29 server-detail Diagnostics card + the
   host/tenant view), gated `Diagnostics.Export`. It POSTs and downloads the ZIP; a `SecretDetected` failure shows
   a non-alarming, non-leaking message ("Export blocked: a potential secret was detected and the package was not
   generated"). Reuse `StatusBadge`; no new untrusted rendering (the ZIP is a download, not rendered).
6. **Docs:** ADR 0034 finalized; `docs/pzserver-architecture.md` gains a "Support package" row; `CONTEXT.md` gains
   the **support package** / **pseudonym** / **secret scan** / **DiagnosticId** terms; cross-reference F31 as the
   consumer that turns the sanitized report into AI context.
7. **Tests:** collector authz (fail-closed; tenant/server-scoped; foreign server 404); `ZipSupportPackageWriter`
   round-trip (entries present, per-entry SHA-256 matches, manifest lists them); endpoint status codes (200
   `application/zip`; 403; 409/422 on `SecretDetected` with **no content leak** in the body); bUnit render/gating
   (action hidden without `Diagnostics.Export`); audit written on success **and** on the secret-scan abort. Bump the
   Web.Tests floor in **both** the csproj and `ci.yml` ([[web-tests-discovery-floor-bump]]); `npm run build:css` +
   commit `wwwroot/app.css` if a new utility appears ([[tailwind-app-css-rebuild]]).

## Secret-scanner detector set (PR-A)

Conservative, content-based, run over the fully-sanitized payload (D-3). Initial set:

- **PEM private-key blocks** — `-----BEGIN (RSA |EC |OPENSSH |PGP )?PRIVATE KEY-----`.
- **JWT** — `eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+`.
- **Bearer/authorization** — `Bearer\s+[A-Za-z0-9._-]{16,}`.
- **Cloud-key shapes** — AWS `AKIA[0-9A-Z]{16}`; generic `xox[baprs]-` Slack-style; long `ghp_`/`github_pat_`.
- **Connection strings** — `(Password|Pwd|User Id|Server)=…;` DB-style pairs; `postgres://user:pass@…`.
- **High-entropy blobs** — a base64/hex run over a length + Shannon-entropy threshold (tuned to avoid hashes we
  legitimately emit, e.g. the manifest's own SHA-256s, which are allow-listed by field).

Each detector is unit-tested positive **and** negative; the entropy detector has explicit non-firing cases
(SHA-256 hex, a GUID, a pseudonym token) so a benign payload never fails. A detection reports only the detector
name + offset for the audit/error — **never the matched value**.

## Non-scope

- **Persistence, history, re-download, retention** — D-2; a package is transient. No `SupportPackage` entity, **no
  migration**.
- **The AI troubleshooting context** (Markdown/JSON export, untrusted-data delimiters, prompt-injection warning,
  safe-action vocabulary, copy-to-clipboard) — **F31** (post-1.1, PRD 52). F30 stops at the sanitized JSON-in-ZIP
  bundle and carries the untrusted-data guarantee forward.
- **New diagnostics / remediation** — F30 collects the F29 report; it runs no new probe and remediates nothing.
- **Collecting logs or audit history into the package** — out of scope (the collector takes the F29 report + env
  facts only); a richer bundle is a later increment.
- **A new permission** — `Diagnostics.Export` already exists (tenant-wide, seeded into Owner). No catalogue or
  built-in-role change; `PermissionCatalogueTests`/`BuiltInRolesTests` stay untouched and green.
- **Cross-package-stable pseudonyms** — D-4; per-package only.

## Domain / contract / persistence changes

- **Domain:** a `DiagnosticId` typed id (`ZWarden.Domain.Ids`, correlation id — not an entity key). **No entity,
  no permission change.**
- **Contracts:** **none** — F30 is Web-side; it triggers no new Agent command (it reuses the F29 gather/report
  seam). `ProtocolVersion.Current` unchanged; closed-vocabulary guard untouched.
- **Persistence:** **none** — no new entity, **no migration** (D-2). The only durable record F30 writes is the
  `Diagnostics.Export` audit event.
- **Solution:** `ZWarden.Diagnostics` gains the `SupportPackage/` namespace; the existing
  `ZWarden.Diagnostics.Tests` and `ZWarden.Web.Tests` projects gain the new tests (floors bumped).

## Test plan (TDD, per PR)

- **PR-A (pure, offline):** stage units first (Sanitize / Redact / Pseudonymize / **SecretScanner each
  detector** / Validate); `SupportPackageBuilder` end-to-end (clean ⇒ complete manifest; **planted secret ⇒
  `SecretDetected`, nothing emitted**; unauthorized short-circuits); `DiagnosticId` round-trip. New floor registered.
- **PR-B (Web, offline + bUnit):** collector authz (fail-closed / tenant / per-server 404); `ZipSupportPackageWriter`
  round-trip + per-entry SHA-256; endpoint 200-zip / 403 / 409-422-no-leak; bUnit gating; audit on success **and**
  on the secret-scan abort.
- **Architecture tier:** assert the `SupportPackage` pipeline namespace has no `System.IO`/`ZipArchive`/HTTP
  dependency (the pure-core guarantee, D-1) — the `ZWarden.ArchitectureTests` layering pattern.
- **Integration tier (`[Category("Networked")]`, deferrable/opt-in as in F16/F29):** against a real PZ server, run a
  full per-server export, unzip the result, and assert the manifest's SHA-256s verify, no raw RCON password / config
  secret survives, and PII is tokenized.

## Diagnostics (security posture)

- **Fail-closed, tenant/server-scoped:** the export authorizes `Diagnostics.Export` (tenant-wide) before any
  collection; a targeted server resolves through the tenant filter (foreign/unknown ⇒ 404). Nothing mutates.
- **Fail-closed secret gate (the point of the feature):** the scanner runs over the fully-sanitized payload and a
  detection **aborts** generation — the ZIP is never written and the abort is audited (PRD 51; D-3). A false
  positive fails safe.
- **All collected text is untrusted (§8 / PRD 53):** every Server-, mod-, config-, cert-, RCON-sourced string is
  sanitized (control-stripped, bounded), redacted (sensitive keys), and pseudonymized (PII) before it can reach the
  ZIP. The bundle is a download, never rendered as markup.
- **No secret in the bundle by construction:** redaction masks sensitive keys, the RCON credential never left the
  Agent to begin with (§5, ADR 0026), the TLS check only ever held the *public* cert, and the scanner is the
  backstop. The manifest carries statuses, sizes, and SHA-256s — never a credential.
- **The audit record never leaks:** `Diagnostics.Export` events carry the `DiagnosticId`, worst status, byte size,
  and scan verdict — never the untrusted contents and never a detected secret's value (only its detector name).

## Documentation

- **ADR 0034** — "The sanitized support package is a transient, fail-closed collect→sanitize→redact→scan→validate→
  package pipeline": why a pure I/O-free core (D-1), why transient with no entity/migration (D-2), why the secret
  scan is a last-line gate that fails rather than emits (D-3), and why pseudonyms are per-package only (D-4).
- `docs/pzserver-architecture.md` — add the "Support package" row (Web-side; reuses the F29 report; sanitize/redact/
  pseudonymize/secret-scan; ZIP download; `Diagnostics.Export`; transient).
- `CONTEXT.md` — add **support package**, **pseudonym**, **secret scan**, and **DiagnosticId** terms.
- Cross-reference F30 from F31's future plan as the sanitized-report producer; note the `Redaction` reuse.

## Acceptance criteria

1. An operator with **`Diagnostics.Export`** can generate a support package (tenant host, or a targeted server) and
   download a **ZIP** containing a `manifest.json` (`DiagnosticId`, schema version, generation time, environment
   facts, per-entry SHA-256, secret-scan attestation) and a sanitized `diagnostics.json` (every F29 domain).
2. The package is produced by the **Collect → Sanitize → Redact → Secret-scan → Validate → Package** pipeline
   (PRD 51); every untrusted string is control-stripped, sensitive keys are masked, and operational PII is
   pseudonymized with **per-package-consistent** tokens (`<HOST-1>`, `<PLAYER-2>`, `<PRIVATE-IP-1>`,
   `<PUBLIC-IP-1>`; PRD 53).
3. **The secret scan fails closed:** a planted RCON password / DB connection string / PEM key / JWT is detected and
   the package is **not generated** (a typed `SecretDetected` failure + an audited abort, no partial ZIP, no leaked
   value) — PRD 51's "fail rather than knowingly emit."
4. **`DiagnosticId`** is a correlation id stamped into the manifest and every `Diagnostics.Export` audit event
   (success and secret-scan abort), matching PRD 49's treatment alongside `OperationId`/`AgentId`/`ServerId`.
5. Authorization is **fail-closed and tenant/server-scoped**; a foreign/unknown targeted server is 404;
   `Diagnostics.Export` is used **as-seeded** with **no catalogue or built-in-role change**
   (`PermissionCatalogueTests`/`BuiltInRolesTests` untouched and green).
6. **No new entity and no migration** — a package is transient; the only durable record is the audit event. F30
   adds no Agent contract (`ProtocolVersion.Current` unchanged).
7. The `SupportPackage` pipeline core has **no I/O dependency** (architecture-tested); its stages are exhaustively
   unit-tested including the fail-closed path.
8. Offline unit tier green (stages + builder + collector + writer + endpoint + audit + bUnit gating); the networked
   export integration test passes on demand; **ADR 0034** written; docs updated; CI green (new
   `ZWarden.Diagnostics.Tests`/`Web.Tests` floors wired into `offline`/`silent-drop-guard`).

## Definition of Done

Per PRD 61: acceptance criteria met; tests authored first (TUnit units for every pipeline stage incl. each secret
detector and the **planted-secret abort**; the `ZipSupportPackageWriter` SHA-256 round-trip; endpoint status codes
with no content leak; bUnit gating; audit on success and abort; a networked full-export integration test,
deferrable to the opt-in tier as in F16/F29); the pipeline **collects and sanitizes** and **remediates nothing**
(scope); the secret scan **fails closed** (PRD 51); all collected text untrusted, sanitized, redacted, and
pseudonymized (§8 / PRD 53); fail-closed tenant/server-scoped authorization on every export; `Diagnostics.Export`
used as-seeded with no catalogue/role change; a package is transient with **no new entity and no migration**; the
pipeline core is I/O-free (architecture-tested); ADR 0034 written; docs updated; CI green.
