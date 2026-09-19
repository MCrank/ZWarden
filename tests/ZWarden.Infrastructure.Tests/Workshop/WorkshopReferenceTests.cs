using ZWarden.Application.Workshop;

namespace ZWarden.Infrastructure.Tests.Workshop;

/// <summary>
/// #110 PR-C: the pure parser for an operator-pasted Workshop reference — a bare numeric id, or a Steam URL that
/// carries one as its <c>id</c> query parameter. It only extracts a bounded numeric id; the input is untrusted
/// (trust-boundaries.md §3) and the URL is never dereferenced.
/// </summary>
public class WorkshopReferenceTests
{
    [Test]
    public async Task A_bare_numeric_id_parses()
    {
        await Assert.That(WorkshopReference.TryParseId("2392709985", out string id)).IsTrue();
        await Assert.That(id).IsEqualTo("2392709985");
    }

    [Test]
    public async Task Surrounding_whitespace_is_trimmed()
    {
        await Assert.That(WorkshopReference.TryParseId("  2392709985\n", out string id)).IsTrue();
        await Assert.That(id).IsEqualTo("2392709985");
    }

    [Test]
    public async Task An_item_url_parses_its_id_query_parameter()
    {
        const string url = "https://steamcommunity.com/sharedfiles/filedetails/?id=2392709985";

        await Assert.That(WorkshopReference.TryParseId(url, out string id)).IsTrue();
        await Assert.That(id).IsEqualTo("2392709985");
    }

    [Test]
    public async Task A_collection_url_with_extra_parameters_parses_its_id()
    {
        const string url = "https://steamcommunity.com/workshop/filedetails/?searchtext=&id=180&browsesort=trend";

        await Assert.That(WorkshopReference.TryParseId(url, out string id)).IsTrue();
        await Assert.That(id).IsEqualTo("180");
    }

    [Test]
    public async Task A_url_without_an_id_parameter_does_not_parse()
    {
        await Assert.That(WorkshopReference.TryParseId("https://steamcommunity.com/app/108600", out _)).IsFalse();
    }

    [Test]
    public async Task An_empty_or_non_numeric_string_does_not_parse()
    {
        await Assert.That(WorkshopReference.TryParseId("", out _)).IsFalse();
        await Assert.That(WorkshopReference.TryParseId("   ", out _)).IsFalse();
        await Assert.That(WorkshopReference.TryParseId("not-a-number", out _)).IsFalse();
        await Assert.That(WorkshopReference.TryParseId(null, out _)).IsFalse();
    }

    [Test]
    public async Task A_word_ending_in_id_is_not_mistaken_for_the_parameter()
    {
        // "guid=123" ends in "id=" but is not the id parameter (not preceded by ? or &).
        await Assert.That(WorkshopReference.TryParseId("https://x/y?guid=123", out _)).IsFalse();
    }
}
