using System.Net;
using Docker.DotNet;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Servers;
using ZWarden.Agent.Tests.Docker;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Servers;

/// <summary>
/// #262: the Agent reads each server's game version from its boot log once per container start. The read is
/// bounded to the first minutes after the start, so a long-running container never streams its whole history. It
/// retries while the server is still booting, gives up once the window has passed, and starts again on a restart.
/// </summary>
public class ServerGameVersionReaderTests
{
    private const string BootLine = " LOG  : General      f:0 st:1,802,558,676> version=42.20.4 b0bbce05d5 demo=false";
    private static readonly DateTimeOffset Started = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly ServerId Server = ServerId.New();
    private static readonly ManagedContainer Container = new("c1", Server, "running");

    private readonly FakeDockerEngine _engine = new();
    private readonly SettableClock _clock = new(Started.AddSeconds(30));

    private ServerGameVersionReader Build() => new(_engine, _clock);

    [Test]
    public async Task A_running_server_reads_its_version_from_the_start_window_once()
    {
        _engine.LogText = BootLine;
        ServerGameVersionReader reader = Build();

        string? version = await reader.ReadAsync(Container, Started, CancellationToken.None);
        string? again = await reader.ReadAsync(Container, Started, CancellationToken.None);

        await Assert.That(version).IsEqualTo("42.20.4");
        await Assert.That(again).IsEqualTo("42.20.4");
        await Assert.That(_engine.ReadLogsCount).IsEqualTo(1);
        await Assert.That(_engine.LastLogsSince!.Value).IsLessThanOrEqualTo(Started);
        await Assert.That(_engine.LastLogsUntil).IsEqualTo(Started + ServerGameVersionReader.BootWindow);
    }

    [Test]
    public async Task While_still_booting_it_retries_and_then_finds_it()
    {
        ServerGameVersionReader reader = Build();

        await Assert.That(await reader.ReadAsync(Container, Started, CancellationToken.None)).IsNull();

        _engine.LogText = BootLine;
        await Assert.That(await reader.ReadAsync(Container, Started, CancellationToken.None)).IsEqualTo("42.20.4");
        await Assert.That(_engine.ReadLogsCount).IsEqualTo(2);
    }

    [Test]
    public async Task Past_the_window_without_the_line_it_gives_up_until_the_next_start()
    {
        ServerGameVersionReader reader = Build();
        _clock.Now = Started + ServerGameVersionReader.BootWindow + TimeSpan.FromSeconds(1);

        await reader.ReadAsync(Container, Started, CancellationToken.None);
        await reader.ReadAsync(Container, Started, CancellationToken.None);
        await Assert.That(_engine.ReadLogsCount).IsEqualTo(1);

        // A restart opens a new window.
        _engine.LogText = BootLine;
        DateTimeOffset restarted = _clock.Now;
        await Assert.That(await reader.ReadAsync(Container, restarted, CancellationToken.None)).IsEqualTo("42.20.4");
    }

    [Test]
    public async Task A_restart_re_reads_so_an_update_is_picked_up()
    {
        _engine.LogText = BootLine;
        ServerGameVersionReader reader = Build();
        await reader.ReadAsync(Container, Started, CancellationToken.None);

        _engine.LogText = " LOG  : General      f:0 st:9> version=42.21.0 c0ffee1234 demo=false";
        DateTimeOffset restarted = Started.AddHours(1);
        _clock.Now = restarted.AddSeconds(20);

        await Assert.That(await reader.ReadAsync(Container, restarted, CancellationToken.None)).IsEqualTo("42.21.0");
    }

    [Test]
    public async Task A_stopped_server_keeps_its_last_known_version_without_reading()
    {
        _engine.LogText = BootLine;
        ServerGameVersionReader reader = Build();
        await reader.ReadAsync(Container, Started, CancellationToken.None);

        string? stopped = await reader.ReadAsync(Container with { State = "exited" }, startedAt: null, CancellationToken.None);

        await Assert.That(stopped).IsEqualTo("42.20.4");
        await Assert.That(_engine.ReadLogsCount).IsEqualTo(1);
    }

    [Test]
    public async Task A_docker_error_is_null_and_retried()
    {
        _engine.LogsException = new DockerApiException(HttpStatusCode.NotFound, "gone");
        ServerGameVersionReader reader = Build();

        await Assert.That(await reader.ReadAsync(Container, Started, CancellationToken.None)).IsNull();

        _engine.LogsException = null;
        _engine.LogText = BootLine;
        await Assert.That(await reader.ReadAsync(Container, Started, CancellationToken.None)).IsEqualTo("42.20.4");
    }

    [Test]
    public async Task A_server_no_longer_managed_is_forgotten()
    {
        _engine.LogText = BootLine;
        ServerGameVersionReader reader = Build();
        await reader.ReadAsync(Container, Started, CancellationToken.None);

        reader.Retain([]);

        await Assert.That(await reader.ReadAsync(Container with { State = "exited" }, null, CancellationToken.None)).IsNull();
    }

    private sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
