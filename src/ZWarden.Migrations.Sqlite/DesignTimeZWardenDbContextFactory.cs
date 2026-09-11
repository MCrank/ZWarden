using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ZWarden.Application.Tenancy;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Migrations.Sqlite;

/// <summary>
/// Builds a <see cref="ZWardenDbContext"/> for the EF design-time tools (<c>dotnet ef migrations</c>)
/// on SQLite. The connection string is a throwaway design-time value; only the provider and the
/// <c>MigrationsAssembly</c> (set by <c>UseZWardenProvider</c>) matter for scaffolding.
/// </summary>
public sealed class DesignTimeZWardenDbContextFactory : IDesignTimeDbContextFactory<ZWardenDbContext>
{
    public ZWardenDbContext CreateDbContext(string[] args)
    {
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, "Data Source=zwarden-design.db")
            .Options;
        return new ZWardenDbContext(options, new SingleTenantContext());
    }
}
