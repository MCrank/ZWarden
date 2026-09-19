#!/usr/bin/env bats
# S4: stop orchestration - the FIFO save -> grace -> quit sequence (T7).
# Linux only (uses a real FIFO); runs in the offline CI tier and the bats container.

setup() {
  load 'test_helper'
  PZ_ROOT="$(mktemp -d)"
  source "${SCRIPTS}/pz-lib.sh"
}

teardown() {
  rm -rf "$PZ_ROOT"
}

@test "graceful stop writes save then quit to the FIFO, in that order" {
  local fifo="$PZ_ROOT/zomboid.control"
  local log="$PZ_ROOT/received.log"
  mkfifo "$fifo"
  : > "$log"

  # Fake JVM: hold the read end open, record each console command, exit on quit.
  ( exec 3< "$fifo"; while read -u 3 cmd; do echo "$cmd" >> "$log"; [ "$cmd" = "quit" ] && break; done ) &
  local reader=$!

  ZW_PZ_STOP_GRACE=0
  pz_graceful_stop "$fifo"
  wait "$reader"

  run cat "$log"
  assert_success
  [ "${lines[0]}" = "save" ]
  [ "${lines[1]}" = "quit" ]
}

@test "the SIGTERM handler exits with the JVM's clean status, not the interrupted-wait 143 (#200)" {
  # Mirror entrypoint.sh's structure exactly: a backgrounded fake JVM whose stdin is the FIFO, a TERM trap that
  # runs pz_graceful_stop then `wait; exit $?`, and the outer `wait` the trap must pre-empt. A clean save->quit
  # (JVM exits 0) must leave the SCRIPT exiting 0 — proving `docker stop` yields a Stopped, not Failed, container.
  local runner="$PZ_ROOT/runner.sh"
  cat > "$runner" <<RUNNER
#!/usr/bin/env bash
set -euo pipefail
source "${SCRIPTS}/pz-lib.sh"
FIFO="$PZ_ROOT/zomboid.control"
ZW_PZ_STOP_GRACE=0
mkfifo "\$FIFO"
exec 3<> "\$FIFO"
# Fake JVM: exits 0 the moment it reads 'quit', like PZ's clean shutdown.
( while read -r cmd; do [ "\$cmd" = "quit" ] && exit 0; done < "\$FIFO" ) &
SERVER_PID=\$!
term_handler() { pz_graceful_stop "\$FIFO"; wait "\$SERVER_PID"; exit \$?; }
trap term_handler TERM
# Deliver SIGTERM to ourselves after the trap is armed, then block on the outer wait like the entrypoint does.
( sleep 0.2; kill -TERM \$\$ ) &
wait "\$SERVER_PID"
RUNNER

  run bash "$runner"
  assert_success   # exit 0, i.e. NOT 143
}

@test "graceful stop waits exactly ZW_PZ_STOP_GRACE between save and quit" {
  # Deterministic (no wall clock): a regular file stands in for the FIFO so the
  # write does not block, and sleep is mocked to record the grace it was asked for.
  local out="$PZ_ROOT/out"
  local graced="$PZ_ROOT/graced"
  sleep() { echo "$1" > "$graced"; }

  ZW_PZ_STOP_GRACE=17
  pz_graceful_stop "$out"

  [ "$(cat "$graced")" = "17" ]
  run cat "$out"
  [ "${lines[0]}" = "save" ]
  [ "${lines[1]}" = "quit" ]
}
