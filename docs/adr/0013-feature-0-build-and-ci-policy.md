# 13. Feature 0 build-and-CI policy: warnings-as-errors, and what a NuGet advisory does

Feature 0 runs `TreatWarningsAsErrors=true` with `AnalysisMode=Recommended`, but a **newly-published NuGet advisory does not break `main`** — it is a visible warning on every build and a red, issue-filing failure on a **scheduled** job, with the F40 gate as the hard stop. Test naming keeps the underscore convention through a **CA1707/CA1515 carve-out scoped to `tests/**`**. Dependencies are locked and centrally pinned.

- Status: accepted
- Decided in: [#14](https://github.com/MCrank/ZWarden/issues/14) (the Feature 0 mini-plan grilling); constraints measured in [#3](https://github.com/MCrank/ZWarden/issues/3) and the [#8](https://github.com/MCrank/ZWarden/issues/8) spike (recorded in ADR [0002](0002-test-stack-and-ci-tiering.md))
- Bears on: PRD 54 (CI stability), PRD 55 (supply chain), PRD 56 (warnings-as-errors); satisfies the four analyzer/CI decisions the technology-baseline ADRs deliberately left open

## Context

PRD 56 reads as one switch. Research proved it is four decisions, and leaving any of them implicit produces either a fragile build or a false sense of safety:

1. **A transitive advisory turns an unrelated build red with no code change.** On `net10.0`, `NuGetAudit` defaults on with `NuGetAuditMode=all`/`NuGetAuditLevel=low`, and .NET 10 newly audits *transitive* packages; under warnings-as-errors those NU19xx warnings are errors. The stock build already failed on `NU1903` from `SQLitePCLRaw.lib.e_sqlite3`, a transitive of the SQLite provider this product needs. That is simultaneously PRD 55 working as intended and a PRD 54 stability problem.
2. **It generalises beyond advisories.** Any upstream `[Obsolete]` (hit for real via `PostgreSqlBuilder()`) and any third-party analyzer bump (TUnit ships its own suite) can turn the build red on a package change. `NuGetAudit=false` touches neither.
3. **Test naming collides with analysis.** `TreatWarningsAsErrors` + `AnalysisMode` makes `Method_does_thing` a **CA1707** build error and wants test classes `internal` (**CA1515**).
4. **`AnalysisMode=All` is a cliff.** Template-shaped code yields 68 CA warnings under it; the stock template *does* build clean under warnings-as-errors with the SDK-default (`Recommended`-level) analyzer set.

A future reader will find "warnings-as-errors, but advisories are only warnings" surprising, so it is recorded here rather than left as prose in the mini-plan.

## Decision

- **`TreatWarningsAsErrors=true`**, `EnableNETAnalyzers=true`, `EnforceCodeStyleInBuild=true`, **`AnalysisMode=Recommended`**. `AnalysisMode=All` is deferred behind its own severity-policy slice.
- **NuGet-advisory two-tier handling.** Audit codes `NU1901`–`NU1905` are demoted via `WarningsNotAsErrors` so a live advisory is a **warning** on PR/`main` — visible, non-blocking. A **scheduled** CI job re-promotes them to errors, fails loudly, and files a tracking issue. **Any open advisory blocks release at the F40 gate.** Rationale: an advisory published overnight must not halt all unrelated work, but must never be silent and must never ship.
- **Third-party `[Obsolete]` / analyzer breakage** is handled by *targeted, per-diagnostic* `NoWarn`/`WarningsNotAsErrors` with a tracking comment — never a global mute.
- **Test-naming carve-out.** `.editorconfig` disables CA1707 and CA1515 for `tests/**` only; production code keeps both. Underscore test names and `internal` test classes are legal in `tests/**` and nowhere else.
- **Dependency locking.** Central package management with `CentralPackageTransitivePinningEnabled`, `RestorePackagesWithLockFile`, and CI `restore --locked-mode` (satisfies PRD 55's dependency-locking requirement); `nuget.config` clears sources before nuget.org; `DOTNET_CLI_TELEMETRY_OPTOUT=1` is repo policy.

## Alternatives considered

- **Break `main` on every advisory.** More conservative — nothing with a known advisory ever merges. Rejected as the default: it periodically halts all work on an event no PR caused, and the scheduled job + F40 gate already guarantee an advisory is neither silent nor shippable. Left available to teams that want it by simply not demoting the NU19xx codes.
- **`NuGetAudit=false`.** Rejected: discards the PRD 55 signal entirely and does not even fix the `[Obsolete]`/analyzer cases.
- **Change the test-naming convention** instead of carving out CA1707. Rejected: the underscore convention is the readable TDD standard; scoping the carve-out to `tests/**` costs nothing in production code.
- **Start at `AnalysisMode=All`.** Rejected for F0: a 68-warning cliff needs a deliberate severity policy that is its own unit of work, not a Feature 0 side effect.

## Consequences

- A live advisory is visible on every build but does not block unrelated work; the scheduled job is the thing that must be watched, and the F40 gate is the thing that must be clean.
- The `tests/**` carve-out means analysis is genuinely weaker in test assemblies — accepted, and bounded to two rules.
- Tightening to `AnalysisMode=All` remains a future, deliberate step with its own `.editorconfig` severity policy.
- This ADR settles the analyzer/CI half of Feature 0; the test-stack and CI-tiering half lives in ADR 0002, and the two are meant to be read together.
