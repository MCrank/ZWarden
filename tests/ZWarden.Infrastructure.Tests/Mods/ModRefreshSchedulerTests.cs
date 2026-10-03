using ZWarden.Application.Mods;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Mods;

namespace ZWarden.Infrastructure.Tests.Mods;

/// <summary>
/// #290 D1: the in-process mod refresh scheduler. Requests come out in order; a duplicate of a request still waiting
/// is dropped (coalescing a burst of triggers for one Server); once read, the same request can queue again; a delayed
/// request arrives after its delay; and a full queue drops the oldest without wedging its de-duplication.
/// </summary>
public class ModRefreshSchedulerTests
{
    private static readonly TenantId Tenant = TenantId.New();

    [Test]
    public async Task Requests_are_read_in_order()
    {
        using ModRefreshScheduler scheduler = new(TimeProvider.System);
        ModRefreshRequest first = Discover(ServerId.New());
        ModRefreshRequest second = Discover(ServerId.New());

        scheduler.Enqueue(first);
        scheduler.Enqueue(second);

        await Assert.That(await ReadAsync(scheduler)).IsEqualTo(first);
        await Assert.That(await ReadAsync(scheduler)).IsEqualTo(second);
    }

    [Test]
    public async Task A_duplicate_of_a_waiting_request_is_dropped_but_can_queue_again_once_read()
    {
        using ModRefreshScheduler scheduler = new(TimeProvider.System);
        ModRefreshRequest request = Discover(ServerId.New());
        ModRefreshRequest other = Discover(ServerId.New());

        scheduler.Enqueue(request);
        scheduler.Enqueue(request);
        scheduler.Enqueue(other);

        await Assert.That(await ReadAsync(scheduler)).IsEqualTo(request);
        await Assert.That(await ReadAsync(scheduler)).IsEqualTo(other);

        scheduler.Enqueue(request);
        await Assert.That(await ReadAsync(scheduler)).IsEqualTo(request);
    }

    [Test]
    public async Task A_delayed_request_arrives_after_its_delay()
    {
        using ModRefreshScheduler scheduler = new(TimeProvider.System);
        ModRefreshRequest request = Discover(ServerId.New());

        scheduler.EnqueueAfter(request, TimeSpan.FromMilliseconds(50));

        await Assert.That(scheduler.Count).IsEqualTo(0);
        await Assert.That(await ReadAsync(scheduler)).IsEqualTo(request);
    }

    [Test]
    public async Task A_full_queue_drops_the_oldest_and_the_dropped_request_can_queue_again()
    {
        using ModRefreshScheduler scheduler = new(TimeProvider.System, capacity: 2);
        ModRefreshRequest a = Discover(ServerId.New());
        ModRefreshRequest b = Discover(ServerId.New());
        ModRefreshRequest c = Discover(ServerId.New());

        scheduler.Enqueue(a);
        scheduler.Enqueue(b);
        scheduler.Enqueue(c); // drops a
        scheduler.Enqueue(a); // a is no longer waiting, so it queues again (dropping b)

        await Assert.That(await ReadAsync(scheduler)).IsEqualTo(c);
        await Assert.That(await ReadAsync(scheduler)).IsEqualTo(a);
    }

    private static ModRefreshRequest Discover(ServerId server) => new(Tenant, ModRefreshKind.DiscoverServer, Server: server);

    private static async Task<ModRefreshRequest> ReadAsync(ModRefreshScheduler scheduler)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        return await scheduler.ReadAsync(timeout.Token);
    }
}
