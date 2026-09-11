#!/usr/bin/env bats
# S2: SteamCMD install path - runscript, stdout-parsed result, appid (T2, T3, T5).

setup() {
  load 'test_helper'
  PZ_ROOT="$(mktemp -d)"
  source "${SCRIPTS}/pz-lib.sh"
}

teardown() {
  rm -rf "$PZ_ROOT"
}

@test "runscript performs an anonymous, validated install into the server dir" {
  run pz_build_steamcmd_runscript "/pz/server"
  assert_success
  assert_output_contains "@NoPromptForPassword 1"
  assert_output_contains "force_install_dir /pz/server"
  assert_output_contains "login anonymous"
  assert_output_contains "app_update 380870"
  assert_output_contains "validate"
}

@test "runscript omits -beta when no branch is set (public)" {
  ZW_PZ_BETA=""
  run pz_build_steamcmd_runscript "/pz/server"
  assert_success
  [[ "$output" != *"-beta"* ]]
}

@test "runscript selects the branch when ZW_PZ_BETA is set" {
  ZW_PZ_BETA="legacy41"
  run pz_build_steamcmd_runscript "/pz/server"
  assert_success
  assert_output_contains "-beta legacy41"
}

@test "install success is read from the stdout success line, not the exit code" {
  printf "Update state (0x61)...\nSuccess! App '380870' fully installed\n" | pz_install_succeeded
}

@test "install failure is fail-closed: an error line is a failure" {
  ! printf "Error! App '380870' state is 0x202 after update job.\n" | pz_install_succeeded
}

@test "ambiguous output (no explicit success) is treated as failure" {
  ! printf "some unrelated chatter\n" | pz_install_succeeded
}

@test "steam_appid.txt is written containing exactly 108600" {
  mkdir -p "$PZ_ROOT/server"
  pz_write_appid "$PZ_ROOT/server"
  [ "$(cat "$PZ_ROOT/server/steam_appid.txt")" = "108600" ]
  [ "$(wc -l < "$PZ_ROOT/server/steam_appid.txt")" -eq 1 ]
}
