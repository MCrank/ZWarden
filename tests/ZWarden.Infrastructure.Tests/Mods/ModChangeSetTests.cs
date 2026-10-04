using ZWarden.Application.Mods;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;

namespace ZWarden.Infrastructure.Tests.Mods;

/// <summary>
/// #290: the pure derivation of each Workshop item's and mod id's status from the configured lists, the booted-with
/// snapshot and what is on disk. Configured and booted is Active; configured only installs on restart; booted only is
/// removed on restart; on disk but neither is a leftover. Before the first boot snapshot, everything configured counts
/// as Active, so a fresh install doesn't light up every row as pending.
/// </summary>
public class ModChangeSetTests
{
    private static readonly DateTimeOffset Boot = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly ServerId Server = ServerId.New();

    [Test]
    public async Task Configured_and_booted_items_and_mods_are_active_with_nothing_pending()
    {
        ServerModState state = Booted(workshop: ["100"], mods: ["A"]);

        ServerModOverview overview = ModChangeSet.Derive(Server, state, [OnDisk("100", "A")]);

        await Assert.That(Statuses(overview)).IsEqualTo("100=Active");
        await Assert.That(ModStatuses(overview)).IsEqualTo("A=Active");
        await Assert.That(overview.PendingChanges).IsEqualTo(0);
        await Assert.That(overview.HasBootSnapshot).IsTrue();
    }

    [Test]
    public async Task An_item_and_mod_added_since_boot_install_on_restart()
    {
        ServerModState state = Booted(workshop: ["100"], mods: ["A"]);
        state.ObserveConfig(["100", "200"], ["A", "B"], Boot.AddMinutes(5));

        ServerModOverview overview = ModChangeSet.Derive(Server, state, [OnDisk("100", "A")]);

        await Assert.That(Statuses(overview)).IsEqualTo("100=Active|200=InstallsOnRestart");
        await Assert.That(ModStatuses(overview)).IsEqualTo("A=Active|B=InstallsOnRestart");
        await Assert.That(overview.PendingChanges).IsEqualTo(2);
    }

    [Test]
    public async Task An_item_removed_since_boot_is_removed_on_restart_then_leftover_after_the_next_boot()
    {
        // The issue's acceptance: remove a mod and restart, and its files show as leftover with no manual Refresh.
        ServerModState state = Booted(workshop: ["100", "200"], mods: ["A", "B"]);
        state.ObserveConfig(["100"], ["A"], Boot.AddMinutes(5));
        ServerWorkshopItem[] items = [OnDisk("100", "A"), OnDisk("200", "B")];

        ServerModOverview before = ModChangeSet.Derive(Server, state, items);
        await Assert.That(Statuses(before)).IsEqualTo("100=Active|200=RemovedOnRestart");
        await Assert.That(ModStatuses(before)).IsEqualTo("A=Active|B=RemovedOnRestart");

        state.MarkBooted(Boot.AddMinutes(10));
        state.ObserveConfig(["100"], ["A"], Boot.AddMinutes(11));
        ServerModOverview after = ModChangeSet.Derive(Server, state, items);

        await Assert.That(Statuses(after)).IsEqualTo("100=Active|200=Leftover");
        await Assert.That(ModStatuses(after)).IsEqualTo("A=Active");
        await Assert.That(after.PendingChanges).IsEqualTo(0);
    }

    [Test]
    public async Task Without_a_boot_snapshot_everything_configured_is_active_and_unconfigured_files_are_leftover()
    {
        ServerModState state = ServerModState.For(Server);
        state.ObserveConfig(["100"], ["A"], Boot);

        ServerModOverview overview = ModChangeSet.Derive(Server, state, [OnDisk("100", "A"), OnDisk("300", "C")]);

        await Assert.That(Statuses(overview)).IsEqualTo("100=Active|300=Leftover");
        await Assert.That(overview.PendingChanges).IsEqualTo(0);
        await Assert.That(overview.HasBootSnapshot).IsFalse();
    }

    [Test]
    public async Task No_state_yet_yields_an_empty_overview()
    {
        ServerModOverview overview = ModChangeSet.Derive(Server, state: null, []);

        await Assert.That(overview.Items).IsEmpty();
        await Assert.That(overview.Mods).IsEmpty();
        await Assert.That(overview.HasBootSnapshot).IsFalse();
    }

    [Test]
    public async Task Items_follow_config_order_then_removals_then_leftovers_and_carry_their_steam_details()
    {
        ServerModState state = Booted(workshop: ["300", "200"], mods: []);
        state.ObserveConfig(["900", "100"], [], Boot.AddMinutes(1));
        ServerWorkshopItem detailed = ServerWorkshopItem.Track(Server, "100");
        detailed.ApplyMetadata("More Traits", null, null, null, ["Build 42"], [Id("ToadTraits")], Boot);

        ServerModOverview overview = ModChangeSet.Derive(Server, state, [detailed, OnDisk("050")]);

        await Assert.That(Statuses(overview))
            .IsEqualTo("900=InstallsOnRestart|100=InstallsOnRestart|300=RemovedOnRestart|200=RemovedOnRestart|050=Leftover");
        ModItemView view = overview.Items.Single(i => i.WorkshopId == "100");
        await Assert.That(view.Title).IsEqualTo("More Traits");
        await Assert.That(string.Join(";", view.GuessedModIds)).IsEqualTo("ToadTraits");
        await Assert.That(overview.Items.Single(i => i.WorkshopId == "900").Title).IsNull();
    }

    // ---- #291 Pick parts: the description's guess checked against mod.info ------------------------------------

    [Test]
    public async Task A_wrong_guess_asks_to_pick_parts_and_names_the_missing_id()
    {
        // Enabled "Guess" from the description, but the download provides "Real".
        ServerModState state = Booted(workshop: ["100"], mods: ["Guess"]);
        ServerWorkshopItem item = Guessed(OnDisk("100", "Real"), "Guess");

        ModItemView view = ModChangeSet.Derive(Server, state, [item]).Items.Single();

        await Assert.That(view.NeedsParts).IsTrue();
        await Assert.That(string.Join(";", view.MissingModIds)).IsEqualTo("Guess");
    }

    [Test]
    public async Task An_item_installed_without_ids_asks_to_pick_parts_once_its_files_are_on_disk()
    {
        ServerModState state = Booted(workshop: ["100"], mods: []);

        ModItemView view = ModChangeSet.Derive(Server, state, [OnDisk("100", "Real")]).Items.Single();

        await Assert.That(view.NeedsParts).IsTrue();
        await Assert.That(view.MissingModIds).IsEmpty();
    }

    [Test]
    public async Task A_correct_guess_needs_nothing()
    {
        ServerModState state = Booted(workshop: ["100"], mods: ["A"]);
        ServerWorkshopItem item = Guessed(OnDisk("100", "A", "Extra"), "A");

        ModItemView view = ModChangeSet.Derive(Server, state, [item]).Items.Single();

        await Assert.That(view.NeedsParts).IsFalse();
    }

    [Test]
    public async Task An_item_not_downloaded_yet_or_no_longer_configured_needs_nothing()
    {
        ServerModState state = Booted(workshop: ["100", "300"], mods: ["Guess"]);
        state.ObserveConfig(["100"], ["Guess"], Boot.AddMinutes(1));
        ServerWorkshopItem pending = Guessed(ServerWorkshopItem.Track(Server, "100"), "Guess");

        ServerModOverview overview = ModChangeSet.Derive(Server, state, [pending, OnDisk("300", "C")]);

        await Assert.That(overview.Items.Any(i => i.NeedsParts)).IsFalse();
    }

    [Test]
    public async Task An_active_item_whose_steam_version_is_newer_than_the_copy_on_disk_is_update_ready()
    {
        // #275 D3: Steam time_updated > the .acf timeupdated of the copy on disk ⇒ a restart would pull an update.
        ServerModState state = Booted(workshop: ["100", "200"], mods: ["A", "B"]);
        ServerWorkshopItem[] items =
        [
            Versions(OnDisk("100", "A"), steam: Boot.AddDays(1), installed: Boot.AddDays(-1)),
            Versions(OnDisk("200", "B"), steam: Boot.AddDays(-1), installed: Boot.AddDays(-1)),
        ];

        ServerModOverview overview = ModChangeSet.Derive(Server, state, items);

        await Assert.That(UpdateReady(overview)).IsEqualTo("100");
        await Assert.That(overview.UpdatesReady).IsEqualTo(1);
        await Assert.That(overview.PendingChanges).IsEqualTo(0);
    }

    [Test]
    public async Task Update_ready_needs_both_versions_known()
    {
        ServerModState state = Booted(workshop: ["100", "200"], mods: ["A", "B"]);
        ServerWorkshopItem[] items =
        [
            Versions(OnDisk("100", "A"), steam: Boot.AddDays(1), installed: null),
            Versions(OnDisk("200", "B"), steam: null, installed: Boot.AddDays(-1)),
        ];

        ServerModOverview overview = ModChangeSet.Derive(Server, state, items);

        await Assert.That(overview.UpdatesReady).IsEqualTo(0);
    }

    [Test]
    public async Task Only_active_items_are_update_ready_never_pending_or_leftover_ones()
    {
        // D8: the update flag only ever replaces Active. An item installing on restart is pulled anyway; a removed or
        // leftover one isn't in WorkshopItems=, so a restart doesn't update it.
        ServerModState state = Booted(workshop: ["100", "200"], mods: ["A", "B"]);
        state.ObserveConfig(["300"], ["C"], Boot.AddMinutes(5));
        ServerWorkshopItem[] items =
        [
            Versions(OnDisk("100", "A"), steam: Boot.AddDays(1), installed: Boot.AddDays(-1)),
            Versions(OnDisk("300", "C"), steam: Boot.AddDays(1), installed: Boot.AddDays(-1)),
            Versions(OnDisk("400", "D"), steam: Boot.AddDays(1), installed: Boot.AddDays(-1)),
        ];

        ServerModOverview overview = ModChangeSet.Derive(Server, state, items);

        await Assert.That(Statuses(overview)).IsEqualTo("300=InstallsOnRestart|100=RemovedOnRestart|200=RemovedOnRestart|400=Leftover");
        await Assert.That(overview.UpdatesReady).IsEqualTo(0);
    }

    private static ServerWorkshopItem Versions(ServerWorkshopItem item, DateTimeOffset? steam, DateTimeOffset? installed)
    {
        item.ApplyMetadata("t", null, null, steam, [], [], Boot);
        item.ObserveDisk(onDisk: true, [.. item.ObservedModIds.Select(Id)], Boot, installed);
        return item;
    }

    private static string UpdateReady(ServerModOverview overview) =>
        string.Join("|", overview.Items.Where(i => i.UpdateReady).Select(i => i.WorkshopId));

    private static ServerWorkshopItem Guessed(ServerWorkshopItem item, params string[] guesses)
    {
        item.ApplyMetadata("t", null, null, null, [], [.. guesses.Select(Id)], Boot);
        return item;
    }

    private static ServerModState Booted(string[] workshop, string[] mods)
    {
        ServerModState state = ServerModState.For(Server);
        state.MarkBooted(Boot);
        state.ObserveConfig(workshop, mods, Boot.AddSeconds(5));
        return state;
    }

    private static ServerWorkshopItem OnDisk(string workshopId, params string[] modIds)
    {
        ServerWorkshopItem item = ServerWorkshopItem.Track(Server, workshopId);
        item.ObserveDisk(onDisk: true, [.. modIds.Select(Id)], Boot);
        return item;
    }

    private static PzModId Id(string value) =>
        PzModId.TryCreate(value, out PzModId id) ? id : throw new ArgumentException(value);

    private static string Statuses(ServerModOverview overview) =>
        string.Join("|", overview.Items.Select(i => $"{i.WorkshopId}={i.Status}"));

    private static string ModStatuses(ServerModOverview overview) =>
        string.Join("|", overview.Mods.Select(m => $"{m.ModId}={m.Status}"));
}
