using ZWarden.Agent.Health;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Agent.Diagnostics;

/// <summary>
/// Small shared helpers the F29 Agent gatherers use to build <see cref="DiagnosticCheckFact"/>s. Detail text is
/// bounded here — a probe error or a filesystem value is untrusted (trust-boundaries §8), and a gather must never
/// carry an unbounded string across the wire.
/// </summary>
internal static class DiagnosticFacts
{
    /// <summary>The maximum length of a fact's detail; longer detail is truncated.</summary>
    public const int MaxDetailLength = 512;

    /// <summary>A low-free-space fraction below which the filesystem check warns.</summary>
    public const double WarnFreeFraction = 0.15;

    /// <summary>A low-free-space fraction below which the filesystem check fails.</summary>
    public const double FailFreeFraction = 0.05;

    /// <summary>Builds a fact, bounding <paramref name="detail"/>.</summary>
    public static DiagnosticCheckFact Fact(DiagnosticDomain domain, ProbeStatus status, string summary, string? detail = null)
    {
        string? bounded = detail is { Length: > MaxDetailLength } ? detail[..MaxDetailLength] : detail;
        return new DiagnosticCheckFact(domain, status, summary, bounded);
    }

    /// <summary>Evaluates a directory's disk usage into a <see cref="DiagnosticDomain.Filesystem"/> fact. A
    /// measurement that could not be read is a Warn (unknown), not a Fail; low free space warns then fails.</summary>
    public static DiagnosticCheckFact Filesystem(string label, DiskUsage usage)
    {
        if (usage.CapacityBytes is not { } capacity || capacity <= 0 || usage.UsedBytes is not { } used)
        {
            return Fact(DiagnosticDomain.Filesystem, ProbeStatus.Warn, $"{label}: disk usage could not be measured.");
        }

        double freeFraction = Math.Clamp((capacity - used) / (double)capacity, 0, 1);
        long freeBytes = Math.Max(capacity - used, 0);
        string detail = $"{FormatBytes(freeBytes)} free of {FormatBytes(capacity)} ({freeFraction:P0}).";

        if (freeFraction < FailFreeFraction)
        {
            return Fact(DiagnosticDomain.Filesystem, ProbeStatus.Fail, $"{label}: disk space is critically low.", detail);
        }

        return freeFraction < WarnFreeFraction
            ? Fact(DiagnosticDomain.Filesystem, ProbeStatus.Warn, $"{label}: disk space is low.", detail)
            : Fact(DiagnosticDomain.Filesystem, ProbeStatus.Pass, $"{label}: disk space is healthy.", detail);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
