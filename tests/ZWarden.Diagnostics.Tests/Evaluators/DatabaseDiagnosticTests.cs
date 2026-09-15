using ZWarden.Application.Diagnostics;
using ZWarden.Diagnostics.Evaluators;

namespace ZWarden.Diagnostics.Tests.Evaluators;

/// <summary>
/// F29 PR-A: the pure database evaluator. A total function of the probe facts — reachable/current is a pass,
/// reachable-but-behind is a warn, unreachable is a fail — asserted here with no EF Core.
/// </summary>
public class DatabaseDiagnosticTests
{
    [Test]
    public async Task A_reachable_up_to_date_database_passes()
    {
        DiagnosticCheck check = DatabaseDiagnostic.Evaluate(new DatabaseProbeFacts(CanConnect: true, PendingMigrations: 0, Provider: "Npgsql"));

        await Assert.That(check.Domain).IsEqualTo(DiagnosticDomain.Database);
        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Pass);
        await Assert.That(check.Summary).Contains("Npgsql");
    }

    [Test]
    public async Task A_reachable_database_with_pending_migrations_warns()
    {
        DiagnosticCheck check = DatabaseDiagnostic.Evaluate(new DatabaseProbeFacts(CanConnect: true, PendingMigrations: 3, Provider: "Sqlite"));

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Warn);
        await Assert.That(check.Detail).Contains("3");
    }

    [Test]
    public async Task An_unreachable_database_fails_and_carries_the_error()
    {
        DiagnosticCheck check = DatabaseDiagnostic.Evaluate(
            new DatabaseProbeFacts(CanConnect: false, PendingMigrations: 0, Provider: "Npgsql", Error: "connection refused"));

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Fail);
        await Assert.That(check.Detail).IsEqualTo("connection refused");
    }

    [Test]
    public async Task An_unreachable_database_fails_even_without_an_error_message()
    {
        DiagnosticCheck check = DatabaseDiagnostic.Evaluate(new DatabaseProbeFacts(CanConnect: false, PendingMigrations: 0, Provider: "Sqlite"));

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Fail);
    }
}
