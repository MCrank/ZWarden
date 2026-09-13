# Feature 14 Mini-Plan — Server Registration and Inventory

**Status:** PLANNED. Roadmap issue: [F14 (#35)](https://github.com/MCrank/ZWarden/issues/35). Track D — the first server-operations feature and **the first UI-bearing feature**; **unblocks F15, F16, F17, F18, F20, F24** (and #52, #45, #40, #39, #36).

**Delivered as two PRs** (the F11 precedent), each independently verifiable, in one context, and inside PRD 59's ~100K guardrail:

- **PR-A — Registry, discovery, import, inventory dashboard.** The `Server` aggregate + persistence + dual-provider migration, snapshot reconciliation, import (adopt a discovered container), the `Server.Register` permission, the app shell + `/servers` inventory. **No provisioning; no new wire command.**
- **PR-B — RegisterServer provisioning.** The mutating provision path: a new `AgentCommand`, an Operation kind, the Agent handler over F13's create-template + port-stride, and the "Register a server" flow in the dashboard.

**Format:** PRD 60. **Written against:** [`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (F14), the F13 hand-off ([`F13-agent-docker-runtime.md`](F13-agent-docker-runtime.md) D2 — "F14 (`RegisterServer`/provisioning)"), ADR [0016](../adr/0016-tenant-isolation-query-filter-and-default-tenant.md) (tenant isolation), [0004](../adr/0004-typed-ids-are-stored-as-native-uuid.md)/[0014](../adr/0014-typed-id-pattern.md) (typed ids), [0018](../adr/0018-zwarden-owned-rbac.md) (RBAC), [0005](../adr/0005-both-database-providers-ship-in-v1-0.md) (both providers), [0022](../adr/0022-operation-lifecycle-and-per-server-locking.md) (operation lifecycle + per-server locking), [0003](../adr/0003-blazor-blueprint-ui-library.md) (Blueprint + wrapper seam), the "Signal" identity ([#18](https://github.com/MCrank/ZWarden/issues/18), `docs/style-guide/`), [`trust-boundaries.md`](../trust-boundaries.md) §3 (observed-never-inferred) / §4 (ownership) / §8 (untrusted Agent output), and PRD 12A (permission naming).

## Objective

Give operators a **Server** — one Project Zomboid instance under ZWarden's management (CONTEXT.md) — as a first-class, tenant-owned record, and the **inventory dashboard** to see and act on the fleet. F14 turns F13's host-local container discovery into a durable, authorized, tenant-scoped model:

1. **Bring a Server under management** two ways: **import** (adopt a canonical container the Agent already discovered) and **register** (declare a new Server and provision its container through F13's create-template — PR-B).
2. **Reconcile observed against desired** — the Agent's `AgentStateSnapshot` (already arriving at the hub) updates each Server's **last-reported run-state**, observed and never inferred (trust-boundaries §3). Health *beyond* last-reported state is F16.
3. **Show the fleet** — the `/servers` inventory: KPI tiles + a virtualized grid on the accepted Signal identity, through the Blueprint wrapper seam (ADR 0003), authorized server-by-server.

Because it is the first UI feature, F14 also stands up the **app shell** (real navigation, the overlay providers MainLayout deferred to "the first feature that needs one") and finishes the F0 **`StatusBadge`** placeholder against the `--status-*` token ramp.

## Dependencies

All four blockers are **closed/merged**:

- **F5** (#27) — authorization: the closed permission catalogue, `PermissionPolicyProvider` (any catalogue name → a policy), `IPermissionChecker`, server-scoped handling (`IServerScoped`, `ServerScopedPermissionHandler`). The `Server.*` family and `IServerScoped` already exist.
- **F10** (#32) — the SignalR control plane: `AgentHub`, the in-memory `IAgentConnectionRegistry`, and `AgentStateSnapshot` ingest (today it only logs `snapshot.Payload.Servers.Count`).
- **F13** (#34) — the Agent Docker runtime: `IContainerRuntime.ListManagedAsync` → `ManagedContainer(DockerId, ServerId, State)`, the canonical label contract (`io.zwarden.server-id`/`agent-id`), `PzContainerSpec` → closed create-template, `PortStrideAllocator`.
- **F3A** (#25) — tenant foundation: `ITenantOwned`, the tenant query filter + ownership interceptor (ADR 0016), `Tenant.DefaultId`.

Consumes settled facts: `ServerId`/`srv-` is already a registered typed id (F1); `ServerState`/`ServerRunState` already exist as **wire** types in `ZWarden.Contracts.Protocol`; F11's operations engine + `IOperationCoordinator`/`IOperationDispatcher` + per-server lock (ADR 0022) is the substrate PR-B's provisioning Operation runs on.

## Decisions (settled in planning)

- **D1 — F14 provisions, split across two PRs.** Reconciling §6 ("import/register … not lifecycle") with F13 D2 ("F14 RegisterServer/provisioning"): **register = provision a new container**, but delivered as PR-B so PR-A ships the entity + inventory + import first and each PR stays in one context. Lifecycle (start/stop/restart) remains **F15**; provisioning (create) is F14's because it is the act of *registering*, and F13's create-template exists precisely for it.
- **D2 — the `Server` is a tenant-owned aggregate that owns its identity; the container is stamped with it.** `Server.Id` (`srv-`) is minted by Web and written to the container as `io.zwarden.server-id` at create (F13). The **ServerId is the durable link**, not the Docker container id (which changes on recreate). The observed `DockerContainerId` is a nullable convenience updated from discovery, never the key.
- **D3 — `Server.Register` is a new TenantWide permission, and the ADR 0018 scope rule gets a recorded carve-out.** A Server does not exist at registration/import time, so the gate cannot be server-scoped — `PermissionChecker` **denies any server-scopable permission checked with no `ServerId`** (verified in the code). One permission covers **both** import and register — "bring a Server under management." It joins the closed catalogue, `All`, the PRD 12A list, and `PermissionCatalogueTests`; it flows into TenantOwner + Administrator via `BuiltInRoles.All` automatically (Administrator = All minus four) and is correctly excluded from Viewer/Moderator/Operator. **Correction to the first draft:** this is the *first* `Server.*` name that is tenant-wide, which contradicts ADR 0018's "all `Server.*` are server-scopable" rule — so it **does** need an ADR touch: ADR 0018 is amended to record `Server.Register` as the explicit exception, and the scope-family test carves it out. Inventory **reads** stay gated by the existing server-scoped `Server.View` (a user sees the Servers they may view; a tenant-wide grant sees all).
- **D4 — the Domain owns a coarse `ServerRunState`; the wire enum maps to it.** `ZWarden.Contracts.Protocol.ServerRunState` cannot be referenced from the Domain (Domain does not reference Contracts). F14 adds `ZWarden.Domain.Servers.ServerRunState` (the same coarse vocabulary: Unknown/Stopped/Starting/Running/Stopping/Failed, serialized by name), and the **reconciler in Web** — which sees both assemblies — maps wire → domain. F16 layers the hierarchical health model on top; F14 persists only the last-reported state + when.
- **D5 — discovery in PR-A rides the existing snapshot; no new wire command.** `AgentStateSnapshot` already carries `ServerState(ServerId, RunState)` per managed container. PR-A wires the hub's snapshot ingest to (a) update the last-reported state of matching Servers and (b) surface **orphans** — a discovered `ServerId` with no Server record — as "discovered, unregistered," which is what **import** adopts. The only new `AgentCommand` in the feature is PR-B's provisioning command.
- **D6 — the inventory renders on the static server, not a live circuit.** A tenant-scoped read needs a live `HttpContext` (the tenant is a session claim, read via `IHttpContextAccessor`), which is exactly why `AuditLog.razor` is static-rendered rather than `InteractiveServer`. F14's `/servers` list follows it: static render of the last-reported state. Live 1 Hz push into the grid is F16 telemetry / F36 (ADR 0003's virtualization-vs-prerender decision), not F14. Interactive bits (import/register dialogs) are scoped, per-request, or plain form posts (antiforgery), mirroring the Account pages.
- **D7 — provisioning allocates ports on the Agent, records them on the Server (PR-B).** `PortStrideAllocator` (F13) needs the strides actually in use on the host, which only the Agent sees. The Agent allocates at create time and reports the concrete UDP pair in the `OperationCompleted` result; Web records it on the Server. RCON is never host-published (F12/F13).

## Scope

### PR-A — Registry, discovery, import, inventory dashboard

1. **Domain — the `Server` aggregate** (`src/ZWarden.Domain/Servers/Server.cs`), mirroring `Agent`: `ITenantOwned` + `IVersioned`; `ServerId Id` (init), `TenantId` (init, **unset** so the interceptor stamps it), `Guid Version`. Fields: required `AgentId AgentId` (the Agent-to-Server association — a Server belongs to exactly one host), `string Name`, `string? Description`, `int? GamePort`/`int? QueryPort` (nullable until provisioned/observed), `string? DockerContainerId` (observed), `ServerRunState LastRunState` (default `Unknown`), `DateTimeOffset? LastStateReportedAt`, `DateTimeOffset CreatedAt`. Static factory `Server.Import(AgentId, ServerId, name, now, …)` (adopt a discovered id) — `TenantId` left unset (ADR 0016). State mutators: `RecordObservedState(ServerRunState, at)`, `Rename(name, description)`, `RecordContainer(dockerId, gamePort, queryPort)`. New Domain enum `ServerRunState` (D4).
2. **Infrastructure — persistence.** `ServerConfiguration : IEntityTypeConfiguration<Server>` (mirror `AgentConfiguration`: `ToTable("Servers")`, key, `Name` required + max length, `LastRunState` `.HasConversion<string>()`, index on `AgentId`; **no** typed-id/tenant/version config — the conventions do that). `ServerRepository : TenantScopedRepository<Server>` (`IServerRepository` in Application) with `ListForTenantAsync`, `GetAsync(ServerId)`, `ListByAgentAsync(AgentId)`. Register in `AddZWardenPersistence`.
3. **Migration — both providers.** `AddServers` in `ZWarden.Migrations.Sqlite` **and** `ZWarden.Migrations.Postgres` (the CONTRIBUTING recipe), snapshots updated. Applied idempotently by `MigrationRunner` on startup.
4. **Application — inventory + import service.** `IServerInventory` / `ServerInventory`: `ListVisibleAsync(user)` (fail-closed — tenant-wide `Server.View` ⇒ all tenant Servers; otherwise only Servers with a matching server-scoped grant, via `IPermissionChecker`), `GetAsync`, `ImportAsync(user, AgentId, ServerId, name)` (authorized `Server.Register`, writes the record + an audit event via `IAuditWriter`), `ListDiscoveredUnregisteredAsync(AgentId)` (orphan ServerIds from the last snapshot minus persisted records). Ctor-inject repository, `ZWardenDbContext`, `TimeProvider`, `IAuditWriter`, `IPermissionChecker`.
5. **Discovery reconciliation (D5).** Wire `AgentHub`'s `StateSnapshot` ingest to a reconciler (`IServerStateReconciler`) that maps each `ServerState.RunState` (wire) → Domain `ServerRunState`, calls `RecordObservedState` on matching persisted Servers, and records the residual **orphans** for import (an in-memory per-Agent last-snapshot view, alongside the F10 registry). Orphans are display data only, treated as untrusted (trust-boundaries §8).
6. **Authorization — `Server.Register`.** Add to `Permissions` (TenantWide), `All`, `BuiltInRoles` (Administrator), the PRD 12A list, and `PermissionCatalogueTests.PrdPermissionNames`.
7. **Web — endpoints.** `MapServerEndpoints()` minimal-API group under `/api` (mirror `EnrollmentEndpoints`): `GET /api/servers` (`Server.View`), `GET /api/agents/{id}/discovered` (`Server.Register` — orphans to import), `POST /api/servers/import` (`Server.Register`; body = agentId, serverId, name; antiforgery via the Lax cookie). Failure text projected as **untrusted display text — escape at render** (trust-boundaries §3).
8. **Web — the app shell + inventory UI** (the first UI feature):
   - **Shell:** real navigation in `MainLayout` (a sidebar/nav with Servers + Audit links, each `AuthorizeView`-gated by the relevant policy), and the deferred overlay providers (`BbPortalHost` / `BbToastProvider` / `BbDialogProvider`) — F14 is "the first feature that needs an overlay" (import dialog + toasts).
   - **`/servers` inventory** (`Pages/Servers/ServerInventory.razor`, `@page "/servers"`, `[Authorize(Policy = "Server.View")]`, **static** render per D6): KPI tiles (running / needs-attention / total / hosts) over `IServerInventory`, then the fleet grid — **`BbDataGrid`** (ADR 0003: virtualizes, not `BbDataTable`) with columns name / host (Agent label) / status / last-seen / ports, using **`StatusBadge`** and Chivo Mono tabular data.
   - **Import flow:** a dialog/form listing discovered-unregistered ServerIds for the chosen Agent + a name field → `POST /api/servers/import` → toast + refresh. ("Register a server" button is PR-B.)
   - **`StatusBadge` finish (seam):** reconcile the F0 placeholder to the `--status-*` tokens and map Domain `ServerRunState` → the ramp (running/stopped/unhealthy/busy/unknown); retire the raw-emerald classes. Remove/relocate the F0 `Ui/ServerStatus.cs` placeholder if now unused.
9. **CSS.** Run `npm run build:css`, commit `wwwroot/app.css` (ADR 0003 condition 2 — the silent stale-`app.css` failure; CI diffs it). Any class built in `.cs` must be a full literal string (Tailwind content glob).

### PR-B — RegisterServer provisioning

1. **Contracts — one new command.** `Provisioning.CreateServer : AgentCommand` (sealed record, `ProtocolMessageAttribute`, mutating, server-scoped), carrying the create inputs (`ServerId`, name; the pinned image ref + network are Agent config, D5 of F13). Update `ClosedCommandVocabularyTests`.
2. **Operations.** Extend `OperationKind` with `ProvisionServer`; map it in `OperationDispatcher` (the `OperationKind → AgentCommand` switch). Provisioning is a **mutating** Operation against the new Server's id, so ADR 0022's per-server lock applies.
3. **Domain/Application.** `Server.Register(AgentId, name, now)` factory (record in `Unknown` state, no container yet); `IServerInventory.RegisterAsync(user, AgentId, name)` — authorized `Server.Register`, writes the record + audit, then `IOperationCoordinator.EnqueueAsync(ProvisionServer, agentId, serverId)`.
4. **Agent.** `AgentCommandProcessor` handles `Provisioning.CreateServer`: allocate the port-stride from discovered containers (`PortStrideAllocator`), build the `PzContainerSpec`, call `IContainerRuntime.CreateAsync` (the closed create-template stamps `io.zwarden.server-id`/`agent-id`), report `OperationCompleted` carrying the allocated UDP pair. Dedupe by `OperationId` (F11).
5. **Reconcile ports.** On `OperationCompleted`, Web records `GamePort`/`QueryPort`/`DockerContainerId` on the Server (D7). The next `AgentStateSnapshot` reconciles run-state.
6. **UI.** "Register a server" button → dialog (choose Agent/host + name) → `POST /api/servers` (`Server.Register`) → shows Operation progress via the existing `GET /api/operations/{id}`.

## Non-scope

- **Lifecycle** (F15) — start / stop / restart, the stdin-FIFO `save`→`quit` stop path. F14 provisions and registers; it does not run a Server.
- **Health beyond last-reported state** (F16) — the hierarchical health model, probes, OpenTelemetry, live 1 Hz push into the grid, the virtualization-vs-prerender call (F36). F14 stores/show only the coarse observed `ServerRunState`.
- **Configuration** (F20), **mods** (F21), **players/RCON** (F18), **backups** (F24), **updates** (F17) — none of these commands or surfaces.
- **De-provisioning / delete-and-destroy-container** — removing a Server record may be in scope as a registry op, but destroying the container is lifecycle-adjacent; deferred unless trivial. (Decide during PR-A; default: out.)
- **Multi-host move / re-association** — a Server belongs to one Agent, set at import/register; moving it between hosts is not F14.
- **The guided enrollment/agent UI** (F33) and **the rich operations history viewer** (F16) — F14 reuses the minimal operations API, it does not build the history surface.

## Domain changes

- **New aggregate `Server`** (`src/ZWarden.Domain/Servers/Server.cs`) + **new enum `ServerRunState`** (`src/ZWarden.Domain/Servers/ServerRunState.cs`). `ServerId`/`srv-` already exist (no prefix-registry change — `PrefixRegistryTests` already lists `srv-`).
- **New permission `Server.Register`** (TenantWide) in `Permissions`, `All`, `BuiltInRoles`.
- **CONTEXT.md:** the **Server** term already exists; F14 is its first concrete model. Add no new term unless "import" vs "register" needs disambiguating in the glossary — if so, one short note (import = adopt a discovered container; register = provision a new one), consistent with **Enrollment**'s "_Avoid_: registration (which means adding a Server)".

## Contract changes

- **PR-A: none.** Discovery rides the existing `AgentStateSnapshot` (D5). No new command; `ClosedCommandVocabularyTests` unchanged.
- **PR-B: one new command** — `Provisioning.CreateServer : AgentCommand`. Envelope + lifecycle unchanged (ADR 0020); catalogue tests updated.

## Security considerations

- **Tenant isolation is load-bearing** (ADR 0016): `Server` is `ITenantOwned`; every read goes through the tenant filter, every write is stamped/checked by the ownership interceptor. A test asserts a Server created under tenant A is invisible and unwritable under tenant B.
- **Authorization is server-by-server and fail-closed** (ADR 0018): the inventory lists only Servers the user may `Server.View`; import/register require tenant-wide `Server.Register`. Hiding a nav link is never the check — the endpoints re-authorize server-side (an `AuthorizeView` is convenience only).
- **Observed, never inferred** (trust-boundaries §3): run-state changes only from an `AgentStateSnapshot`, never from a Web-side guess; the reconciler never promotes a Server to Running without a fresh observation.
- **Untrusted Agent output** (trust-boundaries §8): discovered orphan ids, labels, and any Agent-authored failure text are data, escaped at render, never instructions and never a way to cross a tenant. A snapshot referencing a `ServerId` this tenant does not own updates nothing.
- **Ownership at provision (PR-B)** rides F13's allowed-container enforcement (`io.zwarden.agent-id == self`); Web never targets a container directly — it dispatches an Operation the Agent authorizes locally.
- **No secrets** in the Server record or on the wire (no RCON password — F18; no registry credentials — pre-provisioned image, F13 D5). Ports are non-secret.
- **Antiforgery** on every mutation (the SameSite=Lax cookie + antiforgery middleware already wired).

## Test plan

Tests are written before the code (PRD 2.2). Tier split by need (ADR 0002).

**Offline unit / domain tier:**
1. **`Server` aggregate** — `Import` leaves `TenantId` unset; `RecordObservedState` updates state + timestamp; `RecordContainer` sets ports/docker id; `Rename` validates non-empty name; invariants (required `AgentId`).
2. **Wire→domain state mapping** — every `Contracts.Protocol.ServerRunState` value maps to the Domain `ServerRunState`; an added wire value fails the mapping test (closed-map guard).
3. **Permission catalogue** — `Server.Register` present, TenantWide, in `All`; `PermissionCatalogueTests` equals the amended PRD 12A list.
4. **Inventory authorization (fail-closed)** — tenant-wide `Server.View` ⇒ all Servers; a server-scoped grant ⇒ only that Server; no grant ⇒ none; import/register without `Server.Register` ⇒ denied.

**Infrastructure / persistence tier:**
5. **Tenant isolation** — a Server under tenant A is invisible/unwritable under tenant B (query filter + ownership interceptor).
6. **Round-trip + typed-id/enum storage** — save/load a Server; `ServerId` stored as native uuid, `LastRunState` stored by name; `Version` is the concurrency token.
7. **Migration** — both providers create the `Servers` table; `MigrationRunner` is idempotent (applies clean, re-runs no-op).

**Reconciliation tier:**
8. **Snapshot reconcile** — a snapshot updates matching Servers' last-reported state; a `ServerId` with no record surfaces as a discovered orphan; a record with no observed id is untouched (no inferred transition); a foreign-tenant id changes nothing.

**Web tier (`WebApplicationFactory<Program>` + bUnit where useful):**
9. **Endpoints** — `GET /api/servers` returns only visible Servers; `POST /api/servers/import` requires `Server.Register`, is antiforgery-protected, writes the record + an audit event, is idempotent on a re-import; failure text is escaped.
10. **Inventory page** — renders KPI tiles + the grid from the service; anonymous → redirect to login; a user without `Server.View` → not-authorized; the nav link is gated.
11. **`StatusBadge`** — each `ServerRunState` renders the right `--status-*` token class (full literal strings; the grep that ADR 0003 relies on).

**PR-B additions:**
12. **`Provisioning.CreateServer`** — in the closed vocabulary; `AgentCommandProcessor` dispatches it, dedupes by `OperationId`, calls the create-template with the allocated stride, reports the ports; unknown-command behaviour unregressed.
13. **Register flow** — `RegisterAsync` writes the record and enqueues a `ProvisionServer` Operation against the new Server's id (per-server lock, ADR 0022); the dispatcher maps the kind to the command; ports/docker id are recorded from `OperationCompleted`.

**Architecture tier:**
14. **Reference direction** — `Server`/`ServerRunState` live in Domain; Domain still references neither Contracts nor Infrastructure; Web references Contracts + Domain for the mapping.

## Implementation slices

Each is independently verifiable and inside one agent context.

**PR-A**
- **S1 — Domain.** `Server` aggregate + `ServerRunState` enum + factory/mutators. *Verify:* T1, T2 (map), T14.
- **S2 — Persistence + migration.** `ServerConfiguration`, `IServerRepository`/`ServerRepository`, DI, `AddServers` on both providers. *Verify:* T5, T6, T7.
- **S3 — Authorization.** `Server.Register` across catalogue/`All`/`BuiltInRoles`/PRD list/`PermissionCatalogueTests`. *Verify:* T3, T4.
- **S4 — Application + reconciliation.** `IServerInventory`, `IServerStateReconciler`, hub wiring for snapshot ingest + orphans. *Verify:* T4, T8.
- **S5 — Endpoints.** `MapServerEndpoints()` (list / discovered / import), authorized + antiforgery. *Verify:* T9.
- **S6 — UI.** App shell (nav + overlay providers), `/servers` inventory (KPI + `BbDataGrid`), import dialog, `StatusBadge` finish, `npm run build:css` + committed `app.css`. *Verify:* T10, T11; CSS diff green.

**PR-B**
- **S7 — Contract + operations.** `Provisioning.CreateServer`, `OperationKind.ProvisionServer`, dispatcher map. *Verify:* T12 (vocabulary), dispatch.
- **S8 — Agent handler.** `AgentCommandProcessor` → port-stride + create-template + ports in the result. *Verify:* T12.
- **S9 — Register flow + reconcile + UI.** `RegisterAsync`, port/docker-id reconcile from `OperationCompleted`, "Register a server" dialog + operation progress. *Verify:* T13.

## Diagnostics

- **Agent offline at import/register** — the registry says the Agent is not connected; surface "host unreachable — the Agent is not connected," distinct from an empty inventory.
- **Import of an unknown/foreign id** — a `ServerId` not in the Agent's last snapshot, or belonging to another tenant, is refused with a specific message, not a generic 400.
- **Provision failure (PR-B)** — the Operation ends `Failed` with the Agent-authored reason (image not pre-provisioned → F13's actionable message; no free stride; create-template refusal), surfaced through the operation-state endpoint, escaped.
- **Stale last-reported state** — the grid shows the `LastStateReportedAt` age so an operator distinguishes "Stopped" from "last heard from an hour ago" (F16 makes this a first-class health signal).

## Documentation

- **`docs/adr/`** — no new ADR (D3); the load-bearing decisions are ADR 0016/0018/0022/0003. If import-vs-register warrants a glossary note, it goes in `CONTEXT.md`, not an ADR.
- **PRD 12A list** — record the `Server.Register` addition in the PR (the catalogue test is the executable copy).
- **`src/ZWarden.Web`** — a short note on the app shell and the static-render decision for tenant-scoped inventory (D6), so F15/F16 authors don't reach for `InteractiveServer` by reflex.
- **`CONTRIBUTING.md`** — nothing new; the migration recipe already covers `AddServers`.

## Acceptance criteria

1. A `Server` is a tenant-owned, versioned aggregate; tenant isolation and ownership are proven by test; `ServerId`/`LastRunState` persist correctly across both providers; `MigrationRunner` is idempotent.
2. `Server.Register` is in the closed catalogue (TenantWide) and the amended PRD 12A list; the catalogue test passes.
3. The inventory lists exactly the Servers the caller may `Server.View`, fail-closed; import/register require `Server.Register`; every mutation is antiforgery-protected and audited.
4. An `AgentStateSnapshot` reconciles last-reported run-state onto matching Servers and surfaces orphans for import, never inferring a transition and never crossing a tenant.
5. Import adopts a discovered orphan into a Server record, idempotently.
6. The `/servers` dashboard renders on the accepted Signal identity through the wrapper seam — KPI tiles + `BbDataGrid` + `StatusBadge` on the `--status-*` tokens — is authorization-gated, and CI's `app.css` diff is green.
7. **(PR-B)** `Provisioning.CreateServer` is in the closed vocabulary; registering a Server enqueues a `ProvisionServer` Operation (per-server lock) that provisions the canonical container via F13's create-template and records the allocated ports; F11's dedupe/unknown-command behaviour is unregressed.

## Definition of Done

Per PRD 61, the applicable subset: acceptance criteria met; tests authored first as executable specifications; offline + architecture + persistence tiers green in CI and the Web tier authored; tenant isolation and per-server authorization carry the same rigour as the auth tests; **no secrets** on the record or the wire; diagnostics exist (agent-offline / bad-import / provision-failure / stale-state); the migration ships for **both** providers and is idempotent; `app.css` regenerated and committed (ADR 0003 condition 2); documentation updated; the first-UI-feature seam rules (ADR 0003 — ZWarden components over theme tokens) held; threat considerations reviewed against trust-boundaries §3/§4/§8 and ADR 0016/0018.
