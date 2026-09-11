# ZWarden
ZWarden is a secure, self-hosted control plane for deploying, operating, monitoring, and troubleshooting Project Zomboid dedicated servers.

## Getting started

```bash
dotnet restore ZWarden.slnx --locked-mode
dotnet build   ZWarden.slnx -c Release
dotnet test    --project tests/ZWarden.Domain.Tests/ZWarden.Domain.Tests.csproj
```

## Orientation

- **[CONTRIBUTING.md](./CONTRIBUTING.md)** — how to build, test (TDD is mandatory), and the guards CI enforces.
- **[CONTEXT.md](./CONTEXT.md)** — the domain glossary; the canonical vocabulary.
- **[docs/adr/](./docs/adr/)** — architecture decision records (start at the [index](./docs/adr/README.md)).
- **[docs/scope-and-sequencing.md](./docs/scope-and-sequencing.md)** — the v1.0 / v1.1 roadmap and feature sequence.
- **[docs/trust-boundaries.md](./docs/trust-boundaries.md)** — the six trust boundaries the architecture tests enforce.
- **[docs/feature-plans/](./docs/feature-plans/)** — per-feature mini-plans (PRD 60).
