using ZWarden.Agent.Docker;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Docker;

/// <summary>
/// <see cref="ServerHostDirectories"/> materialises the two host-side bind-mount sources a canonical container
/// needs (#184): the Docker Mounts API never auto-creates them, so the daemon would refuse the create with
/// "bind source path does not exist" if they were missing.
/// </summary>
public class ServerHostDirectoriesTests
{
    private static PzContainerSpec Spec(string dataSource, string serverSource) => new(
        ServerId.New(), "zwarden-srv", "img@sha256:abc", "zwarden-pz", dataSource, serverSource,
        PortStrideAllocator.ForStride(0), 6L * 1024 * 1024 * 1024);

    [Test]
    public async Task EnsureCreated_creates_both_the_data_and_server_install_bind_sources()
    {
        string root = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}");
        string data = Path.Combine(root, "srv");
        string server = Path.Combine(root, "srv.server");
        try
        {
            new ServerHostDirectories().EnsureCreated(Spec(data, server));

            await Assert.That(Directory.Exists(data)).IsTrue();
            await Assert.That(Directory.Exists(server)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task EnsureCreated_is_idempotent_when_the_sources_already_exist()
    {
        string root = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}");
        string data = Path.Combine(root, "srv");
        string server = Path.Combine(root, "srv.server");
        try
        {
            var sut = new ServerHostDirectories();
            sut.EnsureCreated(Spec(data, server));

            // A second call over existing directories must not throw.
            sut.EnsureCreated(Spec(data, server));

            await Assert.That(Directory.Exists(data)).IsTrue();
            await Assert.That(Directory.Exists(server)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
