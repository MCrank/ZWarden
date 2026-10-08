# Issue #346 Mini-Plan — Editable session idle timeout

**Status:** one PR (branch `feat/346-session-timeout`, stacked on #345's branch, closes #346). Third sub-issue of
epic #350.

**Written against:** issue #346, ADR 0048 (the settings store from #345), PRD 11 (bounded sliding session), #297
(security-stamp revalidation every minute).

## Objective

The Owner picks the operator session idle timeout under Settings → Security from a bounded list. The default stays
8 hours. The change applies without a restart: new sign-ins get it at once, and existing sessions pick it up on their
next request. It is audited.

## Facts

- The cookie handler reads `ExpireTimeSpan` only at sign-in (`ExpiresUtc = IssuedUtc + ExpireTimeSpan`). A sliding
  renewal keeps the ticket's own span (`ExpiresUtc - IssuedUtc`), and it renews once more than half the span has
  passed. So an options-monitor change alone would never reach a session that already exists.
- `OnValidatePrincipal` runs on every authenticated request, after the expiry check. Today it is
  `SecurityStampValidator.ValidatePrincipalAsync` (#297).

## Decisions

- **D1: the setting.** `ControlPlaneSettings.SessionIdleTimeout` (`TimeSpan?`). The choices are 30 minutes, 1 hour,
  8 hours, 24 hours and 7 days. The domain refuses anything else, and choosing the default (8 hours) stores null. The
  default moves into the domain (`ControlPlaneSettings.DefaultSessionIdleTimeout`), and `SessionLifetime` points at
  it.
- **D2: the permission is `Tenant.Manage` (Owner only)**, as the issue says. A session lifetime is a security
  posture. The change is audited as `Settings.SessionTimeoutChanged`, old → new.
- **D3: applied through cookie events.**
  - `OnSigningIn` sets `ExpiresUtc = IssuedUtc + timeout`.
  - `OnValidatePrincipal` runs the security-stamp validator first. Then, if the ticket's span differs from the
    tenant's timeout, it either signs the session out (when idle for longer than the new timeout since its last
    renewal) or re-spans the ticket to the new timeout and renews it.
  - The timeout comes from the #345 settings cache (no query per request), keyed by the principal's tenant claim. It
    falls back to the default when the cache isn't registered or there is no tenant.
- **D4: the UI.** Security → Session timeout gets a select and Save for the Owner, read-only for everyone else. The
  hint says "Existing sessions pick up a change on their next request".

## Tests (TDD)

- Domain: the choices, default stores null, anything else refused.
- Infrastructure: the service sets, clears and audits; it denies an Administrator. Cookie events: sign-in uses the
  tenant's timeout; a shorter timeout on an idle-enough ticket rejects it; a shorter timeout on a fresh ticket
  re-spans and renews; an unchanged span does nothing.
- Web: Security shows the select for the Owner and read-only for an Administrator; saving changes the stored value
  and the summary.

## Not in scope

- An interactive page that stays open keeps its circuit until the next full request or the #297 revalidation. The
  timeout applies to the cookie (each request), not to an open circuit's idle time.
