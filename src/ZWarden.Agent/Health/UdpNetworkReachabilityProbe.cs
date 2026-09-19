using System.Net;
using System.Net.Sockets;

namespace ZWarden.Agent.Health;

/// <summary>
/// The default <see cref="INetworkReachabilityProbe"/> (F16). It sends a zero-length datagram to the given
/// <c>host:port</c> and waits briefly: a closed UDP port yields an ICMP port-unreachable that the OS surfaces as a
/// <see cref="SocketException"/> (<c>ConnectionRefused</c>/<c>ConnectionReset</c>), which is the only case we
/// treat as <c>false</c>. Anything else — a send that is silently accepted, a timeout, or any other socket error
/// — is reported as <c>null</c> (unknown), because a datagram vanishing into the void does not prove a listener
/// exists. The caller chooses the address (the PZ container's ZWarden-network IP, not loopback, when the Agent is
/// containerized — #199). Best-effort by construction (ADR-noted in the F16 plan); exercised in the integration
/// tier, not offline.
/// </summary>
public sealed class UdpNetworkReachabilityProbe : INetworkReachabilityProbe
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromMilliseconds(250);

    /// <inheritdoc />
    public async Task<bool?> IsUdpPortReachableAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (port is < 1 or > 65535 || !IPAddress.TryParse(host, out IPAddress? address))
        {
            return null;
        }

        try
        {
            using UdpClient client = new();
            client.Connect(address, port);
            await client.SendAsync(ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ReceiveTimeout);
            try
            {
                // A closed port delivers an ICMP unreachable, surfacing here as a SocketException. A live listener
                // simply does not answer our empty datagram, so we time out — which we read as "unknown", not down.
                await client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }
        catch (SocketException ex) when (
            ex.SocketErrorCode is SocketError.ConnectionRefused or SocketError.ConnectionReset)
        {
            return false;
        }
        catch (SocketException)
        {
            return null;
        }
    }
}
