using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.SteamCmd;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.SteamCmd;

/// <summary>
/// F17: the host-side files for a Server. The update control-file lands in the data volume
/// (<c>&lt;root&gt;/&lt;serverId&gt;</c>, bound at /pz/data) carrying the OperationId; the installed build id is
/// read from the install volume manifest (the host sibling <c>&lt;root&gt;/&lt;serverId&gt;.server</c>, bound at
/// /pz/server). Exercised against a real temp directory — no Docker.
/// </summary>
public class ServerInstallPathsTests
{
    private static (ServerInstallPaths Paths, string Root) NewPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "zw-f17-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return (new ServerInstallPaths(Options.Create(new AgentOptions { DataMountRoot = root })), root);
    }

    [Test]
    public async Task Writing_the_update_request_creates_the_file_with_the_operation_id()
    {
        (ServerInstallPaths paths, string root) = NewPaths();
        ServerId serverId = ServerId.New();
        OperationId operationId = OperationId.New();

        try
        {
            paths.WriteUpdateRequest(serverId, operationId);

            string file = Path.Combine(root, serverId.ToString(), ".zwarden-update-requested");
            await Assert.That(File.Exists(file)).IsTrue();
            await Assert.That(File.ReadAllText(file)).IsEqualTo(operationId.ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Reading_the_build_id_parses_the_install_manifest()
    {
        (ServerInstallPaths paths, string root) = NewPaths();
        ServerId serverId = ServerId.New();
        string steamapps = Path.Combine(root, $"{serverId}.server", "steamapps");
        Directory.CreateDirectory(steamapps);
        File.WriteAllText(Path.Combine(steamapps, "appmanifest_380870.acf"), "\"AppState\" { \"buildid\" \"24909836\" }");

        try
        {
            await Assert.That(paths.ReadInstalledBuildId(serverId)).IsEqualTo("24909836");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Reading_the_build_id_when_the_manifest_is_absent_yields_null()
    {
        (ServerInstallPaths paths, string root) = NewPaths();
        try
        {
            await Assert.That(paths.ReadInstalledBuildId(ServerId.New())).IsNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
