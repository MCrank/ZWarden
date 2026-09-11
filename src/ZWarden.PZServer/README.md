# ZWarden.PZServer

The canonical managed Project Zomboid runtime container (`CONTEXT.md` → **ZWarden.PZServer**).

This is a **container build context**, not a .NET assembly — it has no `.csproj` and is not part
of `ZWarden.slnx`. Its Dockerfile, SteamCMD tooling, canonical filesystem layout, non-root runtime
and stdin-FIFO supervision are delivered by **Feature 12** (see
[`docs/scope-and-sequencing.md`](../../docs/scope-and-sequencing.md) §6, Track B). It lives under
`src/` to keep the component tree in one place.

No Project Zomboid artefact is ever committed here (ADR 0009): the server is installed at runtime
via SteamCMD, and CI uses synthetic fixtures plus a scheduled real-install tier.
