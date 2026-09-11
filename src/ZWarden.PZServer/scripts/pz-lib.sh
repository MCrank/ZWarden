#!/usr/bin/env bash
# ZWarden.PZServer entrypoint library (Feature 12).
#
# Pure, sourced shell functions - no side effects at source time - so the offline
# bats suite can exercise them against synthetic fixtures with no network and no
# real Project Zomboid files (ADR 0009). The entrypoint and supervisor source this.

# --- Invariants (research: project-zomboid-runtime.md, Build 42.20.4) ------------
PZ_STEAM_APP_ID="${PZ_STEAM_APP_ID:-380870}"     # the dedicated server (Tool, free-to-download)
PZ_STEAM_APPID_TXT="${PZ_STEAM_APPID_TXT:-108600}" # steam_appid.txt must contain ONLY this
PZ_INSTALL_MARKER=".zwarden-installed"           # written after a verified install
PZ_LINUX_LAUNCHER="start-server.sh"              # ships in the install dir

# --- Operator-tunable defaults (mini-plan Q3/Q4/Q6) ------------------------------
: "${ZW_PZ_BETA:=}"                # empty => public (42.20.x); e.g. legacy41, 42.19
: "${ZW_PZ_XMS:=4g}"
: "${ZW_PZ_XMX:=4g}"
: "${ZW_PZ_STOP_GRACE:=30}"        # seconds between `save` and `quit` on stop
: "${ZW_PZ_SERVERNAME:=servertest}"

# pz_needs_install <server_dir>
# Exit 0 (needs install) unless BOTH the shipped launcher and our completion marker
# are present. A launcher without the marker is a torn/partial install and must be
# re-run through `validate` rather than trusted (idempotent first-run detection).
pz_needs_install() {
  local server_dir="$1"
  if [ -f "${server_dir}/${PZ_LINUX_LAUNCHER}" ] && [ -f "${server_dir}/${PZ_INSTALL_MARKER}" ]; then
    return 1
  fi
  return 0
}

# pz_build_steamcmd_runscript <server_dir>
# Emits the SteamCMD runscript for an anonymous, validated install. -beta is added
# only when ZW_PZ_BETA is set (default: the public 42.20.x branch).
pz_build_steamcmd_runscript() {
  local server_dir="$1"
  local app_update="app_update ${PZ_STEAM_APP_ID}"
  if [ -n "${ZW_PZ_BETA}" ]; then
    app_update="${app_update} -beta ${ZW_PZ_BETA}"
  fi
  app_update="${app_update} validate"
  printf '%s\n' \
    "@ShutdownOnFailedCommand 1" \
    "@NoPromptForPassword 1" \
    "force_install_dir ${server_dir}" \
    "login anonymous" \
    "${app_update}" \
    "quit"
}

# pz_install_succeeded [app_id]  (reads SteamCMD stdout on stdin)
# Fail-closed: success ONLY if the explicit "fully installed" line is present, because
# SteamCMD's exit codes are undocumented by Valve (ADR 0009). Anything else is a failure.
pz_install_succeeded() {
  local app_id="${1:-$PZ_STEAM_APP_ID}"
  local out
  out="$(cat)"
  case "$out" in
    *"Success! App '${app_id}' fully installed"*) return 0 ;;
    *) return 1 ;;
  esac
}

# pz_write_appid <server_dir>
# steam_appid.txt must contain ONLY 108600 on a single line, or the server aborts with
# "Illegal termination of worker thread" (research §2).
pz_write_appid() {
  local server_dir="$1"
  printf '%s\n' "${PZ_STEAM_APPID_TXT}" > "${server_dir}/steam_appid.txt"
}

# pz_create_layout <root>
# Builds the canonical PZ filesystem (PRD 23). /pz/data is the persistent user-data
# volume; the Workshop cache lives under the Steam root and is symlinked in as
# data/workshop so operators and later features see one logical tree.
pz_create_layout() {
  local root="$1"
  mkdir -p "${root}/server" "${root}/runtime" "${root}/data"
  local workshop_cache="${root}/runtime/steamapps/workshop/content/${PZ_STEAM_APPID_TXT}"
  mkdir -p "${workshop_cache}"
  ln -sfn "${workshop_cache}" "${root}/data/workshop"
}

# pz_build_launch_cmd <server_dir> [data_dir]
# Delegates to PZ's shipped Linux launcher (which owns the classpath, natives path and
# LD_PRELOAD) and appends only the game arguments ZWarden controls. -cachedir relocates
# the user data onto the /pz/data volume so config, saves and logs survive a container
# rebuild (research §5).
pz_build_launch_cmd() {
  local server_dir="$1"
  local data_dir="${2:-/pz/data}"
  printf 'bash %s/%s -cachedir=%s -servername %s -statistic 0' \
    "${server_dir}" "${PZ_LINUX_LAUNCHER}" "${data_dir}" "${ZW_PZ_SERVERNAME}"
}

# pz_tune_jvm <launcher_file>
# Rewrites the shipped launcher's ship-default heap to the configured values and ensures
# -XX:+AlwaysPreTouch (officially recommended with ZGC as of Java 21). Editing the launcher
# rather than the JVM invocation keeps PZ's own classpath/natives wiring intact.
pz_tune_jvm() {
  local launcher="$1"
  sed -i -E \
    -e "s/-Xms[0-9]+[kmgKMG]?/-Xms${ZW_PZ_XMS}/g" \
    -e "s/-Xmx[0-9]+[kmgKMG]?/-Xmx${ZW_PZ_XMX}/g" \
    "${launcher}"
  if ! grep -q -- "-XX:+AlwaysPreTouch" "${launcher}"; then
    sed -i -E "s/-XX:\+UseZGC/-XX:+UseZGC -XX:+AlwaysPreTouch/" "${launcher}"
  fi
}

# pz_graceful_stop <fifo>
# The blessed shutdown (research §2/§5): `save` then, after ZW_PZ_STOP_GRACE seconds,
# `quit` - never a bare SIGTERM to the JVM, which the developers explicitly discourage.
# One writer open for the whole group keeps the server's stdin from seeing EOF between
# the two commands.
pz_graceful_stop() {
  local fifo="$1"
  {
    echo save
    sleep "${ZW_PZ_STOP_GRACE}"
    echo quit
  } > "${fifo}"
}
