---
name: run-web
description: Launch and drive the ZWarden.Web app locally to see a change in the real UI — log in and screenshot a page (e.g. /servers). Use when asked to run/start the web app, screenshot it, or confirm a UI change works in the actual app (not just tests).
---

# Run ZWarden.Web locally

> **Full graph vs. this skill.** For the whole distributed system — Web + Agent (self-enrolled and
> Connected) + Postgres + the wollomatic proxy — with one command and a correlated dashboard, use
> **`aspire run`** (ADR 0031; one-time `dotnet dev-certs https --trust` first; see
> `src/ZWarden.AppHost/README.md` and `docs/agents/aspire.md`). This `run-web` skill stays the fast,
> zero-dependency path for a **Web-only** SQLite boot + screenshot when you don't need the Agent/Docker.

ZWarden.Web is a static-SSR Blazor app. Two things make "just `dotnet run`" fail, and both are
handled by the launcher here:

1. **It fails closed without a secret key ring** (ADR 0015) — `ZW_SECRET_KEYS` +
   `ZW_SECRET_ACTIVE_KEY_ID` must be set or startup throws `KeyRingConfigurationException`.
2. **Every useful page is behind auth.** There's no anonymous inventory; you need a confirmed user.
   Setting `ZWarden:Admin:Email` + `ZWarden:Admin:Password` makes `AdminBootstrapper` seed a
   **confirmed Tenant-Owner admin** you can log in as.

Defaults: SQLite (`Data Source=…`), `http://localhost:5063` (use http, not https, for headless).

## 1. (Optional) Seed demo data

Empty pages render fine, but to see the **fleet table, KPI tiles, StatusBadges and lifecycle
buttons** you need Server rows. There's no anonymous seeding API, so use a throwaway EF seeder that
reuses the real mappings. Write these two files to your scratchpad dir (NOT into the repo — a csproj
referencing the internal projects must not live under the solution), then run it against the same DB
path you'll pass to the launcher.

**IMPORTANT: use a Windows-style DB path** (`C:\…\x.db`). The MSYS `/c/…` form makes
Microsoft.Data.Sqlite throw "unable to open database file".

`<scratch>/seed/seed.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <RunAnalyzersDuringBuild>false</RunAnalyzersDuringBuild>
  </PropertyGroup>
  <ItemGroup>
    <!-- Absolute paths to the repo's projects (edit to your clone root). -->
    <ProjectReference Include="C:\Users\marco\code\ZWarden\src\ZWarden.Infrastructure\ZWarden.Infrastructure.csproj" />
    <ProjectReference Include="C:\Users\marco\code\ZWarden\src\ZWarden.Migrations.Sqlite\ZWarden.Migrations.Sqlite.csproj" />
  </ItemGroup>
</Project>
```

`<scratch>/seed/Program.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Persistence;

string conn = args.Length > 0 ? args[0] : "Data Source=zw-demo.db";
DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
    .UseZWardenProvider(ZWardenDbProvider.Sqlite, conn).Options;
await using ZWardenDbContext db = new(options, new DefaultTenant());
await db.Database.MigrateAsync();   // migrates a fresh DB itself; run this BEFORE the web app

DateTimeOffset now = DateTimeOffset.UtcNow;
AgentId a = AgentId.New(), b = AgentId.New();
void Add(AgentId agent, string name, ServerRunState st, int? g, int? q)
{
    Server s = Server.Import(agent, ServerId.New(), name, now);
    if (st != ServerRunState.Unknown) s.RecordObservedState(st, now);
    if (g is int gp && q is int qp) s.RecordContainer($"demo-{name}", gp, qp);
    db.Add(s);
}
Add(a, "riverside-survival", ServerRunState.Running, 16261, 16262);
Add(a, "muldraugh-hardcore", ServerRunState.Stopped, 16265, 16266);
Add(b, "westpoint-modded", ServerRunState.Failed, 16269, 16270);
Add(b, "rosewood-vanilla", ServerRunState.Unknown, null, null);
Console.WriteLine($"seeded {await db.SaveChangesAsync()} servers");

internal sealed class DefaultTenant : ITenantContext
{
    public bool HasCurrentTenant => true;
    public TenantId CurrentTenantId => Tenant.DefaultId;   // the tenant the seeded admin owns
}
```

Run it (Windows path, same file the launcher will use):

```bash
DB='C:\Users\Public\zwarden-dev.db'
rm -f "$(cygpath "$DB" 2>/dev/null || echo /c/Users/Public/zwarden-dev.db)"*   # start clean
dotnet run --project <scratch>/seed/seed.csproj -- "Data Source=$DB"
```

## 2. Launch the app

```bash
bash .claude/skills/run-web/scripts/run-web.sh 'C:\Users\Public\zwarden-dev.db'
```

Run it **in the background** (it blocks serving), then wait for readiness without a foreground sleep:

```bash
curl -s -o /dev/null -w "%{http_code}\n" --retry 40 --retry-delay 2 \
  --retry-all-errors --retry-connrefused http://localhost:5063/login
```

The launcher generates a throwaway key ring and seeds `admin@zwarden.test` /
`Sup3r-Str0ng-P@ss!` (override via `ZW_ADMIN_EMAIL` / `ZW_ADMIN_PASSWORD`). Pass the **same DB path**
you seeded so the demo servers show.

## 3. Drive it and screenshot

Playwright with the **bundled Chromium** — the installed Edge (`channel: 'msedge'`) closes
immediately on this host. One-time setup in a scratch dir:

```bash
cd <scratch> && npm init -y >/dev/null && npm install playwright && npx playwright install chromium
```

Then log in + screenshot any page. **Copy the script into the scratch dir first** — Node resolves the
`playwright` import from the script's own directory, so it must sit next to `node_modules`:

```bash
cp .claude/skills/run-web/scripts/shot.mjs <scratch>/shot.mjs
( cd <scratch> && node shot.mjs servers.png /servers http://localhost:5063 )
```

**Look at the screenshot** (Read the PNG). Rows should render with StatusBadges and per-row
Start/Stop/Restart buttons; a blank frame means login or seeding failed. Deliver it with
SendUserFile. Stop the background app when done (TaskStop).

## Notes / gotchas
- Env keys use the double-underscore form: `ConnectionStrings__ZWarden`, `ZWarden__Admin__Email`,
  `ZWarden__Database__Provider`.
- The admin password must satisfy ASP.NET Identity (upper/lower/digit/symbol).
- Seed **before** starting the web app (the seeder migrates; the app then no-ops the migration and
  seeds the admin) to avoid two processes writing the SQLite file at once.
- Provider can be Postgres (`ZWarden__Database__Provider=postgres` + a Postgres connection string)
  if you need to exercise that tier; SQLite is the zero-setup default.
- Git Bash mangles a leading-slash arg (`/servers` → `C:/Program Files/Git/servers`); `shot.mjs`
  normalizes it (takes the last path segment), so the documented commands work as-is.
- All scratch artifacts (seeder, DB, node_modules, PNG) live in the scratchpad — never commit them.
