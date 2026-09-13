namespace ZWarden.Rcon;

/// <summary>
/// The Source RCON wire constants, named and valued exactly as PZ's <c>zombie.network.RCONServer</c>
/// declares them (research/project-zomboid-runtime.md §7, verified against Build 42.20.2 and the
/// archived Valve spec). Two of the four <see cref="Type"/> values collide by design - the spec
/// reuses <c>2</c> for both an auth response and an exec command - so the meaning is fixed by the
/// direction the packet travels, not by the number alone.
/// </summary>
internal static class RconConstants
{
    /// <summary>Packet type values (little-endian int on the wire).</summary>
    internal static class Type
    {
        /// <summary>Server → client: a chunk of a command's response body. Also the type of the
        /// empty packet PZ emits before an auth response.</summary>
        internal const int ResponseValue = 0;

        /// <summary>Server → client: the reply to an auth request. Shares the value <c>2</c> with
        /// <see cref="ExecCommand"/> - disambiguated by travel direction.</summary>
        internal const int AuthResponse = 2;

        /// <summary>Client → server: run an admin command. Shares the value <c>2</c> with
        /// <see cref="AuthResponse"/>.</summary>
        internal const int ExecCommand = 2;

        /// <summary>Client → server: authenticate with the RCON password.</summary>
        internal const int Auth = 3;
    }

    /// <summary>The id PZ stamps on an auth response when the password did not match
    /// (<c>0xFFFFFFFF</c>); it then closes the socket. The same id marks an unauthenticated
    /// exec attempt (research §7 quirk 6).</summary>
    internal const int AuthFailureId = -1;

    /// <summary>The bytes the size field accounts for around the body: id (4) + type (4) + the two
    /// trailing NULs (2). The size field <b>excludes</b> its own 4 bytes, so
    /// <c>size == body.Length + SizeFieldOverhead</c> and <c>body.Length == size - SizeFieldOverhead</c>.</summary>
    internal const int SizeFieldOverhead = 4 /* id */ + 4 /* type */ + 2 /* two NULs */;

    /// <summary>Smallest legal value of the size field: an empty body still carries id, type and the
    /// two NULs. A frame claiming less than this is malformed.</summary>
    internal const int MinSizeField = SizeFieldOverhead;

    /// <summary>Largest legal value of the size field, spec-stated (<c>"the maximum possible value
    /// of packet size is 4096"</c>). The on-wire frame is <c>4 + size</c> bytes, so a maximal frame
    /// is 4100 bytes and a maximal body is <c>4096 - 10 = 4086</c> bytes - which is exactly PZ's
    /// response chunk cap below.</summary>
    internal const int MaxSizeField = 4096;

    /// <summary>PZ (Build 42.20.2) splits a response body into chunks of at most this many bytes,
    /// all sharing one id and <see cref="Type.ResponseValue"/>. Because PZ never emits an
    /// end-of-response sentinel, a chunk <b>shorter</b> than this is the client's only positive
    /// signal that the response is complete (research §7 quirks 1-2).</summary>
    internal const int MaxResponseChunkBodyBytes = MaxSizeField - SizeFieldOverhead;
}
