#!/usr/bin/env bash
# Base container health check (Feature 12, mini-plan Q5): the Project Zomboid
# GameServer JVM is alive. Deliberately shallow - Feature 16 layers the hierarchical
# container/process/startup/network model on top. Exit 0 = healthy.
set -euo pipefail
pgrep -f 'zombie.network.GameServer' >/dev/null 2>&1
