using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ZWarden.Application.Workshop;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Workshop;

namespace ZWarden.Infrastructure.Tests.Workshop;

/// <summary>
/// F110 PR-B: the key-gated <see cref="WorkshopSearchClient"/> calls <c>QueryFiles</c> only when a key is
/// configured, sends that key, parses untrusted results defensively, and degrades every failure to an
/// empty/unavailable result rather than throwing (trust-boundaries §8; ADR 0044). All JSON is synthetic and
/// served by a stub handler — no live Steam call (F12 rule).
/// </summary>
public class WorkshopSearchClientTests
{
    private const string Key = "ABCDEF0123456789ABCDEF0123456789";

    private const string TwoHits = """
        {"response":{"total":2,"publishedfiledetails":[
          {"result":1,"publishedfileid":"2857548524","title":"Authentic Z","preview_url":"https://img.example/a.jpg","lifetime_subscriptions":12345,"time_updated":1699999999},
          {"result":1,"publishedfileid":"2196102849","title":"Raven Creek","preview_url":"ftp://bad/x","subscriptions":"7","time_updated":1700000000}
        ]}}
        """;

    [Test]
    public async Task No_key_returns_unavailable_and_makes_no_call()
    {
        StubHandler handler = new(HttpStatusCode.OK, TwoHits);
        WorkshopSearchClient client = NewClient(handler, key: null);

        WorkshopSearchResults results = await client.SearchAsync("zombies");

        await Assert.That(results.SearchAvailable).IsFalse();
        await Assert.That(handler.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task Blank_query_with_a_key_returns_none_and_makes_no_call()
    {
        StubHandler handler = new(HttpStatusCode.OK, TwoHits);
        WorkshopSearchClient client = NewClient(handler, key: Key);

        WorkshopSearchResults results = await client.SearchAsync("   ");

        await Assert.That(results.SearchAvailable).IsTrue();
        await Assert.That(results.Items).IsEmpty();
        await Assert.That(handler.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task A_search_sends_the_key_and_text_and_parses_hits()
    {
        StubHandler handler = new(HttpStatusCode.OK, TwoHits);
        WorkshopSearchClient client = NewClient(handler, key: Key);

        WorkshopSearchResults results = await client.SearchAsync("raven creek");

        await Assert.That(handler.LastUri!).Contains("key=" + Key);
        // Uri.ToString() renders the query decoded; the escaping is applied on the wire.
        await Assert.That(handler.LastUri!).Contains("search_text=raven creek");
        await Assert.That(handler.LastUri!).Contains("query_type=11");
        await Assert.That(results.SearchAvailable).IsTrue();
        await Assert.That(results.Items.Count).IsEqualTo(2);

        WorkshopSearchResult first = results.Items[0];
        await Assert.That(first.WorkshopId).IsEqualTo("2857548524");
        await Assert.That(first.Title).IsEqualTo("Authentic Z");
        await Assert.That(first.PreviewUrl).IsEqualTo("https://img.example/a.jpg");
        await Assert.That(first.Subscriptions).IsEqualTo(12345L);
        await Assert.That(first.UpdatedAt).IsEqualTo(DateTimeOffset.FromUnixTimeSeconds(1699999999));

        // Second hit: non-https preview is dropped; a numeric-string subscription count is read.
        WorkshopSearchResult second = results.Items[1];
        await Assert.That(second.PreviewUrl).IsNull();
        await Assert.That(second.Subscriptions).IsEqualTo(7L);
    }

    [Test]
    public async Task A_rejected_key_reports_unavailable()
    {
        StubHandler handler = new(HttpStatusCode.Forbidden, null);
        WorkshopSearchClient client = NewClient(handler, key: Key);

        WorkshopSearchResults results = await client.SearchAsync("zombies");

        await Assert.That(results.SearchAvailable).IsFalse();
    }

    [Test]
    public async Task A_server_error_degrades_to_an_available_empty_result()
    {
        StubHandler handler = new(HttpStatusCode.InternalServerError, null);
        WorkshopSearchClient client = NewClient(handler, key: Key);

        WorkshopSearchResults results = await client.SearchAsync("zombies");

        await Assert.That(results.SearchAvailable).IsTrue();
        await Assert.That(results.Items).IsEmpty();
    }

    [Test]
    public async Task Malformed_json_degrades_without_throwing()
    {
        StubHandler handler = new(HttpStatusCode.OK, "{ this is not json ");
        WorkshopSearchClient client = NewClient(handler, key: Key);

        WorkshopSearchResults results = await client.SearchAsync("zombies");

        await Assert.That(results.SearchAvailable).IsTrue();
        await Assert.That(results.Items).IsEmpty();
    }

    [Test]
    public async Task Non_numeric_ids_and_error_results_are_skipped()
    {
        const string mixed = """
            {"response":{"publishedfiledetails":[
              {"result":1,"publishedfileid":"abc","title":"bad id"},
              {"result":9,"publishedfileid":"123","title":"deleted"},
              {"result":1,"publishedfileid":"456","title":"good"}
            ]}}
            """;
        StubHandler handler = new(HttpStatusCode.OK, mixed);
        WorkshopSearchClient client = NewClient(handler, key: Key);

        WorkshopSearchResults results = await client.SearchAsync("zombies");

        await Assert.That(results.Items.Count).IsEqualTo(1);
        await Assert.That(results.Items[0].WorkshopId).IsEqualTo("456");
    }

    private static WorkshopSearchClient NewClient(StubHandler handler, string? key) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") },
            new FakeWorkshopSettings(key),
            NullLogger<WorkshopSearchClient>.Instance);

    private sealed class StubHandler(HttpStatusCode status, string? json) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri?.ToString();
            HttpResponseMessage response = new(status);
            if (json is not null)
            {
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }

    /// <summary>A settings stub that only answers the decrypted-key lookup the search client uses.</summary>
    private sealed class FakeWorkshopSettings(string? key) : IWorkshopSettingsService
    {
        public Task<SecretString?> GetActiveApiKeyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<SecretString?>(key is null ? null : new SecretString(key));

        public Task SetApiKeyAsync(ZWarden.Domain.Ids.UserId actor, SecretString apiKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ClearApiKeyAsync(ZWarden.Domain.Ids.UserId actor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsSearchAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(key is not null);
    }
}
