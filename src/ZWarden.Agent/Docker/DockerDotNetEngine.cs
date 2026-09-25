using System.Globalization;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace ZWarden.Agent.Docker;

/// <summary>
/// The real <see cref="IDockerEngine"/> over a <see cref="IDockerClient"/> from Docker.DotNet.Enhanced. It is
/// deliberately mechanical — it maps each allowlisted verb to one client call and projects responses into the
/// undecorated <see cref="EngineContainer"/> — so all ZWarden policy stays in <see cref="ContainerRuntime"/>.
/// The client is constructed without an API-version override, so it negotiates via <c>/_ping</c> and never
/// pins a <c>/v1.xx</c> prefix (ADR 0008). This type is exercised in the integration tier, not offline.
/// </summary>
public sealed class DockerDotNetEngine : IDockerEngine
{
    private static readonly IReadOnlyDictionary<string, string> NoLabels =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly IDockerClient _client;

    /// <summary>Creates the engine over an already-configured Docker client.</summary>
    public DockerDotNetEngine(IDockerClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <inheritdoc />
    public async Task<string> PingApiVersionAsync(CancellationToken cancellationToken)
    {
        VersionResponse version = await _client.System.GetVersionAsync(cancellationToken).ConfigureAwait(false);
        return version.APIVersion;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EngineContainer>> ListAsync(CancellationToken cancellationToken)
    {
        IList<ContainerListResponse> containers = await _client.Containers
            .ListContainersAsync(new ContainersListParameters { All = true }, cancellationToken)
            .ConfigureAwait(false);

        List<EngineContainer> mapped = [];
        foreach (ContainerListResponse c in containers)
        {
            List<PublishedPort> ports = [];
            if (c.Ports is not null)
            {
                foreach (PortSummary p in c.Ports)
                {
                    ports.Add(new PublishedPort(p.PublicPort.GetValueOrDefault(), p.PrivatePort, p.Type ?? string.Empty));
                }
            }

            mapped.Add(new EngineContainer(c.ID, ToReadOnly(c.Labels), c.State ?? string.Empty, ports));
        }

        return mapped;
    }

    /// <inheritdoc />
    public async Task<EngineContainer> InspectAsync(string containerId, CancellationToken cancellationToken)
    {
        ContainerInspectResponse r = await _client.Containers
            .InspectContainerAsync(containerId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<string, string> labels = ToReadOnly(r.Config?.Labels);
        string state = r.State?.Status ?? string.Empty;
        // State.Health is null when the image declares no HEALTHCHECK; the PZServer image (F12) declares one, so
        // a managed container reports healthy/unhealthy/starting here — F16's process and startup probes.
        string? health = r.State?.Health?.Status;
        long exitCode = r.State?.ExitCode ?? 0;
        bool oomKilled = r.State?.OOMKilled ?? false;
        return new EngineContainer(
            r.ID, labels, state, MapPorts(r.NetworkSettings?.Ports), health, exitCode, oomKilled,
            MapNetworkAddresses(r.NetworkSettings?.Networks), ReadStartedAt(r));
    }

    /// <inheritdoc />
    public async Task<ContainerStatsSnapshot> StatsAsync(string containerId, CancellationToken cancellationToken)
    {
        // Stream=false yields exactly one stats frame. A custom IProgress captures it synchronously (unlike
        // System.Progress<T>, which would marshal the callback and could race the await's completion).
        SingleStats sink = new();
        await _client.Containers
            .GetContainerStatsAsync(containerId, new ContainerStatsParameters { Stream = false }, sink, cancellationToken)
            .ConfigureAwait(false);

        ContainerStatsResponse r = sink.Value
            ?? throw new InvalidOperationException("The Docker daemon returned no stats frame.");

        uint onlineCpus = r.CPUStats?.OnlineCPUs ?? 0;
        if (onlineCpus == 0 && r.CPUStats?.CPUUsage?.PercpuUsage is { Count: > 0 } perCpu)
        {
            onlineCpus = (uint)perCpu.Count;
        }

        return new ContainerStatsSnapshot(
            CpuTotalUsage: r.CPUStats?.CPUUsage?.TotalUsage ?? 0,
            PreCpuTotalUsage: r.PreCPUStats?.CPUUsage?.TotalUsage ?? 0,
            SystemCpuUsage: r.CPUStats?.SystemUsage ?? 0,
            PreSystemCpuUsage: r.PreCPUStats?.SystemUsage ?? 0,
            OnlineCpus: onlineCpus,
            MemoryUsage: r.MemoryStats?.Usage ?? 0,
            MemoryCache: ReclaimableCache(r.MemoryStats?.Stats),
            MemoryLimit: r.MemoryStats?.Limit ?? 0);
    }

    // State.StartedAt is Docker's RFC 3339 start time. An exited container keeps its last start, which is not an
    // uptime, so only a running container yields one (#257).
    private static DateTimeOffset? ReadStartedAt(ContainerInspectResponse r) =>
        r.State is { Running: true } state ? ContainerStartTime.Parse(state.StartedAt) : null;

    // The docker CLI subtracts reclaimable page cache from memory usage: cgroup v1 exposes it as "cache",
    // cgroup v2 as "inactive_file". Prefer whichever the daemon reported; 0 if neither.
    private static ulong ReclaimableCache(IDictionary<string, ulong>? stats)
    {
        if (stats is null)
        {
            return 0;
        }

        if (stats.TryGetValue("cache", out ulong cache))
        {
            return cache;
        }

        return stats.TryGetValue("inactive_file", out ulong inactiveFile) ? inactiveFile : 0;
    }

    private static IReadOnlyDictionary<string, string> ToReadOnly(IDictionary<string, string>? labels) =>
        labels is null ? NoLabels : new Dictionary<string, string>(labels, StringComparer.Ordinal);

    // Projects inspect's NetworkSettings.Networks into an undecorated network-name -> IPv4 map. F18 reads the
    // ZWarden-network address to reach the container's private RCON port. Entries with no address (a network the
    // container is attached to but not yet assigned an IP on) are skipped.
    private static IReadOnlyDictionary<string, string> MapNetworkAddresses(
        IDictionary<string, EndpointSettings>? networks)
    {
        if (networks is null)
        {
            return NoLabels; // reuse the shared empty ordinal dictionary
        }

        Dictionary<string, string> mapped = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, EndpointSettings> entry in networks)
        {
            if (!string.IsNullOrEmpty(entry.Value?.IPAddress))
            {
                mapped[entry.Key] = entry.Value.IPAddress;
            }
        }

        return mapped;
    }

    // Captures the single stats frame Stream=false delivers, synchronously as the client reports it.
    private sealed class SingleStats : IProgress<ContainerStatsResponse>
    {
        public ContainerStatsResponse? Value { get; private set; }

        public void Report(ContainerStatsResponse value) => Value = value;
    }

    // Projects inspect's port map ("16261/udp" -> host bindings) into undecorated PublishedPorts. F16's network
    // probe needs the host-side port of a container's published game/query ports.
    private static List<PublishedPort> MapPorts(IDictionary<string, IList<PortBinding>>? ports)
    {
        if (ports is null)
        {
            return [];
        }

        List<PublishedPort> mapped = [];
        foreach (KeyValuePair<string, IList<PortBinding>> entry in ports)
        {
            string[] parts = entry.Key.Split('/');
            if (parts.Length != 2 || !ushort.TryParse(parts[0], out ushort containerPort) || entry.Value is null)
            {
                continue;
            }

            foreach (PortBinding binding in entry.Value)
            {
                if (ushort.TryParse(binding?.HostPort, out ushort hostPort))
                {
                    mapped.Add(new PublishedPort(hostPort, containerPort, parts[1]));
                }
            }
        }

        return mapped;
    }

    /// <inheritdoc />
    public async Task<string> ReadLogsAsync(
        string containerId, DateTimeOffset? since, DateTimeOffset? until, CancellationToken cancellationToken)
    {
        ContainerLogsParameters parameters = new()
        {
            ShowStdout = true,
            ShowStderr = true,
            Follow = false,
            Timestamps = false,
        };
        if (since is { } s)
        {
            parameters.Since = s.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        if (until is { } u)
        {
            parameters.Until = u.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        // The fork demultiplexes stdout/stderr and reports each log line (newline stripped) in order; with
        // Follow=false the task completes once all current lines are read. A synchronous IProgress sink captures
        // them without marshalling (unlike System.Progress<T>), so the entrypoint's stdout banners and SteamCMD's
        // stderr progress lines land in the returned text in arrival order for the parser.
        LogSink sink = new();
        await _client.Containers
            .GetContainerLogsAsync(containerId, parameters, sink, cancellationToken)
            .ConfigureAwait(false);
        return sink.Text;
    }

    // Accumulates the log lines the client reports, one per line, in arrival order.
    private sealed class LogSink : IProgress<string>
    {
        private readonly StringBuilder _builder = new();

        public string Text => _builder.ToString();

        public void Report(string value) => _builder.Append(value).Append('\n');
    }

    // Reassembles one standard stream's newline-delimited lines out of the raw byte chunks a MultiplexedStream
    // yields (a chunk neither starts nor ends on a line or even a UTF-8 boundary). A stateful UTF-8 decoder carries
    // a multi-byte character split across chunks; a pending StringBuilder carries a line split across chunks. Each
    // completed line is stripped of a trailing '\r', has its daemon timestamp prefix split off, and is emitted.
    private sealed class LineAssembler(bool isStderr)
    {
        private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
        private readonly StringBuilder _line = new();
        private char[] _chars = new char[4096];

        public async ValueTask AppendAsync(
            byte[] buffer,
            int count,
            Func<ContainerLogFrame, CancellationToken, ValueTask> onFrame,
            CancellationToken cancellationToken)
        {
            int needed = _decoder.GetCharCount(buffer, 0, count, flush: false);
            if (needed == 0)
            {
                return;
            }

            if (_chars.Length < needed)
            {
                _chars = new char[needed];
            }

            int produced = _decoder.GetChars(buffer, 0, count, _chars, 0, flush: false);
            for (int i = 0; i < produced; i++)
            {
                char c = _chars[i];
                if (c == '\n')
                {
                    await EmitAsync(onFrame, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _line.Append(c);
                }
            }
        }

        public ValueTask FlushAsync(Func<ContainerLogFrame, CancellationToken, ValueTask> onFrame, CancellationToken cancellationToken) =>
            _line.Length > 0 ? EmitAsync(onFrame, cancellationToken) : ValueTask.CompletedTask;

        private async ValueTask EmitAsync(Func<ContainerLogFrame, CancellationToken, ValueTask> onFrame, CancellationToken cancellationToken)
        {
            string raw = _line.ToString();
            _line.Clear();
            if (raw.EndsWith('\r'))
            {
                raw = raw[..^1];
            }

            int space = raw.IndexOf(' ', StringComparison.Ordinal);
            if (space > 0
                && DateTimeOffset.TryParse(
                    raw.AsSpan(0, space), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset timestamp))
            {
                await onFrame(new ContainerLogFrame(timestamp, isStderr, raw[(space + 1)..]), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await onFrame(new ContainerLogFrame(DateTimeOffset.UtcNow, isStderr, raw), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task FollowLogsAsync(
        string containerId,
        int tailLines,
        Func<ContainerLogFrame, CancellationToken, ValueTask> onFrame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        ContainerLogsParameters parameters = new()
        {
            ShowStdout = true,
            ShowStderr = true,
            Follow = true,
            // Daemon-side timestamps let the frame carry when the line was written, not merely when it was read.
            Timestamps = true,
            Tail = tailLines >= 0 ? tailLines.ToString(CultureInfo.InvariantCulture) : "all",
        };

        // The two-arg (tty:false) overload returns a MultiplexedStream that preserves the stdout/stderr framing —
        // the IProgress<string> overload used by the non-following ReadLogsAsync flattens both into one text, which
        // F27 must not do. The 8-byte frame headers do not align to log lines, so a per-stream assembler splits the
        // decoded bytes on '\n' and emits one ContainerLogFrame per complete line.
        using MultiplexedStream stream = await _client.Containers
            .GetContainerLogsAsync(containerId, parameters, cancellationToken)
            .ConfigureAwait(false);

        LineAssembler stdout = new(isStderr: false);
        LineAssembler stderr = new(isStderr: true);
        byte[] buffer = new byte[16 * 1024];
        bool endOfStream = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            MultiplexedStream.ReadResult result;
            try
            {
                result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The subscription was torn down mid-read — the expected, clean end of a follow. Leave any
                // partially-assembled line unemitted rather than flush under a cancelled token.
                return;
            }

            if (result.EOF)
            {
                endOfStream = true;
                break;
            }

            if (result.Count == 0)
            {
                continue;
            }

            LineAssembler target = result.Target == MultiplexedStream.TargetStream.StandardError ? stderr : stdout;
            await target.AppendAsync(buffer, result.Count, onFrame, cancellationToken).ConfigureAwait(false);
        }

        // Only when the container's stream actually ended do we flush a trailing, newline-less final line.
        if (endOfStream)
        {
            await stdout.FlushAsync(onFrame, cancellationToken).ConfigureAwait(false);
            await stderr.FlushAsync(onFrame, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<string> CreateAsync(CreateContainerParameters parameters, CancellationToken cancellationToken)
    {
        CreateContainerResponse response = await _client.Containers
            .CreateContainerAsync(parameters, cancellationToken)
            .ConfigureAwait(false);
        return response.ID;
    }

    /// <inheritdoc />
    public Task StartAsync(string containerId, CancellationToken cancellationToken) =>
        _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(string containerId, int waitBeforeKillSeconds, CancellationToken cancellationToken) =>
        _client.Containers.StopContainerAsync(
            containerId,
            new ContainerStopParameters { WaitBeforeKillSeconds = (uint)waitBeforeKillSeconds },
            cancellationToken);

    /// <inheritdoc />
    public Task RestartAsync(string containerId, int waitBeforeKillSeconds, CancellationToken cancellationToken) =>
        _client.Containers.RestartContainerAsync(
            containerId,
            new ContainerRestartParameters { WaitBeforeKillSeconds = (uint)waitBeforeKillSeconds },
            cancellationToken);
}
