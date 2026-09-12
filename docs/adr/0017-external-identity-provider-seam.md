# 17. The external identity-provider seam is an Application abstraction, proven against a test double

**The external OIDC / identity-provider integration enters ZWarden through one Application-level seam,
`IExternalIdentityProvider`, whose only job is to authenticate a subject and return a plain
`ExternalIdentity` (issuer, subject, email).** No concrete identity-provider SDK is referenced from
Domain or Application. Mapping that identity to a local `ApplicationUser` is Infrastructure's
responsibility (`ExternalLoginService`). **F4 ships the seam and the mapping and proves them against a
test double; the concrete Auth0/OIDC implementation is F3B (v1.1).** A deployment that configures no
external provider never registers an implementation, so the architecture *supports* an external IdP
without *requiring* one.

- Status: accepted
- Decided in: [#26](https://github.com/MCrank/ZWarden/issues/26) (F4), with the F4 mini-plan
  ([`docs/feature-plans/F4-identity-and-authentication.md`](../feature-plans/F4-identity-and-authentication.md))
- Bears on: PRD 63 ("the initial architecture shall not require external identity providers"), PRD 63A
  (`Auth0 Organization` and `Tenant` stay non-interchangeable), the trust boundaries §9 rule 6
  (Domain/Application reference no external identity provider); F3B (the Auth0 Organizations integration)

## Context

PRD 63 requires that the initial architecture not *require* an external identity provider, while PRD 11
and the roadmap still want the *option* of one (Auth0 in F3B, v1.1). The failure mode the scope-and-
sequencing spec calls out by name is F4 having once been "gated behind Auth0", inverting PRD 63. So the
external-IdP capability must exist as a boundary that is real and exercised in v1.0, without any concrete
provider being on the v1.0 critical path.

Two standing constraints shape the boundary:

- **Arch rule 6** (`Domain_and_application_do_not_reference_an_external_identity_provider`) fails the
  build if an Auth0/Okta/OIDC SDK is referenced from Domain or Application. So the seam that those layers
  see must be provider-agnostic.
- **ASP.NET Core Identity is the authority for local users and their links.** Identity already models an
  external login as an `(loginProvider, providerKey)` pair in `AspNetUserLogins`, which is exactly the
  shape an OIDC `(issuer, sub)` maps onto — so the local-mapping half needs no new storage.

## Decision

- **`IExternalIdentityProvider` (Application)** exposes `Issuer` and
  `AuthenticateAsync(credential) → ExternalIdentity?`. `ExternalIdentity` is a plain record of
  `(Issuer, Subject, Email?)` — Domain-level values only, no Identity or SDK type. A `null` result means
  authentication failed.
- **`ExternalLoginService` (Infrastructure)** maps an `ExternalIdentity` to a local `ApplicationUser`:
  it returns the user already linked to `(Issuer, Subject)` via `UserManager.FindByLoginAsync`, or
  provisions a new user **under the ambient tenant** (email pre-confirmed, since the provider vouches for
  it) and links it with `AddLoginAsync`. A repeat sign-in for the same subject resolves to the same user.
  It emits an `ExternalLoginLinked` authentication event on first link.
- **No `IExternalIdentityProvider` is registered in v1.0.** F4 registers only `ExternalLoginService`;
  the interface is bound by F3B (the real Auth0/OIDC provider) or, in tests, by a **test double** that
  asserts an identity for a credential. The seam is therefore proven end-to-end in v1.0 without a
  concrete provider existing.
- **The `Auth0 Organization ↔ Tenant` mapping stays out of F4** (PRD 63A / F3B). `ExternalIdentity`
  carries no organization reference; the tenant a provisioned user lands in is the ambient tenant, which
  in self-hosted is the default tenant.

## Alternatives considered

- **Implement the concrete OIDC/Auth0 provider in F4.** Rejected: it puts an external dependency on the
  v1.0 critical path and re-creates the exact "F4 gated behind Auth0" inversion of PRD 63 the sequencing
  spec fixed. It is F3B, deliberately v1.1.
- **Put the seam in Infrastructure and let Application call into it.** Rejected: Application would then
  depend on an Identity-shaped abstraction, and the temptation to reference the provider SDK from a
  use-case would no longer be caught by arch rule 6. Keeping the seam Domain-only in Application makes the
  rule enforceable.
- **Model the external identity with Identity's own `ExternalLoginInfo`/`UserLoginInfo`.** Rejected for
  the *seam* (those are Identity types and would leak the framework into Application); they are used only
  inside `ExternalLoginService`, where Identity is already the dependency.
- **Ship a real provider disabled behind a flag so the column/flow exists.** Rejected on the same
  reasoning as ADR 0006's passkey decision: a dark provider is a decision made early with less
  information, and an unconfigured deployment is better served by there being nothing to misconfigure.

## Consequences

- **F3B owns a concrete `IExternalIdentityProvider`** (Auth0/OIDC), the RP/host-header validation that a
  real redirect flow needs (trust-boundaries §2, already required by ADR 0006), and the
  `Auth0 Organization → Tenant` resolution that lets external sign-in land in the right tenant in a hosted
  deployment. F4 leaves all of that to it, by design.
- **In self-hosted v1.0 the mapping always lands in the default tenant**, because the ambient tenant is
  the default one. Multi-tenant external sign-in (choosing the tenant from the org/issuer before the user
  exists) is a hosted concern F3B resolves; F4 does not pretend to.
- **The seam is exercised by a test double, not a live IdP**, so v1.0 proves the *contract and the
  local-mapping*, not any specific provider's token validation. That is the intended scope — the provider
  is F3B — and it means the double must stay faithful to the contract (`null` on failure, stable subject).
- **A provisioned external user has no password.** Account-recovery-by-password does not apply to it; that
  is correct (its credential lives at the provider) and consistent with Identity's model.
