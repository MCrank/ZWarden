# 2. Test stack: TUnit, TUnit.Mocks, bUnit in reflection mode, tiered by assembly

PRD 5's named stack survives verification, at exact pins: **TUnit 1.66.27**, **TUnit.Mocks
1.66.27**, **bunit 2.10.3**, **Testcontainers / Testcontainers.PostgreSql 4.15.0**, **Npgsql
10.0.3**, on **.NET 10** (SDK 10.0.401 in CI, `global.json` pinned to 10.0.302 with
`rollForward: latestFeature`; runtime 10.0.12). bUnit runs **only** in TUnit's reflection mode,
in **its own assembly**. CI splits into two tiers **by assembly, never by category filter**, and
**`--minimum-expected-tests` is mandatory**, not hygiene.

- Status: accepted
- Decided in: [#2](https://github.com/MCrank/ZWarden/issues/2) (documentary), [#3](https://github.com/MCrank/ZWarden/issues/3) (platform), [#8](https://github.com/MCrank/ZWarden/issues/8) (the spike that measured all of it in CI)
- Satisfies: PRD 62's verification gate for the testing technologies; supersedes parts of PRD 14 and PRD 56 as noted below

## Context

PRD 5 named TUnit, TUnit Mocks, bUnit and Testcontainers without evidence that they compose.
Documentary review ([#2](https://github.com/MCrank/ZWarden/issues/2)) found a stated
incompatibility — bUnit's own documentation says *"TUnit does not work with razor files"*,
because the Razor SDK and TUnit both use source generators and generators cannot see each
other's output. A throwaway spike
([`spike/test-harness`](https://github.com/MCrank/ZWarden/tree/spike/test-harness), `984af3d`)
then ran the whole stack in CI on `ubuntu-latest`: **25 tests, 0 failures**, across four jobs,
with the offline tier's claim proved inside a network namespace rather than assumed.

Nine things broke on the way, and one of them is why this ADR exists at all.

### The silent-failure hazard, which is the load-bearing finding

Forgetting bUnit's reflection-mode switch does **not** produce an error. It produces a clean
build, a run that prints `Passed!`, exit code 0 — and the `.razor` file's tests simply gone.
Reproduced in CI: three tests exist, two ran, nothing anywhere said so.

A suite that quietly stops testing is the worst failure mode a harness has, and it has **no
compile-time enforcement**. The only guard is MTP's discovery policy:

```
Test run summary: Minimum expected tests policy violation, tests ran 2, minimum expected 3
exit code with --minimum-expected-tests 3: 9
```

So reflection mode is a **correctness requirement**, not a performance choice, and
`--minimum-expected-tests` is the single highest-value line in Feature 0's CI. The spike left a
standing job (`tier1-silent-drop-guard`) that strips the attribute and asserts the guard fires;
that job, or its equivalent, belongs in the real pipeline.

## Alternatives considered

- **Drop bUnit and test Razor components through Playwright only.** Rejected: component tests
  are cheap and fast where end-to-end tests are neither, and PRD 5 names both. The cost of
  keeping bUnit is one differently-configured assembly, which is bounded and now measured.
- **Run the whole solution in reflection mode** to avoid the split. Rejected: it would discard
  TUnit's compile-time discovery and its AOT story for every assembly to accommodate one.
- **Replace TUnit.Mocks with FakeItEasy 9.0.1** (80.4M downloads against TUnit.Mocks' ~134k, and
  it declares `net10.0`). Not taken, but not closed — see Consequences.
- **Tier the two CI runs by `[Category]` filter across one solution-wide pass.** Rejected on
  measurement, not taste: an assembly emptied by a category filter fails the entire run.
  `Spike.Integration.Tests` is 100% `[Category("Networked")]`, and the filtered offline pass gave
  `Zero tests ran` and a non-success exit code, because MTP's `--zero-tests-policy` defaults to
  failing a run that discovered nothing. **Tiers split by assembly; categories slice within
  one.** This changes PRD 14's `tests/` layout, which has no offline/networked split in it.

## What the harness actually requires, measured

These are conditions, not preferences. Each one was a red build before it was a line of config.

1. **`global.json` needs the MTP runner or `dotnet test` is hard-blocked on SDK 10.**
   `"test": { "runner": "Microsoft.Testing.Platform" }` — a `global.json` stanza, not a project
   property. `TUnit.Engine.props` already sets `TestingPlatformDotnetTestSupport=true` and that
   is **not sufficient**: it selects the legacy VSTest bridge, which MTP 2.x refuses on SDK 10.
   In MTP mode the CLI grammar also changes (`--solution`, `--project`, `--test-modules`), so
   every CI script must use the new argument forms.
2. **`TreatWarningsAsErrors` + `AnalysisMode` makes `Method_does_thing` a CA1707 build error.**
   Eight of the spike's first twenty-two build errors were this. PRD 56 and the conventional test
   naming convention are mutually exclusive unless `.editorconfig` carves CA1707 (and CA1515,
   which wants test classes `internal`) out for `tests/**`. **Feature 0 must choose explicitly**
   — the carve-out, or a different naming convention. The spike's green configuration used the
   carve-out; this ADR records the constraint, not the choice.
3. **`TreatWarningsAsErrors` breaks on far more than NuGet advisories.**
   [#3](https://github.com/MCrank/ZWarden/issues/3) established that a new transitive advisory
   turns the build red. The spike generalises it: *any* upstream `[Obsolete]` does too (hit for
   real via `PostgreSqlBuilder()`), and third-party analyzer authors — TUnit ships its own suite
   — can break the build on a package bump. `NuGetAudit=false` does not touch either case. CI
   policy needs a stated position on `WarningsNotAsErrors`/`NoWarn`.
4. **Parallelism is unbounded by default and is not thread-per-test.** Measured: 16 concurrent
   tests on a 4-core runner, on 5 threads. Nothing throttles by core count, so every
   container-backed test starts at once unless constrained with `[NotInParallel]`,
   `[ParallelLimiter<T>]` or `--maximum-parallel-tests`. A throttle policy must be chosen
   **before** PRD 21's per-Server locking tests or any Testcontainers suite lands; it is not
   chosen here.
5. **PRD 14's `ZWarden.sln` is already wrong.** `dotnet new sln` on SDK 10 emits `.slnx`, and
   `dotnet restore ZWarden.sln` on the result fails with `MSB1009`. The XML format worked
   throughout the spike, including `dotnet test --solution`. Feature 0 decides the filename; what
   is settled is that the PRD's literal string is not what the SDK produces.
6. **Test projects do not inherit the platform's C# version story.**
   [#3](https://github.com/MCrank/ZWarden/issues/3) concluded that `net10.0` resolves to C# 14
   with no property set, and warned that setting `LangVersion=latest` makes builds
   machine-dependent. That holds for production projects. It does **not** hold for test projects:
   `TUnit.Engine.props` contains `<LangVersion Condition="'$(LangVersion)' == ''">latest</LangVersion>`,
   deliberately in `.props` so the SDK default cannot win. TUnit.Mocks' C# 14 requirement is
   therefore satisfied through exactly the mechanism the research warned about. Setting
   `<LangVersion>14.0</LangVersion>` explicitly in test projects beats it, if determinism is
   wanted.
7. **The reflection-mode assembly costs four things**, all small and all now known:
   `<EnableTUnitSourceGeneration>false</EnableTUnitSourceGeneration>` in the `.csproj` (build
   side) **and** `[assembly: TUnit.Core.ReflectionMode]` in a `.cs` file (the runtime switch —
   the property alone does nothing at run time; prefer the attribute over
   `TUNIT_EXECUTION_MODE`/`--reflection` because it is version-controlled); the project must use
   `Microsoft.NET.Sdk.Razor`; a hand-maintained `--minimum-expected-tests` floor; and loss of
   compile-time discovery and AOT **for that assembly only**. Everything else behaved identically
   in both modes.
8. **Any Razor-bearing project needs an explicit `<FrameworkReference Include="Microsoft.AspNetCore.App" />`.**
   `Microsoft.NET.Sdk.Razor` does not imply it, and the failure (`CS0234` on
   `Microsoft.AspNetCore`) is opaque until you know.

Also adopted from the spike because it works and is cheap: central package management with
`CentralPackageTransitivePinningEnabled`, `RestorePackagesWithLockFile` plus
`dotnet restore --locked-mode` (which satisfies PRD 55's dependency-locking requirement almost
for free), a `nuget.config` with `<clear />` before nuget.org, and `DOTNET_CLI_TELEMETRY_OPTOUT=1`
as policy — `Microsoft.Testing.Extensions.Telemetry` 2.4.0 ships inside TUnit's closure.

## Versions, as resolved

Direct references: `TUnit` 1.66.27, `TUnit.Mocks` 1.66.27, `bunit` 2.10.3,
`Testcontainers.PostgreSql` 4.15.0, `Npgsql` 10.0.3. Notable transitives, recorded because
TUnit's own graph is internally inconsistent: `Microsoft.Testing.Platform` **2.4.0**,
`Microsoft.Testing.Extensions.TrxReport` **2.3.3** (TUnit asks for 2.3.3 while its siblings
resolve to 2.4.0), `Microsoft.Testing.Extensions.CodeCoverage` 18.10.0. Restore was
advisory-clean across the whole closure — a snapshot, not a property.

## Consequences

- **Feature 0's `tests/` layout is determined by CI tiering**, not by the layer split PRD 14
  draws. `ZWarden.IntegrationTests` and `ZWarden.EndToEndTests` are the natural tier-2
  assemblies; the reflection-mode bUnit assembly is a third shape that belongs to neither tier by
  layer.
- **The offline tier is genuinely offline.** Restore is the only network-permitted step; build
  and the whole test run happen inside a network namespace with loopback only, with four egress
  probes asserted to fail. (`sudo unshare -n`; the unprivileged `unshare -rn` path does not work
  on `ubuntu-latest`.)
- **TUnit.Mocks is proven for the Feature 0 skeleton, not for the product.** Four API shapes were
  exercised. Property mocking, `out`/`ref`, sequenced returns, callbacks, throwing, generic
  methods and strict mode were not. Two API facts worth knowing: `Returns()` **unwraps the Task**
  (`Task<string>` takes a `string`), and it is a **loose** mock — an unconfigured `Task<string>`
  member returns `string.Empty`, not `null`, and does not throw. The seam is free, so this is
  revisited only when a real mocking need exposes a gap.
- **Three dependencies in this stack are effectively single-maintainer** (TUnit here, plus
  Loretta in ADR 10 and Blazor Blueprint in ADR 3). All three are mitigated identically: pin
  exact versions, keep a seam, keep the fork option open.
- **Untouched, and named so nobody assumes otherwise:** Playwright and `WebApplicationFactory`,
  both named in PRD 5, were not exercised at all. Nor were Windows/macOS legs, Release
  configuration, coverage collection, or real SQLite.
- Failure reporting is good enough to rely on: `dotnet test` exits **2** on test failure and
  **9** on a discovery-policy violation, both propagate through MSBuild to a red job, TRX
  distinguishes `Failed` from `NotExecuted`, and TUnit writes a GitHub step summary with no
  configuration (but `$GITHUB_STEP_SUMMARY` is a fresh file per step, so it must be read in the
  same step that wrote it).
