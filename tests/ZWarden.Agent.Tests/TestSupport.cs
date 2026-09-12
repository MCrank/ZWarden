using Microsoft.Extensions.Logging;
using ZWarden.Agent.Identity;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests;

/// <summary>An <see cref="ILogger{T}"/> that records the rendered log entries for assertion.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<Entry> _entries = [];

    public IReadOnlyList<Entry> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _entries.Add(new Entry(logLevel, eventId, formatter(state, exception)));
    }

    internal sealed record Entry(LogLevel Level, EventId EventId, string Message);
}

/// <summary>A pre-resolved <see cref="IAgentIdentity"/> for tests that need one without a store.</summary>
internal sealed class FixedAgentIdentity : IAgentIdentity
{
    public FixedAgentIdentity(AgentId agentId) => AgentId = agentId;

    public AgentId AgentId { get; }
}

/// <summary>A self-cleaning temporary directory for the file-based identity-store tests.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zw-agent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leftover temp directory must not fail a test.
        }
    }
}
