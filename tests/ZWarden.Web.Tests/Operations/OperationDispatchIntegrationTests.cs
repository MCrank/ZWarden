using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Agents;
using ZWarden.Application.Configuration;
using ZWarden.Application.Mods;
using ZWarden.Application.Operations;
using ZWarden.Application.Workshop;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Configuration;
using ZWarden.Infrastructure.Mods;
using ZWarden.Infrastructure.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.Web.Agents;
using ZWarden.Web.Tests.Account;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Web.Tests.Operations;

/// <summary>
/// F11 PR-B end-to-end over a real SignalR client against the booted host: an operator enqueues a
/// <c>Diagnostics.Ping</c>, the dispatcher sends the command down the F10 connection, the Agent (its real
/// <see cref="AgentCommandProcessor"/>) replies, and the hub ingest drives the operation to
/// <see cref="OperationState.Succeeded"/>. With no connected Agent the operation stays
/// <see cref="OperationState.Pending"/>. LongPolling keeps the test on the in-memory <c>TestServer</c>.
/// </summary>
public class OperationDispatchIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task A_ping_runs_end_to_end_to_succeeded()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        await using HubConnection connection = BuildConnection(factory, credential);

        // Stand in for the Agent: receive the dispatched command and report the result on the same operation.
        // (The Agent's real command processing is unit-tested in ZWarden.Agent.Tests.)
        connection.On<string>(AgentHubProtocol.ReceiveCommand, async json =>
        {
            Envelope<IProtocolMessage> command = ProtocolJson.Deserialize(json);
            if (command.Payload is PingAgent && command.OperationId is { } operationId)
            {
                Envelope<OperationCompleted> reply = Envelope.Create(
                    new OperationCompleted(OperationOutcome.Succeeded), Now, operationId: operationId);
                await connection.SendAsync(AgentHubProtocol.OperationCompleted, reply);
            }
        });

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(AgentHubProtocol.Hello, Hello(agentId));

        OperationId operationId;
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            IOperationCoordinator coordinator = scope.ServiceProvider.GetRequiredService<IOperationCoordinator>();
            Operation op = await coordinator.EnqueueAsync(
                new EnqueueOperationRequest(agentId, OperationKind.DiagnosticsPing, IsMutating: false, "e2e-ping"));
            operationId = op.Id;
        }

        Operation? final = await WaitForStateAsync(factory, operationId, OperationState.Succeeded);
        await Assert.That(final).IsNotNull();
        await Assert.That(final!.State).IsEqualTo(OperationState.Succeeded);
        await Assert.That(final.PercentComplete).IsEqualTo(100);
    }

    [Test]
    public async Task A_docker_health_probe_runs_end_to_end_to_succeeded()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        await using HubConnection connection = BuildConnection(factory, credential);

        // The dispatcher must map DiagnosticsDockerHealth to the ProbeDockerHealth command (F13). The stand-in
        // Agent verifies it received exactly that, then reports success. (The real probe is unit-tested.)
        bool receivedDockerHealthCommand = false;
        connection.On<string>(AgentHubProtocol.ReceiveCommand, async json =>
        {
            Envelope<IProtocolMessage> command = ProtocolJson.Deserialize(json);
            if (command.Payload is ProbeDockerHealth && command.OperationId is { } operationId)
            {
                receivedDockerHealthCommand = true;
                Envelope<OperationCompleted> reply = Envelope.Create(
                    new OperationCompleted(OperationOutcome.Succeeded), Now, operationId: operationId);
                await connection.SendAsync(AgentHubProtocol.OperationCompleted, reply);
            }
        });

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(AgentHubProtocol.Hello, Hello(agentId));

        OperationId operationId;
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            IOperationCoordinator coordinator = scope.ServiceProvider.GetRequiredService<IOperationCoordinator>();
            Operation op = await coordinator.EnqueueAsync(
                new EnqueueOperationRequest(agentId, OperationKind.DiagnosticsDockerHealth, IsMutating: false, "e2e-docker-health"));
            operationId = op.Id;
        }

        Operation? final = await WaitForStateAsync(factory, operationId, OperationState.Succeeded);
        await Assert.That(receivedDockerHealthCommand).IsTrue();
        await Assert.That(final).IsNotNull();
        await Assert.That(final!.State).IsEqualTo(OperationState.Succeeded);
    }

    [Test]
    public async Task A_config_apply_runs_end_to_end_and_records_a_revision()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, agentId);
        await using HubConnection connection = BuildConnection(factory, credential);

        // The stand-in Agent verifies it received a ConfigApply carrying the file, baseline and edits from the
        // command payload, then reports the recorded revision. (The real drift-checked write is unit-tested.)
        ConfigApply? received = null;
        connection.On<string>(AgentHubProtocol.ReceiveCommand, async json =>
        {
            Envelope<IProtocolMessage> command = ProtocolJson.Deserialize(json);
            if (command.Payload is ConfigApply apply && command.OperationId is { } operationId)
            {
                received = apply;
                Envelope<OperationCompleted> reply = Envelope.Create(
                    new OperationCompleted(
                        OperationOutcome.Succeeded,
                        Config: new ConfigApplyResult(apply.File, "new-hash-42", "[[\"Zombies\",\"n:1:i\"]]", 1)),
                    Now, serverId: command.ServerId, operationId: operationId);
                await connection.SendAsync(AgentHubProtocol.OperationCompleted, reply);
            }
        });

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(AgentHubProtocol.Hello, Hello(agentId));

        OperationId operationId;
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            IOperationCoordinator coordinator = scope.ServiceProvider.GetRequiredService<IOperationCoordinator>();
            string payload = new ConfigApplyPayload(
                PzConfigFile.SandboxVars, "base-1", [new ConfigApplyEdit("Zombies", ConfigEditKind.Number, "1")]).ToJson();
            Operation op = await coordinator.EnqueueAsync(
                new EnqueueOperationRequest(
                    agentId, OperationKind.ConfigApply, IsMutating: true, "e2e-config-apply",
                    ServerId: serverId, CommandPayload: payload));
            operationId = op.Id;
        }

        Operation? final = await WaitForStateAsync(factory, operationId, OperationState.Succeeded);
        await Assert.That(final).IsNotNull();
        await Assert.That(received).IsNotNull();
        await Assert.That(received!.File).IsEqualTo(PzConfigFile.SandboxVars);
        await Assert.That(received.BaselineHash).IsEqualTo("base-1");
        await Assert.That(received.Edits[0].Path).IsEqualTo("Zombies");

        // The completion recorded a Configuration Revision (the new drift baseline) against the Server.
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            ConfigurationRevisionRepository revisions =
                new(scope.ServiceProvider.GetRequiredService<ZWardenDbContext>());
            ConfigurationRevision? latest = await revisions.FindLatestAsync(serverId, PzConfigFile.SandboxVars);
            await Assert.That(latest).IsNotNull();
            await Assert.That(latest!.SnapshotHash).IsEqualTo("new-hash-42");
            await Assert.That(latest.CanonicalSnapshot).IsEqualTo("[[\"Zombies\",\"n:1:i\"]]");
            // #225: a sandbox write is never reloaded live — the result line says it waits for a restart.
            await Assert.That(final!.StatusLine!).Contains("next restart");
        }

        await connection.StopAsync();
    }

    [Test]
    public async Task A_live_reloaded_ini_apply_says_so_on_the_operation_and_in_the_audit_trail()
    {
        // #225: the Agent reports the INI write was made live with reloadoptions; the control plane records that as
        // the Operation's result line and a Server.ConfigurationLiveReload audit entry.
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, agentId);
        await using HubConnection connection = BuildConnection(factory, credential);

        connection.On<string>(AgentHubProtocol.ReceiveCommand, async json =>
        {
            Envelope<IProtocolMessage> command = ProtocolJson.Deserialize(json);
            if (command.Payload is ConfigApply apply && command.OperationId is { } operationId)
            {
                Envelope<OperationCompleted> reply = Envelope.Create(
                    new OperationCompleted(
                        OperationOutcome.Succeeded,
                        Config: new ConfigApplyResult(
                            apply.File, "ini-hash", "[[\"MaxPlayers\",\"s:20\"]]", 1, ConfigReloadOutcome.Reloaded)),
                    Now, serverId: command.ServerId, operationId: operationId);
                await connection.SendAsync(AgentHubProtocol.OperationCompleted, reply);
            }
        });

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(AgentHubProtocol.Hello, Hello(agentId));

        OperationId operationId;
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            IOperationCoordinator coordinator = scope.ServiceProvider.GetRequiredService<IOperationCoordinator>();
            string payload = new ConfigApplyPayload(
                PzConfigFile.Ini, "base-1", [new ConfigApplyEdit("MaxPlayers", ConfigEditKind.Number, "20")]).ToJson();
            Operation op = await coordinator.EnqueueAsync(
                new EnqueueOperationRequest(
                    agentId, OperationKind.ConfigApply, IsMutating: true, "e2e-config-reload",
                    ServerId: serverId, CommandPayload: payload));
            operationId = op.Id;
        }

        Operation? final = await WaitForStateAsync(factory, operationId, OperationState.Succeeded);
        await Assert.That(final!.StatusLine).IsEqualTo("Applied and reloaded live on the running server.");

        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            AuditEvent? reload = db.Set<AuditEvent>().AsEnumerable()
                .FirstOrDefault(e => e.Action == ConfigurationAuditActions.LiveReload && e.ServerId == serverId);
            await Assert.That(reload).IsNotNull();
            await Assert.That(reload!.Outcome).IsEqualTo(AuditOutcome.Succeeded);
            await Assert.That(reload.Detail!).Contains("reloaded live");
        }

        await connection.StopAsync();
    }

    [Test]
    public async Task A_game_update_says_which_build_it_moved_between_on_the_operation_and_in_the_audit_trail()
    {
        // #273: the Agent reports the build it replaced and the one now installed; the control plane records that as
        // the Operation's result line (the server page shows it) and a Server.GameUpdated audit entry.
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, agentId);
        await using HubConnection connection = BuildConnection(factory, credential);

        connection.On<string>(AgentHubProtocol.ReceiveCommand, async json =>
        {
            Envelope<IProtocolMessage> command = ProtocolJson.Deserialize(json);
            if (command.Payload is UpdateServer && command.OperationId is { } operationId)
            {
                Envelope<OperationCompleted> reply = Envelope.Create(
                    new OperationCompleted(OperationOutcome.Succeeded, Update: new UpdateResult("25485538", "24909836")),
                    Now, serverId: command.ServerId, operationId: operationId);
                await connection.SendAsync(AgentHubProtocol.OperationCompleted, reply);
            }
        });

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(AgentHubProtocol.Hello, Hello(agentId));

        OperationId operationId;
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            IOperationCoordinator coordinator = scope.ServiceProvider.GetRequiredService<IOperationCoordinator>();
            Operation op = await coordinator.EnqueueAsync(
                new EnqueueOperationRequest(
                    agentId, OperationKind.UpdateServer, IsMutating: true, "e2e-game-update", ServerId: serverId));
            operationId = op.Id;
        }

        Operation? final = await WaitForStateAsync(factory, operationId, OperationState.Succeeded);
        await Assert.That(final!.StatusLine).IsEqualTo("Updated from Steam build 24909836 to 25485538.");

        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            AuditEvent? updated = db.Set<AuditEvent>().AsEnumerable()
                .FirstOrDefault(e => e.Action == ServerAuditActions.GameUpdated && e.ServerId == serverId);
            await Assert.That(updated).IsNotNull();
            await Assert.That(updated!.Outcome).IsEqualTo(AuditOutcome.Succeeded);
            await Assert.That(updated.Detail!).Contains("24909836 to 25485538");
        }

        await connection.StopAsync();
    }

    [Test]
    public async Task A_mod_discovery_runs_end_to_end_and_caches_the_inventory()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, agentId);
        await using HubConnection connection = BuildConnection(factory, credential);

        // The dispatcher must map ModDiscovery to the DiscoverMods command (F21). The stand-in Agent verifies it
        // received exactly that, then reports an observed inventory. (The real disk walk is unit-tested.)
        bool receivedDiscoverMods = false;
        connection.On<string>(AgentHubProtocol.ReceiveCommand, async json =>
        {
            Envelope<IProtocolMessage> command = ProtocolJson.Deserialize(json);
            if (command.Payload is DiscoverMods && command.OperationId is { } operationId)
            {
                receivedDiscoverMods = true;
                Envelope<OperationCompleted> reply = Envelope.Create(
                    new OperationCompleted(
                        OperationOutcome.Succeeded,
                        Mods: new ModDiscoveryResult(
                            InstalledItems: [new DiscoveredWorkshopItem("111", [new DiscoveredMod("ModA", "Mod A")])],
                            ConfiguredWorkshopIds: ["111"],
                            EnabledModIds: ["ModA"],
                            Findings: [new ModCompatFinding(ModCompatKind.InstalledButInactive, "Idle", null)])),
                    Now, serverId: command.ServerId, operationId: operationId);
                await connection.SendAsync(AgentHubProtocol.OperationCompleted, reply);
            }
        });

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(AgentHubProtocol.Hello, Hello(agentId));

        OperationId operationId;
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            IOperationCoordinator coordinator = scope.ServiceProvider.GetRequiredService<IOperationCoordinator>();
            Operation op = await coordinator.EnqueueAsync(
                new EnqueueOperationRequest(
                    agentId, OperationKind.ModDiscovery, IsMutating: false, "e2e-mod-discovery", ServerId: serverId));
            operationId = op.Id;
        }

        Operation? final = await WaitForStateAsync(factory, operationId, OperationState.Succeeded);
        await Assert.That(final).IsNotNull();
        await Assert.That(receivedDiscoverMods).IsTrue();

        // The completion cached the observed inventory (recorded before the operation is marked done),
        // ownership-guarded by the reporting Agent.
        IModInventoryCache cache = factory.Services.GetRequiredService<IModInventoryCache>();
        ModInventory? inventory = cache.GetLatest(serverId, agentId);
        await Assert.That(inventory).IsNotNull();
        await Assert.That(inventory!.InstalledItems.Single().Mods.Single().ModId).IsEqualTo("ModA");
        await Assert.That(inventory.Issues.Single().Kind).IsEqualTo(ModCompatIssueKind.InstalledButInactive);
        await Assert.That(cache.GetLatest(serverId, AgentId.New())).IsNull();

        await connection.StopAsync();
    }

    [Test]
    public async Task A_removed_mod_shows_as_leftover_after_a_restart_with_no_manual_refresh()
    {
        // #290 acceptance, end to end through the hub: each restart that completes marks the Server booted and queues
        // a background discovery; the stand-in Agent answers it from a mutable "disk + servertest.ini"; the recorder
        // persists it. Booted with A+B, B removed from config, restart: B's files show as leftover.
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, agentId);
        await using HubConnection connection = BuildConnection(factory, credential);

        string[] configuredItems = ["100", "200"];
        string[] enabledMods = ["A", "B"];
        DiscoveredWorkshopItem[] onDisk =
        [
            new DiscoveredWorkshopItem("100", [new DiscoveredMod("A", "Mod A")]),
            new DiscoveredWorkshopItem("200", [new DiscoveredMod("B", "Mod B")]),
        ];
        connection.On<string>(AgentHubProtocol.ReceiveCommand, async json =>
        {
            Envelope<IProtocolMessage> command = ProtocolJson.Deserialize(json);
            if (command.OperationId is not { } operationId)
            {
                return;
            }

            OperationCompleted? result = command.Payload switch
            {
                RestartServer => new OperationCompleted(OperationOutcome.Succeeded),
                DiscoverMods => new OperationCompleted(
                    OperationOutcome.Succeeded,
                    Mods: new ModDiscoveryResult(onDisk, configuredItems, enabledMods, Findings: [])),
                _ => null,
            };
            if (result is not null)
            {
                await connection.SendAsync(
                    AgentHubProtocol.OperationCompleted,
                    Envelope.Create(result, Now, serverId: command.ServerId, operationId: operationId));
            }
        });

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(AgentHubProtocol.Hello, Hello(agentId));

        await RestartAsync(factory, agentId, serverId, "e2e-mods-boot-1");
        ServerModOverview first = await WaitForOverviewAsync(factory, serverId, o => o.HasBootSnapshot && o.Items.Count == 2);
        await Assert.That(Statuses(first)).IsEqualTo("100=Active|200=Active");

        configuredItems = ["100"];
        enabledMods = ["A"];
        await RestartAsync(factory, agentId, serverId, "e2e-mods-boot-2");
        ServerModOverview second = await WaitForOverviewAsync(
            factory, serverId, o => o.Items.Any(i => i is { WorkshopId: "200", Status: ModChangeStatus.Leftover }));

        await Assert.That(Statuses(second)).IsEqualTo("100=Active|200=Leftover");
        await Assert.That(string.Join("|", second.Mods.Select(m => $"{m.ModId}={m.Status}"))).IsEqualTo("A=Active");
        await Assert.That(second.PendingChanges).IsEqualTo(0);

        await connection.StopAsync();

        static string Statuses(ServerModOverview overview) =>
            string.Join("|", overview.Items.Select(i => $"{i.WorkshopId}={i.Status}"));
    }

    [Test]
    public async Task One_click_install_loads_in_one_restart_and_a_wrong_guess_asks_to_pick_parts()
    {
        // #291 acceptance, end to end through the hub: Install writes WorkshopItems= and Mods= in one apply from the
        // description's guess; one restart downloads and loads it. Item 300's guess is right (Active, nothing to do);
        // item 400's description names "Guess" but the download provides "Real", so it asks to pick parts.
        StubWorkshopMetadata steam = new(new Dictionary<string, string>
        {
            ["300"] = "[h1]Kill counter[/h1]\nWorkshop ID: 300\nMod ID: KillCount",
            ["400"] = "Mod ID: Guess",
        });
        await using ZWardenWebAppFactory factory = new()
        {
            ConfigureTestServicesHook = services => services.AddSingleton<IWorkshopMetadataClient>(steam),
        };
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, agentId);
        UserId operatorUser = await SeedOwnerAsync(factory);
        await using HubConnection connection = BuildConnection(factory, credential);

        // The stand-in Agent's "servertest.ini" and Workshop folder. A ConfigApply rewrites the ini lists; a restart
        // downloads whatever WorkshopItems= names (PZ downloads before it loads Mods=, spike #291).
        List<string> configuredItems = ["100"];
        List<string> enabledMods = ["A"];
        Dictionary<string, string> provides = new() { ["100"] = "A", ["300"] = "KillCount", ["400"] = "Real" };
        HashSet<string> downloaded = ["100"];
        connection.On<string>(AgentHubProtocol.ReceiveCommand, async json =>
        {
            Envelope<IProtocolMessage> command = ProtocolJson.Deserialize(json);
            if (command.OperationId is not { } operationId)
            {
                return;
            }

            if (command.Payload is ConfigApply apply)
            {
                foreach (ConfigValueEdit edit in apply.Edits)
                {
                    List<string> target = edit.Path == "WorkshopItems" ? configuredItems : enabledMods;
                    target.Clear();
                    target.AddRange(edit.Value.Split(';', StringSplitOptions.RemoveEmptyEntries));
                }
            }

            if (command.Payload is RestartServer)
            {
                downloaded.UnionWith(configuredItems);
            }

            OperationCompleted? result = command.Payload switch
            {
                RestartServer or ConfigApply => new OperationCompleted(OperationOutcome.Succeeded),
                DiscoverMods => new OperationCompleted(
                    OperationOutcome.Succeeded,
                    Mods: new ModDiscoveryResult(
                        [.. downloaded.Order().Select(id => new DiscoveredWorkshopItem(id, [new DiscoveredMod(provides[id], null)]))],
                        [.. configuredItems], [.. enabledMods], Findings: [])),
                _ => null,
            };
            if (result is not null)
            {
                await connection.SendAsync(
                    AgentHubProtocol.OperationCompleted,
                    Envelope.Create(result, Now, serverId: command.ServerId, operationId: operationId));
            }
        });

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(AgentHubProtocol.Hello, Hello(agentId));
        await RestartAsync(factory, agentId, serverId, "e2e-install-boot-1");
        await WaitForOverviewAsync(factory, serverId, o => o.HasBootSnapshot && o.Items.Count == 1);

        await InstallAsync(factory, operatorUser, serverId, "300");
        await InstallAsync(factory, operatorUser, serverId, "400");
        ServerModOverview pending = await WaitForOverviewAsync(
            factory, serverId, o => o.Items.Count(i => i.Status == ModChangeStatus.InstallsOnRestart) == 2);
        await Assert.That(string.Join(";", enabledMods)).IsEqualTo("A;KillCount;Guess");
        await Assert.That(pending.PendingChanges).IsEqualTo(4);

        await RestartAsync(factory, agentId, serverId, "e2e-install-boot-2");
        ServerModOverview booted = await WaitForOverviewAsync(
            factory, serverId, o => o.PendingChanges == 0 && o.Items.Any(i => i is { WorkshopId: "400", NeedsParts: true }));

        ModItemView right = booted.Items.Single(i => i.WorkshopId == "300");
        ModItemView wrong = booted.Items.Single(i => i.WorkshopId == "400");
        await Assert.That(right.Status).IsEqualTo(ModChangeStatus.Active);
        await Assert.That(right.NeedsParts).IsFalse();
        await Assert.That(wrong.NeedsParts).IsTrue();
        await Assert.That(string.Join(";", wrong.MissingModIds)).IsEqualTo("Guess");
        await Assert.That(string.Join(";", wrong.ObservedModIds)).IsEqualTo("Real");

        await connection.StopAsync();
    }

    // Plans from the (stubbed) Steam description and installs with the plan's ids, as the Install button will.
    private static async Task InstallAsync(ZWardenWebAppFactory factory, UserId user, ServerId serverId, string workshopId)
    {
        ModManagementResult result;
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            IWorkshopMetadataClient steam = scope.ServiceProvider.GetRequiredService<IWorkshopMetadataClient>();
            ModInstallPlan plan = ModInstallPlan.For((await steam.GetItemsAsync([workshopId]))[0]);
            result = await scope.ServiceProvider.GetRequiredService<IServerModManager>().InstallWorkshopItemsAsync(
                user, serverId, [workshopId], [.. plan.CandidateModIds.Select(id => id.Value)]);
        }

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(await WaitForStateAsync(factory, result.Operation!.Value, OperationState.Succeeded)).IsNotNull();
    }

    private static async Task<UserId> SeedOwnerAsync(ZWardenWebAppFactory factory)
    {
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services);
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Role owner = await db.Set<Role>().FirstAsync(r => r.BuiltIn == BuiltInRoleKind.TenantOwner);
        UserId user = UserId.New();
        db.Add(RoleAssignment.TenantWide(Tenant.DefaultId, user, owner.Id));
        await db.SaveChangesAsync();
        return user;
    }

    private sealed class StubWorkshopMetadata(IReadOnlyDictionary<string, string> descriptions) : IWorkshopMetadataClient
    {
        public Task<IReadOnlyList<WorkshopItemMetadata>> GetItemsAsync(
            IReadOnlyList<string> workshopIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkshopItemMetadata>>(
            [
                .. workshopIds.Select(id => descriptions.TryGetValue(id, out string? text)
                    ? new WorkshopItemMetadata(id, Found: true, Title: $"Item {id}", Description: text)
                    : WorkshopItemMetadata.NotFound(id)),
            ]);

        public Task<IReadOnlyList<WorkshopItemMetadata>> RefreshItemsAsync(
            IReadOnlyList<string> workshopIds, CancellationToken cancellationToken = default) =>
            GetItemsAsync(workshopIds, cancellationToken);

        public Task<IReadOnlyList<string>> GetCollectionItemIdsAsync(
            string collectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private static async Task RestartAsync(ZWardenWebAppFactory factory, AgentId agentId, ServerId serverId, string key)
    {
        OperationId operationId;
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            Operation op = await scope.ServiceProvider.GetRequiredService<IOperationCoordinator>().EnqueueAsync(
                new EnqueueOperationRequest(agentId, OperationKind.RestartServer, IsMutating: true, key, ServerId: serverId));
            operationId = op.Id;
        }

        await Assert.That(await WaitForStateAsync(factory, operationId, OperationState.Succeeded)).IsNotNull();
    }

    // Polls the persisted mod state (as the #292 page will read it) until the condition holds, or ~10 s pass.
    private static async Task<ServerModOverview> WaitForOverviewAsync(
        ZWardenWebAppFactory factory, ServerId serverId, Func<ServerModOverview, bool> condition)
    {
        ServerModOverview overview = ModChangeSet.Derive(serverId, null, []);
        for (int i = 0; i < 200; i++)
        {
            using AsyncServiceScope scope = factory.Services.CreateSystemScope();
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            overview = ModChangeSet.Derive(
                serverId,
                await new ServerModStateRepository(db).FindAsync(serverId),
                await new ServerWorkshopItemRepository(db).ListForServerAsync(serverId));
            if (condition(overview))
            {
                return overview;
            }

            await Task.Delay(50);
        }

        return overview;
    }

    [Test]
    public async Task An_operation_for_a_disconnected_agent_stays_pending()
    {
        await using ZWardenWebAppFactory factory = new();
        _ = factory.Services; // force host start

        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        IOperationCoordinator coordinator = scope.ServiceProvider.GetRequiredService<IOperationCoordinator>();
        Operation op = await coordinator.EnqueueAsync(
            new EnqueueOperationRequest(AgentId.New(), OperationKind.DiagnosticsPing, IsMutating: false, "offline"));

        await Assert.That(op.State).IsEqualTo(OperationState.Pending);
        await Assert.That(op.StartedAt).IsNull();
    }

    private static Envelope<AgentHello> Hello(AgentId agentId) => new()
    {
        ProtocolVersion = ProtocolVersion.Current,
        MessageId = MessageId.New(),
        Timestamp = Now,
        AgentId = agentId,
        Payload = new AgentHello(agentId),
    };

    private static HubConnection BuildConnection(ZWardenWebAppFactory factory, string credential)
        => new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, AgentHubProtocol.Path), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(credential);
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions = ProtocolJson.Options)
            .Build();

    private static async Task<(AgentId AgentId, string Credential)> SeedTrustedAgentAsync(ZWardenWebAppFactory factory)
    {
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        ICredentialHasher hasher = scope.ServiceProvider.GetRequiredService<ICredentialHasher>();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();

        Domain.Security.SecretString credential = hasher.Generate("zwa");
        Agent agent = Agent.Enroll(hasher.Hash(credential), EnrollmentId.New(), DateTimeOffset.UtcNow);
        context.Add(agent);
        await context.SaveChangesAsync();
        return (agent.Id, credential.Reveal());
    }

    private static async Task<ServerId> SeedServerAsync(ZWardenWebAppFactory factory, AgentId agentId)
    {
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        // The ownership interceptor stamps the ambient (default) tenant on insert (ADR 0016), the same tenant the
        // hub records the revision under — so the completion resolves to this Server.
        Domain.Servers.Server server = Domain.Servers.Server.Import(agentId, ServerId.New(), "alpha", Now);
        context.Add(server);
        await context.SaveChangesAsync();
        return server.Id;
    }

    private static async Task<Operation?> WaitForStateAsync(
        ZWardenWebAppFactory factory,
        OperationId operationId,
        OperationState state)
    {
        for (int i = 0; i < 100; i++)
        {
            using AsyncServiceScope scope = factory.Services.CreateSystemScope();
            OperationRepository repo = new(scope.ServiceProvider.GetRequiredService<ZWardenDbContext>());
            Operation? op = await repo.FindByIdAsync(operationId);
            if (op is not null && op.State == state)
            {
                return op;
            }

            await Task.Delay(50);
        }

        return null;
    }
}
