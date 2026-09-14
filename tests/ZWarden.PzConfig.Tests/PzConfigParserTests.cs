using System.Text;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// The read seam end to end: the pre-check runs before the parser (so an oversized or over-deep file
/// never reaches Loretta), and the right reader is chosen for the kind. This is the composition the
/// rest of ZWarden calls.
/// </summary>
public class PzConfigParserTests
{
    private static readonly PzConfigParser Parser = new();

    private static PzConfigReadResult Open(PzConfigKind kind, string content, PzConfigLimits? limits = null) =>
        Parser.Open(kind, Encoding.UTF8.GetBytes(content), limits ?? PzConfigLimits.Default);

    [Test]
    public async Task Opens_a_valid_sandbox_file()
    {
        PzConfigReadResult result = Open(PzConfigKind.SandboxVars, "SandboxVars = {\n    Zombies = 4,\n}\n");

        await Assert.That(result.Parsed).IsTrue();
        await Assert.That(result.Document!.TryGetValue("Zombies", out PzValue v)).IsTrue();
        await Assert.That(((PzNumber)v).Value).IsEqualTo(4d);
    }

    [Test]
    public async Task Opens_a_valid_ini_file()
    {
        PzConfigReadResult result = Open(PzConfigKind.Ini, "PVP=true\nMaxPlayers=16\n");

        await Assert.That(result.Parsed).IsTrue();
        await Assert.That(result.Document!.Kind).IsEqualTo(PzConfigKind.Ini);
    }

    [Test]
    public async Task An_oversized_file_never_reaches_the_parser()
    {
        var limits = new PzConfigLimits { MaxByteLength = 16 };

        PzConfigReadResult result = Open(PzConfigKind.SandboxVars, "SandboxVars = {\n    Zombies = 4,\n}\n", limits);

        await Assert.That(result.Parsed).IsFalse();
        await Assert.That(result.Diagnostics.Single().Code).IsEqualTo(PzConfigDiagnostic.Codes.TooLarge);
    }

    [Test]
    public async Task An_over_deep_file_never_reaches_the_parser()
    {
        var limits = new PzConfigLimits { MaxNestingDepth = 3 };

        // Six levels of nesting - would be a stack-overflow risk if it reached Loretta.
        string bomb = "t = " + new string('{', 6) + new string('}', 6);
        PzConfigReadResult result = Open(PzConfigKind.SandboxVars, bomb, limits);

        await Assert.That(result.Parsed).IsFalse();
        await Assert.That(result.Diagnostics.Single().Code).IsEqualTo(PzConfigDiagnostic.Codes.TooDeep);
    }

    [Test]
    public async Task A_syntax_error_surfaces_as_a_not_parsed_result()
    {
        PzConfigReadResult result = Open(PzConfigKind.SandboxVars, "SandboxVars = {\n    Zombies = ,\n}\n");

        await Assert.That(result.Parsed).IsFalse();
        await Assert.That(result.Diagnostics.Any(d => d.Position is not null)).IsTrue();
    }
}
