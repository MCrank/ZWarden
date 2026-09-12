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
