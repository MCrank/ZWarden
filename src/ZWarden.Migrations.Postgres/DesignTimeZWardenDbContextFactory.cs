using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ZWarden.Application.Tenancy;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Migrations.Postgres;

/// <summary>
/// Builds a <see cref="ZWardenDbContext"/> for the EF design-time tools on PostgreSQL. The connection
/// string is a throwaway design-time value; only the provider and the <c>MigrationsAssembly</c> matter
/// for scaffolding.
/// </summary>
public sealed class DesignTimeZWardenDbContextFactory : IDesignTimeDbContextFactory<ZWardenDbContext>
{
    public ZWardenDbContext CreateDbContext(string[] args)
    {
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Postgres, "Host=localhost;Database=zwarden-design;Username=postgres")
            .Options;
        return new ZWardenDbContext(options, new SingleTenantContext());
    }
}
