using ZWarden.Application.Mods;
using ZWarden.Domain.Mods;

namespace ZWarden.Infrastructure.Tests.Mods;

/// <summary>
/// #290: the best-guess mod ids pulled from a Workshop item's description (authors conventionally list
/// <c>Mod ID: …</c> lines). Driven by the real descriptions of the friends' server items, captured from Steam on
/// 2026-10-02; after a boot, <c>mod.info</c> on disk is the truth and corrects the guess. Every id comes back as a
/// validated <see cref="PzModId"/> — description text is untrusted.
/// </summary>
public class WorkshopDescriptionModIdsTests
{
    [Test]
    public async Task More_traits_yields_its_four_b42_ids_without_the_workshop_prefix()
    {
        IReadOnlyList<PzModId> ids = WorkshopDescriptionModIds.Parse(Fixture("1299328280"));

        await Assert.That(Values(ids)).IsEqualTo("ToadTraits|ToadTraitsDisablePrepared|ToadTraitsDisableSpec|ToadTraitsDynamic");
    }

    [Test]
    [Arguments("3508537032", "NeatUI_Framework")]
    [Arguments("3451167732", "ModernStatus")]
    [Arguments("2553809727", "KillCount")]
    [Arguments("3576056135", "BetterGeneratorInfo")]
    [Arguments("3676995511", "PZShareMapNotes")]
    [Arguments("3776534799", "RUNE-EXP")]
    [Arguments("3682045254", "UnifiedCarryWeightFramework")]
    [Arguments("3750253491", "VB_CommonSense")]
    public async Task Single_mod_items_yield_exactly_their_one_id(string workshopId, string expected)
    {
        // NeatUI repeats its id in a BBCode bullet list and a plain footer; both collapse to one.
        IReadOnlyList<PzModId> ids = WorkshopDescriptionModIds.Parse(Fixture(workshopId));

        await Assert.That(Values(ids)).IsEqualTo(expected);
    }

    [Test]
    public async Task Equipment_ui_yields_every_listed_candidate_once_in_first_seen_order()
    {
        // The ambiguous one: it lists a B42 id, the original id (kept for dependants) and a patch, several times
        // and inside [b] tags. #291 picks among them; mod.info corrects the pick after boot.
        IReadOnlyList<PzModId> ids = WorkshopDescriptionModIds.Parse(Fixture("3682936016"));

        await Assert.That(Values(ids)).IsEqualTo("EQUIPMENT_UI_B42|EQUIPMENT_UI|equipmentuipatch");
    }

    [Test]
    public async Task A_description_with_no_mod_id_lines_yields_nothing()
    {
        const string description = "[h1]My Mod[/h1]\nAdds stuff. Workshop ID: 123456789\nThe mod ID is in the mod.info.";

        await Assert.That(WorkshopDescriptionModIds.Parse(description)).IsEmpty();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task Missing_description_yields_nothing(string? description) =>
        await Assert.That(WorkshopDescriptionModIds.Parse(description)).IsEmpty();

    [Test]
    public async Task Several_ids_on_one_line_split_on_commas_and_semicolons()
    {
        IReadOnlyList<PzModId> ids = WorkshopDescriptionModIds.Parse("Mod IDs: Alpha, Beta;Gamma");

        await Assert.That(Values(ids)).IsEqualTo("Alpha|Beta|Gamma");
    }

    [Test]
    public async Task Only_the_first_word_of_a_candidate_is_kept_so_trailing_notes_drop()
    {
        IReadOnlyList<PzModId> ids = WorkshopDescriptionModIds.Parse("ModID: CoolMod (for Build 42)\nMod ID: [i]Other[/i]");

        await Assert.That(Values(ids)).IsEqualTo("CoolMod|Other");
    }

    [Test]
    public async Task Candidates_the_mod_id_rule_rejects_are_dropped()
    {
        // Untrusted text: a quote or a path never becomes a candidate; a B42 prefix is stripped first.
        IReadOnlyList<PzModId> ids = WorkshopDescriptionModIds.Parse("Mod ID: \"Quoted\"\nMod ID: 42/Good\nMod ID: a=b");

        await Assert.That(Values(ids)).IsEqualTo("Good");
    }

    [Test]
    public async Task A_mention_of_mod_id_inside_a_sentence_is_not_a_candidate()
    {
        const string description = "Mods that need the original Mod ID as a dependency should use this.";

        await Assert.That(WorkshopDescriptionModIds.Parse(description)).IsEmpty();
    }

    // Joined so the assertion pins order as well as membership.
    private static string Values(IReadOnlyList<PzModId> ids) => string.Join("|", ids.Select(i => i.Value));

    private static string Fixture(string workshopId) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "workshop-descriptions", $"{workshopId}.txt"));
}
