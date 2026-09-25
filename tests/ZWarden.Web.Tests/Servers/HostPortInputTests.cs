using ZWarden.Web.Components.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>#229: the optional game-port form field — blank is "no choice", otherwise a whole, in-range port.</summary>
public class HostPortInputTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task Blank_is_no_choice(string? text)
    {
        bool ok = HostPortInput.TryParse(text, out int? port, out string? error);

        await Assert.That(ok).IsTrue();
        await Assert.That(port).IsNull();
        await Assert.That(error).IsNull();
    }

    [Test]
    public async Task A_whole_in_range_port_is_accepted()
    {
        bool ok = HostPortInput.TryParse(" 27015 ", out int? port, out _);

        await Assert.That(ok).IsTrue();
        await Assert.That(port).IsEqualTo(27015);
    }

    [Test]
    [Arguments("abc")]
    [Arguments("27015.5")]
    [Arguments("-5")]
    [Arguments("80")]
    [Arguments("65535")]
    public async Task Anything_else_is_refused_with_a_reason(string text)
    {
        bool ok = HostPortInput.TryParse(text, out int? port, out string? error);

        await Assert.That(ok).IsFalse();
        await Assert.That(port).IsNull();
        await Assert.That(error).IsNotNull();
    }
}
