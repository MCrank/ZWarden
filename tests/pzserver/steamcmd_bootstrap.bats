#!/usr/bin/env bats
# F17: SteamCMD is baked at /opt/steamcmd (outside any mount) and copied into the writable
# /pz/runtime on boot. Under an Agent-created container /pz/runtime is an ephemeral tmpfs
# (ReadonlyRootfs=true, ADR 0008) and SteamCMD self-updates into its OWN directory, so it must
# live on writable storage - a mount at /pz/runtime would otherwise shadow the baked binary. The
# copy is idempotent so a by-hand run with a persistent runtime keeps its self-updated client.

setup() {
  load 'test_helper'
  PZ_ROOT="$(mktemp -d)"
  source "${SCRIPTS}/pz-lib.sh"
  BAKED="${PZ_ROOT}/opt-steamcmd"
  RUNTIME="${PZ_ROOT}/runtime"
  mkdir -p "${BAKED}"
  printf '#!/usr/bin/env bash\necho baked\n' > "${BAKED}/steamcmd.sh"
  chmod +x "${BAKED}/steamcmd.sh"
  printf 'lib\n' > "${BAKED}/libstdc++.so.6"
}

teardown() { rm -rf "$PZ_ROOT"; }

@test "bootstrap copies the baked SteamCMD into an empty runtime dir" {
  pz_bootstrap_steamcmd "${BAKED}" "${RUNTIME}"
  [ -x "${RUNTIME}/steamcmd.sh" ]
  [ -f "${RUNTIME}/libstdc++.so.6" ]
}

@test "bootstrap is idempotent: it does not overwrite a self-updated runtime copy" {
  mkdir -p "${RUNTIME}"
  printf '#!/usr/bin/env bash\necho self-updated\n' > "${RUNTIME}/steamcmd.sh"
  chmod +x "${RUNTIME}/steamcmd.sh"
  pz_bootstrap_steamcmd "${BAKED}" "${RUNTIME}"
  run "${RUNTIME}/steamcmd.sh"
  assert_output_contains "self-updated"
}

# #280: a freshly staged SteamCMD self-updates and restarts on its first run, and the first
# app_update in that same run stalled ("Timed out waiting for update to start") on every update
# observed live - 2 minutes lost before a retry. A throwaway login/quit run absorbs the
# self-update so the real app_update starts on a settled client.
@test "warm-up runs SteamCMD once with an anonymous login and quit" {
  printf '#!/usr/bin/env bash\necho "$*" >> "%s/args"\n' "${PZ_ROOT}" > "${PZ_ROOT}/steamcmd"
  chmod +x "${PZ_ROOT}/steamcmd"
  run pz_warm_steamcmd "${PZ_ROOT}/steamcmd"
  assert_success
  [ "$(wc -l < "${PZ_ROOT}/args")" -eq 1 ]
  [ "$(cat "${PZ_ROOT}/args")" = "+login anonymous +quit" ]
}

@test "a failed warm-up never blocks the install or update that follows" {
  printf '#!/usr/bin/env bash\necho "ERROR! no network"\nexit 7\n' > "${PZ_ROOT}/steamcmd"
  chmod +x "${PZ_ROOT}/steamcmd"
  run pz_warm_steamcmd "${PZ_ROOT}/steamcmd"
  assert_success
  assert_output_contains "warming up SteamCMD"
}
