using ZWarden.Agent.Docker;

namespace ZWarden.Agent.Tests.Docker;

/// <summary>A recording <see cref="IServerHostDirectories"/> that touches no real filesystem — it just
/// captures the specs it was asked to prepare, so tests can assert provisioning ensures the bind sources.</summary>
public sealed class FakeServerHostDirectories : IServerHostDirectories
{
    public List<PzContainerSpec> Created { get; } = [];

    public void EnsureCreated(PzContainerSpec spec) => Created.Add(spec);
}
