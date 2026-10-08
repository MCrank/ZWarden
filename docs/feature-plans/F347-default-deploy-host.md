# Issue #347 Mini-Plan — Default deploy host

**Status:** one PR (branch `feat/347-default-deploy-host`, stacked on #346, closes #347). Last sub-issue of epic #350.

**Written against:** issue #347, ADR 0048 (settings store, #345), #338 (Deploy server sheet), #364 (hosts without a
usable PZ image are not deployable).

## Objective

An Owner or Administrator picks a default deploy host under Settings → General. Deploy server pre-selects it, and the
operator can still pick another host. If that host is later revoked or removed, nothing breaks: deploys have no
default and Settings says why.

## Decisions

- **D1: the setting.** `ControlPlaneSettings.DefaultDeployHost` (`AgentId?`), with null meaning no default. The
  permission is `Tenant.Settings.Manage`, the change is audited as `Settings.DefaultDeployHostChanged`, old → new by
  host name (label, then hostname, then id), and it goes through the shared settings-service path.
- **D2: the service checks the host.** It must be a trusted host (enabled, holds a credential) in the tenant, or the
  change is refused. Revoking or removing the host later does **not** clear the setting. Readers treat a stored host
  that is no longer trusted as "no default". So re-enabling the host restores the default, and the change isn't
  silently lost.
- **D3: the Settings → General picker.** A select of "No default" plus the trusted hosts by display name, and Save.
  It is read-only without the permission. When the stored host is no longer trusted, the row says "the saved host
  is no longer enrolled — Deploy server has no default".
- **D4: the Deploy sheet.** When it opens with no host chosen, it pre-selects the default if that host is deployable
  (connected, image ready; #364). Otherwise the existing rule applies: one deployable host gets chosen. The operator
  can always change it.

## Tests (TDD)

- Domain: set and clear.
- Infrastructure: set, clear and audit by name; refuse an unknown or untrusted host; refuse without the permission.
- Web: the General picker lists hosts with the saved one selected; the missing-host hint; the Deploy sheet
  pre-selects the default and still lets the operator choose another; an undeployable default is not pre-selected.

## Not in scope

- A default per operator (this is one per tenant).
