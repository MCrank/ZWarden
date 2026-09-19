using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using ZWarden.Application.Workshop;
using ZWarden.Infrastructure.Workshop;

namespace ZWarden.Infrastructure.Tests.Workshop;

/// <summary>
/// #110: the keyless Steam Web API metadata client. It enriches Workshop ids from <c>GetPublishedFileDetails</c> and
/// expands collections via <c>GetCollectionDetails</c> — no API key, control-plane egress only. The responses are
/// untrusted external JSON (trust-boundaries.md §8): the client parses defensively, bounds every field, and turns any
/// failure into a not-found/empty result rather than throwing, so enrichment never breaks browse or install. Tests
/// drive it through a stub handler with synthetic JSON — never a live call (F12 rule).
/// </summary>
public class WorkshopMetadataClientTests
{
    private static WorkshopMetadataClient NewClient(StubHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") },
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<WorkshopMetadataClient>.Instance);

    [Test]
    public async Task Enriches_an_item_from_published_file_details()
    {
        const string json = """
        {"response":{"result":1,"resultcount":1,"publishedfiledetails":[
          {"publishedfileid":"2857548524","result":1,"title":"Authentic Z","preview_url":"https://images.steam/az.jpg",
           "file_size":"123456","time_updated":1690000000,"file_description":"A realism overhaul."}
        ]}}
        """;
        StubHandler handler = new(HttpStatusCode.OK, json);

        IReadOnlyList<WorkshopItemMetadata> items = await NewClient(handler).GetItemsAsync(["2857548524"]);

        WorkshopItemMetadata item = items.Single();
        await Assert.That(item.Found).IsTrue();
        await Assert.That(item.Title).IsEqualTo("Authentic Z");
        await Assert.That(item.PreviewUrl).IsEqualTo("https://images.steam/az.jpg");
        await Assert.That(item.SizeBytes).IsEqualTo(123456L);
        await Assert.That(item.Description).IsEqualTo("A realism overhaul.");
        await Assert.That(item.UpdatedAt).IsEqualTo(DateTimeOffset.FromUnixTimeSeconds(1690000000));
    }

    [Test]
    public async Task A_file_size_as_a_json_number_is_parsed()
    {
        const string json = """
        {"response":{"publishedfiledetails":[{"publishedfileid":"111","result":1,"file_size":987}]}}
        """;

        WorkshopItemMetadata item = (await NewClient(new StubHandler(HttpStatusCode.OK, json)).GetItemsAsync(["111"])).Single();

        await Assert.That(item.SizeBytes).IsEqualTo(987L);
    }

    [Test]
    public async Task An_id_steam_omits_from_the_batch_comes_back_not_found()
    {
        const string json = """
        {"response":{"publishedfiledetails":[{"publishedfileid":"111","result":1,"title":"Present"}]}}
        """;

        IReadOnlyList<WorkshopItemMetadata> items = await NewClient(new StubHandler(HttpStatusCode.OK, json))
            .GetItemsAsync(["111", "222"]);

        await Assert.That(items.Single(i => i.WorkshopId == "111").Found).IsTrue();
        await Assert.That(items.Single(i => i.WorkshopId == "222").Found).IsFalse();
    }

    [Test]
    public async Task A_result_code_other_than_one_is_not_found()
    {
        const string json = """
        {"response":{"publishedfiledetails":[{"publishedfileid":"111","result":9}]}}
        """;

        WorkshopItemMetadata item = (await NewClient(new StubHandler(HttpStatusCode.OK, json)).GetItemsAsync(["111"])).Single();

        await Assert.That(item.Found).IsFalse();
    }

    [Test]
    public async Task A_non_https_preview_url_is_dropped()
    {
        const string json = """
        {"response":{"publishedfiledetails":[{"publishedfileid":"111","result":1,"preview_url":"javascript:alert(1)"}]}}
        """;

        WorkshopItemMetadata item = (await NewClient(new StubHandler(HttpStatusCode.OK, json)).GetItemsAsync(["111"])).Single();

        await Assert.That(item.PreviewUrl).IsNull();
    }

    [Test]
    public async Task Non_numeric_ids_are_dropped_without_a_call()
    {
        StubHandler handler = new(HttpStatusCode.OK, "{}");

        IReadOnlyList<WorkshopItemMetadata> items = await NewClient(handler).GetItemsAsync(["not-a-number", ""]);

        await Assert.That(items).IsEmpty();
        await Assert.That(handler.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task A_request_failure_degrades_every_id_to_not_found()
    {
        StubHandler handler = new(HttpStatusCode.ServiceUnavailable, null);

        IReadOnlyList<WorkshopItemMetadata> items = await NewClient(handler).GetItemsAsync(["111", "222"]);

        await Assert.That(items.Count).IsEqualTo(2);
        await Assert.That(items.All(i => !i.Found)).IsTrue();
    }

    [Test]
    public async Task Malformed_json_degrades_to_not_found_without_throwing()
    {
        StubHandler handler = new(HttpStatusCode.OK, "not json at all {");

        WorkshopItemMetadata item = (await NewClient(handler).GetItemsAsync(["111"])).Single();

        await Assert.That(item.Found).IsFalse();
    }

    [Test]
    public async Task Cached_items_are_not_refetched()
    {
        const string json = """
        {"response":{"publishedfiledetails":[{"publishedfileid":"111","result":1,"title":"Once"}]}}
        """;
        StubHandler handler = new(HttpStatusCode.OK, json);
        WorkshopMetadataClient client = NewClient(handler);

        await client.GetItemsAsync(["111"]);
        await client.GetItemsAsync(["111"]);

        await Assert.That(handler.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task Expands_a_collection_into_member_ids_in_order()
    {
        const string json = """
        {"response":{"result":1,"resultcount":1,"collectiondetails":[
          {"publishedfileid":"777","result":1,"children":[
            {"publishedfileid":"111","sortorder":0,"filetype":0},
            {"publishedfileid":"222","sortorder":1,"filetype":0}
          ]}
        ]}}
        """;

        IReadOnlyList<string> ids = await NewClient(new StubHandler(HttpStatusCode.OK, json)).GetCollectionItemIdsAsync("777");

        string[] expected = ["111", "222"];
        await Assert.That(ids).IsEquivalentTo(expected);
    }

    [Test]
    public async Task A_non_collection_id_yields_no_member_ids()
    {
        const string json = """{"response":{"result":9,"resultcount":0,"collectiondetails":[]}}""";

        IReadOnlyList<string> ids = await NewClient(new StubHandler(HttpStatusCode.OK, json)).GetCollectionItemIdsAsync("777");

        await Assert.That(ids).IsEmpty();
    }

    private sealed class StubHandler(HttpStatusCode status, string? json) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            HttpResponseMessage response = new(status);
            if (json is not null)
            {
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }
}
