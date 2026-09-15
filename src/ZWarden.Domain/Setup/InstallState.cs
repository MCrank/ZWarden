using ZWarden.Domain.Ids;

namespace ZWarden.Domain.Setup;

/// <summary>
/// The installation-wide first-run setup record (F33): a single, well-known row (<see cref="DefaultId"/>)
/// that remembers whether the guided setup has been completed and which <see cref="TlsMode"/> the operator
/// declared. Like <see cref="ZWarden.Domain.Tenancy.Tenant"/> it is <b>not</b> tenant-owned — it is an
/// installation fact, evaluated by the first-run gate <i>before</i> any admin or tenant session exists — so
/// the tenant filter never applies to it and the narrow reads against it (the startup seed and the setup
/// wizard) are sanctioned unscoped reads (ADR 0016). Completion is monotonic in v1.0: once set it is never
/// cleared.
/// </summary>
public sealed class InstallState : IVersioned
{
    /// <summary>The internal identifier (<c>ist-&lt;uuid&gt;</c>). Always <see cref="DefaultId"/> in a
    /// self-hosted installation — the record is a singleton.</summary>
    public InstallStateId Id { get; init; }

    /// <summary>When the guided first-run setup was completed, or <see langword="null"/> while setup is
    /// still pending. The presence of a value is the whole gate signal.</summary>
    public DateTimeOffset? SetupCompletedAt { get; private set; }

    /// <summary>The TLS deployment mode the operator declared during setup, or <see langword="null"/> until
    /// it is confirmed. Recorded for the product and the F34 distribution; ZWarden does not terminate TLS.</summary>
    public TlsMode? TlsMode { get; private set; }

    /// <inheritdoc />
    public Guid Version { get; set; }

    /// <summary>
    /// The fixed, well-known identifier of the single install-state row. A compile-time constant so the
    /// bootstrap and the setup wizard can reference it without a prior read (the same pattern as
    /// <see cref="ZWarden.Domain.Tenancy.Tenant.DefaultId"/>).
    /// </summary>
    public static InstallStateId DefaultId { get; } = new(new Guid("01920000-0000-7000-8000-000000000002"));

    /// <summary>True once the guided setup has been completed.</summary>
    public bool IsSetupComplete => SetupCompletedAt is not null;

    /// <summary>Builds the empty install-state singleton seeded at startup (its <see cref="Version"/> is
    /// stamped by the persistence layer on insert).</summary>
    public static InstallState CreateDefault() => new() { Id = DefaultId };

    /// <summary>Records the operator's declared TLS mode. Overwrites any previously recorded mode — the last
    /// confirmation on the wizard wins.</summary>
    public void RecordTlsMode(TlsMode mode) => TlsMode = mode;

    /// <summary>Marks setup complete at <paramref name="completedAt"/>. Idempotent: once completed the first
    /// timestamp stands (completion is monotonic in v1.0).</summary>
    public void MarkSetupComplete(DateTimeOffset completedAt) => SetupCompletedAt ??= completedAt;
}
