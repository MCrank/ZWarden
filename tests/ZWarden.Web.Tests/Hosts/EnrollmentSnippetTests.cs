using ZWarden.Web.Components.Hosts;

namespace ZWarden.Web.Tests.Hosts;

/// <summary>#342: the Agent .env lines shown with a fresh enrollment token.</summary>
public sealed class EnrollmentSnippetTests
{
    [Test]
    public async Task The_domain_drops_the_default_port()
    {
        await Assert.That(EnrollmentSnippet.Domain(new Uri("https://zwarden.example.com/"))).IsEqualTo("zwarden.example.com");
    }

    [Test]
    public async Task The_domain_keeps_a_custom_port()
    {
        await Assert.That(EnrollmentSnippet.Domain(new Uri("https://zw.lan:8443/hosts"))).IsEqualTo("zw.lan:8443");
    }

    [Test]
    public async Task The_env_lines_name_the_domain_and_the_secret()
    {
        string lines = EnrollmentSnippet.EnvLines(new Uri("https://zwarden.example.com/"), "zwe_abc");

        await Assert.That(lines).IsEqualTo("ZWARDEN_DOMAIN=zwarden.example.com\nZWARDEN_ENROLLMENT_SECRET=zwe_abc");
    }
}
