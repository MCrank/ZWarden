# syntax=docker/dockerfile:1
#
# ZWarden.Agent — the host-worker image for the F34 reference Docker Compose distribution (ADR 0037).
#
# The Agent is a headless Worker Service (F8): no inbound HTTP. It connects OUTBOUND to the control plane
# over wss (through Caddy, D-8) and reaches the Docker daemon ONLY through the wollomatic socket-proxy
# (ADR 0008) — it never mounts the raw Docker socket. Build context is the repository root (compose sets
# `context: ../..`).
ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src
COPY . .
# See the Web Dockerfile: no --locked-mode at image-build time (the offline CI tier owns lock enforcement).
RUN dotnet restore src/ZWarden.Agent/ZWarden.Agent.csproj
RUN dotnet publish src/ZWarden.Agent/ZWarden.Agent.csproj -c Release -o /app --no-restore

# The runtime image (no ASP.NET stack needed — the Agent serves nothing).
FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION} AS final
RUN groupadd -g 10001 zwarden \
 && useradd -u 10001 -g 10001 -m -s /usr/sbin/nologin zwarden \
 && mkdir -p /var/lib/zwarden \
 && chown -R zwarden:zwarden /var/lib/zwarden
WORKDIR /app
COPY --from=build /app .
# Non-root runtime (PRD 24).
USER zwarden
# The per-Agent identity + trust store (F9) persist here so a restart reuses its credential and never
# re-enrolls; the Compose stack mounts a named volume at this path.
VOLUME /var/lib/zwarden
ENTRYPOINT ["dotnet", "ZWarden.Agent.dll"]
