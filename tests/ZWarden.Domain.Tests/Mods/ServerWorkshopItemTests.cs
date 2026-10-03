using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;

namespace ZWarden.Domain.Tests.Mods;

/// <summary>
/// #290: what the control plane knows about one Workshop item on one Server: Steam details (display data), the mod
/// ids <b>guessed</b> from its description, and the mod ids <b>observed</b> in <c>mod.info</c> on disk (the truth).
/// A cache and annotation, never the authority: config is the desired state.
/// </summary>
public class ServerWorkshopItemTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Tracking_an_item_records_the_server_and_numeric_workshop_id()
    {
        ServerId server = ServerId.New();

        ServerWorkshopItem item = ServerWorkshopItem.Track(server, "1299328280");

        await Assert.That(item.ServerId).IsEqualTo(server);
        await Assert.That(item.WorkshopId).IsEqualTo("1299328280");
        await Assert.That(item.Id.ToString()).StartsWith("wsi-");
        await Assert.That(item.OnDisk).IsFalse();
        await Assert.That(item.HasMetadata).IsFalse();
        await Assert.That(item.GuessedModIds).IsEmpty();
        await Assert.That(item.ObservedModIds).IsEmpty();
    }

    [Test]
    [Arguments("")]
    [Arguments("12a")]
    [Arguments("123456789012345678901")]
    public async Task Tracking_rejects_a_non_numeric_or_overlong_workshop_id(string workshopId) =>
        await Assert.That(() => ServerWorkshopItem.Track(ServerId.New(), workshopId)).Throws<ArgumentException>();

    [Test]
    public async Task Observing_disk_records_presence_and_the_mod_info_ids()
    {
        ServerWorkshopItem item = ServerWorkshopItem.Track(ServerId.New(), "100");

        item.ObserveDisk(onDisk: true, [Id("ToadTraits"), Id("ToadTraitsDynamic")], At);

        await Assert.That(item.OnDisk).IsTrue();
        await Assert.That(string.Join(";", item.ObservedModIds)).IsEqualTo("ToadTraits;ToadTraitsDynamic");
        await Assert.That(item.ObservedAt).IsEqualTo(At);
    }

    [Test]
    public async Task Observing_the_item_gone_from_disk_clears_its_observed_ids()
    {
        ServerWorkshopItem item = ServerWorkshopItem.Track(ServerId.New(), "100");
        item.ObserveDisk(onDisk: true, [Id("A")], At);

        item.ObserveDisk(onDisk: false, [], At.AddMinutes(1));

        await Assert.That(item.OnDisk).IsFalse();
        await Assert.That(item.ObservedModIds).IsEmpty();
    }

    [Test]
    public async Task Applying_metadata_records_steam_details_and_the_guessed_ids()
    {
        ServerWorkshopItem item = ServerWorkshopItem.Track(ServerId.New(), "100");
        DateTimeOffset updated = At.AddDays(-3);

        item.ApplyMetadata(
            "More Traits", "https://images.steam/mt.jpg", 123_456, updated, ["Build 42", "Traits"],
            [Id("ToadTraits")], At);

        await Assert.That(item.HasMetadata).IsTrue();
        await Assert.That(item.Title).IsEqualTo("More Traits");
        await Assert.That(item.PreviewUrl).IsEqualTo("https://images.steam/mt.jpg");
        await Assert.That(item.SizeBytes).IsEqualTo(123_456L);
        await Assert.That(item.SteamUpdatedAt).IsEqualTo(updated);
        await Assert.That(string.Join(";", item.Tags)).IsEqualTo("Build 42;Traits");
        await Assert.That(string.Join(";", item.GuessedModIds)).IsEqualTo("ToadTraits");
        await Assert.That(item.MetadataRefreshedAt).IsEqualTo(At);
    }

    [Test]
    public async Task Metadata_text_is_bounded_and_extra_tags_are_dropped()
    {
        // Steam text is untrusted: the client bounds it, and the entity enforces the column limits again.
        ServerWorkshopItem item = ServerWorkshopItem.Track(ServerId.New(), "100");
        string[] tags = [.. Enumerable.Range(0, ServerWorkshopItem.MaxTags + 5).Select(i => $"t{i}")];

        item.ApplyMetadata(new string('x', ServerWorkshopItem.MaxTitleLength + 10), null, null, null, tags, [], At);

        await Assert.That(item.Title!.Length).IsEqualTo(ServerWorkshopItem.MaxTitleLength);
        await Assert.That(item.Tags.Count).IsEqualTo(ServerWorkshopItem.MaxTags);
    }

    private static PzModId Id(string value) =>
        PzModId.TryCreate(value, out PzModId id) ? id : throw new ArgumentException(value);
}
