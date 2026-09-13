using ZWarden.Agent.Docker;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Docker;

/// <summary>
/// F13 S2: canonical-label validation (PRD 25). Recognition is all-or-nothing — the whole baked set must
/// match and both ids must parse — because it is the first half of the label-and-assignment check that stands
/// in for container-level authorization (trust-boundaries.md §4).
/// </summary>
public class CanonicalContainerRecognizerTests
{
    private static readonly ServerId Server = ServerId.New();
    private static readonly AgentId Agent = AgentId.New();

    private static Dictionary<string, string> CanonicalLabelSet() => new(StringComparer.Ordinal)
    {
        [CanonicalLabels.Managed] = CanonicalLabels.ManagedValue,
        [CanonicalLabels.Runtime] = CanonicalLabels.RuntimeValue,
        [CanonicalLabels.SchemaVersion] = CanonicalLabels.SchemaVersionValue,
        [CanonicalLabels.ServerId] = Server.ToString(),
        [CanonicalLabels.AgentId] = Agent.ToString(),
    };

    [Test]
    public async Task A_fully_canonical_label_set_is_recognised_with_its_ids()
    {
        bool recognised = CanonicalContainerRecognizer
            .TryRecognize(CanonicalLabelSet(), out ServerId serverId, out AgentId agentId);

        await Assert.That(recognised).IsTrue();
        await Assert.That(serverId).IsEqualTo(Server);
        await Assert.That(agentId).IsEqualTo(Agent);
    }

    [Test]
    [Arguments(CanonicalLabels.Managed)]
    [Arguments(CanonicalLabels.Runtime)]
    [Arguments(CanonicalLabels.SchemaVersion)]
    [Arguments(CanonicalLabels.ServerId)]
    [Arguments(CanonicalLabels.AgentId)]
    public async Task A_missing_label_is_not_recognised(string missing)
    {
        Dictionary<string, string> labels = CanonicalLabelSet();
        labels.Remove(missing);

        await Assert.That(CanonicalContainerRecognizer.TryRecognize(labels, out _, out _)).IsFalse();
    }

    [Test]
    [Arguments(CanonicalLabels.Managed, "false")]
    [Arguments(CanonicalLabels.Runtime, "valheim")]
    [Arguments(CanonicalLabels.SchemaVersion, "2")]
    public async Task A_wrong_baked_value_is_not_recognised(string key, string wrongValue)
    {
        Dictionary<string, string> labels = CanonicalLabelSet();
        labels[key] = wrongValue;

        await Assert.That(CanonicalContainerRecognizer.TryRecognize(labels, out _, out _)).IsFalse();
    }

    [Test]
    [Arguments(CanonicalLabels.ServerId)]
    [Arguments(CanonicalLabels.AgentId)]
    public async Task An_unparseable_id_is_not_recognised(string idLabel)
    {
        Dictionary<string, string> labels = CanonicalLabelSet();
        labels[idLabel] = "not-a-typed-id";

        await Assert.That(CanonicalContainerRecognizer.TryRecognize(labels, out _, out _)).IsFalse();
    }

    [Test]
    public async Task A_server_id_carrying_an_agent_prefix_is_not_recognised()
    {
        // Typed ids are non-interchangeable: an agt- string is not a valid srv-.
        Dictionary<string, string> labels = CanonicalLabelSet();
        labels[CanonicalLabels.ServerId] = Agent.ToString();

        await Assert.That(CanonicalContainerRecognizer.TryRecognize(labels, out _, out _)).IsFalse();
    }

    [Test]
    public async Task Null_labels_are_not_recognised()
    {
        await Assert.That(CanonicalContainerRecognizer.TryRecognize(null, out _, out _)).IsFalse();
    }
}
