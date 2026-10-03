using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZWarden.Application.Authorization;
using ZWarden.Application.Workshop;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Workshop;

/// <summary>
/// The key-gated <see cref="IWorkshopDependencyService"/> (#291 D5). It authorizes <c>Mod.View</c> on the Server,
/// then — only when the tenant has a Steam Web API key — calls <c>IPublishedFileService/GetDetails</c> with
/// <c>includechildren=true</c> for the item's required-item ids, and resolves their details through the keyless
/// <see cref="IWorkshopMetadataClient"/>. As with search, the key is fetched decrypted per call, rides only in the
/// request URL, and is never logged or cached. The reply is untrusted JSON: ids are kept only when numeric and
/// bounded, de-duplicated, never the item itself, and capped. Every failure degrades to "no dependencies".
/// </summary>
public sealed partial class WorkshopDependencyService : IWorkshopDependencyService
{
    private const string GetDetailsPath = "IPublishedFileService/GetDetails/v1/";
    private const int MaxRequired = 50;
    private const int MaxIdLength = 20;

    private readonly HttpClient _http;
    private readonly ServerRepository _servers;
    private readonly IPermissionChecker _permissions;
    private readonly IWorkshopSettingsService _settings;
    private readonly IWorkshopMetadataClient _metadata;
    private readonly ILogger<WorkshopDependencyService> _logger;

    public WorkshopDependencyService(
        HttpClient http,
        ServerRepository servers,
        IPermissionChecker permissions,
        IWorkshopSettingsService settings,
        IWorkshopMetadataClient metadata,
        ILogger<WorkshopDependencyService> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(logger);
        _http = http;
        _servers = servers;
        _permissions = permissions;
        _settings = settings;
        _metadata = metadata;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkshopItemMetadata>> GetRequiredItemsAsync(
        UserId actor, ServerId server, string workshopId, CancellationToken cancellationToken = default)
    {
        string id = (workshopId ?? string.Empty).Trim();
        if (!IsWorkshopId(id)
            || await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false) is null)
        {
            return [];
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(actor, Permissions.ModView, server: server, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return [];
        }

        SecretString? key = await _settings.GetActiveApiKeyAsync(cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            return [];
        }

        IReadOnlyList<string> required = await GetChildIdsAsync(key.Value.Reveal(), id, cancellationToken).ConfigureAwait(false);
        if (required.Count == 0)
        {
            return [];
        }

        // Keyless details for display and for each child's own description ids; keep Steam's order.
        IReadOnlyList<WorkshopItemMetadata> details = await _metadata.GetItemsAsync(required, cancellationToken).ConfigureAwait(false);
        Dictionary<string, WorkshopItemMetadata> byId = details
            .GroupBy(d => d.WorkshopId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return [.. required.Select(r => byId.TryGetValue(r, out WorkshopItemMetadata? m) ? m : WorkshopItemMetadata.NotFound(r))];
    }

    private async Task<IReadOnlyList<string>> GetChildIdsAsync(string key, string workshopId, CancellationToken cancellationToken)
    {
        // The key rides in the query string (GetDetails is GET) — this string is never logged.
        string requestUri = new StringBuilder(GetDetailsPath)
            .Append("?key=").Append(Uri.EscapeDataString(key))
            .Append("&publishedfileids%5B0%5D=").Append(workshopId)
            .Append("&includechildren=true")
            .ToString();
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogHttpFailure(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? "key rejected"
                    : $"HTTP {(int)response.StatusCode}");
                return [];
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false);
            return ParseChildIds(document, workshopId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            LogHttpFailure(ex.GetType().Name);
            return [];
        }
    }

    private static List<string> ParseChildIds(JsonDocument document, string workshopId)
    {
        List<string> ids = [];
        if (!document.RootElement.TryGetProperty("response", out JsonElement response)
            || !response.TryGetProperty("publishedfiledetails", out JsonElement details)
            || details.ValueKind != JsonValueKind.Array)
        {
            return ids;
        }

        HashSet<string> seen = new(StringComparer.Ordinal) { workshopId };
        foreach (JsonElement item in details.EnumerateArray())
        {
            if (!item.TryGetProperty("children", out JsonElement children) || children.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement child in children.EnumerateArray())
            {
                if (child.TryGetProperty("publishedfileid", out JsonElement value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { } childId
                    && IsWorkshopId(childId)
                    && seen.Add(childId))
                {
                    ids.Add(childId);
                    if (ids.Count == MaxRequired)
                    {
                        return ids;
                    }
                }
            }
        }

        return ids;
    }

    private static bool IsWorkshopId(string id) =>
        id.Length is > 0 and <= MaxIdLength && id.All(char.IsAsciiDigit);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workshop required-items lookup failed: {Reason}.")]
    private partial void LogHttpFailure(string reason);
}
