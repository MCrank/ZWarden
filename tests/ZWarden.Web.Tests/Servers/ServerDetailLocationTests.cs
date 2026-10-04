using ZWarden.Web.Components.Pages.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// #312: the Server Detail page keeps its own URL (an in-place switch is a <c>history.pushState</c> the circuit's
/// <c>NavigationManager</c> never sees), so it reads its query values from that URL itself.
/// </summary>
public class ServerDetailLocationTests
{
    [Test]
    public async Task Reads_the_section_file_op_and_add_values()
    {
        ServerDetailLocation location = ServerDetailLocation.Parse(
            "http://localhost/servers/srv-1?section=config&file=SandboxVars&op=op-9&add=1");

        await Assert.That(location.Section).IsEqualTo("config");
        await Assert.That(location.File).IsEqualTo("SandboxVars");
        await Assert.That(location.Op).IsEqualTo("op-9");
        await Assert.That(location.Add).IsEqualTo("1");
    }

    [Test]
    public async Task A_missing_value_is_null()
    {
        ServerDetailLocation location = ServerDetailLocation.Parse("http://localhost/servers/srv-1");

        await Assert.That(location.Section).IsNull();
        await Assert.That(location.File).IsNull();
        await Assert.That(location.Op).IsNull();
        await Assert.That(location.Add).IsNull();
    }

    [Test]
    public async Task Values_are_url_decoded_and_the_first_of_a_repeated_key_wins()
    {
        ServerDetailLocation location = ServerDetailLocation.Parse(
            "http://localhost/servers/srv-1?section=con%66ig&section=logs");

        await Assert.That(location.Section).IsEqualTo("config");
    }

    [Test]
    public async Task Is_on_the_page_of_its_own_server_only()
    {
        ServerDetailLocation location = ServerDetailLocation.Parse("http://localhost/Servers/SRV-1/?section=logs");

        await Assert.That(location.IsServerPage("srv-1")).IsTrue();
        await Assert.That(location.IsServerPage("srv-2")).IsFalse();
        await Assert.That(ServerDetailLocation.Parse("http://localhost/servers").IsServerPage("srv-1")).IsFalse();
        await Assert.That(ServerDetailLocation.Parse("http://localhost/servers/srv-1/x").IsServerPage("srv-1")).IsFalse();
    }

    [Test]
    public async Task A_url_that_is_not_absolute_is_not_a_server_page()
    {
        ServerDetailLocation location = ServerDetailLocation.Parse("not a url");

        await Assert.That(location.IsServerPage("srv-1")).IsFalse();
        await Assert.That(location.Section).IsNull();
    }
}
