# .NET 10 Platform and Framework Baseline — Verification Record

**Resolves:** [MCrank/ZWarden#3](https://github.com/MCrank/ZWarden/issues/3) (wayfinder research; parent [#1](https://github.com/MCrank/ZWarden/issues/1))
**Satisfies:** PRD 62 (Pre-Implementation Verification Gate), platform layer only
**Verification date:** 2026-09-10
**Scope:** .NET 10 SDK, C# language version, ASP.NET Core 10, Blazor Web App + render modes, SignalR, ASP.NET Core Identity, passkey/WebAuthn in Identity, OpenTelemetry for .NET, plus PRD 56 project-configuration constraints.

> **This is a facts-only record.** It contains no recommendation about what ZWarden should choose. Every claim carries a source URL. Facts that could not be verified against a primary source are collected under [Could Not Verify](#could-not-verify).
>
> Version numbers move monthly. Everything here is a snapshot as of **2026-09-10** and must be re-verified if Feature 0 locks versions materially later.

**Evidence classes used below:**

- **DOC** — official documentation (Microsoft Learn, dotnet.microsoft.com, opentelemetry.io).
- **REG** — machine-readable registry (`dotnet/core` `releases.json`, NuGet flat-container / registration API, npm registry).
- **SRC** — official source repositories (`dotnet/aspnetcore`, `dotnet/sdk`, `dotnet/roslyn`, `open-telemetry/*`).
- **LOCAL** — measured directly on this machine against an installed .NET 10 SDK. Local measurements were taken on **SDK 10.0.302** with the **10.0.10** targeting pack, not the current 10.0.401 / 10.0.12. Flagged inline wherever it matters.

---

## 0. Executive fact sheet

| Item | Verified value | Class |
|---|---|---|
| .NET 10 GA | 2025-11-11 (release 10.0.0, SDK 10.0.100) | REG |
| Release type / support | **LTS**, `support-phase: active`, EOL **2028-11-14** | REG, DOC |
| Current patch | **10.0.12**, released **2026-09-08** (security) | REG |
| Current runtime / ASP.NET Core runtime | **10.0.12** / **10.0.12** | REG |
| Current SDK | **10.0.401**; 10.0.112 also serviced in the same release | REG |
| Latest stable C# for this SDK | **C# 14.0**; `LangVersion` default for `net10.0` is 14.0 | REG, DOC, LOCAL |
| ASP.NET Core / Blazor / Identity / SignalR packages | **10.0.12** (single unified train) | REG |
| OpenTelemetry .NET core | **1.18.0** stable, released 2026-08-21, targets `net10.0` | REG, SRC |
| Passkeys in ASP.NET Core Identity | **Shipped stable in .NET 10**, in the shared framework, scaffolded by the Individual Accounts template | SRC, LOCAL |
| Next release train | .NET 11 is at **11.0.0-rc.1** / SDK 11.0.100-rc.1.26425.128, STS, `go-live` | REG |

---

## 1. .NET 10 SDK

### 1.1 Release status and support lifecycle

| Fact | Value | Source |
|---|---|---|
| GA date | 2025-11-11 (release `10.0.0`, SDK `10.0.100`) | [releases.json](https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/releases.json), [10.0 release notes](https://github.com/dotnet/core/tree/main/release-notes/10.0) |
| Release type | `"release-type": "lts"` | [releases.json](https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/releases.json) |
| Support phase | `"support-phase": "active"` | [releases.json](https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/releases.json) |
| End of support | `"eol-date": "2028-11-14"` — "November 14, 2028" | [releases.json](https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/releases.json), [support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) |
| LTS duration | "LTS releases are supported for three years after the initial release." | [support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) |
| STS duration | "STS releases are supported for one year after a subsequent release… so the support period for STS is two years." | [support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) |

Learn states the LTS rule slightly differently: "supported for a minimum of three years, or one year after the next LTS release ships if that date is later" ([releases-and-support](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support)).

Servicing cadence: updates ship "almost every month"; security fixes release on patch Tuesday, "always the second Tuesday of the month." A servicing update is supported "until the next servicing update is released." ([releases-and-support](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support))

**Full support matrix as of today** ([releases-index.json](https://raw.githubusercontent.com/dotnet/core/main/release-notes/releases-index.json)):

| Channel | Latest | Date | SDK | Type | Phase | EOL |
|---|---|---|---|---|---|---|
| 11.0 | 11.0.0-rc.1 | 2026-09-08 | 11.0.100-rc.1.26425.128 | sts | go-live | — |
| **10.0** | **10.0.12** | **2026-09-08** | **10.0.401** | **lts** | **active** | **2028-11-14** |
| 9.0 | 9.0.20 | 2026-09-08 | 9.0.318 | sts | maintenance | 2026-11-10 |
| 8.0 | 8.0.31 | 2026-09-08 | 8.0.425 | lts | maintenance | 2026-11-10 |

.NET 10 is the only channel in `active` support. .NET 8 and .NET 9 both reach EOL on 2026-11-10.

### 1.2 Current patch (as of 2026-09-10)

From [releases.json](https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/releases.json) and the [10.0.12 release notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.12/10.0.12.md):

| Component | Version |
|---|---|
| Latest release (patch) | **10.0.12** |
| Release date | **2026-09-08** |
| .NET Runtime | **10.0.12** |
| ASP.NET Core Runtime | **10.0.12** |
| Latest SDK | **10.0.401** |
| Other SDK serviced in 10.0.12 | **10.0.112** |
| Visual Studio versions for 10.0.401 | `"vs-version": "18.9.3, 18.10.0"` |
| C# version, both SDKs | `"csharp-version": "14.0"` |

10.0.12 is a **security release** fixing CVE-2026-69439, CVE-2026-71328, CVE-2026-69522, CVE-2026-69304, CVE-2026-58649, CVE-2026-69806 ([releases.json](https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/releases.json), [10.0.12.md](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.12/10.0.12.md)).

**Patch timeline** ([10.0 release notes](https://github.com/dotnet/core/tree/main/release-notes/10.0), verified from `releases.json`):

| Date | Release | SDK | Security |
|---|---|---|---|
| 2025-11-11 | 10.0.0 (GA) | 10.0.100 | no |
| 2025-12-09 | 10.0.1 | 10.0.101 | no |
| 2026-01-13 | 10.0.2 | 10.0.102 | no |
| 2026-02-10 | 10.0.3 | 10.0.103 | yes |
| 2026-03-10 | 10.0.4 | 10.0.200 | yes |
| 2026-03-12 | 10.0.5 | 10.0.201 | no |
| 2026-04-14 | 10.0.6 | 10.0.202 | yes |
| 2026-04-21 | 10.0.7 | 10.0.203 | yes |
| 2026-05-12 | 10.0.8 | 10.0.300 | yes |
| 2026-06-09 | 10.0.9 | 10.0.301 | yes |
| 2026-07-14 | 10.0.10 | 10.0.302 | yes |
| 2026-08-11 | 10.0.11 | 10.0.400 | yes |
| **2026-09-08** | **10.0.12** | **10.0.401** | **yes** |

Ten of the thirteen .NET 10 servicing releases to date have been security releases.

### 1.3 SDK feature bands

Feature bands are "the hundreds groups in the third section of the version number." Installing `10.0.101` removes `10.0.100`; installing `10.0.200` does **not** remove `10.0.101` — bands install side by side ([releases-and-support](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support)).

Bands that have shipped for .NET 10 ([download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)):

- **10.0.1xx** — 10.0.100 … 10.0.112
- **10.0.2xx** — 10.0.200 … 10.0.204
- **10.0.3xx** — 10.0.300 … 10.0.303
- **10.0.4xx** — 10.0.400, **10.0.401**

The `sdks` array of the 10.0.12 entry contains exactly two entries — **10.0.401** and **10.0.112** — i.e. the 2026-09-08 security release serviced only the **10.0.4xx** (latest) and **10.0.1xx** (GA) bands ([releases.json](https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/releases.json)). Bands 2xx and 3xx received no update in this release.

The download page reports "C# 14.0, F# 10.0, and Visual Basic 17.13" for .NET 10 SDKs.

**New in .NET 10:** setting the `CheckSdkVulnerabilities` MSBuild property to `true` emits warning **NETSDK1239** when the resolved SDK is end of life ([releases-and-support](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support)).

### 1.4 `global.json` — schema and `rollForward`

All facts in this subsection: [global.json overview](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json).

```json
{
  "sdk": {
    "version": "10.0.401",
    "allowPrerelease": false,
    "rollForward": "latestPatch",
    "paths": [ ".dotnet", "$host$" ],
    "errorMessage": "The required .NET SDK wasn't found."
  },
  "msbuild-sdks": { },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

| Property | Documented behavior |
|---|---|
| `sdk.version` | "Requires the full version number, such as 10.0.100. Doesn't support version numbers like 10, 10.0, or 10.0.x. Doesn't have wildcard support. Doesn't support version ranges." An invalid value yields *"Version '10.0' is not valid for the 'sdk/version' value."* |
| `sdk.allowPrerelease` | Since .NET Core 3.0 SDK. "Indicates whether the SDK resolver should consider prerelease versions." Default when unset: **`true` when not running from Visual Studio**; inside VS it follows the VS prerelease status (`true` for a Preview VS or with *Tools > Options > Environment > Preview Features > Use previews of the .NET SDK*, else `false`). |
| `sdk.paths` | **New in .NET 10 SDK.** "Specifies the locations that should be considered when searching for a compatible .NET SDK." Absolute or relative to the `global.json`; `$host$` is "the location corresponding to the running `dotnet` executable"; searched in declared order, first match wins. Caveat: "only works when using commands that engage the .NET SDK, such as `dotnet run`. It does NOT affect… running the native apphost launcher (`app.exe`), running with `dotnet app.dll`, or running with `dotnet exec app.dll`." |
| `sdk.errorMessage` | **New in .NET 10 SDK.** "Specifies a custom error message displayed when the SDK resolver can't find a compatible .NET SDK." |
| `msbuild-sdks` | "Lets you control the project SDK version in one place rather than in each individual project." |
| `test.runner` | **New in .NET 10 SDK.** "The test runner to discover/run tests with", e.g. `"Microsoft.Testing.Platform"`. |

Comments are permitted: "Comments in *global.json* files are supported using JavaScript or C# style comments." `rollForward` requires a `version` "unless you're setting it to `latestMajor`."

**`rollForward` values — verbatim documented semantics.** Version shape is `x.y.znn` (major.minor.featureband+patch).

| Value | Documented behavior |
|---|---|
| `patch` | "Uses the specified version. If not found, rolls forward to the latest patch level. If not found, fails. This value is the legacy behavior from the earlier versions of the SDK." |
| `feature` | "Uses the latest patch level for the specified major, minor, and feature band. If not found, rolls forward to the next higher feature band within the same major/minor and uses the latest patch level for that feature band. If not found, fails." |
| `minor` | As `feature`, then "rolls forward to the next higher minor and feature band within the same major and uses the latest patch level for that feature band. If not found, fails." |
| `major` | As `minor`, then "rolls forward to the next higher major, minor, and feature band and uses the latest patch level for that feature band. If not found, fails." |
| `latestPatch` | "Uses the latest installed patch level that matches the requested major, minor, and feature band with a patch level that's greater than or equal to the specified value. If not found, fails." |
| `latestFeature` | "Uses the highest installed feature band and patch level that matches the requested major and minor with a feature band and patch level that's greater than or equal to the specified value. If not found, fails." |
| `latestMinor` | "Uses the highest installed minor, feature band, and patch level that matches the requested major with a minor, feature band, and patch level that's greater than or equal to the specified value. If not found, fails." |
| `latestMajor` | "Uses the highest installed .NET SDK with a version that's greater than or equal to the specified value. If not found, fail." |
| `disable` | "Doesn't roll forward; an exact match is required." Doc footnote: "When you use package lock files, set `rollForward` to `disable` so the SDK version and dependency graph stay in lockstep." |

**Default when `rollForward` is omitted** — three documented cases:

1. No `global.json` found → highest installed SDK is used, "equivalent to setting `rollForward` to `latestMajor`".
2. `global.json` found without an SDK version (with or without `allowPrerelease`) → highest installed SDK, "equivalent to setting `rollForward` to `latestMajor`".
3. **`global.json` found with an SDK version and no `rollForward` → "it uses `patch` as the default `rollForward` policy."**

Two independent components search for `global.json`, both walking up ancestor directories: the **.NET SDK muxer** (starts at the current working directory) and the **.NET MSBuild project SDK resolver** (starts at the solution file's directory, else the project file's directory, else cwd).

Generation via CLI: `dotnet new globaljson --sdk-version 10.0.401 --roll-forward latestFeature`.

**Constraint relevant to Feature 0 (fact, not recommendation):** pinning `"version": "10.0.401"` with the default policy (`patch`) restricts the build to the 10.0.4xx band and will fail on a machine that has only 10.0.112 installed. `latestPatch` restricts to the same band but requires ≥ the pinned patch. `latestFeature` permits any 10.0.xxx band ≥ the pinned value. `latestMajor` would permit the .NET 11 SDK once it ships. This machine currently has **SDK 10.0.302** installed (LOCAL: `dotnet --list-sdks`), which satisfies none of `10.0.401`+`patch`, `10.0.401`+`latestPatch`, or `10.0.401`+`latestFeature`.

### 1.5 SDK / MSBuild breaking changes in .NET 10

Source: [Breaking changes in .NET 10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0). **The page states verbatim: "This article is a work in progress. It's not a complete list of breaking changes in .NET 10."**

SDK and MSBuild entries:

| Title | Type |
|---|---|
| .NET CLI `--interactive` defaults to `true` in user scenarios | Behavioral |
| `dotnet` CLI commands log non-command-relevant data to stderr | Behavioral |
| .NET tool packaging creates RuntimeIdentifier-specific tool packages | Behavioral |
| Default workload configuration changed from "loose manifests" to "workload sets" mode | Behavioral |
| `DefineConstants` for target frameworks not available at evaluation time | Behavioral |
| Code coverage `EnableDynamicNativeInstrumentation` defaults to `false` | Behavioral |
| dnx scripts bypass `global.json` SDK selection | Behavioral |
| `dnx.ps1` no longer included in the .NET SDK | Source incompatible |
| Double quotes in file-level directives are disallowed | Source incompatible |
| `dotnet new sln` defaults to the SLNX file format | Behavioral |
| `dotnet package list` performs restore | Behavioral |
| **`dotnet restore` audits transitive packages** | Behavioral |
| `dotnet tool install --local` creates a manifest by default | Behavioral |
| `dotnet watch` logs to stderr instead of stdout | Behavioral |
| project.json not supported in `dotnet restore` | Source incompatible |
| SHA-1 fingerprint support deprecated in `dotnet nuget sign` | Behavioral |
| `MSBUILDCUSTOMBUILDEVENTWARNING` escape hatch removed | Behavioral |
| MSBuild custom culture resource handling | Behavioral |
| NU1510 raised for direct references pruned by NuGet | Source incompatible |
| NuGet packages with no runtime assets aren't included in `deps.json` | Source incompatible |
| **`PackageReference` without a version raises an error (NU1015)** | Behavioral |
| `PrunePackageReference` privatizes direct prunable references | Behavioral |
| HTTP warnings promoted to errors in `dotnet package list` / `dotnet package search` | Behavioral / source incompatible |
| `NUGET_ENABLE_ENHANCED_HTTP_RETRY` environment variable removed | Behavioral |
| NuGet audit sources no longer allow insecure HTTP by default | Behavioral |
| NuGet logs an error for invalid package IDs | Behavioral |
| `ToolCommandName` not set for non-tool packages | Source incompatible |

Core-library entries most likely to touch a greenfield web/worker app: `ActivitySource.CreateActivity`/`StartActivity` sampling behavior change (see §7.6); **default trace context propagator updated to W3C** (see §7.6); `BufferedStream.WriteByte` no longer performs an implicit flush; C# 14 overload resolution with span parameters; **the .NET runtime no longer provides default termination signal handlers**; `System.Linq.AsyncEnumerable` moved into the core libraries (source incompatible); `FilePatternMatch.Stem` changed to non-nullable; **OpenSSL 1.1.1 or later now required on Unix**; `DOTNET_OPENSSL_VERSION_OVERRIDE` and `DOTNET_ICU_VERSION_OVERRIDE` env-var renames.

Extensions entries: `BackgroundService` runs all of `ExecuteAsync` as a Task; fixes to `GetKeyedService()`/`GetKeyedServices()` with `AnyKey`; **null values preserved in configuration**; message no longer duplicated in Console JSON log output; `ProviderAliasAttribute` moved to `Microsoft.Extensions.Logging.Abstractions`.

Networking entries: HTTP/3 disabled by default with `PublishTrimmed` (source incompatible); `MailAddress` enforces validation for consecutive dots; streaming HTTP responses enabled by default in browser HTTP clients; `Uri` length limits removed.

Serialization: **System.Text.Json now checks for property name conflicts**; `XmlSerializer` no longer ignores properties marked `[Obsolete]`.

Containers: **default .NET images now use Ubuntu** (behavioral).

**Documented default-value changes in .NET 10** (same page): `--interactive` → `true`; `EnableDynamicNativeInstrumentation` → `false`; `dotnet new sln` → SLNX; workload config → workload-sets mode; `dotnet tool install --local` creates a manifest; HTTP/3 disabled with `PublishTrimmed`; streaming HTTP responses on in browser clients; tar `atime`/`ctime` excluded; trace propagator → W3C; container base images → Ubuntu; NuGet audit sources disallow insecure HTTP.

ASP.NET Core 10 breaking changes are tracked separately — see §3.3. EF Core 10 breaking changes are at [ef-core-10.0/breaking-changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes) (out of scope for this ticket).

---

## 2. C# language version

**The latest stable C# language version supported by the .NET 10 SDK is C# 14.0.**

| Evidence | Value | Source |
|---|---|---|
| `releases.json`, SDK 10.0.401 and 10.0.112 | `"csharp-version": "14.0"` | [releases.json](https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/releases.json) |
| Download page | "C# 14.0, F# 10.0, and Visual Basic 17.13" | [download/dotnet/10.0](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) |
| Learn | "C# 14 is the latest C# release. C# 14 is supported on .NET 10." | [whats-new/csharp-14](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14) |
| Learn | "C# 15 is supported only on .NET 11 and newer versions. C# 14 is supported only on .NET 10 and newer versions… Using a C# language version newer than the version associated with your target TFM is unsupported." | [language-versioning](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-versioning) |
| Roslyn | The current working set on the feature-status page is **C# 15.0**; all C# 14.0 features are listed as merged. | [Language Feature Status.md](https://github.com/dotnet/roslyn/blob/main/docs/Language%20Feature%20Status.md) |
| **LOCAL** | `dotnet msbuild -getProperty:LangVersion` on a `net10.0` web project resolves to **`14.0`** (measured on SDK 10.0.302) | LOCAL |

### 2.1 `LangVersion` defaults by TFM

From the Defaults table in [language-versioning](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-versioning):

| Target | Version | Default C# |
|---|---|---|
| .NET | 11.x | C# 15 |
| **.NET** | **10.x (`net10.0`)** | **C# 14** |
| .NET | 9.x | C# 13 |
| .NET | 8.x | C# 12 |
| .NET Standard | 2.1 | C# 8.0 |
| .NET Standard | 2.0 / 1.x | C# 7.3 |
| .NET Framework | all | C# 7.3 |

Additional documented behavior:

- "If your project targets a `preview` framework that has a corresponding preview language version, the language version used is the preview language version." ([language-versioning](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-versioning))
- Valid `LangVersion` values include `preview`, `latest`, `latestMajor`/`default`, and `15.0`, `14.0`, `13.0`, `12.0`, `11.0`, `10.0`, `9.0`, `8.0`, `7.3` … `ISO-1` ([configure-language-version](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/configure-language-version)).
- "Specifying `default` uses the latest version of the language that the compiler supports, **without taking into account the target framework**." ([language-versioning](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-versioning))
- Docs warn against `latest`: "The value of `latest` can change from machine to machine, making builds unreliable." ([configure-language-version](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/configure-language-version))
- In Visual Studio the language-version UI option is disabled: "To change the language version in Visual Studio, change the project's target framework."
- `#error version` reports the compiler version and selected language version as CS8304.

**Constraint relevant to PRD 3.1** ("latest stable C# language version supported by the selected .NET 10 SDK"): targeting `net10.0` yields C# 14 with **no `LangVersion` property required**. Setting `LangVersion` explicitly to `latest` is documented as making builds machine-dependent; setting `default` decouples the language version from the TFM.

### 2.2 C# 14 feature list

From [whats-new/csharp-14](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14):

1. **Extension members** — new `extension(...)` block syntax; enables extension *properties*, static extension members, and static user-defined operators as extension members, alongside extension methods.
2. **Null-conditional assignment** — `?.` and `?[]` may appear on the left of an assignment or compound assignment (`customer?.Order = GetCurrentOrder();`). RHS evaluated only when LHS is non-null. `++`/`--` are **not** allowed.
3. **`nameof` supports unbound generic types** — `nameof(List<>)` evaluates to `List`.
4. **More implicit conversions for `Span<T>`/`ReadOnlySpan<T>`** ("first-class span types") — new implicit conversions among `ReadOnlySpan<T>`, `Span<T>`, `T[]`; span types can be extension-method receivers and participate in generic type inference.
5. **Modifiers on simple lambda parameters** — `scoped`, `ref`, `in`, `out`, `ref readonly` without specifying the parameter type. `params` still requires an explicitly typed parameter list.
6. **`field` backed properties** — the `field` token in an accessor body refers to a compiler-synthesized backing field. Disambiguate a real `field` symbol with `@field` or `this.field`.
7. **`partial` events and constructors** — exactly one defining and one implementing declaration; only the implementing partial constructor may have a `this()`/`base()` initializer; the implementing partial event must include `add`/`remove`.
8. **User-defined compound assignment operators.**
9. **New preprocessor directives for file-based apps.**

Roslyn additionally lists as merged for C# 14.0: "String literals in data section as UTF8", "Ignored directives", and "Optional and named arguments in Expression trees" ([Language Feature Status.md](https://github.com/dotnet/roslyn/blob/main/docs/Language%20Feature%20Status.md)).

C# 14 **compiler** breaking changes are tracked separately at [compiler breaking changes - dotnet 10](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/breaking-changes/compiler%20breaking%20changes%20-%20dotnet%2010). One appears in the .NET 10 breaking-changes index: "C# 14 overload resolution with span parameters."

---

## 3. ASP.NET Core 10

### 3.1 Package ids and current stable versions

All versions read from the NuGet flat-container API (`https://api.nuget.org/v3-flatcontainer/<lowercase-id>/index.json`), taking the **highest non-prerelease 10.x**, on 2026-09-10.

| Package id | Stable version | Shared framework? |
|---|---|---|
| `Microsoft.AspNetCore.SignalR.Client` | **10.0.12** | No — `PackageReference` required |
| `Microsoft.AspNetCore.SignalR.Protocols.MessagePack` | **10.0.12** | No — `PackageReference` required |
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | **10.0.12** | No — `PackageReference` required |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | **10.0.12** | No — `PackageReference` required |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | **10.0.12** | No — `PackageReference` required |
| `Microsoft.AspNetCore.OpenApi` | **10.0.12** | No — `PackageReference` required |
| `Microsoft.AspNetCore.Diagnostics.EntityFrameworkCore` | **10.0.12** | No — `PackageReference` required |
| `Microsoft.Extensions.Diagnostics.HealthChecks` | **10.0.12** | **Yes** — see NU1510 note in §3.3 |
| `Microsoft.Extensions.Http.Resilience` | **10.10.0** | No — different version train, see §3.2 |

Sources: the corresponding flat-container indexes, e.g. [SignalR.Client](https://api.nuget.org/v3-flatcontainer/microsoft.aspnetcore.signalr.client/index.json), [OpenApi](https://api.nuget.org/v3-flatcontainer/microsoft.aspnetcore.openapi/index.json), [Http.Resilience](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.http.resilience/index.json).

`Microsoft.Extensions.Diagnostics.HealthChecks` also has an `11.0.0-rc.1.26425.128` prerelease published; `10.0.12` remains the highest stable 10.x.

### 3.2 Two Microsoft.Extensions version trains

A live trap for any `Directory.Packages.props` that assumes one version variable. Verified via flat-container queries on 2026-09-10:

| Package | Stable | Version line |
|---|---|---|
| `Microsoft.Extensions.Diagnostics` | 10.0.12 | `dotnet/runtime` |
| `Microsoft.Extensions.Diagnostics.HealthChecks` | 10.0.12 | `dotnet/runtime` |
| `Microsoft.Extensions.Http.Resilience` | **10.10.0** | `dotnet/extensions` |
| `Microsoft.Extensions.Telemetry` | **10.10.0** | `dotnet/extensions` |
| `Microsoft.Extensions.Http.Diagnostics` | **10.10.0** | `dotnet/extensions` |
| `Microsoft.Extensions.ServiceDiscovery` | **10.10.0** | `dotnet/extensions` |

`Microsoft.Extensions.Http.Resilience` has stable 10.x versions `10.0.0, 10.1.0 … 10.10.0` — **there is no `10.0.12`** ([flat container](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.http.resilience/index.json)).

Verified negative: none of `Microsoft.Extensions.Diagnostics` 10.0.12, `Microsoft.Extensions.Telemetry` 10.10.0, `Microsoft.Extensions.Http.Diagnostics` 10.10.0, or `Microsoft.Extensions.ServiceDiscovery` 10.10.0 declares any `OpenTelemetry.*` NuGet dependency in any dependency group (each nuspec scanned for `id="OpenTelemetry…"` — zero matches). All four target net10.0, net9.0, net8.0, netstandard2.0, net462.

### 3.3 Shared framework inventory (what needs no `PackageReference`)

"Projects that target the `Microsoft.NET.Sdk.Web` SDK implicitly reference the `Microsoft.AspNetCore.App` framework. No additional references are required for these projects." The shared framework "Doesn't include third-party dependencies" and "Includes all supported packages by the ASP.NET Core team." ([metapackage-app](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/metapackage-app))

The authoritative generated list is [`eng/SharedFramework.Local.props` on `release/10.0`](https://github.com/dotnet/aspnetcore/blob/release/10.0/eng/SharedFramework.Local.props) ("This file contains a complete list of the assemblies which are part of the shared framework").

**LOCAL cross-check** — the installed `Microsoft.AspNetCore.App.Ref` **10.0.10** targeting pack (`ref/net10.0`) contains **140 reference assemblies**. Confirmed present:

- SignalR server side: `Microsoft.AspNetCore.SignalR.dll`, `.SignalR.Core.dll`, `.SignalR.Common.dll`, `.SignalR.Protocols.Json.dll`
- Identity: `Microsoft.AspNetCore.Identity.dll`, `Microsoft.Extensions.Identity.Core.dll`, `Microsoft.Extensions.Identity.Stores.dll`
- Blazor/Components: `Microsoft.AspNetCore.Components.dll`, `.Components.Web.dll`, `.Components.Forms.dll`, `.Components.Server.dll`, `.Components.Endpoints.dll`, `.Components.Authorization.dll`
- Security infrastructure: `Microsoft.AspNetCore.Antiforgery.dll`, `.DataProtection.dll` (+ `.Abstractions`, `.Extensions`)

Confirmed **absent** from the targeting pack — each therefore requires an explicit `PackageReference`: `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.AspNetCore.Identity.UI`, `Microsoft.AspNetCore.Components.QuickGrid`, `Microsoft.AspNetCore.Components.WebAssembly`, `Microsoft.AspNetCore.SignalR.Client`, `Microsoft.AspNetCore.SignalR.Protocols.MessagePack`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.OpenApi`.

Also confirmed from `SharedFramework.Local.props`: `Microsoft.Extensions.Diagnostics.HealthChecks` (+ `.Abstractions`) and `Microsoft.Extensions.Validation` **are** in the shared framework, while `Microsoft.AspNetCore.SignalR.Client`, `.Protocols.MessagePack`, `.StackExchangeRedis`, `Authentication.JwtBearer`, `OpenApi`, `Diagnostics.EntityFrameworkCore` and `Extensions.Http.Resilience` are not (grep over the 115-line file returned no matches for any of them).

**Documentation conflict, unresolved:** the Identity intro page states "All the Identity-dependent NuGet packages are included in the ASP.NET Core shared framework" ([identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0)) — inconsistent with the targeting-pack contents for `Identity.EntityFrameworkCore` and `Identity.UI`, and that sentence's link points at the ASP.NET Core **3.0** release notes.

**NU1510 constraint.** With package pruning enabled on a project targeting .NET 10 or later, "NuGet raises a `NU1510` warning" for "a direct package reference that overlaps with a framework-provided library"; the recommended action is to remove the reference ([NU1510](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/nu1510-pruned-references)). `PrunePackageReference` additionally marks directly prunable items `PrivateAssets=all` / `IncludeAssets=none` and excludes them from the generated `.nuspec`, but "you'll still get a `NU1510` warning until you remove the reference from your project" ([PrunePackageReference](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/prune-packagereference-privateassets)). A direct `PackageReference` to `Microsoft.Extensions.Diagnostics.HealthChecks` from a `net10.0` web project therefore warns — and under `TreatWarningsAsErrors` (PRD 56) that becomes a build error.

### 3.4 What's new in ASP.NET Core 10

Source: [aspnetcore-10.0 release notes](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0), verified against the [doc source](https://raw.githubusercontent.com/dotnet/AspNetCore.Docs/live/aspnetcore/release-notes/aspnetcore-10.0.md). The page has exactly these top-level sections: Blazor, Blazor Hybrid, SignalR, Minimal APIs, OpenAPI, Authentication and authorization, Miscellaneous, Breaking changes. (Blazor items are enumerated in §4.5; SignalR in §5.)

**Minimal APIs**

- Empty string in a form post treated as `null` for nullable value types.
- **Validation support in Minimal APIs** — `AddValidation()`, `DisableValidation()`, DataAnnotations, `IValidatableObject`, automatic 400 responses.
- Validation with record types; enhanced validation for classes and records.
- Validation integration with `IProblemDetailsService`.
- **Server-Sent Events (SSE)** via `TypedResults.ServerSentEvents()` / `IAsyncEnumerable<SseItem<T>>`.
- Validation APIs moved to `Microsoft.Extensions.Validation`.

**OpenAPI**

- **OpenAPI 3.1 is the default document version in .NET 10**; `OpenApiOptions.OpenApiVersion`; build-time `--openapi-version OpenApi3_1`.
- OpenAPI in YAML: `app.MapOpenApi("/openapi/{documentName}.yaml")`.
- Response descriptions on `ProducesResponseType` / `Produces` / `ProducesDefaultResponseType`.
- XML doc comments populated into the OpenAPI document — **requires `<GenerateDocumentationFile>true</GenerateDocumentationFile>`**.
- `Microsoft.AspNetCore.OpenApi` included in the Web API (Native AOT) template (`--no-openapi` disables).
- `IOpenApiDocumentProvider` available from DI.
- `GetOrCreateSchemaAsync`, `Document`, `AddComponent` for schema generation in transformers; endpoint-specific operation transformers.
- **`Microsoft.OpenApi` upgraded to 2.0.0** — a breaking change: entities typed as interfaces; `Nullable` removed from `OpenApiSchema` (use `JsonSchemaType.Null` in `Type`); `OpenApiAny` removed (use `JsonNode`).
- Schema enhancements: nullable types modelled with `oneOf`; property descriptions emitted as siblings of `$ref`; metadata from XML comments on `[AsParameters]` types; unknown HTTP methods excluded; invariant culture used for document generation.

**Authentication and authorization**

- **Authentication and authorization metrics** — see §6.4.
- **ASP.NET Core Identity metrics** — see §6.4.
- **Cookie login redirects avoided for known API endpoints** via `IApiEndpointMetadata` — see §3.5.

**Miscellaneous**

- `ExceptionHandlerOptions.SuppressDiagnosticsCallback`.
- **`.localhost` TLD support** — Kestrel binds `*.localhost` to loopback; the dev cert covers `*.dev.localhost`; `dotnet new web -n MyApp --localhost-tld`.
- JSON + `PipeReader` deserialization in MVC and Minimal APIs (AppContext switch `Microsoft.AspNetCore.UseStreamBasedJsonParsing`).
- Automatic eviction from the memory pool — `Microsoft.AspNetCore.MemoryPool` metrics; `IMemoryPoolFactory` / `MemoryPoolFactory`.
- `HttpSysOptions.RequestQueueSecurityDescriptor` (Windows only) ([include](https://raw.githubusercontent.com/dotnet/AspNetCore.Docs/live/aspnetcore/release-notes/aspnetcore-10/includes/httpsys.md)).
- **Better testing support for top-level statements** — a source generator emits `public partial class Program`, and a new analyzer flags an explicit declaration ([include](https://raw.githubusercontent.com/dotnet/AspNetCore.Docs/live/aspnetcore/release-notes/aspnetcore-10/includes/testAppsTopLevel.md)). **Relevant to PRD 5 (`WebApplicationFactory`).**
- New JSON Patch implementation on `System.Text.Json` — package `Microsoft.AspNetCore.JsonPatch.SystemTextJson`. **Not a drop-in replacement** (no dynamic types such as `ExpandoObject`); the doc states the JSON Patch standard has "inherent security risks" that the new implementation "doesn't attempt to mitigate" ([include](https://raw.githubusercontent.com/dotnet/AspNetCore.Docs/live/aspnetcore/release-notes/aspnetcore-10/includes/jsonPatch.md)).
- `RedirectHttpResult.IsLocalUrl` for open-redirect validation — a URL is local if it has no host/authority section and has an absolute path (`~/` virtual paths count).

### 3.5 Breaking changes in ASP.NET Core 10

The .NET 10 breaking-changes index does **not** list ASP.NET Core changes inline; its ASP.NET Core section is a single pointer: "See Breaking changes in ASP.NET Core 10." ([compatibility/10.0](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0))

Complete documented list — **9 items** ([breaking-changes/10/overview](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/overview)):

| Title | Type |
|---|---|
| [Cookie login redirects disabled for known API endpoints](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/cookie-authentication-api-endpoints) | Behavioral |
| [Deprecation of `WithOpenApi` extension method](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/withopenapi-deprecated) | Source incompatible |
| [Exception diagnostics suppressed when `TryHandleAsync` returns true](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/exception-handler-diagnostics-suppressed) | Behavioral |
| [`IActionContextAccessor` / `ActionContextAccessor` obsolete](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/iactioncontextaccessor-obsolete) | Source incompatible / behavioral |
| [`IncludeOpenAPIAnalyzers` and MVC API analyzers deprecated](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/openapi-analyzers-deprecated) | Source incompatible |
| [`IPNetwork` and `ForwardedHeadersOptions.KnownNetworks` obsolete](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/ipnetwork-knownnetworks-obsolete) | Source incompatible |
| [`Microsoft.Extensions.ApiDescription.Client` deprecated](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/apidescription-client-deprecated) | Source incompatible |
| [Razor runtime compilation obsolete](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/razor-runtime-compilation-obsolete) | Source incompatible |
| [`WebHostBuilder`, `IWebHost`, `WebHost` obsolete](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/webhostbuilder-deprecated) | Source incompatible |

**Verified negative: there are no Blazor-specific and no Identity-specific entries in that list.**

**Cookie auth → 401/403 for API endpoints** ([detail page](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/cookie-authentication-api-endpoints), also [aspnet/Announcements#525](https://github.com/aspnet/Announcements/issues/525)) — introduced in **.NET 10 Preview 7**:

- Previously the cookie handler redirected to the login / access-denied URI for everything except XHRs. Now requests to *known API endpoints* return **401/403**.
- Known API endpoints are identified by the new `Microsoft.AspNetCore.Http.Metadata.IApiEndpointMetadata`, applied automatically to: `[ApiController]` endpoints; Minimal API endpoints that read JSON request bodies or write JSON responses; endpoints using `TypedResults` return types; **and SignalR endpoints**.
- XHRs still get 401/403 regardless of endpoint.
- Revert by overriding `CookieAuthenticationEvents.OnRedirectToLogin` / `OnRedirectToAccessDenied`; the page supplies both an "always redirect" and an "exact pre-10 XHR-only" snippet.

**One announcement labeled `10.0.0` is absent from the docs list** ([aspnet/Announcements#517](https://github.com/aspnet/Announcements/issues/517)): "Forwarded Headers Middleware Now Ignores X-Forwarded-* Headers from Unknown Proxies." The issue body says "Version: .NET 8" and "Starting in ASP.NET Core 8.0.17 and 9.0.6" — only `X-Forwarded-*` headers from proxies in `ForwardedHeadersOptions.KnownProxies`/`KnownNetworks` are processed. Escape hatches: `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, or (for apps targeting .NET 9.0 or earlier) the `Microsoft.AspNetCore.HttpOverrides.IgnoreUnknownProxiesWithoutFor` AppContext switch / `MICROSOFT_ASPNETCORE_HTTPOVERRIDES_IGNORE_UNKNOWN_PROXIES_WITHOUT_FOR` env var. **Directly relevant to PRD 45/46 (HTTPS / reverse proxy).** Note also that `IPNetwork` and `ForwardedHeadersOptions.KnownNetworks` are obsolete in 10.0 (table above).

Changes documented in the release notes as breaking but absent from both breaking-changes pages: Blazor WebAssembly `HttpClient` response streaming on by default; `Microsoft.OpenApi` 2.0.0 API shape; Blazor `<NotFound>` render fragment unsupported; `BlazorCacheBootResources` removed.

`dotnet/aspnetcore` issues labeled `breaking-change` with a 10.0 milestone: [#62778](https://github.com/dotnet/aspnetcore/issues/62778) (`WithOpenApi` deprecation, 10.0-preview7), [#62583](https://github.com/dotnet/aspnetcore/pull/62583) (`OwningComponentBase` implements `IAsyncDisposable`, 10.0-preview7), [#62689](https://github.com/dotnet/aspnetcore/issues/62689) (obsolete MVC API analyzers, no milestone).

### 3.6 Migration 9.0 → 10.0

Documented steps ([migration/90-to-100](https://learn.microsoft.com/en-us/aspnet/core/migration/90-to-100)):

1. **Prerequisites** — Visual Studio 2022 with the *ASP.NET and web development* workload; or VS Code + C# Dev Kit + the .NET 10.0 SDK.
2. **Update `global.json`** — change `sdk.version`; the doc's diff example goes `9.0.304` → `10.0.100`.
3. **Update the TFM** — `net9.0` → `net10.0`.
4. **Update package references** — every `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*`, `Microsoft.Extensions.*`, and `System.Net.Http.Json` `Version` "to 10.0.0 or later".
5. **Blazor** — set the WebAssembly environment with `<WasmApplicationEnvironmentName>`; boot config inlined into `dotnet.js` ("Currently, there's no documented replacement strategy" for integrity-check scripts and DLL-extension customization); remove `<BlazorCacheBootResources>` ("it no longer has any effect"); adopt passkey authentication in an existing Blazor Web App; and when navigation errors are disabled, edit `Components/Account/IdentityRedirectManager.cs` to remove the `InvalidOperationException` from `RedirectTo` and remove **five** instances of `[DoesNotReturn]`.
6. **Breaking changes** — defers to the compatibility docs.

The migration doc contains no gRPC, MVC, or SignalR migration sections. ZWarden is greenfield (PRD 57), so migration is not applicable; item 5 is recorded because it documents the shape the .NET 10 template already ships.

---

## 4. Blazor Web App project model and render modes

### 4.1 Template

| Template | Short name | Introduced |
|---|---|---|
| Blazor Web App | `blazor` | 8.0.100 |
| Blazor WebAssembly Standalone App | `blazorwasm` | 3.1.300 |

Sources: [blazor/tooling](https://learn.microsoft.com/en-us/aspnet/core/blazor/tooling?view=aspnetcore-10.0), [dotnet-new-sdk-templates](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new-sdk-templates). `blazorserver`, `blazorserver-empty` and `blazorwasm-empty` are listed as **discontinued** ("Discontinued since 8.0"); the "Hosted" Blazor WebAssembly option "isn't available in .NET 8 or later."

**LOCAL — verbatim `dotnet new blazor --help` on SDK 10.0.302:**

| Option | Values / default |
|---|---|
| `-f, --framework` | `net10.0` \| `net8.0` \| `net9.0` — default **`net10.0`** |
| `-int, --interactivity` | `None` \| `Server` \| `WebAssembly` \| `Auto` — default **`Server`** |
| `-e, --empty` | bool, default `false` |
| `-au, --auth` | `Individual` \| `None` — default **`None`** |
| `-uld, --use-local-db` | bool, default `false` (only with `--auth Individual`) |
| `-ai, --all-interactive` | bool, default `false`; "Enabled if: (InteractivityPlatform != \"None\")" |
| `--no-https` | bool, default `false` (only when `--auth` isn't `Individual`) |
| `--use-program-main` | bool, default `false` |
| `--localhost-tld` | bool, default `false` |
| `--no-restore`, `--exclude-launch-settings` | bool, default `false` |

Documented option semantics ([dotnet-new-sdk-templates](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new-sdk-templates)):

- `None` — "No interactivity (static server-side rendering only)."
- `Server` — "(Default) Runs the app on the server with interactive server-side rendering."
- `WebAssembly` — "Runs the app using client-side rendering in the browser with WebAssembly."
- `Auto` — "Uses interactive server-side rendering while downloading the Blazor bundle and activating the Blazor runtime on the client, then uses client-side rendering with WebAssembly."
- `-ai|--all-interactive` — "Makes every page interactive by applying an interactive render mode at the top level. If `false`, pages use static server-side rendering by default and can be marked interactive on a per-page or per-component basis."
- The `blazor` template supports **only** `None` and `Individual` for `--auth` (no `IndividualB2C`/`SingleOrg`/`MultiOrg`/`Windows`).

IDE-side naming for the same options: **Interactive render mode** (`Server`, `WebAssembly`, `Auto (Server and WebAssembly)`, `None`) and **Interactivity location** (`Per page/component` default, or `Global`). "Interactivity location can only be set if **Interactive render mode** isn't `None` and authentication isn't enabled." ([blazor/tooling](https://learn.microsoft.com/en-us/aspnet/core/blazor/tooling?view=aspnetcore-10.0))

**Three doc/CLI discrepancies found (LOCAL vs DOC):** the real alias is `-int`, not the `-i` referenced in the docs' `--all-interactive` text; `-e` is a real short alias for `--empty`; and `--localhost-tld` exists in the CLI but is absent from [dotnet-new-sdk-templates](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new-sdk-templates).

### 4.2 Project structure

Documented ([blazor/project-structure](https://learn.microsoft.com/en-us/aspnet/core/blazor/project-structure?view=aspnetcore-10.0)):

- `Components/` — server-side Razor components; `Components/Pages/` — routable components (`@page`); `Components/Layout/` — `MainLayout`, `NavMenu`, and **new in .NET 10** `ReconnectModal.razor` + `.css` + `.js` ("included when the app's interactive render mode is either Interactive Server or Interactive Auto").
- `Components/App.razor` — "the root component of the app with HTML `<head>` markup, the `Routes` component, and the Blazor `<script>` tag."
- `Components/Routes.razor` — "is either in the server project or the `.Client` project and sets up routing using the `Router` component."
- `Components/Pages/NotFound.razor` — **.NET 10 only.**
- **"Components using the Interactive WebAssembly or Interactive Auto render modes must be located in the `.Client` project."**
- "Based on the interactive render mode chosen, the `Layout` folder is either in the server project in the `Components` folder or at the root of the `.Client` project."
- `<StaticWebAssetProjectMode>Default</StaticWebAssetProjectMode>` is "required … in the `.Client` project of a Blazor Web App"; "Changing the value (`Default`) … or removing the property from the `.Client` project isn't supported." ([static-files](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/static-files?view=aspnetcore-10.0))

**LOCAL — `dotnet new blazor --interactivity Auto --auth Individual --all-interactive` on SDK 10.0.302** produced these project files verbatim.

Server (`Microsoft.NET.Sdk.Web`):

```xml
<TargetFramework>net10.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<UserSecretsId>aspnet-…</UserSecretsId>
<BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>
```

with `PackageReference`s: `Microsoft.AspNetCore.Components.WebAssembly.Server`, `Microsoft.AspNetCore.Diagnostics.EntityFrameworkCore`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design` (`PrivateAssets="all"`), `Microsoft.EntityFrameworkCore.Tools` — all at `10.0.10`, matching the installed runtime rather than the current 10.0.12.

Client (`Microsoft.NET.Sdk.BlazorWebAssembly`):

```xml
<TargetFramework>net10.0</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable>
<NoDefaultLaunchSettingsFile>true</NoDefaultLaunchSettingsFile>
<StaticWebAssetProjectMode>Default</StaticWebAssetProjectMode>
<BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>
```

with `PackageReference`s `Microsoft.AspNetCore.Components.WebAssembly` and `Microsoft.AspNetCore.Components.WebAssembly.Authentication` at `10.0.10`.

`Microsoft.AspNetCore.Components.Authorization` is used but **not** referenced — it lives in the shared framework (§3.3).

**LOCAL — the generated `App.razor` (the .NET 10 shape):**

```razor
<base href="/" />
<ResourcePreloader />
<link rel="stylesheet" href="@Assets["app.css"]" />
<ImportMap />
<HeadOutlet @rendermode="PageRenderMode" />
…
<Routes @rendermode="PageRenderMode" />
<ReconnectModal />
<script src="@Assets["_framework/blazor.web.js"]"></script>

@code {
    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;
    private IComponentRenderMode? PageRenderMode =>
        HttpContext.AcceptsInteractiveRouting() ? InteractiveAuto : null;
}
```

**LOCAL — the generated server `Program.cs`** uses `AddRazorComponents().AddInteractiveServerComponents().AddInteractiveWebAssemblyComponents().AddAuthenticationStateSerialization()`, `AddCascadingAuthenticationState()`, `MapStaticAssets()`, `app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true)`, and `MapRazorComponents<App>().AddInteractiveServerRenderMode().AddInteractiveWebAssemblyRenderMode().AddAdditionalAssemblies(typeof(Client._Imports).Assembly)`. The client `Program.cs` uses `AddAuthorizationCore()`, `AddCascadingAuthenticationState()`, `AddAuthenticationStateDeserialization()`.

**LOCAL — the generated `Routes.razor`** uses the new .NET 10 parameter: `<Router AppAssembly="…" NotFoundPage="typeof(Pages.NotFound)">` with **no** `<NotFound>` fragment.

`_Imports.razor` includes `@using static Microsoft.AspNetCore.Components.Web.RenderMode`, which is what makes the bare `InteractiveAuto` / `InteractiveServer` shorthand resolve.

### 4.3 Render modes — complete

Primary source: [blazor/components/render-modes](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0); prerendering: [blazor/components/prerender](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/prerender?view=aspnetcore-10.0).

Scope note: "This guidance doesn't apply to standalone Blazor WebAssembly apps… If a render mode is applied to a component in a Blazor WebAssembly app, the render mode designation has no influence on rendering the component."

| Name | Description | Render location | Interactive | `@rendermode` value |
|---|---|---|---|---|
| **Static Server** | Static server-side rendering (static SSR) | Server | No | *(none — the default)* |
| **Interactive Server** | Interactive SSR using Blazor Server | Server | Yes | `InteractiveServer` |
| **Interactive WebAssembly** | Client-side rendering (CSR) using Blazor WebAssembly | Client | Yes | `InteractiveWebAssembly` |
| **Interactive Auto** | Interactive SSR initially, then CSR on subsequent visits once the bundle has downloaded | Server, then client | Yes | `InteractiveAuto` |

- The `RenderMode` static class exposes **exactly three** properties: `InteractiveServer`, `InteractiveWebAssembly`, `InteractiveAuto` (DOC; independently confirmed against the installed `Microsoft.AspNetCore.App.Ref` 10.0.10 XML docs — LOCAL).
- Static SSR has **no** `RenderMode` property and no `@rendermode` value: "The default render mode is Static." A `null` `IComponentRenderMode` means inherit-from-parent.
- Instance types, each taking a `prerender` constructor flag: `InteractiveServerRenderMode`, `InteractiveWebAssemblyRenderMode`, `InteractiveAutoRenderMode`.
- `@rendermode` as a *directive* "takes a single parameter that's a static instance of type `IComponentRenderMode`"; as a *directive attribute* it "can take any render mode instance, static or not."

**Where a render mode can be applied**

- **Component instance**: `<Dialog @rendermode="InteractiveServer" />`.
- **Component definition / page**: `@page "…"` then `@rendermode InteractiveServer`. "Routable pages use the same render mode as the `Router` component that rendered the page."
- **Whole app**: "indicate the render mode at the highest-level interactive component in the app's component hierarchy that isn't a root component" — i.e. `<Routes @rendermode="InteractiveServer" />` in `App.razor`, and "you also typically must set the same interactive render mode on the `HeadOutlet` component."
- **Not supported:** "Making a root component interactive, such as the `App` component, isn't supported. Therefore, the render mode for the entire app can't be set directly by the `App` component."
- **Global client-side interactivity requires file moves**: when applying WebAssembly/Auto globally at `Routes`, move `Components/Layout` → `.Client/Layout`, `Components/Pages` → `.Client/Pages`, and `Routes.razor` → the `.Client` root.

**Prerendering**

- "Prerendering is enabled by default for interactive components."
- "`OnAfterRender{Async}` component lifecycle events aren't called when prerendering, only after the component renders interactively."
- "Internal navigation with interactive routing doesn't use prerendering because the page is already interactive."
- Disable per instance: `@rendermode="new InteractiveServerRenderMode(prerender: false)"` (and the WebAssembly/Auto equivalents). Per definition: `@rendermode @(new InteractiveServerRenderMode(prerender: false))`. Whole app: apply to **both** `<Routes>` and `<HeadOutlet>`.
- "Disabling prerendering using the preceding techniques only takes effect for top-level render modes. If a parent component specifies a render mode, the prerendering settings of its children are ignored."
- Prerendering is **not supported for authentication endpoints** (`/authentication/` path segment) in Blazor WebAssembly ([webassembly security](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/webassembly/?view=aspnetcore-10.0)).

**Documented limitations — the four propagation rules**

1. "The default render mode is Static."
2. Interactive Server / WebAssembly / Auto "can be used from a component, including using different render modes for sibling components."
3. **"You can't switch to a different interactive render mode in a child component. For example, a Server component can't be a child of a WebAssembly component."** Runtime error: *"Cannot create a component of type '…' because its render mode '…InteractiveWebAssemblyRenderMode' is not supported by Interactive Server rendering."*
4. **"Parameters passed to an interactive child component from a Static parent must be JSON serializable. This means that you can't pass render fragments or child content from a Static parent component to an interactive child component."** Runtime error: *"Cannot pass the parameter 'ChildContent' to component 'SharedMessage' with rendermode 'InteractiveServerRenderMode'. This is because the parameter is of the delegate type '…RenderFragment', which is arbitrary code and cannot be serialized."* The same applies to a layout inheriting `LayoutComponentBase` under per-page/component rendering. Workaround: wrap the child in a parameterless component — exactly what the template's `Routes` component does around `Router`.

**Static SSR specifics (.NET 10 wording)** — directly relevant to PRD 12 authorization:

> "During static SSR, Razor component page requests are processed by server-side ASP.NET Core middleware pipeline request processing for authorization. Dedicated Blazor features for authorization aren't operational because Razor components aren't rendered during server-side request processing. Blazor router features in the `Routes` component that aren't available during static SSR include displaying Not Authorized content (`<NotAuthorized>...</NotAuthorized>`)."

> "If the app exhibits root-level interactivity, server-side ASP.NET Core request processing isn't involved after the initial static SSR, which means that the preceding Blazor features work as expected."

**Interactive Server:** "Interactive Server components handle web UI events using a real-time connection with the browser called a circuit. A circuit and its associated state are created when a root Interactive Server component is rendered. The circuit is closed when there are no remaining Interactive Server components on the page."

**Interactive WebAssembly:** "Components using CSR must be built from a separate client project that sets up the Blazor WebAssembly host." Client-side services fail to resolve during prerendering (e.g. injecting `IWebAssemblyHostEnvironment` throws *"There is no registered service of type '…IWebAssemblyHostEnvironment'"*), with five documented mitigations.

**Interactive Auto:** "The Auto render mode never dynamically changes the render mode of a component already on the page. The Auto render mode makes an initial decision about which type of interactivity to use for a component, then the component keeps that type of interactivity for as long as it's on the page… Auto mode prefers to select a render mode that matches the render mode of existing interactive components … to avoid introducing a new interactive runtime that doesn't share state with the existing runtime." Auto components must also live in the `.Client` project.

**State-exposure difference:** persisted prerendered state is exposed to the browser for `InteractiveWebAssembly` (and therefore `InteractiveAuto`); for `InteractiveServer`, "ASP.NET Core Data Protection ensures that the data is transferred securely." ([prerendered-state-persistence](https://learn.microsoft.com/en-us/aspnet/core/blazor/state-management/prerendered-state-persistence?view=aspnetcore-10.0)) **Relevant to PRD 10 (sensitive data protection).**

**Runtime detection APIs:** `ComponentBase.RendererInfo` (`RendererInfo.Name` returns `Static`, `Server`, `WebAssembly`, or `WebView`; `RendererInfo.IsInteractive` is "`true` when rendering interactively or `false` when prerendering or for static SSR") and `ComponentBase.AssignedRenderMode` (`InteractiveServerRenderMode` / `InteractiveAutoRenderMode` / `InteractiveWebAssemblyRenderMode` / `null`).

**Static SSR pages inside a globally interactive app:** `@attribute [ExcludeFromInteractiveRouting]` — "Applying the attribute causes navigation to the page to exit from interactive routing. Inbound navigation is forced to perform a full-page reload." Detected via `RazorComponentsEndpointHttpContextExtensions.AcceptsInteractiveRouting()`; the documented `App.razor` pattern — which the .NET 10 template ships — is `private IComponentRenderMode? PageRenderMode => HttpContext.AcceptsInteractiveRouting() ? InteractiveServer : null;` applied to both `<Routes>` and `<HeadOutlet>`. Alternative: `HttpContext.GetEndpoint()?.Metadata.GetMetadata<RenderModeAttribute>()?.Mode`. Caveat: "Applying a `null` render mode doesn't always enforce static SSR… A `null` render mode is effectively the same as not specifying a render mode, which results in the component inheriting its parent's render mode."

### 4.4 Enabling render modes in `Program.cs` — exact API names

Verbatim from [render-modes](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0):

| API | Fully qualified declaring type |
|---|---|
| `AddRazorComponents` | `Microsoft.Extensions.DependencyInjection.RazorComponentsServiceCollectionExtensions` |
| `AddInteractiveServerComponents` | `Microsoft.Extensions.DependencyInjection.ServerRazorComponentsBuilderExtensions` |
| `AddInteractiveWebAssemblyComponents` | `Microsoft.Extensions.DependencyInjection.WebAssemblyRazorComponentsBuilderExtensions` |
| `MapRazorComponents` | `Microsoft.AspNetCore.Builder.RazorComponentsEndpointRouteBuilderExtensions` |
| `AddInteractiveServerRenderMode` | `Microsoft.AspNetCore.Builder.ServerRazorComponentsEndpointConventionBuilderExtensions` |
| `AddInteractiveWebAssemblyRenderMode` | `Microsoft.AspNetCore.Builder.WebAssemblyRazorComponentsEndpointConventionBuilderExtensions` |

```csharp
// Interactive SSR only
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Interactive WebAssembly only
builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();
app.MapRazorComponents<App>().AddInteractiveWebAssemblyRenderMode();

// Server + WebAssembly + Auto
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode();
```

Related APIs: `AddAdditionalAssemblies(...)` on the `MapRazorComponents` result; `AddInteractiveWebAssemblyRenderMode(options => options.PathPrefix = "/prefix")` via `WebAssemblyComponentsEndpointOptions.PathPrefix`; "explicitly calling `UseBlazorFrameworkFiles` in a Blazor Web App isn't necessary because the API is automatically called when invoking `AddInteractiveWebAssemblyComponents`" ([static-files](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/static-files?view=aspnetcore-10.0)); `RegisterPersistentService<T>(IRazorComponentsBuilder, IComponentRenderMode)`; `builder.Services.AddValidation()` for the new form validation.

### 4.5 What's new for Blazor in .NET 10

All from [aspnetcore-10.0 release notes](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0) unless noted.

**Template / project model**

1. New and updated Blazor Web App security samples (OIDC, Entra ID, Windows Auth), each now with a separate `MinimalApiJwt` web API project; Entra guidance adds an encrypted distributed token cache and Azure Key Vault + Managed Identities for data protection.
2. **`ReconnectModal` reconnection UI in the template** — collocated `.css` and `.js`; "The component doesn't inject styles programmatically, ensuring strict Content Security Policy (CSP) `style-src` compliance." New `components-reconnect-state-changed` event and a new `retrying` state. CSS classes on `#components-reconnect-modal`: `components-reconnect-show`, `-paused`, `-hide`, `-retrying`, `-failed`, `-rejected`. The handler calls `Blazor.reconnect()` on `failed` and `location.reload()` on `rejected`. Optional elements `#components-reconnect-max-retries`, `#components-reconnect-current-attempt`, `#components-seconds-to-next-attempt`. Configuration: `Blazor.start({ circuit: { reconnectionOptions: { maxRetries, retryIntervalMilliseconds } } })` ([blazor/fundamentals/signalr](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/signalr?view=aspnetcore-10.0)).
3. **`NotFound.razor` included in the template by default.**
4. `<BlazorDisableThrowNavigationException>` set to `true` by default in the template.
5. `<ResourcePreloader />` adopted by the template by default.
6. **Passkey (WebAuthn) support in ASP.NET Core Identity** — "The Blazor Web App project template provides out-of-the-box passkey management and login functionality." See §6.
7. PWA service-worker registration now uses `navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' })`.

**State persistence**

8. **Declarative state persistence** — the `[PersistentState]` attribute on `public` properties, persisted via `PersistentComponentState` during prerendering and restored when the component renders interactively; `RegisterPersistentService` for services, which "requires a render mode because the render mode can't be inferred from the service type." Critical constraint: **"Only persisting scoped services is supported on the server. On the WebAssembly client (`InteractiveAuto` or `InteractiveWebAssembly` render modes), the service must be registered as a singleton."** A scoped client registration throws `DirectScopedResolvedFromRootException` in Development. ([prerendered-state-persistence](https://learn.microsoft.com/en-us/aspnet/core/blazor/state-management/prerendered-state-persistence?view=aspnetcore-10.0))
9. **Persistent state across enhanced navigation** — `[PersistentState(AllowUpdates = true)]`; `RestoreBehavior.SkipInitialValue`; `RestoreBehavior.SkipLastSnapshot`; `PersistentComponentState.RegisterOnRestoring`. (LOCAL: `PersistentStateAttribute.AllowUpdates`, `.RestoreBehavior`, and `RestoreBehavior` values `Default`/`SkipInitialValue`/`SkipLastSnapshot` all confirmed in the installed 10.0.10 `Microsoft.AspNetCore.Components.xml`.)
10. **Serialization extensibility** — `PersistentComponentStateSerializer<T>` with `Persist(T, IBufferWriter<byte>)` / `Restore(ReadOnlySequence<byte>)`, registered as a singleton. "Without a registered custom serializer, serialization falls back to JSON."
11. **Circuit state persistence** — "Blazor Web Apps can now persist a user's session (circuit) state when the server connection is lost for an extended period or proactively paused, as long as a full-page refresh isn't triggered", covering tab throttling, mobile app switching, network interruptions, proactive pausing of inactive circuits, and enhanced navigation ([state-management/server](https://learn.microsoft.com/en-us/aspnet/core/blazor/state-management/server?view=aspnetcore-10.0)).

**Static assets, script, boot**

12. **Framework static assets preloaded** via `Link` headers in Blazor Web Apps. Standalone WebAssembly requires `<OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>` plus `<link rel="preload" id="webassembly" />`.
13. **`<ResourcePreloader />`** — "now replaces `<link>` headers for preloading WebAssembly assets in Blazor Web Apps. This permits correct app base path configuration (`<base href="..." />`)." Place it after the `<base>` tag. "Removing the component disables preloading for apps using a `loadBootResource` callback."
14. **Blazor script is now a static web asset** — "served as a static web asset with automatic compression and fingerprinting instead of from an embedded resource." Included automatically if the project has ≥1 `.razor` file; otherwise set `<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>`.
15. Client-side fingerprinting for standalone WebAssembly — `<script type="importmap">`, `_framework/blazor.webassembly#[.{fingerprint}].js`, `#[.{fingerprint}]` markers on developer JS, `<StaticWebAssetFingerprintPattern Include="JSModule" Pattern="*.js" Expression="#[.{fingerprint}]!" />`.
16. **`blazor.boot.json` inlined into `dotnet.js`.** The release notes state "No documented replacement strategy" for integrity-check scripts and DLL-extension renaming.
17. JavaScript bundler support — `<WasmBundlerFriendlyBootConfig>true</WasmBundlerFriendlyBootConfig>`.
18. **Custom Blazor cache and the `BlazorCacheBootResources` MSBuild property removed** — "since all client-side files are fingerprinted and cached by the browser."
19. `@Assets["…"]` (via `ComponentBase.Assets` + `MapStaticAssets`) and `<ImportMap />` (`ImportMap`, `ImportMapDefinition`, `ImportMapDefinition.FromResourceCollection`, `ResourceAssetCollection`). `@Assets["{PATH}"]` is the documented `href` form for .NET 9+; .NET 8.x used plain `{PATH}`.

**Navigation / routing / Not Found**

20. `NavigateTo` no longer scrolls to the top for same-page navigations — "The viewport is preserved when updating the address for the current page (changing query string or fragment)."
21. `<BlazorDisableThrowNavigationException>` opts out of the `NavigationException` from `NavigationManager.NavigateTo` during static SSR. Template default `true`. Identity UI's `IdentityRedirectManager` should drop its post-`RedirectTo` `InvalidOperationException` and `[DoesNotReturn]` attributes.
22. **`Router.NotFoundPage` parameter** — `<Router … NotFoundPage="typeof(Pages.NotFound)">`. **"The `NotFound` render fragment is not supported in .NET 10 or later."**
23. **`NavigationManager.NotFound()`** — static SSR sets HTTP 404; interactive rendering signals the router; streaming rendering with enhanced navigation renders Not Found content without a page reload (otherwise a redirect with page refresh). Priority: (1) `NotFoundEventArgs.Path`, (2) `Router.NotFoundPage`, (3) status-code-pages re-execution middleware page, (4) no action. `DefaultNotFound` (plain-text "Not found") has no route and can't be used during streaming rendering. `NavigationManager.OnNotFound` gives notification.
24. Not Found support without Blazor's router — `app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);` (which the template ships), or subscribe to `OnNotFound` and set `NotFoundEventArgs.Path`. "If both approaches are used, the Not Found path in `OnNotFoundEvent` takes precedence over the re-execution middleware path."
25. **`NavLinkMatch.All` now ignores query string and fragment** — revert with the `Microsoft.AspNetCore.Components.Routing.NavLink.EnableMatchAllForQueryStringAndFragment` AppContext switch, or override `NavLink.ShouldMatch(string currentUriAbsolute)`.
26. `[Route]` now supports route syntax highlighting.

**Components / forms / QuickGrid**

27. **QuickGrid `RowClass`** parameter — per-row CSS class ([quickgrid](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/quickgrid?view=aspnetcore-10.0)).
28. **QuickGrid `HideColumnOptionsAsync`** — closes the column options UI (same source).
29. **Improved form validation** — validates nested object properties and collection items. Opt-in requires `builder.Services.AddValidation();`, **form model types declared in a `.cs` file (not `.razor`)**, and `[ValidatableType]` on the root model. Adds `[SkipValidation]`, source-generator-based (AOT-friendly) validation, and aligns `DataAnnotationsValidator` order/short-circuiting with `System.ComponentModel.DataAnnotations.Validator` (members → type-level attributes → `IValidatableObject.Validate`, skipping later steps on error). Constraint: **"Both the validation feature and Razor compiler use source generators. Currently, one source generator's output can't be used as another's input."**
30. Validation models from another assembly — create a method taking `IServiceCollection` that calls `AddValidation`, then call both ([validation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/validation?view=aspnetcore-10.0)).
31. New **`InputHidden`** component — hidden input for string values, used with `@bind-Value`.
32. **`OwningComponentBase` now implements `IAsyncDisposable`** — new `DisposeAsync`/`DisposeAsyncCore` plus an updated `Dispose`. Tracked as a breaking change in [dotnet/aspnetcore#62583](https://github.com/dotnet/aspnetcore/pull/62583) (milestone 10.0-preview7).

**JS interop**

33. Async on `IJSRuntime`/`IJSObjectReference`: `InvokeConstructorAsync(string identifier, object?[]? args)`, `GetValueAsync<TValue>(string identifier)`, `SetValueAsync<TValue>(string identifier, TValue value)` (overloads take `CancellationToken` or `TimeSpan`). Sync on `IJSInProcessRuntime`/`IJSInProcessObjectReference`: `InvokeConstructor`, `GetValue<TValue>`, `SetValue<TValue>`.

**WebAssembly runtime / HTTP / environment / diagnostics**

34. **`HttpClient` response streaming enabled by default** — flagged in the release notes as a **breaking change**: `HttpContent.ReadAsStreamAsync` now returns `BrowserHttpReadStream` instead of `MemoryStream`, and "`BrowserHttpReadStream` doesn't support synchronous operations like `Stream.Read(Span<Byte>)`." Opt out globally with `<WasmEnableStreamingResponse>false</WasmEnableStreamingResponse>` or `DOTNET_WASM_ENABLE_STREAMING_RESPONSE=false|0`; per request with `requestMessage.SetBrowserResponseStreamingEnabled(false)`. Runtime-level entry: [default-http-streaming](https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/10.0/default-http-streaming).
35. **Environment in standalone Blazor WebAssembly** — the `Blazor-Environment` header and `launchSettings.json` / `ASPNETCORE_ENVIRONMENT` "no longer control environment in standalone Blazor WebAssembly apps." Use `<WasmApplicationEnvironmentName>`. Defaults: `Development` for build, `Production` for publish.
36. Blazor WebAssembly respects the current UI culture — globalization data is now additionally loaded for `CultureInfo.DefaultThreadCurrentUICulture` (in .NET 9 and earlier only `DefaultThreadCurrentCulture` was used).
37. WebAssembly performance profiling and diagnostic counters ([browser dev tools](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/webassembly-browser-developer-tools-diagnostics?view=aspnetcore-10.0), [event pipe](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/webassembly-event-pipe-diagnostics?view=aspnetcore-10.0)).
38. **Metrics and tracing** — "comprehensive metrics and tracing for Blazor apps, providing observability of component lifecycle, navigation, event handling, and circuit management" ([performance](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/?view=aspnetcore-10.0)). **Relevant to PRD 49.**
39. Hot Reload for Blazor WebAssembly — new `WasmEnableHotReload` MSBuild property, default `true` for `Debug`.

**Blazor Hybrid**

40. New article and `MauiBlazorWebIdentity` sample: ".NET MAUI Blazor Hybrid and Web App with ASP.NET Core Identity."

New render-mode-adjacent APIs in .NET 10: `Router.NotFoundPage`, `NavigationManager.NotFound()`/`OnNotFound`/`NotFoundEventArgs.Path`, `RegisterPersistentService<T>`, `ResourcePreloader`, `InputHidden`, `PersistentComponentStateSerializer<T>`, `PersistentComponentState.RegisterOnRestoring`, `PersistentStateAttribute.AllowUpdates`/`.RestoreBehavior`, `RestoreBehavior`. **No new members were added to `RenderMode`** (LOCAL: still exactly three properties in 10.0.10).

### 4.6 Blazor package versions

All **10.0.12** (highest non-prerelease 10.x, flat-container API, 2026-09-10):

| Package id | Stable | Needs `PackageReference`? |
|---|---|---|
| `Microsoft.AspNetCore.Components.WebAssembly` | 10.0.12 | **Yes** |
| `Microsoft.AspNetCore.Components.WebAssembly.Server` | 10.0.12 | **Yes** (server project, Auto/WASM modes) |
| `Microsoft.AspNetCore.Components.WebAssembly.DevServer` | 10.0.12 | **Yes** (`blazorwasm` only, `PrivateAssets="all"`) |
| `Microsoft.AspNetCore.Components.WebAssembly.Authentication` | 10.0.12 | **Yes** |
| `Microsoft.AspNetCore.Components.QuickGrid` | 10.0.12 | **Yes** |
| `Microsoft.AspNetCore.Components.QuickGrid.EntityFrameworkAdapter` | 10.0.12 | **Yes** |
| `Microsoft.AspNetCore.Components.Authorization` | 10.0.12 | **No** — shared framework |
| `Microsoft.AspNetCore.Components.Web` | 10.0.12 | **No** — shared framework |

The QuickGrid doc's own instruction: "Confirm correct package versions at NuGet.org." Note that `Microsoft.AspNetCore.Components.WebAssembly.Server`'s namespace reference says it is "intended for framework use only, not supported for use in application code" ([API ref](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.components.webassembly.server)) — yet the Blazor Web App template adds it as an explicit `PackageReference` (LOCAL). See [Could Not Verify](#could-not-verify).

### 4.7 Blazor breaking changes

**Verified negative:** the ASP.NET Core 10 breaking-changes list (§3.5) contains **no Blazor-specific entries**, and the .NET 10 index defers ASP.NET Core entirely.

The one entry on the .NET 10 index that directly affects Blazor WebAssembly is under **Networking**: "Streaming HTTP responses enabled by default in browser HTTP clients" ([detail](https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/10.0/default-http-streaming)) — item 34 above.

Behaviour/API changes documented in the Blazor release notes as breaking-in-effect but listed on **neither** breaking-changes page: `<NotFound>` render fragment unsupported (item 22); `NavLinkMatch.All` semantics (item 25); `NavigateTo` scroll behavior (item 20); `BlazorCacheBootResources` removed (item 18); `blazor.boot.json` no longer a separate file (item 16); `Blazor-Environment` / `ASPNETCORE_ENVIRONMENT` no longer setting the standalone WebAssembly environment (item 35).

---

## 5. SignalR in .NET 10

**Verified negative: SignalR received no documented feature changes, no public API changes, and no protocol changes in .NET 10.** Four independent checks:

| Check | Result | Source |
|---|---|---|
| Release-notes SignalR section | Contains only "This section describes new features for SignalR." and **lists no features** — the heading is followed by that one line and no `[!INCLUDE]` entries | [doc source, lines 26-28](https://raw.githubusercontent.com/dotnet/AspNetCore.Docs/live/aspnetcore/release-notes/aspnetcore-10.0.md) |
| .NET 10 GA API diff | "API difference between .NET 9.0 GA and .NET 10.0 GA" for `Microsoft.AspNetCore.App` — the assembly index contains **no SignalR assembly** (no `Microsoft.AspNetCore.SignalR*`, no `Microsoft.AspNetCore.Http.Connections*`); the directory holds 35 files, none SignalR. Per-preview api-diffs (preview1, preview4, rc1) likewise contain no SignalR files. | [api-diff 10.0.0.md](https://github.com/dotnet/core/blob/main/release-notes/10.0/api-diff/Microsoft.AspNetCore.App/10.0.0.md), [directory](https://api.github.com/repos/dotnet/core/contents/release-notes/10.0/api-diff/Microsoft.AspNetCore.App) |
| Hub Protocol spec | **Byte-for-byte identical** between the 9.0 and 10.0 release branches (md5 `89a2481470bed1a9897ed1095dfdd696` for both) | [10.0 HubProtocol.md](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/SignalR/docs/specs/HubProtocol.md) vs [9.0](https://github.com/dotnet/aspnetcore/blob/release/9.0/src/SignalR/docs/specs/HubProtocol.md) |
| .NET 10 announcement blog | Does not mention SignalR | [announcing-dotnet-10](https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/) |

Handshake protocol version: the spec states `version` "must always be 1, for both MessagePack and Json protocols."

### 5.1 The one SignalR-affecting change in .NET 10

SignalR endpoints automatically receive `IApiEndpointMetadata`, so **unauthenticated/unauthorized cookie-authenticated requests to SignalR endpoints now return 401/403 instead of redirecting** to login/access-denied ([cookie-authentication-api-endpoints](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/cookie-authentication-api-endpoints)). **Directly relevant to PRD 10 / 10A (SignalR agent control plane) combined with PRD 11's preference for HttpOnly cookies for browser auth.**

### 5.2 SignalR facts (aspnetcore-10.0 moniker)

From [signalr/introduction](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction):

- Features listed for .NET 10: automatic connection management; broadcast to all clients; send to specific clients or groups; scale with Azure SignalR Service and the Redis backplane; "Support trimming and native ahead-of-time (AOT) compilation for supported scenarios"; "Support polymorphic type handling in hub methods"; **"Support distributed tracing with `ActivitySource` for SignalR hub server and .NET client."**
- Transports, in graceful-fallback order: **WebSockets, Server-sent events, Long polling.** "WebSockets is the preferred transport."
- Two built-in hub protocols: JSON text (default) and MessagePack binary (`Microsoft.AspNetCore.SignalR.Protocols.MessagePack`).

From [signalr/version-differences](https://learn.microsoft.com/en-us/aspnet/core/signalr/version-differences) — note this page is titled "Differences between SignalR and ASP.NET Core SignalR" and covers ASP.NET SignalR vs ASP.NET Core SignalR, **not** client/server version compatibility; its `ms.date` is 2019-11-21:

- "ASP.NET Core SignalR isn't compatible with clients or servers for ASP.NET SignalR."
- Server NuGet package: "None. Included in the `Microsoft.AspNetCore.App` shared framework."
- Client packages: `Microsoft.AspNetCore.SignalR.Client` (.NET), `@microsoft/signalr` (npm), `com.microsoft.signalr` (Maven).
- **Automatic reconnects are opt-in** (`WithAutomaticReconnect()` / `withAutomaticReconnect()`).
- **Sticky sessions are required for Redis scaleout** but not for Azure SignalR Service.
- Single hub per connection; no `HubState`, `PersistentConnection`, `GlobalHost`, or `HubPipeline`; hub proxies are not auto-generated; Forever Frame transport isn't supported.

From [signalr/supported-platforms](https://learn.microsoft.com/en-us/aspnet/core/signalr/supported-platforms):

- Server: "SignalR for ASP.NET Core supports any server platform that ASP.NET Core supports."
- JavaScript client: "runs on the current Node.js long-term support (LTS) release" plus current Safari (incl. iOS), Chrome (incl. Android), Edge, Firefox. No Internet Explorer. Targets ES6.
- .NET client: "runs on any platform supported by ASP.NET Core." WebSockets on IIS requires "IIS 8.0 or later on Windows Server 2012 or later."
- Java client "Java 8 or later"; Swift client "Swift >= 5.10"; **the C++ client is "available for experimentation only, isn't currently supported."**

**LOCAL — server-side options confirmed in the 10.0.10 targeting pack:** `HubOptions.KeepAliveInterval`, `HubOptions.StatefulReconnectBufferSize`, `HubConnectionContextOptions.KeepAliveInterval`, `HubConnectionContextOptions.StatefulReconnectBufferSize`. Stateful reconnect remains available and unchanged.

### 5.3 JavaScript client version

From the npm registry ([`@microsoft/signalr`](https://registry.npmjs.org/@microsoft/signalr), fetched with the npm install-v1 accept header):

- `dist-tags.latest` = **`10.0.11`**
- `dist-tags["9.0.x"]` = `9.0.19`; `dist-tags["8.0.x"]` = `8.0.29`; `dist-tags.next` = `11.0.0-preview.7.26381.103`

**The JS client's `latest` (10.0.11) is one patch behind the .NET packages (10.0.12).** PRD 4 minimizes custom JavaScript, so this may not bind, but it is a real version-alignment fact.

---

## 6. ASP.NET Core Identity, and passkeys / WebAuthn

### 6.1 Package ids and versions

Highest non-prerelease 10.x from the flat-container API, 2026-09-10:

| Package id | Stable | Shared framework? |
|---|---|---|
| `Microsoft.AspNetCore.Identity` | **no 10.x exists — highest of any kind is 2.3.13** | assembly is in the shared framework |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | **10.0.12** | No — `PackageReference` required |
| `Microsoft.AspNetCore.Identity.UI` | **10.0.12** | No — `PackageReference` required |
| `Microsoft.Extensions.Identity.Core` | **10.0.12** | **Yes** |
| `Microsoft.Extensions.Identity.Stores` | **10.0.12** | **Yes** |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | **10.0.12** | No — `PackageReference` required |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | **10.0.12** | No — `PackageReference` required |
| `Microsoft.EntityFrameworkCore` | **10.0.12** | No |
| `Microsoft.EntityFrameworkCore.Design` | **10.0.12** | No |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | **10.0.3** | No |

Sources: [Identity](https://api.nuget.org/v3-flatcontainer/microsoft.aspnetcore.identity/index.json), [Identity.EntityFrameworkCore](https://api.nuget.org/v3-flatcontainer/microsoft.aspnetcore.identity.entityframeworkcore/index.json), [Identity.UI](https://api.nuget.org/v3-flatcontainer/microsoft.aspnetcore.identity.ui/index.json), [Extensions.Identity.Core](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.identity.core/index.json), [Npgsql EF provider](https://api.nuget.org/v3-flatcontainer/npgsql.entityframeworkcore.postgresql/index.json).

**Trap worth naming explicitly.** The NuGet package id `Microsoft.AspNetCore.Identity` has **no 10.x version** — its version list jumps 1.1.6 → 2.x and stops at **2.3.13** (published 2026-09-08 per [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Identity/)). The *assembly* `Microsoft.AspNetCore.Identity.dll` ships in the `Microsoft.AspNetCore.App` shared framework (LOCAL: present in the 10.0.10 targeting pack). A `PackageReference` to `Microsoft.AspNetCore.Identity` on a `net10.0` project therefore pulls a legacy 2.x package, not the framework Identity. The Learn API reference confirms the framework placement per type — `IdentityPasskeyOptions` lists "Package: Microsoft.AspNetCore.App.Ref v10.0.0" and assembly `Microsoft.AspNetCore.Identity.dll` ([API ref](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.identitypasskeyoptions?view=aspnetcore-10.0)).

Version-train note: `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3** (published 2026-07-10) is on its own cadence and depends on `Microsoft.EntityFrameworkCore.Relational (>= 10.0.4 && < 11.0.0)`; 11.0.0-preview releases exist ([nuget.org](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/)). Detailed EF/Npgsql verification is out of scope for this ticket (PRD 62 lists them separately).

### 6.2 Passkey / WebAuthn support — GA in .NET 10

**Status: shipped stable (GA) in .NET 10, in the shared framework.** Announced in the release notes under "Web Authentication API (passkey) support for ASP.NET Core Identity": *"ASP.NET Core Identity now supports passkey authentication based on WebAuthn and FIDO2 standards… The Blazor Web App project template provides out-of-the-box passkey management and login functionality."* ([release notes](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0); [doc source](https://github.com/dotnet/AspNetCore.Docs/blob/live/aspnetcore/release-notes/aspnetcore-10/includes/blazor.md)). The section sits inside the **Blazor** section of the release notes, not Authentication.

Dedicated docs, both carrying monikers `aspnetcore-10.0` **and** `aspnetcore-11.0`:

- [Enable Web Authentication API (WebAuthn) passkeys](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/)
- [Implement passkeys in ASP.NET Core Blazor Web Apps](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/blazor)

Stated prerequisite: ".NET SDK (.NET 10 or later)".

**Verified negative:** the main [Introduction to Identity on ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0) page (`view=aspnetcore-10.0`) contains **zero** occurrences of "passkey" or "WebAuthn". Passkey content lives only under `security/authentication/passkeys/*`.

#### Public API surface

Verified from `PublicAPI` files at tag `v10.0.0` in `dotnet/aspnetcore`, and independently confirmed against the installed 10.0.10 reference assemblies (LOCAL).

`SignInManager<TUser>` ([source](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Core/src/PublicAPI.Unshipped.txt)):

```
MakePasskeyCreationOptionsAsync(PasskeyUserEntity)  -> Task<string>
MakePasskeyRequestOptionsAsync(TUser?)              -> Task<string>
PerformPasskeyAttestationAsync(string credentialJson) -> Task<PasskeyAttestationResult>
PerformPasskeyAssertionAsync(string credentialJson)   -> Task<PasskeyAssertionResult<TUser>>
PasskeySignInAsync(string credentialJson)             -> Task<SignInResult>
```

`UserManager<TUser>` ([source](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Extensions.Core/src/PublicAPI.Unshipped.txt)):

```
SupportsUserPasskey -> bool
AddOrUpdatePasskeyAsync(TUser, UserPasskeyInfo) -> Task<IdentityResult>
GetPasskeyAsync(TUser, byte[] credentialId)     -> Task<UserPasskeyInfo?>
GetPasskeysAsync(TUser)                         -> Task<IList<UserPasskeyInfo>>
FindByPasskeyIdAsync(byte[] credentialId)       -> Task<TUser?>
RemovePasskeyAsync(TUser, byte[] credentialId)  -> Task<IdentityResult>
```

Store contract `IUserPasskeyStore<TUser>`: `AddOrUpdatePasskeyAsync`, `FindByPasskeyIdAsync`, `FindPasskeyAsync`, `GetPasskeysAsync`, `RemovePasskeyAsync`.

`UserPasskeyInfo` properties: `CredentialId`, `PublicKey`, `CreatedAt`, `SignCount` (settable), `Transports`, `IsUserVerified` (settable), `IsBackupEligible`, `IsBackedUp` (settable), `Name` (settable), `AttestationObject`, `ClientDataJson`.

Handler and supporting types in `Microsoft.AspNetCore.Identity`: `IPasskeyHandler<TUser>` (`MakeCreationOptionsAsync`, `MakeRequestOptionsAsync`, `PerformAttestationAsync`, `PerformAssertionAsync`), default implementation `PasskeyHandler<TUser>`, plus `PasskeyCreationOptionsResult` (`CreationOptionsJson`, `AttestationState`), `PasskeyRequestOptionsResult` (`RequestOptionsJson`, `AssertionState`), `PasskeyAttestationContext`, `PasskeyAssertionContext`, `PasskeyAttestationResult`, `PasskeyAssertionResult<TUser>`, `PasskeyUserEntity` (`Id`, `Name`, `DisplayName`), `PasskeyOriginValidationContext` (`Origin`, `TopOrigin`, `CrossOrigin`, `HttpContext`), `PasskeyAttestationStatementVerificationContext`, `PasskeyException`.

**Naming correction:** the types are `PasskeyCreationOptionsResult` / `PasskeyRequestOptionsResult`. There is **no** public `PasskeyCreationOptions` or `PasskeyRequestOptions` type (verified negative).

`IdentityPasskeyOptions` — all **10** public properties. Verified from [source](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Core/src/PublicAPI.Unshipped.txt), the [Learn API ref](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.identitypasskeyoptions?view=aspnetcore-10.0), and LOCAL XML docs from the installed 10.0.10 ref pack:

| Property | Type | Default / documented behavior when unset |
|---|---|---|
| `ServerDomain` | `string?` | "If left `null`, the server's origin may be used instead." References [WebAuthn L3 RP ID](https://www.w3.org/TR/webauthn-3/#rp-id) |
| `ChallengeSize` | `int` | 32 bytes |
| `AuthenticatorTimeout` | `TimeSpan` | 5 minutes |
| `AuthenticatorAttachment` | `string?` | — |
| `ResidentKeyRequirement` | `string?` | Controls discoverability / username-less auth |
| `UserVerificationRequirement` | `string?` | — |
| `AttestationConveyancePreference` | `string?` | — |
| `IsAllowedAlgorithm` | `Func<int,bool>?` | "If left `null`, all supported algorithms are allowed." Applies only when creating a new passkey. References the [IANA COSE algorithms registry](https://www.iana.org/assignments/cose/cose.xhtml#algorithms) |
| `ValidateOrigin` | `Func<PasskeyOriginValidationContext, ValueTask<bool>>?` | "If left `null`, cross-origin requests are disallowed, and the request is only considered valid if the request's origin header matches the credential's origin." |
| `VerifyAttestationStatement` | `Func<PasskeyAttestationStatementVerificationContext, ValueTask<bool>>?` | **"If left `null`, this function does not perform any verification and always returns `true`."** Applies only when creating a new passkey |

#### EF Core schema addition

- Entity type: `IdentityUserPasskey<TKey> where TKey : IEquatable<TKey>`, namespace **`Microsoft.AspNetCore.Identity`** (not `...EntityFrameworkCore`), assembly `Microsoft.Extensions.Identity.Stores`. Properties: `UserId` (`TKey`), `CredentialId` (`byte[]`), `Data` (`IdentityPasskeyData`). ([source](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Extensions.Stores/src/IdentityUserPasskey.cs))
- `IdentityPasskeyData`: `PublicKey`, `Name`, `CreatedAt`, `SignCount`, `Transports`, `IsUserVerified`, `IsBackupEligible`, `IsBackedUp`, `AttestationObject`, `ClientDataJson`. ([source](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Extensions.Stores/src/IdentityPasskeyData.cs))
- Table **`AspNetUserPasskeys`**; PK is `CredentialId` with `HasMaxLength(1024)` ("Defined in WebAuthn spec to be no longer than 1023 bytes"); `Data` is mapped as an owned type serialized to a JSON column via `b.OwnsOne(p => p.Data).ToJson()`; required cascade FK from `AspNetUsers`. ([`IdentityUserContext.OnModelCreatingVersion3`](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/EntityFrameworkCore/src/IdentityUserContext.cs))
- New `DbSet`: `public virtual DbSet<TUserPasskey> UserPasskeys { get; set; }`; new 6- and 9-type-parameter generic overloads of `IdentityUserContext` / `IdentityDbContext` / `UserStore` / `UserOnlyStore` taking `TUserPasskey`.

**LOCAL confirmation** — the generated migration in `dotnet new blazor -au Individual` (SDK 10.0.302) creates:

```csharp
migrationBuilder.CreateTable(
    name: "AspNetUserPasskeys",
    columns: table => new
    {
        CredentialId = table.Column<byte[]>(type: "BLOB", maxLength: 1024, nullable: false),
        UserId = table.Column<string>(type: "TEXT", nullable: false),
        Data = table.Column<string>(type: "TEXT", nullable: false)
    },
    …PrimaryKey("PK_AspNetUserPasskeys", x => x.CredentialId)
    …ForeignKey("FK_AspNetUserPasskeys_AspNetUsers_UserId", … onDelete: ReferentialAction.Cascade));
// plus CreateIndex("IX_AspNetUserPasskeys_UserId", …)
```

#### The schema is opt-in and gated

**The passkey table is not created by default.** `Stores.SchemaVersion` defaults to `IdentitySchemaVersions.Version1`, and versions 1 and 2 call `builder.Ignore<TUserPasskey>()`. Only `schemaVersion >= IdentitySchemaVersions.Version3` runs `OnModelCreatingVersion3`, which maps `AspNetUserPasskeys`. ([source](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/EntityFrameworkCore/src/IdentityUserContext.cs))

`IdentitySchemaVersions.Version3` is a `static readonly System.Version` in `Microsoft.Extensions.Identity.Core`. The class dates to 8.0 but the `Version3` field's Applies-to is `aspnetcore-10.0` / `11.0` only ([API ref](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.identityschemaversions.version3?view=aspnetcore-10.0)). LOCAL: the installed 10.0.10 `Microsoft.Extensions.Identity.Core.dll` ref assembly contains `IdentitySchemaVersions` with `Version1`, `Version2`, `Version3`.

The opt-in, verbatim from the LOCAL-generated template `Program.cs` (identical to the [Blazor passkeys doc](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/blazor)):

```csharp
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
```

**A migration IS required for an existing app:** `dotnet ef migrations add AddPasskeySupport` then `dotnet ef database update` (same doc).

#### Template support

**Only the Blazor Web App template ships passkey support:** *"Currently, only the Blazor Web App project template includes built-in passkey support."*; listed as a limitation: *"Template support: Only the Blazor Web App template includes passkey support."* ([Blazor passkeys doc](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/blazor), [passkeys doc](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/)). **Verified negative:** the Razor Pages / MVC Identity UI (`Microsoft.AspNetCore.Identity.UI`) is not listed as having passkey support.

**LOCAL — `dotnet new blazor --auth Individual` on SDK 10.0.302 scaffolded exactly these passkey artifacts:**

```
Components/Account/PasskeyInputModel.cs          (CredentialJson, Error)
Components/Account/PasskeyOperation.cs           (enum: Create = 0, Request = 1)
Components/Account/Shared/PasskeySubmit.razor
Components/Account/Shared/PasskeySubmit.razor.js
Components/Account/Pages/Manage/Passkeys.razor
Components/Account/Pages/Manage/RenamePasskey.razor
Components/Account/Pages/Login.razor             (passkey branch in LoginUser)
Components/Account/IdentityComponentsEndpointRouteBuilderExtensions.cs
  -> accountGroup.MapPost("/PasskeyCreationOptions", …)  calls MakePasskeyCreationOptionsAsync
  -> accountGroup.MapPost("/PasskeyRequestOptions",  …)  calls MakePasskeyRequestOptionsAsync
```

Call sites observed (LOCAL): `SignInManager.PasskeySignInAsync` (Login.razor), `SignInManager.PerformPasskeyAttestationAsync` + `UserManager.AddOrUpdatePasskeyAsync` + `UserManager.GetPasskeysAsync` + `UserManager.RemovePasskeyAsync` (Manage/Passkeys.razor), `UserManager.GetPasskeyAsync` + `UserManager.AddOrUpdatePasskeyAsync` (Manage/RenamePasskey.razor). App-level concerns present in the template: `MaxPasskeyCount`, `browserSupportsPasskeys`, `tryAutofillPasskey`.

`App.razor` loads the passkey module explicitly: `<script src="@Assets["Components/Account/Shared/PasskeySubmit.razor.js"]" type="module"></script>`.

`Components/Account/PasskeyAuthenticators.cs` (AAGUID → friendly authenticator name) is marked `moniker range="aspnetcore-11.0"` — **not in .NET 10**.

#### `MapIdentityApi` has no passkey endpoints — verified negative

Grepping [`IdentityApiEndpointRouteBuilderExtensions.cs` at v10.0.0](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Core/src/IdentityApiEndpointRouteBuilderExtensions.cs) yields **zero** occurrences of "passkey". The mapped routes are exactly `/register`, `/login`, `/refresh`, `/confirmEmail`, `/resendConfirmationEmail`, `/forgotPassword`, `/resetPassword`. **The passkey HTTP endpoints in the docs and template are app-authored, not framework-provided.**

#### WebAuthn feature coverage

Supported scenarios ([passkeys doc](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/)): adding passkeys to existing password accounts; passwordless account creation; passwordless sign-in.

**Algorithms** — **9** COSE algorithms, in this preference order: `ES256`, `PS256`, `ES384`, `PS384`, `PS512`, `RS256`, `ES512`, `RS384`, `RS512` ([`CredentialPublicKey.AllSupportedParameters`](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Core/src/Passkeys/CredentialPublicKey.cs)). Anything else throws `PasskeyException.UnsupportedCredentialPublicKeyAlgorithm()` ([`PasskeyHandler.cs`](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Core/src/PasskeyHandler.cs)). **Notably no Ed25519/EdDSA (-8).** `IsAllowedAlgorithm` can only narrow this set.

**Attestation** — parsed and stored, but **not validated by default**: *"No default attestation validation: The implementation doesn't validate attestation statements by default"* and *"By default, ASP.NET Core Identity doesn't validate attestation statements."* Custom validation goes through `options.VerifyAttestationStatement`, with the doc's warning that *"Attestation validation is complex and requires maintaining trust stores for authenticator certificates."* The complete attestation object (including AAGUID) is stored per credential. Independently confirmed from the LOCAL XML docs: "If left `null`, this function does not perform any verification and always returns `true`."

**Discoverable credentials** — controlled by `ResidentKeyRequirement`, which *"indicates whether the credential should be discoverable, allowing authentication without first providing a username."*

**Conditional UI / autofill** — supported. `MakePasskeyRequestOptionsAsync(null)` *"generates options suitable for conditional UI or username-less authentication"*; the docs mention *"conditional UI, where passkeys appear as autofill suggestions in the username field"*, and the Blazor guide's step 6 says to *"test the passkey autofill feature."* **LOCAL confirmation** — the scaffolded `PasskeySubmit.razor.js` checks `PublicKeyCredential.isConditionalMediationAvailable?.()` and passes `mediation: 'conditional'` to `navigator.credentials.get({ publicKey: options, mediation, signal })`, with an error branch commented "An error occurred during conditional mediation, which is not user-initiated." (Note: the *published doc's* JS sample is moniker-split and only the 11.0 variant adds the `mediation` parameter; the shipping 10.0 template does have it.)

**Origin validation** — default *"allows requests from subdomains and disallows cross-origin iframes"*; overridable via `options.ValidateOrigin`.

#### Documented limitations and gaps

From the [passkeys doc](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/):

- *"The passkey implementation in ASP.NET Core Identity is deliberately scoped to authentication scenarios. It isn't intended as a general-purpose WebAuthn library."*
- **No built-in 2FA support** — *"Passkeys are treated as a primary authentication factor, not as a second factor."* (Bears directly on PRD 11's MFA requirement being separate from its passkey "desired capability".)
- No default attestation validation (above).
- Template support limited to Blazor Web App.
- **RP ID is inferred from the Host header when `ServerDomain` is unset** — *"The hosting environment must validate host headers to prevent credential-scoping attacks."* A passkey registered on `contoso.com` also works on `*.contoso.com`. (Interacts with PRD 45/46 reverse-proxy deployment and PRD 7A multi-tenancy.)
- **HTTPS is required** for all passkey operations.
- **Attestation state is unprotected by default at the handler layer**: *"The state is serialized as JSON without a signature or encryption, so it provides no integrity protection of its own."* Calling `IPasskeyHandler<TUser>` directly means you must protect it yourself (Data Protection or server-side), enforce single use and expiry, and compare `PasskeyAttestationResult.UserEntity` to the signed-in user — otherwise *"an attacker who alters the user ID in the state completes registration normally."* `SignInManager<TUser>` does this for you via a Data-Protection-protected auth cookie signed out on first read.
- **`PerformPasskeyAssertionAsync` does not persist the updated sign count / authenticator flags** — the caller must invoke `UserManager.AddOrUpdatePasskeyAsync` with the returned result. `PasskeySignInAsync` is the higher-level path that handles it.
- `PasskeySignInAsync` returns `SignInResult.Failed` when state is missing or expired, but **throws `InvalidOperationException`** if called with no preceding `MakePasskeyRequestOptionsAsync`.
- Known browser/password-manager bug: some password managers implement `PublicKeyCredential.toJSON` incorrectly, producing `Error: Could not add a passkey: Illegal invocation`; the doc supplies a manual base64 serialization workaround for `PasskeySubmit.razor.js`.
- Resource limits (max passkeys per user, max display-name length) are **not enforced by the framework** — the template enforces them at app level.
- Account recovery is the app's responsibility for passkey-only authentication.

#### Official guidance on third-party libraries

Explicit. The [passkeys doc](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/) states: *"Developers requiring full WebAuthn functionality should consider community libraries that provide comprehensive protocol support"*, and for attestation: *"Third-party libraries are available for attestation validation, such as the Passkeys - FIDO2 .NET Library (WebAuthn) (`passwordless-lib/fido2-net-lib` GitHub repository)"* — with the caveat that these *"aren't owned or maintained by Microsoft and aren't covered by any Microsoft Support Agreement or license."*

fido2-net-lib current versions (flat container, 2026-09-10) — identical for both ids: `Fido2` and `Fido2.AspNet` at **4.0.1** stable, latest prerelease `5.0.0-preview.3` ([Fido2](https://api.nuget.org/v3-flatcontainer/fido2/index.json), [Fido2.AspNet](https://api.nuget.org/v3-flatcontainer/fido2.aspnet/index.json)).

### 6.3 Identity breaking changes in .NET 10

**Verified negative: no Identity-specific breaking change is listed** on either [Breaking changes in .NET 10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0) or [Breaking changes in ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/overview). Of the 9 ASP.NET Core entries (§3.5), exactly one is auth-related: the cookie login-redirect change.

### 6.4 Other .NET 10 auth/authz changes

**Authentication and authorization metrics** ([metrics/security](https://learn.microsoft.com/en-us/aspnet/core/metrics/security?view=aspnetcore-10.0), [metrics/built-in](https://learn.microsoft.com/en-us/aspnet/core/metrics/built-in?view=aspnetcore-10.0)) — available in ASP.NET Core 10.0 or later:

- Meter `Microsoft.AspNetCore.Authorization`: `aspnetcore.authorization.attempts` (Counter, `{request}`) with attributes `user.is_authenticated`, `aspnetcore.authorization.policy`, `aspnetcore.authorization.result`, `error.type`.
- Meter `Microsoft.AspNetCore.Authentication`: `aspnetcore.authentication.authenticate.duration` (Histogram, `s`), `aspnetcore.authentication.challenges`, `.forbids`, `.sign_ins`, `.sign_outs` (Counters); common attributes `aspnetcore.authentication.scheme`, `error.type`, plus `aspnetcore.authentication.result` on the histogram.

**ASP.NET Core Identity metrics** — new `Microsoft.AspNetCore.Identity` meter ([include](https://github.com/dotnet/AspNetCore.Docs/blob/live/aspnetcore/release-notes/aspnetcore-10/includes/identity-metrics.md)): `aspnetcore.identity.user.create.duration`, `.user.update.duration`, `.user.delete.duration`, `.user.check_password_attempts`, `.user.generated_tokens`, `.user.verify_token_attempts`, `.sign_in.authenticate.duration`, `.sign_in.check_password_attempts`, `.sign_in.sign_ins`, `.sign_in.sign_outs`, `.sign_in.two_factor_clients_remembered`, `.sign_in.two_factor_clients_forgotten`.

These three meters, plus the Blazor metrics/tracing in §4.5 item 38, are the first-party instrumentation available to PRD 49 without any OpenTelemetry instrumentation package.

**`RedirectHttpResult.IsLocalUrl`** — new open-redirect validation helper; a URL is local if it has no host/authority section and has an absolute path (`~/` virtual paths count) ([release notes](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0)).

**EF Core 10 items that touch an Identity model** ([EF Core 10 breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes) — 10 entries, 1 medium and 9 low, plus 3 high-impact `Microsoft.Data.Sqlite` date-handling changes; none Identity-specific):

- **"EF tools now require framework to be specified for multi-targeted projects"** (Medium) — `dotnet ef` needs `--framework` on `<TargetFrameworks>` projects; this affects the `AddPasskeySupport` migration workflow.
- **"SQL Server json data type used by default on Azure SQL and compatibility level 170"** (Low) — because the passkey `Data` column is `OwnsOne(...).ToJson()`, on Azure SQL / compat level ≥ 170 it lands in a `json` column rather than `nvarchar(max)`. Not applicable to PostgreSQL/Npgsql or SQLite.
- Also: parameterized collections now use multiple scalar parameters by default; complex-type column names uniquified; nested complex-type properties use the full path in column names; SQL parameter names simplified (`@city` instead of `@__city_0`).

---

## 7. OpenTelemetry for .NET

### 7.1 Current stable release

**`core-1.18.0`, released 2026-08-21T12:32:00Z, `prerelease=false`** ([opentelemetry-dotnet releases](https://github.com/open-telemetry/opentelemetry-dotnet/releases)). Adjacent tags the same day: `core-1.18.0-rc.1` (11:02:18Z) and `coreunstable-1.18.0-beta.1` (13:52:29Z). Contrib packages shipped 2026-08-21 between 15:03 and 19:04 UTC ([contrib releases](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/releases)). Prior stable releases: `core-1.17.0` (2026-07-16), `core-1.16.0` (2026-06-10).

### 7.2 Package ids and stable-vs-prerelease status

All verified from the NuGet flat-container API on 2026-09-10; "stable" = highest non-prerelease entry.

**Core (`open-telemetry/opentelemetry-dotnet`)**

| Package id | Stable | Latest any | Status |
|---|---|---|---|
| `OpenTelemetry` | **1.18.0** | 1.18.0 | STABLE |
| `OpenTelemetry.Api` | **1.18.0** | 1.18.0 | STABLE |
| `OpenTelemetry.Extensions.Hosting` | **1.18.0** | 1.18.0 | STABLE |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | **1.18.0** | 1.18.0 | STABLE |
| `OpenTelemetry.Exporter.Console` | **1.18.0** | 1.18.0 | STABLE |
| `OpenTelemetry.Exporter.Prometheus.AspNetCore` | **none — never had a stable release** | 1.18.0-beta.1 | **PRERELEASE ONLY** |

**Instrumentation (`open-telemetry/opentelemetry-dotnet-contrib`)**

| Package id | Stable | Latest any | Status |
|---|---|---|---|
| `OpenTelemetry.Instrumentation.AspNetCore` | **1.18.0** | 1.18.0 | STABLE |
| `OpenTelemetry.Instrumentation.Http` | **1.18.0** | 1.18.0 | STABLE |
| `OpenTelemetry.Instrumentation.Runtime` | **1.18.0** | 1.18.0 | STABLE |
| `OpenTelemetry.Instrumentation.SqlClient` | **1.18.0** | 1.18.0 | STABLE |
| `OpenTelemetry.Instrumentation.Process` | **none** | 1.18.0-**rc.1** | PRERELEASE (rc) |
| `OpenTelemetry.Instrumentation.EntityFrameworkCore` | **none** | 1.18.0-**beta.1** | PRERELEASE (beta) |
| `OpenTelemetry.Instrumentation.StackExchangeRedis` | **none** | 1.18.0-**beta.1** | PRERELEASE (beta) |

**Resource detectors — all prerelease except AWS**

| Package id | Stable | Latest any |
|---|---|---|
| `OpenTelemetry.Resources.Container` | none | 1.18.0-beta.1 |
| `OpenTelemetry.Resources.Host` | none | 1.18.0-beta.1 |
| `OpenTelemetry.Resources.Process` | none | 1.18.0-rc.1 |
| `OpenTelemetry.Resources.ProcessRuntime` | none | 1.18.0-beta.1 |
| `OpenTelemetry.Resources.OperatingSystem` | none | 1.18.0-beta.1 |
| `OpenTelemetry.Resources.Azure` | none | 1.18.0-beta.1 |
| `OpenTelemetry.Resources.AWS` | **1.18.0** | 1.18.0 |

Sources: the corresponding flat-container indexes, e.g. [OpenTelemetry](https://api.nuget.org/v3-flatcontainer/opentelemetry/index.json), [Instrumentation.AspNetCore](https://api.nuget.org/v3-flatcontainer/opentelemetry.instrumentation.aspnetcore/index.json), [Exporter.Prometheus.AspNetCore](https://api.nuget.org/v3-flatcontainer/opentelemetry.exporter.prometheus.aspnetcore/index.json), [Instrumentation.EntityFrameworkCore](https://api.nuget.org/v3-flatcontainer/opentelemetry.instrumentation.entityframeworkcore/index.json), [Resources.Container](https://api.nuget.org/v3-flatcontainer/opentelemetry.resources.container/index.json).

**Constraint for PRD 49 (fact, not recommendation):** traces, metrics and structured logs via the core SDK + OTLP exporter are all fully stable at 1.18.0. However, **four capabilities a self-hosted ZWarden would plausibly want are prerelease-only today**: Prometheus scrape endpoint (`Exporter.Prometheus.AspNetCore`, beta, **never had a stable release**), EF Core instrumentation (beta), Redis instrumentation (beta), and process metrics + container/host resource detection (rc/beta). `OpenTelemetry.Instrumentation.SqlClient` **is** now stable (its last five releases — 1.15.1, 1.15.2, 1.16.0, 1.17.0, 1.18.0 — are all non-prerelease), and `Instrumentation.Process` / `Resources.Process` have reached `rc.1`, one step further than the `beta.1` set. Note that the `Exporter.Prometheus.AspNetCore` nuspec shows it is built from the *main* opentelemetry-dotnet repo under the `coreunstable-*` tag line, not from contrib ([nuspec](https://api.nuget.org/v3-flatcontainer/opentelemetry.exporter.prometheus.aspnetcore/1.18.0-beta.1/opentelemetry.exporter.prometheus.aspnetcore.nuspec)).

**Npgsql:** `Npgsql.OpenTelemetry` exists, stable **10.0.3**, shipped by the Npgsql project (`projectUrl: https://github.com/npgsql/npgsql`), **not** by OpenTelemetry. It depends on `Npgsql` 10.0.3 and `OpenTelemetry.API` **1.15.3** — an older OTel API than 1.18.0 — and has a **single target framework, `net8.0` only** (no net9.0/net10.0/netstandard group) ([nuspec](https://api.nuget.org/v3-flatcontainer/npgsql.opentelemetry/10.0.3/npgsql.opentelemetry.nuspec)).

### 7.3 net10.0 compatibility

**Yes — the current stable core packages explicitly target `net10.0`.**

Repo-level definition ([`build/Common.props` at core-1.18.0](https://raw.githubusercontent.com/open-telemetry/opentelemetry-dotnet/core-1.18.0/build/Common.props)):

```
<TargetFrameworksForLibraries>net10.0;net9.0;net8.0;netstandard2.0;net462</TargetFrameworksForLibraries>
<TargetFrameworksForLibrariesExtended>net10.0;net9.0;net8.0;netstandard2.1;netstandard2.0;net462</TargetFrameworksForLibrariesExtended>
<TargetFrameworksForPrometheusAspNetCore>net10.0;net9.0;net8.0</TargetFrameworksForPrometheusAspNetCore>
```

Per-package target frameworks from nuspec dependency groups:

| Package (version) | Target frameworks |
|---|---|
| `OpenTelemetry` 1.18.0 | net10.0, net9.0, net8.0, netstandard2.1, netstandard2.0, net462 |
| `OpenTelemetry.Api` 1.18.0 | net10.0, net9.0, net8.0, netstandard2.0, net462 |
| `OpenTelemetry.Extensions.Hosting` 1.18.0 | net10.0, net9.0, net8.0, netstandard2.0, net462 |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.18.0 | net10.0, net9.0, net8.0, netstandard2.1, netstandard2.0, net462 |
| `OpenTelemetry.Exporter.Console` 1.18.0 | net10.0, net9.0, net8.0, netstandard2.0, net462 |
| `OpenTelemetry.Exporter.Prometheus.AspNetCore` 1.18.0-beta.1 | net10.0, net9.0, net8.0 |
| `OpenTelemetry.Instrumentation.AspNetCore` 1.18.0 | net10.0, net8.0, netstandard2.0 (**no net9.0 group**) |
| `OpenTelemetry.Instrumentation.Http` 1.18.0 | net10.0, net8.0, netstandard2.0, net462 |
| `OpenTelemetry.Instrumentation.Runtime` 1.18.0 | net10.0, net8.0, netstandard2.0, net462 |
| `OpenTelemetry.Instrumentation.SqlClient` 1.18.0 | net10.0, net8.0, netstandard2.0, net462 |
| `OpenTelemetry.Instrumentation.StackExchangeRedis` 1.18.0-beta.1 | net10.0, net8.0, netstandard2.0, net462 |
| `OpenTelemetry.Instrumentation.EntityFrameworkCore` 1.18.0-beta.1 | net10.0, net8.0, netstandard2.0 |
| `Npgsql.OpenTelemetry` 10.0.3 | **net8.0 only** |

Independently confirmed via the registration API: `OpenTelemetry` 1.18.0 declares dependency groups for `net10.0`, `.NETFramework4.6.2`, `net8.0`, `net9.0`, `.NETStandard2.0`, `.NETStandard2.1` ([registration](https://api.nuget.org/v3/registration5-gz-semver2/opentelemetry/index.json)).

Dependency-version detail from the `OpenTelemetry` 1.18.0 nuspec: the `net10.0` group depends on `Microsoft.Extensions.Configuration.EnvironmentVariables` **10.0.0**, `Microsoft.Extensions.Diagnostics.Abstractions` **10.0.0**, `Microsoft.Extensions.Logging.Configuration` **10.0.0**, and `OpenTelemetry.Api.ProviderBuilderExtensions` 1.18.0. The `net8.0` group pins those at 8.0.0; `net9.0` at 9.0.0; the netstandard and net462 groups at 10.0.0. From the `OpenTelemetry.Api` 1.18.0 nuspec: the `net10.0` dependency group is **empty** (`<group targetFramework="net10.0" />`) whereas net9.0/net8.0/netstandard2.0/net462 each depend on `System.Diagnostics.DiagnosticSource` 10.0.0 — i.e. on net10.0 that type is in-box.

Documented support policy: "Packages shipped from this repository generally support all the officially supported versions of .NET and .NET Framework … except `.NET Framework 3.5`." ([README at core-1.18.0](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.18.0/README.md)); restated on the docs site as "supports all officially supported versions of .NET and .NET Framework except for .NET Framework 3.5 SP1" ([opentelemetry.io/docs/languages/dotnet](https://opentelemetry.io/docs/languages/dotnet/)).

### 7.4 Breaking changes and notable additions

**1.18.0 breaking change** ([RELEASENOTES.md at core-1.18.0](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.18.0/RELEASENOTES.md)):

> "**Breaking change:** The maximum size of a single OTLP export request is now 64 MiB by default instead of 128 MiB to conform with the OpenTelemetry specification. Raise the value of the new `OtlpExporterOptions.MaxRequestSizeBytes` property to increase the capacity."

Notable additions in 1.18.0 (same source):

- `OtlpExporterOptions.MaxRequestSizeBytes` — default 64 MiB, maximum supported 256 MiB. A batch whose serialized payload exceeds the limit "is not sent and is dropped in its entirety."
- `OtlpExporterOptions.MaxResponseSizeBytes` — default 4 MiB. A response exceeding the limit "is discarded and treated as a non-retryable failure."
- New SDK self-observability metrics: `otel.sdk.processor.log.processed`, `otel.sdk.processor.span.processed`.
- Serialization of attribute values that are key/value lists, in the OTLP, console and Zipkin exporters.
- New `ITelemetryHostInitializer` interface "for applications that do not support hosted services, such as Blazor, to use to manually initialize the OpenTelemetry SDK as part of application startup."
- `BatchActivityExportProcessor` / `SimpleActivityExportProcessor` no longer forward spans after `Shutdown`; `BatchActivityExportProcessor.Shutdown` now waits for in-flight `OnEnd` calls.
- Fix: `UseOtlpExporter` now respects options configured via `services.Configure<OtlpExporterOptions>(...)`.
- Server-supplied OTLP/gRPC retry delay (`RetryInfo.retry_delay`) is clamped to a 100 ms minimum.
- Further fixes: self-diagnostics truncation instead of dropping; activity creation no longer throws on duplicate sampler attribute keys; providers no longer leak background threads on constructor exception; serializer per-thread state cleanup; persistent-storage retry thread no longer terminates permanently.

`MaxRequestSizeBytes` and `MaxResponseSizeBytes` are confirmed present in the **Stable** public API surface ([PublicAPI.Shipped.txt](https://raw.githubusercontent.com/open-telemetry/opentelemetry-dotnet/core-1.18.0/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/.publicApi/Stable/PublicAPI.Shipped.txt)).

**Preceding stable releases, for migration context** (same RELEASENOTES.md):

- **1.17.0** — no entry marked "Breaking". Notable: Resource Schema URL support; `MetricStreamConfiguration.ExcludedTagKeys`; `tracestate` validation updated to **W3C Trace Context Level 2** grammar; the SDK now depends on `Microsoft.Extensions.Configuration.EnvironmentVariables` instead of a vendored implementation; **all libraries are now marked trim and AOT compatible**.
- **1.16.0** — "**Breaking Change** Explicit histogram boundaries no longer allow more than 10 million values." Also: W3C randomness flag support; opt-in gzip compression for the OTLP exporter.

### 7.5 Documentation entry points and current setup API surface

Entry points: [opentelemetry.io/docs/languages/dotnet](https://opentelemetry.io/docs/languages/dotnet/) (all three signals marked **Stable** in the project-status table), [getting-started](https://opentelemetry.io/docs/languages/dotnet/getting-started/) (states ".NET SDK 8+" required; installs `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Exporter.Console` with **no version or `--prerelease` flags**), [observability-with-otel](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel), [observability-otlp-example](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-otlp-example), the [Extensions.Hosting README](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.18.0/src/OpenTelemetry.Extensions.Hosting/README.md), and the [OTLP exporter README](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.18.0/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md).

API names verified against the **Stable** public API surface file, not prose ([PublicAPI.Shipped.txt](https://raw.githubusercontent.com/open-telemetry/opentelemetry-dotnet/core-1.18.0/src/OpenTelemetry/.publicApi/Stable/PublicAPI.Shipped.txt)) — all of these are stable in `OpenTelemetry` 1.18.0:

- `OpenTelemetryBuilderSdkExtensions.ConfigureResource(this IOpenTelemetryBuilder, Action<ResourceBuilder>)` → `IOpenTelemetryBuilder`
- `WithTracing(this IOpenTelemetryBuilder)` and `WithTracing(this IOpenTelemetryBuilder, Action<TracerProviderBuilder>)`
- `WithMetrics(this IOpenTelemetryBuilder)` and `WithMetrics(this IOpenTelemetryBuilder, Action<MeterProviderBuilder>)`
- `WithLogging(this IOpenTelemetryBuilder)`, `WithLogging(this IOpenTelemetryBuilder, Action<LoggerProviderBuilder>)`, and `WithLogging(this IOpenTelemetryBuilder, Action<LoggerProviderBuilder>?, Action<OpenTelemetryLoggerOptions>?)`
- `OpenTelemetrySdk.Create(Action<IOpenTelemetryBuilder>)` → `OpenTelemetrySdk` (for non-host scenarios)
- Per-provider `ConfigureResource(...)` overloads on `TracerProviderBuilder`, `MeterProviderBuilder`, `LoggerProviderBuilder`

Two documentation gaps worth naming: **`WithLogging` is stable**, though the Extensions.Hosting README's "Extension method reference" lists only `ConfigureResource`, `WithTracing` and `WithMetrics`; and the builder type returned by `AddOpenTelemetry()` is described in that README as `OpenTelemetryBuilder`, while the stable extension methods are declared against the interface **`OpenTelemetry.IOpenTelemetryBuilder`**.

From the OTLP exporter's stable API surface: `OpenTelemetryBuilderOtlpExporterExtensions.UseOtlpExporter(this IOpenTelemetryBuilder)`, `UseOtlpExporter(this IOpenTelemetryBuilder, OtlpExportProtocol protocol, Uri baseUrl)`, `OtlpExportProtocol.Grpc = 0`, `OtlpExportProtocol.HttpProtobuf = 1`, `OtlpExporterOptions.Protocol`, `.MaxRequestSizeBytes`, `.MaxResponseSizeBytes`.

Documented `UseOtlpExporter` behaviors ([OTLP README](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.18.0/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md)):

- "Calling `UseOtlpExporter` automatically enables logging, metrics, and tracing."
- "The exporter registered by `UseOtlpExporter` will be added as the last" processor/reader.
- "`UseOtlpExporter` can only be called once. Subsequent calls will result in a `NotSupportedException`."
- "`UseOtlpExporter` cannot be called in addition to signal-specific `AddOtlpExporter` methods. If `UseOtlpExporter` is called signal-specific `AddOtlpExporter` calls will result in a `NotSupportedException` being thrown."
- Overload usage: `.UseOtlpExporter(OtlpExportProtocol.HttpProtobuf, new Uri("http://localhost:4318/"))`.

`AddOtlpExporter()` also exists as a signal-specific extension on `LoggerProviderBuilder`/`OpenTelemetryLoggerOptions`, `MeterProviderBuilder`, and `TracerProviderBuilder`, with named overloads such as `AddOtlpExporter("tracing", configure: null)`.

Resource configuration: the getting-started page uses `ResourceBuilder.CreateDefault().AddService(serviceName)`; the Extensions.Hosting README uses `.ConfigureResource(builder => builder.AddService(serviceName: "MyService"))`. Logging bridge: `builder.Logging.AddOpenTelemetry(...)` with `logging.IncludeFormattedMessage = true;` and `logging.IncludeScopes = true;` ([observability-otlp-example](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-otlp-example)). Environment variables shown in Microsoft's walkthrough: `OTEL_EXPORTER_OTLP_ENDPOINT` (`http://localhost:4317`), `OTEL_SERVICE_NAME`, and `OTEL_RESOURCE_ATTRIBUTES`.

**Documentation staleness fact:** the Microsoft Learn OTLP example pins `Version="1.9.0"` for `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, and `OpenTelemetry.Instrumentation.Http` — **nine minor versions behind** the current stable 1.18.0. The page itself carries the note "Use the latest versions, as the OTel APIs are constantly evolving"; its `ms.date` is 2023-06-14 with `updated_at` 2026-07-01 ([observability-otlp-example](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-otlp-example)). **This is exactly the failure mode PRD 62 guards against.**

### 7.6 .NET 10 breaking changes affecting diagnostics/telemetry

Four relevant entries on [Breaking changes in .NET 10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0).

**(a) `ActivitySource.CreateActivity` / `StartActivity` sampling behavior** — behavioral ([detail](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/activity-sampling)). When creating an `Activity` with `ActivitySamplingResult.PropagationData` **and a parent marked `Recorded`**:

| | `Activity.Recorded` | `Activity.IsAllDataRequested` |
|---|---|---|
| Before .NET 10 | `True` | `False` |
| .NET 10 | `False` | `False` |

Reason: "The previous behavior did not follow the OpenTelemetry specification." Restore with `Activity.ActivityTraceFlags = Recorded` after the call. **OpenTelemetry-specific note from the same page:** "If you use OpenTelemetry .NET and have customized the sampler, verify your sampler configuration. The default OpenTelemetry .NET configuration uses a parent-based algorithm that isn't impacted."

**(b) Default trace context propagator updated to W3C** — behavioral ([detail](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/default-trace-context-propagator)). `DistributedContextPropagator.CreateDefaultPropagator()` and `DistributedContextPropagator.Current` now return/default to the **W3C** propagator rather than the legacy one. The W3C propagator "uses the `baggage` header instead of `Correlation-Context`, enforces W3C-compliant encoding, and supports only W3C-formatted trace parent IDs." Restore legacy behavior with `DistributedContextPropagator.Current = DistributedContextPropagator.CreatePreW3CPropagator();`. Referenced changes: [dotnet/runtime#114583](https://github.com/dotnet/runtime/pull/114583), [dotnet/runtime#114584](https://github.com/dotnet/runtime/issues/114584). **Directly relevant to PRD 49's correlation requirement (OperationId / AgentId / ServerId / DiagnosticId) across the manager↔agent boundary (PRD 16).**

**(c) `ProviderAliasAttribute` moved to `Microsoft.Extensions.Logging.Abstractions`** — source incompatible ([detail](https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/provideraliasattribute-moved-assembly)). Type-forwarded from `Microsoft.Extensions.Logging`, so "In most scenarios, no action is required." Breaks only "when your project references an older version of `Microsoft.Extensions.Logging` alongside the .NET 10 version of `Microsoft.Extensions.Logging.Abstractions`."

**(d) `Message` no longer duplicated in Console log output** — behavioral ([detail](https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/console-json-logging-duplicate-messages)). With the JSON console formatter the message previously "appeared three times: once as the top-level `Message`, again within the `State` object, and a third time as the original format string." In .NET 10 `Message` appears only at the top level and `State` retains only `{OriginalFormat}`. Caveat: "In some cases, a `Message` might still appear within the `State` object… when its content differs from the top-level `Message`." Affects `AddConsole`, `AddConsoleFormatter`, `AddJsonConsole`, `AddSimpleConsole`, `AddSystemdConsole`. **Relevant to PRD 48 (logging) if log output is parsed.**

**Verified negative:** the .NET 10 breaking-changes index contains **no** entry for `System.Diagnostics.Metrics.Meter`, `Metrics`, `DiagnosticSource`, or `EventSource` (checked against the full category listing: ASP.NET Core, Containers, Core .NET libraries, Cryptography, EF Core, Extensions, Globalization, Install tool, Interop, Networking, Reflection, SDK and MSBuild, Serialization, Windows Forms, WPF).

### 7.7 Microsoft first-party adjacent packages

| Package id | Stable | Latest any |
|---|---|---|
| `Microsoft.Extensions.Diagnostics` | 10.0.12 | 11.0.0-rc.1.26425.128 |
| `Microsoft.Extensions.Diagnostics.Abstractions` | 10.0.12 | 11.0.0-rc.1.26425.128 |
| `Microsoft.Extensions.Telemetry` | 10.10.0 | 10.10.0 |
| `Microsoft.Extensions.Telemetry.Abstractions` | 10.10.0 | 10.10.0 |
| `Microsoft.Extensions.Http.Diagnostics` | 10.10.0 | 10.10.0 |
| `Microsoft.Extensions.ServiceDiscovery` | 10.10.0 | 10.10.0 |
| `Microsoft.Extensions.ServiceDiscovery.Dns` | 10.10.0 | 10.10.0 |
| `Microsoft.Extensions.Compliance.Redaction` | 10.10.0 | 10.10.0 |
| `Aspire.Hosting` | 13.5.3 | 13.5.3 |
| `Aspire.Hosting.AppHost` | 13.5.3 | 13.5.3 |
| `Aspire.ProjectTemplates` | 13.5.3 | 13.5.3 |

Package descriptions from nuspecs: `Microsoft.Extensions.Diagnostics` 10.0.12 — "the default implementation of IMeterFactory and additional extension methods to easily register it with the Dependency Injection framework"; `Microsoft.Extensions.Telemetry` 10.10.0 — "Provides canonical implementations of telemetry abstractions"; `Microsoft.Extensions.Http.Diagnostics` 10.10.0 — "Telemetry support for HTTP Client"; `Microsoft.Extensions.ServiceDiscovery` 10.10.0 — "Provides extensions to HttpClient that enable service discovery based on configuration."

**There is no NuGet package for Aspire's OpenTelemetry defaults** — Microsoft documents the wiring as a *project template*, not a package:

> "The default project templates for Aspire contain a `ServiceDefaults` project, part of which is to setup and configure OTel. … The Service Defaults project template includes the OTel SDK, ASP.NET, HttpClient and Runtime Instrumentation packages, and those are configured in the [`Extensions.cs`] file."

Obtained via `dotnet new aspire-servicedefaults --output ServiceDefaults`, then `builder.ConfigureOpenTelemetry();` or `AddServiceDefaults()` ([observability-with-otel](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel), template source at [microsoft/aspire](https://github.com/microsoft/aspire/blob/main/src/Aspire.ProjectTemplates/templates/aspire-servicedefaults/Extensions.cs)).

Aspire is now versioned independently of .NET at **13.5.3** and documented at `aspire.dev` rather than under `learn.microsoft.com/dotnet/aspire` — the Learn page links to [aspire.dev/get-started/what-is-aspire](https://aspire.dev/get-started/what-is-aspire/), [aspire.dev/fundamentals/telemetry](https://aspire.dev/fundamentals/telemetry/), and [aspire.dev/dashboard/explore](https://aspire.dev/dashboard/explore/). The standalone dashboard container is `mcr.microsoft.com/dotnet/aspire-dashboard:latest`, exposing port 18888 (UI) and 18889 (OTLP ingest) ([observability-otlp-example](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-otlp-example)).

### 7.8 OTLP / specification version

The opentelemetry-dotnet repo does **not** state a specification version it implements. Its README points to the compliance matrix instead:

> "To understand which portions of the OpenTelemetry Specification have been implemented in OpenTelemetry .NET see: Spec Compliance Matrix — https://github.com/open-telemetry/opentelemetry-specification/blob/main/spec-compliance-matrix.md"

([README at core-1.18.0](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.18.0/README.md)). The 1.18.0 release notes reference spec conformance for the request/response size limits ("to conform with the OpenTelemetry specification") without naming a version, and the OTLP exporter README links the spec at an unversioned `main` path.

Upstream spec versions for context — **not** claimed as what 1.18.0 implements: latest `opentelemetry-specification` release **v1.60.0** (2026-08-07; preceded by v1.59.0 on 2026-07-10 and v1.58.0 on 2026-06-22) ([releases](https://github.com/open-telemetry/opentelemetry-specification/releases)); latest `opentelemetry-proto` release **v1.11.0** (2026-07-21; preceded by v1.10.0 on 2026-03-09 and v1.9.0 on 2025-10-31) ([releases](https://github.com/open-telemetry/opentelemetry-proto/releases)).

A stale-link caveat: the repo's `src/Shared/Proto/README.md` says ".proto files are copied from the opentelemetry-proto repo" and links commit `1a931b4b57c34e7fd8f7dddcaa9b7587840e9c08` — a commit dated **2020-06-24** ("Declare Trace part of OTLP Stable (#160)"). That link cannot be used to infer the OTLP proto version implemented by 1.18.0. Relatedly, OTLP serialization in 1.18.0 is hand-written rather than protobuf-tool-generated: the repo contains `ProtobufOtlpTraceFieldNumberConstants.cs`, `ProtobufOtlpMetricFieldNumberConstants.cs`, `ProtobufOtlpLogFieldNumberConstants.cs`, `ProtobufOtlpCommonFieldNumberConstants.cs` under `src/OpenTelemetry.Exporter.OpenTelemetryProtocol/Implementation/Serializer/`.

Spec-related behavior confirmed in the changelog: 1.17.0 "Updated `tracestate` key validation to comply with the W3C Trace Context Level 2 grammar"; 1.16.0 "Add support for the W3C randomness flag."

---

## 8. Project configuration constraints (PRD 56)

PRD 56 requires `Nullable`, `ImplicitUsings` and `TreatWarningsAsErrors` to be enabled, plus "Current .NET analyzers." This section records the verified defaults and the measured consequences.

### 8.1 Documented defaults

| Property | Documented default | Source |
|---|---|---|
| `Nullable` | "When there's no value set, the default value **`disable`** is applied, however the .NET 6 templates are by default provided with the `Nullable` value set to `enable`." Valid values: `enable`, `disable`, `warnings`, `annotations`. | [compiler-options/language](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/language) |
| `ImplicitUsings` | Off unless set — "To disable implicit `global using` directives, remove the property or set it to `false` or `disable`." "The templates for new C# projects that target .NET 6 or later have `ImplicitUsings` set to `enable` by default." Requires .NET 6+ and C# 10+. | [msbuild-props#implicitusings](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#implicitusings) |
| `TreatWarningsAsErrors` | "**By default, `TreatWarningsAsErrors` isn't in effect**, which means warnings don't prevent the generation of an output file." | [compiler-options/errors-warnings](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/errors-warnings) |
| `EnableNETAnalyzers` | ".NET code quality analysis is **enabled, by default, for projects that target .NET 5 or a later version**." Applies only to the built-in SDK analyzers; do not combine with the `Microsoft.CodeAnalysis.NetAnalyzers` NuGet package. | [msbuild-props#enablenetanalyzers](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#enablenetanalyzers), [code-analysis/overview](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview) |
| `AnalysisLevel` | "If your project targets .NET 5 or later, or if you've added the `AnalysisMode` property, **the default value is `latest`**." | [msbuild-props#analysislevel](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#analysislevel) |
| `AnalysisMode` | **`Default`** — "only a small number of rules are enabled as build warnings." | [code-analysis/overview](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview) |
| `EnforceCodeStyleInBuild` | "**.NET code style analysis is disabled, by default, on build for all .NET projects.**" | [msbuild-props#enforcecodestyleinbuild](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#enforcecodestyleinbuild) |
| `CodeAnalysisTreatWarningsAsErrors` | Effect only: "If you use the `-warnaserror` flag when you build your projects, .NET code quality analysis warnings are also treated as errors. If you do not want code quality analysis warnings to be treated as errors, you can set `CodeAnalysisTreatWarningsAsErrors` to `false`." No default stated. | [msbuild-props](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#codeanalysistreatwarningsaserrors) |
| `WarningsNotAsErrors` | Overrides `TreatWarningsAsErrors` for a listed set; accepts warning numbers with an optional `CS` prefix, and the literal `nullable`. No default stated. | [compiler-options/errors-warnings](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/errors-warnings) |

**`AnalysisLevel` values** ([msbuild-props#analysislevel](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#analysislevel)): `latest`, `latest-<mode>`, `preview`, `preview-<mode>`, `10.0`/`10` ("The set of rules that was available for the .NET 10 release is used, even if newer rules are available"), `10.0-<mode>`, plus `9.0`/`9` and `8.0`/`8` with the same `-<mode>` variants. A compound `AnalysisLevel` takes precedence over `AnalysisMode`. With `EnforceCodeStyleInBuild=true`, `AnalysisLevel` also affects IDExxxx code-style rules. Per-category overrides exist: `AnalysisLevelDesign`, `AnalysisLevelDocumentation`, `AnalysisLevelGlobalization`, `AnalysisLevelInteroperability`, `AnalysisLevelMaintainability`, `AnalysisLevelNaming`, `AnalysisLevelPerformance`, `AnalysisLevelSingleFile`, `AnalysisLevelReliability`, `AnalysisLevelSecurity`, `AnalysisLevelStyle`, `AnalysisLevelUsage`, plus the parallel `AnalysisMode<Category>` set; omitted per-category values fall back to the global value.

**`AnalysisMode` values**, in increasing order of rules enabled ([code-analysis/overview](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview)): `None`, `Default`, `Minimum`, `Recommended`, `All`. Exception for `All`: "The following rules are *not* enabled by setting `AnalysisMode` to `All` or by setting `AnalysisLevel` to `latest-all`: CA1017, CA1045, CA1005, CA1014, CA1060, CA1021, and the code metrics analyzer rules (CA1501, CA1502, CA1505, CA1506, and CA1509)."

Also: "By default, you'll get the latest code analysis rules and default rule severities as you upgrade to newer versions of the .NET SDK."

**Documentation conflict on the `AnalysisLevel` default, resolved empirically.** The C# compiler-options page states a different rule — "Beginning with the .NET 7 SDK… The default `AnalysisLevel` matches the Target Framework Moniker (TFM) from the project file. The default `WarningLevel` matches the value for `AnalysisLevel`." ([errors-warnings](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/errors-warnings)) — whereas msbuild-props says the default is `latest`. The two are not reconciled in the docs. **LOCAL measurement resolves what actually happens** (see §8.2): `AnalysisLevel` evaluates to `latest` and `EffectiveAnalysisLevel` resolves to `10.0` for a `net10.0` project — i.e. both statements describe the same outcome from different angles.

### 8.2 LOCAL — measured effective values

Measured with `dotnet msbuild -getProperty:…` on the scaffolded `net10.0` Blazor Web App server project, **SDK 10.0.302**:

| Property | Measured value |
|---|---|
| `LangVersion` | **`14.0`** |
| `AnalysisLevel` | **`latest`** |
| `EffectiveAnalysisLevel` | **`10.0`** |
| `AnalysisMode` | *(empty — i.e. `Default`)* |
| `EnableNETAnalyzers` | **`true`** |
| `TreatWarningsAsErrors` | **`false`** |
| `Nullable` | **`enable`** |
| `ImplicitUsings` | **`enable`** |
| `EnforceCodeStyleInBuild` | **`false`** |
| `NuGetAudit` | **`true`** |
| `NuGetAuditMode` | **`all`** |
| `NuGetAuditLevel` | **`low`** |

### 8.3 What the templates actually set

Verified against the shipping template sources on the release branches, and independently against LOCAL generated output:

- **`dotnet new console` / `classlib`** — `<TargetFramework>net10.0</TargetFramework>` plus `<ImplicitUsings Condition="'$(csharpFeature_ImplicitUsings)' == 'true'">enable</ImplicitUsings>` and `<Nullable Condition="'$(csharpFeature_Nullable)' == 'true'">enable</Nullable>` ([SDK template feed](https://github.com/dotnet/sdk/blob/release/10.0.4xx/template_feed/Microsoft.DotNet.Common.ProjectTemplates.10.0/content/ConsoleApplication-CSharp/Company.ConsoleApplication1.csproj)). The gating symbols are `csharpFeature_ImplicitUsings = csharp10orLater` and `csharpFeature_Nullable = csharp8orLater` ([template.json](https://github.com/dotnet/sdk/blob/release/10.0.4xx/template_feed/Microsoft.DotNet.Common.ProjectTemplates.10.0/content/ConsoleApplication-CSharp/.template.config/template.json)) — both true for `net10.0`/C# 14, so **both are emitted as `enable`**.
- **`dotnet new web`** — `Nullable=enable` and `ImplicitUsings=enable`, unconditional ([EmptyWeb-CSharp.csproj.in](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/ProjectTemplates/Web.ProjectTemplates/EmptyWeb-CSharp.csproj.in)).
- **`dotnet new webapi`** — same, unconditional ([WebApi-CSharp.csproj.in](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/ProjectTemplates/Web.ProjectTemplates/WebApi-CSharp.csproj.in)).
- **`dotnet new blazor`** — same, unconditional, plus `BlazorDisableThrowNavigationException=true` ([BlazorWebCSharp.csproj.in](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/ProjectTemplates/Web.ProjectTemplates/BlazorWebCSharp.csproj.in)). Confirmed LOCAL (§4.2).
- `${DefaultNetCoreTargetFramework}` resolves to **`net10.0`** on `release/10.0` ([eng/Versions.props](https://github.com/dotnet/aspnetcore/blob/release/10.0/eng/Versions.props)).
- **None** of these templates set `TreatWarningsAsErrors`, `EnableNETAnalyzers`, `AnalysisLevel`, `AnalysisMode`, `CodeAnalysisTreatWarningsAsErrors`, `WarningsNotAsErrors`, or `EnforceCodeStyleInBuild` (verified by reading the full template project files linked above, and confirmed LOCAL).

**Net effect for PRD 56:** `Nullable` and `ImplicitUsings` come for free from the templates; `TreatWarningsAsErrors` and any analyzer tightening beyond the SDK default must be added by Feature 0 (e.g. in a `Directory.Build.props`).

### 8.4 New analyzer rules in .NET 10

From [`AnalyzerReleases.Shipped.md`, "Release 10.0"](https://github.com/dotnet/sdk/blob/main/src/Microsoft.CodeAnalysis.NetAnalyzers/src/Microsoft.CodeAnalysis.NetAnalyzers/AnalyzerReleases.Shipped.md):

| Rule | Category | Severity | Analyzer |
|---|---|---|---|
| [CA1873](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1873) | Performance | Info | `AvoidPotentiallyExpensiveCallWhenLoggingAnalyzer` |
| [CA1874](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1874) | Performance | Info | `UseRegexMembers` |
| [CA1875](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1875) | Performance | Info | `UseRegexMembers` |
| [CA2023](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2023) | Reliability | **Warning** | `LoggerMessageDefineAnalyzer` |
| [CA2024](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2024) | Reliability | **Warning** | `DoNotUseEndOfStreamInAsyncMethods` |
| [CA2025](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2025) | Reliability | Disabled | `DoNotPassDisposablesIntoUnawaitedTasksAnalyzer` |
| [CA2266](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2266) | Usage | **Warning** | `MissingShebangInFileBasedProgram` |

The two .NET 10 additions that appear in the **default-enabled** warning set for .NET 10, per [code-analysis/overview](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview) (".NET 10" tab), are **CA2023** ("Invalid braces in message template") and **CA2266** ("File-based program entry point should start with `#!`").

The complete default-enabled-as-warning/error set for .NET 10 (same source): CA1416, CA1417, CA1418, CA1420, CA1422, CA1831, **CA1856 (Error)**, CA1857, CA2013, CA2014, CA2015, CA2017, CA2018, CA2021, CA2022, **CA2023**, CA2200, CA2247, **CA2252 (Error)**, CA2255, CA2256, CA2257, CA2258, CA2259, CA2260, CA2261, CA2264, CA2265, **CA2266**. CA2024 and CA1873–CA1875 are *not* in that list.

### 8.5 LOCAL — measured consequences of `TreatWarningsAsErrors`

Three builds of the scaffolded Blazor Web App + `.Client` solution (SDK 10.0.302, template packages at 10.0.10):

| Configuration | Result |
|---|---|
| `-p:TreatWarningsAsErrors=true` (SDK-default analyzers, `NuGetAudit` on) | **Build FAILED** — `error NU1903: Warning As Error: Package 'SQLitePCLRaw.lib.e_sqlite3' 2.1.11 has a known high severity vulnerability` ([GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)) |
| `-p:TreatWarningsAsErrors=true -p:NuGetAudit=false` | **Build succeeded**, zero warnings |
| `-p:NuGetAudit=false -p:AnalysisMode=All -p:EnforceCodeStyleInBuild=true --no-incremental` | **68 CA warnings**: CA2007 ×38, CA1515 ×10, CA1812 ×6, CA5394 ×4, CA1062 ×4, CA1873 ×2, CA1859 ×2, CA1848 ×2 |

**Three constraints this establishes for Feature 0.**

1. **`TreatWarningsAsErrors` promotes NuGet audit warnings (NU19xx) to build errors.** `NuGetAudit` defaults to `true` with `NuGetAuditMode=all` and `NuGetAuditLevel=low` on net10.0 (§8.2), and .NET 10 newly made `dotnet restore` audit *transitive* packages (§1.5). The failure above came from `SQLitePCLRaw.lib.e_sqlite3`, a **transitive** dependency of `Microsoft.EntityFrameworkCore.Sqlite`. Consequence: with PRD 56 as written, **any newly published advisory against any transitive dependency breaks the build with no code change** — a supply-chain signal (PRD 55) and a CI-stability concern (PRD 54) at the same time. The knobs are `NuGetAudit`, `NuGetAuditMode`, `NuGetAuditLevel`, and `WarningsNotAsErrors`.
2. **The stock .NET 10 template is clean under `TreatWarningsAsErrors` with the SDK-default analyzer set** — so PRD 56's core requirement is satisfiable from an empty baseline.
3. **`TreatWarningsAsErrors` + `AnalysisMode=All` is not satisfiable against template-shaped code as generated** — 68 CA warnings. The dominant rules are CA2007 (`ConfigureAwait`), CA1515 (make types internal), CA1812 (uninstantiated internal class), CA5394 (insecure randomness), CA1062 (validate public arguments), CA1848 (use `LoggerMessage` delegates). Any mode above `Default` needs an explicit `.editorconfig` severity policy, which Feature 0 already lists as a deliverable.

### 8.6 Other .NET 10 configuration constraints worth noting

- **`PackageReference` without a version is now an error (NU1015)** ([detail](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/nu1015-packagereference-version)) — interacts with Central Package Management.
- **`dotnet restore` audits transitive packages** ([detail](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/nugetaudit-transitive-packages)).
- **NuGet audit sources no longer allow insecure HTTP by default** ([detail](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/nuget-audit-source-http-disallowed)).
- **NU1510 for framework-provided packages** — §3.3; under `TreatWarningsAsErrors` this is a build error.
- **`rollForward: disable`** is the documented pairing for package lock files: "When you use package lock files, set `rollForward` to `disable` so the SDK version and dependency graph stay in lockstep" ([global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json)).
- **`CheckSdkVulnerabilities=true`** emits NETSDK1239 when the resolved SDK is end of life ([releases-and-support](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support)).
- **`<GenerateDocumentationFile>true</GenerateDocumentationFile>`** is required for XML doc comments in OpenAPI documents (§3.4). Enabling it also turns on CS1591 (missing XML comment) warnings, which under `TreatWarningsAsErrors` become errors — the standard mitigation is `WarningsNotAsErrors` or `NoWarn` for CS1591. *(The CS1591 interaction is inferred from documented behavior of the two properties, not measured — see [Could Not Verify](#could-not-verify).)*
- **Source-generator constraint on validation:** "Both the validation feature and Razor compiler use source generators. Currently, one source generator's output can't be used as another's input" — so form model types must live in `.cs` files, not `.razor` (§4.5 item 29).
- **`dotnet new sln` now defaults to the SLNX format** (§1.5) — affects what Feature 0's solution file looks like.
- **`--interactive` now defaults to `true` in user scenarios** (§1.5) — relevant to CI invocations (PRD 54), which should pass `--interactive false` or run in a non-user context.

---

## Could Not Verify

Recorded explicitly, per the ticket's instruction.

**SDK / C#**

1. **Whether the .NET 10 SDK's bundled Roslyn *accepts* `LangVersion` `15.0` or `preview`.** `releases.json` reports `"csharp-version": "14.0"` for SDK 10.0.401 and 10.0.112, and Learn states C# 15 "is supported only on .NET 11 and newer versions" — but no primary source states whether the .NET 10 compiler will accept `15.0`/`preview` as a value. The `15.0` row does appear in the version-independent `LangVersion` table.
2. **Documented defaults for `CodeAnalysisTreatWarningsAsErrors` and `WarningsNotAsErrors`.** Both are documented by effect only; neither page states a default.
3. **The `AnalysisLevel` default is stated inconsistently across two primary Microsoft docs** (`latest` per msbuild-props vs. "matches the TFM" per the C# compiler-options page). Both quotes are recorded in §8.1; the docs do not reconcile them. §8.2's LOCAL measurement shows the observable outcome but is not a doc resolution.
4. **Whether the .NET 10 breaking-changes list is complete.** The page states verbatim: "This article is a work in progress. It's not a complete list of breaking changes in .NET 10." The ASP.NET Core breaking-changes page carries the same caveat.
5. **No .NET-10-specific "code analysis release notes" page exists.** The authoritative per-release rule list is `AnalyzerReleases.Shipped.md`, which the code-analysis overview itself points to.

**ASP.NET Core / SignalR**

6. **A SignalR-specific "what's new in .NET 10" feature list.** No primary source lists any — four independent checks in §5 all came back empty. There is no "Announcing ASP.NET Core in .NET 10" devblogs post at the expected URL (HTTP 404).
7. **An ASP.NET Core SignalR client-to-server version-compatibility matrix.** The `signalr/version-differences` page exists but is about ASP.NET SignalR vs ASP.NET Core SignalR, not client/server version pairing. No matrix found on the introduction or supported-platforms pages either. **This is a real gap for PRD 16/17 (agent protocol), where manager and agent may run different patch levels.**
8. **A stated minimum SDK patch version.** The migration doc shows `10.0.100` only as a `global.json` diff example, not as a stated minimum.
9. **Visual Studio version requirement discrepancy, unresolved.** The migration doc lists "Visual Studio 2022" as the prerequisite; the .NET 10 announcement refers to "updates to Visual Studio 2026"; `releases.json` records `"vs-version": "18.9.3, 18.10.0"` for SDK 10.0.401. No primary source reconciles the three.
10. **`Microsoft.Extensions.Http.Resilience` shared-framework status by direct documentation.** Its absence from `SharedFramework.Local.props` is the basis for the "needs a `PackageReference`" claim; no doc page states it.
11. **`dotnet/extensions` release-tag vs NuGet mismatch.** The repo's latest tagged release is `v10.9.0` (2026-08-12), while NuGet publishes `10.10.0` as the highest stable for `Microsoft.Extensions.Telemetry`, `.Http.Resilience`, `.Http.Diagnostics`, `.ServiceDiscovery` and `.Compliance.Redaction`. Most likely a release-notes lag, but no primary source confirms that. The NuGet versions are the verified ones.
12. **Release dates for the Microsoft.Extensions and Aspire packages** — ids and versions verified via flat-container; publication dates not confirmed from a primary source.
13. **An `<GenerateDocumentationFile>` × CS1591 × `TreatWarningsAsErrors` interaction, not measured.** §8.6 infers it from the documented behavior of each property individually. It was not reproduced on this machine.

**Blazor**

14. **`RegisterPersistentService` render-mode argument names.** The prerendered-state-persistence article lists the permitted values as `RenderMode.Server`, `RenderMode.Webassembly` and `RenderMode.InteractiveAuto` — but `RenderMode` exposes only `InteractiveServer`, `InteractiveWebAssembly`, `InteractiveAuto` (DOC and LOCAL, §4.3). This reads as a documentation error; no primary source states the intended names.
15. **`--localhost-tld` semantics and availability history** — present in `dotnet new blazor -h` under SDK 10.0.302 (LOCAL) but absent from the template docs. No documentation page found.
16. **A single authoritative Learn page enumerating which Blazor packages ship in the shared framework.** The §4.6 split rests on explicit "add a package reference" instructions in the QuickGrid and WebAssembly-security docs, the generated template `.csproj` files (LOCAL), and the targeting-pack assembly list (LOCAL) — not on a published list.
17. **Whether `Microsoft.AspNetCore.Components.WebAssembly.Server` is intended for direct app use.** Its namespace reference says "intended for framework use only, not supported for use in application code", yet the Blazor Web App template adds it as an explicit `PackageReference`. No doc reconciles the two.
18. **Boot-configuration migration guidance** — the release notes state outright that there is "No documented replacement strategy" for integrity-check scripts and DLL-extension renaming after `blazor.boot.json` was inlined.
19. **`ResourcePreloader` API reference** — documented only in the release notes; no API-reference page located.

**Identity / passkeys**

20. **Data Protection changes in .NET 10 — none found, but not provably zero.** Grepping the live-branch `aspnetcore-10.0.md` release notes and its includes for "data protection" returned nothing, and neither breaking-changes page has an entry. This is "none documented", not "none exist". **Worth re-checking for PRD 10, which depends on Data Protection.**
21. **`ValidateOnStart` changes in .NET 10** — none found. The Extensions section of the .NET 10 breaking-changes page lists 6 items, none about options validation.
22. **JWT bearer handler changes in .NET 10** — none documented in the release notes or either breaking-changes list, despite the package shipping at 10.0.12.
23. **OIDC / `OpenIdConnect` handler changes in .NET 10** — same: package at 10.0.12, no documented changes found. **This is a gap for PRD 3B (Auth0 Organizations / OIDC).**
24. **`PersistedGrant` / token changes** — nothing found. `PersistedGrant` is a Duende IdentityServer concept, not an ASP.NET Core framework type.
25. **The literal value of `IdentitySchemaVersions.Version3`** (whether `3.0` or `3.0.0`). The API ref documents it as `public static readonly Version Version3` described as "the 3.0 version of the identity schema" but does not print the literal.
26. **`IdentityUserPasskey<TKey>` Learn API reference page** — the expected `…identity.entityframeworkcore.identityuserpasskey-1` URL returns 404; the type's namespace is `Microsoft.AspNetCore.Identity`, verified from source rather than from the Learn reference.
27. **Whether the `Microsoft.AspNetCore.Identity` 2.3.13 NuGet package is formally deprecated.** nuget.org shows no deprecation banner on the latest version; the Identity doc calls it "the primary package for Identity"; yet the shared framework delivers an assembly of that name in-box and no 10.x package exists. The intent is not stated in any primary source.

**OpenTelemetry**

28. **The exact OpenTelemetry specification version that OpenTelemetry .NET 1.18.0 implements.** No primary source states a version number; the repo delegates to an unversioned per-feature compliance matrix (§7.8).
29. **The exact `opentelemetry-proto` version the 1.18.0 OTLP exporter targets.** The only in-repo reference is the stale 2020 commit link documented in §7.8.
30. **`OpenTelemetry.Resources.Container` / `.Host` / `.ProcessRuntime` target frameworks.** Prerelease status and version numbers verified from the flat-container indexes; individual nuspecs were not fetched for TFM lists.
31. **Why `OpenTelemetry.Instrumentation.AspNetCore` 1.18.0 has no `net9.0` dependency group** (it has net10.0, net8.0, netstandard2.0). The nuspec establishes the fact; no primary source explains it. It does not imply lack of net9.0 support — a net9.0 app resolves the net8.0 assets.

**Out of scope for this ticket** (PRD 62 lists them separately, and other wayfinder tickets own them): Blazor Blueprint UI, TUnit, TUnit Mocks, EF Core 10 in depth, the SQLite EF provider, Npgsql in depth, Docker Engine API libraries, Testcontainers, Playwright, bUnit, Caddy, Let's Encrypt/ACME, Project Zomboid server requirements/ports/RCON, SteamCMD, OWASP Top 10, OWASP ASVS.

---

## Re-verification checklist

Everything below is time-sensitive. Re-check before Feature 0 locks versions if that happens materially after 2026-09-10.

| Item | Where to re-check |
|---|---|
| Current .NET 10 patch and SDK | [`releases.json`](https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/releases.json) — read `latest-release`, `latest-sdk`, and the `sdks` array to see which feature bands are serviced |
| Which SDK feature bands still receive updates | Same `sdks` array (only 10.0.4xx and 10.0.1xx were serviced in 10.0.12) |
| ASP.NET Core / Blazor / Identity / SignalR package versions | `https://api.nuget.org/v3-flatcontainer/<lowercase-id>/index.json` |
| `dotnet/extensions` train (`10.10.0` today) | [Http.Resilience index](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.http.resilience/index.json) — moves independently of the runtime train |
| OpenTelemetry stable, and which instrumentation packages are still prerelease | [opentelemetry-dotnet releases](https://github.com/open-telemetry/opentelemetry-dotnet/releases), [contrib releases](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/releases), plus flat-container per id |
| Whether Prometheus/EF Core/Redis/Process instrumentation has reached stable | Flat-container indexes in §7.2 |
| .NET 10 breaking changes (page is explicitly incomplete) | [compatibility/10.0](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0) and [ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/overview) |
| Passkey API additions (some already marked `aspnetcore-11.0`) | [passkeys doc](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/) — check moniker ranges; `PasskeyAuthenticators.cs` is 11.0-only today |
| `@microsoft/signalr` npm version | [registry](https://registry.npmjs.org/@microsoft/signalr) — `dist-tags.latest` |
| .NET 11 status (STS, `go-live` at rc.1 today) | [`releases-index.json`](https://raw.githubusercontent.com/dotnet/core/main/release-notes/releases-index.json) |

**Method note.** Every version string in this document was read from a machine-readable registry (`dotnet/core` `releases.json`, the NuGet flat-container/registration APIs, the npm registry) or an official source repository at a release tag — never from prose or from model memory. Behavioral claims are quoted from Microsoft Learn or from official repo docs at a pinned tag. Claims marked LOCAL were measured on this machine against **SDK 10.0.302** and the **10.0.10** targeting pack, which lag the current 10.0.401 / 10.0.12; they are recorded because they are direct observations of shipping .NET 10 behavior, but the exact patch numbers they report (e.g. template `PackageReference` versions of `10.0.10`) are artifacts of the installed SDK, not of .NET 10 generally.
