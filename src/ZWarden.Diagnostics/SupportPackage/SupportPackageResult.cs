namespace ZWarden.Diagnostics.SupportPackage;

/// <summary>Why a support package was not produced (F30). The pipeline is fail-closed: a detected secret or invalid
/// content aborts generation and nothing is emitted (PRD 51). <see cref="NotAuthorized"/> is raised earlier, by the
/// Web-side collector, before any content is gathered.</summary>
public enum SupportPackageFailure
{
    /// <summary>The caller lacks the tenant-wide <c>Diagnostics.Export</c> permission (collector-side).</summary>
    NotAuthorized,

    /// <summary>The secret scanner found prohibited material; generation was aborted rather than emit it (D-3).</summary>
    SecretDetected,

    /// <summary>The assembled content failed validation (empty or malformed) and was not packaged.</summary>
    InvalidContent,
}

/// <summary>A produced support package (F30): its manifest and its documents, ready for the Web-side writer to ZIP
/// and stream. The documents are in a stable, deterministic order.</summary>
/// <param name="Manifest">The package index.</param>
/// <param name="Documents">The packaged documents (each with its own SHA-256).</param>
public sealed record BuiltSupportPackage(
    SupportPackageManifest Manifest,
    IReadOnlyList<SupportPackageDocument> Documents);

/// <summary>
/// The outcome of building a support package (F30): on success the <see cref="BuiltSupportPackage"/>; otherwise a
/// typed <see cref="SupportPackageFailure"/>. On a <see cref="SupportPackageFailure.SecretDetected"/> the
/// <see cref="Detection"/> names the detector and offset (never the value) for the audit trail; the package is
/// null and nothing is emitted.
/// </summary>
public sealed record SupportPackageResult(
    bool Succeeded,
    BuiltSupportPackage? Package,
    SupportPackageFailure? Failure,
    SecretDetection? Detection = null)
{
    /// <summary>A successful build.</summary>
    public static SupportPackageResult Success(BuiltSupportPackage package) => new(true, package, null);

    /// <summary>A fail-closed secret detection: no package, the detector recorded for audit.</summary>
    public static SupportPackageResult SecretDetected(SecretDetection detection) =>
        new(false, null, SupportPackageFailure.SecretDetected, detection);

    /// <summary>A typed failure with no package.</summary>
    public static SupportPackageResult Denied(SupportPackageFailure failure) => new(false, null, failure);
}
