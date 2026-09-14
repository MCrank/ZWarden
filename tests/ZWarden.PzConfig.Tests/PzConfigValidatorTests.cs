using System.Text;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Validation;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// The value-tree + schema-overlay validator (F20a decision 1). Known keys are checked for type and
/// range; a key with no schema entry is preserved and reported as Info, never dropped or errored
/// (ADR 0010). Sandbox and INI use the schema table; the spawn files are checked structurally.
/// </summary>
public class PzConfigValidatorTests
{
    private static readonly PzConfigParser Parser = new();
    private static readonly PzConfigValidator Validator = new();

    private static IReadOnlyList<PzConfigDiagnostic> Validate(PzConfigKind kind, string content)
    {
        PzConfigReadResult result = Parser.Open(kind, Encoding.UTF8.GetBytes(content));
        return Validator.Validate(result.Document!);
    }

    private static bool HasError(IReadOnlyList<PzConfigDiagnostic> diagnostics, string code, string pathFragment) =>
        diagnostics.Any(d => d.Severity == PzDiagnosticSeverity.Error && d.Code == code && d.Message.Contains(pathFragment, StringComparison.Ordinal));

    [Test]
    public async Task A_valid_sandbox_file_has_no_errors()
    {
        const string src = """
            SandboxVars = {
                VERSION = 6,
                Zombies = 4,
                Distribution = 1,
                Map = {
                    AllowMiniMap = false,
                    AllowWorldMap = true,
                },
            }
            """;

        IReadOnlyList<PzConfigDiagnostic> diagnostics = Validate(PzConfigKind.SandboxVars, src);

        await Assert.That(diagnostics.Any(d => d.Severity == PzDiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task An_out_of_range_sandbox_integer_is_an_error()
    {
        const string src = "SandboxVars = {\n    VERSION = 6,\n    Zombies = 9,\n}\n";

        IReadOnlyList<PzConfigDiagnostic> diagnostics = Validate(PzConfigKind.SandboxVars, src);

        await Assert.That(HasError(diagnostics, PzConfigDiagnostic.Codes.OutOfRange, "Zombies")).IsTrue();
    }

    [Test]
    public async Task A_wrong_typed_sandbox_value_is_an_error()
    {
        // AllowMiniMap must be a boolean; here it is a number.
        const string src = "SandboxVars = {\n    VERSION = 6,\n    Map = {\n        AllowMiniMap = 3,\n    },\n}\n";

        IReadOnlyList<PzConfigDiagnostic> diagnostics = Validate(PzConfigKind.SandboxVars, src);

        await Assert.That(HasError(diagnostics, PzConfigDiagnostic.Codes.WrongType, "Map.AllowMiniMap")).IsTrue();
    }

    [Test]
    public async Task A_nested_table_value_below_the_cap_is_range_checked()
    {
        const string src = "SandboxVars = {\n    VERSION = 6,\n    Basement = {\n        SpawnFrequency = 8,\n    },\n}\n";

        IReadOnlyList<PzConfigDiagnostic> diagnostics = Validate(PzConfigKind.SandboxVars, src);

        await Assert.That(HasError(diagnostics, PzConfigDiagnostic.Codes.OutOfRange, "Basement.SpawnFrequency")).IsTrue();
    }

    [Test]
    public async Task An_unknown_sandbox_key_is_info_not_error()
    {
        const string src = "SandboxVars = {\n    VERSION = 6,\n    ModAddedKnob = 5,\n}\n";

        IReadOnlyList<PzConfigDiagnostic> diagnostics = Validate(PzConfigKind.SandboxVars, src);

        PzConfigDiagnostic info = diagnostics.Single(d => d.Code == PzConfigDiagnostic.Codes.UnknownKey && d.Message.Contains("ModAddedKnob", StringComparison.Ordinal));
        await Assert.That(info.Severity).IsEqualTo(PzDiagnosticSeverity.Info);
    }

    [Test]
    public async Task A_missing_version_is_an_error()
    {
        const string src = "SandboxVars = {\n    Zombies = 4,\n}\n";

        IReadOnlyList<PzConfigDiagnostic> diagnostics = Validate(PzConfigKind.SandboxVars, src);

        await Assert.That(diagnostics.Any(d => d.Code == PzConfigDiagnostic.Codes.Missing)).IsTrue();
    }

    [Test]
    public async Task Ini_coerces_and_range_checks_numeric_strings()
    {
        IReadOnlyList<PzConfigDiagnostic> ok = Validate(PzConfigKind.Ini, "MaxPlayers=32\n");
        await Assert.That(ok.Any(d => d.Severity == PzDiagnosticSeverity.Error)).IsFalse();

        IReadOnlyList<PzConfigDiagnostic> bad = Validate(PzConfigKind.Ini, "MaxPlayers=500\n");
        await Assert.That(HasError(bad, PzConfigDiagnostic.Codes.OutOfRange, "MaxPlayers")).IsTrue();
    }

    [Test]
    public async Task Ini_flags_a_non_boolean_where_a_boolean_is_expected()
    {
        IReadOnlyList<PzConfigDiagnostic> diagnostics = Validate(PzConfigKind.Ini, "PVP=maybe\n");

        await Assert.That(HasError(diagnostics, PzConfigDiagnostic.Codes.WrongType, "PVP")).IsTrue();
    }

    [Test]
    public async Task Ini_reports_an_unknown_key_as_info()
    {
        IReadOnlyList<PzConfigDiagnostic> diagnostics = Validate(PzConfigKind.Ini, "SomeFutureSetting=1\n");

        await Assert.That(diagnostics.Any(d => d.Code == PzConfigDiagnostic.Codes.UnknownKey)).IsTrue();
    }

    [Test]
    public async Task A_valid_spawnregions_file_has_no_errors()
    {
        const string src = "function SpawnRegions()\n\treturn {\n\t\t{ name = \"Muldraugh, KY\", file = \"media/maps/Muldraugh, KY/spawnpoints.lua\" },\n\t\t{ name = \"Custom\", serverfile = \"zwarden_spawnpoints.lua\" },\n\t}\nend\n";

        IReadOnlyList<PzConfigDiagnostic> diagnostics = Validate(PzConfigKind.SpawnRegions, src);

        await Assert.That(diagnostics.Any(d => d.Severity == PzDiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task A_spawnregion_without_file_or_serverfile_is_an_error()
    {
        const string src = "function SpawnRegions()\n\treturn {\n\t\t{ name = \"Broken\" },\n\t}\nend\n";

        IReadOnlyList<PzConfigDiagnostic> diagnostics = Validate(PzConfigKind.SpawnRegions, src);

        await Assert.That(diagnostics.Any(d => d.Code == PzConfigDiagnostic.Codes.Missing)).IsTrue();
    }

    [Test]
    public async Task A_valid_spawnpoints_file_has_no_errors()
    {
        const string src = "function SpawnPoints()\n\treturn {\n\t\tunemployed = {\n\t\t\t{ posX = 100, posY = 200, posZ = 0 }\n\t\t}\n\t}\nend\n";

        IReadOnlyList<PzConfigDiagnostic> diagnostics = Validate(PzConfigKind.SpawnPoints, src);

        await Assert.That(diagnostics.Any(d => d.Severity == PzDiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task A_spawnpoint_cell_missing_a_coordinate_is_an_error()
    {
        const string src = "function SpawnPoints()\n\treturn {\n\t\tunemployed = {\n\t\t\t{ posX = 100 }\n\t\t}\n\t}\nend\n";

        IReadOnlyList<PzConfigDiagnostic> diagnostics = Validate(PzConfigKind.SpawnPoints, src);

        await Assert.That(diagnostics.Any(d => d.Code == PzConfigDiagnostic.Codes.Missing)).IsTrue();
    }
}
