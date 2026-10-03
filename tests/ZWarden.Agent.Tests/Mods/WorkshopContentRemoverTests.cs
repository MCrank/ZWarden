using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Mods;
using ZWarden.Agent.SteamCmd;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.PzConfig;

namespace ZWarden.Agent.Tests.Mods;

/// <summary>
/// #293: deleting unused Workshop downloads, end to end against a real temp filesystem and the real F20a parser. The
/// remover works only on a Server this Agent owns, accepts only bare numeric ids, refuses an id still in
/// <c>WorkshopItems=</c> or a folder that is a link, and treats an absent folder as already deleted.
/// </summary>
public class WorkshopContentRemoverTests
{
    [Test]
    public async Task Deletes_an_unreferenced_item_and_leaves_the_rest()
    {
        using var temp = new TempServer();
        temp.WriteItem("2392709985");
        temp.WriteItem("2553809727");
        temp.WriteIni("WorkshopItems=2553809727\nMods=KillCount\n");

        WorkshopContentRemoval removal = await temp.Remover().DeleteAsync(temp.ServerId, ["2392709985"], CancellationToken.None);

        await Assert.That(removal.Succeeded).IsTrue();
        await Assert.That(removal.Result!.Items.Single()).IsEqualTo(
            new WorkshopContentDeletion("2392709985", WorkshopContentDeletionOutcome.Deleted));
        await Assert.That(Directory.Exists(temp.ItemDir("2392709985"))).IsFalse();
        await Assert.That(Directory.Exists(temp.ItemDir("2553809727"))).IsTrue();
    }

    [Test]
    public async Task Refuses_an_item_still_listed_in_WorkshopItems()
    {
        using var temp = new TempServer();
        temp.WriteItem("2553809727");
        temp.WriteIni("WorkshopItems=111;2553809727\n");

        WorkshopContentRemoval removal = await temp.Remover().DeleteAsync(temp.ServerId, ["2553809727"], CancellationToken.None);

        await Assert.That(removal.Succeeded).IsTrue();
        await Assert.That(removal.Result!.Items.Single().Outcome).IsEqualTo(WorkshopContentDeletionOutcome.RefusedReferenced);
        await Assert.That(Directory.Exists(temp.ItemDir("2553809727"))).IsTrue();
    }

    [Test]
    public async Task Reports_each_id_in_request_order()
    {
        using var temp = new TempServer();
        temp.WriteItem("1");
        temp.WriteItem("2");
        temp.WriteIni("WorkshopItems=2\n");

        WorkshopContentRemoval removal = await temp.Remover().DeleteAsync(temp.ServerId, ["2", "3", "1"], CancellationToken.None);

        await Assert.That(removal.Result!.Items).IsEquivalentTo(
        [
            new WorkshopContentDeletion("2", WorkshopContentDeletionOutcome.RefusedReferenced),
            new WorkshopContentDeletion("3", WorkshopContentDeletionOutcome.AlreadyAbsent),
            new WorkshopContentDeletion("1", WorkshopContentDeletionOutcome.Deleted),
        ]);
    }

    [Test]
    public async Task An_absent_folder_counts_as_already_deleted()
    {
        using var temp = new TempServer();
        temp.WriteIni("WorkshopItems=\n");

        WorkshopContentRemoval removal = await temp.Remover().DeleteAsync(temp.ServerId, ["2392709985"], CancellationToken.None);

        await Assert.That(removal.Succeeded).IsTrue();
        await Assert.That(removal.Result!.Items.Single().Outcome).IsEqualTo(WorkshopContentDeletionOutcome.AlreadyAbsent);
    }

    [Test]
    [Arguments("..")]
    [Arguments("../../etc")]
    [Arguments("123/../456")]
    [Arguments("/etc")]
    [Arguments("C:\\Windows")]
    [Arguments("12a")]
    [Arguments("")]
    [Arguments(" 123")]
    [Arguments("123456789012345678901")]
    public async Task A_malformed_id_fails_the_whole_command_before_anything_is_deleted(string bad)
    {
        using var temp = new TempServer();
        temp.WriteItem("2392709985");
        temp.WriteIni("WorkshopItems=\n");

        WorkshopContentRemoval removal = await temp.Remover().DeleteAsync(temp.ServerId, ["2392709985", bad], CancellationToken.None);

        await Assert.That(removal.Succeeded).IsFalse();
        await Assert.That(removal.FailureReason).IsNotNull();
        await Assert.That(Directory.Exists(temp.ItemDir("2392709985"))).IsTrue();
    }

    [Test]
    public async Task An_empty_request_fails()
    {
        using var temp = new TempServer();
        temp.WriteIni("WorkshopItems=\n");

        WorkshopContentRemoval removal = await temp.Remover().DeleteAsync(temp.ServerId, [], CancellationToken.None);

        await Assert.That(removal.Succeeded).IsFalse();
    }

    [Test]
    public async Task Refuses_a_server_this_Agent_does_not_own()
    {
        using var temp = new TempServer();
        temp.WriteItem("2392709985");
        temp.WriteIni("WorkshopItems=\n");

        // The runtime lists only this Agent's containers; a different Server's is there, the target's isn't.
        WorkshopContentRemoval removal = await temp.Remover(owned: false)
            .DeleteAsync(temp.ServerId, ["2392709985"], CancellationToken.None);

        await Assert.That(removal.Succeeded).IsFalse();
        await Assert.That(Directory.Exists(temp.ItemDir("2392709985"))).IsTrue();
    }

    [Test]
    public async Task Fails_closed_when_the_config_is_missing()
    {
        using var temp = new TempServer();
        temp.WriteItem("2392709985");

        WorkshopContentRemoval removal = await temp.Remover().DeleteAsync(temp.ServerId, ["2392709985"], CancellationToken.None);

        await Assert.That(removal.Succeeded).IsFalse();
        await Assert.That(Directory.Exists(temp.ItemDir("2392709985"))).IsTrue();
    }

    [Test]
    public async Task Fails_closed_when_the_config_does_not_parse()
    {
        using var temp = new TempServer();
        temp.WriteItem("2392709985");
        // Over the parser's 5 MiB limit, so it never parses; the WorkshopItems= list is unknown.
        temp.WriteIni("WorkshopItems=\n" + new string('#', (5 * 1024 * 1024) + 1));

        WorkshopContentRemoval removal = await temp.Remover().DeleteAsync(temp.ServerId, ["2392709985"], CancellationToken.None);

        await Assert.That(removal.Succeeded).IsFalse();
        await Assert.That(Directory.Exists(temp.ItemDir("2392709985"))).IsTrue();
    }

    [Test]
    public async Task Refuses_an_item_folder_that_is_a_link_and_leaves_its_target_alone()
    {
        using var temp = new TempServer();
        temp.WriteIni("WorkshopItems=\n");
        string outside = Path.Combine(temp.Root, "precious");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "not a mod");
        Directory.CreateDirectory(temp.ContentRoot);
        try
        {
            Directory.CreateSymbolicLink(temp.ItemDir("2392709985"), outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Creating a symlink needs privilege on Windows; the Linux CI tier exercises this path.
            return;
        }

        WorkshopContentRemoval removal = await temp.Remover().DeleteAsync(temp.ServerId, ["2392709985"], CancellationToken.None);

        await Assert.That(removal.Result!.Items.Single().Outcome).IsEqualTo(WorkshopContentDeletionOutcome.RefusedUnsafe);
        await Assert.That(File.Exists(Path.Combine(outside, "keep.txt"))).IsTrue();
    }

    [Test]
    public async Task Refuses_when_the_content_root_itself_is_a_link()
    {
        // The server container can write its install volume, so content/108600 could be swapped for a link.
        using var temp = new TempServer();
        temp.WriteIni("WorkshopItems=\n");
        string outside = Path.Combine(temp.Root, "elsewhere");
        Directory.CreateDirectory(Path.Combine(outside, "2392709985"));
        File.WriteAllText(Path.Combine(outside, "2392709985", "keep.txt"), "not a mod");
        Directory.CreateDirectory(Path.GetDirectoryName(temp.ContentRoot)!);
        try
        {
            Directory.CreateSymbolicLink(temp.ContentRoot, outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Creating a symlink needs privilege on Windows; the Linux CI tier exercises this path.
            return;
        }

        WorkshopContentRemoval removal = await temp.Remover().DeleteAsync(temp.ServerId, ["2392709985"], CancellationToken.None);

        await Assert.That(removal.Result!.Items.Single().Outcome).IsEqualTo(WorkshopContentDeletionOutcome.RefusedUnsafe);
        await Assert.That(File.Exists(Path.Combine(outside, "2392709985", "keep.txt"))).IsTrue();
    }

    private sealed class TempServer : IDisposable
    {
        public TempServer()
        {
            Root = Path.Combine(Path.GetTempPath(), $"zw-293-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public ServerId ServerId { get; } = ServerId.New();

        public string ContentRoot =>
            Path.Combine(Root, $"{ServerId}.server", "steamapps", "workshop", "content", "108600");

        public string ItemDir(string workshopId) => Path.Combine(ContentRoot, workshopId);

        public void WriteItem(string workshopId)
        {
            string modDir = Path.Combine(ItemDir(workshopId), "mods", "M" + workshopId);
            Directory.CreateDirectory(modDir);
            File.WriteAllText(Path.Combine(modDir, "mod.info"), $"id=M{workshopId}\n");
        }

        public void WriteIni(string content) => WriteIniBytes(Encoding.UTF8.GetBytes(content));

        public void WriteIniBytes(byte[] bytes)
        {
            string dir = Path.Combine(Root, ServerId.ToString(), "Server");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "servertest.ini"), bytes);
        }

        public WorkshopContentRemover Remover(bool owned = true)
        {
            AgentOptions options = new() { DataMountRoot = Root };
            ManagedContainer listed = new("c-1", owned ? ServerId : ServerId.New(), "running");
            return new WorkshopContentRemover(
                new ServerInstallPaths(Options.Create(options)),
                new ServerModConfigReader(new PzConfigParser(), Options.Create(options)),
                new ListingRuntime(listed),
                NullLogger<WorkshopContentRemover>.Instance);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    // Only the owned-container listing is used; every other verb is loud.
    private sealed class ListingRuntime(params ManagedContainer[] managed) : IContainerRuntime
    {
        public Task<IReadOnlyList<ManagedContainer>> ListManagedAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManagedContainer>>(managed);

        public Task<DockerHealth> ProbeHealthAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<HostMemory> ReadHostMemoryAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ObservedContainer>> InspectManagedAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PortAllocation> ClaimRequestedPortsAsync(int gamePort, ServerId forServer, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ServerContainer?> InspectServerAsync(ServerId serverId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PortAllocation> AllocateNextPortsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> CreateAsync(PzContainerSpec spec, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StartAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StopAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RestartAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StartAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StopAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RestartAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> ReadServerLogsAsync(ServerId serverId, DateTimeOffset? since, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> ResolveNetworkAddressAsync(ServerId serverId, string networkName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task FollowServerLogsAsync(ServerId serverId, int tailLines, Func<ContainerLogFrame, CancellationToken, ValueTask> onFrame, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
