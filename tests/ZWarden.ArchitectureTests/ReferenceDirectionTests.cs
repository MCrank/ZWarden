namespace ZWarden.ArchitectureTests;

/// <summary>
/// The reference-direction assertions from docs/trust-boundaries.md §9, each written
/// so it fails the build when the boundary is crossed. These are the rules an empty
/// skeleton already satisfies; §9 rules 3, 4 and 7 (free-form command contracts, the
/// tenant filter, the RCON-password type) arrive with the features that add the types.
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

    // §9 rule 1: a compromised Agent must not be able to reach the database, so it
    // references neither Infrastructure, EF Core, nor a provider.
    [Test]
    public async Task Agent_does_not_reference_persistence()
    {
        var agent = ProjectGraph.ForSourceProject("ZWarden.Agent");

        await Assert.That(agent.ProjectReferences).DoesNotContain("ZWarden.Infrastructure");
        await Assert.That(agent.PackageReferences.Any(IsPersistence)).IsFalse();
    }

    // F8 skeleton scope boundary: the Agent runtime carries no transport (SignalR, F10) and no
    // Docker client (F13) yet. This fails the build if a later feature's dependency is pulled
    // forward into the skeleton before its own feature lands.
    [Test]
    public async Task Agent_does_not_reference_transport_or_docker()
    {
        var agent = ProjectGraph.ForSourceProject("ZWarden.Agent");

        await Assert.That(agent.PackageReferences.Any(IsSignalRClient)).IsFalse();
        await Assert.That(agent.PackageReferences.Any(IsDockerClient)).IsFalse();
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
