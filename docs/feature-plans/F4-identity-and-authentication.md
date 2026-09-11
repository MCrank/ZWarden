# Feature 4 Mini-Plan — Identity and Authentication

**Status:** draft, for review. Roadmap issue: [F4 (#26)](https://github.com/MCrank/ZWarden/issues/26). Track A, immediately after F3A.

**Format:** PRD 60. **Written against:** PRD §2.1 (no invisible stale security parameters), §2.2 (TDD),
§7A (tenant derived from the session, never the browser), §11 (authentication, MFA, recovery codes,
lockout, HttpOnly cookies), §63 ("the initial architecture shall not require external identity
providers"), §63A (`Auth0 Organization` and `Tenant` stay non-interchangeable); the trust boundaries
([`trust-boundaries.md`](../trust-boundaries.md) §2 Browser→Web, §6 Tenant→Tenant, §9 rules 2 and 6);
ADR [0006](../adr/0006-identity-hardening-and-deferred-passkeys.md) (the PBKDF2 iteration-count
configuration and deferred passkeys — the two Identity decisions this feature discharges),
ADR [0014](../adr/0014-typed-id-pattern.md)/[0004](../adr/0004-typed-ids-are-stored-as-native-uuid.md)
(`UserId`/`RoleId` already in the registry), ADR
[0015](../adr/0015-application-layer-secret-encryption.md) (secret-aware types + `ISecretProtector` for
MFA/recovery material), ADR [0016](../adr/0016-tenant-isolation-query-filter-and-default-tenant.md)
(the tenant filter this feature now supplies a *session-derived* context to). It writes one new ADR,
**0017 (the OIDC / identity-provider seam)**, for the load-bearing seam decision, and discharges the
F3A/F3B forward-reference by name: the session-derived `ITenantContext` "needs Identity, which is F4".

## Objective

Deliver first-party authentication for the control plane: **ASP.NET Core Identity** over the existing
`ZWardenDbContext` (now an `IdentityDbContext`), user management, login/logout, a NIST SP 800-63-4
password policy, HttpOnly secure session cookies, account recovery, **MFA (TOTP + recovery codes) with
lockout**, and **authentication audit events** — and, distinctively, **the OIDC / identity-provider
seam proven against a test double** so v1.1's Auth0 integration (F3B) plugs into a boundary that has
been exercised since v1.0. This is also the feature that first **derives the current tenant from the
authenticated session** (the F3A-reserved `ITenantContext` slot) and the first feature to **wire the
persistence, security, and tenant foundations into `Program.cs`** — F4 is "the first persisting
feature" F2/F3A deferred host wiring to. Everything ships fail-closed and is proven test-first (PRD 2.2).

## Dependencies

- **F3A** — the tenancy every user record is scoped by. F4's `ApplicationUser` is `ITenantOwned`; F4
  supplies the **session-derived `ITenantContext`** the F3A filter has been waiting for, registered
  ahead of `AddTenantFoundation` (its `TryAddScoped` means the first registration wins). The default
  tenant is the tenant every self-hosted user is created under.
- **F3** — the security foundation. MFA shared secrets and recovery codes are wrapped in `SecretString`
  / `Secret<T>` and protected through `ISecretProtector`; F4 copies `AddSecurityFoundation`'s DI shape
  for `AddIdentityFoundation`, and inherits F3's fail-closed posture.
- **F2** — the persistence foundation: `ZWardenDbContext`, the typed-id / `IVersioned` / tenant-filter
  model conventions (F4 adds Identity's tables beneath them), `UseZWardenProvider`, the two
  provider-specific migrations assemblies (F4 adds the `AspNet*` tables as the second migration on each
  provider), and `MigrateAndBootstrapDefaultTenantAsync`.
- **F1** — typed IDs. `UserId` (`usr-`) and `RoleId` (`rol-`) already exist; F4 consumes them as the
  Identity key type and adds no new prefix. (Authentication **audit** events are F6's `aud-`; F4 raises
  domain events / writes through a minimal seam, it does not build the audit store — see Non-scope.)
- **F0** — the offline tier for unit/model/architecture tests, the networked tier for the per-provider
  Identity migration, and warnings-as-errors (ADR 0013).
- ADR 0006 — the PBKDF2 iteration count (chosen, not inherited) and the passkey deferral.

## Scope

1. **`ApplicationUser` + `ApplicationRole` (Infrastructure, ADR 0014 keys; §9 rule 2/6).** Identity
   entities keyed on the existing typed IDs: `ApplicationUser : IdentityUser<UserId>, ITenantOwned` and
   `ApplicationRole : IdentityRole<RoleId>`. **They live in `ZWarden.Infrastructure`, never Domain or
   Application** — `IdentityUser<>` is a `Microsoft.AspNetCore.Identity` type, and both the "Domain
   references nothing" rule (§9 rule 2) and the "Domain/Application reference no external identity
   provider" rule (§9 rule 6) forbid an Identity/IdP dependency there. `ApplicationUser` carries the
   immutable `TenantId` (init-only, F3A ownership rule) so every user is scoped to a tenant.
2. **`ZWardenDbContext` becomes an `IdentityDbContext` (Infrastructure).** Change the base to
   `IdentityDbContext<ApplicationUser, ApplicationRole, UserId>` (typed-key overload). `base.OnModelCreating`
   already runs before `ApplyConventions`, so the `AspNet*` tables receive the typed-id value converters
   and — for `ApplicationUser` only — the tenant filter, with no reordering. The `AspNet*` tables are
   **not** tenant-owned except through `ApplicationUser`; roles are tenant-global in v1.0 (F5 owns the
   permission model). `Stores.SchemaVersion` stays at the default `Version1` (passkeys deferred, ADR 0006).
3. **Password hashing configured, not inherited (ADR 0006, PRD 2.1).** `PasswordHasherOptions` set
   explicitly: **PBKDF2-HMAC-SHA512**, `IterationCount` chosen against OWASP's then-current figure
   (220,000 for this PRF as of the date recorded), 128-bit salt, 256-bit subkey. The chosen integer and
   the date it was chosen are recorded in a documented constant *where the configuration lives*, so it is
   reviewable at the F40 gate. No hand-rolled derivation — Identity's `PasswordHasher<TUser>` performs the
   work; the existing guard forbidding `new Rfc2898DeriveBytes(` stays green by construction.
4. **Password policy = NIST SP 800-63-4 (ADR 0006 consequences).** `IdentityOptions.Password`: **15-char
   minimum**, 64-char maximum accepted, **no composition rules** (`RequireDigit`/`RequireUppercase`/
   `RequireLowercase`/`RequireNonAlphanumeric` all off), no periodic rotation, and a **breached-password
   blocklist check** via a custom `IPasswordValidator<ApplicationUser>` against an offline blocklist seam
   (no network call on the credential path). Salt ≥32 bits is satisfied by Identity's 128-bit salt.
5. **Secure session cookies (PRD 11, trust-boundaries §2).** Application cookie configured HttpOnly,
   `Secure` (always), `SameSite=Lax`, a bounded sliding expiration, and a fixed cookie name; login is a
   server-set cookie, never a browser-exposed bearer token. Antiforgery (already present) is retained.
   **Host-header validation** is added as the trust-boundary §2 precondition ADR 0006 records (a
   configured allowed-hosts list), so the boundary the deferred passkey feature makes acute is already in
   place.
6. **Login / logout / user management (PRD 11).** `SignInManager`/`UserManager`-backed flows: sign-in
   (with lockout), sign-out, an operator-seeded first admin user (bootstrapped beside the default tenant,
   idempotent, under the default tenant), and the minimal server-side user-management surface (create,
   disable, reset). All Blazor Server interactive-render endpoints; every authorization decision re-made
   server-side (§2 — hiding a control is not authorization; F5 owns permissions, F4 only authenticates).
7. **Account recovery (PRD 11).** Password-reset and email-confirmation token flows via Identity's token
   providers; recovery tokens are single-use and short-lived. Recovery **codes** (MFA fallback) are
   generated, `ISecretProtector`-protected at rest, and revealed once. No outbound email transport is
   built in F4 — recovery emits through a minimal `IAccountNotification` seam with a logging/no-op default
   (transport is a later feature); the token lifecycle and single-use semantics are what F4 proves.
8. **MFA — TOTP + recovery codes + lockout (PRD 11).** Authenticator (TOTP) enrolment and verification via
   Identity's authenticator-key + `TwoFactorSignInAsync`; the shared secret is a `SecretString` protected
   through `ISecretProtector`; recovery codes as in §7; account lockout on repeated failure. This is
   v1.0's MFA story — **passkeys are explicitly not it** (ADR 0006): they are a primary factor with no 2FA
   and unverified attestation by default, deferred to v1.1 with the schema-version change treated as a
   migration.
9. **The session-derived `ITenantContext` (F3A's reserved slot; PRD 7A).** A
   `ClaimsPrincipalTenantContext : ITenantContext` in Infrastructure that resolves `CurrentTenantId` from
   the authenticated principal's tenant claim (stamped at sign-in from `ApplicationUser.TenantId`) — never
   from a request parameter, header, or route value. Fails closed: no authenticated tenant claim ⇒
   `HasCurrentTenant == false` and reading `CurrentTenantId` throws, exactly as the interface contracts.
   Registered before `AddTenantFoundation` so it wins the `TryAddScoped`. Self-hosted single-tenant still
   resolves to the default tenant (every user is created under it), so the two implementations agree.
10. **The OIDC / identity-provider seam, proven against a test double (F4's distinctive scope; ADR 0017).**
    An **Application-level abstraction** — `IExternalIdentityProvider` (or the minimal shape ADR 0017
    fixes) — describing "authenticate a subject against an external IdP and map it to a local
    `ApplicationUser` within a tenant", with **no concrete IdP SDK referenced from Domain or Application**
    (§9 rule 6). F4 ships the seam, the local-mapping logic, and a **test-double provider** that exercises
    the full mapping/link path end to end; the concrete Auth0/OIDC provider is F3B (v1.1). The
    `Auth0 Organization ↔ Tenant` mapping stays out (non-interchangeable, PRD 63A). This proves PRD 63:
    the architecture *supports* an external IdP without *requiring* one.
11. **Authentication audit events (PRD 11), through a seam.** Sign-in success/failure, lockout, MFA
    challenge/verify, password/recovery changes, and external-IdP link raise structured
    **authentication events** through a minimal `IAuthenticationEventSink` with a logging default. F4 emits
    the events and proves they fire; **F6 owns the durable, queryable audit store** (`aud-`) and binds a
    real sink. Events carry no secret material (no passwords, tokens, TOTP secrets, or recovery codes).
12. **Host wiring (F4 is the first persisting feature).** `Program.cs` gains, in order:
    `AddSecurityFoundation`, the session-derived `ITenantContext` registration, `AddTenantFoundation`,
    `AddZWardenPersistence(provider, conn)`, `AddIdentityFoundation` (Identity + options + policy + cookie
    + MFA + the seams), `UseAuthentication`/`UseAuthorization`, and a startup call to
    `MigrateAndBootstrapDefaultTenantAsync` followed by the first-admin bootstrap. SQLite default needs no
    external service. `ZWardenDbContext.ConfigurationAssemblies` is extended to include the Identity
    configuration assembly.
13. **Architecture guards extended.** Add F4's Identity/OIDC types to the sanctioned side of §9 rule 6 as
    appropriate *without* weakening it — Identity's local types live in Infrastructure, the IdP SDK stays
    out of Domain/Application, and the OIDC seam abstraction in Application references only Domain types. A
    guard asserts `ApplicationUser` is `ITenantOwned` (a user with no tenant scope is a red build).

## Non-scope

- **Authorization / RBAC / permissions** — F5 (PRD 12A). F4 *authenticates* (who you are, proven) and
  establishes roles as Identity entities; it does **not** define permissions, role assignments, resource
  handlers, or the Moderator role. Being signed in is not being authorized; every such decision is F5's.
- **The concrete Auth0 / OIDC integration and the `Auth0 Organization ↔ Tenant` mapping** — F3B (v1.1).
  F4 ships the *seam* and a test double; it references no Auth0 SDK and adds no external-org column.
- **The durable audit store** — F6. F4 emits authentication events through a seam with a logging default;
  the queryable `aud-` store, correlation ids, and the admin viewer are F6.
- **Passkeys / WebAuthn** — v1.1 (ADR 0006). `Stores.SchemaVersion` stays `Version1`; enabling it later
  is a planned migration on both providers, not an F4 line in `Program.cs`.
- **Outbound email / SMS transport** — later. F4 proves recovery/confirmation *token* lifecycle behind a
  notification seam with a no-op/logging default; it wires no SMTP.
- **Tenant administration, membership, invitations** — F3D (v1.1). F4 creates users under the *default*
  tenant of a self-hosted install; it does not manage tenants.
- **Agent authentication / enrollment** — F9/ADR 0007. F4 is human-user authentication to ZWarden.Web;
  the Agent's outbound enrollment credential is a separate boundary.

## Domain changes

- **No new Domain entity for the user.** The Identity user/role types are framework-coupled and live in
  `ZWarden.Infrastructure` (§9 rules 2, 6). The **`IExternalIdentityProvider` seam** and the
  **`IAuthenticationEventSink`** / **`IAccountNotification`** abstractions live in `ZWarden.Application`,
  referencing only Domain (typed IDs, `TenantId`) and `SecretString` — no Identity or IdP SDK. Concrete
  Identity, the cookie/policy/MFA configuration, `ClaimsPrincipalTenantContext`, the test-double IdP, and
  the logging sinks live in `ZWarden.Infrastructure`; the pages and DI wiring in `ZWarden.Web`.
- **Glossary (`CONTEXT.md`):** add terms only — **Session** (the authenticated, tenant-bearing cookie
  session the current Tenant derives from), **Authentication event** (distinct from an F6 **Audit event**),
  and **Identity provider seam** (the external-IdP abstraction; not an `Auth0 Organization`). Keep
  `Auth0 Organization` and `Tenant` non-interchangeable.

## Contract changes

- **`ITenantContext` gains its production implementation** — the tenant now derives from the authenticated
  session's claim, still never from the browser; the single-tenant and session-derived implementations
  agree on the default tenant for self-hosted.
- **`IExternalIdentityProvider`** — the one way the system authenticates a subject against an external IdP
  and maps it to a local tenant-scoped user; a deployment with no external IdP configured never touches it
  (PRD 63).
- **`IAuthenticationEventSink`** — the one way authentication-relevant events leave the auth code; F6 binds
  a durable sink, F4 a logging one.
- **The Identity contract** — password policy, hashing parameters, cookie hardening, lockout, and MFA are
  fixed configuration recorded in code, not framework defaults.

## Security considerations

- **The browser never selects a tenant (PRD 7A, §2/§6).** The tenant claim is stamped server-side at
  sign-in from `ApplicationUser.TenantId`; `ClaimsPrincipalTenantContext` reads only that claim and fails
  closed with no authenticated tenant. No request-parameter path exists; code review and the guards keep it so.
- **The iteration count is chosen and visible (ADR 0006, PRD 2.1).** It is a documented constant with the
  date it was set, reviewed at F40 — the opposite of the invisible-stale-default failure PRD 2.1 exists to
  prevent.
- **Secrets are secret-aware and protected (ADR 0015).** TOTP shared secrets and recovery codes are
  `SecretString`/`Secret<T>` and `ISecretProtector`-protected at rest; they never stringify, log, or
  serialize in the clear, and authentication events carry none of them.
- **Fail closed:** no tenant claim ⇒ no ambient tenant (throws, never `TenantId.Empty`); a breached or
  under-length password is rejected before hashing; lockout on repeated failure; recovery/confirmation
  tokens are single-use and short-lived; the external-IdP seam is inert unless explicitly configured.
- **Host-header validation at the browser boundary (§2, ADR 0006).** An allowed-hosts list is enforced, so
  a request cannot spoof the host used to build absolute URLs / the future passkey RP ID.
- **Cookies over bearer tokens (PRD 11).** HttpOnly + Secure + SameSite; no auth token is exposed to
  browser script; antiforgery retained.
- **No IdP SDK in Domain/Application (§9 rule 6).** The OIDC seam is an abstraction; the concrete provider
  is Infrastructure-only, keeping the "no external IdP required" architecture (PRD 63) enforceable by test.

## Test plan

Written before the code (PRD 2.2). Offline tier except the per-provider Identity migration (networked).

1. **User is tenant-owned & scoped** — an `ApplicationUser` created under tenant A carries A's immutable
   `TenantId`; tenant B's context cannot read it; changing its `TenantId` throws (F3A interceptor).
2. **Password hashing parameters** — a hashed password verifies; the hasher is configured to
   PBKDF2-HMAC-SHA512 at the chosen `IterationCount` (assert the configured value equals the documented
   constant; the constant is not the framework's 100k).
3. **Password policy (NIST 800-63-4)** — a 14-char password is rejected and a 15-char one accepted with no
   composition rule imposed; a 64-char password is accepted; a known-breached password is rejected by the
   blocklist validator (offline, no network).
4. **Cookie hardening** — the configured application cookie is HttpOnly, Secure, SameSite=Lax, named, with
   the bounded expiration; sign-in issues it and sign-out clears it.
5. **Login / lockout** — valid credentials sign in; N failures lock the account; a locked account is
   refused even with correct credentials until the window elapses.
6. **MFA (TOTP + recovery)** — enrolling produces a protected authenticator secret; a correct TOTP passes
   `TwoFactorSignInAsync`; a wrong one fails; a recovery code signs in once and is then spent; the stored
   secret and codes are protected (never plaintext).
7. **Account recovery tokens** — a reset token resets the password and is then invalid (single-use);
   email-confirmation confirms once; the notification seam is invoked (no transport asserted).
8. **Session-derived tenant** — a signed-in principal with tenant A's claim yields
   `CurrentTenantId == A`; an unauthenticated principal yields `HasCurrentTenant == false` and throws on
   `CurrentTenantId`; the session context is registered ahead of `AddTenantFoundation` (the session impl wins).
9. **OIDC seam against a test double** — the test-double `IExternalIdentityProvider` authenticates a
   subject and maps/links it to a local `ApplicationUser` under the current tenant; a second sign-in links
   to the same user; nothing in Domain/Application references a concrete IdP SDK (architecture test).
10. **Authentication events** — sign-in success/failure, lockout, MFA verify, and password reset each emit
    exactly one event through the sink; no event payload contains a password, token, TOTP secret, or
    recovery code.
11. **Per-provider Identity migration** — a fresh SQLite database migrated by `MigrationRunner` has the
    `AspNet*` tables plus the typed-id column types and the `ApplicationUser` tenant column; the same
    migration applies on PostgreSQL (networked tier).
12. **Host wiring smoke** — the composed host resolves `UserManager`, `SignInManager`, a scoped
    `ZWardenDbContext` with the session-derived `ITenantContext`, and completes migrate-then-bootstrap with
    the first admin seeded idempotently under the default tenant.
13. **Architecture guards** — Domain/Application reference no Identity/IdP package (§9 rule 6 still green
    with F4's types classified correctly); `ApplicationUser` is `ITenantOwned`; the tenant-filter and
    obsolete-crypto guards stay green.

## Implementation slices

Sliced so each is independently green and TDD-first. If the size breaks the PRD 60 guardrail, **split by
slice** at the marked seam (S1–S5 identity core → S6–S8 recovery/MFA → S9–S11 seam/events/wiring) —
never by inventing scope the PRD does not have.

- **S1 — Identity entities + DbContext base (Infrastructure).** `ApplicationUser`/`ApplicationRole`,
  `ZWardenDbContext : IdentityDbContext<…, UserId>`, configuration assembly registered. *Verify:* test 1,
  and the model builds with typed-id columns + the tenant filter on `ApplicationUser`.
- **S2 — Password hashing + policy (Infrastructure).** `PasswordHasherOptions` (documented iteration
  constant), `IdentityOptions.Password`, the breached-password validator + offline blocklist seam.
  *Verify:* tests 2, 3.
- **S3 — Cookie hardening + host-header validation (Infrastructure/Web).** Application cookie options,
  allowed-hosts. *Verify:* test 4.
- **S4 — Login / logout / lockout + first-admin bootstrap (Infrastructure/Web).** Sign-in/out flows,
  lockout options, idempotent first-admin seed under the default tenant. *Verify:* test 5, part of 12.
- **S5 — Session-derived `ITenantContext` (Infrastructure).** `ClaimsPrincipalTenantContext`, tenant claim
  stamped at sign-in, registration order. *Verify:* test 8. *(Guardrail split point A.)*
- **S6 — Account recovery + notification seam (Infrastructure/Web).** Reset/confirm token flows,
  `IAccountNotification` no-op default, protected recovery codes. *Verify:* test 7.
- **S7 — MFA: TOTP + recovery codes (Infrastructure/Web).** Authenticator enrol/verify, protected secret,
  recovery-code sign-in. *Verify:* test 6.
- **S8 — Authentication event seam (Application/Infrastructure).** `IAuthenticationEventSink` + logging
  default, events raised on the auth paths, secret-free payloads. *Verify:* test 10. *(Guardrail split point B.)*
- **S9 — OIDC / identity-provider seam + test double (Application/Infrastructure; ADR 0017).**
  `IExternalIdentityProvider`, local-mapping/link logic, the test-double provider. *Verify:* test 9.
- **S10 — Per-provider Identity migration.** The `AspNet*` migration in `ZWarden.Migrations.Sqlite` and
  `ZWarden.Migrations.Postgres`. *Verify:* test 11 (SQLite offline; Postgres networked).
- **S11 — Host wiring + architecture guards.** `Program.cs` composition order, `AddIdentityFoundation`,
  `UseAuthentication`/`UseAuthorization`, migrate-then-bootstrap; §9-rule-6 classification and the
  `ITenantOwned` user guard. *Verify:* tests 12, 13.

## Diagnostics

- **A rejected sign-in / locked account** produces a clear, catchable outcome distinguishing wrong
  credentials from lockout from unconfirmed account, and an authentication event — never echoing the
  password or naming which factor matched.
- **The iteration count is self-describing** — a documented constant with its choice date, so an operator
  or the F40 gate can read the parameter without source archaeology.
- **A fail-closed tenant claim** names the missing authenticated tenant at the point of use, so a wiring
  mistake surfaces as a caught failure, not a silent cross-tenant read.
- **The OIDC seam is observable in tests** — the test double exercises the whole mapping path, so a
  regression that couples Domain/Application to a concrete IdP is a failing architecture test, not a
  v1.1 surprise.
- **Authentication events are secret-free by assertion** (test 10), so enabling F6's durable sink cannot
  start logging credentials.

## Documentation

- **ADR 0017 (new, written with this plan)** — the OIDC / identity-provider **seam** decision: an
  Application abstraction with the concrete provider confined to Infrastructure so PRD 63 ("no external IdP
  required") stays test-enforceable, the local-user mapping rule, and why the `Auth0 Organization ↔ Tenant`
  mapping stays out (F3B). ADR 0006 already records the iteration-count and passkey decisions this feature
  discharges; this plan records type placement, the cookie/policy configuration values, and the test topology.
- `CONTEXT.md` — **Session**, **Authentication event**, **Identity provider seam** (terms only).
- `CONTRIBUTING.md` — the rules that Identity types live in Infrastructure (never Domain/Application), the
  tenant claim is the *only* source of the session tenant, MFA/recovery secrets are always secret-aware and
  protected, the password policy/iteration constant are configured-not-inherited and live in one documented
  place, and the OIDC seam is an abstraction with no IdP SDK above Infrastructure.

## Acceptance criteria

1. A user authenticates against first-party ASP.NET Core Identity over `ZWardenDbContext`
   (`IdentityDbContext<…, UserId>`), with login, logout, and lockout, on both providers' migrations.
2. Password hashing is **PBKDF2-HMAC-SHA512 at an explicitly configured iteration count** recorded as a
   dated constant (not the inherited 100k); the password policy meets NIST SP 800-63-4 (15-char min, 64
   max, no composition rules, no rotation, breached-password check).
3. Session cookies are HttpOnly + Secure + SameSite with bounded expiration; host-header validation is
   enforced; no auth bearer token is exposed to the browser.
4. Account recovery (reset + confirmation) tokens are single-use and short-lived; **MFA (TOTP + recovery
   codes)** works with the shared secret and codes secret-aware and protected at rest.
5. The **current tenant derives from the authenticated session** (`ClaimsPrincipalTenantContext`), never
   the browser, failing closed with no tenant claim, and agreeing with the single-tenant default for
   self-hosted; it wins registration over `SingleTenantContext`.
6. The **OIDC / identity-provider seam is proven against a test double** end to end, with **no concrete IdP
   SDK referenced from Domain or Application** (architecture test green); the architecture *supports* an
   external IdP without requiring one (PRD 63).
7. **Authentication events** fire on the auth paths through a seam with a logging default and carry no
   secret material; F6 can bind a durable sink without touching auth code.
8. `Program.cs` wires security + tenant + persistence + Identity and completes migrate-then-bootstrap with
   an idempotent first admin under the default tenant.
9. The offline tier is green; the networked tier proves the per-provider Identity migration; no obsolete
   API or nullable warnings (warnings-as-errors, ADR 0013); passkeys remain deferred (`SchemaVersion`
   `Version1`).

## Definition of Done

Per PRD 61, the applicable subset: acceptance criteria met; tests authored first; unit, model, and
architecture tests green in the offline tier and the per-provider Identity migration green in the
networked tier; the PRD 11 exit condition holds — **a user can register/be provisioned, authenticate with
MFA, recover access, and be locked out, over hardened cookies, with the tenant derived from the session
and the external-IdP seam exercised against a double** — enforced by tests, not merely intended; error
conditions modelled and diagnosable without leaking credentials; ADR 0017, `CONTEXT.md`, and
`CONTRIBUTING.md` updated; CI green; no unresolved warnings. (Authorization/RBAC, the concrete Auth0
integration and org↔tenant mapping, the durable audit store, passkeys, email/SMS transport, and tenant
administration are N/A at F4 — they build on the authenticated identity and the seams this feature proves,
owned by F5, F3B, F6, v1.1, and F3D respectively.)
