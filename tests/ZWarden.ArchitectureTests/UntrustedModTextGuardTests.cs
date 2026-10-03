using System.Text.RegularExpressions;

namespace ZWarden.ArchitectureTests;

/// <summary>
/// #290 (trust-boundaries §8): Workshop text is untrusted. Two source-text scans, in the style of
/// <see cref="InteractiveDataAccessGuardTests"/>:
/// <list type="bullet">
/// <item><b>Mod ids reach config only as <c>PzModId</c>.</b> A guess parsed from a description (or an id from
/// <c>mod.info</c>) can't be handed to <c>ModListEditor</c> or stored on a <c>ServerWorkshopItem</c> as a bare
/// string; it must pass the rule first. The only string lists <c>ModListEditor</c> takes are the current config
/// lists and Workshop ids.</item>
/// <item><b>Steam text stays data.</b> <c>MarkupString</c> (raw HTML) is used only in the named seams below, so no
/// Steam title, description or tag can be rendered unescaped.</item>
/// </list>
/// </summary>
public partial class UntrustedModTextGuardTests
{
    // ModListEditor's string-list parameters that are allowed: the current config lists as read, and Workshop ids
    // (numeric, validated by the caller). A new mod-id intent parameter must be IReadOnlyList<PzModId>.
    private static readonly string[] AllowedEditorStringLists =
        ["enabledModIds", "configuredWorkshopIds", "workshopIdsToRemove"];

    // The only files allowed to build raw HTML: the authenticator QR code (an SVG we generate from our own data).
    private static readonly string[] MarkupStringSeams =
    [
        Path.Combine("src", "ZWarden.Web", "Components", "Account", "AuthenticatorQrCode.cs"),
        Path.Combine("src", "ZWarden.Web", "Components", "Account", "Shared", "AuthenticatorSetupPanel.razor"),
    ];

    [Test]
    public async Task Mod_list_editor_takes_new_mod_ids_only_as_pz_mod_ids()
    {
        string code = await ReadCodeAsync(Path.Combine("src", "ZWarden.Application", "Mods", "ModListEditor.cs"));

        string[] parameters = [.. PublicStringListParameters(code)];
        string[] violations = [.. parameters
            .Where(name => !AllowedEditorStringLists.Contains(name, StringComparer.Ordinal))
            .Select(name => $"ModListEditor parameter '{name}' is IReadOnlyList<string>: a mod-id intent must be IReadOnlyList<PzModId> (#290).")];

        await Assert.That(violations).IsEmpty();
        // Not vacuous: the scan does see the current-list parameters it allows.
        await Assert.That(parameters).Contains("enabledModIds");
    }

    [Test]
    public async Task Server_workshop_item_stores_mod_ids_only_as_pz_mod_ids()
    {
        string code = await ReadCodeAsync(Path.Combine("src", "ZWarden.Domain", "Mods", "ServerWorkshopItem.cs"));

        string[] violations = [.. PublicStringListParameters(code)
            .Where(name => name.EndsWith("ModIds", StringComparison.Ordinal))
            .Select(name => $"ServerWorkshopItem parameter '{name}' is IReadOnlyList<string>: mod ids must arrive as PzModId (#290).")];

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Raw_html_is_built_only_in_the_named_seams()
    {
        string web = Path.Combine(RepoRoot(), "src", "ZWarden.Web");
        List<string> violations = [];

        foreach (string file in Directory.EnumerateFiles(web, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            string relative = Path.GetRelativePath(RepoRoot(), file);
            if (MarkupStringSeams.Contains(relative, StringComparer.Ordinal))
            {
                continue;
            }

            if ((await ReadCodeAsync(relative)).Contains("MarkupString", StringComparison.Ordinal))
            {
                violations.Add($"MarkupString in {relative} - untrusted text (Steam titles, descriptions, tags) must render escaped (#290, trust-boundaries §8).");
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    // The names of IReadOnlyList<string> parameters on public methods (private helpers may shuffle raw lists).
    private static IEnumerable<string> PublicStringListParameters(string code) =>
        PublicMethodParameters().Matches(code)
            .SelectMany(m => StringListParameter().Matches(m.Groups["params"].Value + ")"))
            .Select(m => m.Groups["name"].Value);

    // The parameter list of a public method signature.
    [GeneratedRegex(@"public\s+(?:static\s+)?[\w<>\[\]?,\s]+?\s+\w+\s*\((?<params>[^)]*)\)")]
    private static partial Regex PublicMethodParameters();

    // "IReadOnlyList<string> name" as a parameter (followed by , or )).
    [GeneratedRegex(@"IReadOnlyList<string>\s+(?<name>\w+)\s*[,)]")]
    private static partial Regex StringListParameter();

    private static async Task<string> ReadCodeAsync(string relative) =>
        string.Join('\n', (await File.ReadAllLinesAsync(Path.Combine(RepoRoot(), relative)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ZWarden.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (ZWarden.slnx).");
    }
}
