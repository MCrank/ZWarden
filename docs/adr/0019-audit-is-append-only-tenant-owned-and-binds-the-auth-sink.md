# 19. The audit store is an append-only, tenant-owned table, and F6 binds the durable authentication-event sink

**ZWarden's audit is a durable, queryable database record — an `AuditEvent` (`aud-`) that is
`ITenantOwned`, append-only, and never a log line.** F6 writes audit events through an
`IAuditWriter` seam, reads them through a tenant-filtered `IAuditQuery`, and shows them in an
`Audit.View`-gated administrative viewer. It also **binds the durable `IAuthenticationEventSink`
F4 deferred**: authentication events are mapped to audit events and persisted, superseding the
logging-only stub. Structured operational logging (Serilog) is a **separate, later** concern — an
audit event is not a log entry (`CONTEXT.md`), and F6 introduces no logging framework.

- Status: accepted
- Decided in: #28 (F6 — Audit System); builds on #26/#27 (F4/F5) and ADR 0016/0018
- Bears on: PRD 11 (authentication events), PRD 12/12A (`Audit.View`), PRD 2.1/2.2; ADR 0004
  (native-uuid typed ids), ADR 0016 (tenant filter + default tenant), ADR 0018 (ZWarden-owned RBAC)

## Context

F1 already reserved the `aud-` prefix (`AuditEventId`) and F5 already put `Audit.View` in the closed
permission catalogue as a **tenant-wide, sensitive** permission (the built-in Viewer role excludes
it). F4 deliberately emitted authentication events — sign-in success/failure, lockout, MFA, password
reset, external-login link — through an `IAuthenticationEventSink` with only a **logging** default,
stating in the code and its plan that "F6 owns the durable, queryable audit store and binds a real
sink over the same seam." So F6 is greenfield on top of finished foundations, with two forces:

1. **What "audit" means here.** The glossary is explicit that an Audit event is *not* a log entry:
   it must be durable, queryable, filterable, and shown to an administrator. That is a database table,
   not a log sink. The user's stated preference for Serilog is about *operational* logging, a distinct
   concern that would be a new dependency and its own host-wiring/ADR decision — out of F6's scope.
2. **The tenant of a pre-authentication event.** The audit store is tenant-owned and fails closed
   (ADR 0016). Some authentication events (a failed sign-in for an unknown address) have no subject
   and, in a hosted deployment, no resolvable tenant. But v1.0 ships **self-hosted**, where
   `ClaimsPrincipalTenantContext` resolves an unauthenticated request to the **default tenant**
   (`HasCurrentTenant` is always true; ADR 0016). So in the deployment mode we are shipping, *every*
   authentication event has a clean tenant home; the tenant-less case exists only under a future
   hosted `ITenantContext` (F3B, v1.1) that does not fall back.

## Decision

- **`AuditEvent` (`aud-`) is a Domain entity, `ITenantOwned`, and append-only.** It carries only
  non-secret data: the ambient `TenantId` (stamped by the ownership interceptor), `OccurredAt` (from
  `TimeProvider`), a stable machine-readable `Action` string (the audit currency, mirroring how
  permission names are the authorization currency — never an ad-hoc enum a later feature must edit),
  an `AuditOutcome` (`Succeeded` / `Failed` / `Denied` — a genuinely closed, stable set), an optional
  actor `UserId`, an optional `ServerId` (a first-class filter axis), an optional `CorrelationId`, and
  an optional non-secret `Detail`. Every property is `init`; there is **no update or delete path** and
  **no `IVersioned` concurrency token** — append-only is the point, and a mutable version would
  contradict it.
- **`IAuditWriter` (Application seam) is the only way to record an audit event.** It appends through
  the ambient tenant context and clock; it never mutates. `IAuditQuery` (Application seam) is the only
  way to read them, over a `TenantScopedRepository<AuditEvent>` — so a query is tenant-scoped by
  construction and no `IgnoreQueryFilters()` path exists.
- **Correlation is ambient.** An `ICorrelationContext` seam yields the current request/operation
  correlation id (default implementation: `System.Diagnostics.Activity.Current`), stamped onto each
  event so related events share an id without the caller threading one through.
- **F6 binds the durable authentication sink.** An `AuditAuthenticationEventSink` maps each
  `AuthenticationEvent` to an `AuditEvent` (`Action = "Authentication.<Kind>"`, outcome derived from
  the kind) and persists it via `IAuditWriter`, **superseding** `LoggingAuthenticationEventSink`. Like
  the seam requires, it never throws into the auth path — a sink failure is swallowed and logged, never
  a blocked sign-in.
- **The administrative viewer is `Audit.View`-gated server-side.** The Blazor page carries
  `@attribute [Authorize(Policy = "Audit.View")]` (real server-side authorization under Blazor Server,
  not mere `AuthorizeView` visibility), and reads through the tenant-filtered `IAuditQuery`.
- **Serilog is not introduced.** F6 keeps `Microsoft.Extensions.Logging`. Adopting Serilog as the
  operational logging framework is a separate later decision with its own ADR.

## Alternatives considered

- **Route audit through Serilog now.** Rejected for F6: a log sink is not queryable/filterable by an
  administrator, the glossary separates the two concepts, and it drags in a new dependency + host
  wiring + a `SecretString` redaction policy that exceed #28. Kept available as the future operational
  logging choice (the user's stated preference), unblocked by this decision.
- **A closed `AuditCategory` enum instead of a stable `Action` string.** Rejected: every later
  feature that audits (operations, configuration, backups) would have to edit a Domain enum and its
  closed-set test — the same brittleness ADR 0018 avoided for permissions by using stable names.
- **Make `AuditEvent` `IVersioned`.** Rejected: append-only records are never updated, so a
  concurrency token is dead weight that muddies the append-only contract.
- **Defer the authentication-sink binding to a follow-up.** Rejected: self-hosted is precisely the
  easy case (default tenant always resolves), so binding now closes F4's explicit loop cheaply; only
  the hosted tenant-less edge is deferred, and it is documented below.

## Consequences

- Authentication events become durable and queryable in v1.0 self-hosted, filed under the default
  tenant. The **hosted, pre-authentication, tenant-less case is a known deferred gap**: when a hosted
  `ITenantContext` that does not fall back lands (F3B, v1.1), tenant-less authentication events need a
  system/tenant-agnostic audit stream — out of scope here and called out so it is deliberate, not
  missed.
- The audit store grows unbounded (append-only). Retention/rotation is **not** in v1.0 (notifications
  and alerting are already out per #28); it is a foreseeable later concern.
- `AuditEvent.Detail` and `Action` must stay non-secret, exactly as `AuthenticationEvent` already
  guarantees; the durable sink cannot log a credential because the source event carries none.
- Because the durable sink supersedes the logging one, the previous per-event info log line is
  replaced by the durable record (a sink failure still logs a warning). Operational log output for
  authentication is a Serilog-era concern.
