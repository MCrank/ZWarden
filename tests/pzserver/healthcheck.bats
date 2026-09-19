#!/usr/bin/env bats
# S?: the base container health check matches the real dedicated-server process (#191).
# Build 42 runs a native ./ProjectZomboid64 launcher (JVM embedded), so the old
# 'zombie.network.GameServer' class name is absent from the process argv.

setup() {
  load 'test_helper'
  source "${SCRIPTS}/pz-lib.sh"
}

matches() { printf '%s' "$1" | grep -Eq "${PZ_SERVER_PROCESS_PATTERN}"; }

@test "health pattern matches the Build 42 native launcher process" {
  matches "./ProjectZomboid64 -cachedir=/pz/data -servername servertest -adminpassword s3cr3t"
}

@test "health pattern matches a direct java GameServer launch (legacy/B41 or hand run)" {
  matches "/pz/server/jre64/bin/java -Djava.class.path=/pz/server/java/. zombie.network.GameServer -statistic 0"
}

@test "health pattern does NOT match install-phase or wrapper processes" {
  ! matches "/usr/bin/tini -- /pz/scripts/entrypoint.sh"
  ! matches "bash /pz/scripts/entrypoint.sh"
  ! matches "bash /pz/server/start-server.sh -cachedir=/pz/data"
  ! matches "/pz/runtime/linux32/steamcmd +runscript /pz/runtime/tmp.abc"
}

@test "pz_server_running is defined for the healthcheck to call" {
  declare -F pz_server_running >/dev/null
}

@test "the healthcheck script delegates to the shared, tested pattern" {
  grep -q 'pz_server_running' "${SCRIPTS}/healthcheck.sh"
}
