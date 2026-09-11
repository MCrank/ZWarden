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
 && apt-get install -y --no-install-recommends \
      ca-certificates curl tini procps \
      lib32gcc-s1 lib32stdc++6 \
 && rm -rf /var/lib/apt/lists/*

# Non-root runtime (PRD 24): a dedicated user owns the whole /pz tree.
RUN groupadd -g "${PZ_GID}" pzserver \
 && useradd -u "${PZ_UID}" -g "${PZ_GID}" -m -d /home/pzserver -s /usr/sbin/nologin pzserver \
 && mkdir -p /pz/server /pz/runtime /pz/data \
 && chown -R pzserver:pzserver /pz

COPY --chown=pzserver:pzserver scripts/ /pz/scripts/
RUN chmod +x /pz/scripts/*.sh

USER pzserver
WORKDIR /pz/runtime

# SteamCMD from Valve's own CDN tarball - self-owned, not apt, not a third-party image (Q1).
RUN curl -fsSL https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz | tar -xz

ENV PZ_ROOT=/pz \
    ZW_PZ_BETA="" \
    ZW_PZ_XMS=4g \
    ZW_PZ_XMX=4g \
    ZW_PZ_STOP_GRACE=30 \
    ZW_PZ_SERVERNAME=servertest

# Canonical labels (PRD 25). server-id and agent-id are set by the Agent at create time (F13).
LABEL io.zwarden.managed="true" \
      io.zwarden.runtime="project-zomboid" \
      io.zwarden.schema-version="1"

# The two UDP ports (research §3). RCON 27015/tcp is deliberately NOT exposed - it is private
# and Agent-owned (F18, PRD 29).
EXPOSE 16261/udp 16262/udp
VOLUME /pz/data

# Base health check (Q5): the GameServer JVM is alive. Generous start-period for the first-run
# SteamCMD install + world generation. Feature 16 layers the real hierarchical model.
HEALTHCHECK --interval=30s --timeout=5s --start-period=600s --retries=3 \
  CMD /pz/scripts/healthcheck.sh

ENTRYPOINT ["/usr/bin/tini", "--", "/pz/scripts/entrypoint.sh"]
