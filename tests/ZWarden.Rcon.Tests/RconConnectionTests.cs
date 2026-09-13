using System.Diagnostics;
using System.Text;
using ZWarden.Domain.Security;
using ZWarden.Rcon.Tests.Fakes;

namespace ZWarden.Rcon.Tests;

/// <summary>
/// The RCON client against a loopback <see cref="FakeRconServer"/> that reproduces PZ's behaviour
/// from research §7. Every one of the four measured traps has a test here: no end-of-response
/// terminator, an empty result that sends no packet, the five-connection cap, and tick-serialized
/// execution - plus the auth handshake, single-connection reuse, and recovery after a drop.
/// </summary>
public class RconConnectionTests
{
    private static RconOptions Fast => new()
    {
        IdleWindow = TimeSpan.FromMilliseconds(150),
        CommandTimeout = TimeSpan.FromSeconds(5),
        ConnectTimeout = TimeSpan.FromSeconds(3),
    };

    private static RconEndpoint EndpointFor(FakeRconServer server, string password) =>
        new(server.Host, server.Port, new SecretString(password));

    [Test]
    public async Task Authenticates_with_the_correct_password()
    {
        await using var server = new FakeRconServer { Password = "letmein" }.Start();
        await using var client = new RconConnection(EndpointFor(server, "letmein"), Fast);

        await client.ConnectAsync();

        await Assert.That(client.IsConnected).IsTrue();
    }

    [Test]
    public async Task Rejects_a_wrong_password_with_an_authentication_exception()
    {
        await using var server = new FakeRconServer { Password = "right" }.Start();
        await using var client = new RconConnection(EndpointFor(server, "wrong"), Fast);

        await Assert.That(client.ConnectAsync()).Throws<RconAuthenticationException>();
    }

    [Test]
    public async Task An_empty_password_server_reads_as_rcon_disabled()
    {
        // An empty RCONPassword silently disables RCON: PZ never authenticates, and the client sees
        // the socket close during the handshake (research §7).
        await using var server = new FakeRconServer { Password = string.Empty }.Start();
        await using var client = new RconConnection(EndpointFor(server, "anything"), Fast);

        await Assert.That(client.ConnectAsync()).Throws<RconAuthenticationException>();
    }

    [Test]
    public async Task Executes_a_command_and_returns_the_response()
    {
        await using var server = new FakeRconServer { Password = "p", Responder = cmd => $"result of {cmd}" }.Start();
        await using var client = new RconConnection(EndpointFor(server, "p"), Fast);

        string response = await client.ExecuteAsync("players");

        await Assert.That(response).IsEqualTo("result of players");
    }

    [Test]
    public async Task An_empty_result_returns_an_empty_string_without_hanging()
    {
        // Trap 2: a command that produces no output sends NO packet at all. The client must resolve
        // this to "" via the idle window, not block until its command timeout.
        await using var server = new FakeRconServer { Password = "p", Responder = _ => null }.Start();
        await using var client = new RconConnection(EndpointFor(server, "p"), Fast);

        var stopwatch = Stopwatch.StartNew();
        string response = await client.ExecuteAsync("servermsg \"hi\"");
        stopwatch.Stop();

        await Assert.That(response).IsEqualTo(string.Empty);
        // Resolved by the idle window (~150ms), nowhere near the 5s command timeout.
        await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Reassembles_a_multi_chunk_response()
    {
        // Trap 1: no terminator. A response longer than PZ's 4086-byte chunk arrives as several
        // packets; the client stitches them and ends on the short final chunk.
        string big = BuildAscii((4086 * 2) + 100);
        await using var server = new FakeRconServer { Password = "p", Responder = _ => big }.Start();
        await using var client = new RconConnection(EndpointFor(server, "p"), Fast);

        string response = await client.ExecuteAsync("help");

        await Assert.That(response.Length).IsEqualTo(big.Length);
        await Assert.That(response).IsEqualTo(big);
    }

    [Test]
    public async Task Ends_on_the_idle_window_when_the_last_chunk_is_exactly_the_chunk_size()
    {
        // Trap 1 backstop: a response that is an exact multiple of the chunk size has no short chunk,
        // so the idle window - not a short chunk - is what ends the read.
        string exact = BuildAscii(4086);
        await using var server = new FakeRconServer { Password = "p", Responder = _ => exact }.Start();
        await using var client = new RconConnection(EndpointFor(server, "p"), Fast);

        string response = await client.ExecuteAsync("help");

        await Assert.That(response).IsEqualTo(exact);
    }

    [Test]
    public async Task Serializes_concurrent_commands_over_one_connection()
    {
        // Trap 8: execution is tick-serialized and cannot be pipelined. Firing commands concurrently
        // must still yield each correct response, over a single shared socket.
        await using var server = new FakeRconServer { Password = "p", TickDelayMs = 20, Responder = cmd => $"r:{cmd}" }.Start();
        await using var client = new RconConnection(EndpointFor(server, "p"), Fast);

        Task<string>[] calls =
        [
            client.ExecuteAsync("a"),
            client.ExecuteAsync("b"),
            client.ExecuteAsync("c"),
            client.ExecuteAsync("d"),
        ];
        string[] results = await Task.WhenAll(calls);

        string[] expected = ["r:a", "r:b", "r:c", "r:d"];
        await Assert.That(results).IsEquivalentTo(expected);
        await Assert.That(server.AcceptedCount).IsEqualTo(1); // one socket for all commands
    }

    [Test]
    public async Task Reuses_a_single_connection_across_sequential_commands()
    {
        await using var server = new FakeRconServer { Password = "p", Responder = cmd => cmd }.Start();
        await using var client = new RconConnection(EndpointFor(server, "p"), Fast);

        await client.ExecuteAsync("one");
        await client.ExecuteAsync("two");
        await client.ExecuteAsync("three");

        await Assert.That(server.AcceptedCount).IsEqualTo(1);
    }

    [Test]
    public async Task The_sixth_simultaneous_connection_is_accepted_then_closed()
    {
        // Trap 7: PZ caps connections at five; the sixth is accepted then instantly EOF'd, so the
        // client sees a connect that fails during the handshake rather than a refusal.
        await using var server = new FakeRconServer { Password = "p", MaxConnections = 5 }.Start();

        var held = new List<RconConnection>();
        try
        {
            for (int i = 0; i < 5; i++)
            {
                var c = new RconConnection(EndpointFor(server, "p"), Fast);
                await c.ConnectAsync(); // sequential, so all five are registered before the sixth
                held.Add(c);
            }

            await using var sixth = new RconConnection(EndpointFor(server, "p"), Fast);
            await Assert.That(sixth.ConnectAsync()).Throws<RconException>();
            await Assert.That(server.MaxConcurrentConnections).IsEqualTo(5);
        }
        finally
        {
            foreach (RconConnection c in held)
            {
                await c.DisposeAsync();
            }
        }
    }

    [Test]
    public async Task Recovers_by_reconnecting_after_the_server_drops_the_socket()
    {
        // PZ normally keeps the socket alive (quirk 5); when a connection is nonetheless lost, a new
        // command re-establishes it. TCP half-open detection is inherently racy, so we assert the
        // observable contract: a later command succeeds and a second socket was opened.
        await using var server = new FakeRconServer { Password = "p", CloseAfterCommands = 1, Responder = cmd => $"ok:{cmd}" }.Start();
        await using var client = new RconConnection(EndpointFor(server, "p"), Fast);

        string first = await client.ExecuteAsync("one");
        await Assert.That(first).IsEqualTo("ok:one");

        await Task.Delay(150); // let the server's close propagate

        // The drop may surface as a thrown fault or, racily on a slow host, as a single empty read;
        // either way the connection is then dropped and re-established, so a bounded retry reaches the
        // live server again. The only non-empty response the server gives for "two" is "ok:two".
        string? second = null;
        for (int attempt = 0; attempt < 3 && string.IsNullOrEmpty(second); attempt++)
        {
            try
            {
                second = await client.ExecuteAsync("two");
            }
            catch (RconException)
            {
                // Connection lost surfaced; the next attempt reconnects.
            }
        }

        await Assert.That(second).IsEqualTo("ok:two");
        await Assert.That(server.AcceptedCount).IsGreaterThanOrEqualTo(2); // a reconnect happened
    }

    [Test]
    public async Task ExecuteAsync_after_dispose_throws()
    {
        await using var server = new FakeRconServer { Password = "p" }.Start();
        var client = new RconConnection(EndpointFor(server, "p"), Fast);
        await client.DisposeAsync();

        await Assert.That(client.ExecuteAsync("players")).Throws<ObjectDisposedException>();
    }

    private static string BuildAscii(int length)
    {
        var builder = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            builder.Append((char)('a' + (i % 26)));
        }

        return builder.ToString();
    }
}
