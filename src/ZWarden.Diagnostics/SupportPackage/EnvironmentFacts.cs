namespace ZWarden.Diagnostics.SupportPackage;

/// <summary>
/// The non-secret environment facts a support package records alongside the diagnostic report (F30, PRD 51): the
/// ZWarden build, the database provider, and the host OS/runtime. These help a maintainer reproduce a problem
/// without identifying the deployment. They are ZWarden-collected (not Server-sourced) but still flow through the
/// sanitize/redact/pseudonymize pipeline uniformly — defence in depth.
/// </summary>
/// <param name="ZWardenVersion">The ZWarden.Web build/assembly version.</param>
/// <param name="DatabaseProvider">The configured database provider (e.g. <c>Sqlite</c>, <c>Postgres</c>).</param>
/// <param name="OperatingSystem">The host OS description.</param>
/// <param name="RuntimeFramework">The .NET runtime description.</param>
public sealed record EnvironmentFacts(
    string ZWardenVersion,
    string DatabaseProvider,
    string OperatingSystem,
    string RuntimeFramework);
