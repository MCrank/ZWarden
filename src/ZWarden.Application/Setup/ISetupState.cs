using ZWarden.Domain.Setup;

namespace ZWarden.Application.Setup;

/// <summary>
/// The read/write seam over the installation-wide first-run setup record (F33). The first-run gate reads
/// <see cref="IsSetupCompleteAsync"/> on every request until setup is done; the setup wizard records the
/// declared <see cref="TlsMode"/> and marks completion through here. Not tenant-scoped — the install-state
/// record is an installation fact evaluated before any tenant session exists (ADR 0016 sanctioned unscoped
/// read; ADR 0036).
/// </summary>
public interface ISetupState
{
    /// <summary>Whether the guided first-run setup has been completed. Cheap on the hot path once complete
    /// (memoized process-wide), so the gate can call it per request.</summary>
    Task<bool> IsSetupCompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>The TLS mode the operator declared during setup, or <see langword="null"/> if not yet
    /// confirmed.</summary>
    Task<TlsMode?> GetTlsModeAsync(CancellationToken cancellationToken = default);

    /// <summary>Records the operator's declared TLS mode on the install-state record.</summary>
    Task RecordTlsModeAsync(TlsMode mode, CancellationToken cancellationToken = default);

    /// <summary>Marks the guided setup complete (idempotent). Lifts the first-run gate for every request
    /// thereafter.</summary>
    Task MarkSetupCompleteAsync(CancellationToken cancellationToken = default);
}
