using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ZWarden.Application.Workshop;

namespace ZWarden.Infrastructure.Workshop;

/// <summary>
/// The keyless Steam Web API implementation of <see cref="IWorkshopMetadataClient"/> (#110). It POSTs to the two
/// endpoints Valve serves without an API key — <c>ISteamRemoteStorage/GetPublishedFileDetails</c> and
/// <c>GetCollectionDetails</c> — against the <see cref="HttpClient"/> configured with an <c>api.steampowered.com</c>
/// base address and a bounded response buffer. Responses are <b>untrusted</b> external JSON (trust-boundaries.md §8):
/// parsed defensively with <see cref="JsonDocument"/>, every field bounded, wrong-typed or missing fields tolerated,
/// and any failure (network, timeout, HTTP status, malformed JSON) turned into an empty/not-found result rather than
/// a throw. Item metadata is cached per id so a browse that revisits ids does not re-hit Steam or its per-IP throttle.
/// </summary>
public sealed partial class WorkshopMetadataClient : IWorkshopMetadataClient
{
    // Steam ids are numeric strings. We reject anything else before it reaches the request, both as an untrusted-input
    // guard and because a non-numeric token can never resolve.
    private const int MaxIdsPerRequest = 100;
    private const int MaxChildren = 500;
    private const int MaxTitleLength = 512;
    private const int MaxUrlLength = 2048;
    private const int MaxDescriptionLength = 4000;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan NotFoundCacheTtl = TimeSpan.FromMinutes(5);

    private const string DetailsPath = "ISteamRemoteStorage/GetPublishedFileDetails/v1/";
    private const string CollectionPath = "ISteamRemoteStorage/GetCollectionDetails/v1/";

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WorkshopMetadataClient> _logger;

    public WorkshopMetadataClient(HttpClient http, IMemoryCache cache, ILogger<WorkshopMetadataClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkshopItemMetadata>> GetItemsAsync(
        IReadOnlyList<string> workshopIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workshopIds);

        // Keep only well-formed numeric ids, de-duplicated in first-seen order, and bound the batch size.
        List<string> ids = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string id in workshopIds)
        {
            if (IsNumericId(id) && seen.Add(id) && ids.Count < MaxIdsPerRequest)
            {
                ids.Add(id);
            }
        }

        if (ids.Count == 0)
        {
            return [];
        }

        // Serve what the cache already holds; only fetch the rest.
        List<WorkshopItemMetadata> results = [];
        List<string> toFetch = [];
        foreach (string id in ids)
        {
            if (_cache.TryGetValue(CacheKey(id), out WorkshopItemMetadata? cached) && cached is not null)
            {
                results.Add(cached);
            }
            else
            {
                toFetch.Add(id);
            }
        }

        if (toFetch.Count > 0)
        {
            foreach (WorkshopItemMetadata fetched in await FetchItemsAsync(toFetch, cancellationToken).ConfigureAwait(false))
            {
                _cache.Set(CacheKey(fetched.WorkshopId), fetched, fetched.Found ? CacheTtl : NotFoundCacheTtl);
                results.Add(fetched);
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetCollectionItemIdsAsync(
        string collectionId, CancellationToken cancellationToken = default)
    {
        if (!IsNumericId(collectionId))
        {
            return [];
        }

        JsonDocument? document = await PostAsync(
            CollectionPath,
            new Dictionary<string, string> { ["collectioncount"] = "1", ["publishedfileids[0]"] = collectionId },
            cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return [];
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("response", out JsonElement response) ||
                !response.TryGetProperty("collectiondetails", out JsonElement details) ||
                details.ValueKind != JsonValueKind.Array || details.GetArrayLength() == 0)
            {
                return [];
            }

            JsonElement first = details[0];
            if (!first.TryGetProperty("children", out JsonElement children) || children.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            List<string> ids = [];
            foreach (JsonElement child in children.EnumerateArray())
            {
                if (ids.Count >= MaxChildren)
                {
                    break;
                }

                string? childId = ReadString(child, "publishedfileid", MaxTitleLength);
                if (childId is not null && IsNumericId(childId))
                {
                    ids.Add(childId);
                }
            }

            return ids;
        }
    }

    private async Task<IReadOnlyList<WorkshopItemMetadata>> FetchItemsAsync(
        List<string> ids, CancellationToken cancellationToken)
    {
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["itemcount"] = ids.Count.ToString(CultureInfo.InvariantCulture),
        };
        for (int i = 0; i < ids.Count; i++)
        {
            form[$"publishedfileids[{i}]"] = ids[i];
        }

        JsonDocument? document = await PostAsync(DetailsPath, form, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            // The whole batch failed — every id degrades to not-found so the caller still renders bare ids.
            return [.. ids.Select(WorkshopItemMetadata.NotFound)];
        }

        using (document)
        {
            Dictionary<string, WorkshopItemMetadata> byId = new(StringComparer.Ordinal);
            if (document.RootElement.TryGetProperty("response", out JsonElement response) &&
                response.TryGetProperty("publishedfiledetails", out JsonElement items) &&
                items.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in items.EnumerateArray())
                {
                    WorkshopItemMetadata? parsed = ParseItem(item);
                    if (parsed is not null)
                    {
                        byId[parsed.WorkshopId] = parsed;
                    }
                }
            }

            // Preserve one entry per requested id; anything Steam omitted or returned as an error is not-found.
            return [.. ids.Select(id => byId.TryGetValue(id, out WorkshopItemMetadata? m) ? m : WorkshopItemMetadata.NotFound(id))];
        }
    }

    private static WorkshopItemMetadata? ParseItem(JsonElement item)
    {
        string? id = ReadString(item, "publishedfileid", MaxTitleLength);
        if (id is null || !IsNumericId(id))
        {
            return null;
        }

        // result != 1 means Steam has no usable record (deleted/hidden/unknown) — treat as not-found.
        if (item.TryGetProperty("result", out JsonElement result) && result.ValueKind == JsonValueKind.Number &&
            result.TryGetInt32(out int code) && code != 1)
        {
            return WorkshopItemMetadata.NotFound(id);
        }

        return new WorkshopItemMetadata(
            id,
            Found: true,
            Title: ReadString(item, "title", MaxTitleLength),
            PreviewUrl: ReadHttpUrl(item, "preview_url"),
            SizeBytes: ReadLong(item, "file_size"),
            UpdatedAt: ReadUnixSeconds(item, "time_updated"),
            Description: ReadString(item, "file_description", MaxDescriptionLength)
                ?? ReadString(item, "description", MaxDescriptionLength));
    }

    private async Task<JsonDocument?> PostAsync(
        string path, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using HttpResponseMessage response = await _http.PostAsync(path, content, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogHttpFailure(path, (int)response.StatusCode);
                return null;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            LogRequestFailed(path, ex.Message);
            return null;
        }
    }

    private static bool IsNumericId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 20 && id.All(char.IsAsciiDigit);

    private static string CacheKey(string id) => "wsmeta:" + id;

    private static string? ReadString(JsonElement element, string name, int maxLength)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? text = value.GetString();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static string? ReadHttpUrl(JsonElement element, string name)
    {
        string? url = ReadString(element, name, MaxUrlLength);
        return url is not null && Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp)
            ? url
            : null;
    }

    // file_size arrives as either a JSON number or a numeric string depending on the API version.
    private static long? ReadLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            return number >= 0 ? number : null;
        }

        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
        {
            return parsed >= 0 ? parsed : null;
        }

        return null;
    }

    private static DateTimeOffset? ReadUnixSeconds(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long seconds) && seconds > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Steam Workshop metadata request to {Path} returned HTTP {Status}.")]
    private partial void LogHttpFailure(string path, int status);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Steam Workshop metadata request to {Path} failed: {Reason}")]
    private partial void LogRequestFailed(string path, string reason);
}
