using System.Net;
using System.Net.Sockets;
using System.Text;
using ZWarden.Agent.Health;

namespace ZWarden.Agent.Tests.Health;

/// <summary>
/// #231: a bare UDP probe can only prove a port closed, never open, so the diagnostics game-port check was Warn on
/// every healthy server. Project Zomboid (B42, Steam mode) answers the Steam A2S_INFO query on its game port with
/// the challenge handshake — verified against a live 42.20.4 server — which is a genuine "listening" signal. These
/// pin the wire codec and, over a loopback socket, the challenge round-trip and the no-answer timeout.
/// </summary>
public class SteamQueryProbeTests
{
    private static readonly byte[] Header = [0xFF, 0xFF, 0xFF, 0xFF];

    [Test]
    public async Task The_request_is_the_a2s_info_query_with_an_optional_challenge_appended()
    {
        byte[] plain = SteamQueryCodec.InfoRequest([]);
        byte[] challenged = SteamQueryCodec.InfoRequest([1, 2, 3, 4]);

        await Assert.That(plain).IsEquivalentTo(Header.Concat([(byte)0x54]).Concat(Encoding.ASCII.GetBytes("Source Engine Query\0")).ToArray());
        await Assert.That(challenged.Length).IsEqualTo(plain.Length + 4);
        await Assert.That(challenged[^4..]).IsEquivalentTo(new byte[] { 1, 2, 3, 4 });
    }

    [Test]
    public async Task A_challenge_reply_yields_its_four_byte_token()
    {
        byte[] reply = [.. Header, 0x41, 9, 8, 7, 6];

        await Assert.That(SteamQueryCodec.TryReadChallenge(reply, out byte[] token)).IsTrue();
        await Assert.That(token).IsEquivalentTo(new byte[] { 9, 8, 7, 6 });
        await Assert.That(SteamQueryCodec.TryReadChallenge(InfoReply("x", "y", 0, 1), out _)).IsFalse();
    }

    [Test]
    public async Task An_info_reply_parses_name_map_and_player_counts()
    {
        await Assert.That(SteamQueryCodec.TryParseInfo(InfoReply("ZBeta", "Muldraugh, KY", 3, 32), out SteamServerInfo? info)).IsTrue();

        await Assert.That(info!.Name).IsEqualTo("ZBeta");
        await Assert.That(info.Map).IsEqualTo("Muldraugh, KY");
        await Assert.That(info.Players).IsEqualTo(3);
        await Assert.That(info.MaxPlayers).IsEqualTo(32);
    }

    [Test]
    public async Task A_truncated_or_foreign_reply_does_not_parse()
    {
        byte[] full = InfoReply("ZBeta", "Muldraugh, KY", 3, 32);

        await Assert.That(SteamQueryCodec.TryParseInfo(full.AsSpan(0, 12), out _)).IsFalse();
        await Assert.That(SteamQueryCodec.TryParseInfo([0x00, 0x01, 0x02], out _)).IsFalse();
        await Assert.That(SteamQueryCodec.TryParseInfo([.. Header, 0x6D, 0x00], out _)).IsFalse();
    }

    [Test]
    public async Task Control_characters_in_the_server_name_are_dropped()
    {
        await Assert.That(SteamQueryCodec.TryParseInfo(InfoReply("Z\u001bBeta\n", "Map", 0, 1), out SteamServerInfo? info)).IsTrue();
        await Assert.That(info!.Name).IsEqualTo("ZBeta");
    }

    [Test]
    public async Task The_probe_completes_the_challenge_handshake_and_reports_the_server()
    {
        // A loopback stand-in for PZ: challenge first, then the info reply for a request carrying the token.
        using UdpClient server = new(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        Task serve = Task.Run(async () =>
        {
            UdpReceiveResult first = await server.ReceiveAsync();
            await server.SendAsync(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x41, 5, 6, 7, 8 }, first.RemoteEndPoint);
            UdpReceiveResult second = await server.ReceiveAsync();
            if (second.Buffer.AsSpan()[^4..].SequenceEqual(new byte[] { 5, 6, 7, 8 }))
            {
                await server.SendAsync(InfoReply("ZBeta", "Muldraugh, KY", 1, 16), second.RemoteEndPoint);
            }
        });

        SteamQueryResult result = await new SteamQueryProbe().QueryInfoAsync("127.0.0.1", port, CancellationToken.None);
        await serve;

        await Assert.That(result.Status).IsEqualTo(SteamQueryStatus.Answered);
        await Assert.That(result.Info!.Name).IsEqualTo("ZBeta");
        await Assert.That(result.Info.MaxPlayers).IsEqualTo(16);
    }

    [Test]
    public async Task A_listener_that_never_answers_is_no_answer_not_refused()
    {
        using UdpClient silent = new(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)silent.Client.LocalEndPoint!).Port;

        SteamQueryResult result = await new SteamQueryProbe().QueryInfoAsync("127.0.0.1", port, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(SteamQueryStatus.NoAnswer);
    }

    [Test]
    [Arguments("not-an-ip", 16261)]
    [Arguments("172.22.0.3", 0)]
    public async Task An_unusable_target_is_no_answer(string host, int port)
    {
        SteamQueryResult result = await new SteamQueryProbe().QueryInfoAsync(host, port, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(SteamQueryStatus.NoAnswer);
    }

    // FF FF FF FF 'I' protocol name\0 map\0 folder\0 game\0 appid(short) players maxplayers bots type env vis vac
    private static byte[] InfoReply(string name, string map, byte players, byte maxPlayers)
    {
        List<byte> bytes = [.. Header, 0x49, 0x11];
        foreach (string s in new[] { name, map, "zomboid", "Project Zomboid" })
        {
            bytes.AddRange(Encoding.UTF8.GetBytes(s));
            bytes.Add(0);
        }

        bytes.AddRange([0x00, 0x00, players, maxPlayers, 0x00, (byte)'d', (byte)'l', 0x00, 0x01]);
        return [.. bytes];
    }
}
