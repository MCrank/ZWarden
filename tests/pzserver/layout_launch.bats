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
  # The server downloads Workshop items under its own install dir, the persistent /pz/server volume (spike #293).
  [ "$(readlink "$PZ_ROOT/data/workshop")" = "$PZ_ROOT/server/steamapps/workshop/content/108600" ]
}

@test "create_layout leaves the Workshop folder for the server to create" {
  pz_create_layout "$PZ_ROOT"
  # Nothing downloads to the ephemeral runtime/ Steam root, and the install volume is not pre-populated (#293).
  [ ! -e "$PZ_ROOT/runtime/steamapps" ]
  [ ! -e "$PZ_ROOT/server/steamapps" ]
}

@test "launch command runs the shipped launcher with cachedir and servername" {
  run pz_build_launch_cmd "$PZ_ROOT/server" "$PZ_ROOT/data"
  assert_success
  assert_output_contains "${PZ_LINUX_LAUNCHER}"
  assert_output_contains "-cachedir=$PZ_ROOT/data"
  assert_output_contains "-servername servertest"
  # Build 42 rejects -statistic ("unknown option"); it must no longer be emitted (#188).
  refute_output_contains "-statistic"
}

@test "launch command never carries the admin password (kept out of the logged line, #188)" {
  ZW_PZ_ADMIN_PASSWORD="s3cr3t-admin-pw"
  run pz_build_launch_cmd "$PZ_ROOT/server" "$PZ_ROOT/data"
  assert_success
  refute_output_contains "-adminpassword"
  refute_output_contains "s3cr3t-admin-pw"
}

@test "admin_password honours an operator-set ZW_PZ_ADMIN_PASSWORD" {
  ZW_PZ_ADMIN_PASSWORD="operator-chosen-pw"
  run pz_admin_password "$PZ_ROOT/data"
  assert_success
  [ "$output" = "operator-chosen-pw" ]
}

@test "admin_password generates a strong password, persists it, and reuses it" {
  ZW_PZ_ADMIN_PASSWORD=""
  # Call the function directly (not via `run`) so the persist-then-reuse across two calls does not
  # depend on how a given bats version scopes `run` — matching the working pattern below.
  local first second
  first="$(pz_admin_password "$PZ_ROOT/data")"
  [ "${#first}" -ge 20 ]
  [ -s "$PZ_ROOT/data/.zwarden-adminpw" ]
  # A second call returns the SAME persisted value (stable across restarts).
  second="$(pz_admin_password "$PZ_ROOT/data")"
  [ "$second" = "$first" ]
}

@test "a generated admin password file is group-readable but not world-readable (#377)" {
  ZW_PZ_ADMIN_PASSWORD=""
  pz_admin_password "$PZ_ROOT/data" >/dev/null
  run stat -c '%a' "$PZ_ROOT/data/.zwarden-adminpw"
  assert_success
  # umask 027 => 640: the Agent (group 10000) can read it to back it up; "other" cannot (#377).
  [ "$output" = "640" ]
}

@test "fix_admin_password_mode makes an existing 0600 file 0640 without changing it (#377)" {
  mkdir -p "$PZ_ROOT/data"
  ( umask 077; printf '%s' "old-image-pw" > "$PZ_ROOT/data/.zwarden-adminpw" )
  run pz_fix_admin_password_mode "$PZ_ROOT/data"
  assert_success
  [ "$(stat -c '%a' "$PZ_ROOT/data/.zwarden-adminpw")" = "640" ]
  [ "$(cat "$PZ_ROOT/data/.zwarden-adminpw")" = "old-image-pw" ]
}

@test "fix_admin_password_mode is a no-op when there is no password file (#377)" {
  mkdir -p "$PZ_ROOT/data"
  run pz_fix_admin_password_mode "$PZ_ROOT/data"
  assert_success
  [ ! -e "$PZ_ROOT/data/.zwarden-adminpw" ]
}

@test "fix_admin_password_mode never fails the start when the file cannot be changed (#377)" {
  # A restored world's file is owned by the Agent's uid, which pzserver cannot chmod; under the
  # entrypoint's `set -e` a failing chmod would stop the container from starting.
  mkdir -p "$PZ_ROOT/data"
  ( umask 077; printf '%s' "restored-pw" > "$PZ_ROOT/data/.zwarden-adminpw" )
  chmod() { return 1; }
  run pz_fix_admin_password_mode "$PZ_ROOT/data"
  assert_success
  [ "$(cat "$PZ_ROOT/data/.zwarden-adminpw")" = "restored-pw" ]
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

@test "share_workshop makes existing Workshop folders group-writable so the Agent can delete unused ones (#293)" {
  local ws="$PZ_ROOT/server/steamapps/workshop/content/108600"
  mkdir -p "$ws/111/mods/A"
  echo "id=A" > "$ws/111/mods/A/mod.info"
  chmod 755 "$ws" "$ws/111" "$ws/111/mods" "$ws/111/mods/A"
  chmod 644 "$ws/111/mods/A/mod.info"

  run pz_share_workshop "$PZ_ROOT/server"
  assert_success

  # Removing an entry needs write on the folder holding it, so every folder gets group-write; files are left alone.
  for d in "$ws" "$ws/111" "$ws/111/mods" "$ws/111/mods/A"; do
    [ "$(stat -c %A "$d" | cut -c6)" = "w" ]
  done
  [ "$(stat -c %a "$ws/111/mods/A/mod.info")" = "644" ]
}

@test "share_workshop is a no-op before the server has downloaded anything" {
  run pz_share_workshop "$PZ_ROOT/server"
  assert_success
  [ ! -e "$PZ_ROOT/server/steamapps" ]
}
