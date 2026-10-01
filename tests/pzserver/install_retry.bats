#!/usr/bin/env bats
# S2b: SteamCMD install retry (F12/#65). A FRESH SteamCMD's first app_update routinely dies
# with "Failed to install app '<id>' (Missing configuration)"; the installer must retry
# (warming the config) and only fail-closed after exhausting its attempts. No network here -
# SteamCMD is a stub whose per-call output is scripted.

setup() {
  load 'test_helper'
  PZ_ROOT="$(mktemp -d)"
  source "${SCRIPTS}/pz-lib.sh"

  STEAMCMD="${PZ_ROOT}/steamcmd"
  RUNSCRIPT="${PZ_ROOT}/runscript"
  COUNT="${PZ_ROOT}/calls"
  OUTCOMES="${PZ_ROOT}/outcomes"
  : > "${RUNSCRIPT}"
  export STUB_COUNT="${COUNT}" STUB_OUTCOMES="${OUTCOMES}"

  # SteamCMD stand-in: on its Nth call it maps the Nth line of $STUB_OUTCOMES to the real
  # stdout we parse. SteamCMD's exit codes are undocumented (ADR 0009), so it always exits 0 -
  # success is decided purely from stdout via pz_install_succeeded.
  cat > "${STEAMCMD}" <<'STUB'
#!/usr/bin/env bash
n=$(( $(cat "${STUB_COUNT}" 2>/dev/null || echo 0) + 1 ))
echo "${n}" > "${STUB_COUNT}"
case "$(sed -n "${n}p" "${STUB_OUTCOMES}")" in
  success) echo "Success! App '380870' fully installed" ;;
  missing) echo "ERROR! Failed to install app '380870' (Missing configuration)" ;;
  badbeta) echo "ERROR! Failed to set beta 'zw-nope'" ;;
  # #280: SteamCMD gives up on starting the job yet still prints the success line.
  stalled) printf '%s
' " Update state (0x0) : Timed out waiting for update to start, bailing." "Success! App '380870' fully installed." ;;
  # #288: a previous failed job left StateFlags/UpdateResult 6 in the app manifest; SteamCMD reads it and
  # aborts before downloading anything, every run, until the manifest is reset.
  stuck)   printf '%s
' " Update state (0x3) reconfiguring, progress: 0.00 (0 / 0)" "Error! App '380870' state is 0x6 after update job." ;;
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

@test "install succeeds on the first attempt without retrying" {
  printf 'success\n' > "${OUTCOMES}"
  run pz_install_with_retry "${STEAMCMD}" "${RUNSCRIPT}"
  assert_success
  [ "$(calls)" -eq 1 ]
}

@test "install retries past a 'Missing configuration' first attempt, then succeeds" {
  printf 'missing\nsuccess\n' > "${OUTCOMES}"
  ZW_PZ_INSTALL_ATTEMPTS=3
  run pz_install_with_retry "${STEAMCMD}" "${RUNSCRIPT}"
  assert_success
  [ "$(calls)" -eq 2 ]
}

@test "install is fail-closed after exhausting every attempt" {
  printf 'missing\nmissing\nmissing\n' > "${OUTCOMES}"
  ZW_PZ_INSTALL_ATTEMPTS=3
  run pz_install_with_retry "${STEAMCMD}" "${RUNSCRIPT}"
  assert_failure
  [ "$(calls)" -eq 3 ]
}

@test "attempt count is operator-tunable via ZW_PZ_INSTALL_ATTEMPTS" {
  printf 'missing\nmissing\n' > "${OUTCOMES}"
  ZW_PZ_INSTALL_ATTEMPTS=2
  run pz_install_with_retry "${STEAMCMD}" "${RUNSCRIPT}"
  assert_failure
  [ "$(calls)" -eq 2 ]
}

# #258: a branch Steam doesn't know (or a password-protected one) is not transient - SteamCMD
# prints "Failed to set beta" and downloads nothing. Stop at once with a clear reason.
@test "a nonexistent branch fails at once without retrying" {
  printf 'badbeta\nsuccess\n' > "${OUTCOMES}"
  ZW_PZ_INSTALL_ATTEMPTS=3
  ZW_PZ_BETA="zw-nope"
  run pz_install_with_retry "${STEAMCMD}" "${RUNSCRIPT}"
  [ "$status" -eq 2 ]
  [ "$(calls)" -eq 1 ]
  assert_output_contains "Steam branch 'zw-nope' does not exist or is password-protected; refusing to launch"
}

# #280: SteamCMD can wait ~2 min, print "Timed out waiting for update to start, bailing." and
# STILL print "Success! ... fully installed" - the job never ran, so on a server with a real
# update out we'd record success on the old build. Not a success; retry it.
@test "a 'timed out waiting for update to start' attempt is not a success and is retried" {
  printf 'stalled\nsuccess\n' > "${OUTCOMES}"
  ZW_PZ_INSTALL_ATTEMPTS=3
  run pz_install_with_retry "${STEAMCMD}" "${RUNSCRIPT}"
  assert_success
  [ "$(calls)" -eq 2 ]
}

@test "timing out on every attempt fails with a clear reason the Agent can surface" {
  printf 'stalled\nstalled\nstalled\n' > "${OUTCOMES}"
  ZW_PZ_INSTALL_ATTEMPTS=3
  run pz_install_with_retry "${STEAMCMD}" "${RUNSCRIPT}"
  assert_failure
  [ "$(calls)" -eq 3 ]
  # ERROR!-prefixed (no [zwarden] prefix) so SteamCmdLogParser takes it as the failure reason.
  assert_output_contains "ERROR! SteamCMD timed out waiting for the update to start on every attempt; the installed build is unchanged."
}

@test "the success check rejects output where the job never started" {
  run pz_install_succeeded <<< $' Update state (0x0) : Timed out waiting for update to start, bailing.\nSuccess! App \'380870\' fully installed.'
  assert_failure
}

# #288: SteamCMD's sticky "state is 0x6 after update job" - a failed job wrote StateFlags/UpdateResult 6 into
# steamapps/appmanifest_380870.acf, and every later run reads that and gives up without downloading. Retrying
# can never clear it; moving the manifest aside does (SteamCMD rebuilds it; `validate` re-checks the files).
manifest_setup() {
  SERVER_DIR="${PZ_ROOT}/server"
  MANIFEST="${SERVER_DIR}/steamapps/appmanifest_380870.acf"
  mkdir -p "${SERVER_DIR}/steamapps"
  printf '"AppState"\n{\n\t"StateFlags"\t\t"6"\n}\n' > "${MANIFEST}"
}

@test "a stuck 0x6 app state resets the app manifest once and the retry succeeds" {
  manifest_setup
  printf 'stuck\nsuccess\n' > "${OUTCOMES}"
  ZW_PZ_INSTALL_ATTEMPTS=3
  run pz_install_with_retry "${STEAMCMD}" "${RUNSCRIPT}" "${SERVER_DIR}"
  assert_success
  [ "$(calls)" -eq 2 ]
  [ ! -e "${MANIFEST}" ]
  [ -f "${MANIFEST}.bak" ]
  assert_output_contains "[zwarden] SteamCMD app state stuck (0x6); reset the app manifest and retrying"
}

@test "a 0x6 that survives the manifest reset fails with a clear reason after one reset only" {
  manifest_setup
  printf 'stuck\nstuck\nstuck\n' > "${OUTCOMES}"
  ZW_PZ_INSTALL_ATTEMPTS=3
  run pz_install_with_retry "${STEAMCMD}" "${RUNSCRIPT}" "${SERVER_DIR}"
  assert_failure
  [ "$(calls)" -eq 3 ]
  # One reset: SteamCMD would recreate the manifest; a second rename must not clobber the first backup.
  [ "$(grep -c 'reset the app manifest' <<< "${output}")" -eq 1 ]
  [ -f "${MANIFEST}.bak" ]
  assert_output_contains "ERROR! SteamCMD update state is stuck (0x6) even after resetting the app manifest; check free disk space on the server volume. The installed build is unchanged."
}

@test "an attempt without 0x6 never touches the app manifest" {
  manifest_setup
  printf 'missing\nsuccess\n' > "${OUTCOMES}"
  ZW_PZ_INSTALL_ATTEMPTS=3
  run pz_install_with_retry "${STEAMCMD}" "${RUNSCRIPT}" "${SERVER_DIR}"
  assert_success
  [ -f "${MANIFEST}" ]
  [ ! -e "${MANIFEST}.bak" ]
}

@test "a stuck 0x6 with no manifest on disk still retries and reports the stuck state" {
  SERVER_DIR="${PZ_ROOT}/server"
  mkdir -p "${SERVER_DIR}"
  printf 'stuck\nstuck\n' > "${OUTCOMES}"
  ZW_PZ_INSTALL_ATTEMPTS=2
  run pz_install_with_retry "${STEAMCMD}" "${RUNSCRIPT}" "${SERVER_DIR}"
  assert_failure
  [ "$(calls)" -eq 2 ]
  assert_output_contains "ERROR! SteamCMD update state is stuck (0x6)"
}
