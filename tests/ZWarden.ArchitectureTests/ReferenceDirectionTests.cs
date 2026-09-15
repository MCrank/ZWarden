namespace ZWarden.ArchitectureTests;

/// <summary>
/// The reference-direction assertions from docs/trust-boundaries.md §9, each written
/// so it fails the build when the boundary is crossed. These are the rules an empty
/// skeleton already satisfies. The type-level rules landed with the features that added
/// the types: rule 3 (no free-form command contract) in <see cref="ClosedCommandVocabularyTests"/>
/// at the F40 gate, rule 4 (the tenant filter) in <c>TenantFilterGuardTests</c>, and rule 7
/// (the RCON-password type is unreachable from Web) below.
/// </summary>
public class ReferenceDirectionTests
{
    private static bool IsPersistence(string package) =>
        package.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
        || package.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase)
        || package.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
        || package.StartsWith("Dapper", StringComparison.OrdinalIgnoreCase);

    private static bool IsDockerClient(string package) =>
        package.Contains("Docker.DotNet", StringComparison.OrdinalIgnoreCase);

    private static bool IsSignalRClient(string package) =>
        package.Contains("SignalR", StringComparison.OrdinalIgnoreCase);

    private static bool IsExternalIdentityProvider(string package) =>
        package.Contains("Auth0", StringComparison.OrdinalIgnoreCase)
        || package.Contains("Okta", StringComparison.OrdinalIgnoreCase);

    private static bool IsAspire(string package) =>
        package.StartsWith("Aspire.", StringComparison.OrdinalIgnoreCase);

    // §9 rule 1: a compromised Agent must not be able to reach the database, so it
    // references neither Infrastructure, EF Core, nor a provider.
    [Test]
    public async Task Agent_does_not_reference_persistence()
    {
        var agent = ProjectGraph.ForSourceProject("ZWarden.Agent");

        await Assert.That(agent.ProjectReferences).DoesNotContain("ZWarden.Infrastructure");
        await Assert.That(agent.PackageReferences.Any(IsPersistence)).IsFalse();
    }

    // F10 gave the Agent the SignalR *client* for its outbound control-plane connection. F13 now adds the
    // Docker client too: the Agent owns the host's Docker boundary (§9 rule 5), so this is the one project
    // permitted to reference it. The persistence guard above still holds — SignalR and Docker are transport,
    // not persistence — and Web still must not reference a Docker client (asserted below).
    [Test]
    public async Task Agent_references_the_signalr_and_docker_clients()
    {
        var agent = ProjectGraph.ForSourceProject("ZWarden.Agent");

        await Assert.That(agent.PackageReferences.Any(IsSignalRClient)).IsTrue();
        await Assert.That(agent.PackageReferences.Any(IsDockerClient)).IsTrue();
    }

    // §9 rule 2: the domain model depends on no infrastructure framework - the cleanest
    // form of which, for the skeleton, is that Domain declares no dependencies at all.
    [Test]
    public async Task Domain_references_no_infrastructure()
    {
        var domain = ProjectGraph.ForSourceProject("ZWarden.Domain");

        await Assert.That(domain.ProjectReferences).IsEmpty();
        await Assert.That(domain.PackageReferences).IsEmpty();
        await Assert.That(domain.FrameworkReferences).IsEmpty();
    }

    // §9 rule 5: nothing in Web talks to Docker directly - the Agent owns that boundary.
    [Test]
    public async Task Web_does_not_reference_a_docker_client()
    {
        var web = ProjectGraph.ForSourceProject("ZWarden.Web");

        await Assert.That(web.PackageReferences.Any(IsDockerClient)).IsFalse();
    }

    // §9 rule 7: the RCON password type lives only in ZWarden.Rcon, an Agent-only assembly (F18). Web must
    // never reference it - anywhere in its transitive closure - so "the browser never receives RCON credential
    // material" holds by construction: Web cannot leak a credential type it cannot even see.
    [Test]
    public async Task Web_does_not_reference_the_rcon_client()
    {
        HashSet<string> closure = TransitiveProjectClosure("ZWarden.Web");

        await Assert.That(closure).DoesNotContain("ZWarden.Rcon");
    }

    // Walks the project-reference graph from a root src project, returning every project it can reach.
    private static HashSet<string> TransitiveProjectClosure(string projectName)
    {
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        Stack<string> pending = new();
        pending.Push(projectName);
        while (pending.Count > 0)
        {
            foreach (string reference in ProjectGraph.ForSourceProject(pending.Pop()).ProjectReferences)
            {
                if (visited.Add(reference))
                {
                    pending.Push(reference);
                }
            }
        }

        return visited;
    }

    // F20a (ADR 0010): the Lua config parser lives behind IPzConfigDocument in ZWarden.PzConfig, which
    // depends only on the domain - it is infrastructure-free (no persistence, no Docker, no transport), so
    // the seam can be consumed by any tier and the hand-rolled fallback stays a real option.
    [Test]
    public async Task PzConfig_references_only_the_domain()
    {
        var pzConfig = ProjectGraph.ForSourceProject("ZWarden.PzConfig");

        await Assert.That(pzConfig.ProjectReferences.Count).IsEqualTo(1);
        await Assert.That(pzConfig.ProjectReferences).Contains("ZWarden.Domain");
        await Assert.That(pzConfig.PackageReferences.Any(IsPersistence)).IsFalse();
        await Assert.That(pzConfig.PackageReferences.Any(IsDockerClient)).IsFalse();
        await Assert.That(pzConfig.PackageReferences.Any(IsSignalRClient)).IsFalse();
    }

    // F20a (ADR 0010): Loretta is a single-maintainer dependency, confined to ZWarden.PzConfig behind the
    // seam so a fork or the hand-rolled fallback can replace it in one place. No other src project may take
    // a direct dependency on it.
    [Test]
    public async Task Only_pzconfig_references_the_lua_parser()
    {
        string[] withLoretta = [.. ProjectGraph.SourceProjectNames()
            .Where(name => ProjectGraph.ForSourceProject(name).PackageReferences
                .Any(package => package.Contains("Loretta", StringComparison.OrdinalIgnoreCase)))];

        await Assert.That(withLoretta.Length).IsEqualTo(1);
        await Assert.That(withLoretta[0]).IsEqualTo("ZWarden.PzConfig");
    }

    // #123 / ADR 0031: Aspire is dev/test orchestration ONLY. No Aspire package may enter a production
    // src/ project — the AppHost is the single exception (it IS the dev/test orchestrator). This keeps the
    // dev/test/prod boundary honest: an Aspire dependency accidentally added to Web, the Agent, or any
    // other production project fails the build here.
    [Test]
    public async Task Only_the_apphost_references_aspire()
    {
        string[] withAspire = [.. ProjectGraph.SourceProjectNames()
            .Where(name => ProjectGraph.ForSourceProject(name).PackageReferences.Any(IsAspire))];

        await Assert.That(withAspire.Length).IsEqualTo(1);
        await Assert.That(withAspire[0]).IsEqualTo("ZWarden.AppHost");
    }

    // §9 rule 6: Auth0/IdP specifics never leak into Domain or Application (PRD 11).
    [Test]
    [Arguments("ZWarden.Domain")]
    [Arguments("ZWarden.Application")]
    public async Task Domain_and_application_do_not_reference_an_external_identity_provider(string project)
    {
        var graph = ProjectGraph.ForSourceProject(project);

        await Assert.That(graph.PackageReferences.Any(IsExternalIdentityProvider)).IsFalse();
    }
}
