#!/usr/bin/env bats
# S1: first-run install detection must be idempotent (mini-plan T1).

setup() {
  load 'test_helper'
  PZ_ROOT="$(mktemp -d)"
  source "${SCRIPTS}/pz-lib.sh"
}

teardown() {
  rm -rf "$PZ_ROOT"
}

@test "needs install when the server dir is empty" {
  run pz_needs_install "$PZ_ROOT/server"
  assert_success   # exit 0 == install needed
}

@test "skips install when launcher and completion marker are both present" {
  mkdir -p "$PZ_ROOT/server"
  touch "$PZ_ROOT/server/start-server.sh"
  touch "$PZ_ROOT/server/.zwarden-installed"
  run pz_needs_install "$PZ_ROOT/server"
  assert_failure   # non-zero == skip
}

@test "needs install on a torn install: launcher present but marker missing" {
  mkdir -p "$PZ_ROOT/server"
  touch "$PZ_ROOT/server/start-server.sh"
  run pz_needs_install "$PZ_ROOT/server"
  assert_success
}
