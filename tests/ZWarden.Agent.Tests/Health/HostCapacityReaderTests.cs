using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Health;
using ZWarden.Agent.Tests.Docker;
using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Agent.Tests.Health;

/// <summary>
/// #230: the host capacity report is Docker's view of the host (total RAM, limits committed to this Agent's
/// containers) plus the Agent's own sizing policy (overhead, default heap, host reserve), so the wizard can show what
/// a new server would cost and what is free.
/// </summary>
public class HostCapacityReaderTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Test]
    public async Task The_report_combines_the_hosts_memory_with_the_agents_sizing_policy()
    {
        var runtime = new FakeContainerRuntime { HostMemory = new HostMemory(32 * GiB, 20 * GiB) };
        var options = new AgentOptions
        {
            DefaultHeapSizeBytes = 5 * GiB,
            MemoryOverheadBytes = 3 * GiB,
            HostMemoryReserveBytes = 4 * GiB,
        };

        HostCapacityReport report = await new HostCapacityReader(runtime, Options.Create(options)).ReadAsync(CancellationToken.None);

        await Assert.That(report).IsEqualTo(new HostCapacityReport(
            TotalMemoryBytes: 32 * GiB,
            CommittedMemoryBytes: 20 * GiB,
            MemoryOverheadBytes: 3 * GiB,
            DefaultHeapSizeBytes: 5 * GiB,
            ReserveMemoryBytes: 4 * GiB));
    }
}
