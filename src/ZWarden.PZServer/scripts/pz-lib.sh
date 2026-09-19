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
PZ_UPDATE_REQUEST=".zwarden-update-requested"    # F17: Agent-dropped control-file in /pz/data holding the OperationId
PZ_STEAMCMD_BAKED="${PZ_STEAMCMD_BAKED:-/opt/steamcmd}" # F17: SteamCMD is baked here, outside any /pz mount

# --- Operator-tunable defaults (mini-plan Q3/Q4/Q6) ------------------------------
: "${ZW_PZ_BETA:=}"                # empty => public (42.20.x); e.g. legacy41, 42.19
: "${ZW_PZ_XMS:=4g}"
: "${ZW_PZ_XMX:=4g}"
: "${ZW_PZ_STOP_GRACE:=30}"        # seconds between `save` and `quit` on stop
: "${ZW_PZ_SERVERNAME:=servertest}"
: "${ZW_PZ_INSTALL_ATTEMPTS:=3}"   # SteamCMD install attempts before fail-closed (F12/#65)
: "${ZW_PZ_INSTALL_RETRY_DELAY:=15}" # seconds between install attempts
: "${ZW_PZ_ADMIN_PASSWORD:=}"      # non-interactive in-game admin password (#188); generated + persisted when empty
PZ_ADMIN_PASSWORD_FILE=".zwarden-adminpw" # where a generated admin password is persisted under /pz/data

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

# pz_install_with_retry <steamcmd> <runscript>
# Drives the anonymous install with retries. A FRESH SteamCMD's first app_update routinely
# dies with "Failed to install app '<id>' (Missing configuration)" - a known Valve gotcha -
# and, less often, a transient Steam-side error; re-running app_update warms the config and
# clears both (ADR 0009). Succeeds as soon as the fail-closed "fully installed" line appears
# (pz_install_succeeded), retrying up to ZW_PZ_INSTALL_ATTEMPTS with ZW_PZ_INSTALL_RETRY_DELAY
# seconds between tries; returns 1 only after every attempt fails. SteamCMD output is teed
# through so operators still watch progress live.
pz_install_with_retry() {
  local steamcmd="$1" runscript="$2"
  local attempts="${ZW_PZ_INSTALL_ATTEMPTS:-3}"
  local delay="${ZW_PZ_INSTALL_RETRY_DELAY:-15}"
  local n out
  for (( n = 1; n <= attempts; n++ )); do
    echo "[zwarden] SteamCMD install attempt ${n}/${attempts}..." >&2
    # SteamCMD exits non-zero on a failed app_update (@ShutdownOnFailedCommand); keep the
    # capture from aborting a `set -e` caller so the retry loop can actually run. Success is
    # decided from stdout, not the exit code (ADR 0009).
    out="$("${steamcmd}" +runscript "${runscript}" 2>&1 | tee /dev/stderr)" || true
    if printf '%s' "${out}" | pz_install_succeeded; then
      return 0
    fi
    echo "[zwarden] attempt ${n}/${attempts} did not report a completed install." >&2
    if [ "${n}" -lt "${attempts}" ]; then
      sleep "${delay}"
    fi
  done
  return 1
}

# pz_write_appid <server_dir>
# steam_appid.txt must contain ONLY 108600 on a single line, or the server aborts with
# "Illegal termination of worker thread" (research §2).
pz_write_appid() {
  local server_dir="$1"
  printf '%s\n' "${PZ_STEAM_APPID_TXT}" > "${server_dir}/steam_appid.txt"
}

# --- Health (F12 base check) -----------------------------------------------------
# The running dedicated-server process. Build 42's start-server.sh execs a NATIVE launcher
# (`./ProjectZomboid64`) that runs the JVM embedded, so the main class `zombie.network.GameServer`
# is NOT present in any process's argv (#191) - matching it alone leaves the container forever
# "unhealthy" even though the server is up. Match the native launcher, and also the class name so a
# direct `java ... zombie.network.GameServer` launch (legacy/B41, or a hand run) is still detected.
# Extended-regex alternation (pgrep -f uses ERE).
PZ_SERVER_PROCESS_PATTERN='ProjectZomboid[0-9]*|zombie\.network\.GameServer'

# pz_server_running  — exit 0 when the PZ dedicated-server process is alive, non-zero otherwise.
# Deliberately shallow (the container base health, mini-plan Q5); F16 layers the hierarchical model.
pz_server_running() {
  pgrep -f "${PZ_SERVER_PROCESS_PATTERN}" >/dev/null 2>&1
}

# pz_bootstrap_steamcmd <baked_dir> <runtime_dir>
# SteamCMD is baked at /opt/steamcmd (outside any /pz mount) and copied into the runtime dir on
# boot, because under an Agent-created container /pz/runtime is an ephemeral tmpfs (ReadonlyRootfs,
# ADR 0008) and SteamCMD self-updates into its own directory - so it must live on writable storage,
# and a mount at /pz/runtime would shadow a binary baked there. Idempotent: skips the copy when a
# steamcmd.sh already exists (a by-hand run with a persistent runtime keeps its self-updated client).
pz_bootstrap_steamcmd() {
  local baked="$1" runtime_dir="$2"
  if [ ! -x "${runtime_dir}/steamcmd.sh" ]; then
    mkdir -p "${runtime_dir}"
    # -dR (recurse + keep symlinks + copy mode), NOT -a (#184): under an Agent-created container /pz/runtime is
    # a root-owned tmpfs, and `-a` would try to preserve timestamps on that root dir, which the non-root PZ user
    # cannot do ("Operation not permitted") — aborting the boot. We don't need the source's times/ownership.
    cp -dR "${baked}/." "${runtime_dir}/"
  fi
}

# --- Update path (F17) -----------------------------------------------------------
# The Agent cannot exec into the container (ADR 0008 denies exec/attach), so it requests
# an update by dropping a control-file into the writable /pz/data volume - holding the
# OperationId - and restarting. The entrypoint then runs `app_update ... validate` PAST the
# install marker, brackets the run so the Agent can bound its `docker logs` parse, and - unlike
# a first-run install - treats a failed update as non-fatal (the existing install still boots).

# pz_update_requested <data_dir>
# Exit 0 when an update control-file is present in the data volume, non-zero otherwise.
pz_update_requested() {
  local data_dir="$1"
  [ -f "${data_dir}/${PZ_UPDATE_REQUEST}" ]
}

# pz_read_update_session <data_dir>
# Echoes the OperationId the Agent wrote into the control-file (first line, whitespace trimmed),
# or empty if absent. This id brackets the SteamCMD output in the log so the Agent parses only
# the lines of this update session.
pz_read_update_session() {
  local data_dir="$1" session=""
  read -r session < "${data_dir}/${PZ_UPDATE_REQUEST}" 2>/dev/null || true
  printf '%s' "${session}"
}

# pz_clear_update_request <data_dir>
# Removes the control-file. Called after every update attempt - success OR failure - so a
# persisted request can never drive a restart loop of repeated updates.
pz_clear_update_request() {
  local data_dir="$1"
  rm -f "${data_dir}/${PZ_UPDATE_REQUEST}"
}

# pz_run_update <steamcmd> <runscript> <session>
# Runs the same anonymous `app_update ... validate` as an install (via pz_install_with_retry,
# so the "Missing configuration" retry still applies), bracketed by a begin/end banner carrying
# the session id. The Agent keys its log-parse window on these banners; the end banner also
# states the stdout-decided outcome authoritatively. Returns the run's success/failure.
pz_run_update() {
  local steamcmd="$1" runscript="$2" session="$3"
  echo "[zwarden] steamcmd update session ${session} begin"
  if pz_install_with_retry "${steamcmd}" "${runscript}"; then
    echo "[zwarden] steamcmd update session ${session} end (success)"
    return 0
  fi
  echo "[zwarden] steamcmd update session ${session} end (failure)"
  return 1
}

# pz_apply_update <steamcmd> <server_dir> <data_dir>
# The whole update disposition: read the session, build the standard validated runscript, run it
# bracketed, and on success (re)write steam_appid.txt and the install marker. The control-file is
# cleared unconditionally at the end. Returns 0 on a verified update, non-zero on failure - the
# caller logs and lets the server boot on the existing install; it must NOT fail-close on this.
pz_apply_update() {
  local steamcmd="$1" server_dir="$2" data_dir="$3"
  local session runscript rc
  session="$(pz_read_update_session "${data_dir}")"
  runscript="$(mktemp)"
  pz_build_steamcmd_runscript "${server_dir}" > "${runscript}"
  if pz_run_update "${steamcmd}" "${runscript}" "${session}"; then
    pz_write_appid "${server_dir}"
    touch "${server_dir}/${PZ_INSTALL_MARKER}"
    rc=0
  else
    rc=1
  fi
  rm -f "${runscript}"
  pz_clear_update_request "${data_dir}"
  return "${rc}"
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

# pz_admin_password <data_dir>
# The in-game administrator password supplied non-interactively at launch (#188). Build 42
# PROMPTS for it on first boot when none is given ("Enter new administrator password:") and,
# with stdin on the control FIFO, that prompt never resolves and the server hangs. Precedence:
# an operator-set ZW_PZ_ADMIN_PASSWORD wins; otherwise a strong one is generated ONCE and
# persisted under the world volume so it is stable across restarts (a fresh value each boot
# would rotate the admin account every restart). ZWarden manages the server over RCON, not this
# account, so a generated default is safe; operators can override or read the persisted file.
pz_admin_password() {
  local data_dir="${1:-/pz/data}"
  if [ -n "${ZW_PZ_ADMIN_PASSWORD}" ]; then
    printf '%s' "${ZW_PZ_ADMIN_PASSWORD}"
    return 0
  fi
  local pwfile="${data_dir}/${PZ_ADMIN_PASSWORD_FILE}"
  if [ -s "${pwfile}" ]; then
    cat "${pwfile}"
    return 0
  fi
  local pw
  pw="$(LC_ALL=C tr -dc 'A-Za-z0-9' < /dev/urandom | head -c 24)"
  mkdir -p "${data_dir}"
  ( umask 077; printf '%s' "${pw}" > "${pwfile}" )
  printf '%s' "${pw}"
}

# pz_build_launch_cmd <server_dir> [data_dir]
# Delegates to PZ's shipped Linux launcher (which owns the classpath, natives path and
# LD_PRELOAD) and appends only the game arguments ZWarden controls. -cachedir relocates
# the user data onto the /pz/data volume so config, saves and logs survive a container
# rebuild (research §5). The admin password is appended by the entrypoint (never here) so it
# is kept out of the logged launch line; -statistic was dropped as Build 42 rejects it.
pz_build_launch_cmd() {
  local server_dir="$1"
  local data_dir="${2:-/pz/data}"
  printf 'bash %s/%s -cachedir=%s -servername %s' \
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
