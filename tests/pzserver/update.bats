#!/usr/bin/env bats
# F17 PR-A: the SteamCMD update path. The Agent cannot exec into the container (ADR 0008
# denies exec/attach), so it drops a control-file into the writable /pz/data volume carrying
# the OperationId and restarts; the entrypoint runs `app_update ... validate` PAST the install
# marker, brackets the SteamCMD output with a `steamcmd update session <id> begin/end` banner
# the Agent parses from `docker logs`, and - unlike a first-run install - a FAILED update never
# bricks a working server (it clears the request and lets the server launch on the old install,
# no `exit 1`). Offline: SteamCMD is a stub whose per-call stdout is scripted (ADR 0009).

setup() {
  load 'test_helper'
  PZ_ROOT="$(mktemp -d)"
  DATA_DIR="${PZ_ROOT}/data"
  SERVER_DIR="${PZ_ROOT}/server"
  mkdir -p "${DATA_DIR}" "${SERVER_DIR}"
  source "${SCRIPTS}/pz-lib.sh"

  STEAMCMD="${PZ_ROOT}/steamcmd"
  RUNSCRIPT="${PZ_ROOT}/runscript"
  COUNT="${PZ_ROOT}/calls"
  OUTCOMES="${PZ_ROOT}/outcomes"
  : > "${RUNSCRIPT}"
  export STUB_COUNT="${COUNT}" STUB_OUTCOMES="${OUTCOMES}"

  # Same stub shape as install_retry.bats: maps the Nth call to the Nth outcome line and always
  # exits 0, so every assertion below proves success/failure is read from stdout, not the code.
  cat > "${STEAMCMD}" <<'STUB'
#!/usr/bin/env bash
n=$(( $(cat "${STUB_COUNT}" 2>/dev/null || echo 0) + 1 ))
echo "${n}" > "${STUB_COUNT}"
case "$(sed -n "${n}p" "${STUB_OUTCOMES}")" in
  success) echo "Success! App '380870' fully installed" ;;
  missing) echo "ERROR! Failed to install app '380870' (Missing configuration)" ;;
  *)       echo "unrelated chatter" ;;
esac
STUB
  chmod +x "${STEAMCMD}"

  ZW_PZ_INSTALL_RETRY_DELAY=0   # no real backoff under test
}

teardown() {
  rm -rf "$PZ_ROOT"
}

calls() { cat "${COUNT}" 2>/dev/null || echo 0; }

# --- the control-file contract (Agent <-> entrypoint over /pz/data) --------------

@test "an update is requested when the control-file is present in the data dir" {
  printf 'op-abc123\n' > "${DATA_DIR}/.zwarden-update-requested"
  run pz_update_requested "${DATA_DIR}"
  assert_success
}

@test "no update is requested when the control-file is absent" {
  run pz_update_requested "${DATA_DIR}"
  assert_failure
}

@test "the requested session id is read from the control-file (whitespace trimmed)" {
  printf '  op-abc123 \n' > "${DATA_DIR}/.zwarden-update-requested"
  run pz_read_update_session "${DATA_DIR}"
  assert_success
  [ "$output" = "op-abc123" ]
}

@test "clearing the update request removes the control-file" {
  printf 'op-abc123\n' > "${DATA_DIR}/.zwarden-update-requested"
  pz_clear_update_request "${DATA_DIR}"
  [ ! -e "${DATA_DIR}/.zwarden-update-requested" ]
}

# --- pz_run_update: the banner-bracketed, stdout-decided SteamCMD run ------------

@test "a successful update brackets SteamCMD output with begin/end(success) banners" {
  printf 'success\n' > "${OUTCOMES}"
  run pz_run_update "${STEAMCMD}" "${RUNSCRIPT}" "op-xyz"
  assert_success
  assert_output_contains "steamcmd update session op-xyz begin"
  assert_output_contains "steamcmd update session op-xyz end (success)"
}

@test "update success is decided from stdout, not the exit code" {
  printf 'missing\n' > "${OUTCOMES}"   # the stub still exits 0
  ZW_PZ_INSTALL_ATTEMPTS=1
  run pz_run_update "${STEAMCMD}" "${RUNSCRIPT}" "op-xyz"
  assert_failure
  assert_output_contains "steamcmd update session op-xyz end (failure)"
}

@test "a failed update retries like an install before reporting failure" {
  printf 'missing\nmissing\nmissing\n' > "${OUTCOMES}"
  ZW_PZ_INSTALL_ATTEMPTS=3
  run pz_run_update "${STEAMCMD}" "${RUNSCRIPT}" "op-xyz"
  assert_failure
  [ "$(calls)" -eq 3 ]
}

@test "an update recovers past a transient 'Missing configuration' first attempt" {
  printf 'missing\nsuccess\n' > "${OUTCOMES}"
  ZW_PZ_INSTALL_ATTEMPTS=3
  run pz_run_update "${STEAMCMD}" "${RUNSCRIPT}" "op-xyz"
  assert_success
  [ "$(calls)" -eq 2 ]
}

# --- pz_apply_update: run + persist-on-success + always-clear -------------------

@test "apply-update clears the control-file and marks the install on success" {
  printf 'success\n' > "${OUTCOMES}"
  printf 'op-1\n' > "${DATA_DIR}/.zwarden-update-requested"
  run pz_apply_update "${STEAMCMD}" "${SERVER_DIR}" "${DATA_DIR}"
  assert_success
  [ ! -e "${DATA_DIR}/.zwarden-update-requested" ]
  [ -f "${SERVER_DIR}/.zwarden-installed" ]
  [ "$(cat "${SERVER_DIR}/steam_appid.txt")" = "108600" ]
}

@test "apply-update clears the control-file on failure too (no restart loop, no brick)" {
  printf 'missing\n' > "${OUTCOMES}"
  ZW_PZ_INSTALL_ATTEMPTS=1
  printf 'op-1\n' > "${DATA_DIR}/.zwarden-update-requested"
  run pz_apply_update "${STEAMCMD}" "${SERVER_DIR}" "${DATA_DIR}"
  assert_failure
  [ ! -e "${DATA_DIR}/.zwarden-update-requested" ]
}
