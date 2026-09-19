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
# /pz/runtime is an ephemeral tmpfs under an Agent-created container (F17), so stage the baked
# SteamCMD into it before first use; SteamCMD self-updates into its own dir, hence writable storage.
pz_bootstrap_steamcmd "${PZ_STEAMCMD_BAKED}" "${PZ_ROOT}/runtime"

if pz_needs_install "${SERVER_DIR}"; then
  log "installing Project Zomboid dedicated server (app ${PZ_STEAM_APP_ID}) via anonymous SteamCMD..."
  runscript="$(mktemp)"
  pz_build_steamcmd_runscript "${SERVER_DIR}" > "${runscript}"
  # A fresh SteamCMD's first app_update often fails with "Missing configuration"; retry to
  # warm the config, and parse stdout for the result since exit codes are unreliable (F12/#65).
  if pz_install_with_retry "${STEAMCMD}" "${runscript}"; then
    rm -f "${runscript}"
    pz_write_appid "${SERVER_DIR}"
    touch "${SERVER_DIR}/${PZ_INSTALL_MARKER}"
    log "install complete."
  else
    rm -f "${runscript}"
    log "SteamCMD failed to install after ${ZW_PZ_INSTALL_ATTEMPTS} attempts; refusing to launch (fail-closed)." >&2
    exit 1
  fi
elif pz_update_requested "${DATA_DIR}"; then
  # F17: the Agent requested an update by dropping a control-file (with the OperationId) into
  # /pz/data and restarting us. Run app_update...validate past the marker; a FAILED update is
  # non-fatal - the existing install is intact, so we log and boot it (the Operation reports the
  # failure via the log the Agent parses). The request is always cleared inside pz_apply_update.
  log "update requested (session $(pz_read_update_session "${DATA_DIR}")); running SteamCMD app_update validate..."
  if pz_apply_update "${STEAMCMD}" "${SERVER_DIR}" "${DATA_DIR}"; then
    log "update complete."
  else
    log "update FAILED; launching the server on the existing install (the Operation reports the failure)." >&2
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
# Build 42 prompts for an admin password on first boot; supply it non-interactively so the
# server never blocks on stdin (#188). Kept OUT of the logged line and appended only to the
# executed command, so the secret never lands in container logs the Agent streams.
admin_pw="$(pz_admin_password "${DATA_DIR}")"
log "launching: ${launch} -adminpassword <redacted>"
( cd "${SERVER_DIR}" && eval "${launch} -adminpassword $(printf '%q' "${admin_pw}")" ) < "${FIFO}" &
SERVER_PID=$!

# This handler is the authoritative stop path (#193). tini runs in its DEFAULT mode (the ENTRYPOINT is
# `tini -- entrypoint.sh`, NOT `tini -g --`), so on `docker stop` it forwards SIGTERM to its direct child
# ONLY — this entrypoint — and never to the process group. The JVM (a grandchild) therefore does not get
# SIGTERM directly; this trap fires and drives the blessed FIFO save→grace→quit. Verified live against a
# real Build 42 managed container stopped the way the control plane stops it (F15 `docker stop -t 120`):
# "SIGTERM received" logs, the grace window elapses, the world saves (#2 pre-release validation).
# Build 42's ./ProjectZomboid64 launcher also self-saves on a direct SIGTERM, so even a stop that reached
# the JVM directly (e.g. `docker stop -g`, or a future tini `-g`) is data-safe — a benign backstop, not the
# mechanism we rely on. Keep this path: it honours ZW_PZ_STOP_GRACE and is the seam for any future
# in-container pre-stop step, and B42's native behaviour is not contractual (it changed B41→B42).
# NOTE: this trap uses ZW_PZ_STOP_GRACE (default 30s), so a stop must allow at least that long before the
# SIGKILL deadline. The managed path uses `docker stop -t 120`; a MANUAL `docker stop` defaults to 10s and
# would SIGKILL mid-grace (world still saved, but no clean quit) — pass `-t 120` when stopping by hand.
term_handler() {
  log "SIGTERM received; graceful stop (save, ${ZW_PZ_STOP_GRACE}s grace, quit)..."
  pz_graceful_stop "${FIFO}"
  wait "${SERVER_PID}"
  # Exit with the JVM's real status (0 on a clean save→quit), NOT the 143 the outer `wait` below would
  # surface after being interrupted by SIGTERM — a graceful stop must leave the container exited 0 so the
  # control plane reads it as Stopped, not Failed (#200).
  exit $?
}
trap term_handler TERM

wait "${SERVER_PID}"
