using System.Globalization;
using ZWarden.Application.Servers;
using ZWarden.Domain.Servers;

namespace ZWarden.Web.Components.Servers;

/// <summary>
/// The new-server wizard's parsing and wording (#230), shared by the /servers form and the Server Detail recreate form
/// so they agree. The heap is typed in GiB (0.5 steps read naturally); blank uses the suggestion for the expected player
/// count (D2, <see cref="ServerMemoryRules.SuggestHeap"/>), or the Agent's default when that is blank too. The capacity
/// wording is what an operator reads before acknowledging an overcommit (D1).
/// </summary>
public static class NewServerForm
{
    /// <summary>Resolves the heap from the typed GiB, else the expected players. Returns <c>false</c> with an
    /// operator-facing <paramref name="error"/> when either is unreadable or out of range; <c>true</c> with
    /// <paramref name="heapBytes"/> <c>null</c> when both are blank (the Agent's default).</summary>
    public static bool TryResolveHeap(string? heapGiB, string? expectedPlayers, out long? heapBytes, out string? error)
    {
        heapBytes = null;
        error = null;
        if (!string.IsNullOrWhiteSpace(heapGiB))
        {
            if (!decimal.TryParse(heapGiB.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal gib))
            {
                error = "The heap must be a number of GiB, e.g. 6 or 6.5.";
                return false;
            }

            long bytes = (long)Math.Round(gib * 1024m, MidpointRounding.AwayFromZero) * ServerMemoryRules.MiB;
            error = ServerMemoryRules.ValidateHeap(bytes);
            if (error is not null)
            {
                return false;
            }

            heapBytes = bytes;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(expectedPlayers))
        {
            if (!int.TryParse(expectedPlayers.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int players)
                || players > InitialSettingsRules.MaxMaxPlayers)
            {
                error = $"Expected players must be a whole number from 0 to {InitialSettingsRules.MaxMaxPlayers}.";
                return false;
            }

            heapBytes = ServerMemoryRules.SuggestHeap(players);
        }

        return true;
    }

    /// <summary>Parses the optional player cap. Blank ⇒ <c>null</c> (PZ's default).</summary>
    public static bool TryParseMaxPlayers(string? text, out int? maxPlayers, out string? error)
    {
        maxPlayers = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            error = "Max players must be a whole number.";
            return false;
        }

        error = InitialSettingsRules.ValidateMaxPlayers(value);
        if (error is not null)
        {
            return false;
        }

        maxPlayers = value;
        return true;
    }

    /// <summary>A byte size as GiB with at most one decimal (e.g. <c>6.5 GiB</c>).</summary>
    public static string FormatGiB(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(bytes / (double)ServerMemoryRules.GiB, 1):0.#} GiB");

    /// <summary>The host's free memory for new servers, or a note that the Agent has not reported it yet.</summary>
    public static string CapacityLine(HostCapacity? capacity) => capacity is null
        ? "Host memory not reported yet — the Agent reports it every few seconds once connected."
        : $"{FormatGiB(capacity.FreeBytes)} free for new servers of {FormatGiB(capacity.TotalBytes)} "
            + $"(keeping {FormatGiB(capacity.ReserveBytes)} for the host).";

    /// <summary>The warning shown when a server with <paramref name="heapBytes"/> would not fit the host's free memory.</summary>
    public static string ShortfallWarning(HostCapacity capacity, long heapBytes)
    {
        ArgumentNullException.ThrowIfNull(capacity);
        return $"This server's memory limit is {FormatGiB(capacity.LimitFor(heapBytes))} (heap + "
            + $"{FormatGiB(capacity.OverheadBytes)} overhead), but the host has only {FormatGiB(capacity.FreeBytes)} free — "
            + $"{FormatGiB(capacity.ShortfallFor(heapBytes))} short. {OvercommitImpact} Confirm below to create it anyway.";
    }

    /// <summary>The memory free for an existing server's heap change: the host's free memory plus what its container
    /// already has (from <paramref name="currentHeapBytes"/>, or the Agent's default heap when unknown).</summary>
    public static string ResizeCapacityLine(HostCapacity capacity, long? currentHeapBytes)
    {
        ArgumentNullException.ThrowIfNull(capacity);
        return $"{FormatGiB(capacity.FreeForResize(currentHeapBytes))} free on this host for this server, counting the "
            + $"{FormatGiB(capacity.LimitFor(currentHeapBytes ?? capacity.DefaultHeapBytes))} its container already has.";
    }

    /// <summary>The warning shown when raising a server's heap to <paramref name="newHeapBytes"/> would not fit.</summary>
    public static string ResizeShortfallWarning(HostCapacity capacity, long? currentHeapBytes, long newHeapBytes)
    {
        ArgumentNullException.ThrowIfNull(capacity);
        return $"A {FormatGiB(newHeapBytes)} heap puts this server's memory limit at {FormatGiB(capacity.LimitFor(newHeapBytes))} "
            + $"(heap + {FormatGiB(capacity.OverheadBytes)} overhead), but the host has only "
            + $"{FormatGiB(capacity.FreeForResize(currentHeapBytes))} free for it — "
            + $"{FormatGiB(capacity.ResizeShortfall(currentHeapBytes, newHeapBytes))} short. {OvercommitImpact} "
            + "Confirm below to recreate it anyway.";
    }

    // What overcommitting means in practice — the operator reads this before acknowledging (D1).
    private const string OvercommitImpact =
        "Docker limits are ceilings, not reservations, so this can work if the servers are rarely busy at once. If they "
        + "are, the host runs out of memory and the kernel kills a server mid-game, which can lose unsaved progress.";
}
