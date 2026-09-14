using System.Text;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Internal;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// The ZWarden-side size and nesting-depth pre-check that runs BEFORE Loretta sees a file
/// (ADR 0010, trust-boundaries §8). No Lua parser survives hostile nesting - the process simply
/// dies with an uncatchable StackOverflowException (research §5) - so this scanner is the guard,
/// and it must count braces that are real structure while ignoring braces that live inside comments
/// or strings. It is deliberately conservative: an ambiguous (malformed) file counts braces rather
/// than skipping them, so a bomb can never hide behind an unterminated string.
/// </summary>
public class PzConfigPreCheckTests
{
    private static ReadOnlySpan<byte> Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static PzConfigDiagnostic? Check(string content, PzConfigKind kind = PzConfigKind.SandboxVars, PzConfigLimits? limits = null) =>
        PzConfigPreCheck.Check(Bytes(content), kind, limits ?? PzConfigLimits.Default);

    [Test]
    public async Task A_normal_file_passes()
    {
        PzConfigDiagnostic? result = Check("SandboxVars = {\n    Zombies = 4,\n    Map = {\n        AllowMiniMap = false,\n    },\n}\n");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Oversized_file_is_rejected_before_parsing()
    {
        var limits = new PzConfigLimits { MaxByteLength = 32 };
        PzConfigDiagnostic? result = Check(new string('x', 100), limits: limits);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Code).IsEqualTo(PzConfigDiagnostic.Codes.TooLarge);
        await Assert.That(result.Severity).IsEqualTo(PzDiagnosticSeverity.Error);
    }

    [Test]
    public async Task Depth_within_the_cap_passes()
    {
        var limits = new PzConfigLimits { MaxNestingDepth = 4 };

        // t = { { { { } } } }  -> max depth 4
        PzConfigDiagnostic? result = Check("t = { { { { } } } }", limits: limits);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Depth_over_the_cap_is_rejected()
    {
        var limits = new PzConfigLimits { MaxNestingDepth = 4 };

        // five levels
        PzConfigDiagnostic? result = Check("t = { { { { { } } } } }", limits: limits);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Code).IsEqualTo(PzConfigDiagnostic.Codes.TooDeep);
    }

    [Test]
    public async Task Braces_in_a_line_comment_are_not_counted()
    {
        var limits = new PzConfigLimits { MaxNestingDepth = 2 };

        PzConfigDiagnostic? result = Check("t = { -- { { { { { { {\n}", limits: limits);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Braces_in_a_block_comment_are_not_counted()
    {
        var limits = new PzConfigLimits { MaxNestingDepth = 2 };

        PzConfigDiagnostic? result = Check("t = { --[[ { { { { { ]] }", limits: limits);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Braces_in_a_quoted_string_are_not_counted()
    {
        var limits = new PzConfigLimits { MaxNestingDepth = 2 };

        PzConfigDiagnostic? result = Check("t = { name = \"{{{{{{{{\" }", limits: limits);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Braces_in_a_long_bracket_string_are_not_counted()
    {
        var limits = new PzConfigLimits { MaxNestingDepth = 2 };

        PzConfigDiagnostic? result = Check("t = { s = [==[ {{{{{{ ]==] }", limits: limits);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task An_escaped_quote_does_not_end_the_string_so_inner_braces_stay_uncounted()
    {
        var limits = new PzConfigLimits { MaxNestingDepth = 2 };

        // The \" keeps the string open, so the { that follows is inside the string.
        PzConfigDiagnostic? result = Check("t = { name = \"a\\\"{{{{{{{\" }", limits: limits);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task A_bracketed_key_is_not_a_long_bracket_string()
    {
        var limits = new PzConfigLimits { MaxNestingDepth = 3 };

        // ["park ranger"] is an index/key bracket, not a [[ long string; the nested { must still count.
        PzConfigDiagnostic? result = Check("t = { [\"park ranger\"] = { { } } }", limits: limits);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task A_nesting_bomb_hidden_after_an_unterminated_string_is_still_rejected()
    {
        var limits = new PzConfigLimits { MaxNestingDepth = 8 };

        // Unterminated string then a deep run of braces. A scanner that skipped to EOF would
        // under-count and let the bomb through; the conservative scanner counts them.
        PzConfigDiagnostic? result = Check("t = \"oops" + new string('{', 64), limits: limits);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Code).IsEqualTo(PzConfigDiagnostic.Codes.TooDeep);
    }

    [Test]
    public async Task Ini_kind_skips_the_depth_scan()
    {
        var limits = new PzConfigLimits { MaxNestingDepth = 2 };

        // A '{' in an INI value must never trip the Lua depth scan - INI has no nesting.
        PzConfigDiagnostic? result = Check("PublicName={{{{{{{{{{", kind: PzConfigKind.Ini, limits: limits);

        await Assert.That(result).IsNull();
    }
}
