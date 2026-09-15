using ZWarden.Application.Diagnostics;

namespace ZWarden.Diagnostics.Evaluators;

/// <summary>
/// Evaluates the application database's <see cref="DatabaseProbeFacts"/> into a <see cref="DiagnosticCheck"/>
/// (F29). A <b>pure</b> function — no EF Core, no I/O — so its verdict table is unit-tested. A database that does
/// not answer is a <see cref="DiagnosticStatus.Fail"/>; one that answers but has un-applied migrations is a
/// <see cref="DiagnosticStatus.Warn"/> (the schema is behind); otherwise <see cref="DiagnosticStatus.Pass"/>.
/// </summary>
public static class DatabaseDiagnostic
{
    /// <summary>Evaluates the database facts into a <see cref="DiagnosticDomain.Database"/> check.</summary>
    public static DiagnosticCheck Evaluate(DatabaseProbeFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (!facts.CanConnect)
        {
            return DiagnosticCheck.Create(
                DiagnosticDomain.Database,
                DiagnosticStatus.Fail,
                "The application database is unreachable.",
                facts.Error);
        }

        if (facts.PendingMigrations > 0)
        {
            return DiagnosticCheck.Create(
                DiagnosticDomain.Database,
                DiagnosticStatus.Warn,
                $"The database is reachable ({facts.Provider}) but the schema is behind.",
                $"{facts.PendingMigrations} pending migration(s).");
        }

        return DiagnosticCheck.Create(
            DiagnosticDomain.Database,
            DiagnosticStatus.Pass,
            $"The database is reachable and up to date ({facts.Provider}).");
    }
}
