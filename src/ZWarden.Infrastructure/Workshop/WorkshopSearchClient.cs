using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZWarden.Application.Workshop;
using ZWarden.Domain.Security;

namespace ZWarden.Infrastructure.Workshop;

/// <summary>
/// The key-gated Workshop search implementation of <see cref="IWorkshopSearchService"/> (#110 PR-B; ADR 0044).
/// It calls <c>IPublishedFileService/QueryFiles</c> — which, unlike keyless lookup, requires the tenant's stored
/// Steam Web API key (research §4 addendum: a standard <c>steamcommunity.com/dev/apikey</c> key suffices; a
/// publisher key is not needed). The key is fetched decrypted per request from <see cref="IWorkshopSettingsService"/>
/// and used for exactly one call; it is never cached in plaintext and never logged (the request URL carries the
/// key, so only the path and status are logged). Responses are untrusted external JSON (trust-boundaries §8):
/// parsed defensively, bounded, and any failure degrades to an empty/unavailable result rather than a throw.
/// A rejected key (401/403) reports <see cref="WorkshopSearchResults.Unavailable"/>, matching the capability model.
/// </summary>
public sealed partial class WorkshopSearchClient : IWorkshopSearchService
{
    private const string QueryFilesPath = "IPublishedFileService/QueryFiles/v1/";
    private const string AppId = "108600"; // Project Zomboid (research §4: Workshop lives under 108600)
    private const int QueryTypeRankedByTextSearch = 11; // EPublishedFileQueryType
    private const int MaxResults = 25;
    private const int MaxQueryLength = 256;
    private const int MaxTitleLength = 512;
    private const int MaxUrlLength = 2048;

    private readonly HttpClient _http;
    private readonly IWorkshopSettingsService _settings;
    private readonly ILogger<WorkshopSearchClient> _logger;

    public WorkshopSearchClient(HttpClient http, IWorkshopSettingsService settings, ILogger<WorkshopSearchClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WorkshopSearchResults> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        SecretString? key = await _settings.GetActiveApiKeyAsync(cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            return WorkshopSearchResults.Unavailable;
        }

        string text = (query ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return WorkshopSearchResults.None;
        }

        if (text.Length > MaxQueryLength)
        {
            text = text[..MaxQueryLength];
        }

        string requestUri = BuildRequestUri(key.Value.Reveal(), text);
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // The stored key was rejected — surface as "search unavailable", not an empty match.
                LogKeyRejected((int)response.StatusCode);
                return WorkshopSearchResults.Unavailable;
            }

            if (!response.IsSuccessStatusCode)
            {
                LogHttpFailure((int)response.StatusCode);
                return WorkshopSearchResults.None;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false);
            return Parse(document);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            LogRequestFailed(ex.Message);
            return WorkshopSearchResults.None;
        }
    }

    private static string BuildRequestUri(string key, string text)
    {
        // The key rides in the query string (QueryFiles is GET) — this string is never logged.
        StringBuilder sb = new(QueryFilesPath);
        sb.Append("?key=").Append(Uri.EscapeDataString(key));
        sb.Append("&query_type=").Append(QueryTypeRankedByTextSearch.ToString(CultureInfo.InvariantCulture));
        sb.Append("&appid=").Append(AppId);
        sb.Append("&numperpage=").Append(MaxResults.ToString(CultureInfo.InvariantCulture));
        sb.Append("&return_metadata=true");
        sb.Append("&return_previews=true");
        sb.Append("&search_text=").Append(Uri.EscapeDataString(text));
        return sb.ToString();
    }

    private static WorkshopSearchResults Parse(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("response", out JsonElement response) ||
            !response.TryGetProperty("publishedfiledetails", out JsonElement items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return WorkshopSearchResults.None;
        }

        List<WorkshopSearchResult> results = [];
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (results.Count >= MaxResults)
            {
                break;
            }

            string? id = WorkshopJson.ReadString(item, "publishedfileid", MaxTitleLength);
            if (id is null || !WorkshopJson.IsNumericId(id))
            {
                continue;
            }

            // result != 1 means Steam has no usable record for this hit (deleted/hidden) — skip it.
            if (item.TryGetProperty("result", out JsonElement result) && result.ValueKind == JsonValueKind.Number &&
                result.TryGetInt32(out int code) && code != 1)
            {
                continue;
            }

            results.Add(new WorkshopSearchResult(
                id,
                Title: WorkshopJson.ReadString(item, "title", MaxTitleLength),
                PreviewUrl: WorkshopJson.ReadHttpUrl(item, "preview_url", MaxUrlLength),
                Subscriptions: WorkshopJson.ReadLong(item, "lifetime_subscriptions")
                    ?? WorkshopJson.ReadLong(item, "subscriptions"),
                UpdatedAt: WorkshopJson.ReadUnixSeconds(item, "time_updated")));
        }

        return results.Count == 0 ? WorkshopSearchResults.None : WorkshopSearchResults.From(results);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Steam Workshop search key was rejected with HTTP {Status}; search reported unavailable.")]
    private partial void LogKeyRejected(int status);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Steam Workshop search returned HTTP {Status}.")]
    private partial void LogHttpFailure(int status);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Steam Workshop search request failed: {Reason}")]
    private partial void LogRequestFailed(string reason);
}
