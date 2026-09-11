using ZWarden.Domain.Ids;

namespace ZWarden.Domain.Tests.Ids;

/// <summary>The typed-ID contract (PRD 6/7/8, ADR 0014), exercised through AgentId.</summary>
public class TypedIdTests
{
    [Test]
    public async Task New_generates_a_non_empty_version_7_id()
    {
        AgentId id = AgentId.New();
        await Assert.That(id.IsEmpty).IsFalse();
        await Assert.That(id.Value.Version).IsEqualTo(7);
    }

    [Test]
    public async Task ToString_is_the_lowercase_canonical_form()
    {
        Guid g = Guid.CreateVersion7();
        AgentId id = AgentId.FromGuid(g);
        await Assert.That(id.ToString()).IsEqualTo($"agt-{g:D}");
        await Assert.That(id.ToString()).IsEqualTo(id.ToString().ToLowerInvariant());
    }

    [Test]
    public async Task Parse_round_trips_ToString()
    {
        AgentId id = AgentId.New();
        await Assert.That(AgentId.Parse(id.ToString())).IsEqualTo(id);
    }

    [Test]
    public async Task Parse_is_case_insensitive_on_the_uuid_and_normalises_to_lowercase()
    {
        Guid g = Guid.CreateVersion7();
        string upper = $"agt-{g.ToString("D").ToUpperInvariant()}";
        AgentId id = AgentId.Parse(upper);
        await Assert.That(id.ToString()).IsEqualTo($"agt-{g:D}");
    }

    [Test]
    public async Task Parse_rejects_the_wrong_prefix()
    {
        string wrong = $"usr-{Guid.CreateVersion7():D}";
        await Assert.That(() => AgentId.Parse(wrong)).Throws<FormatException>();
    }

    [Test]
    public async Task Parse_rejects_a_malformed_uuid()
    {
        await Assert.That(() => AgentId.Parse("agt-not-a-uuid")).Throws<FormatException>();
    }

    [Test]
    public async Task TryParse_returns_false_without_throwing()
    {
        await Assert.That(AgentId.TryParse("nope", out _)).IsFalse();
        await Assert.That(AgentId.TryParse(null, out _)).IsFalse();
    }

    [Test]
    public async Task Default_id_is_empty()
    {
        await Assert.That(default(AgentId).IsEmpty).IsTrue();
    }
}
