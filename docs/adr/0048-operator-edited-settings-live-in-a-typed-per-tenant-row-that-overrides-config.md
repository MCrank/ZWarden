# 48. Operator-edited settings live in a typed, per-tenant row that overrides config

The control-plane settings an operator may change from Settings (instance name, session idle timeout, default deploy
host) live in **one tenant-owned row per tenant**, `ControlPlaneSettings` (`cps-`). The row has **one typed, nullable
column per setting**. A null column means "not overridden": the deploy-time config value, or the built-in default,
applies. Clearing a setting therefore reverts to config, and config stays the baseline for a fresh install. Values
are validated in the domain aggregate, every change is audited old → new, and readers on hot paths (the shell's
instance name, the auth cookie's lifetime) go through an in-process cache that the writing service refreshes.

- Status: accepted
- Decided in: #345 (epic #350, Settings reorganisation)
- Bears on: ADR [0016](./0016-tenant-isolation-query-filter-and-default-tenant.md) (the row is tenant-owned),
  ADR [0018](./0018-zwarden-owned-rbac.md) (new `Tenant.Settings.Manage` permission), ADR
  [0019](./0019-audit-is-append-only-tenant-owned-and-binds-the-auth-sink.md) (audited), ADR
  [0044](./0044-workshop-search-is-an-optional-per-tenant-operator-supplied-key.md) (the same one-row-per-tenant
  shape as `WorkshopIntegrationSettings`)

## Context

Until #345, ZWarden treated every control-plane setting as deploy-time config: instance name, session lifetime and
forwarded-header trust were all fixed at boot and shown read-only in Settings. Epic #350 makes three of them editable
in the UI. They need a store that survives restarts, scopes per tenant for SaaS, and still lets an operator who never
touches Settings run purely from config.

## Decision

- **One row per tenant, typed columns.** `ControlPlaneSettings` is `ITenantOwned` and `IVersioned`, with a unique
  index on `TenantId`. Each setting is its own nullable column (`InstanceName` in #345; the session timeout in #346;
  the default deploy host in #347), added by its own migration.
- **Null means config.** A reader resolves "row value ?? config value ?? default". The UI says where the effective
  value comes from and offers a way back to config.
- **Permissions.** `Tenant.Settings.Manage` (Owner and Administrator) covers the settings an Administrator may
  change. Settings with a wider security reach (session lifetime) stay on `Tenant.Manage` (Owner only). Existing
  installs get the new permission on their built-in roles through a one-time data migration, as `Server.Delete` did.
- **Cache.** Hot-path readers use an in-process singleton cache keyed by tenant. A miss loads in a fresh tenant scope,
  never a circuit's own `DbContext`, and the service refreshes the entry after a save. Web is a single process (one
  control plane per install), so in-process invalidation is sufficient.

## Consequences

- Adding an editable setting means a column, a migration and a domain setter, a little more ceremony than a key/value
  bag. In return the schema documents every setting and no reader parses strings.
- Config can no longer *force* a value an operator has overridden in the UI; the override wins until it is cleared.
  The UI shows the config default next to an override, so this is never hidden.
- A future multi-process Web would need cross-process cache invalidation (or a short TTL); noted, not built.
