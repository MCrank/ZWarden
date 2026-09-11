#!/usr/bin/env bash
# Runs its arguments with NO network at all except loopback.
#
# Tries an unprivileged user+network namespace first (keeps $HOME and
# $NUGET_PACKAGES), falls back to sudo. ADR 0002 measured that the unprivileged
# `unshare -rn` path does NOT work on ubuntu-latest, so the sudo path is the one
# that actually runs in CI; the unprivileged attempt is kept for local use.
set -euo pipefail

inner=$(printf '%q ' "$@")

if unshare -rn true 2>/dev/null; then
  echo "::notice::network isolation via unprivileged user+net namespace"
  exec unshare -rn -- bash -c "ip link set lo up 2>/dev/null || true; ${inner}"
fi

echo "::notice::network isolation via sudo unshare"
exec sudo -E env "HOME=${HOME}" "PATH=${PATH}" \
  "NUGET_PACKAGES=${NUGET_PACKAGES:-}" "DOTNET_CLI_HOME=${HOME}" \
  "DOTNET_CLI_TELEMETRY_OPTOUT=1" "DOTNET_NOLOGO=1" \
  unshare -n -- bash -c "ip link set lo up 2>/dev/null || true; ${inner}"
