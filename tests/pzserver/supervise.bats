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
