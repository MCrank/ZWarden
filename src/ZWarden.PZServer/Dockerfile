# ZWarden.PZServer - the canonical managed Project Zomboid runtime container (Feature 12).
#
# The BUILD needs general network (apt + Valve's SteamCMD tarball) but never touches Steam
# or Project Zomboid's files. The ~6.72 GiB PZ server install happens at RUNTIME on first
# run via anonymous SteamCMD (ADR 0009), so no PZ artefact is ever committed or baked, and
# the canonical image can be pre-provisioned on a host without pulling PZ at create time.
FROM debian:bookworm-slim
# NOTE: the reference deployment (F34) pins this base by digest; the foundation uses the tag.

ARG PZ_UID=10000
ARG PZ_GID=10000

# i386 multiarch - SteamCMD is a 32-bit x86 binary (research §4) - plus the minimal libs it
# needs, tini as init (correct signal forwarding + zombie reaping), procps for the health
# check, and curl/ca-certificates to fetch the SteamCMD tarball.
RUN dpkg --add-architecture i386 \
 && apt-get update \
 && apt-get -y upgrade \
 && apt-get install -y --no-install-recommends \
      ca-certificates curl tini procps \
      lib32gcc-s1 lib32stdc++6 \
 && rm -rf /var/lib/apt/lists/*
# `apt-get upgrade` pulls the base layer's packages up to current Debian security patches at build
# time (F40 container-scan gate, ADR 0039): debian:bookworm-slim ships a stale libpcre2 among others,
# and the reference deployment rebuilds/repins the digest to carry the fixes forward.

# Non-root runtime (PRD 24): a dedicated user owns the whole /pz tree.
RUN groupadd -g "${PZ_GID}" pzserver \
 && useradd -u "${PZ_UID}" -g "${PZ_GID}" -m -d /home/pzserver -s /usr/sbin/nologin pzserver \
 && mkdir -p /pz/server /pz/runtime /pz/data /opt/steamcmd \
 && chown -R pzserver:pzserver /pz /opt/steamcmd

COPY --chown=pzserver:pzserver scripts/ /pz/scripts/
RUN chmod +x /pz/scripts/*.sh

USER pzserver
WORKDIR /opt/steamcmd

# SteamCMD from Valve's own CDN tarball - self-owned, not apt, not a third-party image (Q1). Baked
# at /opt/steamcmd, OUTSIDE any /pz mount: under an Agent-created container /pz/runtime is an
# ephemeral tmpfs (F17), so a binary baked there would be shadowed. The entrypoint copies it into
# the writable runtime on boot, where SteamCMD self-updates (research §4).
RUN curl -fsSL https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz | tar -xz

ENV PZ_ROOT=/pz \
    ZW_PZ_BETA="" \
    ZW_PZ_XMS=4g \
    ZW_PZ_XMX=4g \
    ZW_PZ_STOP_GRACE=30 \
    ZW_PZ_SERVERNAME=servertest
# ZW_PZ_ADMIN_PASSWORD (runtime-only, deliberately NOT an ENV so no secret is baked into the image):
# the in-game admin password passed non-interactively at launch (#188). Set it via `docker run -e` /
# compose `environment`. When empty the entrypoint generates a strong one on first boot and persists it
# under /pz/data (stable across restarts). Build 42 otherwise prompts for it and the server hangs on stdin.
# NOTE (#184): under an Agent-created container the root filesystem is read-only, so SteamCMD's writable needs —
# a temp dir (mktemp, /tmp/dumps) and $HOME (~/.steam) — are redirected onto writable tmpfs by the F13 create
# template (PzContainerFactory sets HOME/TMPDIR and mounts a /tmp tmpfs), NOT baked here, so the standalone
# image keeps its default writable /tmp and /home/pzserver.

# Canonical labels (PRD 25). server-id and agent-id are set by the Agent at create time (F13).
LABEL io.zwarden.managed="true" \
      io.zwarden.runtime="project-zomboid" \
      io.zwarden.schema-version="1"

# The two UDP ports (research §3). RCON 27015/tcp is deliberately NOT exposed - it is private
# and Agent-owned (F18, PRD 29).
EXPOSE 16261/udp 16262/udp
# /pz/data holds the world; /pz/server holds the SteamCMD install (F17: persisted so updates are
# incremental and the build id survives a recreate). /pz/runtime is deliberately NOT a volume - it
# is an ephemeral tmpfs the Agent supplies (F17), re-bootstrapped each boot.
VOLUME /pz/data /pz/server

# Base health check (Q5): the GameServer JVM is alive. The start-period must cover a genuine first run — the
# ~6.72 GiB anonymous SteamCMD download + preallocation + first world generation — during which there is no
# GameServer process yet; 10 min was far too short (the container went `unhealthy` mid-install, #191), so it is
# 45 min. A successful check still flips the container healthy immediately, even within the start-period, so a
# fast (already-installed) restart is not delayed. Feature 16 layers the real hierarchical model.
HEALTHCHECK --interval=30s --timeout=5s --start-period=45m --retries=3 \
  CMD /pz/scripts/healthcheck.sh

ENTRYPOINT ["/usr/bin/tini", "--", "/pz/scripts/entrypoint.sh"]
