using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Tenancy;

/// <summary>
/// #297 PR-B (ADR 0046 Q7): each action from an interactive page runs in its own scope, with the circuit's tenant,
/// over the production model and the session tenant context. The scoped context is built from the scoped
/// <c>IDbContextFactory</c>, so both see the same tenant. Offline tier.
/// </summary>
public class ActionScopeRunnerTests
{
    [Test]
    public async Task An_action_from_a_circuit_sees_only_the_circuits_tenant()
    {
        await WithHostAsync(async root =>
        {
            TenantId a = TenantId.New(), b = TenantId.New();
            await SeedServerAsync(root, a, "a-1");
            await SeedServerAsync(root, a, "a-2");
            await SeedServerAsync(root, b, "b-1");

            await using AsyncServiceScope circuit = root.CreateTenantScope(a);
            circuit.ServiceProvider.GetRequiredService<TenantAssignment>().MarkCircuit();
            ActionScopeRunner actions = circuit.ServiceProvider.GetRequiredService<ActionScopeRunner>();

            List<string> names = await actions.RunAsync<ZWardenDbContext, List<string>>(
                (db, ct) => db.Set<Server>().Select(s => s.Name).OrderBy(n => n).ToListAsync(ct));

            await Assert.That(names).IsEquivalentTo(["a-1", "a-2"]);
        });
    }

    [Test]
    public async Task An_action_from_a_circuit_without_a_tenant_throws()
    {
        await WithHostAsync(async root =>
        {
            await using AsyncServiceScope circuit = root.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            circuit.ServiceProvider.GetRequiredService<TenantAssignment>().MarkCircuit();
            ActionScopeRunner actions = circuit.ServiceProvider.GetRequiredService<ActionScopeRunner>();

            await Assert.That(async () => await actions.RunAsync<ZWardenDbContext, int>(
                (db, ct) => db.Set<Server>().CountAsync(ct))).Throws<InvalidOperationException>();
        });
    }

    [Test]
    public async Task The_factory_and_the_scoped_context_bind_the_same_scope_tenant()
    {
        await WithHostAsync(async root =>
        {
            TenantId a = TenantId.New(), b = TenantId.New();
            await SeedServerAsync(root, a, "a-1");
            await SeedServerAsync(root, b, "b-1");

            await using AsyncServiceScope scope = root.CreateTenantScope(b);
            await using ZWardenDbContext fromFactory = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<ZWardenDbContext>>().CreateDbContextAsync();
            ZWardenDbContext scoped = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();

            await Assert.That(ReferenceEquals(fromFactory, scoped)).IsFalse();
            await Assert.That(await fromFactory.Set<Server>().Select(s => s.Name).SingleAsync()).IsEqualTo("b-1");
            await Assert.That(await scoped.Set<Server>().Select(s => s.Name).SingleAsync()).IsEqualTo("b-1");
        });
    }

    private static async Task SeedServerAsync(IServiceProvider root, TenantId tenant, string name)
    {
        await using AsyncServiceScope scope = root.CreateTenantScope(tenant);
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        db.Add(Server.Import(AgentId.New(), ServerId.New(), name, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private static async Task WithHostAsync(Func<ServiceProvider, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        ServiceCollection services = new();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSessionTenantContext();
        services.AddTenantFoundation();
        services.AddZWardenPersistence(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False");

        await using ServiceProvider root = services.BuildServiceProvider();
        try
        {
            await root.MigrateAndBootstrapDefaultTenantAsync();
            await body(root);
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }
}
