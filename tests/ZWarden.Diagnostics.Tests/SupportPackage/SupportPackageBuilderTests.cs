using System.Text;
using ZWarden.Application.Diagnostics;
using ZWarden.Diagnostics.SupportPackage;
using ZWarden.Domain.Ids;

namespace ZWarden.Diagnostics.Tests.SupportPackage;

/// <summary>
/// F30 PR-A: the pipeline end-to-end (PRD 51). A clean report produces a package whose manifest indexes a single,
/// checksummed <c>diagnostics.json</c>; a planted secret makes generation <b>fail closed</b> — nothing is emitted,
/// only the detector is recorded (D-3); and redaction/pseudonymization scrub the packaged content so no raw secret
/// or PII survives into the bytes.
/// </summary>
public class SupportPackageBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static SupportPackageBuilder NewBuilder() => new(new FixedTimeProvider(Now));

    private static EnvironmentFacts Env() => new("1.0.0", "Sqlite", "Linux 6.1", ".NET 10.0");

    private static SupportPackageContent Content(params DiagnosticCheck[] checks) =>
        new(new DiagnosticReport(checks, Now), Env(), "tenant", SupportPackageContent.NoTargets);

    private static string TextOf(BuiltSupportPackage package) =>
        Encoding.UTF8.GetString(package.Documents.Single().Content);

    [Test]
    public async Task A_clean_report_builds_a_valid_package()
    {
        DiagnosticCheck check = new(DiagnosticDomain.Web, DiagnosticStatus.Pass, "Web is responding.");

        SupportPackageResult result = NewBuilder().Build(Content(check));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Package!.Documents.Count).IsEqualTo(1);
        await Assert.That(result.Package.Documents[0].Name).IsEqualTo(SupportPackageBuilder.DiagnosticsDocumentName);
    }

    [Test]
    public async Task The_manifest_indexes_the_document_with_a_matching_checksum()
    {
        SupportPackageResult result = NewBuilder().Build(
            Content(new DiagnosticCheck(DiagnosticDomain.Database, DiagnosticStatus.Pass, "DB reachable.")));

        SupportPackageDocument document = result.Package!.Documents.Single();
        SupportPackageManifestEntry entry = result.Package.Manifest.Entries.Single();

        await Assert.That(entry.Name).IsEqualTo(document.Name);
        await Assert.That(entry.Sha256).IsEqualTo(document.Sha256);
        await Assert.That(entry.Bytes).IsEqualTo(document.Content.Length);
    }

    [Test]
    public async Task The_manifest_stamps_a_diag_correlation_id_and_the_pinned_time()
    {
        SupportPackageManifest manifest = NewBuilder()
            .Build(Content(new DiagnosticCheck(DiagnosticDomain.Web, DiagnosticStatus.Pass, "ok")))
            .Package!.Manifest;

        await Assert.That(manifest.GeneratedAtUtc).IsEqualTo(Now);
        await Assert.That(DiagnosticPackageId.TryParse(manifest.DiagnosticId, out _)).IsTrue();
        await Assert.That(manifest.SchemaVersion).IsEqualTo(SupportPackageManifest.CurrentSchemaVersion);
        await Assert.That(manifest.SecretScan.Ran).IsTrue();
        await Assert.That(manifest.SecretScan.Clean).IsTrue();
    }

    [Test]
    public async Task The_worst_status_headlines_the_manifest()
    {
        SupportPackageResult result = NewBuilder().Build(Content(
            new DiagnosticCheck(DiagnosticDomain.Web, DiagnosticStatus.Pass, "ok"),
            new DiagnosticCheck(DiagnosticDomain.Rcon, DiagnosticStatus.Fail, "unreachable")));

        await Assert.That(result.Package!.Manifest.WorstStatus).IsEqualTo(DiagnosticStatus.Fail.ToString());
    }

    [Test]
    public async Task A_planted_secret_in_detail_fails_closed_and_emits_nothing()
    {
        // A secret sitting in free-text detail with no key — exactly what key-based redaction cannot catch.
        DiagnosticCheck hostile = DiagnosticCheck.Create(
            DiagnosticDomain.Mod, DiagnosticStatus.Warn, "Mod readme scanned.",
            "readme mentions key AKIAIOSFODNN7EXAMPLE for the uploader");

        SupportPackageResult result = NewBuilder().Build(Content(hostile));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(SupportPackageFailure.SecretDetected);
        await Assert.That(result.Package).IsNull();
        await Assert.That(result.Detection!.DetectorName).IsEqualTo("aws-access-key");
    }

    [Test]
    public async Task A_keyed_secret_is_redacted_and_never_reaches_the_bytes()
    {
        DiagnosticCheck check = DiagnosticCheck.Create(
            DiagnosticDomain.Rcon, DiagnosticStatus.Warn, "RCON config read.", "RconPassword=hunter2secret");

        SupportPackageResult result = NewBuilder().Build(Content(check));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(TextOf(result.Package!)).DoesNotContain("hunter2secret");
    }

    [Test]
    public async Task A_private_ip_in_detail_is_pseudonymized_in_the_bytes()
    {
        DiagnosticCheck check = DiagnosticCheck.Create(
            DiagnosticDomain.Agent, DiagnosticStatus.Pass, "Agent connected.", "last seen from 192.168.1.42");

        string text = TextOf(NewBuilder().Build(Content(check)).Package!);

        await Assert.That(text).Contains("<PRIVATE-IP-1>");
        await Assert.That(text).DoesNotContain("192.168.1.42");
    }

    [Test]
    public async Task An_empty_report_still_builds_a_valid_package()
    {
        SupportPackageResult result = NewBuilder().Build(Content());

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Package!.Documents.Count).IsEqualTo(1);
    }
}
