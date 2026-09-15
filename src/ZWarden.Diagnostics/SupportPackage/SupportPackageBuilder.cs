using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZWarden.Application.Diagnostics;
using ZWarden.Domain.Ids;

namespace ZWarden.Diagnostics.SupportPackage;

/// <summary>
/// The pure support-package pipeline (F30 — see <see cref="ISupportPackageBuilder"/>). For each collected report it
/// mints a <see cref="DiagnosticPackageId"/>, runs every untrusted string through
/// Sanitize → Redact → Pseudonymize, serializes the sanitized report to <c>diagnostics.json</c>, then applies the
/// <b>fail-closed secret gate</b> (<see cref="SecretScanner"/>) over the serialized bytes: on a detection it
/// returns <see cref="SupportPackageFailure.SecretDetected"/> and emits nothing (PRD 51, D-3). Otherwise it builds
/// the manifest (with a per-document SHA-256 and the clean-scan attestation) and validates the assembly. No I/O:
/// JSON serialization and hashing are in-memory; the ZIP and the download are the Web-side writer's job.
/// </summary>
public sealed class SupportPackageBuilder : ISupportPackageBuilder
{
    /// <summary>The single content document a package carries today.</summary>
    public const string DiagnosticsDocumentName = "diagnostics.json";

    // Relaxed escaping keeps the pseudonym tokens (<HOST-1>, …) readable in the file. Safe here: a support package
    // is a downloaded artifact, never rendered as HTML by ZWarden (trust-boundaries §8 escaping is at render, and
    // this file is never a render surface).
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly TimeProvider _time;

    /// <summary>Creates a builder. <paramref name="time"/> stamps the manifest's generation time.</summary>
    public SupportPackageBuilder(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <inheritdoc />
    public SupportPackageResult Build(SupportPackageContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        // One pseudonymizer per package so a value maps to the same token everywhere in it (D-4).
        Pseudonymizer pseudonymizer = new(content.PseudonymTargets);

        SanitizedReport sanitized = Sanitize(content, pseudonymizer);

        byte[] documentBytes = JsonSerializer.SerializeToUtf8Bytes(sanitized, Json);
        string documentText = System.Text.Encoding.UTF8.GetString(documentBytes);

        // The fail-closed gate: scan the fully-sanitized payload; a detection aborts generation (PRD 51, D-3).
        SecretDetection? detection = SecretScanner.Scan(documentText);
        if (detection is not null)
        {
            return SupportPackageResult.SecretDetected(detection);
        }

        SupportPackageDocument document = SupportPackageDocument.Create(DiagnosticsDocumentName, documentBytes);

        SupportPackageManifest manifest = new(
            DiagnosticId: DiagnosticPackageId.New().ToString(),
            SchemaVersion: SupportPackageManifest.CurrentSchemaVersion,
            GeneratedAtUtc: _time.GetUtcNow(),
            Environment: sanitized.Environment,
            Scope: sanitized.Scope,
            WorstStatus: content.Report.Worst().ToString(),
            Entries: [new SupportPackageManifestEntry(document.Name, document.Sha256, document.Content.Length)],
            SecretScan: new SecretScanAttestation(Ran: true, Clean: true));

        BuiltSupportPackage package = new(manifest, [document]);
        return PackageValidator.IsValid(package)
            ? SupportPackageResult.Success(package)
            : SupportPackageResult.Denied(SupportPackageFailure.InvalidContent);
    }

    // Runs the transform stages over every untrusted string. Detail is untrusted (§8) — full pipeline. Summary is
    // ZWarden-authored — sanitized only (defence in depth). Environment values are sanitized and pseudonymized.
    private static SanitizedReport Sanitize(SupportPackageContent content, Pseudonymizer pseudonymizer)
    {
        string Full(string? s) => pseudonymizer.Apply(SupportPackageRedactor.RedactText(SupportPackageSanitizer.Sanitize(s)));

        List<SanitizedCheck> checks = new(content.Report.Checks.Count);
        foreach (DiagnosticCheck check in content.Report.Checks)
        {
            checks.Add(new SanitizedCheck(
                check.Domain.ToString(),
                check.Status.ToString(),
                SupportPackageSanitizer.Sanitize(check.Summary),
                check.Detail is null ? null : Full(check.Detail)));
        }

        EnvironmentFacts environment = content.Environment;
        EnvironmentFacts sanitizedEnvironment = new(
            pseudonymizer.Apply(SupportPackageSanitizer.Sanitize(environment.ZWardenVersion)),
            pseudonymizer.Apply(SupportPackageSanitizer.Sanitize(environment.DatabaseProvider)),
            pseudonymizer.Apply(SupportPackageSanitizer.Sanitize(environment.OperatingSystem)),
            pseudonymizer.Apply(SupportPackageSanitizer.Sanitize(environment.RuntimeFramework)));

        return new SanitizedReport(
            SupportPackageManifest.CurrentSchemaVersion,
            SupportPackageSanitizer.Sanitize(content.Scope),
            content.Report.Worst().ToString(),
            sanitizedEnvironment,
            new SanitizedReportBody(content.Report.RanAt, checks));
    }

    // The serialized shape of diagnostics.json. Records so System.Text.Json emits stable, camelCase JSON.
    private sealed record SanitizedReport(
        int SchemaVersion,
        string Scope,
        string WorstStatus,
        EnvironmentFacts Environment,
        SanitizedReportBody Report);

    private sealed record SanitizedReportBody(DateTimeOffset RanAt, IReadOnlyList<SanitizedCheck> Checks);

    private sealed record SanitizedCheck(string Domain, string Status, string Summary, string? Detail);
}
