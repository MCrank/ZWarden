using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;

namespace ZWarden.Domain.Tests.Mods;

/// <summary>
/// #290: a Server's mod lists as last observed in <c>servertest.ini</c> (the desired state) and as the server last
/// <b>booted with</b>. A boot marks the snapshot pending; the first config observation made at or after that boot
/// fills it (PZ has already read the file at launch). "Pending" changes are the difference between the two.
/// </summary>
public class ServerModStateTests
{
    private static readonly DateTimeOffset Boot = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task A_new_state_has_empty_lists_and_no_boot_snapshot()
    {
        ServerId server = ServerId.New();

        ServerModState state = ServerModState.For(server);

        await Assert.That(state.ServerId).IsEqualTo(server);
        await Assert.That(state.ConfiguredWorkshopIds).IsEmpty();
        await Assert.That(state.BootedModIds).IsEmpty();
        await Assert.That(state.HasBootSnapshot).IsFalse();
        await Assert.That(state.BootSnapshotPending).IsFalse();
    }

    [Test]
    public async Task Observing_config_records_the_configured_lists_in_order()
    {
        ServerModState state = ServerModState.For(ServerId.New());

        state.ObserveConfig(["200", "100"], ["B", "A"], Boot);

        await Assert.That(string.Join(";", state.ConfiguredWorkshopIds)).IsEqualTo("200;100");
        await Assert.That(string.Join(";", state.ConfiguredModIds)).IsEqualTo("B;A");
        await Assert.That(state.ConfigObservedAt).IsEqualTo(Boot);
        await Assert.That(state.HasBootSnapshot).IsFalse();
    }

    [Test]
    public async Task A_boot_then_a_later_observation_fills_the_booted_with_snapshot()
    {
        ServerModState state = ServerModState.For(ServerId.New());

        state.MarkBooted(Boot);
        await Assert.That(state.BootSnapshotPending).IsTrue();

        state.ObserveConfig(["100"], ["A"], Boot.AddSeconds(5));

        await Assert.That(state.BootSnapshotPending).IsFalse();
        await Assert.That(state.HasBootSnapshot).IsTrue();
        await Assert.That(state.BootedAt).IsEqualTo(Boot);
        await Assert.That(string.Join(";", state.BootedWorkshopIds)).IsEqualTo("100");
        await Assert.That(string.Join(";", state.BootedModIds)).IsEqualTo("A");
    }

    [Test]
    public async Task An_observation_older_than_the_boot_does_not_fill_the_snapshot()
    {
        // A discovery that started before the restart reports the pre-boot file; it must not become "booted with".
        ServerModState state = ServerModState.For(ServerId.New());
        state.MarkBooted(Boot);

        state.ObserveConfig(["100"], ["A"], Boot.AddSeconds(-1));

        await Assert.That(state.BootSnapshotPending).IsTrue();
        await Assert.That(state.HasBootSnapshot).IsFalse();
    }

    [Test]
    public async Task Config_changes_after_the_snapshot_leave_the_booted_with_lists_alone()
    {
        ServerModState state = ServerModState.For(ServerId.New());
        state.MarkBooted(Boot);
        state.ObserveConfig(["100", "200"], ["A", "B"], Boot.AddSeconds(5));

        state.ObserveConfig(["100"], ["A"], Boot.AddMinutes(10));

        await Assert.That(string.Join(";", state.ConfiguredWorkshopIds)).IsEqualTo("100");
        await Assert.That(string.Join(";", state.BootedWorkshopIds)).IsEqualTo("100;200");
        await Assert.That(string.Join(";", state.BootedModIds)).IsEqualTo("A;B");
    }

    [Test]
    public async Task A_second_boot_replaces_the_snapshot_on_its_next_observation()
    {
        ServerModState state = ServerModState.For(ServerId.New());
        state.MarkBooted(Boot);
        state.ObserveConfig(["100", "200"], ["A", "B"], Boot.AddSeconds(5));
        state.ObserveConfig(["100"], ["A"], Boot.AddMinutes(10));

        state.MarkBooted(Boot.AddMinutes(20));
        state.ObserveConfig(["100"], ["A"], Boot.AddMinutes(20).AddSeconds(5));

        await Assert.That(string.Join(";", state.BootedWorkshopIds)).IsEqualTo("100");
        await Assert.That(state.BootedAt).IsEqualTo(Boot.AddMinutes(20));
    }

    [Test]
    public async Task Oversized_lists_and_entries_are_rejected()
    {
        // The lists are Agent-reported (untrusted): bounded before they reach the database.
        ServerModState state = ServerModState.For(ServerId.New());
        string[] tooMany = [.. Enumerable.Range(0, ServerModState.MaxListEntries + 1).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))];
        string[] tooLong = [new string('a', ServerModState.MaxEntryLength + 1)];

        await Assert.That(() => state.ObserveConfig(tooMany, [], Boot)).Throws<ArgumentException>();
        await Assert.That(() => state.ObserveConfig([], tooLong, Boot)).Throws<ArgumentException>();
    }
}
