// ZWarden.Agent host entry point.
//
// The Worker Service skeleton proper - Agent identity, configuration, lifecycle,
// local persistence, health state, graceful shutdown and diagnostic hooks - is
// Feature 8 (docs/scope-and-sequencing.md §6, Track C). Feature 0 provides only a
// host that builds and starts cleanly.
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
IHost host = builder.Build();
host.Run();
