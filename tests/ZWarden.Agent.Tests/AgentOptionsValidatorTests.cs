using ZWarden.Agent.Configuration;

namespace ZWarden.Agent.Tests;

/// <summary>
/// F8 test plan item 4: <see cref="AgentOptions"/> binds when valid and fails closed at startup on
/// invalid values, naming the offending option.
/// </summary>
public class AgentOptionsValidatorTests
{
    private static AgentOptions Valid() => new()
    {
        IdentityFilePath = Path.Combine(Path.GetTempPath(), "agent-id.txt"),
        ControlPlaneUri = "https://localhost:8443",
        HeartbeatInterval = TimeSpan.FromSeconds(30),
        HealthReportInterval = TimeSpan.FromSeconds(15),
        ShutdownTimeout = TimeSpan.FromSeconds(10),
    };

    [Test]
    public async Task Valid_options_pass()
    {
        var result = new AgentOptionsValidator().Validate(name: null, Valid());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task Empty_identity_path_fails(string path)
    {
        var options = Valid();
        options.IdentityFilePath = path;

        var result = new AgentOptionsValidator().Validate(name: null, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage!).Contains(nameof(AgentOptions.IdentityFilePath));
    }

    [Test]
    [Arguments("not-a-uri")]
    [Arguments("/relative/path")]
    [Arguments("http://insecure:8080")]
    [Arguments("ftp://host")]
    public async Task Non_absolute_or_insecure_control_plane_uri_fails(string uri)
    {
        var options = Valid();
        options.ControlPlaneUri = uri;

        var result = new AgentOptionsValidator().Validate(name: null, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage!).Contains(nameof(AgentOptions.ControlPlaneUri));
    }

    [Test]
    [Arguments("https://cp.example:8443")]
    [Arguments("wss://cp.example:8443/agents")]
    public async Task Https_or_wss_control_plane_uri_passes(string uri)
    {
        var options = Valid();
        options.ControlPlaneUri = uri;

        var result = new AgentOptionsValidator().Validate(name: null, options);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Non_positive_heartbeat_interval_fails()
    {
        var options = Valid();
        options.HeartbeatInterval = TimeSpan.Zero;

        var result = new AgentOptionsValidator().Validate(name: null, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage!).Contains(nameof(AgentOptions.HeartbeatInterval));
    }

    [Test]
    public async Task Non_positive_health_report_interval_fails()
    {
        var options = Valid();
        options.HealthReportInterval = TimeSpan.FromSeconds(-1);

        var result = new AgentOptionsValidator().Validate(name: null, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage!).Contains(nameof(AgentOptions.HealthReportInterval));
    }

    [Test]
    public async Task Non_positive_shutdown_timeout_fails()
    {
        var options = Valid();
        options.ShutdownTimeout = TimeSpan.Zero;

        var result = new AgentOptionsValidator().Validate(name: null, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage!).Contains(nameof(AgentOptions.ShutdownTimeout));
    }
}
