#!/usr/bin/env bats
# S3: canonical /pz filesystem + launch/JVM assembly (T4, T6).

setup() {
  load 'test_helper'
  PZ_ROOT="$(mktemp -d)"
  source "${SCRIPTS}/pz-lib.sh"
}

teardown() {
  rm -rf "$PZ_ROOT"
}

@test "create_layout builds the canonical /pz tree" {
  pz_create_layout "$PZ_ROOT"
  [ -d "$PZ_ROOT/server" ]
  [ -d "$PZ_ROOT/runtime" ]
  [ -d "$PZ_ROOT/data" ]
}

@test "create_layout symlinks the Workshop cache into data/workshop" {
  pz_create_layout "$PZ_ROOT"
  [ -L "$PZ_ROOT/data/workshop" ]
}

@test "launch command runs the shipped launcher with cachedir, servername, statistic" {
  run pz_build_launch_cmd "$PZ_ROOT/server" "$PZ_ROOT/data"
  assert_success
  assert_output_contains "${PZ_LINUX_LAUNCHER}"
  assert_output_contains "-cachedir=$PZ_ROOT/data"
  assert_output_contains "-servername servertest"
  assert_output_contains "-statistic 0"
}

@test "tune_jvm overrides the shipped 16g heap with the configured value" {
  cp "$FIXTURES/start-server.sh" "$PZ_ROOT/start-server.sh"
  ZW_PZ_XMS="4g"; ZW_PZ_XMX="4g"
  pz_tune_jvm "$PZ_ROOT/start-server.sh"
  grep -q -- "-Xms4g" "$PZ_ROOT/start-server.sh"
  grep -q -- "-Xmx4g" "$PZ_ROOT/start-server.sh"
  ! grep -qE -- "-Xm[sx]16g" "$PZ_ROOT/start-server.sh"
}

@test "tune_jvm ensures AlwaysPreTouch is present for ZGC" {
  cp "$FIXTURES/start-server.sh" "$PZ_ROOT/start-server.sh"
  pz_tune_jvm "$PZ_ROOT/start-server.sh"
  grep -q -- "-XX:+AlwaysPreTouch" "$PZ_ROOT/start-server.sh"
}
