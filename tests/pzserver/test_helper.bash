#!/usr/bin/env bash
# Shared setup for the ZWarden.PZServer bats suite (offline tier). No network,
# no real Project Zomboid files - only synthetic fixtures and stubs (ADR 0009).

REPO_ROOT="$(cd "${BATS_TEST_DIRNAME}/../.." && pwd)"
SCRIPTS="${REPO_ROOT}/src/ZWarden.PZServer/scripts"
FIXTURES="${BATS_TEST_DIRNAME}/fixtures"
STUBS="${BATS_TEST_DIRNAME}/stubs"

# Minimal assertions so the suite needs no bats-assert/bats-support dependency.
assert_success() {
  if [ "$status" -ne 0 ]; then
    echo "expected success (exit 0) but got exit $status; output: $output" >&2
    return 1
  fi
}

assert_failure() {
  if [ "$status" -eq 0 ]; then
    echo "expected failure (non-zero) but got exit 0; output: $output" >&2
    return 1
  fi
}

assert_output_contains() {
  case "$output" in
    *"$1"*) : ;;
    *) echo "expected output to contain: $1"$'\n'"actual: $output" >&2; return 1 ;;
  esac
}
