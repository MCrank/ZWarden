namespace ZWarden.Diagnostics.SupportPackage;

/// <summary>
/// One packaged document's entry in the manifest (F30, PRD 51): its name, its lowercase-hex SHA-256, and its byte
/// length. The SHA-256 lets a recipient verify the document was not altered after packaging (the F24/ADR 0028
/// checksum posture, applied per entry).
/// </summary>
/// <param name="Name">The document's name inside the package (e.g. <c>diagnostics.json</c>).</param>
/// <param name="Sha256">The lowercase-hex SHA-256 over the document bytes.</param>
/// <param name="Bytes">The document's byte length.</param>
public sealed record SupportPackageManifestEntry(string Name, string Sha256, int Bytes);

/// <summary>
/// The secret-scan attestation stamped into the manifest (F30 D-3): that the scan ran and that it was clean.
/// A package only ever exists when <see cref="Clean"/> is true — a detection aborts generation — so this records,
/// for the recipient, that the fail-closed gate was applied.
/// </summary>
/// <param name="Ran">Whether the secret scan ran (always true for a produced package).</param>
/// <param name="Clean">Whether the scan found nothing (always true for a produced package).</param>
public sealed record SecretScanAttestation(bool Ran, bool Clean);

/// <summary>
/// The support package's index (F30, PRD 51): the <see cref="DiagnosticId"/> correlation id, the schema version,
/// when it was generated, the environment facts, the scope, the report's worst status, a per-entry checksum list,
/// and the secret-scan attestation. It carries statuses, sizes, and hashes only — never a credential or the
/// untrusted contents.
/// </summary>
/// <param name="DiagnosticId">The <c>diag-</c> correlation id (also in the audit event; PRD 49). Not an entity key.</param>
/// <param name="SchemaVersion">The manifest schema version.</param>
/// <param name="GeneratedAtUtc">When the package was generated.</param>
/// <param name="Environment">The (sanitized) environment facts.</param>
/// <param name="Scope">The package scope (<c>tenant</c> or <c>server:&lt;pseudonym&gt;</c>).</param>
/// <param name="WorstStatus">The report's headline verdict.</param>
/// <param name="Entries">One checksum entry per packaged document.</param>
/// <param name="SecretScan">The fail-closed secret-scan attestation.</param>
public sealed record SupportPackageManifest(
    string DiagnosticId,
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    EnvironmentFacts Environment,
    string Scope,
    string WorstStatus,
    IReadOnlyList<SupportPackageManifestEntry> Entries,
    SecretScanAttestation SecretScan)
{
    /// <summary>The current manifest schema version.</summary>
    public const int CurrentSchemaVersion = 1;
}
