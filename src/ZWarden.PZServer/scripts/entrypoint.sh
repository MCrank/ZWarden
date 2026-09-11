#!/usr/bin/env bash
# ZWarden.PZServer entrypoint (Feature 12). Runs under tini as PID 1.
#
# Orchestrates the pure functions in pz-lib.sh: build the /pz tree, install on first
# run via anonymous SteamCMD (network - ADR 0009), tune the JVM, then launch the server
# with its stdin wired to a FIFO and translate SIGTERM into the blessed save->quit stop.
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=pz-lib.sh
source "${DIR}/pz-lib.sh"

PZ_ROOT="${PZ_ROOT:-/pz}"
SERVER_DIR="${PZ_ROOT}/server"
DATA_DIR="${PZ_ROOT}/data"
FIFO="${PZ_ROOT}/runtime/zomboid.control"
STEAMCMD="${PZ_ROOT}/runtime/steamcmd.sh"

log() { echo "[zwarden] $*"; }

pz_create_layout "${PZ_ROOT}"

if pz_needs_install "${SERVER_DIR}"; then
  log "installing Project Zomboid dedicated server (app ${PZ_STEAM_APP_ID}) via anonymous SteamCMD..."
  runscript="$(mktemp)"
  pz_build_steamcmd_runscript "${SERVER_DIR}" > "${runscript}"
  # Tee so the operator sees progress; capture to parse the (undocumented-exit-code) result.
  install_out="$("${STEAMCMD}" +runscript "${runscript}" 2>&1 | tee /dev/stderr)"
  rm -f "${runscript}"
  if printf '%s' "${install_out}" | pz_install_succeeded; then
    pz_write_appid "${SERVER_DIR}"
    touch "${SERVER_DIR}/${PZ_INSTALL_MARKER}"
    log "install complete."
  else
    log "SteamCMD did not report 'fully installed'; refusing to launch (fail-closed)." >&2
    exit 1
  fi
else
  log "existing install detected; skipping SteamCMD."
fi

pz_tune_jvm "${SERVER_DIR}/${PZ_LINUX_LAUNCHER}"

# A read-write open (3<>) never blocks and keeps the server's stdin from ever seeing EOF,
# so console commands can be written to the FIFO for the container's whole life.
[ -p "${FIFO}" ] || { rm -f "${FIFO}"; mkfifo "${FIFO}"; }
exec 3<> "${FIFO}"

launch="$(pz_build_launch_cmd "${SERVER_DIR}" "${DATA_DIR}")"
log "launching: ${launch}"
( cd "${SERVER_DIR}" && eval "${launch}" ) < "${FIFO}" &
SERVER_PID=$!

term_handler() {
  log "SIGTERM received; graceful stop (save, ${ZW_PZ_STOP_GRACE}s grace, quit)..."
  pz_graceful_stop "${FIFO}"
  wait "${SERVER_PID}"
}
trap term_handler TERM

wait "${SERVER_PID}"
