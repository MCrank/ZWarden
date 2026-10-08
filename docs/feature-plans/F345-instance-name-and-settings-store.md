# Issue #345 Mini-Plan — Editable Instance name + the control-plane settings store

**Status:** one PR (branch `feat/345-instance-name`, closes #345). Second sub-issue of epic #350, after #344.

**Written against:** issue #345, epic #350, `F344-settings-sub-nav.md` (`SettingRow`), ADR 0016 (tenant-owned rows),
F110's `WorkshopIntegrationSettings` (the one-row-per-tenant settings pattern), F6 (audit), PRD 12 (enforcement in
the service).

## Objective

An Owner or Administrator renames the instance from Settings → General. The sidebar's workspace label and the signed-in
page titles show the new name at once, with no restart. Clearing the field reverts to the configured name. Every change
is audited as old → new. The store built here also holds #346 (session timeout) and #347 (default deploy host).

## Decisions

- **D1: the store is one typed row per tenant.** `ControlPlaneSettings` (`cps-` id) is tenant-owned and versioned,
  with a unique index on `TenantId`, the same shape as `WorkshopIntegrationSettings`. It has typed nullable columns,
  where null means "use the config value or the default". This PR adds `InstanceName` (max 64). #346 and #347 each add
  their own column and migration. Typed columns keep validation in the domain and the schema honest; a key/value bag
  would push parsing into every reader.
- **D2: a new permission, `Tenant.Settings.Manage`.** The issue wants Owner and Admin. `Tenant.Manage` is Owner-only,
  and `User.Manage` means something else. The Administrator bundle gets the new permission automatically, because it
  is "all except" a fixed list. Existing installs get it through a data migration granting it to built-in Tenant Owner
  and Administrator roles, as #271 did for `Server.Delete`. #347 reuses it; #346 (Owner-only) uses `Tenant.Manage`.
- **D3: validation lives in the domain.** The name is trimmed, must be 1–64 characters, and may not contain control
  characters. Blank means clear. It is shown as text only; Razor encodes it.
- **D4: an in-process cache.** `InstanceNameCache` is a singleton holding each tenant's override. On a miss it loads
  in a fresh tenant scope, so circuits never share a `DbContext` (the #154 class). The settings service refreshes the
  cache after a save. Web runs as one process, so in-process invalidation is enough.
- **D5: the shell.** A scoped `InstanceName` service returns the override, or `InstanceOptions.Name` when there is
  none or no tenant (anonymous pages). `MainLayout` uses it for the workspace label. A new `ShellTitle` component
  renders `<Page> · <instance name>` for the signed-in pages (Fleet, Hosts, Server, Audit, Settings). Their mixed
  `·`/`—` separators become `·`. Sign-in, account and setup pages keep "ZWarden", so the name is never shown to
  anonymous visitors.
- **D6: audit.** `Settings.InstanceNameChanged`, with detail `instance name "old" → "new"`. A null side reads
  `(config: <name>)`. A save that changes nothing is not audited.
- **D7: the UI.** General → Instance name gets an input and a Save button. When an override exists there is also a
  "Use config name" button. The hint says the config default is `<name>` (`ZWarden:Instance:Name`). Operators without
  `Tenant.Settings.Manage` see the value read-only.

## Tests (TDD)

- Domain: name validation (trim, bounds, control characters, blank means clear).
- Infrastructure: the service sets, clears and audits old → new; it denies an operator without the permission; it
  skips the audit for a no-op; it refreshes the cache. The migration grants the permission to built-in roles.
- Web: General shows the override; saving updates the label and title on the next page; clearing reverts; Admin can
  edit; the title uses `ShellTitle`.
- Browser: rename on Settings, then the sidebar label shows the new name.

## Slices

1. Domain: `ControlPlaneSettings`, id, validation, permission and role catalogue.
2. Infrastructure: configuration, repository, service, cache, audit actions, migrations (both providers, plus the
   grant).
3. Web: `InstanceName`, `MainLayout`, `ShellTitle`, the General row editor.
4. Chores: `app.css`, floors, docs, this plan's result.

## Not in scope

- Session timeout (#346), default deploy host (#347).
- Showing the name to anonymous visitors (sign-in page).
