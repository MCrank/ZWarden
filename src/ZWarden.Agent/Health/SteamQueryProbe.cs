using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ZWarden.Agent.Health;

/// <summary>How a Steam A2S_INFO query to a game port ended (#231).</summary>
public enum SteamQueryStatus
{
    /// <summary>The server answered with its info: something is listening and serving players.</summary>
    Answered,

    /// <summary>No answer in time — not proof of anything (Steam queries off, still starting, or filtered).</summary>
    NoAnswer,

    /// <summary>The host refused the datagram (ICMP port-unreachable): nothing is listening on that port.</summary>
    Refused,
}

/// <summary>The parts of an A2S_INFO reply an operator cares about.</summary>
public sealed record SteamServerInfo(string Name, string Map, int Players, int MaxPlayers);

/// <summary>The outcome of one <see cref="ISteamQueryProbe.QueryInfoAsync"/>.</summary>
public sealed record SteamQueryResult(SteamQueryStatus Status, SteamServerInfo? Info = null);

/// <summary>
/// Asks a game server for its Steam A2S_INFO (#231). Unlike a bare UDP probe, which can only ever prove a port
/// closed, an answer here proves the Project Zomboid server is listening — B42 in Steam mode answers on its game
/// port with the challenge handshake (verified against a live 42.20.4 server). Read-only and best-effort.
/// </summary>
public interface ISteamQueryProbe
{
    Task<SteamQueryResult> QueryInfoAsync(string host, int port, CancellationToken cancellationToken);
}

/// <summary>The default <see cref="ISteamQueryProbe"/>: one info request, one challenge retry, a short timeout each.</summary>
public sealed class SteamQueryProbe : ISteamQueryProbe
{
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromMilliseconds(1500);

    /// <inheritdoc />
    public async Task<SteamQueryResult> QueryInfoAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (port is < 1 or > 65535 || !IPAddress.TryParse(host, out IPAddress? address))
        {
            return new SteamQueryResult(SteamQueryStatus.NoAnswer);
        }

        try
        {
            using UdpClient client = new(address.AddressFamily);
            client.Connect(address, port);

            byte[] challenge = [];
            // Current servers answer the first request with a challenge token that must be echoed back; one retry
            // with it is the whole protocol. A server that skips the challenge answers the first request directly.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                await client.SendAsync(SteamQueryCodec.InfoRequest(challenge), cancellationToken).ConfigureAwait(false);
                byte[]? reply = await ReceiveAsync(client, cancellationToken).ConfigureAwait(false);
                if (reply is null)
                {
                    return new SteamQueryResult(SteamQueryStatus.NoAnswer);
                }

                if (SteamQueryCodec.TryParseInfo(reply, out SteamServerInfo? info))
                {
                    return new SteamQueryResult(SteamQueryStatus.Answered, info);
                }

                if (!SteamQueryCodec.TryReadChallenge(reply, out challenge))
                {
                    break;
                }
            }

            return new SteamQueryResult(SteamQueryStatus.NoAnswer);
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionRefused or SocketError.ConnectionReset)
        {
            return new SteamQueryResult(SteamQueryStatus.Refused);
        }
        catch (SocketException)
        {
            return new SteamQueryResult(SteamQueryStatus.NoAnswer);
        }
    }

    private static async Task<byte[]?> ReceiveAsync(UdpClient client, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReplyTimeout);
        try
        {
            UdpReceiveResult result = await client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
            return result.Buffer;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}

/// <summary>
/// The Steam A2S_INFO wire format (#231): the request, the challenge reply (<c>0x41</c>) and the info reply
/// (<c>0x49</c>). The reply comes off the network, so parsing is bounds-checked and never throws, and the strings
/// are stripped of control characters and capped before they reach an operator's screen.
/// </summary>
public static class SteamQueryCodec
{
    private const byte InfoRequestType = 0x54;
    private const byte ChallengeType = 0x41;
    private const byte InfoType = 0x49;
    private const int MaxStringLength = 64;

    private static readonly byte[] Query = Encoding.ASCII.GetBytes("Source Engine Query\0");

    /// <summary>The info request, with the server's challenge token appended once one was issued.</summary>
    public static byte[] InfoRequest(ReadOnlySpan<byte> challenge)
    {
        byte[] request = new byte[5 + Query.Length + challenge.Length];
        request.AsSpan(0, 4).Fill(0xFF);
        request[4] = InfoRequestType;
        Query.CopyTo(request, 5);
        challenge.CopyTo(request.AsSpan(5 + Query.Length));
        return request;
    }

    /// <summary>Reads the four-byte token from a challenge reply.</summary>
    public static bool TryReadChallenge(ReadOnlySpan<byte> reply, out byte[] token)
    {
        if (reply.Length >= 9 && IsSimpleHeader(reply) && reply[4] == ChallengeType)
        {
            token = reply.Slice(5, 4).ToArray();
            return true;
        }

        token = [];
        return false;
    }

    /// <summary>Parses an info reply: name, map, folder and game strings, then app id, players and max players.</summary>
    public static bool TryParseInfo(ReadOnlySpan<byte> reply, out SteamServerInfo? info)
    {
        info = null;
        if (reply.Length < 6 || !IsSimpleHeader(reply) || reply[4] != InfoType)
        {
            return false;
        }

        int offset = 6; // header, type, protocol version
        if (!TryReadString(reply, ref offset, out string name)
            || !TryReadString(reply, ref offset, out string map)
            || !TryReadString(reply, ref offset, out _)
            || !TryReadString(reply, ref offset, out _)
            || offset + 4 > reply.Length)
        {
            return false;
        }

        offset += 2; // app id
        info = new SteamServerInfo(name, map, reply[offset], reply[offset + 1]);
        return true;
    }

    private static bool IsSimpleHeader(ReadOnlySpan<byte> reply) =>
        reply[0] == 0xFF && reply[1] == 0xFF && reply[2] == 0xFF && reply[3] == 0xFF;

    private static bool TryReadString(ReadOnlySpan<byte> reply, ref int offset, out string value)
    {
        int end = reply[offset..].IndexOf((byte)0);
        if (end < 0)
        {
            value = string.Empty;
            return false;
        }

        string raw = Encoding.UTF8.GetString(reply.Slice(offset, end));
        offset += end + 1;
        string clean = new([.. raw.Where(c => !char.IsControl(c))]);
        value = clean.Length <= MaxStringLength ? clean : clean[..MaxStringLength];
        return true;
    }
}
