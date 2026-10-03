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
: "${ZW_PZ_BETA:=}"                # empty => public; e.g. unstable, 42.19 (Build 42 only, #258)
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
# only when ZW_PZ_BETA is set (default: the public branch). The Agent validates the name to
# [a-z0-9._-] before it reaches here, since it is interpolated unquoted (#258).
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
# #280: SteamCMD can print "Timed out waiting for update to start, bailing." and STILL print the
# success line - the job never ran, so a real update would be missed. That is not a success.
pz_install_succeeded() {
  local app_id="${1:-$PZ_STEAM_APP_ID}"
  local out
  out="$(cat)"
  case "$out" in
    *"Timed out waiting for update to start"*) return 1 ;;
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
# seconds between tries; returns 1 only after every attempt fails, or 2 at once when Steam rejects
# the ZW_PZ_BETA branch (#258: nothing to retry; the reason is logged). SteamCMD output is teed
# through so operators still watch progress live.
# #288: with [server_dir], an attempt that ends "state is 0x6 after update job" moves the app manifest
# aside ONCE (pz_reset_app_manifest) before the next attempt - SteamCMD's sticky failed-job state, which
# no amount of plain retrying clears.
pz_install_with_retry() {
  local steamcmd="$1" runscript="$2" server_dir="${3:-}"
  local attempts="${ZW_PZ_INSTALL_ATTEMPTS:-3}"
  local delay="${ZW_PZ_INSTALL_RETRY_DELAY:-15}"
  local n out stalled=0 stuck=0 reset=0
  for (( n = 1; n <= attempts; n++ )); do
    echo "[zwarden] SteamCMD install attempt ${n}/${attempts}..." >&2
    # SteamCMD exits non-zero on a failed app_update (@ShutdownOnFailedCommand); keep the
    # capture from aborting a `set -e` caller so the retry loop can actually run. Success is
    # decided from stdout, not the exit code (ADR 0009).
    out="$("${steamcmd}" +runscript "${runscript}" 2>&1 | tee /dev/stderr)" || true
    if printf '%s' "${out}" | pz_install_succeeded; then
      return 0
    fi
    # #258: an unknown or password-protected branch is not transient; retrying only delays the reason.
    case "${out}" in
      *"Failed to set beta"*)
        echo "[zwarden] Steam branch '${ZW_PZ_BETA}' does not exist or is password-protected; refusing to launch." >&2
        return 2
        ;;
    esac
    case "${out}" in
      *"Timed out waiting for update to start"*) stalled=$(( stalled + 1 )) ;;
    esac
    stuck=0
    case "${out}" in
      *"state is 0x6 after update job"*) stuck=1 ;;
    esac
    echo "[zwarden] attempt ${n}/${attempts} did not report a completed install." >&2
    if [ "${stuck}" -eq 1 ] && [ "${reset}" -eq 0 ] && [ -n "${server_dir}" ]; then
      pz_reset_app_manifest "${server_dir}"
      reset=1
    fi
    if [ "${n}" -lt "${attempts}" ]; then
      sleep "${delay}"
    fi
  done
  # #280: the job never started on any attempt - say so plainly. ERROR!-prefixed (no [zwarden]
  # tag) so the Agent's SteamCmdLogParser takes it as the Operation's failure reason.
  if [ "${stalled}" -eq "${attempts}" ]; then
    echo "ERROR! SteamCMD timed out waiting for the update to start on every attempt; the installed build is unchanged."
  elif [ "${stuck}" -eq 1 ]; then
    # #288: still 0x6 on the last attempt (a full disk causes the same state). Same ERROR! contract as above.
    echo "ERROR! SteamCMD update state is stuck (0x6) even after resetting the app manifest; check free disk space on the server volume. The installed build is unchanged."
  fi
  return 1
}

# pz_reset_app_manifest <server_dir>
# #288: a failed SteamCMD job writes StateFlags/UpdateResult 6 into steamapps/appmanifest_<app>.acf, and every
# later run reads that and aborts ("state is 0x6 after update job") without downloading - a long-standing Valve
# bug. Moving the manifest aside makes SteamCMD rebuild it; the game files and the world are untouched, and the
# runscript's `validate` re-checks the install. Renamed (not deleted) so an operator can restore it; an older
# .bak is overwritten. Best-effort: no manifest is fine.
pz_reset_app_manifest() {
  local manifest="$1/steamapps/appmanifest_${PZ_STEAM_APP_ID}.acf"
  if [ -f "${manifest}" ]; then
    mv -f "${manifest}" "${manifest}.bak"
  fi
  echo "[zwarden] SteamCMD app state stuck (0x6); reset the app manifest and retrying" >&2
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

# pz_warm_steamcmd <steamcmd>
# #280: the freshly staged SteamCMD (the tmpfs above) self-updates and restarts on its first run,
# and the first app_update in that same run stalled - "Timed out waiting for update to start" after
# ~2 minutes - on every update observed live, costing a retry each time. A throwaway anonymous
# login/quit absorbs the self-update so the real app_update starts on a settled client. Best-effort:
# it never fails the caller (the install/update after it is fail-closed on its own). Output goes to
# stderr, before the update session banner, so the Agent never parses it as the update's result.
pz_warm_steamcmd() {
  local steamcmd="$1"
  echo "[zwarden] warming up SteamCMD (self-update + anonymous login) before app_update..." >&2
  "${steamcmd}" +login anonymous +quit >&2 2>&1 || true
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

# pz_run_update <steamcmd> <runscript> <session> [server_dir]
# Runs the same anonymous `app_update ... validate` as an install (via pz_install_with_retry,
# so the "Missing configuration" retry still applies), bracketed by a begin/end banner carrying
# the session id. The Agent keys its log-parse window on these banners; the end banner also
# states the stdout-decided outcome authoritatively. Returns the run's success/failure.
pz_run_update() {
  local steamcmd="$1" runscript="$2" session="$3" server_dir="${4:-}"
  echo "[zwarden] steamcmd update session ${session} begin"
  if pz_install_with_retry "${steamcmd}" "${runscript}" "${server_dir}"; then
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
  if pz_run_update "${steamcmd}" "${runscript}" "${session}" "${server_dir}"; then
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
# volume. The server downloads WorkshopItems= under its OWN install dir - the persistent
# /pz/server volume, "installed to /pz/server/steamapps/workshop/content/108600/<id>"
# (spikes #291/#293) - not the runtime/ Steam root a standalone SteamCMD would use. That
# folder is symlinked in as data/workshop so operators see one logical tree; the server
# creates it on its first download, so the link may dangle until then.
pz_create_layout() {
  local root="$1"
  mkdir -p "${root}/server" "${root}/runtime" "${root}/data"
  ln -sfn "${root}/server/steamapps/workshop/content/${PZ_STEAM_APPID_TXT}" "${root}/data/workshop"
}

# pz_share_workshop <server_dir>
# Gives the PZ game-server group (gid 10000, which the Agent runs in - #184) write on every
# folder of the Workshop tree, so the Agent can delete an unused download (#293). Removing an
# entry needs write on the folder holding it, so folders are enough; files are left as they
# are. The entrypoint's umask covers new downloads; this repairs folders PZ's Steam client
# made under the old 0022 umask. Runs as their owner (pzserver), so chmod is allowed; only
# folders still missing the bit are touched. A no-op before the first download.
pz_share_workshop() {
  local workshop="$1/steamapps/workshop"
  [ -d "${workshop}" ] || return 0
  find "${workshop}" -type d ! -perm -g=w -exec chmod g+w {} +
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
