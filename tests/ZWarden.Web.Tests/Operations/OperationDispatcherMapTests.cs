using ZWarden.Application.Backups;
using ZWarden.Application.Configuration;
using ZWarden.Application.Console;
using ZWarden.Application.Players;
using ZWarden.Application.Servers;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Operations;
using ZWarden.Web.Operations;

namespace ZWarden.Web.Tests.Operations;

/// <summary>
/// F15: the operation-kind → command map (<see cref="OperationDispatcher.CommandFor"/>) — one place that turns
/// each <see cref="OperationKind"/> into the payload-free command it dispatches. The lifecycle kinds map to the
/// lifecycle commands; the diagnostic/provisioning maps are unregressed; an unmapped kind throws rather than
/// dispatching a wrong command.
/// </summary>
public class OperationDispatcherMapTests
{
    private static readonly int[] GracefulLeads = [60, 10];

    [Test]
    public async Task Diagnostic_and_provisioning_kinds_map_to_their_commands()
    {
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.DiagnosticsPing)).IsTypeOf<PingAgent>();
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.DiagnosticsDockerHealth)).IsTypeOf<ProbeDockerHealth>();
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.ProvisionServer)).IsTypeOf<CreateServer>();
    }

    [Test]
    public async Task A_provision_without_a_payload_leaves_the_stride_to_the_agent()
    {
        var command = (CreateServer)OperationDispatcher.CommandFor(OperationKind.ProvisionServer);
        await Assert.That(command.GamePort).IsNull();
    }

    [Test]
    public async Task A_provision_payload_carries_the_chosen_game_port()
    {
        var command = (CreateServer)OperationDispatcher.CommandFor(
            OperationKind.ProvisionServer, new ServerContainerPayload(27015).ToJson());
        await Assert.That(command.GamePort).IsEqualTo(27015);
    }

    [Test]
    public async Task A_recreate_payload_maps_to_the_recreate_command_with_its_port_and_plan()
    {
        var command = (RecreateServer)OperationDispatcher.CommandFor(
            OperationKind.RecreateServer,
            new ServerContainerPayload(27015, new GracefulRestartPayload([60], "Changing ports.")).ToJson());

        await Assert.That(command.GamePort).IsEqualTo(27015);
        await Assert.That(command.Plan!.WarningLeadSeconds).IsEquivalentTo([60]);
        await Assert.That(command.Plan!.Reason).IsEqualTo("Changing ports.");
    }

    [Test]
    public async Task A_recreate_without_a_plan_uses_the_agents_default_warning()
    {
        var command = (RecreateServer)OperationDispatcher.CommandFor(
            OperationKind.RecreateServer, new ServerContainerPayload(null).ToJson());

        await Assert.That(command.GamePort).IsNull();
        await Assert.That(command.Plan).IsNull();
    }
    // #230: a stand-in for ISecretProtector.UnprotectString — the stored password is never plaintext.
    private static string Unprotect(string envelope) => envelope.StartsWith("enc:", StringComparison.Ordinal)
        ? envelope["enc:".Length..]
        : throw new InvalidOperationException("not an envelope");

    [Test]
    public async Task A_provision_payload_carries_the_heap_and_the_initial_settings_with_the_password_decrypted()
    {
        const long Heap = 6L * 1024 * 1024 * 1024;
        string payload = new ServerContainerPayload(
            null, HeapSizeBytes: Heap,
            Settings: new InitialSettingsPayload(true, "Knox", 12, ProtectedPassword: "enc:hunter2", "Hi")).ToJson();

        var command = (CreateServer)OperationDispatcher.CommandFor(OperationKind.ProvisionServer, payload, Unprotect);

        await Assert.That(command.HeapSizeBytes).IsEqualTo(Heap);
        await Assert.That(command.Settings).IsEqualTo(new InitialServerSettings(true, "Knox", 12, "hunter2", "Hi"));
    }

    [Test]
    public async Task A_stored_provision_payload_never_holds_the_password_in_plaintext()
    {
        string payload = new ServerContainerPayload(
            null, Settings: new InitialSettingsPayload(null, null, null, ProtectedPassword: "enc:hunter2", null)).ToJson();

        await Assert.That(payload).DoesNotContain("\"hunter2\"");
    }

    [Test]
    public async Task A_protected_password_cannot_be_dispatched_without_the_protector()
    {
        string payload = new ServerContainerPayload(
            null, Settings: new InitialSettingsPayload(null, null, null, ProtectedPassword: "enc:x", null)).ToJson();

        await Assert.That(() => OperationDispatcher.CommandFor(OperationKind.ProvisionServer, payload))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task A_recreate_payload_carries_a_new_heap()
    {
        var command = (RecreateServer)OperationDispatcher.CommandFor(
            OperationKind.RecreateServer, new ServerContainerPayload(null, HeapSizeBytes: 8L * 1024 * 1024 * 1024).ToJson());

        await Assert.That(command.HeapSizeBytes).IsEqualTo(8L * 1024 * 1024 * 1024);
    }

    [Test]
    public async Task Lifecycle_kinds_map_to_the_lifecycle_commands()
    {
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.StartServer)).IsTypeOf<StartServer>();
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.StopServer)).IsTypeOf<StopServer>();
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.RestartServer)).IsTypeOf<RestartServer>();
    }

    [Test]
    public async Task A_plain_restart_carries_no_graceful_plan()
    {
        // #114: no payload ⇒ the Agent applies its default warning schedule.
        var command = (RestartServer)OperationDispatcher.CommandFor(OperationKind.RestartServer);
        await Assert.That(command.Plan).IsNull();
    }

    [Test]
    public async Task A_restart_payload_maps_to_the_graceful_plan()
    {
        // #114: an operator-chosen countdown/message rides the command payload onto the wire command.
        string payload = new GracefulRestartPayload(GracefulLeads, "Applying mod changes.").ToJson();

        var command = (RestartServer)OperationDispatcher.CommandFor(OperationKind.RestartServer, payload);

        await Assert.That(command.Plan).IsNotNull();
        await Assert.That(command.Plan!.WarningLeadSeconds).IsEquivalentTo(GracefulLeads);
        await Assert.That(command.Plan!.Reason).IsEqualTo("Applying mod changes.");
    }

    [Test]
    public async Task The_update_kind_maps_to_the_update_command()
    {
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.UpdateServer)).IsTypeOf<UpdateServer>();
    }

    [Test]
    public async Task The_rcon_health_kind_maps_to_the_rcon_probe_command()
    {
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.RconHealthProbe)).IsTypeOf<ProbeRconHealth>();
    }

    [Test]
    public async Task The_list_players_kind_maps_to_the_list_command()
    {
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.ListPlayers)).IsTypeOf<ListPlayers>();
    }

    [Test]
    public async Task The_diagnostics_gather_kinds_map_to_their_gather_commands()
    {
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.GatherHostDiagnostics)).IsTypeOf<GatherHostDiagnostics>();
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.GatherServerDiagnostics)).IsTypeOf<GatherServerDiagnostics>();
    }

    [Test]
    public async Task The_mod_discovery_kind_maps_to_the_discover_command()
    {
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.ModDiscovery)).IsTypeOf<DiscoverMods>();
    }

    [Test]
    public async Task The_player_action_kinds_read_their_target_from_the_command_payload()
    {
        string kickJson = new PlayerCommandPayload(Username: "Bob", Reason: "grief").ToJson();
        var kick = (KickPlayer)OperationDispatcher.CommandFor(OperationKind.KickPlayer, kickJson);
        await Assert.That(kick.Username).IsEqualTo("Bob");
        await Assert.That(kick.Reason).IsEqualTo("grief");

        string userJson = new PlayerCommandPayload(Username: "Mallory").ToJson();
        await Assert.That(((BanPlayer)OperationDispatcher.CommandFor(OperationKind.BanPlayer, userJson)).Username).IsEqualTo("Mallory");
        await Assert.That(((UnbanPlayer)OperationDispatcher.CommandFor(OperationKind.UnbanPlayer, userJson)).Username).IsEqualTo("Mallory");
        await Assert.That(((RemoveFromWhitelist)OperationDispatcher.CommandFor(OperationKind.RemoveFromWhitelist, userJson)).Username).IsEqualTo("Mallory");

        string modeJson = new PlayerCommandPayload(Open: false).ToJson();
        await Assert.That(((SetWhitelistMode)OperationDispatcher.CommandFor(OperationKind.SetWhitelistMode, modeJson)).Open).IsFalse();
    }

    [Test]
    public async Task A_player_action_kind_without_a_payload_throws()
    {
        await Assert.That(() => OperationDispatcher.CommandFor(OperationKind.KickPlayer)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task The_console_kind_reads_its_command_line_from_the_payload()
    {
        string json = new ConsoleCommandPayload("servermsg \"hello\"").ToJson();

        var console = (ExecuteConsoleCommand)OperationDispatcher.CommandFor(OperationKind.ExecuteConsoleCommand, json);

        await Assert.That(console.Input).IsEqualTo("servermsg \"hello\"");
    }

    [Test]
    public async Task The_console_kind_without_a_payload_throws()
    {
        await Assert.That(() => OperationDispatcher.CommandFor(OperationKind.ExecuteConsoleCommand)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task The_config_apply_kind_reads_its_file_baseline_and_edits_from_the_payload()
    {
        string json = new ConfigApplyPayload(
            PzConfigFile.SandboxVars,
            BaselineHash: "abc123",
            Edits:
            [
                new ConfigApplyEdit("Zombies", ConfigEditKind.Number, "3"),
                new ConfigApplyEdit("PublicName", ConfigEditKind.Text, "My Server"),
            ]).ToJson();

        var apply = (ConfigApply)OperationDispatcher.CommandFor(OperationKind.ConfigApply, json);

        await Assert.That(apply.File).IsEqualTo(PzConfigFile.SandboxVars);
        await Assert.That(apply.BaselineHash).IsEqualTo("abc123");
        await Assert.That(apply.Edits.Count).IsEqualTo(2);
        await Assert.That(apply.Edits[0]).IsEqualTo(new ConfigValueEdit("Zombies", ConfigValueKind.Number, "3"));
        await Assert.That(apply.Edits[1]).IsEqualTo(new ConfigValueEdit("PublicName", ConfigValueKind.Text, "My Server"));
    }

    [Test]
    public async Task The_config_apply_kind_without_a_payload_throws()
    {
        await Assert.That(() => OperationDispatcher.CommandFor(OperationKind.ConfigApply)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task The_raw_config_apply_kind_reads_its_file_baseline_and_correlation_from_the_payload()
    {
        string json = new ConfigApplyRawPayload(PzConfigFile.SandboxVars, BaselineHash: "abc123", CorrelationId: "corr-9").ToJson();

        var raw = (ConfigApplyRaw)OperationDispatcher.CommandFor(OperationKind.ConfigApplyRaw, json);

        await Assert.That(raw.File).IsEqualTo(PzConfigFile.SandboxVars);
        await Assert.That(raw.BaselineHash).IsEqualTo("abc123");
        await Assert.That(raw.CorrelationId).IsEqualTo("corr-9");
    }

    [Test]
    public async Task The_raw_config_apply_kind_without_a_payload_throws()
    {
        await Assert.That(() => OperationDispatcher.CommandFor(OperationKind.ConfigApplyRaw)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task The_backup_kind_maps_to_the_backup_command()
    {
        // The reason rides the payload for the ingest, not the Agent command — a backup takes no parameters.
        string json = new BackupCommandPayload(Reason: "Manual").ToJson();
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.Backup, json)).IsTypeOf<BackupServer>();
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.Backup)).IsTypeOf<BackupServer>();
    }

    [Test]
    public async Task The_delete_backup_kind_reads_its_archive_name_from_the_payload()
    {
        string json = new BackupCommandPayload(BackupId: "bkp-x", ArchiveName: "world-1.tar.gz").ToJson();
        var delete = (DeleteBackup)OperationDispatcher.CommandFor(OperationKind.DeleteBackup, json);
        await Assert.That(delete.ArchiveName).IsEqualTo("world-1.tar.gz");
    }

    [Test]
    public async Task A_delete_backup_kind_without_a_payload_throws()
    {
        await Assert.That(() => OperationDispatcher.CommandFor(OperationKind.DeleteBackup)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task The_restore_kind_reads_the_archive_name_and_checksum_from_the_payload()
    {
        string json = new RestoreCommandPayload(BackupId: "bkp-x", ArchiveName: "world-1.tar.gz", Sha256: "abc123").ToJson();
        var restore = (RestoreServer)OperationDispatcher.CommandFor(OperationKind.Restore, json);
        await Assert.That(restore.ArchiveName).IsEqualTo("world-1.tar.gz");
        await Assert.That(restore.Sha256).IsEqualTo("abc123");
    }

    [Test]
    public async Task A_restore_kind_without_a_payload_throws()
    {
        await Assert.That(() => OperationDispatcher.CommandFor(OperationKind.Restore)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task An_unmapped_kind_throws()
    {
        await Assert.That(() => OperationDispatcher.CommandFor((OperationKind)999)).Throws<NotSupportedException>();
    }
}
