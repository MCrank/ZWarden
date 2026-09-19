#!/usr/bin/env bash
# Base container health check (Feature 12, mini-plan Q5): the Project Zomboid
# dedicated-server process is alive. Deliberately shallow - Feature 16 layers the
# hierarchical container/process/startup/network model on top. Exit 0 = healthy.
#
# The process-match pattern lives in pz-lib.sh (pz_server_running) so it is unit-tested
# and cannot silently drift from how Build 42 actually launches the server (#191).
set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=pz-lib.sh
source "${DIR}/pz-lib.sh"
pz_server_running
