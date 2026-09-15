using ZWarden.Application.Diagnostics;
using ZWarden.Diagnostics.SupportPackage;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;

namespace ZWarden.Diagnostics.Tests.SupportPackage;

/// <summary>
/// F30 PR-B: the orchestrator. Fail-closed on <c>Diagnostics.Export</c>; it collects the F29 report, runs the pure
/// pipeline, and audits the outcome — a success with the <c>DiagnosticId</c>, or a fail-closed secret-scan abort
/// (named, never leaking the value). Uses the real builder so the pipeline actually runs.
/// </summary>
public class SupportPackageServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly EnvironmentFacts Env = new("1.0.0", "Sqlite", "Linux", ".NET 10.0");

    private static DiagnosticReport CleanReport() =>
        new([new DiagnosticCheck(DiagnosticDomain.Web, DiagnosticStatus.Pass, "Web is responding.")], Now);

    private static DiagnosticReport ReportWithSecret() =>
        new([DiagnosticCheck.Create(
            DiagnosticDomain.Mod, DiagnosticStatus.Warn, "Mod readme scanned.",
            "readme mentions key AKIAIOSFODNN7EXAMPLE")], Now);

    private static SupportPackageService Service(
        bool authorize, DiagnosticsRunResult run, CapturingAuditWriter audit)
    {
        return new SupportPackageService(
            new FakePermissionChecker(authorize),
            new FakeDiagnosticsService(run),
            new SupportPackageBuilder(new FixedTimeProvider(Now)),
            new FakeEnvironmentFactsProvider(Env),
            audit);
    }

    [Test]
    public async Task Without_export_permission_it_is_denied_and_nothing_is_collected()
    {
        FakeDiagnosticsService diagnostics = new(DiagnosticsRunResult.Success(CleanReport()));
        CapturingAuditWriter audit = new();
        SupportPackageService service = new(
            new FakePermissionChecker(allow: false), diagnostics,
            new SupportPackageBuilder(new FixedTimeProvider(Now)),
            new FakeEnvironmentFactsProvider(Env), audit);

        SupportPackageResult result = await service.CreateTenantPackageAsync(UserId.New());

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(SupportPackageFailure.NotAuthorized);
        await Assert.That(diagnostics.TenantRun).IsFalse();
        await Assert.That(audit.Entries).IsEmpty();
    }

    [Test]
    public async Task When_the_run_is_denied_it_is_not_authorized()
    {
        CapturingAuditWriter audit = new();
        SupportPackageService service = Service(
            authorize: true, DiagnosticsRunResult.Denied(DiagnosticsRunFailure.NotAuthorized), audit);

        SupportPackageResult result = await service.CreateTenantPackageAsync(UserId.New());

        await Assert.That(result.Failure).IsEqualTo(SupportPackageFailure.NotAuthorized);
    }

    [Test]
    public async Task A_clean_report_produces_a_package_and_a_succeeded_audit()
    {
        CapturingAuditWriter audit = new();
        SupportPackageService service = Service(authorize: true, DiagnosticsRunResult.Success(CleanReport()), audit);

        SupportPackageResult result = await service.CreateTenantPackageAsync(UserId.New());

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Package!.Manifest.Scope).IsEqualTo("tenant");
        await Assert.That(audit.Actions).Contains(DiagnosticsAuditActions.Export);
        await Assert.That(audit.Entries[0].Outcome).IsEqualTo(AuditOutcome.Succeeded);
        await Assert.That(audit.Entries[0].Detail!).Contains(result.Package.Manifest.DiagnosticId);
    }

    [Test]
    public async Task A_detected_secret_fails_closed_and_audits_a_failure_without_the_value()
    {
        CapturingAuditWriter audit = new();
        SupportPackageService service = Service(authorize: true, DiagnosticsRunResult.Success(ReportWithSecret()), audit);

        SupportPackageResult result = await service.CreateTenantPackageAsync(UserId.New());

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(SupportPackageFailure.SecretDetected);
        await Assert.That(audit.Entries[0].Outcome).IsEqualTo(AuditOutcome.Failed);
        await Assert.That(audit.Entries[0].Detail!).Contains("aws-access-key");
        await Assert.That(audit.Entries[0].Detail!).DoesNotContain("AKIAIOSFODNN7EXAMPLE");
    }

    [Test]
    public async Task A_server_package_uses_the_per_server_run_and_is_server_scoped()
    {
        FakeDiagnosticsService diagnostics = new(
            DiagnosticsRunResult.Denied(DiagnosticsRunFailure.NotAuthorized),
            DiagnosticsRunResult.Success(CleanReport()));
        CapturingAuditWriter audit = new();
        SupportPackageService service = new(
            new FakePermissionChecker(allow: true), diagnostics,
            new SupportPackageBuilder(new FixedTimeProvider(Now)),
            new FakeEnvironmentFactsProvider(Env), audit);

        SupportPackageResult result = await service.CreateServerPackageAsync(UserId.New(), ServerId.New(), AgentId.New());

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(diagnostics.ServerRun).IsTrue();
        await Assert.That(result.Package!.Manifest.Scope).IsEqualTo("server");
        await Assert.That(audit.Entries[0].ServerId).IsNotNull();
    }
}
