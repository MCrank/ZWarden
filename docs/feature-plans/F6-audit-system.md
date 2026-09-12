# Feature 6 Mini-Plan — Audit System

**Status:** draft, for review. Roadmap issue: [F6 (#28)](https://github.com/MCrank/ZWarden/issues/28). Track A,
immediately after F5. **Depends on F5** (#27, closed) as a native GitHub issue dependency.

**Format:** PRD 60. **Written against:** PRD §11 (authentication events are recorded and queryable — F6 owns
the durable store F4 deferred), PRD §12/12A (`Audit.View` is a tenant-wide, sensitive permission already in the
F5 catalogue; enforcement is server-side, *hiding a control is not authorization*), §2.1 (no invisible stale
sets), §2.2 (TDD), §7A (tenant derives from the session, never the browser); the foundations F6 builds on —
ADR [0016](../adr/0016-tenant-isolation-query-filter-and-default-tenant.md) (the tenant filter every audit read
passes through, and the **default tenant** an unauthenticated self-hosted request resolves to), ADR
[0004](../adr/0004-typed-ids-are-stored-as-native-uuid.md) (native-uuid typed ids), ADR
[0014](../adr/0014-typed-id-pattern.md) (`AuditEventId`/`aud-` **already in the registry** — F6 adds **no
prefix**), and F4's `IAuthenticationEventSink` seam (F6 binds the durable sink over it). It writes one new ADR,
**0019 (audit is append-only, tenant-owned, and binds the auth sink)**, for the load-bearing decisions.

## Objective

Deliver the control plane's **audit** capability: a durable, queryable record of security- and
administration-relevant occurrences. Concretely: an append-only, tenant-owned **`AuditEvent`** (`aud-`) carrying
only non-secret data; an **`IAuditWriter`** seam that appends through the ambient tenant and clock and stamps a
**correlation id**; an **`IAuditQuery`** seam that reads through the tenant filter with **filtering** (time,
action, actor, server, outcome, correlation); an **administrative viewer** gated by `Audit.View` server-side;
and the **durable `IAuthenticationEventSink`** F4 deferred, so the authentication events F4 already emits become
durable audit records. Everything ships fail-closed and tenant-isolated, proven test-first (PRD 2.2) against the
two-tenant fixture. Structured operational logging (Serilog) is explicitly **not** introduced (ADR 0019).

## Dependencies

- **F5 — Authorization** (#27). F6 consumes F5's `Audit.View` permission (tenant-wide, sensitive; the built-in
  Viewer role already excludes it) and its dynamic policy provider, so `@attribute [Authorize(Policy =
  "Audit.View")]` gates the viewer with **no** policy registration. F6 also audits the kinds of administrative
  change F5 shaped its create/adjust and grant/revoke paths to be auditable (those bindings land as the owning
  features apply them; F6 provides the writer).
- **F4 — Identity and Authentication** (#26). F6 binds a durable sink over F4's `IAuthenticationEventSink`,
  turning the events F4 emits (`SignInSucceeded/Failed`, `LockedOut`, `MfaVerified`, `PasswordReset`,
  `ExternalLoginLinked`) into audit records. `AuthenticationEvent` already carries **only non-secret** data.
- **F3A / F2 / F1** (via ADR 0016/0004/0014). `AuditEvent` is `ITenantOwned`, so the ownership interceptor
  stamps the tenant and the filter scopes every read; it ships **one migration per provider** (SQLite +
  Postgres); it reuses the existing `AuditEventId` (`aud-`) — no new prefix. In self-hosted v1.0 an
  unauthenticated request resolves to the **default tenant** (ADR 0016 / `ClaimsPrincipalTenantContext`), which
  is what gives even a pre-auth authentication event a clean tenant home.
- **F0** — offline tier for unit/model/architecture tests; networked tier for the per-provider migration;
  warnings-as-errors (ADR 0013).

## Scope

1. **`AuditEvent` (Domain, `aud-`, `ITenantOwned`, append-only).** A sealed entity carrying only non-secret
   data: `Id` (`aud-`), the ambient `TenantId` (init-only, ADR 0016), `OccurredAt` (`DateTimeOffset`), a stable
   machine-readable **`Action`** string (the audit currency — e.g. `Authentication.SignInSucceeded`,
   `Role.Created`), an **`AuditOutcome`** (`Succeeded`/`Failed`/`Denied` — a closed, stable enum), an optional
   actor `UserId?`, an optional `ServerId?` (a first-class filter axis), an optional `CorrelationId` string, and
   an optional non-secret `Detail`. **Every property is `init`; there is no update/delete path and no
   `IVersioned` token** — append-only is the contract (ADR 0019). A named static factory constructs it.
2. **`IAuditWriter` (Application seam) + `AuditWriter` (Infrastructure).** The one way to record an audit event:
   `WriteAsync(AuditEntry entry, ct)`. It appends through the ambient `ITenantContext` (stamped by the
   interceptor), sets `OccurredAt` from `TimeProvider`, and stamps the `CorrelationId` from `ICorrelationContext`.
   It never mutates or deletes. `AuditEntry` is a small non-secret Application record (`Action`, `Outcome`,
   optional `ActorUserId`, `ServerId`, `Detail`).
3. **Correlation (`ICorrelationContext` seam + `ActivityCorrelationContext`).** The ambient request/operation
   correlation id, so related events share one without threading it through call sites. Default implementation
   reads `System.Diagnostics.Activity.Current` (ASP.NET Core opens an Activity per request); a null current
   correlation is allowed (the field is optional).
4. **`IAuditQuery` (Application seam) + `AuditQueryService` (Infrastructure) with filtering.** Reads through a
   `TenantScopedRepository<AuditEvent>` — tenant-scoped by construction, no `IgnoreQueryFilters()`. The filter
   (`AuditQuery`) covers a time range, `Action`, actor, `ServerId`, `Outcome`, and `CorrelationId`, ordered
   newest-first and paged (`skip`/`take`). Returns an `AuditEventView` projection safe to hand to the Web layer.
5. **Durable authentication-event sink (Infrastructure).** `AuditAuthenticationEventSink : IAuthenticationEventSink`
   maps each `AuthenticationEvent` → an `AuditEvent` (`Action = "Authentication." + Kind`; `Outcome` derived —
   `SignInFailed`/`LockedOut` ⇒ `Failed`, the rest ⇒ `Succeeded`) and persists it via `IAuditWriter`. It
   **supersedes** `LoggingAuthenticationEventSink` and, per the seam's contract, **never throws into the auth
   path** — a sink failure is caught and logged, never a blocked sign-in. Self-hosted default-tenant path per
   ADR 0019; the hosted tenant-less pre-auth case is the documented deferred gap.
6. **Per-provider migration.** The `AuditEvents` table as the fourth migration in `ZWarden.Migrations.Sqlite`
   and `ZWarden.Migrations.Postgres`, with native-uuid typed-id columns (ADR 0004), the `TenantId` column, and
   indexes on `(TenantId, OccurredAt)` and `(TenantId, CorrelationId)` for the viewer's common reads.
7. **Administrative viewer (Web) + host wiring + guards/docs.** A Blazor page (`/audit`,
   `@attribute [Authorize(Policy = "Audit.View")]`, `@rendermode InteractiveServer`) that reads through
   `IAuditQuery` and renders a filterable, paged table (Blazor Blueprint per ADR 0003; regenerate `app.css`).
   A nav entry gated by `AuthorizeView(Policy = "Audit.View")`. `Program.cs` gains
   `builder.Services.AddZWardenAudit()` after `AddZWardenAuthorization()` (registering the writer, query,
   repository, correlation context, and the durable sink that supersedes the logging default). Guards: the
   audit entity is `ITenantOwned` and filtered (model guard); no `IgnoreQueryFilters()`; Domain/Application stay
   framework-free; ADR 0019, `CONTEXT.md`, `CONTRIBUTING.md`, and the ADR README index (adding the missing
   **0018** row and the new **0019** row).

## Non-scope

- **Notifications or alerting** on audit events — out (#28; post-1.1). F6 records and shows; it does not notify.
- **Serilog / a structured operational-logging framework** — out (ADR 0019). An audit event is not a log entry;
  Serilog remains the intended *operational* logging choice for a later observability feature, unblocked by F6.
- **Retention / rotation / archival** of the append-only store — out (a foreseeable later concern; ADR 0019).
- **The hosted, tenant-less pre-authentication audit stream** — deferred with hosted multi-tenancy (F3B, v1.1).
  v1.0 self-hosted files every event under the default tenant (ADR 0019).
- **Auditing every mutating call site** — F6 delivers the writer and proves it on the authentication sink;
  wiring `IAuditWriter` into each operation/config/role path lands with the feature that owns that path (the
  paths F5 already shaped to be auditable), not as a sweep here.
- **Exporting audit** (the redacted Support Package) — a Diagnostics concern, not F6.

## Domain changes

- **`AuditEvent` (Domain).** Append-only, `ITenantOwned`, non-secret; `AuditEventId Id` (existing prefix).
  Framework-free (no EF, no ASP.NET Core) — the EF configuration, repository, writer, query service, and sink
  are Infrastructure.
- **`AuditOutcome` enum (Domain).** `Succeeded` / `Failed` / `Denied`. Closed and stable.
- **Glossary (`CONTEXT.md`), terms only:** **Audit event** (the durable, queryable, tenant-owned `aud-` record
  of a security/administration occurrence — *not* a log entry, and distinct from an **Authentication event**,
  which is the in-flight signal F4 emits and F6 persists), **Audit action** (the stable machine-readable name),
  and **Correlation id** (the ambient id tying related audit events together).

## Contract changes

- **`IAuditWriter`** — the one way to append an audit event; append-only by contract (no update/delete).
- **`IAuditQuery` / `AuditQuery` / `AuditEventView`** — the tenant-scoped read surface with filtering + paging.
- **`ICorrelationContext`** — the ambient correlation id seam.
- **`IAuthenticationEventSink`** — unchanged interface; F6 registers a durable implementation over it.
- **Persistence** — one new table (`AuditEvents`) on both providers; tenant-scoped.
- No Agent/SignalR/serialization contract changes (F6 is ZWarden.Web-internal).

## Security considerations

- **Non-secret by construction.** `AuditEvent.Action`/`Detail` carry no credentials — the durable sink cannot
  log one because `AuthenticationEvent` already carries none. Asserted, mirroring F4's contract.
- **Tenant-isolated reads.** Every query is over the ADR 0016 filter; a tenant sees only its own audit events,
  proven against the two-tenant fixture. No `IgnoreQueryFilters()` path exists (architecture guard).
- **Server-side enforcement (PRD 12).** The viewer is gated by `[Authorize(Policy = "Audit.View")]` — genuine
  server-side authorization under Blazor Server, not `AuthorizeView` visibility — and the read is tenant-filtered
  regardless. `Audit.View` is sensitive: the built-in Viewer role deliberately lacks it (F5).
- **Fail-closed writes.** An audit write appends under the ambient tenant; in self-hosted v1.0 that always
  resolves (default tenant). The durable auth sink never throws into the auth path (seam contract), so an audit
  failure degrades to a logged warning, never a blocked or falsified sign-in.
- **Append-only integrity.** No update/delete API and no version token; the store is add-only, so an audit
  record cannot be silently altered through the application.

## Test plan

Written before the code (PRD 2.2). Offline tier except the per-provider migration (networked). Isolation tests
use the two-tenant fixture.

1. **Entity is append-only & non-secret** — `AuditEvent` factory sets the fields; all properties are init-only;
   `AuditOutcome` maps as specified. (Domain)
2. **Persistence: tenant-stamped & isolated** — an event created under tenant A carries A's immutable
   `TenantId`; tenant B's context resolves none of A's events; changing `TenantId` throws
   `TenantScopeViolationException`. (Infrastructure, two-tenant fixture)
3. **Writer appends with clock + correlation** — `AuditWriter.WriteAsync` sets `OccurredAt` from a fake
   `TimeProvider` and `CorrelationId` from a fake `ICorrelationContext`, stamps the ambient tenant, and only
   ever adds. (Infrastructure)
4. **Query filtering** — `AuditQueryService` filters by time range, `Action`, actor, `ServerId`, `Outcome`, and
   `CorrelationId`, returns newest-first paged results, and never returns another tenant's events. (Infrastructure)
5. **Durable auth sink maps & persists** — each `AuthenticationEventKind` maps to the expected `Action`/`Outcome`
   and is persisted via the writer (spy/real); a failed sign-in and a lockout record as `Failed`. (Infrastructure)
6. **Auth sink never throws into the auth path** — when the underlying writer throws, `RecordAsync` completes
   (swallows + logs), so a sink failure cannot block a sign-in. (Infrastructure)
7. **Per-provider migration** — a fresh SQLite database has the `AuditEvents` table with typed-id column types,
   the `TenantId` column, and the indexes; the same migration applies on PostgreSQL (networked tier).
8. **Viewer requires Audit.View** — the audit page is gated by the `Audit.View` policy: a holder sees it, a
   non-holder is refused; rows render from `IAuditQuery`. (Web / bUnit)
9. **Architecture/model guards** — `AuditEvent` is `ITenantOwned` and carries a filter (model guard, reflection);
   Domain/Application reference no framework/IdP package; no `IgnoreQueryFilters()` in `src/`.

## Implementation slices

F6 is well within the PRD 59 ~100K guardrail (smaller than F5), so it ships as **one PR** on
`feat/f6-audit-system`, sliced TDD-first with one commit per slice (each green). If it unexpectedly breaks the
guardrail, split at the S5/S6 seam (store + writer/query, then sink + viewer). Slices:

- **S1 — `AuditEvent` + `AuditOutcome` (Domain).** *Verify:* test 1.
- **S2 — EF config + persistence + tenant-scoped repository (Infrastructure).** *Verify:* tests 2, 9 (model guard).
- **S3 — `IAuditWriter`/`AuditWriter` + `ICorrelationContext` (Application/Infrastructure).** *Verify:* test 3.
- **S4 — `IAuditQuery`/`AuditQueryService` + `AuditEventView` filtering (Application/Infrastructure).** *Verify:* test 4.
- **S5 — Durable `AuditAuthenticationEventSink` (Infrastructure), supersedes the logging default.** *Verify:* tests 5, 6.
- **S6 — Per-provider `Audit` migration (SQLite + Postgres).** *Verify:* test 7 (SQLite offline; Postgres networked).
- **S7 — `AddZWardenAudit` host wiring + admin viewer + nav + guards/docs (Web).** *Verify:* tests 8, 9.

## Diagnostics

- **An audit failure is legible, not silent.** The durable auth sink logs a warning on a write failure (it must
  not throw into the auth path), so a misconfiguration surfaces without falsifying or blocking a sign-in.
- **Correlation ties a story together.** Events sharing a request/operation share a `CorrelationId`, so an
  operator can reconstruct a sequence (e.g. an MFA verify following a sign-in) from the viewer.
- **The store is add-only and self-describing.** Stable `Action` strings and a closed `AuditOutcome` mean the
  viewer and future exporters read the record without source archaeology, and the append-only shape means a
  displayed event is the event as written.

## Documentation

- **ADR 0019 (new, written with this plan)** — *audit is append-only, tenant-owned, and binds the auth sink.*
  Records: `AuditEvent` shape and append-only/no-`IVersioned` rationale; `Action` as the stable currency (not an
  enum); ambient correlation; the durable sink superseding the logging one; the self-hosted default-tenant path
  and the hosted tenant-less deferred gap; and **why Serilog is not introduced** (audit ≠ log entry).
- **`CONTEXT.md`** — **Audit event**, **Audit action**, **Correlation id** (terms only), kept distinct from
  **Authentication event**.
- **`CONTRIBUTING.md`** — audit is append-only through `IAuditWriter`; read only through the tenant-filtered
  `IAuditQuery`; actions come from stable names, never ad-hoc strings; `Detail` is non-secret; the viewer is
  re-authorized server-side.
- **`docs/adr/README.md`** — add the missing **0018** row (F5) and the new **0019** row.

## Acceptance criteria

1. `AuditEvent` (`aud-`) is `ITenantOwned`, append-only (init-only, no update/delete, no version token), and
   non-secret; it reuses the existing prefix.
2. `IAuditWriter` appends through the ambient tenant + clock and stamps a correlation id; `IAuditQuery` reads
   through the ADR 0016 filter with time/action/actor/server/outcome/correlation filtering and paging; a tenant
   sees only its own events (two-tenant fixture).
3. Authentication events are **durable** — `AuditAuthenticationEventSink` supersedes the logging default, maps
   each kind to an `Action`/`Outcome`, persists via the writer, and never throws into the auth path.
4. The `AuditEvents` table ships on **both** providers with typed-id columns and the tenant/index columns.
5. The administrative viewer is gated by `Audit.View` **server-side** and renders tenant-filtered, filterable
   rows — UI visibility is never the enforcement (PRD 12).
6. **Serilog is not introduced**; `Microsoft.Extensions.Logging` remains (ADR 0019).
7. The offline tier is green; the networked tier proves the per-provider migration; no obsolete-API or nullable
   warnings (warnings-as-errors, ADR 0013).

## Definition of Done

Per PRD 61, the applicable subset: acceptance criteria met; tests authored first; unit, model, and architecture
tests green in the offline tier and the per-provider migration green in the networked tier; audit reads are
tenant-isolated and the viewer is enforced server-side, proven by test not merely intended; the append-only
contract holds; the durable authentication sink is bound and cannot block a sign-in; ADR 0019, `CONTEXT.md`,
`CONTRIBUTING.md`, and the ADR README index updated; `app.css` regenerated; CI green; no unresolved warnings.
Delivered as **one PR**. (Notifications/alerting, Serilog, retention, the hosted tenant-less stream, auditing
every call site, and audit export are N/A at F6 — owned by later features, v1.1, and other tracks respectively.)
