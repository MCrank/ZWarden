# Research: the data-layer stack — EF Core 10, both providers, UUIDv7

Resolves [#4](https://github.com/MCrank/ZWarden/issues/4) (wayfinder research), part of [#1](https://github.com/MCrank/ZWarden/issues/1).

**Date of verification:** 2026-09-10. Every fact below was checked against a current official primary
source (Microsoft Learn, the NuGet.org registration/catalog API, the Npgsql docs and GitHub repo, the
Testcontainers docs and repo, the PostgreSQL docs and source, SQLite docs, RFC 9562) and carries a
source URL. Nothing here is answered from model memory.

**Scope.** Facts only. This note deliberately does **not** decide the native-`uuid`-versus-text storage
question from PRD 8 — that is settled by measurement in the dual-provider spike. Where a fact could not
be verified it is marked **UNVERIFIED**.

**PRD anchors.** PRD 3.1 (.NET 10), PRD 5 (TUnit, Testcontainers, real SQLite and real PostgreSQL),
PRD 6 (UUIDv7 identifiers), PRD 8 (strongly typed IDs; storage may be native UUID or text "depending
upon measured database and portability considerations"), PRD 9 (SQLite for small installs, PostgreSQL
for larger; EF Core + migrations; "provider-compatible domain model"; "optimistic concurrency where
appropriate"; avoid provider-specific functionality), PRD 21 (server operation concurrency),
PRD 59 Feature 1 (typed UUIDv7 IDs, "EF conversion strategy"), PRD 59 Feature 2 (exit condition: *the
same core persistence test suite passes against SQLite and PostgreSQL*).

---

## 1. Package inventory

### 1.1 Summary table

| Package id (exact casing) | Current stable | Published | Declared TFMs | Licence |
|---|---|---|---|---|
| `Microsoft.EntityFrameworkCore` | 10.0.12 | 2026-09-08 | `net10.0` only | MIT |
| `Microsoft.EntityFrameworkCore.Abstractions` | 10.0.12 | 2026-09-08 | `net10.0` only | MIT |
| `Microsoft.EntityFrameworkCore.Relational` | 10.0.12 | 2026-09-08 | `net10.0` only | MIT |
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.12 | 2026-09-08 | `net10.0` only | MIT |
| `Microsoft.EntityFrameworkCore.Sqlite.Core` | 10.0.12 | 2026-09-08 | `net10.0` only | MIT |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.12 | 2026-09-08 | `net10.0` only (`developmentDependency`) | MIT |
| `Microsoft.EntityFrameworkCore.Tools` | 10.0.12 | 2026-09-08 | `net8.0`, `net9.0`, `net10.0` | MIT |
| `Microsoft.Data.Sqlite` | 10.0.12 | 2026-09-08 | `netstandard2.0` | MIT |
| `Microsoft.Data.Sqlite.Core` | 10.0.12 | 2026-09-08 | `net8.0`, `netstandard2.0` | MIT |
| `Npgsql` | 10.0.3 | 2026-05-27 | `net8.0`, `net9.0`, `net10.0` | PostgreSQL Licence |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.3 | 2026-07-10 | `net10.0` only | PostgreSQL Licence |
| `Npgsql.DependencyInjection` | 10.0.3 | 2026-05-27 | `net8.0` only | PostgreSQL Licence |
| `Testcontainers` | 4.15.0 | 2026-09-06 | `net8.0`, `net9.0`, `net10.0`, `netstandard2.0`, `netstandard2.1` | MIT |
| `Testcontainers.PostgreSql` | 4.15.0 | 2026-09-06 | same five, real `lib/net10.0` asset | MIT |

### 1.2 EF Core 10 and the SQLite provider

All nine Microsoft packages ship on one release train; 10.0.12 was published **2026-09-08 14:03 UTC**
(sources: the NuGet registration indexes, e.g.
<https://api.nuget.org/v3/registration5-semver1/microsoft.entityframeworkcore/index.json> and
<https://api.nuget.org/v3/registration5-semver1/microsoft.entityframeworkcore.sqlite/index.json>, and
the per-version catalog entries such as
<https://api.nuget.org/v3/catalog0/data/2026.09.08.19.44.30/microsoft.entityframeworkcore.sqlite.10.0.12.json>).

**10.x release history:** 10.0.0 GA **2025-11-11**, then 10.0.1 (2025-12-09), 10.0.2 (2026-01-13),
10.0.3 (2026-02-10), 10.0.4 (2026-03-10), 10.0.5 (2026-03-12), 10.0.6 (2026-04-14), 10.0.7
(2026-04-21), 10.0.8 (2026-05-12), 10.0.9 (2026-06-09), 10.0.10 (2026-07-14), 10.0.11 (2026-08-11),
10.0.12 (2026-09-08). The 9.x line is still shipping in parallel (9.0.20, 2026-09-08).

**net10.0 support is not merely present — it is the only option.** The 10.0.12 catalog entries for
`Microsoft.EntityFrameworkCore`, `.Relational`, `.Abstractions`, `.Sqlite` and `.Sqlite.Core` declare a
single dependency group, `net10.0`, with `lib/net10.0/…` assemblies. Confirmed by the release notes:

> "EF10 requires the .NET 10 SDK to build and requires the .NET 10 runtime to run. EF10 will not run on
> earlier .NET versions, and will not run on .NET Framework."
> — <https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew>

**Support policy.** EF Core 10 is an **LTS** release, released November 2025, supported until
**2028-11-10** (<https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew> and
<https://learn.microsoft.com/en-us/ef/core/what-is-new/>). .NET 10 itself is LTS, GA 2025-11-11, end of
support **2028-11-14** (<https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core>;
<https://github.com/dotnet/core/blob/main/release-notes/10.0/README.md>, which also lists the latest
patch as 10.0.12, 2026-09-08). The two dates differ by four days across the two Microsoft pages; noted
rather than resolved.

**Minimum SQLite engine:** "SQLite (3.46.1 onwards)" — <https://learn.microsoft.com/en-us/ef/core/providers/sqlite/>.
The `Microsoft.EntityFrameworkCore.Sqlite` metapackage bundles the native engine via
`SQLitePCLRaw.bundle_e_sqlite3 [2.1.12,)`; `.Sqlite.Core` does not.

**Newer prereleases exist.** All nine packages carry an `11.0.0` prerelease tail ending at
**`11.0.0-rc.1.26425.128`**, published 2026-09-08
(<https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore/index.json>;
<https://api.nuget.org/v3/registration5-gz-semver2/microsoft.entityframeworkcore/index.json>). EF Core
11.0 stable is scheduled for **November 2026** (<https://learn.microsoft.com/en-us/ef/core/what-is-new/>).
Several complex-type capabilities are EF11, not EF10 — see §5.4.

**Licence.** All nine declare NuGet `licenseExpression: MIT`; `dotnet/efcore` `LICENSE.txt` is the MIT
Licence, © .NET Foundation and Contributors
(<https://github.com/dotnet/efcore/blob/main/LICENSE.txt>). `requireLicenseAcceptance` is `true` on all
nine. **UNVERIFIED:** the licence of the transitive native dependency `SQLitePCLRaw.bundle_e_sqlite3` /
`SQLitePCLRaw.core` 2.1.12 — a separate project, not covered by the efcore LICENSE.

**Doc caveat.** The EF "Supported .NET implementations" table has not been refreshed for EF Core 10 — it
still stops at 9.0, last updated 2025-07-25
(<https://learn.microsoft.com/en-us/ef/core/miscellaneous/platforms>). The authoritative TFM evidence is
the NuGet catalog dependency groups plus the what's-new prerequisite statement above.

### 1.3 Npgsql and Npgsql.EntityFrameworkCore.PostgreSQL

| Package id | Current stable | Published (UTC) |
|---|---|---|
| `Npgsql` | **10.0.3** | 2026-05-27T11:33:04.433Z |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | **10.0.3** | 2026-07-10T08:53:42.443Z |
| `Npgsql.DependencyInjection` | **10.0.3** | 2026-05-27T11:33:06.663Z |

Sources: <https://api.nuget.org/v3/registration5-semver1/npgsql/10.0.3.json>,
<https://api.nuget.org/v3/registration5-semver1/npgsql.entityframeworkcore.postgresql/10.0.3.json>,
<https://api.nuget.org/v3/registration5-semver1/npgsql.dependencyinjection/10.0.3.json>. All three are
`listed: true`. Note the two packages are on **different publish cadences** — the driver's 10.0.3 predates
the EF provider's by six weeks.

**Version-matching with EF Core is enforced by the package.** `Npgsql.EntityFrameworkCore.PostgreSQL`
10.0.3 pins `Microsoft.EntityFrameworkCore` and `.Relational` to **`[10.0.4, 11.0.0)`** and `Npgsql` to
`[10.0.3, )`
(<https://api.nuget.org/v3/catalog0/data/2026.07.10.08.56.27/npgsql.entityframeworkcore.postgresql.10.0.3.json>).
So the provider major tracks the EF Core major exactly, and it floats forward within EF Core 10.x — it
will resolve the latest 10.0.x EF Core (currently 10.0.12, §1.2).

**First EF Core 10-compatible stable:** provider **10.0.0**, published **2025-11-22T17:32:57Z** — eleven
days after EF Core 10 GA
(<https://api.nuget.org/v3/registration5-semver1/npgsql.entityframeworkcore.postgresql/10.0.0.json>;
GitHub tag `v10.0.0` 2025-11-22T17:42:35Z,
<https://api.github.com/repos/npgsql/efcore.pg/releases>). Driver `Npgsql` v10.0.0 shipped the same day
(2025-11-22T17:40:43Z, <https://api.github.com/repos/npgsql/npgsql/releases>).

**net10.0 support** (from the NuGet catalog dependency groups, which are authoritative on TFMs):

- `Npgsql` 10.0.3 **multi-targets `net8.0`, `net9.0`, `net10.0`**. The `net10.0` group depends on
  `Microsoft.Extensions.Logging.Abstractions [10.0.0, )`
  (<https://api.nuget.org/v3/catalog0/data/2026.05.27.11.36.39/npgsql.10.0.3.json>).
- `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 declares a **single `net10.0` group** — consistent with
  EF Core 10 being net10.0-only (§1.2).
- `Npgsql.DependencyInjection` 10.0.3 declares a **single `net8.0` group** — usable from net10.0 via normal
  TFM compatibility, but it is not multi-targeted
  (<https://api.nuget.org/v3/catalog0/data/2026.05.27.11.36.01/npgsql.dependencyinjection.10.0.3.json>).

**UNVERIFIED:** there is no explicit "net10.0 is supported" prose statement in the Npgsql docs or release
notes. The 10.0 release notes only say ".NET 6 being out of support since November 2024, Npgsql 10.0 also
drops support for .NET 6" (<https://www.npgsql.org/doc/release-notes/10.0.html>) and do not enumerate
TFMs. The catalog dependency groups above are the only authoritative evidence.

**Licence: the PostgreSQL Licence** (SPDX `PostgreSQL`) — a permissive BSD/MIT-style licence, **not** MIT.
All three packages declare `licenseExpression: PostgreSQL` / `licenseUrl:
https://licenses.nuget.org/PostgreSQL` (catalog entries above), and GitHub reports "PostgreSQL License"
for both repos (<https://github.com/npgsql/npgsql/blob/main/LICENSE>,
<https://github.com/npgsql/efcore.pg/blob/main/LICENSE>). *This is the one licence in the stack that is
not MIT — worth recording for the supply-chain requirements of PRD 55.*

**Newer prereleases.** `Npgsql.EntityFrameworkCore.PostgreSQL` has **`11.0.0-preview.1` through
`11.0.0-preview.6`** (no 11.x stable)
(<https://api.nuget.org/v3-flatcontainer/npgsql.entityframeworkcore.postgresql/index.json>). The driver
`Npgsql` has **no 11.x at all**, stable or prerelease — 10.0.3 is the highest version
(<https://api.nuget.org/v3-flatcontainer/npgsql/index.json>). **UNVERIFIED:** the publish date of
`11.0.0-preview.6` — the `registration5-semver1` leaf 404s for semver2 prereleases.

**Documentation caveats found while verifying.** Both index URLs
`https://www.npgsql.org/doc/release-notes/` and `https://www.npgsql.org/efcore/release-notes/`
**return HTTP 404**; only the per-version pages resolve (e.g.
<https://www.npgsql.org/doc/release-notes/10.0.html>,
<https://www.npgsql.org/efcore/release-notes/10.0.html>). Separately, the GitHub releases page for
`efcore.pg` has **no release entry for 10.0.1, 10.0.3, or any 11.0.0-preview**, even though those exist on
NuGet — **GitHub releases are not a complete record for that repo; NuGet is authoritative.**

**Npgsql 10.0 driver changes worth knowing** (<https://www.npgsql.org/doc/release-notes/10.0.html>).
New: OpenTelemetry-aligned tracing and metrics (including `db.client.operation.duration`, COPY and
physical-connection-open tracing) — relevant to PRD 49; GSSAPI session encryption; a `RequireAuth`
connection-string parameter; built-in `cube` support; `PGAPPNAME`; SHA3 with SASL; `IDbTypeResolver`.
**Breaking:** .NET 6 dropped; **`date` now maps to `DateOnly` and `time` to `TimeOnly`** (previously
`DateTime`/`TimeSpan`); `cidr` maps to `IPNetwork` instead of the obsolete `NpgsqlCidr`; metric and
tracing names changed; only root CA certificates validate TLS chains (libpq alignment);
`BeginTextImportAsync` returns `NpgsqlCopyTextReader`; `DataTypeName` now takes precedence over
`NpgsqlDbType`.

**EFCore.PG 10.0 provider changes** (<https://www.npgsql.org/efcore/release-notes/10.0.html>).
New: full support for EF 10 **JSON complex types** via `.ToJson()` → `jsonb`; better SQL for scalar
collections nested in JSON (`@>`); **PostgreSQL 18 virtual generated columns** (omit `stored: true`);
**`Guid.CreateVersion7()` translated to `uuidv7()`** on PG 18 (§4.7); `cube` via `NpgsqlCube`; NodaTime
`LocalDate.At()`/`AtMidnight()`. **Breaking:** array `Contains` now defaults to
`element = ANY(arrayColumn)` rather than `@>` (GIN indexes must be modelled explicitly to keep the old
behaviour); network functions return `IPNetwork`; `cidr` scaffolds to `IPNetwork`; `NpgsqlCidr` marked for
removal.

### 1.4 Testcontainers for .NET

| Package | Version | Published (UTC) |
|---|---|---|
| `Testcontainers` | **4.15.0** | 2026-09-06T18:06:28.897Z |
| `Testcontainers.PostgreSql` | **4.15.0** | 2026-09-06T18:06:30.780Z |

Note the id casing: **`Testcontainers.PostgreSql`** — lowercase `q` and `l`, *not* `PostgreSQL`.
(Sources: <https://api.nuget.org/v3/registration5-semver1/testcontainers/index.json>,
<https://api.nuget.org/v3/registration5-semver1/testcontainers.postgresql/index.json>, and the catalog
entries <https://api.nuget.org/v3/catalog0/data/2026.09.06.18.10.30/testcontainers.4.15.0.json> and
<https://api.nuget.org/v3/catalog0/data/2026.09.06.18.09.53/testcontainers.postgresql.4.15.0.json>.)
The GitHub release for 4.15.0 is dated 2026-09-07
(<https://github.com/testcontainers/testcontainers-dotnet/releases>). No prerelease is newer than the
current stable — the newest prereleases are `4.1.0-beta.*`
(<https://api.nuget.org/v3-flatcontainer/testcontainers/index.json>).

**net10.0 support: explicit and first-class.** Both packages declare five dependency groups — `net8.0`,
`net9.0`, `net10.0`, `.NETStandard2.0`, `.NETStandard2.1` — and `Testcontainers.PostgreSql` ships a real
`lib/net10.0/Testcontainers.PostgreSql.dll`, so a `net10.0` test project resolves the dedicated asset
with no netstandard fallback (catalog entries above). Confirmed in the repo:
`<TargetFrameworks>net8.0;net9.0;net10.0;netstandard2.0;netstandard2.1</TargetFrameworks>` in both
<https://github.com/testcontainers/testcontainers-dotnet/blob/develop/src/Testcontainers/Testcontainers.csproj>
and
<https://github.com/testcontainers/testcontainers-dotnet/blob/develop/src/Testcontainers.PostgreSql/Testcontainers.PostgreSql.csproj>;
`global.json` pins SDK `10.0.100`
(<https://github.com/testcontainers/testcontainers-dotnet/blob/develop/global.json>).

.NET 10 support arrived in **4.9.0** (2025-11-23): "feat: Add .NET 10 support (#1572)"
(<https://github.com/testcontainers/testcontainers-dotnet/releases/tag/4.9.0>). The same PR **dropped
`net462`** (<https://github.com/testcontainers/testcontainers-dotnet/pull/1572>), so .NET Framework
consumers now resolve the netstandard2.0 asset.

**Licence:** MIT. NuGet metadata for both packages gives `licenseExpression: MIT`; the repo LICENSE reads
"The MIT License (MIT) / Copyright (c) 2019 - 2026 Andre Hofmeister and other authors"
(<https://github.com/testcontainers/testcontainers-dotnet/blob/develop/LICENSE>).

**Prerequisites.** "Testcontainers requires a Docker-API compatible container runtime… actively tested
against recent versions of Docker on Linux, as well as against Docker Desktop on Mac and Windows"; other
setups "are not actively tested in the main development workflow"
(<https://dotnet.testcontainers.org/>). Linux containers work on all three host OSes; native Windows
containers only on Windows, and "Windows requires the host operating system version to match the
container operating system version" (same page). The docs state **no minimum .NET version** — the TFM
list above is the only authoritative evidence.

**Docker Engine API pinning.** 4.9.0 added `DOCKER_API_VERSION` and pins the API version to `1.44`,
because "Docker Engine v29 introduced breaking changes that affect Docker.DotNet and Testcontainers for
.NET" (<https://github.com/testcontainers/testcontainers-dotnet/releases/tag/4.9.0>). Relevant if the CI
runner's engine is newer.

**PostgreSQL module** (<https://dotnet.testcontainers.org/modules/postgres/>, and the 4.15.0 source
<https://github.com/testcontainers/testcontainers-dotnet/blob/develop/src/Testcontainers.PostgreSql/PostgreSqlBuilder.cs>):

- Documented usage: `new PostgreSqlBuilder("postgres:15.1").Build()`, then `StartAsync()`.
- **The implicit default image is deprecated.** `PostgreSqlImage = "postgres:15.1"` and the
  **parameterless `PostgreSqlBuilder()` constructor** are both `[Obsolete]` in 4.15.0; the supported
  constructors are `PostgreSqlBuilder(string image)` and `PostgreSqlBuilder(IImage image)`. Rationale:
  <https://github.com/testcontainers/testcontainers-dotnet/discussions/1470#discussioncomment-15185721>.
  The image tag must therefore be chosen explicitly — which is also where the PostgreSQL major version
  used by the test suite gets pinned (see §4.3 on `uuidv7()` requiring PG 18).
- Defaults: port `5432`, database/username/password all `postgres`. `Init()` adds
  `-c fsync=off -c full_page_writes=off -c synchronous_commit=off` (citing
  <https://www.postgresql.org/docs/current/non-durability.html>).
- **Wait strategy:** if none is supplied, the module execs
  `pg_isready --host localhost --dbname <db> --username <user>` inside the container and succeeds on exit
  code 0. The explicit `--host` "ensure[s] readiness only after the initdb scripts have executed, and the
  server is listening on TCP/IP". Throws `NotSupportedException` if `pg_isready` is absent (pre-9.3
  images).
- `GetConnectionString()` returns `Host=…;Port=<mapped 5432>;Database=…;Username=…;Password=…`.
  `ExecScriptAsync(scriptContent, ct)` copies a script into the container and runs it through `psql`.
- The generic `GetConnectionString(ConnectionMode)` overload exists at the core-library level, but the
  docs state "Testcontainers modules do not yet implement this feature… Providers will be integrated by
  modules in future releases" (<https://dotnet.testcontainers.org/api/connection_string_provider/>).
- xUnit helper packages `Testcontainers.XunitV3` / `Testcontainers.Xunit` exist
  (<https://dotnet.testcontainers.org/test_frameworks/xunit_net/>). **No TUnit helper package was
  found** — PRD 5 selects TUnit, so container lifecycle will need wiring by hand. Marked
  **UNVERIFIED** only in the sense that absence was not exhaustively proven.

**CI and the Resource Reaper (Ryuk).** "To use Testcontainers in your CI/CD environment, you only require
Docker installed"; **GitHub-hosted runners need no extra configuration**
(<https://dotnet.testcontainers.org/cicd/>). Windows agents "use the Docker Windows engine and cannot run
Linux containers". GitLab CI needs `docker:dind` + `DOCKER_HOST=tcp://docker:2375`. Ryuk cleans up
containers after the run and "**Right now, only Linux containers are supported**"; the docs advise
"Whenever possible, do not disable the Resource Reaper"
(<https://dotnet.testcontainers.org/api/resource_reaper/>). It is disabled with
**`TESTCONTAINERS_RYUK_DISABLED=true`** (properties key `ryuk.disabled`, default `false`); the docs' own
Bitbucket Pipelines example sets it because "Bitbucket Pipelines does not support Ryuk". Ryuk runs
privileged by default (`TESTCONTAINERS_RYUK_CONTAINER_PRIVILEGED`, default `true`) and its image is
pinned by digest to `testcontainers/ryuk:0.14.0@sha256:7c1a8a9a…`. Full configuration table (including
`DOCKER_HOST`, `DOCKER_CONTEXT`, `TESTCONTAINERS_HOST_OVERRIDE`,
`TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE`, and the `wait.strategy.*` timeouts, default timeout `01:00:00`):
<https://dotnet.testcontainers.org/custom_configuration/>.

**Rootless / Podman / Testcontainers Cloud.** A `RootlessUnixEndpointAuthenticationProvider` exists and
is Linux-only
(<https://github.com/testcontainers/testcontainers-dotnet/blob/develop/src/Testcontainers/Builders/RootlessUnixEndpointAuthenticationProvider.cs>),
and the source states "If multiple container runtimes are present in a development environment, we
prioritize Testcontainers Cloud if it is running"
(<https://github.com/testcontainers/testcontainers-dotnet/blob/develop/src/Testcontainers/Builders/TestcontainersEndpointAuthenticationProvider.cs>).
**UNVERIFIED:** there is no docs statement that Podman is officially supported, and no Testcontainers
Cloud documentation page in the repo — only the code artefacts cited.

**First-party alternative, recorded as a fact only, with no recommendation:**
`Aspire.Hosting.PostgreSQL`, newest stable **13.5.3**, published 2026-08-25, `licenseExpression: MIT`,
authors Microsoft, single dependency group targeting **`net8.0`**
(<https://api.nuget.org/v3/catalog0/data/2026.08.25.20.57.38/aspire.hosting.postgresql.13.5.3.json>).

---

## 2. What breaks "one persistence test suite, both providers"

PRD 59 Feature 2's exit condition is that *the same core persistence test suite passes against SQLite and
PostgreSQL*. The verified hazards, grouped by cause.

### 2.1 Modeling concepts SQLite does not support at all

The SQLite provider does not support three concepts from EF's common relational library
(<https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations>):

- **Schemas**
- **Sequences**
- **Database-generated concurrency tokens**

Consequences: `HasDefaultSchema` / `ToTable(schema:)`, `HasSequence` / `UseSequence` / `UseHiLo`, and
`IsRowVersion` / `[Timestamp]` cannot be shared. Note the asymmetry in *how* they fail — `EnsureSchema`
and `DropSchema` are listed as "✔ (no-op)", so schema-qualified DDL **silently loses the schema on
SQLite** rather than raising. A model that is schema-qualified for PostgreSQL will therefore pass on
SQLite while producing a different physical schema.

### 2.2 Types SQLite has no native representation for

Verbatim from <https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations>:

> "SQLite doesn't natively support the following data types. EF Core can read and write values of these
> types, and querying for equality (`where e.Property == value`) is also supported. Other operations,
> however, like comparison and ordering will require evaluation on the client."

The list: **`DateTimeOffset`, `decimal`, `TimeSpan`, `ulong`**. All four work on PostgreSQL natively.
A LINQ predicate that translates to SQL on PostgreSQL may silently become client-evaluated on SQLite —
same result, different execution, and different behaviour once paging or `Any()`/`Count()` are involved.

EF Core 10 narrows this one notch: "Support `MAX`/`MIN`/`ORDER BY` using `decimal` on SQLite"
(<https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew>, PR
<https://github.com/dotnet/efcore/pull/35606>). The limitations page — updated 2026-04-16, i.e. *after*
EF10 GA — still carries the blanket statement. The two Microsoft pages are in tension; the PR is the more
specific evidence.

### 2.3 Facets are enforced on PostgreSQL and ignored on SQLite

> "SQLite allows you to specify type facets like length, precision, and scale, but they are not enforced
> by the database engine. Your app is responsible for enforcing these."
> — <https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types>

`HasMaxLength(n)`, `HasPrecision(18, 2)` and friends are hard constraints on PostgreSQL and decorative on
SQLite. This is the archetypal "green on SQLite, red on PostgreSQL" failure: a test that writes an
over-long string passes on SQLite and raises on PostgreSQL.

The same page warns: "One common gotcha is that using a column type of `STRING` will try to convert values
to INTEGER or REAL… We recommend only using the four primitive SQLite type names."

### 2.4 Ordering semantics diverge because SQLite stores several types as TEXT

SQLite's default type mapping (<https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types>)
stores `DateOnly`, `DateTime`, `DateTimeOffset`, `Decimal`, `Guid`, `TimeOnly` and `TimeSpan` as **TEXT**
(`Decimal` as TEXT because "REAL would be lossy"), `UInt64` as INTEGER with "Large values overflow", and
`Boolean` as INTEGER 0/1.

SQLite's cross-class sort order (<https://www.sqlite.org/datatype3.html>): "values with storage class NULL
come first, followed by INTEGER and REAL values interspersed in numeric order, followed by TEXT values in
collating sequence order, and finally BLOB values in memcmp() order." So `ORDER BY` on a decimal,
timestamp or Guid column is **lexicographic TEXT ordering on SQLite** versus true numeric / `timestamptz`
/ `uuid` ordering on PostgreSQL. For zero-padded ISO-8601 timestamps and canonical UUID text the two
orders coincide; for `decimal` they do not (`"9.0"` sorts after `"10.0"`).

### 2.5 Collation and case sensitivity

<https://learn.microsoft.com/en-us/ef/core/miscellaneous/collations-and-case-sensitivity>:

- Both providers are on the favourable side of the main split: "some databases are case-sensitive by
  default (e.g. Sqlite, PostgreSQL)".
- "EF Core makes no attempt to translate simple equality to a database case-sensitive operation: C#
  equality is translated directly to SQL equality, which may or may not be case-sensitive."
- "**By design, EF Core refrains from translating** these [`string.Equals(…, StringComparison)`]
  overloads to SQL, and **attempting to use them will result in an exception**." — so the portable-looking
  API is unavailable on both.
- "The actual list of available collations and their naming schemes is database-specific." A shared suite
  cannot use one `UseCollation("…")` string for both providers.
- "Specifying an explicit collation in a query will generally prevent that query from using an index."

SQLite's three built-in collations are **BINARY** (memcmp, the default for every column), **NOCASE** and
**RTRIM** (<https://www.sqlite.org/datatype3.html>). NOCASE folds **ASCII only**: "SQLite does not attempt
to do full UTF case folding due to the size of the tables required." PostgreSQL's collation surface is
entirely different. EF's own value-conversion docs make the same warning: ".NET string comparisons and
database string comparisons can differ in more than just case sensitivity. This pattern works for simple
ASCII keys, but may fail for keys with any kind of culture-specific characters."
(<https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions>)

### 2.6 Migrations: limited ALTER TABLE, table rebuilds, no idempotent scripts

<https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations>:

> "If you attempt to apply one of the unsupported operations to a SQLite database then a
> `NotSupportedException` will be thrown."

> "A **rebuild** will be attempted in order to perform certain operations. **Rebuilds are only possible for
> database artifacts that are part of your EF Core model.** If a database artifact isn't part of the
> model — for example, if it was created manually inside a migration — then a `NotSupportedException` is
> still thrown."

| Operation | SQLite |
|---|---|
| AddColumn, CreateIndex, CreateTable, DropIndex, DropTable, RenameColumn, RenameTable, Insert, Update, Delete | ✔ |
| AddCheckConstraint, AddForeignKey, AddPrimaryKey, AddUniqueConstraint, **AlterColumn**, DropCheckConstraint, **DropColumn**, DropForeignKey, DropPrimaryKey, DropUniqueConstraint, RenameIndex | ✔ (rebuild) |
| EnsureSchema, DropSchema | ✔ (no-op) |

**Idempotent migration scripts are impossible on SQLite:** "Unlike other databases, SQLite doesn't include
a procedural language. Because of this, there is no way to generate the if-then logic required by the
idempotent migration scripts." Workarounds given: `dotnet ef migrations script CurrentMigration`, or
`dotnet ef database update --connection "…"`.

**Migration locking differs by mechanism.** EF9 introduced concurrent-migration protection. SQL Server
uses a session-level `sp_getapplock` released when the connection closes; "**SQLite doesn't have built-in
application locks. EF Core instead creates a `__EFMigrationsLock` table and inserts a row to acquire the
lock.**" An abandoned lock (process killed mid-migration) means "each attempt will wait **indefinitely**
for the lock to be released"; recovery is `DROP TABLE "__EFMigrationsLock";`. **This is a direct hazard
for a test suite that times out or kills a migration run against a file-backed SQLite database.**

EF Core 10 also changed migration transaction scope: "Stop spanning all migrations with a single
transaction (#35096). This reverts a change done in EF9 which caused issues in various migration
scenarios." (<https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew>)

### 2.7 Transactions, savepoints, ambient transactions

<https://learn.microsoft.com/en-us/ef/core/saving/transactions>:

- "By default, if the database provider supports transactions, all changes in a single call to
  `SaveChanges` are applied in a transaction." Controlled by
  `Database.AutoTransactionBehavior` = `WhenNeeded` (default) / `Always` / `Never`.
- When a transaction is already in progress, "EF automatically creates a savepoint before saving any
  data… If `SaveChanges` encounters any error, it automatically rolls the transaction back to the
  savepoint… This allows you to possibly correct issues and retry saving, in particular when optimistic
  concurrency issues occur." Manual API: `CreateSavepointAsync` / `RollbackToSavepointAsync` /
  `ReleaseSavepoint`.
- The only documented savepoint incompatibility is SQL Server MARS — no SQLite- or PostgreSQL-specific
  savepoint limitation is documented. **UNVERIFIED:** `Microsoft.Data.Sqlite` savepoint support is not
  addressed either way on that page.
- **`TransactionScope` / `System.Transactions` is a real divergence:** "EF Core relies on database
  providers to implement support for System.Transactions. **If a provider does not implement support for
  System.Transactions, it is possible that calls to these APIs will be completely ignored. SqlClient
  supports it.**" Distributed transactions were added in .NET 7 for **Windows only**. A test asserting
  ambient-transaction rollback can therefore silently no-op on one provider.
- Isolation levels are not interchangeable: "PostgreSQL repeatable reads isolation level" raises a
  serialization error (SQL Server *snapshot* semantics) rather than blocking (SQL Server *repeatable
  read* semantics) — <https://learn.microsoft.com/en-us/ef/core/saving/concurrency>.
- "Manually controlling transactions… is incompatible with implicitly invoked retrying execution
  strategies."

### 2.8 Insert-conflict exceptions are provider-specific

From <https://learn.microsoft.com/en-us/ef/core/saving/concurrency>:

> "EF also throws `DbUpdateConcurrencyException` when attempting to delete a row that has been
> concurrently modified. However, **this exception is generally never thrown when adding entities**; while
> the database may indeed raise a unique constraint violation… this results in a **provider-specific
> exception**, and not `DbUpdateConcurrencyException`."

A shared test that asserts on a unique-constraint violation must not assert on the inner exception type:
SQLite raises `SqliteException`, PostgreSQL an Npgsql exception (see §3.5). `DbUpdateException` is the only
portable assertion.

### 2.9 JSON and complex types

- JSON column support for the SQLite provider landed in **EF Core 8**
  (<https://github.com/dotnet/efcore/issues/28816>, milestone 8.0.0, closed 2023-02-24).
- EF Core 10 adds `ToJson()` for **complex types**
  (<https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew>).
- Physical column type differs: "On SQL Server 2025 and Azure SQL, EF uses the native `json` data type by
  default; **on other databases and older SQL Server versions, JSON is stored in a text column.** You can
  override the column type with `HasColumnType`."
  (<https://learn.microsoft.com/en-us/ef/core/modeling/complex-types>) — so PostgreSQL's `jsonb` is opt-in
  via `HasColumnType`, which is provider-specific configuration.
- `ExecuteUpdate` over JSON is new in EF10 but "requires mapping your types as complex types… and does not
  work when your types are mapped as owned entities".
- Indexing into JSON-mapped complex collections "requires a database that supports JSON indexes, such as
  SQL Server 2025" and is an **EF Core 11** feature.
- Complex-type constraints on every relational provider: no identity or tracking of their own (no
  `DbSet<T>`), no navigations, never their own table, complex **collections must** be mapped to JSON with
  `ToJson` (cannot be table-split), collections of value types (`struct`) unsupported, optional complex
  types require at least one required property or a configured `HasDiscriminator()`. Complex types are
  **not discovered by convention** — they need `[ComplexType]` or `ComplexProperty`/`ComplexCollection`.

### 2.10 EF Core 10 date/time breaking changes on SQLite (High impact)

Three **High**-impact `Microsoft.Data.Sqlite` behaviour changes in EF10, all tracking
<https://github.com/dotnet/efcore/issues/36195>
(<https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes>):

1. **"Using `GetDateTimeOffset` without an offset now assumes UTC"** — previously parsed as the machine
   local time zone. "This is to align with SQLite's behavior where timestamps without an offset are
   treated as UTC."
2. **"Writing `DateTimeOffset` into REAL column now writes in UTC"** — "The value written was incorrect."
3. **"Using `GetDateTime` with an offset now returns value in UTC"** — previously returned
   `DateTimeKind.Local`; now converted and returned as `DateTimeKind.Utc`.

Escape hatch (documented as "a last/temporary resort"):
`AppContext.SetSwitch("Microsoft.Data.Sqlite.Pre10TimeZoneHandling", isEnabled: true);`

These matter because they change the *result values* a SQLite-backed test observes — and because they move
SQLite's behaviour toward, not away from, PostgreSQL's UTC-centric `timestamptz` handling (§3.4).

### 2.11 Other EF Core 10 breaking changes that bite a persistence test suite

From <https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes> unless noted:

- **"SQL parameter names are now simplified"** (#35196): `@__city_0` → `@city`. Explicitly flagged as
  breaking "snapshot tests comparing SQL text, or interceptors and loggers that parse
  `DbCommand.CommandText` or inspect `DbParameter.ParameterName`". *The single most likely EF10 break for
  a SQL-assertion suite.*
- **"Parameterized collections now use multiple parameters by default"** (#34346): `ids.Contains(b.Id)`
  now emits `IN (@ids1, @ids2, @ids3)` rather than a JSON-array parameter, and EF **pads** the list
  (8 values → 10 parameters, the last two duplicating the 8th) to limit SQL variety. Modes:
  `ParameterTranslationMode.MultipleParameters` (new default) / `.Constant` / `.Parameter`, set globally
  with `UseParameterizedCollectionMode(...)` or per query with `EF.Constant` / `EF.Parameter` /
  `EF.MultipleParameters`.
- **"Complex type column names are now uniquified"** and **"Nested complex type properties use full path
  in column names"** (`NestedComplex_Property` → `Complex_NestedComplex_Property`). Both **change
  generated DDL**, so migration snapshots differ.
- **"Redact inlined constants from logging by default"** — inlined values log as `?` unless
  `EnableSensitiveDataLogging()`. Affects log-assertion tests.
- **"EF tools now require framework to be specified for multi-targeted projects"** (#37230): a project
  using `<TargetFrameworks>` must pass `--framework`.
- **`ExecuteUpdateAsync` now accepts a regular, non-expression lambda** (#32018) — a compile break for
  code building `Expression<Func<SetPropertyCalls<T>, …>>` trees.
- **"More consistent ordering for split queries"** — split queries previously omitted the key column from
  the inner subquery's `ORDER BY`, "leading to possible incorrect data being returned"; EF10 appends it.
  Changes generated SQL for `AsSplitQuery()` + `Include` + `OrderBy` + `Take`.
- **New, per-provider translations** are themselves a portability hazard: `DateOnly.ToDateTime()`
  (#35194); `DateOnly.DayNumber` and DayNumber subtraction **"for SQL Server and SQLite"** (#36183);
  `MAX`/`MIN`/`ORDER BY` on `decimal` for SQLite (#35606). A query that translates on one provider may not
  on the other.
- Also new in EF10 and provider-neutral: `.LeftJoin(...)` / `.RightJoin(...)` are recognised from the
  .NET 10 BCL ("C# query syntax… doesn't yet support expressing left/right join operations"), and
  **named query filters** — `HasQueryFilter("SoftDeletionFilter", b => !b.IsDeleted)` plus
  `IgnoreQueryFilters(["SoftDeletionFilter"])`, replacing the one-filter-per-entity-type restriction
  (<https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew>).

---

## 3. Npgsql-side provider behaviour that breaks the shared suite

§2 covered the SQLite-side constraints. These are the PostgreSQL-side divergences, i.e. the cases where
the *same* EF Core code does something different because the provider is Npgsql.

### 3.1 `LIKE` case sensitivity — a silent behavioural fork

This is the sharpest of the set, because nothing errors and no configuration flags it.

- **SQLite's `LIKE` is case-INSENSITIVE for ASCII by default.** Verbatim: "Any other character matches
  itself or its lower/upper case equivalent (i.e. case-insensitive matching). **Important Note:** SQLite
  only understands upper/lower case for ASCII characters by default. The LIKE operator is case sensitive
  by default for unicode characters that are beyond the ASCII range. For example, the expression
  `'a' LIKE 'A'` is TRUE but `'æ' LIKE 'Æ'` is FALSE." Overridable with the `case_sensitive_like` pragma;
  `GLOB` is case-sensitive. (<https://www.sqlite.org/lang_expr.html>)
- **PostgreSQL's `LIKE` is case-sensitive.** `ILIKE` is the case-insensitive form.
- EF Core translates `string.Contains` / `StartsWith` / `EndsWith` to `LIKE` on Npgsql (`LIKE %value%`,
  `LIKE value || '%'`, `LIKE '%' || value`) — <https://www.npgsql.org/efcore/mapping/translations.html>.

**Consequence: the identical `Contains` / `StartsWith` / `EndsWith` test passes case-INSENSITIVELY on
SQLite and case-SENSITIVELY on PostgreSQL.** A search test seeded with mixed-case data will disagree
across providers with no error on either side.

`EF.Functions.ILike(matchExpression, pattern)` → `matchExpression ILIKE pattern` is **Npgsql-only**; there
is no `ILIKE` in SQLite, so any test using it cannot be part of the shared suite
(<https://www.npgsql.org/efcore/mapping/translations.html>,
<https://www.npgsql.org/efcore/misc/collations-and-case-sensitivity.html>).
`string.Compare` / `CompareTo` translate to `CASE WHEN` expressions on Npgsql.

### 3.2 Text ordering: byte order versus locale

- **SQLite:** every column defaults to `BINARY`, i.e. `memcmp()` (§2.5).
- **PostgreSQL:** the default collation comes from the **locale chosen at database-creation time**, and
  text ordering follows that locale (ICU or libc). `C`/`POSIX` sorts by raw byte value; only ASCII `A`–`Z`
  count as letters there. Ordering results therefore differ between a `C`-collated database and an
  `en_US`-collated one. (<https://www.postgresql.org/docs/current/collation.html>)

**`ORDER BY` on a text column is therefore the highest-risk assertion in a shared suite**: SQLite gives
byte order; PostgreSQL gives locale-dependent linguistic order. The Testcontainers image's default locale
determines which — and the CI container's locale need not match a developer's local PostgreSQL.

PostgreSQL **non-deterministic** (case-/accent-insensitive ICU) collations carry their own restrictions:
"certain operations are not possible with nondeterministic collations, such as some pattern matching
operations", and B-tree cannot use deduplication with them
(<https://www.postgresql.org/docs/current/collation.html>). Npgsql's own page adds that database-level
collations **cannot** be non-deterministic (use EF pre-convention model configuration instead), that PG 18
handles case-insensitive collations better than earlier versions, and covers the legacy `citext` extension
(`HasPostgresExtension("citext")`) with its accent and function-overload limitations
(<https://www.npgsql.org/efcore/misc/collations-and-case-sensitivity.html>). PostgreSQL also supports
`CREATE COLLATION` via `HasCollation(… provider: "icu", deterministic: false)` — none of which has a
SQLite counterpart.

### 3.3 PostgreSQL's failed-transaction state

**The most likely single cause of a "passes on SQLite, fails on PostgreSQL" persistence test.**

After **any** error inside a PostgreSQL transaction, every subsequent command fails with SQLSTATE
**`25P02` = `in_failed_sql_transaction`** until the transaction is ended or rolled back to a savepoint
(<https://www.postgresql.org/docs/current/errcodes-appendix.html>). SQLite has no equivalent
whole-transaction-abort semantics.

So a test that opens a transaction, provokes an expected constraint violation, catches it, and then
continues issuing commands on the same transaction **works on SQLite and fails on PostgreSQL** unless a
savepoint rollback intervenes. EF's automatic savepoint-before-`SaveChanges` behaviour (§2.7) covers the
`SaveChanges` path; hand-rolled command sequences inside an explicit transaction are not covered.

### 3.4 DateTime / DateTimeOffset / timestamptz

**Npgsql's current mapping rules** (<https://www.npgsql.org/doc/types/datetime.html>):

| Direction | Mapping |
|---|---|
| Read `timestamp with time zone` | `DateTime` with `Kind=Utc` (or `DateTimeOffset`) |
| Read `timestamp without time zone` | `DateTime` with `Kind=Unspecified` |
| Read `date` / `time` / `timetz` / `interval` | `DateOnly` (or `DateTime`) / `TimeOnly` (or `TimeSpan`) / `DateTimeOffset` / `TimeSpan` or `NpgsqlInterval` |
| Write `DateTime(Utc)` | `timestamp with time zone` |
| Write `DateTime(Local)` or `DateTime(Unspecified)` | `timestamp without time zone` |
| Write `DateTimeOffset` | `timestamp with time zone` |
| Write `DateOnly` / `TimeOnly` / `TimeSpan` | `date` / `time without time zone` / `interval` |

**The Npgsql 6.0 breaking change**, still the current regime, verbatim from
<https://www.npgsql.org/doc/release-notes/6.0.html>:

> "UTC DateTime is now strictly mapped to `timestamptz`, while Local/Unspecified DateTime is now strictly
> mapped to `timestamp`."
> "It is no longer possible to write UTC DateTime as `timestamp`, or Local/Unspecified DateTime as
> `timestamptz`."
> "`timestamptz` values are now read back as DateTime with Kind=UTC, without any conversions."

**What throws on PostgreSQL and cannot throw on SQLite:** writing a non-UTC `DateTime` to `timestamptz`;
writing a `DateTimeOffset` whose offset is **non-zero** (PostgreSQL cannot represent it); reading or
writing `timetz` as `DateTime`/`TimeSpan`; reading an `interval` with month or year components as
`TimeSpan`. Opt-out: `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);`, which "must be
set before any Npgsql operations are invoked".

Note also the Npgsql 10.0 driver change (§1.3): **`date` now maps to `DateOnly` and `time` to `TimeOnly`**,
where earlier versions used `DateTime`/`TimeSpan`.

On the SQLite side there is no date/time type at all (§2.4), `DateTimeOffset` cannot be compared or ordered
server-side (§2.2), and EF Core 10's three High-impact UTC changes (§2.10) move SQLite *toward* Npgsql's
UTC-centric regime. The portable shape is therefore a UTC `DateTime` (or `DateTimeOffset` with a zero
offset), which is what PRD-level "store timestamps in UTC" discipline would give anyway.

### 3.5 Exception types on constraint violation

EF Core is explicit that this is provider-specific (§2.8). The concrete types:

- **PostgreSQL: `Npgsql.PostgresException`** — "The exception that is thrown when the PostgreSQL backend
  reports errors (e.g. query SQL issues, constraint violations)", deriving
  `NpgsqlException` → `DbException` → `ExternalException` → `SystemException`. Properties include
  `SqlState` ("The SQLSTATE code for the error", always present), `MessageText`, `Severity`, `Detail`,
  `Hint`, `Position`, `SchemaName`, `TableName`, `ColumnName`, and **`ConstraintName`**.
  (<https://www.npgsql.org/doc/api/Npgsql.PostgresException.html>)
- **SQLite: `Microsoft.Data.Sqlite.SqliteException : System.Data.Common.DbException`**, with
  `SqliteErrorCode` and `SqliteExtendedErrorCode` — integer result codes rather than SQLSTATE strings, and
  **no `ConstraintName` property**
  (<https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlite.sqliteexception>).

`PostgresErrorCodes` constants, from the source
(<https://github.com/npgsql/npgsql/blob/main/src/Npgsql/PostgresErrorCodes.cs>):
`UniqueViolation = "23505"`, `ForeignKeyViolation = "23503"`, `NotNullViolation = "23502"`,
`CheckViolation = "23514"`, `IntegrityConstraintViolation = "23000"`, `SerializationFailure = "40001"`,
`DeadlockDetected = "40P01"` — condition names `unique_violation`, `foreign_key_violation`,
`not_null_violation`, `check_violation`, `serialization_failure`, `deadlock_detected` confirmed at
<https://www.postgresql.org/docs/current/errcodes-appendix.html>.

**Consequence:** a shared test can assert `DbUpdateException`, but **cannot** assert on the inner exception
type, on an error code, or on *which* unique index was violated — `ConstraintName` exists only on the
PostgreSQL side.

### 3.6 Value generation: identity by default vs AUTOINCREMENT

<https://www.npgsql.org/efcore/modeling/generated-properties.html>:

- **"The default value generation strategy is 'identity by default'"** — `GENERATED BY DEFAULT AS
  IDENTITY`, which still permits application-supplied values. `UseIdentityAlwaysColumn()` gives
  `GENERATED ALWAYS AS IDENTITY` and **rejects** application values;
  `UseIdentityByDefaultColumns()` sets it model-wide; `UseSerialColumn()` is the legacy path "recommended
  only for older PostgreSQL versions"; `UseHiLo()` is available per-property or model-wide (default block
  size 100); manual sequences via `HasDefaultValueSql("nextval('\"SequenceName\"')")`.
- **None of this is shareable with SQLite**, which has no sequences (§2.1) and a SQLite-only
  `UseAutoincrement()` / `SqliteValueGenerationStrategy` surface (§5.3).

**Identity gaps differ, and this breaks exact-key assertions.** PostgreSQL sequences are non-transactional:
"the value obtained by `nextval` is not reclaimed for re-use if the calling transaction later aborts. This
means that transaction aborts or database crashes can result in gaps in the sequence of assigned values";
`setval` changes "are not undone if the calling transaction rolls back"; and "PostgreSQL sequence objects
*cannot be used to obtain 'gapless' sequences*"
(<https://www.postgresql.org/docs/current/functions-sequence.html>). A test asserting exact generated key
values after a rolled-back insert passes on SQLite's rowid and fails on PostgreSQL.

*(Client-side UUIDv7 keys — PRD 6 — sidestep this entire section, since the application supplies the key
before `SaveChanges`.)*

### 3.7 `decimal` / `numeric`

PostgreSQL `numeric` ↔ .NET `decimal` (`NpgsqlDbType.Numeric`, `DbType.Decimal`/`VarNumeric`); `numeric`
can also be read as `byte, short, int, long, float, double, BigInteger` (BigInteger since 6.0), and
`BigInteger` writes to `numeric` (<https://www.npgsql.org/doc/types/basic.html>).

PostgreSQL `numeric` is **exact and far wider than .NET `decimal`**: max explicitly-declarable precision
1000; unconstrained columns allow up to 131,072 digits before and 16,383 after the decimal point.
PostgreSQL also treats `NaN` "as equal, and greater than all non-`NaN` values" so numerics sort in B-trees
(<https://www.postgresql.org/docs/current/datatype-numeric.html>). Combined with §2.3 (SQLite ignores
precision/scale facets) and §2.4 (SQLite stores decimals as TEXT and sorts them lexically), any
money/precision assertion, `OrderBy(decimal)`, `Max(decimal)` or range filter can diverge.

**UNVERIFIED:** what Npgsql does when a PostgreSQL `numeric` value exceeds .NET `decimal`'s range —
`https://www.npgsql.org/doc/types/numeric.html` returns 404 and no other doc statement was found.

### 3.8 Arrays and jsonb: PostgreSQL-native shapes with no SQLite equivalent

**Arrays** (<https://www.npgsql.org/efcore/mapping/array.html>). Npgsql maps `string[]` / `List<string>` to
native `text[]` columns with change detection, and translates indexing (`array[0]` → `array[1]`, 1-based),
`Length` → `cardinality()`, `Skip`/`Take` → `array[3,5]`, `Contains`, `Concat` → `||`, `IndexOf` →
`array_position(...) - 1`, and `EF.Functions.ArrayAgg` → `array_agg()`. Multidimensional PostgreSQL arrays
"aren't yet supported by the EF Core provider". **EFCore.PG 10 breaking change:** array `Contains` now
defaults to `element = ANY(arrayColumn)` instead of `@>`, so GIN indexes must be modelled explicitly to
keep the old behaviour (<https://www.npgsql.org/efcore/release-notes/10.0.html>).

SQLite has no array type; EF Core primitive collections there are JSON-in-text. Generated SQL, index
behaviour and the translated operator set are entirely different.

**JSON** (<https://www.npgsql.org/efcore/mapping/json.html>). Npgsql supports both `json` and `jsonb`, with
`jsonb` "almost always preferred for efficiency reasons". As of EF 10, "complex types are the recommended
way to map .NET types" via `ToJson()`, and the examples map to `jsonb`; the pre-8.0 POCO mapping via
System.Text.Json is "now considered deprecated". `JsonDocument` is auto-mapped to `jsonb` (and is
`IDisposable`). Translated operators: `->`, `->>`, `@>`, `?`, `?|`, `?&`, `#>>`, `jsonb_array_length()`,
plus `jsonpath` type mapping added in 9.0
(<https://www.npgsql.org/efcore/release-notes/9.0.html>).

SQLite's JSON is text with `json_*` functions — no `jsonb`, no GIN indexing, and none of the PostgreSQL
containment or existence operators (see also §2.9).

### 3.9 Migrations produce genuinely different DDL

PostgreSQL supports as real DDL everything SQLite needs a table rebuild or a `NotSupportedException` for
(§2.6), plus schemas, sequences, `CREATE EXTENSION` (`HasPostgresExtension`), `CREATE COLLATION`, and — on
PG 18 — virtual generated columns
(<https://www.npgsql.org/efcore/misc/collations-and-case-sensitivity.html>,
<https://www.npgsql.org/efcore/release-notes/10.0.html>).

**A single migration set generated for PostgreSQL will not apply cleanly to SQLite, and vice versa.** The
two provider models produce different DDL by construction: identity vs AUTOINCREMENT, `jsonb` vs TEXT,
`text[]` vs JSON text, `uuid` vs TEXT, schemas vs none. Feature 2's "migration execution strategy"
deliverable therefore has to account for **two migration assemblies or two model configurations**, not one
migration set applied twice. *(Stated as a consequence of the verified facts, not as a recommendation.)*

### 3.10 `IsRowVersion` means something different on each provider

This is the single hardest item in the shared suite, so it is worth stating precisely.

- **PostgreSQL has no `rowversion` type**, but Npgsql maps `IsRowVersion()` to the **`xmin` system
  column**. Verbatim: "All PostgreSQL tables have a set of implicit and hidden system columns", including
  `xmin`, which "holds the ID of the latest updating transaction"; because it "automatically gets updated
  every time the row is changed, it is ideal for use as a concurrency token." The property must be a
  **`uint`**, configured either with `[Timestamp]` on `public uint Version { get; set; }` or fluently with
  `.Property(b => b.Version).IsRowVersion()`
  (<https://www.npgsql.org/efcore/modeling/concurrency.html>).
- So `IsRowVersion()` **does work** on Npgsql — but it means "map to `xmin`" and requires a `uint`
  property, **not** the `byte[]` that SQL Server's `rowversion` uses.
- **SQLite supports no database-generated concurrency token at all** (§2.1, §6.2). `IsRowVersion()` has no
  SQLite implementation.

**Net effect: the concurrency-token property type itself differs** — `uint` mapped to `xmin` on PostgreSQL
versus an application-managed token on SQLite. A shared entity model cannot use one concurrency-token
configuration for both providers. The one configuration that *is* identical on both is the
application-managed token of §6.3.

**UNVERIFIED:** whether and when `UseXminAsConcurrencyToken` was removed or obsoleted — the current Npgsql
concurrency page simply does not mention it.

---

## 4. UUIDv7: what exists, where

### 4.1 RFC 9562 — the specification

<https://www.rfc-editor.org/rfc/rfc9562.html>. **Standards Track, May 2024, obsoletes RFC 4122.**
Authors K. Davis (Cisco Systems), B. Peabody (Uncloud), P. Leach (University of Washington). PostgreSQL's
own docs note "RFC 9562 defines 8 different UUID versions (previously RFC 4122)"
(<https://www.postgresql.org/docs/current/datatype-uuid.html>).

**Byte order (§4):** "In the absence of explicit application or presentation protocol specification to the
contrary, each field is encoded with the most significant byte first (known as 'network byte order')." —
i.e. the binary form is **big-endian**.

**UUIDv7 layout (§5.7):** "UUIDv7 features a time-ordered value field derived from the widely implemented
and well-known Unix Epoch timestamp source, the number of milliseconds since midnight 1 Jan 1970 UTC,
leap seconds excluded."

| Field | Bits | Definition (verbatim) |
|---|---|---|
| `unix_ts_ms` | 48 | "48-bit big-endian unsigned number of the Unix Epoch timestamp in milliseconds as per Section 6.1." |
| `ver` | 4 | "The 4-bit version field as defined by Section 4.2, set to 0b0111 (7)." |
| `rand_a` | 12 | "12 bits of pseudorandom data to provide uniqueness as per Section 6.9 and/or optional constructs to guarantee additional monotonicity as per Section 6.2." |
| `var` | 2 | "The 2-bit variant field as defined by Section 4.1, set to 0b10." |
| `rand_b` | 62 | "The final 62 bits of pseudorandom data to provide uniqueness as per Section 6.9 and/or an optional counter to guarantee additional monotonicity as per Section 6.2." |

48 + 4 + 12 + 2 + 62 = 128 bits, with 74 bits available for randomness or counters.

**Monotonicity is only guaranteed to the millisecond by the baseline.** §6.2: "Monotonicity (each
subsequent value being greater than the last) is the backbone of time-based sortable UUIDs" — but the
field definitions above place sub-millisecond monotonicity in *optional* constructs. Within a single
millisecond, ordering is random unless the implementation adds a counter or extra clock precision.

**Sorting (§6.11):** "UUIDv6 and UUIDv7 are designed so that implementations that require sorting (e.g.,
database indexes) sort as opaque raw bytes without the need for parsing or introspection." And: "UUID
formats created by this specification are intended to be lexicographically sortable while in the textual
representation." So **both** the big-endian raw 16 bytes and the canonical hex text sort in time order, and
the two orders agree, because the hex text is a direct big-endian transcription. §6.13 covers "DBMS and
Database Considerations".

### 4.2 .NET 10

**The UUIDv7 API surface in .NET 10 is exactly what .NET 9 shipped — .NET 10 adds nothing further.**

`Guid.CreateVersion7` — <https://learn.microsoft.com/en-us/dotnet/api/system.guid.createversion7>:

```csharp
public static Guid CreateVersion7();
public static Guid CreateVersion7(DateTimeOffset timestamp);
```

Both are documented as "Creates a new `Guid` according to RFC 9562, following the Version 7 format."
`CreateVersion7()` "uses `UtcNow` to determine the Unix Epoch timestamp source"; the overload throws
`ArgumentOutOfRangeException` if the timestamp "represents an offset prior to `UnixEpoch`".

**No monotonicity guarantee is documented.** Both overloads state only that "This method seeds the
`rand_a` and `rand_b` sub-fields with **random data**" — .NET does not document use of the RFC 9562 §6.2
counter or increased-clock-precision methods. Two `Guid`s created in the same millisecond are therefore
**not** documented to be ordered relative to each other. (Contrast PostgreSQL's `uuidv7()`, §4.3, which
does implement Method 3.)

`Guid.Version` — <https://learn.microsoft.com/en-us/dotnet/api/system.guid.version> — "Gets the value of
the version field"; "corresponds to the most significant 4 bits of the 6th byte:
`00000000-0000-F000-0000-000000000000`".

**First shipped in .NET 9.** The API pages' moniker lists are `net-9.0`, `net-10.0`, `net-11.0`, and the
.NET 9 release notes say so outright: "**In .NET 9, you can create a `Guid` according to Version 7 via the
new `Guid.CreateVersion7()` and `Guid.CreateVersion7(DateTimeOffset)` methods.** You can also use the new
`Version` property to retrieve a `Guid` object's version field."
(<https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9/libraries>). Both are present in
.NET 10 (`net-10.0` is the default moniker).

**.NET 10 adds nothing about Guid or UUID.**
<https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries> contains no mention of Guid,
UUID, UUIDv7 or endianness (its sections are Cryptography, Globalization and date/time, Strings,
Collections, Serialization, System.Numerics, Options validation, Diagnostics, ZIP files, Windows process
management, WebSocket enhancements, TLS enhancements), and the .NET 10 release-notes README lists no
UUID/Guid items (<https://github.com/dotnet/core/blob/main/release-notes/10.0/README.md>).

**The `Guid` byte layout is mixed-endian, and this is the sharp edge.**
<https://learn.microsoft.com/en-us/dotnet/api/system.guid.tobytearray> states verbatim:

> "Note that the order of bytes in the returned byte array is different from the string representation of a
> `Guid` value. **The order of the beginning four-byte group and the next two two-byte groups is reversed,
> whereas the order of the last two-byte group and the closing six-byte group is the same.**"

The docs' own example makes it concrete — `Guid` `35918bc9-196d-40ea-9779-889d79b753f0` yields bytes
`C9 8B 91 35 6D 19 EA 40 97 79 88 9D 79 B7 53 F0`. So a UUIDv7's leading 48-bit timestamp is **scattered
across non-adjacent, byte-reversed positions** in `ToByteArray()` output, and a blob in that layout does
**not** sort in time order.

The endianness-aware primitives, both **added in .NET 8** (monikers `net-8.0` … `net-11.0`):

- `public byte[] ToByteArray(bool bigEndian)` — <https://learn.microsoft.com/en-us/dotnet/api/system.guid.tobytearray>
- `public Guid(ReadOnlySpan<byte> b, bool bigEndian)` — <https://learn.microsoft.com/en-us/dotnet/api/system.guid.-ctor>

**Correction to a common assumption:** there is **no** `new Guid(byte[], bool bigEndian)` overload. The
full constructor list is `Guid(Byte[])`, `Guid(ReadOnlySpan<Byte>)`, `Guid(String)`,
`Guid(ReadOnlySpan<Byte>, Boolean)`, `Guid(Int32, Int16, Int16, Byte[])`, `Guid(Int32, Int16, Int16,
Byte×8)`, `Guid(UInt32, UInt16, UInt16, Byte×8)` — the endianness-aware one is **span-based only**
(<https://learn.microsoft.com/en-us/dotnet/api/system.guid.-ctor>).

### 4.3 PostgreSQL

**The `uuid` type.** <https://www.postgresql.org/docs/current/datatype-uuid.html> (currently PG 18)
describes a UUID as "a 128-bit quantity", cites RFC 9562, and notes PostgreSQL "provides native support
for generating UUIDs using UUIDv4 and UUIDv7 algorithms" and "standard comparison operators for UUIDs".

Accepted input formats — all equivalent: standard lower-case hex; upper case; braces; hyphens omitted; a
hyphen after any group of four digits. **Output is always the standard lower-case hyphenated form.**

**Storage size: 16 bytes.** The docs page states 128 bits but not the byte count; the source is explicit —
`#define UUID_LEN 16` and `typedef struct pg_uuid_t { unsigned char data[UUID_LEN]; }` in
<https://github.com/postgres/postgres/blob/master/src/include/utils/uuid.h>. So a `uuid` is a fixed
16-byte, non-varlena value holding the **raw big-endian RFC 9562 octets**, not the text form. *(Flagged:
the 16-byte figure comes from the source header, not the docs page.)*

**Ordering is unsigned big-endian byte comparison.** From
<https://github.com/postgres/postgres/blob/master/src/backend/utils/adt/uuid.c>:

```c
static int
uuid_internal_cmp(const pg_uuid_t *arg1, const pg_uuid_t *arg2)
{
	return memcmp(arg1->data, arg2->data, UUID_LEN);
}
```

Because the stored bytes are the big-endian octets and comparison is `memcmp` over `unsigned char[16]`,
a UUIDv7's leading 48-bit timestamp dominates the sort. **B-tree indexes on a `uuid` column holding v7
values are therefore time-ordered** — exactly the property RFC 9562 §6.11 describes.

**Built-in UUID functions** (<https://www.postgresql.org/docs/current/functions-uuid.html>):

| Function | Documented behaviour |
|---|---|
| `gen_random_uuid() → uuid` | "Generates a version 4 (random) UUID." |
| `uuidv4() → uuid` | "Generates a version 4 (random) UUID." |
| `uuidv7([shift interval]) → uuid` | "Generates a version 7 (time-ordered) UUID. The timestamp is computed using UNIX timestamp with millisecond precision + sub-millisecond timestamp + random." Optional `shift` offsets the timestamp; valid range 1970-01-01 UTC to ~year 10889, else an error is raised. |
| `uuid_extract_timestamp(uuid) → timestamptz` | "Extracts a `timestamp with time zone` from a UUID of version 1 or 7. For other versions, this function returns null." |
| `uuid_extract_version(uuid) → smallint` | "Extracts the version from a UUID of one of the variants described by RFC 9562." |

**Version attribution.** `uuidv7()` and the `uuidv4()` alias are **PostgreSQL 18**: "Add `UUID` version 7
generation function `uuidv7()` (Andrey Borodin). This `UUID` value is temporally sortable. Function alias
`uuidv4()` has been added to explicitly generate version 4 UUIDs."
(<https://www.postgresql.org/docs/current/release-18.html>). The two **extraction** functions are older —
**PostgreSQL 17**: "Add functions `uuid_extract_timestamp()` and `uuid_extract_version()` to return UUID
information (Andrey Borodin)" (<https://www.postgresql.org/docs/release/17.0/>, released 2024-09-26).
*(This corrects a plausible assumption that all five arrived together in 18.)*

**PostgreSQL's `uuidv7()` is more monotonic than the RFC baseline or .NET.** The implementation comment in
<https://github.com/postgres/postgres/blob/master/src/backend/utils/adt/uuid.c> reads: "To ensure
monotonicity in scenarios of high-frequency UUID generation, we employ the method 'Replace Leftmost Random
Bits with Increased Clock Precision (Method 3)'" — the 12 `rand_a` bits carry sub-millisecond precision as
a 1/4096 fraction of a millisecond.

**Current stable major: PostgreSQL 18, released 2025-09-25**, current minor 18.6, EOL 2030-11-14. Also
supported: 17 (17.11), 16 (16.15), 15 (15.19), 14 (14.24, EOL 2026-11-12). Each major gets five years;
minor releases at least every three months. (<https://www.postgresql.org/support/versioning/>;
<https://www.postgresql.org/docs/current/release-18.html>)

**Pre-18 options.** `gen_random_uuid()` is a core function; pgcrypto's same-named function is now a shim —
"(Obsolete, this function internally calls the core function of the same name.)"
(<https://www.postgresql.org/docs/current/pgcrypto.html>). `uuid-ossp` "provides additional functions that
implement other standard algorithms for generating UUIDs"
(<https://www.postgresql.org/docs/current/uuid-ossp.html>). The third-party `pg_uuidv7` extension
(<https://github.com/fboulnois/pg_uuidv7>, MPL-2.0) provides `uuid_generate_v7()`,
`uuid_v7_to_timestamptz()`, `uuid_timestamptz_to_v7()`, "Supports Postgres 13 through 18", and notes "As
of Postgres 18, there is a built in `uuidv7()` function, however it does not include all of the
functionality below."

**Consequence for the test suite:** a database-side `uuidv7()` default requires the container image to be
**PostgreSQL 18 or newer**, which interacts with §1.4 — the Testcontainers module no longer supplies a
default image, so the tag is chosen explicitly and its major version is a deliberate decision.

### 4.4 SQLite

**There is no native UUID/GUID type.** <https://www.sqlite.org/datatype3.html> gives an exhaustive list of
storage classes:

> "Each value stored in an SQLite database … has one of the following storage classes: **NULL**;
> **INTEGER** … stored in 0, 1, 2, 3, 4, 6, or 8 bytes depending on the magnitude of the value;
> **REAL** … an 8-byte IEEE floating point number; **TEXT** … stored using the database encoding (UTF-8,
> UTF-16BE or UTF-16LE); **BLOB** … stored exactly as it was input."

A UUID must therefore be TEXT (36-char canonical or 32 hex chars) or a 16-byte BLOB. SQLite follows the
same pattern elsewhere: "SQLite does not have a separate Boolean storage class"; "SQLite does not have a
storage class set aside for storing dates and/or times."

**Type affinity, and a trap for a column literally declared `uuid`.** The affinity rules (same page): "INT"
→ INTEGER; "CHAR"/"CLOB"/"TEXT" → TEXT; "BLOB" or no type → BLOB; "REAL"/"FLOA"/"DOUB" → REAL;
"Otherwise, the affinity is NUMERIC". The strings `uuid` and `guid` match none of rules 1–4, so such a
column gets **NUMERIC affinity**.

**Comparison and ordering** (same page):

> "A TEXT value is less than a BLOB value. **When two TEXT values are compared an appropriate collating
> sequence is used to determine the result.** … **When two BLOB values are compared, the result is
> determined using memcmp().**"

Cross-class order: NULL < INTEGER/REAL < TEXT < BLOB. "Every column of every table has an associated
collating function. If no collating function is explicitly defined, then the collating function defaults
to **BINARY**", and "BLOBs are always compared byte-by-byte using memcmp()". BINARY itself "Compares string
data using memcmp(), regardless of text encoding".

**Ordering consequence for UUIDv7 in SQLite:** both representations preserve v7 time ordering, with one
condition. A 16-byte **big-endian** BLOB sorts by `memcmp` over the RFC 9562 octets — the spec's "opaque
raw bytes" case. Canonical **lower-case hex TEXT** under BINARY also sorts by `memcmp` of the UTF-8 bytes,
which for a fixed-width string with hyphens at fixed positions is order-equivalent. **Mixing upper- and
lower-case hex text breaks it** (BINARY is byte-exact: `'F'` = 0x46 < `'a'` = 0x61), and a **mixed-endian
BLOB** (§4.2) breaks it outright.

**The `uuid` extension exists, generates v4 only, and is not in standard builds.**
<https://www.sqlite.org/src/file/ext/misc/uuid.c> (header dated 2019-10-23) documents `uuid()` ("generate
a version 4 UUID as a string"), `uuid_str(X)` and `uuid_blob(X)` ("convert a UUID X into a 16-byte blob");
outputs are "always well-formed **RFC-4122** UUID strings" in lower-case hex; parsing is liberal like
PostgreSQL's; bad input returns NULL. Three independent confirmations that it is not available by default:

1. The built-in core scalar function list — "The core functions shown below are available by default" —
   **does not include** `uuid()`, `uuid_str()` or `uuid_blob()`; that page instead suggests
   `lower(hex(randomblob(16)))` for generating unique identifiers
   (<https://www.sqlite.org/lang_corefunc.html>).
2. It lives in `ext/misc/`, described as example loadable extensions, and "For security reasons, extension
   loading is turned off by default" (<https://sqlite.org/loadext.html>).
3. Even the `sqlite3` CLI does not bundle it — the CLI's list of built-in extras is UINT, decimal,
   `generate_series()`, `base64()`/`base85()`, and POSIX regex; no UUID functions
   (<https://sqlite.org/cli.html>).

It also predates RFC 9562 (it cites RFC 4122) and has **no UUIDv7 generator**.

### 4.5 How Microsoft.Data.Sqlite stores a `Guid`

**Default: TEXT, canonical hyphenated form.** The mapping table at
<https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types> gives:

| .NET | SQLite | Remarks |
|---|---|---|
| **Guid** | **TEXT** | `00000000-0000-0000-0000-000000000000` |

i.e. a **36-character** string. **Alternative: BLOB.** The same page's "Alternative types" table lists
`Guid → BLOB`, and the mechanism is `SqliteType`:
`command.Parameters.AddWithValue("$x", value).SqliteType = SqliteType.Blob;`
(<https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/parameters>).

**EF Core 10 did not change this.** The EF10 what's-new page contains nothing about Guid or UUID storage;
its SQLite items are `DateOnly.DayNumber` translation, `MAX`/`MIN`/`ORDER BY` on `decimal`, configurable
AUTOINCREMENT, a `LoadExtension` fix, and the date/time UTC changes of §2.10. The
Microsoft.Data.Sqlite types page carries `ms.date: 2019-12-13`.

**EF Core does not flag Guid as a problem type on SQLite.** The limitations page
(<https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations>) **does not mention Guid at
all**; its client-evaluation list is only `DateTimeOffset`, `decimal`, `TimeSpan`, `ulong` (§2.2). So EF
does not consider Guid comparison or ordering to require client evaluation on SQLite.

**Chaining the verified facts — the default TEXT representation does preserve UUIDv7 time ordering:**
the v7 timestamp is in the leading bits (§4.1); the canonical text transcribes those bits left-to-right,
and RFC 9562 §6.11 states the text form is "intended to be lexicographically sortable"; Microsoft.Data.
Sqlite's default is that canonical text as TEXT; TEXT compares under BINARY, i.e. `memcmp` (§4.4).
**Condition:** the case of the hex digits must be consistent across all rows.

**UNVERIFIED, and both matter to the storage spike:**

1. **The byte order Microsoft.Data.Sqlite uses for the alternative `Guid → BLOB` mapping.** Not documented
   on the types page or the parameters page. Historically it writes `Guid.ToByteArray()` — the
   mixed-endian layout — which would **not** be time-ordered under `memcmp`, but this could not be
   confirmed from a primary source. Confirm empirically or from the `Microsoft.Data.Sqlite` source before
   relying on it.
2. **The case (upper vs lower) of hex digits in the default Guid TEXT representation.** The docs' example
   value is all zeros and therefore uninformative. For contrast, `Guid.ToString()` in .NET produces lower
   case and PostgreSQL always outputs lower case (§4.3) — but Microsoft.Data.Sqlite's own choice is not
   stated.

For PostgreSQL there is no equivalent hazard: the storage form of `uuid` is the raw big-endian 16 octets
and `uuid_cmp` is `memcmp` over them (§4.3), so v7 values are time-ordered in `uuid` columns and their
B-tree indexes without any application-side care.

### 4.6 How Npgsql maps `Guid` to `uuid` — and the byte-swap that makes it correct

**Mapping:** PostgreSQL `uuid` reads as .NET `Guid` by default; .NET `Guid` writes to `uuid` with
`NpgsqlDbType.Uuid` (<https://www.npgsql.org/doc/types/basic.html>).

**Wire format is big-endian**, i.e. RFC 9562 network byte order, and Npgsql handles the swap explicitly.
From the current converter source
(<https://github.com/npgsql/npgsql/blob/main/src/Npgsql/Internal/Converters/Primitive/GuidUuidConverter.cs>):

```csharp
public override Guid Read(PgReader reader)
    => new(reader.ReadBytes(16).FirstSpan, bigEndian: true);

public override void Write(PgWriter writer, Guid value)
{
    Span<byte> bytes = stackalloc byte[16];
    value.TryWriteBytes(bytes, bigEndian: true, out _);
    writer.WriteBytes(bytes);
}
```

Fixed 16-byte payload (`BufferRequirements.CreateFixedSize(16 * sizeof(byte))`). Because .NET's `Guid`
stores its first three fields in machine-native order while PostgreSQL stores UUIDs big-endian (§4.2,
§4.3), Npgsql uses the `bigEndian: true` overloads to perform the swap. **The upshot is that the canonical
representation round-trips identically** between .NET and non-.NET PostgreSQL clients, and — critically for
PRD 6 — a UUIDv7 written through Npgsql lands in the `uuid` column with its 48-bit timestamp in the leading
octets, so it sorts in time order (§4.3). The behaviour is read from the code; there are no explanatory
comments in that file. The only endianness-related issue in the driver repo is the 2019 PR
<https://github.com/npgsql/npgsql/pull/2310> ("Remove unsafe code from UuidHandler").

### 4.7 First-class UUIDv7 support, by layer

Summarising the whole question the ticket asks. **The answer differs sharply by layer: .NET has it,
EF Core itself has none, and the Npgsql provider has two forms of it.**

| Layer | First-class UUIDv7 support |
|---|---|
| **.NET 10** | **Yes** — `Guid.CreateVersion7()` / `Guid.CreateVersion7(DateTimeOffset)` and `Guid.Version`, all shipped in **.NET 9** and unchanged in .NET 10 (§4.2). No documented monotonicity within a millisecond. |
| **EF Core 10 (provider-neutral)** | **None.** The EF10 what's-new and breaking-changes pages contain no Guid or UUID item at all (§4.5). EF Core generates `Guid` keys client-side, but the core libraries have no UUIDv7 concept. |
| **`Npgsql` (ADO.NET driver)** | **None, by design** — UUID *generation* is not a driver concern. GitHub searches of `npgsql/npgsql` for `uuidv7` and for `CreateVersion7` both return `total_count: 0`; an org-wide search for `uuidv7` returns 7 items, **all** in `npgsql/efcore.pg` or `npgsql/doc`. What the driver does provide is the correct big-endian mapping of §4.6. |
| **`Npgsql.EntityFrameworkCore.PostgreSQL`** | **Yes — two independent mechanisms** (below). |
| **PostgreSQL 18** | **Yes** — built-in `uuidv7()`, with Method-3 sub-millisecond monotonicity (§4.3). |
| **SQLite / Microsoft.Data.Sqlite / EF SQLite provider** | **None.** No UUID type (§4.4), no v7 generator anywhere (the `ext/misc/uuid.c` extension is v4-only and not in standard builds), and no Guid-related EF10 change (§4.5). |

**(a) Client-side UUIDv7 value generation is the Npgsql provider's default, and has been since 9.0.**

The 9.0 release notes, under "Other new features" (anchor `#uuidv7-guids-are-generated-by-default`):
"When your entity types have a `Guid` key, EF Core by default generates key values for new entities
client-side - in .NET - before inserting those entity types to the database" — and 9.0 generates
"version 7 GUIDs, which is a sequential GUID type that's more appropriate for database indexes"
(<https://www.npgsql.org/efcore/release-notes/9.0.html>).

The modeling doc, verbatim: "By default, for GUID key properties, a GUID is generated client-side by the
EF provider and sent to the database" and "From version 9.0 and onwards, these GUIDs are sequential
(version 7), which are more optimized for database indexes." For **non-key** properties it is opt-in via
`.HasValueGenerator<NpgsqlSequentialGuidValueGenerator>()`
(<https://www.npgsql.org/efcore/modeling/generated-properties.html>).

The implementation on `main` is a one-liner
(<https://github.com/npgsql/efcore.pg/blob/main/src/EFCore.PG/ValueGeneration/NpgsqlSequentialGuidValueGenerator.cs>):

```csharp
public class NpgsqlSequentialGuidValueGenerator : ValueGenerator<Guid>
{
    public override Guid Next(EntityEntry entry) => Guid.CreateVersion7();
    public override bool GeneratesTemporaryValues => false;
}
```

Note the class name still says "SequentialGuid" while the body now delegates straight to
`Guid.CreateVersion7()`. Provenance: PR <https://github.com/npgsql/efcore.pg/pull/3249> ("Add UUID version
7 as the default guid generator"), merged **2024-09-01**, from issue
<https://github.com/npgsql/efcore.pg/issues/2909> ("Switch to UUIDv7 for client-side GUID generation",
opened 2023-10-23). The PR body notes the implementation "is based on .NET 9's `Guid.CreateVersion7`
function, which needed to be ported since EFCore and EFCore.PG target .NET 8" — porting no longer needed
now the provider targets net10.0. Docs change: <https://github.com/npgsql/doc/pull/349>. A follow-up gap
(<https://github.com/npgsql/efcore.pg/issues/3460>, "UUID Generation Still v4 Instead of v7 After Upgrading
to .NET 9 / EF Core 9", 2025-02-12) was fixed by
<https://github.com/npgsql/efcore.pg/pull/3462> ("Generate UUIDv7 values for value-generated strings",
merged 2025-02-14).

**There is no merged opt-out.** PR <https://github.com/npgsql/efcore.pg/pull/3839> ("Add
EnableLegacyUuidBehaviour switch to preserve v4 UUID generation") was opened and **closed unmerged** on
2026-05-19, so there is no first-class flag to revert to v4 client-side generation.

**(b) Database-side `uuidv7()`, new in provider 10.0, gated on PostgreSQL 18.**

Verbatim from <https://www.npgsql.org/efcore/release-notes/10.0.html>: "PostgreSQL 18 also added the
`uuidv7()` built-in function, which allows database generation of UUIDv7 values. **In EFCore.PG 10, if you
configure the provider to target PG 18 (`.UseNpgsql("...", o => o.SetPostgresVersion(18, 0))`), the
provider will also translate `Guid.CreateVersion7()` to that function.**" Tracking issue
<https://github.com/npgsql/efcore.pg/issues/3567>, closed 2025-07-06, milestone 10.0.0.

As a column default, from <https://www.npgsql.org/efcore/modeling/generated-properties.html>: "Starting
with PostgreSQL 18, version 7 GUIDs can be generated, which are optimized for database indexes:
`.HasDefaultValueSql("uuidv7()")`." For earlier PostgreSQL, "you can use an extension such as `pg_uuidv7`
to generate version 7 GUIDs, or generate random version 7 GUIDs instead:
`.HasDefaultValueSql("uuid_generate_v4()")`" — the phrase "random version 7" appears to be a doc typo for
version 4.

`uuidv7()` really is PG 18+: the **PostgreSQL 17** version of the UUID-functions page documents only
`gen_random_uuid`, `uuid_extract_timestamp` and `uuid_extract_version`, with neither `uuidv7()` nor
`uuidv4()` (<https://www.postgresql.org/docs/17/functions-uuid.html>), whereas the current (18) page has
all five (<https://www.postgresql.org/docs/current/functions-uuid.html>).

**Consequences for the shared suite.**

1. **Client-side generation (a) is the only mechanism that can be identical on both providers**, because it
   happens in .NET before `SaveChanges` and involves no SQL. Note that on the SQLite side there is no
   provider default doing this — the Npgsql provider supplies UUIDv7 key generation, the EF SQLite provider
   does not (§4.7 table), so *application-level* generation is the only way to get the same behaviour on
   both.
2. **Database-side generation (b) cannot be shared at all** — `HasDefaultValueSql("uuidv7()")` requires
   PostgreSQL 18 and has no SQLite equivalent, and the `Guid.CreateVersion7()` → `uuidv7()` translation
   only fires when `SetPostgresVersion(18, 0)` is configured.
3. If (b) is used, the Testcontainers image tag must be **PostgreSQL 18 or newer** — and §1.4 notes the
   module no longer supplies a default image, so that tag is an explicit choice.
4. **PostgreSQL's `uuidv7()` and .NET's `Guid.CreateVersion7()` differ in monotonicity guarantee**:
   PostgreSQL implements RFC 9562 Method 3 (sub-millisecond precision in `rand_a`), .NET documents only
   random `rand_a`/`rand_b` (§4.2, §4.3). A test asserting strict ordering of IDs generated within one
   millisecond would pass with database-side generation and be unsound with client-side generation.

---

## 5. Strongly typed IDs in EF Core 10

### 5.1 EF Core 10 adds no dedicated strongly-typed-ID feature

The EF10 what's-new page (<https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew>)
has **no "strongly typed IDs" item**. Two things there are relevant:

1. **Complex types were substantially extended** — the value-object story. Optional (nullable) complex
   properties; `ToJson()` mapping; **struct support** ("Complex types also supports mapping .NET structs
   instead of classes… collections of structs aren't currently supported"); full `ExecuteUpdateAsync`
   support. EF's stated position: "these issues — as well as various others — make complex types the
   better choice for modeling JSON and table splitting, and **users already using owned entity types for
   these are advised to switch to complex types**." Value semantics are the point:
   `customer.BillingAddress = customer.ShippingAddress` throws with owned entities but works with complex
   types, and LINQ compares complex types by content rather than identity.
2. **"AUTOINCREMENT can now be disabled for SQLite and is also supported for properties with value
   converters"** — precisely the strongly-typed-ID plus value-generation pain point (§5.3).

### 5.2 Value converters — the actual mechanism

<https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions> (last updated 2025-10-30):

- A converter is `ModelClrType` → `ProviderClrType` plus the inverse, as **two expression trees**
  ("Expression trees are used so that they can be compiled into the database access delegate for
  efficient conversions").
- Configure with `.HasConversion(v => …, v => …)`, `.HasConversion<TProvider>()`, or an explicit
  `new ValueConverter<TModel, TProvider>(…)`. Bulk-configure by subclassing `ValueConverter<,>` and calling
  `configurationBuilder.Properties<Currency>().HaveConversion<CurrencyConverter>()` in
  `ConfigureConventions`.
- "**A `null` value will never be passed to a value converter.** A null in a database column is always a
  null in the entity instance, and vice-versa." (<https://github.com/dotnet/efcore/issues/13850>)
- Facets: `ConverterMappingHints(size:, unicode:)` apply to the converted provider type; explicit property
  facets override hints.
- The page has a **"Value objects as keys"** section showing exactly the strongly-typed-ID pattern
  (`readonly struct BlogKey`, `HasConversion(new ValueConverter<BlogKey, int>(v => v.Id, v => new
  BlogKey(v)))`), with two caveats printed verbatim:
  - "**Showing this pattern does not mean we recommend it.** Carefully consider whether this level of
    abstraction is helping or hampering your development experience."
  - "**Key properties with conversions can only use generated key values starting with EF Core 7.0.**"
- Mutable model types (e.g. `ICollection<string>`) additionally need a `ValueComparer<T>`; a
  `readonly struct` value object can be snapshotted and compared without one
  (<https://learn.microsoft.com/en-us/ef/core/modeling/value-comparers>).

**The six documented limitations, verbatim, and all still present as of the 2025-10-30 update — EF Core 10
removed none of them:**

1. "As noted above, **`null` cannot be converted**." (#13850)
2. "**It isn't possible to query into value-converted properties**, e.g. reference members on the
   value-converted .NET type in your LINQ queries." (#10434)
3. "There is currently **no way to spread a conversion of one property to multiple columns** or vice-versa."
   (#13947) — hence "Value converters can currently only convert values to and from a single database
   column", and composite value objects must be JSON-serialized into one column. "We plan to allow mapping
   an object to multiple columns in a future version of EF Core."
4. "**Value generation is not supported for most keys mapped through value converters.**" (#11597)
5. "**Value conversions cannot reference the current DbContext instance.**" (#12205)
6. "**Parameters using value-converted types cannot currently be used in raw SQL APIs.**" (#27354)

"Removal of these limitations is being considered for future releases."

Limitation 4 is the one EF10 narrows, and only for SQLite (§5.3). Limitations 2 and 6 are the ones that
constrain a prefixed-typed-ID design: a `ServerId` wrapping a `Guid` cannot have its inner members
referenced in LINQ, and cannot be passed as a parameter to `FromSql`.

### 5.3 Value generation for value-converted keys, per provider

**SQLite** — <https://learn.microsoft.com/en-us/ef/core/providers/sqlite/value-generation> (updated
2026-01-06):

> "By convention, numeric primary key columns that are configured to have their values generated on add are
> set up with SQLite's AUTOINCREMENT feature. **Starting with EF Core 10, SQLite AUTOINCREMENT can also be
> enabled or disabled via configuration.**"

> "By convention, integer primary keys are automatically configured with AUTOINCREMENT when they are not
> part of a composite key and don't have a foreign key on them. However, you may need to **explicitly
> configure a property to use SQLite AUTOINCREMENT when the property has a value conversion from a
> non-integer type**":

```csharp
modelBuilder.Entity<Blog>().Property(b => b.Id).HasConversion<int>().UseAutoincrement();
```

Disabling: `.Metadata.SetValueGenerationStrategy(SqliteValueGenerationStrategy.None)` or
`.ValueGeneratedNever()`. Rationale for disabling: "AUTOINCREMENT imposes extra CPU, memory, disk space,
and disk I/O overhead compared to the default key generation algorithm in SQLite - ROWID. The downside of
`ROWID` is that it reuses values from deleted rows." Caveat on `ValueGeneratedNever()`: "this still won't
disable the default value generation server-side, so non-EF usages could still get a generated value. To
completely disable value generation, change the column type from `INTEGER` to `INT`."

**Cross-provider note.** This entire `SqliteValueGenerationStrategy` / `UseAutoincrement()` surface is
SQLite-only, and PostgreSQL's identity/serial/sequence strategies are equally provider-specific. Combined
with §2.1 (sequences unsupported on SQLite), **no sequence- or identity-based key generation strategy can
be shared** between the two providers. Client-side generation — which is what UUIDv7 IDs are — sidesteps
the whole surface, since the application supplies the key value before `SaveChanges`.

### 5.4 Complex properties: some relevant capabilities are EF Core 11, not 10

From <https://learn.microsoft.com/en-us/ef/core/modeling/complex-types> (updated 2026-08-05):

- "Starting with **EF Core 11**, **keys and indexes can target scalar properties nested inside
  non-collection complex types**" (`HasIndex(c => c.Address.PostCode)`).
- "Starting with **EF Core 11**, you can configure a property nested inside a complex type directly by
  chaining member access" (`.Property(c => c.Address.Line1)`).
- "Starting with **EF Core 11**, complex types and JSON columns can be used on entity types that use TPT
  or TPC."
- `EF.Functions.JsonPathExists` is also EF Core 11.

So in EF Core 10, a strongly typed ID modelled as a **complex property** cannot be a key or be indexed;
the value-converter route of §5.2 is the only one available for keys.

---

## 6. Optimistic concurrency in EF Core 10, per provider

Primary source: <https://learn.microsoft.com/en-us/ef/core/saving/concurrency> (updated 2025-10-30).
**Note:** `/ef/core/modeling/concurrency` **redirects to this same article** — its `canonicalUrl` is
`/ef/core/saving/concurrency`. There is no separate modeling-concurrency page.

### 6.1 Mechanism

> "EF Core implements *optimistic concurrency*… optimistic concurrency takes no locks, but arranges for the
> data modification to fail on save if the data has changed since it was queried."

> "In EF Core, optimistic concurrency is implemented by configuring a property as a *concurrency token*.
> The concurrency token is loaded and tracked when an entity is queried… Then, when an update or delete
> operation is performed during `SaveChanges()`, the value of the concurrency token on the database is
> compared against the original value read by EF Core."

Generated SQL takes the shape
`UPDATE [People] SET [FirstName] = @p0 WHERE [PersonId] = @p1 AND [Version] = @p2;` — "if a concurrent
update occurred, the UPDATE fails to find any matching rows and reports that zero were affected. As a
result, EF Core's `SaveChanges()` throws a `DbUpdateConcurrencyException`." See §2.8 for why **inserts**
are different.

### 6.2 Database-generated tokens: PostgreSQL has one, SQLite has none

`[Timestamp]` / `.Property(p => p.Version).IsRowVersion()` maps to a SQL Server `rowversion` column:
"Since `rowversion` automatically changes when the row is updated, it's very useful as a minimum-effort
concurrency token that protects the entire row." But, verbatim:

> "The `rowversion` type shown above is a **SQL Server-specific feature**; the details on setting up an
> automatically-updating concurrency token differ across databases, and **some databases don't support
> these at all (e.g. SQLite)**. Consult your provider documentation for the precise details."

The SQLite limitations page lists "Database-generated concurrency tokens" as an unsupported modeling
concept and links to this exact section
(<https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations>).

**UNVERIFIED / negative finding:** no official documentation of a trigger-based `rowversion` emulation for
SQLite was found. The docs' only sanctioned answer for SQLite optimistic concurrency is the
application-managed token below; any trigger approach would be unofficial.

For the PostgreSQL side see **§3.10** — `IsRowVersion()` *does* work on Npgsql, but it means "map to the
`xmin` system column" and requires a **`uint`** property, not SQL Server's `byte[]`. That pattern is
documented by Npgsql (<https://www.npgsql.org/efcore/modeling/concurrency.html>), not by Microsoft, and it
has no SQLite counterpart — so a shared model cannot use one `IsRowVersion()` configuration for both
providers.

### 6.3 Application-managed tokens: the one path that works on both

Verbatim from <https://learn.microsoft.com/en-us/ef/core/saving/concurrency>:

> "Rather than have the database manage the concurrency token automatically, you can manage it in
> application code. This **allows using optimistic concurrency on databases - like SQLite - where no
> native automatically-updating type exists.** But even on SQL Server, an application-managed concurrency
> token can provide fine-grained control on exactly which column changes cause the token to be
> regenerated."

```csharp
[ConcurrencyCheck] public Guid Version { get; set; }              // data annotation
modelBuilder.Entity<Person>().Property(p => p.Version).IsConcurrencyToken();   // fluent
```

Required discipline — "Since this property isn't database-generated, **you must assign it in application
whenever persisting changes**":

```csharp
person.FirstName = "Paul";
person.Version = Guid.NewGuid();
await context.SaveChangesAsync();
```

> "If you want a new GUID value to always be assigned, you can do this via a `SaveChanges` interceptor.
> However, one advantage of manually managing the concurrency token is that you can control precisely when
> it gets regenerated, to avoid needless concurrency conflicts."
> (interceptors: <https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors#savechanges-interception>)

Because `Guid` maps to **TEXT** on SQLite (§4.5) and to native `uuid` on PostgreSQL (§4.3), a `Guid`
concurrency token works on both providers — but the stored representation and column type differ, so the
generated DDL is not identical even though the behaviour is.

Related and cross-provider-relevant: a SQL Server `rowversion` can be surfaced as a friendlier `ulong` via
`.IsRowVersion().HasConversion<byte[]>()` (<https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions>)
— but `ulong` is one of the four SQLite-unsupported query types (§2.2) *and* `rowversion` does not exist on
SQLite, so this pattern is unavailable here.

### 6.4 Resolving conflicts

Documented procedure: catch `DbUpdateConcurrencyException` in a retry loop; iterate `ex.Entries`; read
`entry.CurrentValues` (proposed), `entry.OriginalValues`, and `await entry.GetDatabaseValuesAsync()`
(database); merge; then `entry.OriginalValues.SetValues(databaseValues)` to "Refresh original values to
bypass next concurrency check"; retry. The three value sets are named "Current values", "Original values",
"Database values". Full sample:
<https://github.com/dotnet/EntityFramework.Docs/tree/main/samples/core/Saving/Concurrency/>.

### 6.5 The isolation-level alternative, and its provider divergence

"Optimistic concurrency via concurrency tokens isn't the only way…" — repeatable-read or serializable
isolation is the alternative, implemented in two different styles: shared locks and blocking (SQL Server
repeatable read) versus a serialization error on write, which is "implemented by the SQL Server snapshot
isolation level, **as well as by the PostgreSQL repeatable reads isolation level**". Drawbacks noted:
locking hurts concurrency, and "this approach requires a transaction to span all the operations". Also,
"Manually controlling transactions… is incompatible with implicitly invoked retrying execution strategies"
(<https://learn.microsoft.com/en-us/ef/core/saving/transactions>,
<https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency#execution-strategies-and-transactions>).

This is directly relevant to PRD 21 (only one conflicting mutating lifecycle operation per server at a
time): the isolation-level route behaves differently on the two providers, whereas an application-managed
concurrency token behaves identically.

---

## 7. Everything that could not be verified

Collected for the record. Each is flagged in place above.

1. **Byte order of Microsoft.Data.Sqlite's alternative `Guid → BLOB` mapping** (mixed-endian vs
   big-endian). Not documented on
   <https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types> or the parameters page. Bears
   directly on the storage spike.
2. **Case (upper vs lower) of hex digits in Microsoft.Data.Sqlite's default Guid TEXT form.** The docs'
   example value is all zeros. Bears on whether BINARY-collation ordering is stable.
3. **PostgreSQL's docs do not state the `uuid` type's 16-byte storage size** — only "128-bit quantity".
   The 16-byte figure comes from `UUID_LEN` in
   <https://github.com/postgres/postgres/blob/master/src/include/utils/uuid.h>.
4. **Licence of `SQLitePCLRaw.bundle_e_sqlite3` / `SQLitePCLRaw.core` 2.1.12**, the transitive native
   dependency of `Microsoft.EntityFrameworkCore.Sqlite`. Not covered by the efcore MIT LICENSE.
5. **No official trigger-based `rowversion` emulation for SQLite** exists in the documentation. Negative
   finding, not a gap in the research.
6. **EF Core's own pages disagree about `decimal` ordering on SQLite** (§2.2): the EF10 release note and
   PR #35606 say `MAX`/`MIN`/`ORDER BY` are now translated; the limitations page, updated later, still
   states the blanket client-evaluation rule. No source reconciles them.
7. **`Microsoft.Data.Sqlite` savepoint support** is not addressed either way on the EF transactions page.
8. **The EF "Supported .NET implementations" table has not been refreshed for EF Core 10** — it stops at
   9.0 (<https://learn.microsoft.com/en-us/ef/core/miscellaneous/platforms>), so it cannot be cited for
   EF10's TFM.
9. **EF and .NET support-end dates for the 10.0 line differ by four days** — 2028-11-10 per the EF docs,
   2028-11-14 per the .NET support policy page.
10. **No TUnit integration package for Testcontainers was found** (only `Testcontainers.Xunit` /
    `Testcontainers.XunitV3`). PRD 5 selects TUnit; absence was not exhaustively proven.
11. **No official Podman / rootless support statement** in the Testcontainers docs, and **no Testcontainers
    Cloud documentation page** — only the source artefacts cited in §1.4.
12. **No explicit "net10.0 is supported" prose in the Npgsql docs or release notes** (§1.3). The 10.0 notes
    only record that .NET 6 was dropped. The NuGet catalog dependency groups are the only authoritative
    TFM evidence.
13. **Publish date of `Npgsql.EntityFrameworkCore.PostgreSQL` 11.0.0-preview.6.** The package exists on
    NuGet, but the `registration5-semver1` leaf returns 404 for semver2 prereleases.
14. **Whether and when `UseXminAsConcurrencyToken` was removed or obsoleted** (§3.10). The current Npgsql
    concurrency page simply does not mention it.
15. **Npgsql's behaviour when a PostgreSQL `numeric` value exceeds .NET `decimal`'s range** (§3.7).
    `https://www.npgsql.org/doc/types/numeric.html` returns 404 and no other doc statement was found.
16. **Broken Npgsql doc index URLs.** Both `https://www.npgsql.org/doc/release-notes/` and
    `https://www.npgsql.org/efcore/release-notes/` return **HTTP 404**; only the per-version pages resolve.
    Recorded so a future session does not mistake this for a fetch failure.
17. **The `efcore.pg` GitHub releases page is an incomplete record** — no entry for 10.0.1, 10.0.3, or any
    11.0.0-preview, though all exist on NuGet. Use NuGet as the authority for that repo's versions.

---

## 8. Corrections to plausible assumptions

Four things that a reasonable prior belief would get wrong, recorded so they are not re-derived:

1. **There is no `new Guid(byte[], bool bigEndian)` overload.** The endianness-aware constructor is
   `Guid(ReadOnlySpan<byte>, bool bigEndian)`, added in **.NET 8** alongside `ToByteArray(bool)` (§4.2).
2. **`uuid_extract_timestamp()` and `uuid_extract_version()` arrived in PostgreSQL 17, not 18.** PG 18
   added `uuidv7()` and the `uuidv4()` alias (§4.3).
3. **SQLite's `ext/misc/uuid.c` predates RFC 9562 and generates v4 only** — it cites RFC 4122, has no v7
   generator, and is not compiled into the standard library or even the `sqlite3` CLI (§4.4).
4. **Npgsql and the Npgsql EF provider are licensed under the PostgreSQL Licence, not MIT** — the only
   non-MIT licence in this stack (§1.3).
