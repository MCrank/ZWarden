#!/usr/bin/env bash
# Proves the isolation the offline tier claims. A green offline test run means
# nothing unless egress is actually impossible, so assert that it is.
set -uo pipefail

fail=0

probe() {
  local what="$1"; shift
  if "$@" >/dev/null 2>&1; then
    echo "NOT ISOLATED: $what succeeded inside the sandbox"
    fail=1
  else
    echo "isolated: $what failed as required"
  fi
}

probe "DNS for api.nuget.org"        getent hosts api.nuget.org
probe "TCP to api.nuget.org:443"     curl -sS --max-time 8 https://api.nuget.org/v3/index.json
probe "TCP to 1.1.1.1:443 by IP"     curl -sS --max-time 8 https://1.1.1.1
probe "TCP to github.com:443"        curl -sS --max-time 8 https://github.com

if [ "$fail" -ne 0 ]; then
  echo "The offline tier is not offline."
  exit 1
fi
echo "All egress probes failed. The sandbox has no network beyond loopback."
