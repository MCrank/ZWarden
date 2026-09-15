namespace ZWarden.Diagnostics.SupportPackage;

/// <summary>
/// Sequences the support-package pipeline (F30, PRD 51): Sanitize → Redact → Pseudonymize → <b>Secret-scan
/// (fail-closed)</b> → Validate → assemble. It takes the collected <see cref="SupportPackageContent"/> and returns
/// either a <see cref="BuiltSupportPackage"/> (documents + manifest, ready for the Web-side ZIP writer) or a typed
/// failure. It is <b>pure</b> — no filesystem, no ZIP, no HTTP — so the security-critical transform-and-gate logic
/// is exhaustively unit-testable and cannot regress behind I/O (F30 D-1). Authorization happens earlier, in the
/// Web-side collector.
/// </summary>
public interface ISupportPackageBuilder
{
    /// <summary>Runs the pipeline over <paramref name="content"/>. Returns <see cref="SupportPackageResult.Success"/>
    /// with the built package, or a fail-closed failure — <see cref="SupportPackageFailure.SecretDetected"/> when
    /// the scanner finds prohibited material (nothing is emitted; the detection is recorded for audit), or
    /// <see cref="SupportPackageFailure.InvalidContent"/> when the assembled package fails validation.</summary>
    SupportPackageResult Build(SupportPackageContent content);
}
