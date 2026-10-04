using ZWarden.Domain.Ids;
using ZWarden.Web.Components.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// #336: the one Host naming rule the Fleet board and the Hosts page share — the operator's Label, else the
/// Agent-reported Hostname, else a short id that stays distinct for Agents enrolled together (UUIDv7 prefixes match).
/// </summary>
public class HostNamesTests
{
    private static readonly AgentId Id = AgentId.Parse("agt-01a107c8-251b-7c5f-943d-fcf4421d8e99");

    [Test]
    public async Task The_label_wins_over_the_hostname()
        => await Assert.That(HostNames.Display(Id, "Basement box", "nsfw-01")).IsEqualTo("Basement box");

    [Test]
    public async Task The_hostname_is_used_without_a_label()
        => await Assert.That(HostNames.Display(Id, "  ", "nsfw-01")).IsEqualTo("nsfw-01");

    [Test]
    public async Task The_short_id_is_used_without_either_name()
        => await Assert.That(HostNames.Display(Id, null, "")).IsEqualTo("agt-01a107c8…d8e99");

    [Test]
    public async Task The_short_id_keeps_the_head_and_the_tail()
        => await Assert.That(HostNames.ShortId(Id)).IsEqualTo("agt-01a107c8…d8e99");
}
