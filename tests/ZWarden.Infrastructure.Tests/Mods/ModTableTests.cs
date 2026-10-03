using ZWarden.Application.Mods;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Tests.Mods;

/// <summary>
/// #292 D1: the Mods table's rows. A row is a Workshop item, placed by its first enabled mod id in <c>Mods=</c> (load
/// order); its ids are the enabled ones its files (or, before the download, its description) provide. Enabled ids no
/// item provides get an "other mod" row. Items with nothing enabled follow in config order, then removals; leftovers
/// sit apart for the footer. ▲/▼ moves a row's whole id block past its neighbour's block, as one reorder.
/// </summary>
public class ModTableTests
{
    private static readonly ServerId Server = ServerId.New();

    [Test]
    public async Task Rows_follow_the_load_order_of_their_first_enabled_id_not_the_workshop_order()
    {
        ServerModOverview overview = Overview(
            [Item("100", observed: ["A"]), Item("200", observed: ["B"])],
            ["B", "A"]);

        ModTableView table = ModTable.Build(overview);

        await Assert.That(Keys(table)).IsEqualTo("item:200=B|item:100=A");
    }

    [Test]
    public async Task A_multi_part_item_is_one_row_holding_its_enabled_parts_in_load_order()
    {
        ServerModOverview overview = Overview(
            [Item("100", observed: ["P1", "P2", "P3"]), Item("200", observed: ["X"])],
            ["P2", "X", "P1"]);

        ModTableView table = ModTable.Build(overview);

        await Assert.That(Keys(table)).IsEqualTo("item:100=P2,P1|item:200=X");
    }

    [Test]
    public async Task Before_the_download_an_item_claims_the_ids_its_description_lists()
    {
        ServerModOverview overview = Overview(
            [Item("100", ModChangeStatus.InstallsOnRestart, guessed: ["KillCount"], onDisk: false)],
            [new ModIdView("KillCount", ModChangeStatus.InstallsOnRestart)]);

        ModTableView table = ModTable.Build(overview);

        await Assert.That(Keys(table)).IsEqualTo("item:100=KillCount");
        await Assert.That(table.Rows[0].Status).IsEqualTo(ModChangeStatus.InstallsOnRestart);
    }

    [Test]
    public async Task Files_on_disk_win_over_another_items_description_guess()
    {
        ServerModOverview overview = Overview(
            [Item("100", guessed: ["Shared"]), Item("200", observed: ["Shared"])],
            ["Shared"]);

        ModTableView table = ModTable.Build(overview);

        await Assert.That(Keys(table)).IsEqualTo("item:200=Shared|item:100=");
    }

    [Test]
    public async Task An_enabled_id_no_item_provides_gets_its_own_other_mod_row_in_place()
    {
        ServerModOverview overview = Overview(
            [Item("100", observed: ["A"]), Item("200", observed: ["C"])],
            ["A", "LocalMod", "C"]);

        ModTableView table = ModTable.Build(overview);

        await Assert.That(Keys(table)).IsEqualTo("item:100=A|mod:LocalMod=LocalMod|item:200=C");
        await Assert.That(table.Rows[1].Item).IsNull();
    }

    [Test]
    public async Task Items_with_nothing_enabled_follow_in_config_order_then_removals_then_leftovers_apart()
    {
        ServerModOverview overview = Overview(
            [
                Item("300", observed: ["Z"]),
                Item("100", observed: ["A"]),
                Item("200", ModChangeStatus.InstallsOnRestart, onDisk: false),
                Item("400", ModChangeStatus.RemovedOnRestart, observed: ["R"]),
                Item("500", ModChangeStatus.Leftover, observed: ["L"]),
            ],
            [
                new ModIdView("A", ModChangeStatus.Active),
                new ModIdView("R", ModChangeStatus.RemovedOnRestart),
            ]);

        ModTableView table = ModTable.Build(overview);

        await Assert.That(Keys(table)).IsEqualTo("item:100=A|item:300=|item:200=|item:400=R");
        await Assert.That(string.Join("|", table.Leftovers.Select(l => l.WorkshopId))).IsEqualTo("500");
    }

    [Test]
    public async Task A_removed_mod_id_no_item_provides_is_a_removed_other_row_at_the_end()
    {
        ServerModOverview overview = Overview(
            [Item("100", observed: ["A"])],
            [new ModIdView("A", ModChangeStatus.Active), new ModIdView("Gone", ModChangeStatus.RemovedOnRestart)]);

        ModTableView table = ModTable.Build(overview);

        await Assert.That(Keys(table)).IsEqualTo("item:100=A|mod:Gone=Gone");
        await Assert.That(table.Rows[1].Status).IsEqualTo(ModChangeStatus.RemovedOnRestart);
    }

    [Test]
    public async Task Only_rows_with_an_enabled_id_can_move_and_the_ends_cannot_move_outwards()
    {
        ServerModOverview overview = Overview(
            [Item("100", observed: ["A"]), Item("200", observed: ["B"]), Item("300", onDisk: false)],
            ["A", "B"]);

        ModTableView table = ModTable.Build(overview);

        await Assert.That(Movability(table)).IsEqualTo("item:100=down|item:200=up|item:300=");
    }

    [Test]
    public async Task Moving_a_row_down_swaps_its_whole_id_block_with_the_next_rows_block()
    {
        ServerModOverview overview = Overview(
            [Item("100", observed: ["P1", "P2"]), Item("200", observed: ["X"]), Item("300", observed: ["Y"])],
            ["P1", "P2", "X", "Y"]);

        IReadOnlyList<string>? order = ModTable.Build(overview).MoveOrder("item:100", ModMove.Down);

        await Assert.That(string.Join(",", order!)).IsEqualTo("X,P1,P2,Y");
    }

    [Test]
    public async Task Moving_a_row_up_groups_its_scattered_parts_into_one_block()
    {
        ServerModOverview overview = Overview(
            [Item("100", observed: ["P1", "P2"]), Item("200", observed: ["X"]), Item("300", observed: ["Y"])],
            ["Y", "P1", "X", "P2"]);

        IReadOnlyList<string>? order = ModTable.Build(overview).MoveOrder("item:200", ModMove.Up);

        // Rows: 300 (Y), 100 (P1 + P2, first at #2), 200 (X); 200 moves above 100 and 100's parts close up.
        await Assert.That(string.Join(",", order!)).IsEqualTo("Y,X,P1,P2");
    }

    [Test]
    public async Task A_move_keeps_exactly_the_enabled_ids_including_other_mod_rows()
    {
        ServerModOverview overview = Overview([Item("100", observed: ["A"])], ["LocalMod", "A"]);

        IReadOnlyList<string>? order = ModTable.Build(overview).MoveOrder("mod:LocalMod", ModMove.Down);

        await Assert.That(string.Join(",", order!)).IsEqualTo("A,LocalMod");
    }

    [Test]
    public async Task A_move_past_the_edge_or_of_an_unknown_or_unmovable_row_is_null()
    {
        ServerModOverview overview = Overview(
            [Item("100", observed: ["A"]), Item("200", onDisk: false)],
            ["A"]);
        ModTableView table = ModTable.Build(overview);

        await Assert.That(table.MoveOrder("item:100", ModMove.Up)).IsNull();
        await Assert.That(table.MoveOrder("item:100", ModMove.Down)).IsNull();
        await Assert.That(table.MoveOrder("item:200", ModMove.Up)).IsNull();
        await Assert.That(table.MoveOrder("item:999", ModMove.Up)).IsNull();
    }

    [Test]
    public async Task An_overview_before_the_first_discovery_has_no_rows()
    {
        ModTableView table = ModTable.Build(new ServerModOverview(Server, false, null, [], []));

        await Assert.That(table.Rows).IsEmpty();
        await Assert.That(table.Leftovers).IsEmpty();
    }

    private static ServerModOverview Overview(IReadOnlyList<ModItemView> items, IReadOnlyList<string> activeMods) =>
        Overview(items, [.. activeMods.Select(m => new ModIdView(m, ModChangeStatus.Active))]);

    private static ServerModOverview Overview(IReadOnlyList<ModItemView> items, IReadOnlyList<ModIdView> mods) =>
        new(Server, true, null, items, mods);

    private static ModItemView Item(
        string workshopId,
        ModChangeStatus status = ModChangeStatus.Active,
        IReadOnlyList<string>? observed = null,
        IReadOnlyList<string>? guessed = null,
        bool onDisk = true) =>
        new(workshopId, status, $"Title {workshopId}", null, null, null, [], guessed ?? [], observed ?? [], onDisk);

    private static string Keys(ModTableView table) =>
        string.Join("|", table.Rows.Select(r => $"{r.Key}={string.Join(",", r.Mods.Select(m => m.ModId))}"));

    private static string Movability(ModTableView table) =>
        string.Join("|", table.Rows.Select(r =>
            $"{r.Key}={string.Join(",", new[] { r.CanMoveUp ? "up" : null, r.CanMoveDown ? "down" : null }.OfType<string>())}"));
}
