using ZWarden.PzConfig.Revisions;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// The full-key Build 42 fixtures (#227): ZWarden-authored files carrying every vanilla key of a 42.20.4
/// <c>&lt;name&gt;.ini</c> and <c>&lt;name&gt;_SandboxVars.lua</c> in file order, with representative values, plus one
/// mod-added sandbox table. Only the key set, value shapes and the <c>Min: … Max: … Default: …</c> ranges mirror the
/// game; the comment prose is ours (ADR 0009 — no PZ-derived artefact is committed).
/// </summary>
internal static class B42Fixtures
{
    /// <summary>The mod-added sandbox table in the SandboxVars fixture.</summary>
    public const string ModTable = "BetterLockpicking";

    public static byte[] IniBytes => File.ReadAllBytes(PathOf("b42-servertest.ini"));

    public static byte[] SandboxVarsBytes => File.ReadAllBytes(PathOf("b42-SandboxVars.lua"));

    public static PzConfigReadResult ReadIni() => new PzConfigParser().Open(PzConfigKind.Ini, IniBytes);

    public static PzConfigReadResult ReadSandboxVars() =>
        new PzConfigParser().Open(PzConfigKind.SandboxVars, SandboxVarsBytes);

    /// <summary>Every scalar setting path in a parsed fixture, in file order.</summary>
    public static IReadOnlyList<string> PathsOf(PzConfigReadResult read) =>
        [.. PzValueSnapshot.Of(read.Document!).Scalars.Select(s => s.Path)];

    private static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
