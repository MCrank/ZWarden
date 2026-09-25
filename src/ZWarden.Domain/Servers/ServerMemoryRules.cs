namespace ZWarden.Domain.Servers;

/// <summary>
/// The rules for a Server's JVM heap (#230), shared by the Web edge (form feedback, the wizard's suggestion) and the
/// Agent (defence in depth, where the container is built). The heap is the only memory the operator chooses; the
/// container's limit is the heap plus the Agent's configured overhead (#198), so a valid heap always leaves non-heap
/// headroom. The heap is injected as <c>-Xms/-Xmx</c> in whole MiB, so it must be a whole number of MiB.
/// </summary>
public static class ServerMemoryRules
{
    /// <summary>One GiB in bytes.</summary>
    public const long GiB = 1024L * 1024 * 1024;

    /// <summary>One MiB in bytes — the heap's granularity (the JVM flag is written in <c>m</c>).</summary>
    public const long MiB = 1024L * 1024;

    /// <summary>The smallest heap accepted. Below this a B42 dedicated server does not reliably load a map.</summary>
    public const long MinHeapSizeBytes = 2 * GiB;

    /// <summary>The largest heap accepted — a sanity bound against a typo, not a sizing recommendation.</summary>
    public const long MaxHeapSizeBytes = 128 * GiB;

    /// <summary>The heap the suggestion starts from, before any players.</summary>
    public const long SuggestionBaseBytes = 4 * GiB;

    /// <summary>The heap the suggestion adds per expected concurrent player.</summary>
    public const long SuggestionPerPlayerBytes = GiB / 4;

    /// <summary>The suggestion is rounded up to this step so the numbers read cleanly.</summary>
    public const long SuggestionStepBytes = GiB / 2;

    /// <summary>
    /// Validates a requested heap. Returns <c>null</c> when acceptable, or a short operator-facing reason why not.
    /// </summary>
    public static string? ValidateHeap(long heapSizeBytes)
    {
        if (heapSizeBytes is < MinHeapSizeBytes or > MaxHeapSizeBytes)
        {
            return $"The heap must be between {MinHeapSizeBytes / GiB} GiB and {MaxHeapSizeBytes / GiB} GiB.";
        }

        return heapSizeBytes % MiB != 0 ? "The heap must be a whole number of MiB." : null;
    }

    /// <summary>
    /// The suggested heap for an expected number of concurrent players (D2): 4 GiB plus 0.25 GiB per player, rounded
    /// up to 0.5 GiB (8 players → 6 GiB, 16 → 8 GiB, 32 → 12 GiB). A starting point only — mods, zombie density and
    /// loaded chunks matter more than headcount. The player count is clamped to 0–254 (PZ's <c>MaxPlayers</c> range).
    /// </summary>
    public static long SuggestHeap(int expectedPlayers)
    {
        long raw = SuggestionBaseBytes + (Math.Clamp(expectedPlayers, 0, 254) * SuggestionPerPlayerBytes);
        long rounded = (raw + SuggestionStepBytes - 1) / SuggestionStepBytes * SuggestionStepBytes;
        return Math.Min(rounded, MaxHeapSizeBytes);
    }
}
