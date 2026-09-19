# syntax=docker/dockerfile:1
#
# ZWarden.Web — the control-plane image for the F34 reference Docker Compose distribution (ADR 0037).
#
# Build context is the REPOSITORY ROOT (the compose file sets `context: ../..`) so the whole project graph
# — Directory.Build.props, Directory.Packages.props, nuget.config, the referenced src projects — is present
# for the restore/publish. Only Caddy is ever host-published (ADR 0035); this image listens on plain HTTP
# 8080 inside the stack and trusts the proxy's X-Forwarded-* headers (F32).
ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src
COPY . .
# No --locked-mode here: the image build is a deploy-time action, not the offline CI tier, and pinning the
# lock to the container SDK's exact patch invites the SDK-drift failures the CI tier already guards.
RUN dotnet restore src/ZWarden.Web/ZWarden.Web.csproj
RUN dotnet publish src/ZWarden.Web/ZWarden.Web.csproj -c Release -o /app --no-restore

# Debian-based aspnet (not chiseled): a shell + apt let us add curl for the container liveness probe, which
# the health-ordered Compose startup (D-5) gates the Agent and Caddy on.
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/* \
 && groupadd -g 10001 zwarden \
 && useradd -u 10001 -g 10001 -m -s /usr/sbin/nologin zwarden \
 && mkdir -p /data \
 && chown -R zwarden:zwarden /data
WORKDIR /app
COPY --from=build /app .
# Non-root runtime (PRD 24).
USER zwarden
# The SQLite database (base mode) and any other durable Web state live on the mounted volume, not the layer.
VOLUME /data
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
# Default the Data Protection key ring onto the writable /data volume (#186). Without this the app falls back
# to <contentRoot>/dp-keys = /app/dp-keys, which is root-owned, so the non-root `zwarden` user crashes creating
# it (UnauthorizedAccessException) on a bare `docker run` — /app is not writable by uid 10001. /data is created
# and chowned to zwarden above and is the persisted volume, so keys are durable across recreates by default.
# The reference Compose sets the same value explicitly (deploy/compose/compose.yaml); this makes the standalone
# image self-sufficient.
ENV ZWarden__DataProtection__KeyRingPath=/data/dp-keys
ENTRYPOINT ["dotnet", "ZWarden.Web.dll"]
