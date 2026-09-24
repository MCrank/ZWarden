using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Configuration;
using ZWarden.Web.Agents;

namespace ZWarden.Web.Tests.Agents;

/// <summary>#225: the result line a completed configuration write shows — live now, or waiting for a (re)start.</summary>
public class ConfigReloadTextTests
{
    [Test]
    [Arguments(ConfigReloadOutcome.Reloaded, null, "Applied and reloaded live on the running server.")]
    [Arguments(ConfigReloadOutcome.NotRunning, null, "Applied; the server is not running, so it takes effect when it next starts.")]
    [Arguments(ConfigReloadOutcome.Failed, "RCON is disabled on this server.", "Applied; takes effect on the next restart (the live reload failed: RCON is disabled on this server.)")]
    [Arguments(ConfigReloadOutcome.Failed, null, "Applied; takes effect on the next restart (the live reload failed).")]
    [Arguments(ConfigReloadOutcome.NotAttempted, null, "Applied; takes effect on the next restart.")]
    public async Task An_ini_write_describes_its_reload_outcome(ConfigReloadOutcome outcome, string? detail, string expected)
    {
        var config = new ConfigApplyResult(PzConfigFile.Ini, "h", "[]", 1, outcome, detail);

        await Assert.That(ConfigReloadText.Describe(config)).IsEqualTo(expected);
    }

    [Test]
    public async Task A_sandbox_write_waits_for_a_restart_and_an_unchanged_write_has_no_line()
    {
        await Assert.That(ConfigReloadText.Describe(new ConfigApplyResult(PzConfigFile.SandboxVars, "h", "[]", 2)))
            .IsEqualTo("Applied; takes effect on the next restart (this file cannot be reloaded live).");
        await Assert.That(ConfigReloadText.Describe(new ConfigApplyResult(PzConfigFile.Ini, "h", "[]", 0))).IsNull();
    }
}
