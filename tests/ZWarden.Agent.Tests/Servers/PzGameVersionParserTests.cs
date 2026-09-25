using ZWarden.Agent.Servers;

namespace ZWarden.Agent.Tests.Servers;

/// <summary>
/// #262: the PZ server prints its game version early in boot, e.g.
/// <c> LOG  : General      f:0 st:1,802,558,676> version=42.20.4 b0bbce05d5 demo=false</c> (verified live on B42
/// 42.20.4). Container logs are untrusted, so the parse is total: only that General boot line yields a version.
/// </summary>
public class PzGameVersionParserTests
{
    private const string BootLine = " LOG  : General      f:0 st:1,802,558,676> version=42.20.4 b0bbce05d5 demo=false";

    [Test]
    public async Task The_verified_B42_boot_line_yields_its_version()
    {
        await Assert.That(PzGameVersionParser.Parse(BootLine)).IsEqualTo("42.20.4");
    }

    [Test]
    public async Task The_line_is_found_among_other_boot_output()
    {
        string log = string.Join('\n',
            "[zwarden] session start op=abc",
            " LOG  : General      f:0 st:1,802,558,600> Loading networking libraries...",
            BootLine,
            " LOG  : General      f:0 st:1,802,558,700> server is listening on port 16261");

        await Assert.That(PzGameVersionParser.Parse(log)).IsEqualTo("42.20.4");
    }

    [Test]
    public async Task A_line_without_the_revision_still_parses()
    {
        await Assert.That(PzGameVersionParser.Parse(" LOG  : General f:0 st:1> version=42.21 demo=false"))
            .IsEqualTo("42.21");
    }

    [Test]
    [Arguments("")]
    [Arguments("version=42.20.4 demo=false")] // not the General boot line
    [Arguments(" LOG  : Mod          f:0 st:1> version=1.2.3 demo=false")] // another subsystem's version
    [Arguments(" LOG  : General      f:0 st:1> version=forty-two demo=false")]
    [Arguments(" LOG  : General      f:0 st:1> loaded mod version=3.1.0")]
    public async Task Anything_else_is_null(string log)
    {
        await Assert.That(PzGameVersionParser.Parse(log)).IsNull();
    }

    [Test]
    public async Task An_oversized_line_is_skipped()
    {
        string huge = " LOG  : General      f:0 st:1> version=42.20.4 " + new string('x', 5_000) + " demo=false";

        await Assert.That(PzGameVersionParser.Parse(huge)).IsNull();
    }
}
